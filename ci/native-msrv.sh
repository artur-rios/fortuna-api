#!/usr/bin/env bash
# The `native-msrv` check: native-core.yml's `msrv (rust-version)` job. The native core's tests must pass on the
# minimum supported Rust version it declares. yggdrasil.yml runs it in the pinned Rust image of that version, from
# the repository's root, and only when something the native core is built from changes (its `paths`).
#
# The workflow read rust-version from Cargo.toml and installed that toolchain. The toolchain is the image now, so
# this reads it and fails when the image is not that version: raising rust-version means changing the image in
# yggdrasil.yml (rust:<rust-version>-bookworm) in the same change.
set -euo pipefail

# The repository's root, wherever this is run from.
cd "$(dirname "${BASH_SOURCE[0]}")/.."

manifest=native/fortuna-core/Cargo.toml

msrv=$(sed -n 's/^rust-version = "\(.*\)"/\1/p' "$manifest")
if [ -z "$msrv" ]; then
  echo "ci/native-msrv.sh: no rust-version in $manifest" >&2
  exit 1
fi

installed=$(rustc --version)
case $installed in
  "rustc $msrv."* | "rustc $msrv "*) ;;
  *)
    echo "ci/native-msrv.sh: $manifest declares rust-version $msrv but the image has '$installed'; change the image of native-msrv in yggdrasil.yml to rust:$msrv-bookworm" >&2
    exit 1
    ;;
esac

# Test on the declared minimum supported Rust version
cargo test --manifest-path "$manifest" --locked
