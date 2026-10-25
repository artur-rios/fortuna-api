# Sourced by the checks that run the repository's Python helpers (scripts/*.py) in the .NET SDK image, which
# ships no Python: the GitHub runner had it, so the workflows never installed it.

# Makes sure python3 is on the PATH. The pipeline runs a check as root, where apt can install it. As another user
# (a local run) it cannot, so the script says so instead of failing later on a missing command.
ensure_python3() {
  if command -v python3 >/dev/null 2>&1; then
    return 0
  fi

  if [ "$(id -u)" -ne 0 ]; then
    echo "ci: python3 is not installed and this is not root, so it cannot be installed here" >&2
    return 1
  fi

  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq
  apt-get install -y -qq --no-install-recommends python3
}
