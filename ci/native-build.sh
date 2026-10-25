#!/usr/bin/env bash
# The `native-build` check: the Linux leg of native-core.yml's `build` job. Format, lint, test, build the native
# library, then check the generated header and compile the C and C++ consumers against it. yggdrasil.yml runs it
# in the pinned stable Rust image, from the repository's root, and only when something the native core is built
# from changes (its `paths`). The Windows leg stays a GitHub Actions job (native-core.yml), unrequired.
#
# The workflow's rust-toolchain action installed clippy and rustfmt with the toolchain; the image carries only the
# minimal profile, so they are added here.
#
# The workflow also uploaded the library and header as an artifact (`fortuna-native-linux-x64`); they are left in
# native/fortuna-core/target/release and native/fortuna-core/include, and nothing consumes an archive of them yet.
set -euo pipefail

# The repository's root, wherever this is run from.
cd "$(dirname "${BASH_SOURCE[0]}")/.."

manifest=native/fortuna-core/Cargo.toml

rustup component add clippy rustfmt

# Check formatting
cargo fmt --manifest-path "$manifest" -- --check

# Lint native core
cargo clippy --manifest-path "$manifest" --all-targets --locked -- -D warnings

# Test native boundary
cargo test --manifest-path "$manifest" --locked

# Build native library
cargo build --manifest-path "$manifest" --release --locked

# Check generated header. build.rs regenerates it; the committed one must be the same. (safe.directory: the
# checkout may belong to another user than the one running the check.)
git -c safe.directory="$PWD" diff --exit-code -- native/fortuna-core/include/fortuna_core.h

# Compile C and C++ header consumers
smoke=$(mktemp -d)
trap 'rm -rf "$smoke"' EXIT
release=native/fortuna-core/target/release
cc native/fortuna-core/tests/header_smoke.c -L "$release" -lfortuna_core -o "$smoke/fortuna-header-c"
c++ -x c++ native/fortuna-core/tests/header_smoke.c -L "$release" -lfortuna_core -o "$smoke/fortuna-header-cpp"
LD_LIBRARY_PATH="$release" "$smoke/fortuna-header-c"
LD_LIBRARY_PATH="$release" "$smoke/fortuna-header-cpp"
