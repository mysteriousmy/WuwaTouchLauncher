#!/usr/bin/env python3
"""Black-box checks against the Windows NativeAOT artifact, in an isolated Wine prefix.

Run with xvfb-run -a python3 test-windows-wine.py.
WUWA_TEST_WINE selects the Wine executable; no game installation is used.
"""
import json
import hashlib
import base64
import os
from pathlib import Path
import shutil
import sqlite3
import struct
import subprocess
import tempfile
import unittest
from tools.native_test_build import wine_include_args, wine_library
import uuid
import time

ROOT = Path(__file__).resolve().parent
BINARY = Path(os.environ.get("WUWA_TEST_LAUNCHER", ROOT / "dist/win-x64/wuwa-touch-launcher.exe"))
WINE = os.environ.get("WUWA_TEST_WINE") or shutil.which("wine")
PREFIX = ROOT / "artifacts/avalonia-wine-prefix"


def windows(path):
    return "Z:" + str(path.resolve()).replace("/", "\\")


def link_strings(path):
    data = path.read_bytes()
    assert struct.unpack_from("<I", data)[0] == 0x4C
    flags = struct.unpack_from("<I", data, 0x14)[0]
    offset = 0x4C
    strings = {}
    if flags & 1:
        offset += 2 + struct.unpack_from("<H", data, offset)[0]
    if flags & 2:
        size, header, link_flags, _, local_base, _, _ = struct.unpack_from("<7I", data, offset)
        if link_flags & 1:
            if header >= 0x24:
                base = offset + struct.unpack_from("<I", data, offset + 28)[0]
                end = base
                while data[end:end + 2] != b"\0\0":
                    end += 2
                strings["target"] = data[base:end].decode("utf-16-le")
            else:
                base = offset + local_base
                strings["target"] = data[base:data.index(0, base)].decode("cp1252")
        offset += struct.unpack_from("<I", data, offset)[0]
    for flag, name in [(4, "name"), (8, "relative"), (16, "working"), (32, "arguments"), (64, "icon")]:
        if flags & flag:
            count = struct.unpack_from("<H", data, offset)[0]
            offset += 2
            size = count * (2 if flags & 128 else 1)
            strings[name] = data[offset:offset + size].decode("utf-16-le" if flags & 128 else "cp1252").rstrip("\0")
            offset += size
    return strings


class WindowsNativeAot(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not WINE:
            raise RuntimeError("Wine is required; set WUWA_TEST_WINE if needed.")
        if not os.environ.get("DISPLAY"):
            raise RuntimeError("Use xvfb-run -a; these tests must not use the user's desktop.")
        PREFIX.mkdir(parents=True, exist_ok=True)
        cls.env = os.environ | {
            "WINEPREFIX": str(PREFIX), "WINEDEBUG": "-all", "WAYLAND_DISPLAY": "",
            "WINEDLLOVERRIDES": "winemenubuilder=;winewayland.drv=d",
            # Wine's system ICU lacks exports required by .NET 10. Real Windows
            # keeps its normal ICU default; this override applies only to Wine QA.
            "DOTNET_SYSTEM_GLOBALIZATION_USENLS": "1",
        }
        cls.env.pop("WUWA_RENDERER", None)
        # Proton's standalone Wine needs the graphics DLLs normally supplied by
        # its launcher. Copy only into this test-owned prefix, never the product.
        vkd3d = Path(WINE).resolve().parents[1] / "lib/vkd3d/x86_64-windows"
        if vkd3d.is_dir():
            subprocess.run([WINE, "wineboot", "--init"], env=cls.env,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True, timeout=45)
            for dll in vkd3d.glob("libvkd3d*.dll"):
                shutil.copy2(dll, PREFIX / "drive_c/windows/system32" / dll.name)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="wuwa-windows-aot-")
        self.root = Path(self.temp.name)
        self.config = self.root / "中文 profile.json"
        self.game = self.root / "游戏/Client/Binaries/Win64/Client-Win64-Shipping.exe"
        self.game.parent.mkdir(parents=True)
        self.game.write_bytes(b"MZ")
        self.registry_keys = []
        self.external_files = []

    def tearDown(self):
        for key in reversed(self.registry_keys):
            subprocess.run([WINE, "reg", "delete", key, "/f"], env=self.env,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30)
        for path in self.external_files:
            path.unlink(missing_ok=True)
        self.temp.cleanup()

    def registry(self, key, **values):
        if key not in self.registry_keys:
            self.registry_keys.append(key)
        for name, value in values.items():
            completed = subprocess.run([WINE, "reg", "add", key, "/v", name, "/t", "REG_SZ",
                                        "/d", value, "/f"], env=self.env, capture_output=True, timeout=30)
            self.assertEqual(completed.returncode, 0, completed.stderr)

    def installation(self, path=None, name="Wuthering Waves"):
        self.registry(r"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\WuwaAotFixture",
                      DisplayName=name, InstallLocation=windows(path or self.game.parents[3]))

    def scan(self):
        return json.loads(self.cli("scan"))["InstalledGames"]

    def cli(self, command, *args):
        result = subprocess.run(
            [WINE, str(BINARY), command, "--config", windows(self.config), *args],
            env=self.env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=45,
        )
        self.assertEqual(result.returncode, 0, result.stderr.decode(errors="replace"))
        return result.stdout.decode("utf-8-sig")

    def save(self):
        self.cli("save", "--game", windows(self.game), "--mode", "touch", "--resources", "keep",
                 "--hotkey", "Ctrl+Shift+F9", "--touch-setting", "fps=240")

    def test_artifact_is_native_pe_and_starts_without_relay_tracing(self):
        data = BINARY.read_bytes()
        pe = struct.unpack_from("<I", data, 0x3C)[0]
        self.assertEqual(data[pe:pe + 4], b"PE\0\0")
        self.assertEqual(struct.unpack_from("<H", data, pe + 4)[0], 0x8664)
        optional = pe + 24
        self.assertEqual(struct.unpack_from("<H", data, optional)[0], 0x20B)
        self.assertEqual(struct.unpack_from("<II", data, optional + 112 + 14 * 8), (0, 0))
        self.assertIn("Avalonia", self.cli("--help"))

    def test_windows_registered_installation_is_deduplicated_and_auto_selected(self):
        self.installation()
        self.registry(r"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\WuwaAotDuplicate",
                      DisplayName="鸣潮", DisplayIcon='"' + windows(self.game) + '",0')
        found = self.scan()
        self.assertEqual([item["Executable"].lower() for item in found], [windows(self.game).lower()])
        self.assertEqual(json.loads(self.cli("preview"))["GameExecutable"].lower(), windows(self.game).lower())

    def test_windows_kuro_launcher_cache_finds_separate_game_directory(self):
        launcher = self.root / "库洛启动器"
        cache = launcher / "kr_game_cache/kr_game_temp.bin"
        cache.parent.mkdir(parents=True)
        payload = json.dumps({"installDirPath": windows(self.game.parents[3]),
                              "resources": json.dumps([{"bundleName": "HD", "installDirPath": windows(self.game.parents[3])}])})
        cache.write_bytes(base64.b64encode(bytes(b ^ 0x63 for b in payload.encode())))
        self.registry(r"HKCU\Software\kurogame\KRLauncher\G152_WuwaAotFixture",
                      SingleLauncherInstallPath=windows(launcher))
        found = self.scan()
        self.assertEqual(len(found), 1)
        self.assertEqual(found[0]["Executable"].lower(), windows(self.game).lower())
        self.assertEqual(found[0]["Source"], "库洛启动器")

    def test_windows_steam_library_metadata_and_epic_manifest_are_detected(self):
        steam = self.root / "Steam"
        library = self.root / "SteamLibrary"
        steamapps = steam / "steamapps"
        steamapps.mkdir(parents=True)
        (steamapps / "libraryfolders.vdf").write_text(
            '"libraryfolders" { "0" { "path" "' + windows(library).replace("\\", "\\\\") + '" } }')
        steam_game = library / "steamapps/common/Wuthering Waves/Client/Binaries/Win64/Client-Win64-Shipping.exe"
        steam_game.parent.mkdir(parents=True)
        steam_game.write_bytes(b"MZ")
        (library / "steamapps/appmanifest_3513350.acf").write_text(
            '"AppState" { "name" "Wuthering Waves" "installdir" "Wuthering Waves" }')
        self.registry(r"HKCU\Software\Valve\Steam", SteamPath=windows(steam))
        epic = PREFIX / "drive_c/ProgramData/Epic/EpicGamesLauncher/Data/Manifests/WuwaAotFixture.item"
        epic.parent.mkdir(parents=True, exist_ok=True)
        epic.write_text(json.dumps({"DisplayName": "Wuthering Waves", "InstallLocation": windows(self.game.parents[3])}))
        self.external_files.append(epic)
        found = self.scan()
        self.assertEqual({entry["Source"] for entry in found}, {"Steam", "Epic Games"})
        self.assertEqual({entry["Executable"].lower() for entry in found},
                         {windows(self.game).lower(), windows(steam_game).lower()})

    def test_windows_missing_and_malformed_records_require_manual_selection(self):
        self.installation(self.root / "missing")
        self.registry(r"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\UnrelatedAotFixture",
                      DisplayName="Unrelated Game", InstallLocation=windows(self.game.parents[3]))
        launcher = self.root / "broken-launcher"
        cache = launcher / "kr_game_cache/kr_game_temp.bin"
        cache.parent.mkdir(parents=True)
        cache.write_text("malformed base64!")
        self.registry(r"HKCU\Software\kurogame\KRLauncher\G152_WuwaAotFixture",
                      SingleLauncherInstallPath=windows(launcher))
        self.assertEqual(self.scan(), [])
        result = subprocess.run([WINE, str(BINARY), "preview", "--config", windows(self.config)],
                                env=self.env, capture_output=True, timeout=45)
        self.assertEqual(result.returncode, 1)
        self.assertIn("请选择 Client-Win64-Shipping.exe", result.stderr.decode("utf-8-sig"))
        self.save()
        self.assertEqual(json.loads(self.cli("preview"))["GameExecutable"].lower(), windows(self.game).lower())

    def test_windows_multiple_installations_are_not_silently_selected(self):
        self.installation()
        second = self.root / "Second/Client/Binaries/Win64/Client-Win64-Shipping.exe"
        second.parent.mkdir(parents=True)
        second.write_bytes(b"MZ")
        self.registry(r"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\WuwaAotSecond",
                      DisplayName="鸣潮", InstallLocation=windows(second.parents[3]))
        result = subprocess.run([WINE, str(BINARY), "preview", "--config", windows(self.config)],
                                env=self.env, capture_output=True, timeout=45)
        self.assertEqual(result.returncode, 1)
        self.assertIn("多个鸣潮安装", result.stderr.decode("utf-8-sig"))

    def test_windows_gui_hides_linux_options_and_keeps_manual_path(self):
        # The scanned installation differs from the valid saved manual selection.
        installed = self.root / "Installed/Client/Binaries/Win64/Client-Win64-Shipping.exe"
        installed.parent.mkdir(parents=True)
        installed.write_bytes(b"MZ")
        self.installation(installed.parents[3])
        self.save()
        before = self.config.read_bytes()
        output = ROOT / "artifacts/avalonia-migration/ui-windows"
        output.mkdir(parents=True, exist_ok=True)
        (output / "result.json").unlink(missing_ok=True)
        self.cli("ui-smoke", "--output", windows(output))
        report = json.loads((output / "result.json").read_text())
        self.assertTrue(report["Passed"], report)
        self.assertEqual(report["Backend"], "Windows")
        self.assertIn(report["Renderer"], ("Gpu", "Software"))
        for behavior in ("native-aot", "live-skia-render-context",
                         "windows-hides-linux-launch-options", "windows-manual-picker-available",
                         "session-footer-starting", "session-footer-switches-to-running", "session-exit-enables-launch",
                         "touch-tap-commits-dropdown-option", "touch-drag-does-not-select-option", "mouse-dropdown-selection-preserved",
                         "touch-cannot-select-missing-resource", "resource-detection-separates-installed-tiers",
                         "windows-hides-linux-advanced-options", "all-56-graphics-options",
                         "memory-mixed-default-all-file", "memory-mixed-checkbox-updates-profile",
                         "memory-radio-exclusive-and-selections-retained",
                         "memory-all-54-options-except-display-and-resolution",
                         "adaptive-fullscreen-default-off", "adaptive-fullscreen-home-toggle",
                         "adaptive-fullscreen-physical-pixels", "adaptive-fullscreen-session-snapshot",
                         "windows-display-api-physical-pixels", "adaptive-fullscreen-elevation-snapshot",
                         "adaptive-fullscreen-owns-display-settings", "adaptive-fullscreen-can-be-disabled",
                         "adaptive-fullscreen-off-enables-manual-settings", "adaptive-fullscreen-hidden-in-normal-mode",
                         "close-dialog-button-visible:close-cancel", "close-dialog-button-visible:close-discard",
                         "close-dialog-button-visible:close-save", "close-cancel-preserves-draft",
                         "windows-three-material-choices-and-default", "windows-version-material-defaults",
                         "mica-disables-opacity-and-keeps-value", "acrylic-preserves-stored-opacity",
                         "disabling-material-restores-background-opacity", "windows-session-error-pipe-preserves-chinese",
                         "theme-controls-and-palette:light", "theme-controls-and-palette:dark",
                         "system-theme-change:Light", "system-theme-change:Dark",
                         "explicit-theme-ignores-system-change",
                         "windows-title-bar-matches-content:light", "windows-title-bar-matches-content:dark",
                         "windows-title-bar-matches-content:mica", "windows-title-bar-matches-content:acrylic",
                         "windows-title-bar-matches-content:close-dialog",
                         "windows-startup-working-set-request", "windows-minimize-working-set-request",
                         "windows-caption-buttons-do-not-overlap:normal",
                         "windows-caption-buttons-do-not-overlap:maximized",
                         "windows-caption-buttons-do-not-overlap:restored",
                         "windows-caption-maximizes", "windows-caption-restores",
                         "windows-text-grayscale-antialias",
                         "working-set-trim-is-not-periodic", "save-succeeds"):
            self.assertIn(behavior, report["Checks"])
        saved = json.loads((output / "profiles-test.json").read_text())
        self.assertEqual(saved["GameExecutable"].lower(), windows(self.game).lower())
        self.assertEqual(saved["TouchOnly"]["MemoryMode"], "mixed")
        self.assertTrue(saved["TouchOnly"]["MemoryOptions"]["native-fg"])
        self.assertTrue(saved["TouchOnly"]["AdaptiveFullscreen"])
        self.assertEqual(saved["Appearance"],
                         {"Theme": "system", "BackgroundOpacity": 72, "WindowsEffect": "none"})
        self.assertEqual(self.config.read_bytes(), before)

    def test_windows_gui_autoselects_one_valid_registered_installation(self):
        self.installation()
        output = self.root / "auto-selection"
        self.cli("ui-smoke", "--output", windows(output))
        self.assertTrue(json.loads((output / "result.json").read_text())["Passed"])
        saved = json.loads((output / "profiles-test.json").read_text())
        self.assertEqual(saved["GameExecutable"].lower(), windows(self.game).lower())
        self.assertFalse(self.config.exists(), "UI diagnostics must not create the original profile")

    def test_windows_single_exe_extracts_the_native_touch_helper(self):
        self.save()
        standalone = self.root / "single-file/wuwa-touch-launcher.exe"
        standalone.parent.mkdir()
        shutil.copy2(BINARY, standalone)
        result = subprocess.run([WINE, str(standalone), "preview", "--config", windows(self.config)],
                                env=self.env, capture_output=True, timeout=45)
        self.assertEqual(result.returncode, 0, result.stderr)
        helper = json.loads(result.stdout.decode("utf-8-sig"))["Command"][0]
        mapped = subprocess.check_output([WINE, "winepath", "-u", helper], env=self.env, timeout=30)
        self.assertEqual(Path(mapped.decode().strip()).read_bytes(), (ROOT / "assets/touch-bridge.exe").read_bytes())
        self.assertEqual(list(standalone.parent.iterdir()), [standalone])

    def test_windows_launch_session_reports_validation_errors_as_utf8(self):
        self.save()
        plan = json.loads(self.cli("preview"))
        status = plan["Command"][plan["Command"].index("--status") + 1]
        token = uuid.uuid4().hex
        report_windows = status.rsplit("\\", 1)[0] + "\\session-result-" + token + ".txt"
        report = Path(subprocess.check_output([WINE, "winepath", "-u", report_windows],
                                             env=self.env, timeout=30).decode().strip())
        report.parent.mkdir(parents=True, exist_ok=True)
        self.external_files.append(report)
        config = json.loads(self.config.read_text())
        config["GameExecutable"] = windows(self.root / "不存在的游戏/Client/Binaries/Win64/Client-Win64-Shipping.exe")
        self.config.write_text(json.dumps(config, ensure_ascii=False))
        before = self.config.read_bytes()
        result = subprocess.run([WINE, str(BINARY), "launch", "--config", windows(self.config),
                                 "--session-result", token], env=self.env, capture_output=True, timeout=45)
        self.assertEqual(result.returncode, 1)
        error = result.stderr.decode("utf-8-sig", errors="strict")
        message = report.read_text(encoding="utf-8-sig")
        self.assertIn("找不到游戏程序：", message)
        self.assertIn("不存在的游戏", message)
        self.assertEqual(error.strip(), "鸣潮启动器：" + message)
        self.assertEqual(self.config.read_bytes(), before)

    def test_windows_launch_session_rejects_path_like_result_tokens(self):
        self.save()
        result = subprocess.run([WINE, str(BINARY), "launch", "--config", windows(self.config),
                                 "--session-result", "../outside"], env=self.env, capture_output=True, timeout=45)
        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.stderr.decode("utf-8-sig").strip(), "鸣潮启动器：启动结果标识无效。")

    def test_windows_saved_profiles_and_ephemeral_mode_override(self):
        self.save()
        before = self.config.read_bytes()
        touch = json.loads(self.cli("preview"))
        normal = json.loads(self.cli("preview", "--mode", "normal"))
        self.assertEqual(touch["Mode"], "touch")
        self.assertIn("-CloudGame", touch["GameArguments"])
        self.assertTrue(touch["IniOverrides"])
        self.assertEqual(normal["Mode"], "normal")
        self.assertNotIn("-CloudGame", normal["GameArguments"])
        self.assertEqual(normal["IniOverrides"], [])
        self.assertEqual(self.config.read_bytes(), before)

    def test_windows_fps_unlock_embeds_original_plugin_and_excludes_normal_mode(self):
        self.save()
        plan = json.loads(self.cli("preview", "--touch-setting", "unlock-fps=420"))
        self.assertEqual(plan["FpsUnlockTarget"], 420)
        self.assertEqual(plan["Command"][plan["Command"].index("--fps-target") + 1], "420")
        plugin = plan["Command"][plan["Command"].index("--fps-plugin") + 1]
        mapped = subprocess.check_output([WINE, "winepath", "-u", plugin], env=self.env, timeout=30)
        self.assertEqual(hashlib.sha256(Path(mapped.decode().strip()).read_bytes()).hexdigest(),
                         "ee48443ed86f65260364ecd36b65bd5a6bb2e4c5dd074e26983d5259847c630b")
        normal = json.loads(self.cli("preview", "--mode", "normal", "--touch-setting", "unlock-fps=420"))
        self.assertEqual(normal["FpsUnlockTarget"], 0)
        self.assertNotIn("--fps-plugin", normal["Command"])

    def test_windows_uses_native_helper_without_linux_environment(self):
        self.save()
        plan = json.loads(self.cli("preview"))
        self.assertEqual(plan["Environment"], {})
        self.assertTrue(plan["Command"][0].endswith("\\touch-bridge.exe"))
        self.assertEqual(plan["Command"][plan["Command"].index("--hotkey-modifiers") + 1], "6")
        self.assertEqual(plan["Command"][plan["Command"].index("--hotkey-key") + 1], "120")

    def test_windows_adjacent_old_helper_cannot_override_embedded_helper(self):
        self.save()
        isolated = self.root / "upgrade with old files"
        isolated.mkdir()
        executable = isolated / BINARY.name
        shutil.copy2(BINARY, executable)
        adjacent = isolated / "touch-bridge.exe"
        stale = b"MZ legacy helper without fps options"
        adjacent.write_bytes(stale)
        before = self.config.read_bytes()
        result = subprocess.run(
            [WINE, str(executable), "preview", "--config", windows(self.config),
             "--touch-setting", "unlock-fps=120"],
            env=self.env, capture_output=True, timeout=45)
        self.assertEqual(result.returncode, 0, result.stderr.decode(errors="replace"))
        plan = json.loads(result.stdout.decode("utf-8-sig"))
        helper = plan["Command"][0]
        self.assertNotEqual(helper.lower(), windows(adjacent).lower())
        expected = (ROOT / "assets/touch-bridge.exe").read_bytes()
        digest = hashlib.sha256(expected).hexdigest()
        self.assertTrue(helper.lower().endswith("\\runtime\\" + digest + "\\touch-bridge.exe"))
        cached = PREFIX / "drive_c" / helper[3:].replace("\\", "/")
        self.assertEqual(cached.read_bytes(), expected)
        self.assertEqual(plan["FpsUnlockTarget"], 120)
        self.assertEqual(adjacent.read_bytes(), stale)
        self.assertEqual(self.config.read_bytes(), before)

    def test_native_lnk_arguments_reference_shared_config_and_fixed_mode(self):
        self.save()
        links = self.root / "快捷方式"
        self.cli("shortcuts", "--shortcut-dir", windows(links))
        for mode, title in [("normal", "普通模式"), ("touch", "触控模式")]:
            text = link_strings(links / f"鸣潮 · {title}.lnk")
            self.assertEqual(text["target"].lower(), windows(BINARY).lower())
            self.assertEqual(text["working"].lower(), windows(BINARY.parent).lower())
            self.assertEqual(text["arguments"], f'"launch" "--config" "{windows(self.config)}" "--mode" "{mode}" "--notify"')

    def test_windows_native_session_heartbeat_and_clean_exit(self):
        obj = self.root / "game.obj"
        subprocess.run(["clang", "--target=x86_64-pc-windows-msvc", "-O2", "-fno-stack-protector",
                        "-fno-builtin", *wine_include_args(),
                        "-c", str(ROOT / "tests/fps-child-fixture.c"), "-o", str(obj)], check=True)
        subprocess.run(["lld-link", "/nodefaultlib", "/subsystem:windows", "/entry:mainCRTStartup",
                        "/out:" + str(self.game), str(obj), wine_library("kernel32")], check=True)
        self.save()
        self.cli("save", "--mode", "normal", "--resources", "keep")
        plan = json.loads(self.cli("preview"))
        path = plan["Command"][plan["Command"].index("--status") + 1]
        self.assertNotIn("wine", plan["Source"].lower())
        # Native Windows path in the isolated prefix; helper writes the actual status.
        if path.startswith("Z:"):
            status = Path(path[2:].replace(chr(92), "/"))
        else:
            self.assertTrue(path.startswith("C:"), path)
            status = PREFIX / "drive_c" / path[3:].replace(chr(92), "/")
        process = subprocess.Popen([WINE, str(BINARY), "launch", "--config", windows(self.config)],
                                   env=self.env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        observed = []
        try:
            deadline = time.monotonic() + 35
            while time.monotonic() < deadline and process.poll() is None:
                try:
                    data = json.loads(status.read_text())
                    if data["phase"] == "active" and data.get("targetCount", 0) > 0:
                        observed.append(data["elapsedMs"])
                except (FileNotFoundError, json.JSONDecodeError):
                    pass
                time.sleep(.1)
            out, err = process.communicate(timeout=5)
            self.assertEqual(process.returncode, 0, (out + err).decode("utf-8-sig", errors="replace"))
            self.assertGreater(len(set(observed)), 2, "Native heartbeat did not progress")
            self.assertEqual(json.loads(status.read_text())["phase"], "game-exited")
            self.assertNotIn(b"Wine", err)
        finally:
            if process.poll() is None:
                process.kill()
                process.communicate(timeout=5)

    def test_windows_graphics_database_restores_after_failed_game_launch(self):
        self.save()
        self.cli("save", "--touch-setting", "shadow=3", "--touch-setting", "ray-quality=3",
                 "--touch-setting", "ray-gi=1", "--touch-setting", "ray-reflections=1",
                 "--touch-setting", "ray-shadows=1", "--touch-setting", "unlock-fps=240")
        plan = json.loads(self.cli("preview"))
        self.assertEqual(plan["DatabaseOverrides"], {
            "ShadowQuality": "3", "RayTracing": "3", "RayTracedGI": "1",
            "RayTracedReflection": "1", "RayTracedShadow": "1"})
        db_path = self.game.parents[2] / "Saved/LocalStorage/LocalStorage.db"
        db_path.parent.mkdir(parents=True)
        original = {key: "0" for key in plan["DatabaseOverrides"]}
        original["IsUsingQualityPreset"] = '"___1B___"'
        original["CustomFrameRate"] = "2"
        original["PcVsync"] = "1"
        with sqlite3.connect(db_path) as db:
            db.execute("CREATE TABLE LocalStorage(key TEXT PRIMARY KEY,value TEXT)")
            db.executemany("INSERT INTO LocalStorage VALUES(?,?)", original.items())
        db.close()
        result = subprocess.run([WINE, str(BINARY), "launch", "--config", windows(self.config)],
                                env=self.env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=45)
        self.assertEqual(result.returncode, 1)
        self.assertIn("Win32", result.stderr.decode("utf-8-sig", errors="replace"))
        with sqlite3.connect(db_path) as db:
            self.assertEqual(dict(db.execute("SELECT key,value FROM LocalStorage")), original)
        db.close()


if __name__ == "__main__":
    unittest.main(verbosity=2)
