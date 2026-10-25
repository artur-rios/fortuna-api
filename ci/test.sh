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

# Check every test has a category. The two test steps below select by Category trait, so a test without one
# would run in neither. List the tests outside both categories and fail on any.
uncategorized=$(dotnet test "$SOLUTION" --configuration Release --no-build -m:1 --list-tests \
  --filter "Category!=Unit&Category!=Functional" | sed -n 's/^    //p')
if [ -n "$uncategorized" ]; then
  echo "ci/test.sh: these tests have no Category trait (Unit or Functional), so no test step runs them:" >&2
  echo "$uncategorized" >&2
  exit 1
fi

# Unit tests, then functional tests
dotnet test "$SOLUTION" --configuration Release --no-build -m:1 --filter "Category=Unit" --collect:"XPlat Code Coverage"
dotnet test "$SOLUTION" --configuration Release --no-build -m:1 --filter "Category=Functional" --collect:"XPlat Code Coverage"

# Enforce coverage. The report (docs/coverage-report, archived by the check) is written even when the threshold
# fails, since it is how the gap is found.
python3 scripts/coverage.py --report-only
