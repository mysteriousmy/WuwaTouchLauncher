#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ $# -gt 1 || ($# -eq 1 && "$1" != "--gui-experiment") ]]; then
  echo "用法：bash build-linux-static.sh [--gui-experiment]" >&2
  exit 2
fi
enable_gui=false
output_dir="$project_dir/dist/linux-static-cli"
if [[ "${1:-}" == "--gui-experiment" ]]; then
  enable_gui=true
  output_dir="$project_dir/artifacts/static-gui-experiment"
fi
cache_dir="${XDG_CACHE_HOME:-$HOME/.cache}/wuwa-static-nuget"
mkdir -p "$output_dir" "$cache_dir"
if [[ ! -f "$project_dir/assets/touch-bridge.exe" || "$project_dir/native/touch-bridge.c" -nt "$project_dir/assets/touch-bridge.exe" ||
      "$project_dir/native/memory-graphics.h" -nt "$project_dir/assets/touch-bridge.exe" ||
      "$project_dir/native/memory-graphics-layout.h" -nt "$project_dir/assets/touch-bridge.exe" ]]; then
  bash "$project_dir/build.sh" --native-only
fi
docker build -f "$project_dir/build/linux-static.Dockerfile" \
  --build-arg "BUILD_UID=$(id -u)" --build-arg "BUILD_GID=$(id -g)" \
  -t wuwa-static-build:10.0.402 "$project_dir/build"
docker run --rm --user "$(id -u):$(id -g)" \
  -e DOTNET_CLI_HOME=/tmp/wuwa-dotnet -e NUGET_PACKAGES=/cache \
  -e "WUWA_BUILD_GUI=$enable_gui" \
  -v "$cache_dir:/cache" -v "$project_dir:/input:ro" -v "$output_dir:/output" \
  wuwa-static-build:10.0.402 sh -eu -c '
    mkdir -p /tmp/wuwa-build/src
    cp /input/src/*.cs /input/src/*.csproj /tmp/wuwa-build/src/
    cp -r /input/assets /tmp/wuwa-build/
    dotnet publish /tmp/wuwa-build/src/WuwaTouchLauncher.csproj \
      -c Release -r linux-musl-x64 -p:StaticExecutable=true \
      -p:EnableGui="$WUWA_BUILD_GUI" -o /output
  '
if [[ "$enable_gui" == false ]]; then
  install -m 755 "$project_dir/dist/touch-bridge.exe" "$output_dir/touch-bridge.exe"
  "$output_dir/wuwa-touch-cli" --help >/dev/null
  if readelf -l "$output_dir/wuwa-touch-cli" | grep -q INTERP ||
     readelf -d "$output_dir/wuwa-touch-cli" | grep -q NEEDED; then
    echo "构建结果仍依赖动态加载器或共享库。" >&2
    exit 1
  fi
  echo "静态命令行版：$output_dir/wuwa-touch-cli"
fi
