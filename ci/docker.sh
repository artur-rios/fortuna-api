#!/bin/sh
# The `docker` check: tests.yml's `docker` job. The Dockerfile must build; nothing is pushed. yggdrasil.yml runs it
# in the pinned Docker CLI image (Alpine: no bash, so this is POSIX sh) with the CI engine's socket, from the
# repository's root. The GitHub job's layer cache (type=gha) has no equivalent here; the engine's own cache does.
set -eu

# The repository's root, wherever this is run from.
cd "$(dirname "$0")/.."

tag="fortuna-api-check:$$"
# The image is only built to prove the Dockerfile works: remove it afterwards, whatever the outcome.
trap 'docker image rm --force "$tag" >/dev/null 2>&1 || true' EXIT

DOCKER_BUILDKIT=1 docker build --tag "$tag" .
