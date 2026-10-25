#!/usr/bin/env bash
# Focused integration test of native/touch-bridge.c. Uses a separate Wine prefix
# and X display; never sends synthetic input to the user's desktop.
set -euo pipefail
project_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
test_wine="${WUWA_TEST_WINE:-$(command -v wine || true)}"
if [[ -z "$test_wine" ]]; then echo "请安装 Wine，或用 WUWA_TEST_WINE 指定 wine 程序。" >&2; exit 1; fi
test_prefix="$project_dir/artifacts/test-prefix"
test_server="$(dirname -- "$test_wine")/wineserver"
if [[ -x "$test_server" ]]; then
  trap 'env WINEPREFIX="$test_prefix" "$test_server" -k >/dev/null 2>&1 || true' EXIT
fi
result="$project_dir/artifacts/wine-probe.json"
mkdir -p "$test_prefix"
rm -f "$result"
windows_result="Z:${result//\//\\}"
xvfb-run -a env WAYLAND_DISPLAY= WINEPREFIX="$test_prefix" WINEDEBUG=-all WINEDLLOVERRIDES=winemenubuilder= \
  bash -c '
    timeout 45 "$1" "$2" --self-test --status "$3"
    first=$?
    if [[ $first -ne 0 ]]; then exit "$first"; fi
    timeout 45 "$1" "$2" --self-test --hotkey-modifiers 6 --hotkey-key 120 --status "$4"
  ' -- "$test_wine" "$project_dir/dist/touch-bridge.exe" "$windows_result" "${windows_result%.json}-custom.json"
python3 - "$result" <<'PY'
import json, sys
with open(sys.argv[1]) as f:
    result = json.load(f)
assert result["phase"] == "self-test-passed", result
assert result["selfTestPassed"] and result["exitCode"] == 0, result
assert not result["filtering"], result
assert result["blockedMouse"] >= 2 and result["probeMouse"] >= 2, result
assert result["probeKeys"] >= 3, result
print("PASS wine_input_filter_smoke: outside-target mouse, target filtering, keyboard, real Ctrl+Alt+F10 off/on, property restore")
with open(sys.argv[1].replace(".json", "-custom.json")) as f:
    custom = json.load(f)
assert custom["phase"] == "self-test-passed" and custom["exitCode"] == 0, custom
assert not custom["filtering"] and custom["probeKeys"] >= 4, custom
print("PASS wine_custom_hotkey_toggle: real Ctrl+Shift+F9 off/on")
PY
