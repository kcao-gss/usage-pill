#!/usr/bin/env bash
# Runs the Windows .NET SDK against this repository from WSL.
set -euo pipefail

DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SLN_WIN="$(wslpath -w "$REPO/UsagePill.sln")"
APP_WIN="$(wslpath -w "$REPO/src/UsagePill/UsagePill.csproj")"
TEST_WIN="$(wslpath -w "$REPO/tests/UsagePill.Tests/UsagePill.Tests.csproj")"

# MSBuild refuses a UNC working directory, so run from a real Windows path.
cd /mnt/c/Windows/Temp

cmd="${1:-build}"
shift || true

case "$cmd" in
  build)   "$DOTNET" build   "$SLN_WIN"  --nologo "$@" ;;
  test)    "$DOTNET" test    "$TEST_WIN" --nologo "$@" ;;
  run)     "$DOTNET" run     --project "$APP_WIN" "$@" ;;
  publish) "$DOTNET" publish "$APP_WIN" -c Release -r win-x64 --self-contained false "$@" ;;
  *) echo "usage: build.sh [build|test|run|publish] [extra dotnet args]" >&2; exit 2 ;;
esac
