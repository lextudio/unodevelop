#!/usr/bin/env bash
# Build a universal macOS .dmg for local testing.
# Ported from OpenDevelop's dist.macos.sh; adjusted for UnoDevelop's TFM (net10.0-desktop,
# not net10.0-windows) and app name, and drops the ICSharpCode.Core.Presentation/AvalonDock
# WPF-only workarounds that don't apply to this Uno/WinUI-Skia port.
# Usage: ./dist.macos.sh [--skip-publish] [--debug]
#   --skip-publish  reuse existing publish output (faster iteration on bundle/dmg)
#   --debug         package the Debug configuration instead of Release.

set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"
host_dir="$script_dir/src/Main/SharpDevelop/SharpDevelop.csproj"

dotnet_candidates=(
  "/usr/local/share/dotnet/dotnet"
  "/opt/homebrew/bin/dotnet"
)
dotnet=""
for c in "${dotnet_candidates[@]}"; do
  if [[ -x "${c}" ]]; then
    dotnet="${c}"
    break
  fi
done
if [[ -z "${dotnet}" ]]; then
  dotnet="$(command -v dotnet 2>/dev/null || true)"
fi
if [[ -z "${dotnet}" || ! -x "${dotnet}" ]]; then
  echo "dist.macos.sh: cannot find dotnet (checked ${dotnet_candidates[*]} and PATH)" >&2
  exit 1
fi
dotnet="$(readlink -f "${dotnet}")"

skip_publish=0
config="Release"
for arg in "$@"; do
  [[ "$arg" == "--skip-publish" ]] && skip_publish=1
  [[ "$arg" == "--debug" ]] && config="Debug"
done

# The AppHost (native executable entry point) and managed assembly are cached under
# obj/<config>/net10.0-desktop/ (shared across RIDs). If this TFM lacks a RID-specific host
# pack on macOS (as net10.0-windows did for OpenDevelop), a second publish would reuse the
# first RID's AppHost/PE machine type. Clean between RIDs so each publish is forced to
# regenerate for the correct architecture - harmless if net10.0-desktop turns out to already
# be RID-specific here, just an unnecessary rebuild.
host_obj="${script_dir}/src/Main/SharpDevelop/obj/${config}"

publish_for_rid() {
  local rid="$1"
  echo "==> Cleaning intermediate outputs for ${rid}…"
  rm -rf "$host_obj/net10.0-desktop"
  echo "==> Publishing ${rid} (${config})…"
  "${dotnet}" publish "$host_dir" -r "${rid}" -c "${config}"
}

if [[ "$skip_publish" -eq 0 ]]; then
  publish_for_rid osx-arm64
  publish_for_rid osx-x64
else
  echo "==> Skipping publish (--skip-publish)"
fi

echo "==> Building .app bundle (universal, ${config})…"
DIST_CONFIG="${config}" "$script_dir/build/macos/build-application-bundle.sh" osx-universal

echo "==> Building .dmg…"
"$script_dir/build/macos/build-dmg.sh" UnoDevelop.app UnoDevelop-macos-universal.dmg

echo ""
echo "Done: $(pwd)/UnoDevelop-macos-universal.dmg"
