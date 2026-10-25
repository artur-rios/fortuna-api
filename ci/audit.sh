#!/usr/bin/env bash
# The `audit` check: audit.yml's `audit` job. It checks the native core's dependencies against the RustSec
# advisory database with `cargo audit`, as rustsec/audit-check did there. It runs on every pull request, with no
# path filter, and a new advisory fails it without any change in the repository, which is the point.
# yggdrasil.yml runs it in the pinned Rust image from the repository's root.
#
# The action also posted its findings as a GitHub check run (the GITHUB_TOKEN); Jenkins reports the check itself,
# so nothing here needs a token. `cargo audit` exits 1 when it finds a vulnerability (or an advisory it denies),
# and so does this script.
set -euo pipefail

# The repository's root, wherever this is run from.
cd "$(dirname "${BASH_SOURCE[0]}")/.."

# renovate: datasource=crate depName=cargo-audit
CARGO_AUDIT_VERSION=0.22.2

# cargo-audit goes in a directory of its own, not the user's cargo home: the check runs as whoever the CI engine
# runs it as, with no promise about a writable home.
tools=$(mktemp -d)
trap 'rm -rf "$tools"' EXIT
cargo install cargo-audit --locked --version "$CARGO_AUDIT_VERSION" --root "$tools"

cd native/fortuna-core
"$tools/bin/cargo-audit" audit
