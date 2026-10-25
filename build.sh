#!/usr/bin/env bash
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ $# -gt 1 || ($# -eq 1 && "$1" != "--native-only") ]]; then
  echo "用法：bash build.sh [--native-only]" >&2
  exit 2
fi
mkdir -p "$project_dir/artifacts" "$project_dir/dist"
wine_headers=""
wine_libraries=""
for candidate in /usr/include/wine /usr/include/wine/wine; do
  if [[ -f "$candidate/windows/windows.h" ]]; then wine_headers="$candidate"; break; fi
done
for candidate in /usr/lib/wine/x86_64-windows /usr/lib/x86_64-linux-gnu/wine/x86_64-windows; do
  if [[ -f "$candidate/libkernel32.a" ]]; then wine_libraries="$candidate"; break; fi
done
if [[ -z "$wine_headers" || -z "$wine_libraries" ]]; then
  echo "需要 Wine 开发头文件与 x86_64 Windows 导入库（Ubuntu: libwine-dev）。" >&2
  exit 1
fi
clang --target=x86_64-pc-windows-msvc -O2 -Wall -Wextra -Werror \
  -fno-stack-protector -fno-builtin \
  -isystem "$wine_headers/msvcrt" -isystem "$wine_headers/windows" \
  -c "$project_dir/native/touch-bridge.c" -o "$project_dir/artifacts/touch-bridge.obj"
lld-link /entry:mainCRTStartup /subsystem:windows /nodefaultlib /Brepro \
  /out:"$project_dir/dist/touch-bridge.exe" "$project_dir/artifacts/touch-bridge.obj" \
  "$wine_libraries/libkernel32.a" "$wine_libraries/libuser32.a" \
  "$wine_libraries/libshell32.a" "$wine_libraries/libole32.a" "$wine_libraries/libbcrypt.a"
install -m 644 "$project_dir/dist/touch-bridge.exe" "$project_dir/assets/touch-bridge.exe"
if [[ "${1:-}" == "--native-only" ]]; then exit 0; fi
python3 "$project_dir/tools/publish-gui.py" linux-x64
install -m 755 "$project_dir/artifacts/publish/linux-x64/wuwa-touch-launcher" "$project_dir/dist/wuwa-touch-launcher"
