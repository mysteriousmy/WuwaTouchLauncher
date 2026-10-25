#!/usr/bin/env bash
# Validated Linux -> Windows x64 single-file NativeAOT + static graphics release.
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
bash "$project_dir/build.sh" --native-only
python3 "$project_dir/tools/publish-gui.py" win-x64
mkdir -p "$project_dir/dist/win-x64"
install -m 755 "$project_dir/artifacts/publish/win-x64/wuwa-touch-launcher.exe" "$project_dir/dist/win-x64/wuwa-touch-launcher.exe"
python3 "$project_dir/tools/package-release.py" win-x64
