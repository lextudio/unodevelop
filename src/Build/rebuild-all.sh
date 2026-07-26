#!/usr/bin/env bash
#
# rebuild-all.sh — single entry point for building (and optionally running) UnoDevelop.
#
# Mirrors OpenDevelop's rebuild-all.sh in spirit (one command instead of remembering steps),
# but UnoDevelop has no separate native-runtime repack step to chain — it's a single
# `dotnet build` against src/UnoDevelop.slnx followed by an optional run of the SharpDevelop
# project (AssemblyName: UnoDevelop, TargetFramework: net10.0-desktop).
#
# Usage:
#   ./rebuild-all.sh                build (Debug) and run
#   ./rebuild-all.sh --build-only   build only, do not launch
#   ./rebuild-all.sh --release      build in Release configuration
#
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
slnx="${repo_root}/src/UnoDevelop.slnx"
app_project="${repo_root}/src/Main/SharpDevelop/SharpDevelop.csproj"

configuration="Debug"
do_run=1

for arg in "$@"; do
  case "${arg}" in
    --build-only|--no-run) do_run=0 ;;
    --release)             configuration="Release" ;;
    *) echo "rebuild-all.sh: unknown flag '${arg}'" >&2; exit 2 ;;
  esac
done

echo "==> Building UnoDevelop (${configuration})..."
dotnet build "${slnx}" -c "${configuration}"

if [[ "${do_run}" -eq 1 ]]; then
  echo "==> Launching UnoDevelop..."
  exec dotnet run --project "${app_project}" -c "${configuration}" --no-build
else
  echo "==> Build complete (--build-only; no launch)."
fi
