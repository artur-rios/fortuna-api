#!/usr/bin/env bash
# The `test` check: the steps of tests.yml's `test` job, in the same order. yggdrasil.yml runs it in the pinned
# .NET SDK image from the repository's root, with the Docker engine (the functional tests use Testcontainers).
#
# REPORTGENERATOR_LICENSE, when set, is ReportGenerator's PRO license (yggdrasil.yml binds the Jenkins credential
# reportgenerator-license to it); scripts/coverage.py reads it and never prints it. Without it the free report
# is made.
set -euo pipefail

# The repository's root, wherever this is run from.
cd "$(dirname "${BASH_SOURCE[0]}")/.."

# shellcheck source=ci/python.sh
. ci/python.sh

export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

SOLUTION=src/ArturRios.Fortuna.sln

ensure_python3

# Test Python helpers
python3 -m unittest discover -s scripts -p "test_*.py" --verbose

# Check blank lines before return, break and yield
python3 scripts/check_blank_lines.py

# Restore tools, then dependencies
dotnet tool restore
dotnet restore "$SOLUTION"

# Scan dependencies
python3 scripts/vulnerabilities.py

# Build
dotnet build "$SOLUTION" --configuration Release --no-restore -m:1

# Check every test has a category. The run below takes every test, but the documented way to run one suite
# (`--filter "Category=Unit"` or `"Category=Functional"`) selects by Category trait, so a test without one would be
# missed by both. List the tests outside both categories and fail on any.
uncategorized=$(dotnet test "$SOLUTION" --configuration Release --no-build -m:1 --list-tests \
  --filter "Category!=Unit&Category!=Functional" | sed -n 's/^    //p')
if [ -n "$uncategorized" ]; then
  echo "ci/test.sh: these tests have no Category trait (Unit or Functional), so neither category filter runs them:" >&2
  echo "$uncategorized" >&2
  exit 1
fi

# Every test once, unit and functional alike, each test project in a process of its own and all of them side by
# side. (The unit tests and then the functional tests used to run as two passes over the solution, one project after
# another, starting every test process twice.) The functional tests start PostgreSQL through Testcontainers: the CI
# engine's socket (`docker: true`). Each run writes its .trx results and a coverage.cobertura.xml under its project's
# TestResults/, which scripts/coverage.py merges below.
#
# WebApi.Tests runs as three processes. Its tests configure the API through process-wide environment variables, so one
# process runs its classes one at a time; processes of their own (each with its own PostgreSQL container) run
# together. The shards split the classes by the first letter of their name, and the last one is everything the others
# are not, so every test runs exactly once and a new class always lands in one. Three, not more: a check gets about
# 5 GB of the CI agent, and three WebApi processes with their databases, beside the other projects, stay within it.
# Each shard runs from a copy of the build output of its own, since the coverage collector instruments the assemblies
# in place and restores them afterwards.
WEBAPI_TESTS=ArturRios.Fortuna.WebApi.Tests
first_letters() { # <letters>: a filter that matches the WebApi test classes whose names start with one of them
  local letter filter=""
  for letter in $(echo "$1" | grep -o .); do
    filter+="${filter:+|}FullyQualifiedName~$WEBAPI_TESTS.$letter"
  done
  echo "$filter"
}
WEBAPI_SHARDS=(
  "$(first_letters ABC)"
  "$(first_letters DEFGHI)"
  "$(first_letters ABCDEFGHI | sed 's/|/\&/g; s/~/!~/g')"
)
RUN_LOGS=$(mktemp -d)
SHARD_COPIES=()
trap 'rm -rf "$RUN_LOGS" "${SHARD_COPIES[@]}"' EXIT
run_tests() { # <project> <run name> [dotnet test arguments...]
  local project=$1 name=$2
  shift 2
  dotnet test "$project" --configuration Release --no-build \
    --logger "trx;LogFilePrefix=$name" \
    --results-directory "$(dirname "$project")/TestResults" \
    --collect:"XPlat Code Coverage" "$@" >"$RUN_LOGS/$name.log" 2>&1
}
runs=()
for project in $(find tests -name '*.Tests.csproj' | sort); do
  name=$(basename "$project" .csproj)
  if [ "$name" = "$WEBAPI_TESTS" ]; then
    output=$(dirname "$project")/bin/Release/net10.0
    for shard in "${!WEBAPI_SHARDS[@]}"; do
      # Inside the project's own bin/ (ignored by git), not a temporary directory: some tests find the repository
      # by walking up from their assembly's directory. OutputPath must be absolute, or MSBuild reads it relative to
      # the project.
      copy=$PWD/$(dirname "$project")/bin/shard-$((shard + 1))
      rm -rf "$copy"
      cp -R "$output" "$copy"
      SHARD_COPIES+=("$copy")
      # The project still names the run, so the coverage collector is found; OutputPath points it at the copy.
      run_tests "$project" "$name.$((shard + 1))" -p:OutputPath="$copy/" -p:AppendTargetFrameworkToOutputPath=false \
        --filter "${WEBAPI_SHARDS[$shard]}" &
      runs+=("$!:$name.$((shard + 1))")
    done
  else
    run_tests "$project" "$name" &
    runs+=("$!:$name")
  fi
done
failed=0
for run in "${runs[@]}"; do
  if wait "${run%%:*}"; then status=passed; else status=FAILED; failed=1; fi
  echo "== ${run#*:}: $status"
  cat "$RUN_LOGS/${run#*:}.log"
done
[ "$failed" -eq 0 ]

# Enforce coverage. The report (docs/coverage-report, archived by the check) is written even when the threshold
# fails, since it is how the gap is found.
python3 scripts/coverage.py --report-only
