#!/usr/bin/env bash
# The `check` check: check-openapi.yml's `check` job. The committed OpenAPI document must be what the build
# generates. yggdrasil.yml runs it in the pinned .NET SDK image from the repository's root.
set -euo pipefail

# The repository's root, wherever this is run from.
cd "$(dirname "${BASH_SOURCE[0]}")/.."

# shellcheck source=ci/python.sh
. ci/python.sh

export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

ensure_python3

dotnet tool restore
python3 scripts/openapi.py --check
