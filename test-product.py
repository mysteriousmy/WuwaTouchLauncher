#!/usr/bin/env python3
"""Black-box product contracts against the actual published NativeAOT executable."""
import json
import os
from pathlib import Path
import signal
import sqlite3
import subprocess
import sys
import tempfile
import time
import unittest
from contextlib import contextmanager


@contextmanager
def database(path):
    db = sqlite3.connect(path)
    try:
        with db:
            yield db
    finally:
        db.close()

LAUNCHER = Path(os.environ.get("WUWA_TEST_LAUNCHER",
                Path(__file__).resolve().parent / "dist/wuwa-touch-launcher")).resolve()
RUNNER = r'''
import json, os, sys, time
from pathlib import Path
root = Path(os.environ["TEST_ROOT"])
state = Path(os.environ["XDG_STATE_HOME"]) / "wuwa-touch-launcher"
state.mkdir(parents=True, exist_ok=True)
(root / "argv.json").write_text(json.dumps(sys.argv[1:]))
(root / "pid").write_text(str(os.getpid()))
def status(phase):
    filtered = "--filter" in sys.argv and sys.argv[sys.argv.index("--filter") + 1] == "1"
    p = state / "status.tmp"
    p.write_text(json.dumps({"phase": phase, "filtering": filtered if phase == "active" else False,
                            "exitCode": 0, "targetCount": 1 if phase == "active" else 0}))
    p.replace(state / "status.json")
status("active")
if os.environ.get("TEST_HOLD") == "1":
    while not (root / "finish").exists():
        status("active")
        time.sleep(0.05)
status("game-exited")
'''


class ProductContracts(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="wuwa-product-")
        self.root = Path(self.temp.name)
        self.config_path = self.root / '配置 path % $ " test.json'
        self.client = self.root / "游戏 with spaces/Client"
        self.game = self.client / "Binaries/Win64/Client-Win64-Shipping.exe"
        self.game.parent.mkdir(parents=True)
        self.game.write_bytes(b"fixture")
        self.ini = self.client / "Saved/Config/WindowsNoEditor/Engine.ini"
        self.ini.parent.mkdir(parents=True)
        self.ini.write_text("[SystemSettings]\nt.MaxFPS=60\nOtherSetting=untouched\n")
        self.original_ini = self.ini.read_bytes()
        self.user_ini = self.ini.with_name("GameUserSettings.ini")
        self.user_ini.write_text("[/Script/Engine.GameUserSettings]\nFrameRateLimit=60.000000\n")
        self.opti = self.game.parent / "OptiScaler.ini"
        self.opti.write_text("[FrameGen]\nEnabled=custom-user-setting\n")
        self.db = self.client / "Saved/LocalStorage/LocalStorage.db"
        self.db.parent.mkdir(parents=True)
        with database(self.db) as db:
            db.execute("CREATE TABLE LocalStorage(key TEXT PRIMARY KEY, value TEXT)")
            db.execute("INSERT INTO LocalStorage VALUES('ImageQuality','2')")
            db.execute("INSERT INTO LocalStorage VALUES('CustomFrameRate','2')")
            db.execute("INSERT INTO LocalStorage VALUES('IsUsingQualityPreset',?)", ('"___0B___"',))
            for key in ("PcVsync", "AnisoLevel", "ShadowQuality", "NiagaraQuality", "ImageDetail",
                        "AntiAliasing", "SceneAo", "VolumeFog", "VolumeLight", "BloomEnable", "MotionBlur",
                        "NpcDensity", "VegetationDensity", "VegetationDither", "DynamicBones", "WaterInteract",
                        "TeammateFx", "EnemyHitDisplayMode", "SkinDamageMode", "LoadingRangeScaleLevel",
                        "AutoAdjustImageQuality", "AutoExposure", "NvidiaSuperSamplingEnable",
                        "NvidiaSuperSamplingQuality", "NvidiaSuperSamplingSharpness",
                        "NvidiaSuperSamplingFrameGenerate", "Fsr3Enable", "Fsr3Quality", "Fsr3Fg", "FsrEnable",
                        "Xess2Enable", "Xess2Quality", "Xess2Fg", "RayTracing", "RayTracedGI",
                        "RayTracedReflection", "RayTracedShadow", "Brightness", "SaturationNew", "ContrastNew",
                        "ImageDisplayMode", "EyeProtectionMode", "EyeProtectionTemp", "EyeProtectionStrength",
                        "EyeProtectionBrightness", "EyeProtectionTexture"):
                db.execute("INSERT INTO LocalStorage VALUES(?, '0')", (key,))
        for tier in ("HD", "UHD"):
            directory = self.client / "Content" / tier
            directory.mkdir(parents=True)
            (directory / "base.pak").write_bytes(b"fixture-pak")
        self.runner = self.root / "fake wine runner"
        self.runner.write_text(f"#!{sys.executable}\n" + RUNNER)
        self.runner.chmod(0o755)
        self.prefix = self.root / "prefix"
        self.prefix.mkdir()
        self.env = os.environ.copy()
        self.env.pop("DISPLAY", None)  # Every CLI contract must work without a GUI.
        self.env["XDG_CONFIG_HOME"] = str(self.root / "config")
        self.env["XDG_STATE_HOME"] = str(self.root / "state")
        self.env["XDG_DATA_HOME"] = str(self.root / "data")
        self.config = {
            "Version": 2, "Mode": "normal", "GameExecutable": str(self.game),
            "Linux": {"Integration": "manual", "Runner": str(self.runner), "RunnerKind": "wine",
                      "Prefix": str(self.prefix), "Environment": {"TEST_ROOT": str(self.root)}},
            "Normal": {"Resources": "hd", "DirectX": "dx12", "SkipSplash": True, "Quality": "keep"},
            "Touch": {"Resources": "hd", "DirectX": "dx12", "SkipSplash": True, "Quality": "keep"},
            "TouchOnly": {"MouseFilter": True, "HotkeyEnabled": True, "Hotkey": "Ctrl+Alt+F10",
                          "Values": {"fps": "240", "vsync": "0", "anisotropy": "16"}}
        }
        self.write_config()
        self.children = []

    def write_config(self):
        self.config_path.write_text(json.dumps(self.config, ensure_ascii=False))

    def cli(self, command, *args, expected=0):
        result = subprocess.run([str(LAUNCHER), command, "--config", str(self.config_path), *args],
                                env=self.env, text=True, capture_output=True, timeout=15)
        self.assertEqual(result.returncode, expected, result.stdout + result.stderr)
        return result

    def plan(self, *args):
        return json.loads(self.cli("preview", *args).stdout)

    def test_appearance_defaults_and_cli_save_preserve_preferences(self):
        self.cli("save")
        saved = json.loads(self.config_path.read_text())
        self.assertEqual(saved["Appearance"],
                         {"Theme": "system", "BackgroundOpacity": 85, "WindowsEffect": "auto"})
        for theme, opacity, effect in [("light", 30, "none"), ("dark", 100, "mica"), ("system", 72, "acrylic")]:
            with self.subTest(theme=theme, opacity=opacity, effect=effect):
                appearance = {"Theme": theme, "BackgroundOpacity": opacity, "WindowsEffect": effect}
                self.config["Appearance"] = appearance
                self.write_config()
                self.cli("save", "--mode", "touch")
                self.assertEqual(json.loads(self.config_path.read_text())["Appearance"], appearance)

    def test_invalid_appearance_does_not_overwrite_profile(self):
        for field, value in [("Theme", "unknown"), ("WindowsEffect", "blur"),
                             ("BackgroundOpacity", 29), ("BackgroundOpacity", 101)]:
            with self.subTest(field=field, value=value):
                self.config["Appearance"] = {field: value}
                self.write_config()
                original = self.config_path.read_bytes()
                result = self.cli("save", expected=1)
                self.assertTrue(result.stderr.strip())
                self.assertEqual(self.config_path.read_bytes(), original)

    def test_shortcuts_do_not_rewrite_existing_profile(self):
        original = self.config_path.read_bytes()
        self.cli("shortcuts")
        self.assertEqual(self.config_path.read_bytes(), original)
        self.assertFalse(Path(str(self.config_path) + ".bak").exists())
        entries = self.root / "data/applications"
        self.assertTrue((entries / "wuwa-normal.desktop").is_file())
        self.assertTrue((entries / "wuwa-touch.desktop").is_file())

    def start(self, *args):
        process = subprocess.Popen([str(LAUNCHER), "launch", "--config", str(self.config_path), *args],
                                   env=self.env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        self.children.append(process)
        deadline = time.monotonic() + 8
        while time.monotonic() < deadline:
            if (self.root / "pid").exists():
                return process
            if process.poll() is not None:
                self.fail("Launcher exited before the fixture became active: " + str(process.communicate()))
            time.sleep(0.05)
        self.fail("Fixture did not start")

    def finish(self, process):
        (self.root / "finish").touch()
        out, err = process.communicate(timeout=8)
        self.assertEqual(process.returncode, 0, out + err)

    def tearDown(self):
        for p in self.children:
            if p.poll() is None:
                p.kill()
            p.communicate(timeout=3)
        pid_file = self.root / "pid"
        if pid_file.exists():
            try:
                os.kill(int(pid_file.read_text()), signal.SIGTERM)
            except ProcessLookupError:
                pass
        self.temp.cleanup()

    def test_cli_overrides_use_selected_profile_and_do_not_save(self):
        before = self.config_path.read_bytes()
        plan = self.plan("--resources", "uhd", "--dx", "dx11", "--skip-splash", "false", "--mode", "touch")
        self.assertIn("-krqlv=uhd", plan["GameArguments"])
        self.assertIn("-dx11", plan["GameArguments"])
        self.assertIn("-CloudGame", plan["GameArguments"])
        self.assertNotIn("-SkipSplash", plan["GameArguments"])
        self.assertEqual(self.config_path.read_bytes(), before)
        self.assertEqual(self.ini.read_bytes(), self.original_ini)
        self.assertFalse((self.root / "argv.json").exists())

    def test_save_round_trips_all_gui_options(self):
        self.cli("save", "--mode", "touch", "--resources", "uhd", "--quality", "3", "--dx", "dx11",
                 "--skip-splash", "false", "--args", '-windowed "-name=中文 name"',
                 "--mouse-filter", "false", "--hotkey-enabled", "false", "--hotkey", "Shift+F8",
                 "--touch-setting", "fps=144", "--touch-setting", "ray-tracing=keep",
                 "--env", "PROTON_LOG=1", "--wrapper", "gamemoderun", "--umu", "/tmp/custom-umu",
                 "--inherit-lutris-wrapper", "false",
                 "--runner-kind", "wine", "--prefix", str(self.prefix))
        saved = json.loads(self.cli("config").stdout)
        self.assertEqual(saved["Mode"], "touch")
        self.assertEqual(saved["Touch"]["Resources"], "uhd")
        self.assertEqual(saved["Touch"]["Quality"], "3")
        self.assertEqual(saved["Touch"]["DirectX"], "dx11")
        self.assertFalse(saved["Touch"]["SkipSplash"])
        self.assertEqual(saved["Touch"]["CustomArguments"], '-windowed "-name=中文 name"')
        self.assertEqual(saved["Normal"]["Resources"], "hd")
        self.assertFalse(saved["TouchOnly"]["MouseFilter"])
        self.assertFalse(saved["TouchOnly"]["HotkeyEnabled"])
        self.assertEqual(saved["TouchOnly"]["Hotkey"], "Shift+F8")
        self.assertEqual(saved["TouchOnly"]["Values"]["fps"], "144")
        self.assertEqual(saved["Linux"]["Environment"]["PROTON_LOG"], "1")
        self.assertEqual(saved["Linux"]["Wrapper"], "gamemoderun")
        self.assertEqual(saved["Linux"]["Umu"], "/tmp/custom-umu")
        self.assertFalse(saved["Linux"]["InheritLutrisWrapper"])
        self.assertEqual(self.config_path.stat().st_mode & 0o777, 0o600)
        self.assertTrue(Path(str(self.config_path) + ".bak").exists())

    def test_normal_mode_never_applies_touch_settings_or_hooks(self):
        plan = self.plan("--mode", "normal")
        self.assertEqual(plan["IniOverrides"], [])
        self.assertEqual(plan["DatabaseOverrides"], {})
        self.assertIsNone(plan["ImageQuality"])
        self.assertFalse(any("CloudGame" in arg for arg in plan["GameArguments"]))
        for key in ("--allow-filter", "--filter", "--hotkey-enabled"):
            self.assertEqual(plan["Command"][plan["Command"].index(key) + 1], "0")

    def test_touch_configuration_excludes_optiscaler(self):
        plan = self.plan("--mode", "touch", "--touch-setting", "dlss=1", "--touch-setting", "ray-quality=3")
        self.assertGreater(len(plan["IniOverrides"]), 0)
        self.assertTrue(all(Path(x["Path"]).name in ("Engine.ini", "GameUserSettings.ini") for x in plan["IniOverrides"]))
        self.assertNotIn("OptiScaler", json.dumps(plan["IniOverrides"]))
        self.assertEqual(plan["Command"][plan["Command"].index("--hotkey-modifiers") + 1], "3")
        self.assertEqual(plan["Command"][plan["Command"].index("--hotkey-key") + 1], "121")

    def test_argument_quoting_preserves_unicode_paths_and_literal_shell_text(self):
        payload = r'''-path="C:\Games\鸣潮 folder\Client.exe" '$(echo nothing)' "" -label="a\"b"'''
        args = self.plan("--args", payload)["GameArguments"]
        self.assertIn("-path=C:\\Games\\鸣潮 folder\\Client.exe", args)
        self.assertIn("$(echo nothing)", args)
        self.assertIn("", args)
        self.assertIn('-label=a"b', args)

    def test_validation_rejects_bad_options_without_overwriting_config(self):
        before = self.config_path.read_bytes()
        cases = [
            ("invalid-mode", ["--mode", "other"]),
            ("invalid-resource", ["--resources", "4k"]),
            ("invalid-hotkey", ["--hotkey", "F10"]),
            ("invalid-hotkey-key", ["--hotkey", "Ctrl+F25"]),
            ("invalid-touch-value", ["--touch-setting", "fps=239"]),
            ("unknown-option", ["--unknown", "x"]),
            ("unclosed-quote", ["--args", '"unterminated']),
            ("invalid-env", ["--env", "BAD-NAME=value"]),
        ]
        for name, values in cases:
            with self.subTest(case=name):
                self.cli("save", *values, expected=1)
                self.assertEqual(self.config_path.read_bytes(), before)

    def test_custom_managed_arguments_are_rejected(self):
        self.cli("preview", "--args", "-CloudGame", expected=1)
        self.cli("preview", "--args", "-krqlv=uhd", expected=1)

    def test_lutris_yaml_inheritance_and_original_arguments_are_preserved(self):
        lutris = self.root / "lutris"
        (lutris / "games").mkdir(parents=True)
        (lutris / "runners").mkdir()
        (lutris / "system.yml").write_text("system:\n  env:\n    BASE: global\n    OVERRIDE: global\n")
        (lutris / "runners/wine.yml").write_text("system:\n  env: {OVERRIDE: runner}\nwine:\n  esync: false\n")
        path = lutris / "games/wuthering-waves.yml"
        path.write_text(
            f"game:\n  exe: '{self.game}'\n  prefix: '{self.prefix}'\n"
            '  args: -dx11 -krqlv=uhd -SkipSplash "-custom=from Lutris"\n'
            f"wine:\n  custom_wine_path: '{self.runner}'\n"
            "system:\n  env:\n    OVERRIDE: game\n    COMPLEX: 'a: b # c'\n"
        )
        before = path.read_bytes()
        plan = self.plan("--integration", "lutris", "--lutris-config", str(path),
                         "--resources", "keep", "--dx", "auto", "--env", "OVERRIDE=user")
        self.assertEqual(plan["GameExecutable"], str(self.game))
        self.assertIn("-dx11", plan["GameArguments"])
        self.assertIn("-krqlv=uhd", plan["GameArguments"])
        self.assertIn("-custom=from Lutris", plan["GameArguments"])
        self.assertEqual(plan["Environment"]["BASE"], "global")
        self.assertEqual(plan["Environment"]["OVERRIDE"], "game")
        self.assertNotIn("TEST_ROOT", plan["Environment"])
        self.assertEqual(plan["Environment"]["COMPLEX"], "a: b # c")
        self.assertEqual(plan["Environment"]["WINEESYNC"], "0")
        self.assertEqual(path.read_bytes(), before)

    def test_normal_defaults_hd_and_preserves_explicit_resource_preference(self):
        self.config.pop("Normal")
        self.write_config()
        self.assertIn("-krqlv=hd", self.plan("--mode", "normal")["GameArguments"])
        for tier in ("keep", "sd", "hd", "uhd"):
            self.cli("save", "--mode", "normal", "--resources", tier)
            self.assertEqual(json.loads(self.config_path.read_text())["Normal"]["Resources"], tier)

    def test_sd_resource_preflight_requires_nonempty_pack_and_launches_installed_tier(self):
        sd = self.client / "Content/SD/subdir/pack.PAK"
        sd.parent.mkdir(parents=True)
        for data in (None, b""):
            if data is not None:
                sd.write_bytes(data)
            error = self.cli("launch", "--mode", "normal", "--resources", "sd", expected=1)
            self.assertIn("尚未安装 SD 资源", error.stderr)
            self.assertFalse((self.root / "argv.json").exists())
            self.assertEqual(self.ini.read_bytes(), self.original_ini)
        sd.write_bytes(b"resource fixture")
        self.cli("launch", "--mode", "normal", "--resources", "sd")
        self.assertIn("-krqlv=sd", json.loads((self.root / "argv.json").read_text()))

    def test_missing_resource_preflight_does_not_touch_ini_or_launch(self):
        (self.client / "Content/HD/base.pak").unlink()
        self.cli("launch", "--mode", "touch", expected=1)
        self.assertEqual(self.ini.read_bytes(), self.original_ini)
        self.assertFalse((self.root / "argv.json").exists())

    def test_touch_session_restores_ini_and_leaves_optiscaler_untouched(self):
        self.config["Linux"]["Environment"]["TEST_HOLD"] = "1"
        self.write_config()
        opti_before = self.opti.read_bytes()
        process = self.start("--mode", "touch")
        self.assertIn("t.MaxFPS=240", self.ini.read_text())
        self.assertIn("r.MaxAnisotropy=16", self.ini.read_text())
        with self.user_ini.open("a") as stream:
            stream.write("MusicVolume=7\n")
        self.finish(process)
        self.assertEqual(self.ini.read_bytes(), self.original_ini)
        self.assertIn("FrameRateLimit=60.000000", self.user_ini.read_text())
        self.assertIn("MusicVolume=7", self.user_ini.read_text())
        self.assertEqual(self.opti.read_bytes(), opti_before)
        self.assertFalse((self.root / "state/wuwa-touch-launcher/graphics-restore.json").exists())

    def test_normal_session_preserves_existing_graphics(self):
        before = self.user_ini.read_bytes()
        self.cli("launch", "--mode", "normal")
        self.assertEqual(self.ini.read_bytes(), self.original_ini)
        self.assertEqual(self.user_ini.read_bytes(), before)
        args = json.loads((self.root / "argv.json").read_text())
        self.assertEqual(args[args.index("--allow-filter") + 1], "0")

    def test_quality_preset_is_restored_after_session(self):
        self.config["Linux"]["Environment"]["TEST_HOLD"] = "1"
        self.config["TouchOnly"]["Values"] = {}
        self.write_config()
        process = self.start("--mode", "touch", "--quality", "3")
        with database(self.db) as db:
            self.assertEqual(db.execute("SELECT value FROM LocalStorage WHERE key='ImageQuality'").fetchone()[0], "3")
            self.assertEqual(db.execute("SELECT value FROM LocalStorage WHERE key='IsUsingQualityPreset'").fetchone()[0], '"___1B___"')
        self.finish(process)
        with database(self.db) as db:
            self.assertEqual(db.execute("SELECT value FROM LocalStorage WHERE key='ImageQuality'").fetchone()[0], "2")
            self.assertEqual(db.execute("SELECT value FROM LocalStorage WHERE key='IsUsingQualityPreset'").fetchone()[0], '"___0B___"')

    def test_quality_group_preserves_later_in_game_choice(self):
        self.config["Linux"]["Environment"]["TEST_HOLD"] = "1"
        self.config["TouchOnly"]["Values"] = {}
        self.write_config()
        process = self.start("--mode", "touch", "--quality", "3")
        with database(self.db) as db:
            db.execute("UPDATE LocalStorage SET value='4' WHERE key='ImageQuality'")
        self.finish(process)
        with database(self.db) as db:
            self.assertEqual(db.execute("SELECT value FROM LocalStorage WHERE key='ImageQuality'").fetchone()[0], "4")
            self.assertEqual(db.execute("SELECT value FROM LocalStorage WHERE key='IsUsingQualityPreset'").fetchone()[0], '"___1B___"')

    def test_launch_without_overrides_uses_last_saved_touch_profile(self):
        self.cli("save", "--mode", "touch", "--resources", "uhd", "--mouse-filter", "false", "--hotkey", "Shift+F8")
        self.cli("launch")
        args = json.loads((self.root / "argv.json").read_text())
        self.assertIn("-CloudGame", args)
        self.assertIn("-krqlv=uhd", args)
        self.assertEqual(args[args.index("--filter") + 1], "0")
        self.assertEqual(args[args.index("--hotkey-modifiers") + 1], "4")
        self.assertEqual(args[args.index("--hotkey-key") + 1], "119")

    def test_invalid_null_config_is_reported_without_replacement(self):
        self.config["TouchOnly"] = None
        self.write_config()
        before = self.config_path.read_bytes()
        result = self.cli("save", expected=1)
        self.assertIn("鸣潮启动器", result.stderr)
        self.assertEqual(self.config_path.read_bytes(), before)

    def test_lutris_owns_hidden_wrapper_runner_prefix_and_arguments(self):
        path = self.root / "lutris/games/wuthering-waves.yml"
        path.parent.mkdir(parents=True)
        path.write_text(
            f"game:\n  exe: '{self.game}'\n  prefix: '{self.prefix}'\n"
            "  args: -from-lutris\n"
            f"wine:\n  custom_wine_path: '{self.runner}'\n"
            "system:\n  prefix_command: gamemoderun\n"
        )
        inherited = self.plan("--integration", "lutris", "--lutris-config", str(path))
        self.assertEqual(inherited["Command"][0], "gamemoderun")
        self.config["Linux"].update({"Wrapper": "hidden-wrapper", "Runner": "/missing/manual-runner",
                                     "Prefix": "/missing/manual-prefix", "RunnerKind": "proton",
                                     "Umu": "/missing/manual-umu", "InheritLutrisWrapper": False})
        self.config["Normal"]["CustomArguments"] = "-hidden-argument"
        self.write_config()
        owned = self.plan("--integration", "lutris", "--lutris-config", str(path))
        self.assertEqual(owned["Command"][:2], ["gamemoderun", str(self.runner)])
        self.assertEqual(owned["Environment"]["WINEPREFIX"], str(self.prefix))
        self.assertIn("-from-lutris", owned["GameArguments"])
        self.assertNotIn("-hidden-argument", owned["GameArguments"])
        self.assertNotIn("TEST_ROOT", owned["Environment"])

    def test_lutris_missing_runner_does_not_use_saved_manual_runner(self):
        path = self.root / "lutris/games/wuthering-waves.yml"
        path.parent.mkdir(parents=True)
        path.write_text(
            f"game:\n  exe: '{self.game}'\n  prefix: '{self.prefix}'\n"
            "wine:\n  version: nonexistent-wuwa-test-runner\n"
        )
        result = self.cli("preview", "--integration", "lutris", "--lutris-config", str(path), expected=1)
        self.assertIn("Lutris 条目指定的运行器", result.stderr)
        self.assertFalse((self.root / "argv.json").exists())

    def test_single_file_materializes_and_repairs_its_embedded_touch_helper(self):
        import shutil
        isolated = self.root / "single-file"
        isolated.mkdir()
        executable = isolated / LAUNCHER.name
        shutil.copy2(LAUNCHER, executable)
        before = self.config_path.read_bytes()
        def preview():
            completed = subprocess.run([str(executable), "preview", "--config", str(self.config_path)],
                                       env=self.env, text=True, capture_output=True, timeout=15)
            self.assertEqual(completed.returncode, 0, completed.stderr)
            return json.loads(completed.stdout)
        plan = preview()
        helper = Path(plan["Command"][1])
        self.assertTrue(helper.is_relative_to(self.root / "state"))
        original = helper.read_bytes()
        self.assertTrue(original.startswith(b"MZ"))
        self.assertEqual(original, (Path(__file__).parent / "assets/touch-bridge.exe").read_bytes())
        helper.write_bytes(b"interrupted previous extraction")
        self.assertEqual(Path(preview()["Command"][1]).read_bytes(), original)
        self.assertEqual(self.config_path.read_bytes(), before)
        self.assertEqual(list(isolated.iterdir()), [executable])

    def test_adjacent_old_helper_cannot_override_fps_capable_embedded_helper(self):
        import shutil
        isolated = self.root / "upgrade with old files"
        isolated.mkdir()
        executable = isolated / LAUNCHER.name
        shutil.copy2(LAUNCHER, executable)
        adjacent = isolated / "touch-bridge.exe"
        stale = b"MZ legacy helper without fps options"
        adjacent.write_bytes(stale)
        before = self.config_path.read_bytes()
        for mode in ("touch", "normal"):
            with self.subTest(mode=mode):
                result = subprocess.run(
                    [str(executable), "preview", "--config", str(self.config_path), "--mode", mode,
                     "--touch-setting", "unlock-fps=120"],
                    env=self.env, text=True, capture_output=True, timeout=15)
                self.assertEqual(result.returncode, 0, result.stderr)
                plan = json.loads(result.stdout)
                helper = Path(plan["Command"][1])
                self.assertNotEqual(helper, adjacent)
                self.assertEqual(helper.read_bytes(),
                                 (Path(__file__).parent / "assets/touch-bridge.exe").read_bytes())
                self.assertEqual(plan["FpsUnlockTarget"], 120 if mode == "touch" else 0)
        self.assertEqual(adjacent.read_bytes(), stale)
        self.assertEqual(self.config_path.read_bytes(), before)

    def test_launch_errors_can_notify_from_headless_menu_entry(self):
        directory = self.root / "bin"
        directory.mkdir()
        notifier = directory / "notify-send"
        notifier.write_text(f"#!{sys.executable}\nimport sys\nfrom pathlib import Path\n"
                            f"Path({str(self.root / 'notification')!r}).write_text('\\n'.join(sys.argv[1:]))\n")
        notifier.chmod(0o755)
        self.env["PATH"] = str(directory) + os.pathsep + self.env["PATH"]
        self.game.unlink()
        self.cli("launch", "--notify", expected=1)
        text = (self.root / "notification").read_text()
        self.assertIn("--urgency=critical", text)
        self.assertIn("找不到游戏", text)

    def test_settings_files_created_for_touch_are_removed_after_exit(self):
        self.ini.unlink()
        self.user_ini.unlink()
        self.cli("launch", "--mode", "touch")
        self.assertFalse(self.ini.exists())
        self.assertFalse(self.user_ini.exists())

    def test_graphics_catalog_round_trips_all_categories(self):
        # Independent expectations from the 3.7 menu's keys/ranges. This also
        # exercises every non-legacy database mapping through the public CLI.
        cases = {
            "shadow": ("3", "ShadowQuality"), "effects": ("2", "NiagaraQuality"),
            "detail": ("3", "ImageDetail"), "anti-aliasing": ("1", "AntiAliasing"),
            "ambient-occlusion": ("3", "SceneAo"), "volumetric-fog": ("3", "VolumeFog"),
            "volumetric-light": ("1", "VolumeLight"), "bloom": ("1", "BloomEnable"),
            "motion-blur": ("2", "MotionBlur"), "npc-density": ("2", "NpcDensity"),
            "vegetation": ("3", "VegetationDensity"), "vegetation-dither": ("1", "VegetationDither"),
            "dynamic-bones": ("1", "DynamicBones"), "environment-interaction": ("1", "WaterInteract"),
            "teammate-effects": ("1", "TeammateFx"), "hit-material": ("1", "EnemyHitDisplayMode"),
            "damage-effects": ("1", "SkinDamageMode"), "view-distance": ("2", "LoadingRangeScaleLevel"),
            "auto-quality": ("0", "AutoAdjustImageQuality"), "auto-exposure": ("1", "AutoExposure"),
            "dlss": ("1", "NvidiaSuperSamplingEnable"), "dlss-quality": ("3", "NvidiaSuperSamplingQuality"),
            "dlss-sharpness": ("0.4", "NvidiaSuperSamplingSharpness"),
            "native-fg": ("5", "NvidiaSuperSamplingFrameGenerate"),
            "fsr": ("0", "Fsr3Enable"), "fsr-quality": ("-2", "Fsr3Quality"), "fsr-fg": ("1", "Fsr3Fg"),
            "fsr-legacy": ("0", "FsrEnable"), "xess": ("0", "Xess2Enable"),
            "xess-quality": ("4", "Xess2Quality"), "xess-fg": ("3", "Xess2Fg"),
            "ray-quality": ("3", "RayTracing"), "ray-gi": ("1", "RayTracedGI"),
            "ray-reflections": ("1", "RayTracedReflection"), "ray-shadows": ("1", "RayTracedShadow"),
            "brightness": ("-0.8", "Brightness"), "saturation": ("68", "SaturationNew"),
            "contrast": ("32", "ContrastNew"), "image-display": ("2", "ImageDisplayMode"),
            "eye-mode": ("2", "EyeProtectionMode"), "eye-temperature": ("6500", "EyeProtectionTemp"),
            "eye-strength": ("0.8", "EyeProtectionStrength"), "eye-brightness": ("0.5", "EyeProtectionBrightness"),
            "eye-texture": ("0.4", "EyeProtectionTexture"),
        }
        args = [item for key, (value, _) in cases.items() for item in ("--touch-setting", f"{key}={value}")]
        self.cli("save", "--mode", "touch", *args)
        saved = json.loads(self.cli("config").stdout)["TouchOnly"]["Values"]
        plan = self.plan()
        for key, (value, dbkey) in cases.items():
            with self.subTest(setting=key):
                self.assertEqual(saved[key], value)
                self.assertEqual(plan["DatabaseOverrides"][dbkey], value)
        self.assertEqual(plan["DatabaseOverrides"]["AnisoLevel"], "4")
        self.assertEqual(plan["DatabaseOverrides"]["PcVsync"], "0")
        normal = self.plan("--mode", "normal")
        self.assertEqual(normal["DatabaseOverrides"], {})
        self.assertEqual(normal["IniOverrides"], [])
        keep_args = [item for key in saved for item in ("--touch-setting", f"{key}=keep")]
        empty = self.plan(*keep_args)
        self.assertEqual(empty["IniOverrides"], [])
        self.assertEqual(empty["DatabaseOverrides"], {})

    def test_display_and_ray_options_map_to_expected_engine_values(self):
        plan = self.plan("--mode", "touch", "--touch-setting", "resolution=2560x1440",
                         "--touch-setting", "display-mode=2", "--touch-setting", "render-scale=75",
                         "--touch-setting", "texture=4", "--touch-setting", "dlss-quality=99",
                         "--touch-setting", "ray-quality=3", "--touch-setting", "ray-shadows=1",
                         "--touch-setting", "fps=144", "--touch-setting", "lighting=4",
                         "--touch-setting", "smooth-fps=0", "--touch-setting", "fixed-fps=0")
        values = {k: v for change in plan["IniOverrides"] for k, v in change["Values"].items()}
        self.assertEqual(values["ResolutionSizeX"], "2560")
        self.assertEqual(values["ResolutionSizeY"], "1440")
        self.assertEqual(values["FullscreenMode"], "2")
        self.assertEqual(values["r.ScreenPercentage"], "75")
        self.assertEqual(values["sg.TextureQuality"], "4")
        self.assertEqual(values["r.NGX.DLSS.Quality.Auto"], "1")
        self.assertNotIn("r.NGX.DLSS.Quality", values)
        self.assertEqual(values["r.RayTracing.Enable"], "1")
        self.assertEqual(values["sg.RayTracingQuality"], "3")
        self.assertEqual(values["r.RayTracing.Shadows"], "1")
        self.assertEqual(values["t.MaxFPS"], "144")
        self.assertEqual(values["FrameRateLimit"], "144.000000")
        self.assertEqual(values["r.Kuro.GlobalLightQuality"], "4")
        self.assertEqual(values["bSmoothFrameRate"], "False")
        self.assertEqual(values["bUseFixedFrameRate"], "False")
        self.assertTrue(any("尚未证实" in note for note in plan["Notes"]))

    def test_adaptive_fullscreen_defaults_persists_and_is_touch_only(self):
        self.assertFalse(json.loads(self.cli("config").stdout)["TouchOnly"]["AdaptiveFullscreen"])
        self.cli("save", "--adaptive-fullscreen", "true", "--touch-setting", "resolution=1280x720",
                 "--touch-setting", "display-mode=2")
        saved = json.loads(self.config_path.read_text())
        self.assertTrue(saved["TouchOnly"]["AdaptiveFullscreen"])
        normal = self.plan("--mode", "normal")
        self.assertEqual(normal["IniOverrides"], [])
        self.assertNotIn("-fullscreen", normal["GameArguments"])
        self.cli("save", "--adaptive-fullscreen", "false")
        manual = self.plan("--mode", "touch")
        values = {k: v for change in manual["IniOverrides"] for k, v in change["Values"].items()}
        self.assertEqual((values["FullscreenMode"], values["ResolutionSizeX"], values["ResolutionSizeY"]),
                         ("2", "1280", "720"))
        self.assertFalse(json.loads(self.config_path.read_text())["TouchOnly"]["AdaptiveFullscreen"])

    def test_adaptive_fullscreen_overrides_old_window_size_and_conflicting_arguments(self):
        before = self.config_path.read_bytes()
        plan = self.plan("--mode", "touch", "--adaptive-fullscreen", "true",
                         "--desktop-resolution", "3072x1920",
                         "--touch-setting", "resolution=1280x720", "--touch-setting", "display-mode=2",
                         "--args", '-windowed -borderless -ResX=800 -rEsY=600 -Res=640x480 "-tag=中文 test"')
        values = {k: v for change in plan["IniOverrides"] for k, v in change["Values"].items()}
        for key in ("FullscreenMode", "LastConfirmedFullscreenMode", "PreferredFullscreenMode", "r.FullScreenMode"):
            self.assertEqual(values[key], "0", key)
        for key in ("ResolutionSizeX", "LastUserConfirmedResolutionSizeX"):
            self.assertEqual(values[key], "3072", key)
        for key in ("ResolutionSizeY", "LastUserConfirmedResolutionSizeY"):
            self.assertEqual(values[key], "1920", key)
        arguments = plan["GameArguments"]
        self.assertEqual([a for a in arguments if a.startswith("-Res")], ["-ResX=3072", "-ResY=1920"])
        self.assertEqual(arguments.count("-fullscreen"), 1)
        for unwanted in ("-windowed", "-borderless", "-rEsY=600", "-Res=640x480"):
            self.assertNotIn(unwanted, arguments)
        self.assertIn("-tag=中文 test", arguments)
        self.assertIn("-CloudGame", arguments)
        self.assertEqual(self.config_path.read_bytes(), before)
        self.assertEqual(self.ini.read_bytes(), self.original_ini)
        self.assertFalse((self.root / "argv.json").exists())

    def test_adaptive_fullscreen_rereads_primary_screen_for_each_launch_plan(self):
        bindir = self.root / "display-bin"
        bindir.mkdir()
        output = self.root / "xrandr-output.txt"
        xrandr = bindir / "xrandr"
        xrandr.write_text(f"#!{sys.executable}\nimport sys\nfrom pathlib import Path\n"
                          "assert sys.argv[1:] == ['--current']\n"
                          f"print(Path({str(output)!r}).read_text())\n")
        xrandr.chmod(0o755)
        self.env["PATH"] = str(bindir) + os.pathsep + self.env["PATH"]
        self.env["DISPLAY"] = ":999"
        cases = [
            ("primary-not-desktop-union",
             "Screen 0: current 5360 x 1440\nHDMI-0 connected 1920x1080+0+0\n"
             "DP-1 connected primary 3440x1440+1920+0\n", ("3440", "1440")),
            ("rotated-primary-after-mode-change",
             "HDMI-0 connected 1920x1080+0+0\nDP-1 connected primary 1200x1920-1200+0 left\n",
             ("1200", "1920")),
            ("single-output-without-primary-label",
             "HDMI-0 disconnected\nXWAYLAND0 connected 2560x1600+0+0 (normal)\n",
             ("2560", "1600")),
        ]
        for name, response, expected in cases:
            with self.subTest(case=name):
                output.write_text(response)
                plan = self.plan("--mode", "touch", "--adaptive-fullscreen", "true")
                values = {k: v for change in plan["IniOverrides"] for k, v in change["Values"].items()}
                self.assertEqual((values["ResolutionSizeX"], values["ResolutionSizeY"]), expected)
                self.assertEqual(values["FullscreenMode"], "0")
        output.write_text("DP-1 disconnected\n")
        error = self.cli("launch", "--mode", "touch", "--adaptive-fullscreen", "true", expected=1)
        self.assertIn("未找到正在显示的屏幕", error.stderr)
        self.assertFalse((self.root / "argv.json").exists())

    def test_adaptive_fullscreen_missing_display_does_not_change_game_files(self):
        before = self.config_path.read_bytes()
        user_before = self.user_ini.read_bytes()
        result = self.cli("launch", "--mode", "touch", "--adaptive-fullscreen", "true", expected=1)
        self.assertIn("无法读取主屏幕分辨率", result.stderr)
        self.assertEqual(self.config_path.read_bytes(), before)
        self.assertEqual(self.ini.read_bytes(), self.original_ini)
        self.assertEqual(self.user_ini.read_bytes(), user_before)
        self.assertFalse((self.root / "argv.json").exists())

    def test_adaptive_fullscreen_rejects_invalid_screen_snapshots(self):
        before = self.config_path.read_bytes()
        for name, size in [("zero", "0x1080"), ("negative", "-1x1080"), ("overflow", "65536x1080"),
                           ("missing-height", "1920x"), ("trailing-data", "1920x1080x1")]:
            with self.subTest(case=name):
                self.cli("launch", "--mode", "touch", "--adaptive-fullscreen", "true",
                         "--desktop-resolution", size, expected=1)
                self.assertEqual(self.config_path.read_bytes(), before)
                self.assertEqual(self.ini.read_bytes(), self.original_ini)
                self.assertFalse((self.root / "argv.json").exists())

    def test_adaptive_fullscreen_applies_and_restores_both_display_configurations(self):
        self.user_ini.write_text("[/Script/Engine.GameUserSettings]\nFullscreenMode=2\n"
                                 "LastConfirmedFullscreenMode=2\nPreferredFullscreenMode=1\n"
                                 "ResolutionSizeX=1280\nResolutionSizeY=720\n"
                                 "LastUserConfirmedResolutionSizeX=1280\nLastUserConfirmedResolutionSizeY=720\n")
        user_before = self.user_ini.read_bytes()
        self.config["Linux"]["Environment"]["TEST_HOLD"] = "1"
        self.write_config()
        profile_before = self.config_path.read_bytes()
        process = self.start("--mode", "touch", "--adaptive-fullscreen", "true",
                             "--desktop-resolution", "2376x1080")
        actual = self.user_ini.read_text()
        for line in ("FullscreenMode=0", "LastConfirmedFullscreenMode=0", "PreferredFullscreenMode=0",
                     "ResolutionSizeX=2376", "ResolutionSizeY=1080",
                     "LastUserConfirmedResolutionSizeX=2376", "LastUserConfirmedResolutionSizeY=1080"):
            self.assertIn("\n" + line + "\n", "\n" + actual)
        self.assertIn("\nr.FullScreenMode=0\n", "\n" + self.ini.read_text())
        argv = json.loads((self.root / "argv.json").read_text())
        self.assertIn("-ResX=2376", argv)
        self.assertIn("-ResY=1080", argv)
        self.finish(process)
        self.assertEqual(self.user_ini.read_bytes(), user_before)
        self.assertEqual(self.ini.read_bytes(), self.original_ini)
        self.assertEqual(self.config_path.read_bytes(), profile_before)

    def test_adaptive_fullscreen_overrides_lutris_display_arguments_without_editing_yaml(self):
        lutris = self.root / "adaptive-lutris.yml"
        lutris.write_text(f"game:\n  exe: '{self.game}'\n  prefix: '{self.prefix}'\n"
                          "  args: -windowed -ResX=1280 -ResY=720 -custom=retained\n"
                          f"wine:\n  custom_wine_path: '{self.runner}'\n")
        before = lutris.read_bytes()
        plan = self.plan("--mode", "touch", "--adaptive-fullscreen", "true",
                         "--desktop-resolution", "3440x1440",
                         "--integration", "lutris", "--lutris-config", str(lutris))
        self.assertNotIn("-windowed", plan["GameArguments"])
        self.assertNotIn("-ResX=1280", plan["GameArguments"])
        self.assertIn("-ResX=3440", plan["GameArguments"])
        self.assertIn("-ResY=1440", plan["GameArguments"])
        self.assertIn("-custom=retained", plan["GameArguments"])
        self.assertEqual(lutris.read_bytes(), before)

    def test_graphics_overrides_apply_and_restore_with_custom_preset(self):
        self.config["Linux"]["Environment"]["TEST_HOLD"] = "1"
        self.write_config()
        with database(self.db) as db:
            before = dict(db.execute("SELECT key,value FROM LocalStorage"))
        process = self.start("--mode", "touch", "--quality", "4",
                             "--touch-setting", "shadow=3", "--touch-setting", "volumetric-fog=2",
                             "--touch-setting", "ray-quality=3", "--touch-setting", "ray-gi=1",
                             "--touch-setting", "ray-reflections=1", "--touch-setting", "ray-shadows=1")
        with database(self.db) as db:
            during = dict(db.execute("SELECT key,value FROM LocalStorage"))
        self.assertEqual(during["ImageQuality"], "4")
        self.assertEqual(during["IsUsingQualityPreset"], '"___0B___"')
        for key, value in {"ShadowQuality": "3", "VolumeFog": "2", "RayTracing": "3",
                           "RayTracedGI": "1", "RayTracedReflection": "1", "RayTracedShadow": "1"}.items():
            self.assertEqual(during[key], value)
        self.finish(process)
        with database(self.db) as db:
            self.assertEqual(dict(db.execute("SELECT key,value FROM LocalStorage")), before)
        self.assertEqual(self.ini.read_bytes(), self.original_ini)
        self.assertFalse((self.root / "state/wuwa-touch-launcher/graphics-database-restore.json").exists())

    def test_fps_unlock_is_opt_in_and_isolated_from_normal_mode(self):
        self.assertEqual(self.plan("--mode", "touch")["FpsUnlockTarget"], 0)
        self.assertNotIn("--fps-plugin", self.plan("--mode", "touch")["Command"])
        normal = self.plan("--mode", "normal", "--touch-setting", "unlock-fps=240")
        self.assertEqual(normal["FpsUnlockTarget"], 0)
        self.assertNotIn("--fps-plugin", normal["Command"])
        self.assertEqual(normal["IniOverrides"], [])
        self.cli("save", "--touch-setting", "unlock-fps=999", expected=1)

    def test_memory_mixed_defaults_to_file_persists_and_excludes_normal_mode(self):
        self.cli("save")
        self.assertEqual(json.loads(self.config_path.read_text())["TouchOnly"]["MemoryMode"], "mixed")
        off = self.plan("--mode", "touch", "--touch-setting", "dlss=1")
        self.assertFalse(off["MemoryGraphicsEnabled"])
        self.assertEqual(off["MemoryGraphicsOverrides"], {})
        self.assertNotIn("--memory-graphics", off["Command"])
        self.cli("save", "--memory-injection", "true", "--touch-setting", "dlss=1")
        self.assertEqual(json.loads(self.config_path.read_text())["TouchOnly"]["MemoryMode"], "memory")
        normal = self.plan("--mode", "normal")
        self.assertFalse(normal["MemoryGraphicsEnabled"])
        self.assertEqual(normal["MemoryGraphicsOverrides"], {})
        self.assertNotIn("--memory-graphics", normal["Command"])
        self.assertEqual(normal["IniOverrides"], [])
        self.assertEqual(normal["DatabaseOverrides"], {})
        self.cli("save", "--memory-injection", "false")
        self.assertEqual(json.loads(self.config_path.read_text())["TouchOnly"]["MemoryMode"], "file")

    def test_memory_mixed_selects_only_checked_items_and_retains_selections(self):
        self.config["TouchOnly"]["Values"] = {"dlss": "1", "shadow": "3", "bloom": "keep", "eye-mode": "2"}
        self.write_config()
        self.cli("save", "--memory-mode", "mixed", "--memory-setting", "dlss=true",
                 "--memory-setting", "eye-mode=true", "--memory-setting", "bloom=true")
        mixed = self.plan("--mode", "touch")
        self.assertEqual(mixed["MemoryGraphicsOverrides"], {"r.NGX.DLSS.Enable": 1})
        self.assertEqual(mixed["MemoryGraphicsUnavailable"], ["eye-mode"])
        self.assertEqual(mixed["DatabaseOverrides"]["ShadowQuality"], "3")
        self.assertEqual(mixed["Command"].count("--graphics-unavailable"), 1)
        self.cli("save", "--memory-mode", "file")
        file_plan = self.plan("--mode", "touch")
        self.assertFalse(file_plan["MemoryGraphicsEnabled"])
        self.assertEqual(file_plan["MemoryGraphicsOverrides"], {})
        self.assertEqual(file_plan["MemoryGraphicsUnavailable"], [])
        self.cli("save", "--memory-mode", "memory")
        all_plan = self.plan("--mode", "touch")
        self.assertEqual(all_plan["MemoryGraphicsOverrides"], {"r.NGX.DLSS.Enable": 1, "sg.ShadowQuality": 3})
        self.cli("save", "--memory-mode", "mixed")
        self.assertEqual(self.plan("--mode", "touch")["MemoryGraphicsOverrides"], mixed["MemoryGraphicsOverrides"])
        self.assertEqual(json.loads(self.config_path.read_text())["TouchOnly"]["MemoryOptions"],
                         {"dlss": True, "eye-mode": True, "bloom": True})
        self.cli("save", "--memory-setting", "dlss=false", "--memory-setting", "eye-mode=false")
        self.assertEqual(self.plan("--mode", "touch")["MemoryGraphicsOverrides"], {})
        self.assertEqual(self.plan("--mode", "normal")["MemoryGraphicsUnavailable"], [])
        for key in ("display-mode", "resolution", "unknown"):
            with self.subTest(key=key):
                self.cli("save", "--memory-setting", key + "=true", expected=1)
        self.cli("save", "--memory-mode", "invalid", expected=1)
        self.cli("save", "--memory-setting", "fps=perhaps", expected=1)

    def test_mixed_memory_does_not_fight_unchecked_fps_unlock_plugin(self):
        self.config["TouchOnly"]["Values"] = {"unlock-fps":"240", "fps":"45", "vsync":"1"}
        self.write_config()
        only_fps = self.plan("--mode", "touch", "--memory-mode", "mixed", "--memory-setting", "fps=true")
        self.assertEqual(only_fps["MemoryGraphicsOverrides"], {"t.MaxFPS":240})
        self.assertEqual(only_fps["FpsUnlockTarget"], 240)
        both = self.plan("--mode", "touch", "--memory-mode", "mixed", "--memory-setting", "fps=true",
                         "--memory-setting", "vsync=true")
        self.assertEqual(both["MemoryGraphicsOverrides"], {"t.MaxFPS":240, "r.VSync":0, "rhi.SyncInterval":0})
        untouched = self.plan("--mode", "touch", "--memory-mode", "mixed")
        self.assertEqual(untouched["MemoryGraphicsOverrides"], {})
        self.assertFalse(untouched["MemoryGraphicsEnabled"])
        self.assertIn("--fps-plugin", untouched["Command"])

    def test_memory_legacy_boolean_migration_and_explicit_mode_precedence(self):
        for legacy, expected in ((True, "memory"), (False, "mixed")):
            with self.subTest(legacy=legacy):
                self.config["TouchOnly"]["MemoryInjection"] = legacy
                self.write_config()
                self.assertEqual(json.loads(self.cli("config").stdout)["TouchOnly"]["MemoryMode"], expected)
                self.cli("save")
                self.assertNotIn("MemoryInjection", json.loads(self.config_path.read_text())["TouchOnly"])
        for first in (True, False):
            self.config["TouchOnly"] = dict(
                [("MemoryMode", "file"), ("MemoryInjection", True)] if first else
                [("MemoryInjection", True), ("MemoryMode", "file")])
            self.write_config()
            self.assertEqual(json.loads(self.cli("config").stdout)["TouchOnly"]["MemoryMode"], "file")

    def test_all_54_memory_choices_produce_a_request_or_explicit_skip(self):
        cases = {
            "unlock-fps":"240", "fps":"45", "vsync":"0", "render-scale":"150",
            "smooth-fps":"0", "fixed-fps":"0", "auto-quality":"1", "shadow":"3",
            "effects":"2", "detail":"3", "texture":"4", "anti-aliasing":"1",
            "anisotropy":"16", "view-distance":"2", "npc-density":"2", "vegetation":"3",
            "vegetation-dither":"1", "dynamic-bones":"1", "environment-interaction":"1",
            "teammate-effects":"1", "hit-material":"1", "damage-effects":"1", "lighting":"4",
            "ambient-occlusion":"3", "volumetric-fog":"3", "volumetric-light":"1", "bloom":"1",
            "motion-blur":"3", "auto-exposure":"1", "dlss":"1", "dlss-quality":"99",
            "dlss-sharpness":"-0.3", "native-fg":"5", "fsr":"1", "fsr-quality":"-2",
            "fsr-fg":"1", "fsr-legacy":"1", "xess":"1", "xess-quality":"4", "xess-fg":"3",
            "ray-tracing":"1", "ray-quality":"3", "ray-gi":"1", "ray-reflections":"1",
            "ray-shadows":"1", "brightness":"-0.8", "saturation":"68", "contrast":"32",
            "image-display":"2", "eye-mode":"2", "eye-temperature":"6500", "eye-strength":"0.8",
            "eye-brightness":"0.5", "eye-texture":"0.5"
        }
        unavailable = {"smooth-fps", "fixed-fps", "npc-density", "vegetation-dither", "dynamic-bones",
                       "teammate-effects", "hit-material", "damage-effects", "eye-mode", "eye-temperature",
                       "eye-strength", "eye-brightness", "eye-texture"}
        self.assertEqual(len(cases), 54)
        self.config["TouchOnly"]["Values"] = {}
        self.write_config()
        for key, value in cases.items():
            with self.subTest(key=key):
                plan = self.plan("--mode", "touch", "--memory-mode", "mixed",
                                 "--memory-setting", key + "=true", "--touch-setting", key + "=" + value)
                self.assertTrue(plan["MemoryGraphicsEnabled"])
                if key in unavailable:
                    self.assertEqual(plan["MemoryGraphicsUnavailable"], [key])
                    self.assertEqual(plan["MemoryGraphicsOverrides"], {})
                    self.assertIn("--graphics-unavailable", plan["Command"])
                else:
                    self.assertEqual(plan["MemoryGraphicsUnavailable"], [])
                    self.assertGreater(len(plan["MemoryGraphicsOverrides"]), 0)
                    self.assertEqual(plan["Command"].count("--graphics-set"), len(plan["MemoryGraphicsOverrides"]))
                keep = self.plan("--mode", "touch", "--memory-mode", "mixed",
                                 "--memory-setting", key + "=true", "--touch-setting", key + "=keep")
                self.assertEqual(keep["MemoryGraphicsOverrides"], {})
                self.assertEqual(keep["MemoryGraphicsUnavailable"], [])

    def test_memory_fsr_xess_quality_and_frame_generation_requests(self):
        self.config["TouchOnly"]["Values"] = {}
        self.write_config()
        for key, name, cases in [
            ("fsr-quality", "r.FidelityFX.FSR3.QualityMode", [(-2,4),(-1,3),(0,2),(1,1),(2,0)]),
            ("xess-quality", "r.XeSS.Quality", [(-2,0),(-1,1),(0,2),(1,3),(2,4),(3,5),(4,6)]),
            ("fsr-fg", "r.FidelityFX.FI.Enabled", [(0,0),(1,1)])]:
            for option, target in cases:
                with self.subTest(key=key, value=option):
                    plan = self.plan("--mode", "touch", "--memory-mode", "mixed",
                                     "--memory-setting", key + "=true", "--touch-setting", f"{key}={option}")
                    self.assertEqual(plan["MemoryGraphicsOverrides"], {name: target})
        for value in range(4):
            plan = self.plan("--mode", "touch", "--memory-mode", "memory", "--touch-setting", f"xess-fg={value}")
            expected = {"r.XeFG.Enabled": int(value != 0)}
            if value:
                expected["r.XeFG.MaxInterpolatedFrames"] = value
            self.assertEqual(plan["MemoryGraphicsOverrides"], expected)

    def test_memory_injection_verified_quality_modes_and_boolean_mapping(self):
        self.config["TouchOnly"]["Values"] = {}
        self.write_config()
        for quality in ("-2", "-1", "0", "1", "3", "99"):
            with self.subTest(quality=quality):
                plan = self.plan("--mode", "touch", "--memory-injection", "true",
                                 "--touch-setting", "dlss=1", "--touch-setting", "dlss-quality=" + quality,
                                 "--touch-setting", "anti-aliasing=1")
                expected = {"r.NGX.DLSS.Enable": 1, "r.DefaultFeature.AntiAliasing": 2,
                            "r.NGX.DLSS.Quality.Auto": 1 if quality == "99" else 0}
                if quality != "99":
                    expected["r.NGX.DLSS.Quality"] = int(quality)
                self.assertEqual(plan["MemoryGraphicsOverrides"], expected)
                for name, value in expected.items():
                    self.assertIn(name + "=" + str(value), plan["Command"])
                self.assertEqual(plan["Command"].count("--graphics-set"), len(expected))
        off = self.plan("--mode", "touch", "--memory-injection", "true",
                        "--touch-setting", "dlss=0", "--touch-setting", "anti-aliasing=0")
        self.assertEqual(off["MemoryGraphicsOverrides"],
                         {"r.NGX.DLSS.Enable": 0, "r.DefaultFeature.AntiAliasing": 0})

    def test_memory_experimental_mappings_keep_configuration_preload(self):
        self.config["TouchOnly"]["Values"] = {}
        self.write_config()
        plan = self.plan("--mode", "touch", "--memory-injection", "true",
                         "--touch-setting", "dlss=keep", "--touch-setting", "dlss-quality=keep",
                         "--touch-setting", "anti-aliasing=keep", "--touch-setting", "native-fg=keep",
                         "--touch-setting", "shadow=3", "--touch-setting", "detail=3",
                         "--touch-setting", "vegetation=3")
        self.assertTrue(plan["MemoryGraphicsEnabled"])
        self.assertEqual(plan["MemoryGraphicsOverrides"], {"sg.ShadowQuality": 3, "r.DetailMode": 3, "foliage.DensityScale": 1})
        self.assertEqual(plan["Command"].count("--graphics-set"), 3)
        for key, value in {"ShadowQuality": "3",
                           "ImageDetail": "3", "VegetationDensity": "3"}.items():
            self.assertEqual(plan["DatabaseOverrides"][key], value)

    def test_memory_frame_generation_maps_all_dlss_multipliers(self):
        self.config["TouchOnly"]["Values"] = {}
        self.write_config()
        for value in ("0", "1", "2", "3", "4", "5", "keep"):
            with self.subTest(value=value):
                plan = self.plan("--mode", "touch", "--memory-injection", "true",
                                 "--touch-setting", "native-fg=" + value)
                expected = {} if value == "keep" else {"r.Streamline.DLSSG.Enable": 0 if value == "0" else 1}
                if value not in ("keep", "0"):
                    expected["r.Streamline.DLSSG.FramesToGenerate"] = int(value)
                self.assertEqual(plan["MemoryGraphicsOverrides"], expected)
                if value != "keep":
                    self.assertEqual(plan["DatabaseOverrides"]["NvidiaSuperSamplingFrameGenerate"], value)
                else:
                    self.assertNotIn("NvidiaSuperSamplingFrameGenerate", plan["DatabaseOverrides"])

    def test_memory_explicit_numeric_controls_and_unlock_precedence(self):
        self.config["TouchOnly"]["Values"] = {}
        self.write_config()
        options = {"fps": "45", "vsync": "1", "render-scale": "150", "anisotropy": "16",
                   "lighting": "4", "dlss-sharpness": "-0.3"}
        args = [arg for key, value in options.items() for arg in ("--touch-setting", key + "=" + value)]
        plan = self.plan("--mode", "touch", "--memory-injection", "true", *args)
        expected = {"t.MaxFPS": 45, "r.VSync": 1, "rhi.SyncInterval": 1, "r.ScreenPercentage": 150,
                    "r.MaxAnisotropy": 16, "r.Kuro.GlobalLightQuality": 4, "r.NGX.DLSS.Sharpness": -0.3}
        self.assertEqual(plan["MemoryGraphicsOverrides"], expected)
        self.assertIn("r.NGX.DLSS.Sharpness=-0.3", plan["Command"])
        self.assertEqual(plan["Command"].count("--graphics-set"), 7)
        unlocked = self.plan("--mode", "touch", "--memory-injection", "true", *args,
                             "--touch-setting", "unlock-fps=240")
        self.assertEqual(unlocked["MemoryGraphicsOverrides"], expected | {
            "t.MaxFPS": 240, "r.VSync": 0, "rhi.SyncInterval": 0})
        self.assertEqual(unlocked["FpsUnlockTarget"], 240)

    def test_memory_post_processing_matches_native_menu_levels(self):
        self.config["TouchOnly"]["Values"] = {}
        self.write_config()
        cases = [
            ("ambient-occlusion", "0", {"r.AmbientOcclusionLevels": 0, "r.DistanceFieldAO": 0}),
            ("ambient-occlusion", "1", {"r.AmbientOcclusionLevels": -1, "r.DistanceFieldAO": 0}),
            ("ambient-occlusion", "2", {"r.AmbientOcclusionLevels": -1, "r.DistanceFieldAO": 1, "r.DistanceFieldAOQuality": 2}),
            ("ambient-occlusion", "3", {"r.AmbientOcclusionLevels": -1, "r.DistanceFieldAO": 1, "r.DistanceFieldAOQuality": 3}),
            ("volumetric-fog", "0", {"r.VolumetricFog": 0}),
            ("volumetric-fog", "1", {"r.VolumetricFog": 1, "r.Kuro.VRS.VolumeCloudQuality": 1}),
            ("volumetric-fog", "2", {"r.VolumetricFog": 1, "r.Kuro.VRS.VolumeCloudQuality": 2}),
            ("volumetric-fog", "3", {"r.VolumetricFog": 1, "r.Kuro.VRS.VolumeCloudQuality": 3}),
            ("volumetric-light", "0", {"r.LightShaftQuality": 0}),
            ("volumetric-light", "1", {"r.LightShaftQuality": 1}),
            ("motion-blur", "0", {"r.MotionBlur.Amount": 0}),
            ("motion-blur", "1", {"r.MotionBlur.Amount": 0.25}),
            ("motion-blur", "2", {"r.MotionBlur.Amount": 0.5}),
            ("motion-blur", "3", {"r.MotionBlur.Amount": 0.75}),
            ("bloom", "0", {"r.Kuro.KuroBloomEnable": 0}),
            ("bloom", "1", {"r.Kuro.KuroBloomEnable": 1}),
            ("auto-exposure", "0", {"r.Kuro.AutoExposurePlayerCustom": 0}),
            ("auto-exposure", "1", {"r.Kuro.AutoExposurePlayerCustom": 1}),
            ("image-display", "0", {"r.Kuro.KuroEnableScreenFilter": 0, "r.BlueLightFilter.Disable": 1,
                                    "r.Tonemapper.BrightnessAndTextureDisable": 1}),
            ("image-display", "1", {"r.Kuro.KuroEnableScreenFilter": 1, "r.BlueLightFilter.Disable": 1,
                                    "r.Tonemapper.BrightnessAndTextureDisable": 1}),
            ("image-display", "2", {"r.Kuro.KuroEnableScreenFilter": 0, "r.BlueLightFilter.Disable": 0,
                                    "r.Tonemapper.BrightnessAndTextureDisable": 0})]
        database_keys = {"ambient-occlusion": "SceneAo", "volumetric-fog": "VolumeFog",
                         "volumetric-light": "VolumeLight", "motion-blur": "MotionBlur", "bloom": "BloomEnable",
                         "auto-exposure": "AutoExposure", "image-display": "ImageDisplayMode"}
        for key, value, expected in cases:
            with self.subTest(key=key, value=value):
                plan = self.plan("--mode", "touch", "--memory-injection", "true", "--touch-setting", key + "=" + value)
                self.assertEqual(plan["MemoryGraphicsOverrides"], expected)
                self.assertEqual(plan["Command"].count("--graphics-set"), len(expected))
                self.assertEqual(plan["DatabaseOverrides"], {database_keys[key]: value})
        keep = [arg for key in {row[0] for row in cases} for arg in ("--touch-setting", key + "=keep")]
        self.assertEqual(self.plan("--mode", "touch", "--memory-injection", "true", *keep)["MemoryGraphicsOverrides"], {})

    def test_memory_injection_launch_keeps_existing_restore_contract(self):
        self.config["Linux"]["Environment"]["TEST_HOLD"] = "1"
        self.write_config()
        before_profile = self.config_path.read_bytes()
        before_opti = self.opti.read_bytes()
        with database(self.db) as db:
            before_db = dict(db.execute("SELECT key,value FROM LocalStorage"))
        process = self.start("--mode", "touch", "--memory-injection", "true",
                             "--touch-setting", "dlss=1", "--touch-setting", "dlss-quality=0",
                             "--touch-setting", "anti-aliasing=0")
        args = json.loads((self.root / "argv.json").read_text())
        self.assertIn("--memory-graphics", args)
        self.assertIn("r.NGX.DLSS.Quality=0", args)
        self.finish(process)
        self.assertEqual(self.config_path.read_bytes(), before_profile)
        self.assertEqual(self.ini.read_bytes(), self.original_ini)
        self.assertEqual(self.opti.read_bytes(), before_opti)
        with database(self.db) as db:
            self.assertEqual(dict(db.execute("SELECT key,value FROM LocalStorage")), before_db)

    def test_fps_unlock_current_database_and_ini_restore(self):
        self.config["Linux"]["Environment"]["TEST_HOLD"] = "1"
        self.write_config()
        with database(self.db) as db:
            db.execute("UPDATE LocalStorage SET value='1' WHERE key='PcVsync'")
            before = dict(db.execute("SELECT key,value FROM LocalStorage"))
        plan = self.plan("--mode", "touch", "--touch-setting", "unlock-fps=240",
                         "--touch-setting", "fps=60", "--touch-setting", "vsync=1")
        self.assertEqual(plan["FpsUnlockTarget"], 240)
        self.assertEqual(plan["Command"][plan["Command"].index("--fps-target") + 1], "240")
        self.assertTrue(plan["Command"][plan["Command"].index("--fps-plugin") + 1].endswith("ww_ulk_plugin_base.dll"))
        process = self.start("--mode", "touch", "--touch-setting", "unlock-fps=240",
                             "--touch-setting", "fps=60", "--touch-setting", "vsync=1")
        with database(self.db) as db:
            during = dict(db.execute("SELECT key,value FROM LocalStorage"))
        self.assertEqual(during["CustomFrameRate"], "3")
        self.assertEqual(during["PcVsync"], "0")
        self.assertNotIn("GameQualitySetting", during)
        self.assertIn("t.MaxFPS=240", self.ini.read_text())
        self.assertIn("bUseVSync=False", self.user_ini.read_text())
        self.finish(process)
        with database(self.db) as db:
            self.assertEqual(dict(db.execute("SELECT key,value FROM LocalStorage")), before)
        self.assertEqual(self.ini.read_bytes(), self.original_ini)
        self.assertFalse((self.root / "state/wuwa-touch-launcher/fps-database-restore.json").exists())

    def test_fps_unlock_legacy_database_preserves_in_game_edits(self):
        self.config["Linux"]["Environment"]["TEST_HOLD"] = "1"
        self.write_config()
        old = {"KeyCustomFrameRate": 60, "KeyPcVsync": 1, "Other": 7}
        with database(self.db) as db:
            db.execute("INSERT INTO LocalStorage VALUES('GameQualitySetting',?)", (json.dumps(old),))
        process = self.start("--mode", "touch", "--touch-setting", "unlock-fps=420")
        with database(self.db) as db:
            value = json.loads(db.execute("SELECT value FROM LocalStorage WHERE key='GameQualitySetting'").fetchone()[0])
            self.assertEqual(value, {"KeyCustomFrameRate": 420, "KeyPcVsync": 0, "Other": 7})
            value["Other"] = 9
            db.execute("UPDATE LocalStorage SET value=? WHERE key='GameQualitySetting'", (json.dumps(value),))
            db.execute("UPDATE LocalStorage SET value='1' WHERE key='CustomFrameRate'")
        self.finish(process)
        with database(self.db) as db:
            after = dict(db.execute("SELECT key,value FROM LocalStorage"))
        self.assertEqual(json.loads(after["GameQualitySetting"]), old | {"Other": 9})
        self.assertEqual(after["CustomFrameRate"], "1")

    def test_fps_unlock_missing_schema_does_not_start_or_change_settings(self):
        with database(self.db) as db:
            db.execute("DELETE FROM LocalStorage WHERE key='CustomFrameRate'")
            before = dict(db.execute("SELECT key,value FROM LocalStorage"))
        self.cli("launch", "--mode", "touch", "--touch-setting", "unlock-fps=120", expected=1)
        self.assertFalse((self.root / "pid").exists())
        with database(self.db) as db:
            self.assertEqual(dict(db.execute("SELECT key,value FROM LocalStorage")), before)
        self.assertEqual(self.ini.read_bytes(), self.original_ini)

    def test_fps_unlock_legacy_numeric_formats_and_later_edits(self):
        from decimal import Decimal
        self.config["Linux"]["Environment"]["TEST_HOLD"] = "1"
        self.write_config()
        old = '{"KeyCustomFrameRate":60,"KeyPcVsync":1,"Other":7}'
        cases = [
            ("4.2e2", "0.000e900", 60, 1),
            ("420.0000000000000000000000000001", "1e-100",
             Decimal("420.0000000000000000000000000001"), Decimal("1e-100")),
            ('"manual"', "false", "manual", False),
        ]
        for fps, vsync, expected_fps, expected_vsync in cases:
            with self.subTest(fps=fps, vsync=vsync):
                (self.root / "finish").unlink(missing_ok=True)
                (self.root / "pid").unlink(missing_ok=True)
                with database(self.db) as db:
                    db.execute("INSERT OR REPLACE INTO LocalStorage VALUES('GameQualitySetting',?)", (old,))
                process = self.start("--mode", "touch", "--touch-setting", "unlock-fps=420")
                current = '{"KeyCustomFrameRate":' + fps + ',"KeyPcVsync":' + vsync + ',"Other":9}'
                with database(self.db) as db:
                    db.execute("UPDATE LocalStorage SET value=? WHERE key='GameQualitySetting'", (current,))
                self.finish(process)
                with database(self.db) as db:
                    restored = json.loads(db.execute("SELECT value FROM LocalStorage WHERE key='GameQualitySetting'").fetchone()[0],
                                          parse_float=Decimal)
                self.assertEqual(restored, {"KeyCustomFrameRate": expected_fps, "KeyPcVsync": expected_vsync, "Other": 9})

    def test_fps_unlock_crash_journal_is_recovered_before_next_normal_launch(self):
        state = self.root / "state/wuwa-touch-launcher"
        state.mkdir(parents=True, exist_ok=True)
        with database(self.db) as db:
            db.execute("UPDATE LocalStorage SET value='3' WHERE key='CustomFrameRate'")
        journal = state / "fps-database-restore.json"
        journal.write_text(json.dumps({"Database": str(self.db), "Values": [
            {"Key": "CustomFrameRate", "Original": "2", "Applied": "3"},
            {"Key": "PcVsync", "Original": "1", "Applied": "0"}]}))
        self.cli("launch", "--mode", "normal")
        with database(self.db) as db:
            after = dict(db.execute("SELECT key,value FROM LocalStorage"))
        self.assertEqual(after["CustomFrameRate"], "2")
        self.assertEqual(after["PcVsync"], "1")
        self.assertFalse(journal.exists())

    def test_graphics_restore_preserves_individual_in_game_edits(self):
        self.config["Linux"]["Environment"]["TEST_HOLD"] = "1"
        self.write_config()
        process = self.start("--mode", "touch", "--touch-setting", "shadow=3", "--touch-setting", "volumetric-fog=2")
        with database(self.db) as db:
            db.execute("UPDATE LocalStorage SET value='1' WHERE key='ShadowQuality'")
        self.finish(process)
        with database(self.db) as db:
            after = dict(db.execute("SELECT key,value FROM LocalStorage"))
        self.assertEqual(after["ShadowQuality"], "1")
        self.assertEqual(after["VolumeFog"], "0")
        self.assertEqual(after["IsUsingQualityPreset"], '"___0B___"')

    def test_graphics_restore_keeps_new_in_game_preset(self):
        self.config["Linux"]["Environment"]["TEST_HOLD"] = "1"
        self.write_config()
        process = self.start("--mode", "touch", "--quality", "4", "--touch-setting", "shadow=0",
                             "--touch-setting", "volumetric-fog=0")
        with database(self.db) as db:
            db.execute("UPDATE LocalStorage SET value='3' WHERE key IN ('ImageQuality', 'ShadowQuality', 'VolumeFog')")
            db.execute("UPDATE LocalStorage SET value=? WHERE key='IsUsingQualityPreset'", ('"___1B___"',))
        self.finish(process)
        with database(self.db) as db:
            after = dict(db.execute("SELECT key,value FROM LocalStorage"))
        for key in ("ImageQuality", "ShadowQuality", "VolumeFog"):
            self.assertEqual(after[key], "3")
        self.assertEqual(after["IsUsingQualityPreset"], '"___1B___"')

    def test_ray_switch_disables_saved_quality_and_restores_it(self):
        self.config["Linux"]["Environment"]["TEST_HOLD"] = "1"
        self.write_config()
        with database(self.db) as db:
            db.execute("UPDATE LocalStorage SET value='3' WHERE key='RayTracing'")
        process = self.start("--mode", "touch", "--touch-setting", "ray-tracing=0")
        self.assertIn("r.RayTracing.Enable=0", self.ini.read_text())
        with database(self.db) as db:
            self.assertEqual(db.execute("SELECT value FROM LocalStorage WHERE key='RayTracing'").fetchone()[0], "0")
        self.finish(process)
        with database(self.db) as db:
            self.assertEqual(db.execute("SELECT value FROM LocalStorage WHERE key='RayTracing'").fetchone()[0], "3")

    def test_missing_graphics_key_rolls_back_before_start(self):
        with database(self.db) as db:
            db.execute("DELETE FROM LocalStorage WHERE key='RayTracedGI'")
            before = dict(db.execute("SELECT key,value FROM LocalStorage"))
        result = self.cli("launch", "--mode", "touch", "--quality", "4",
                          "--touch-setting", "shadow=3", "--touch-setting", "ray-gi=1", expected=1)
        self.assertIn("RayTracedGI", result.stderr)
        self.assertFalse((self.root / "pid").exists())
        self.assertEqual(self.ini.read_bytes(), self.original_ini)
        with database(self.db) as db:
            self.assertEqual(dict(db.execute("SELECT key,value FROM LocalStorage")), before)

    def test_graphics_recovery_precedes_preset_recovery_after_crash(self):
        state = self.root / "state/wuwa-touch-launcher"
        state.mkdir(parents=True)
        with database(self.db) as db:
            db.execute("UPDATE LocalStorage SET value='4' WHERE key='ImageQuality'")
            db.execute("UPDATE LocalStorage SET value='3' WHERE key='ShadowQuality'")
        (state / "quality-restore.json").write_text(json.dumps({
            "Database": str(self.db), "Original": "2", "Applied": "4",
            "OriginalPresetFlag": '"___0B___"', "AppliedPresetFlag": '"___1B___"'}))
        (state / "graphics-database-restore.json").write_text(json.dumps({
            "Database": str(self.db), "Values": [
                {"Key": "ShadowQuality", "Original": "0", "Applied": "3"},
                {"Key": "IsUsingQualityPreset", "Original": '"___1B___"', "Applied": '"___0B___"'}]}))
        self.cli("launch", "--mode", "normal")
        with database(self.db) as db:
            after = dict(db.execute("SELECT key,value FROM LocalStorage"))
        self.assertEqual(after["ImageQuality"], "2")
        self.assertEqual(after["ShadowQuality"], "0")
        self.assertEqual(after["IsUsingQualityPreset"], '"___0B___"')
        self.assertFalse((state / "quality-restore.json").exists())
        self.assertFalse((state / "graphics-database-restore.json").exists())

    def test_graphics_conflicts_and_invalid_ranges_are_rejected(self):
        before = self.config_path.read_bytes()
        cases = [
            ("shadow-upper-bound", ["--touch-setting", "shadow=4"]),
            ("fog-negative", ["--touch-setting", "volumetric-fog=-1"]),
            ("dlss-invalid-quality", ["--touch-setting", "dlss-quality=2"]),
            ("unknown-resolution", ["--touch-setting", "resolution=0x0"]),
            ("simultaneous-upscalers", ["--touch-setting", "dlss=1", "--touch-setting", "fsr=1"]),
            ("ray-switch-conflict", ["--touch-setting", "ray-tracing=0", "--touch-setting", "ray-gi=1"]),
            ("ray-quality-conflict", ["--touch-setting", "ray-quality=0", "--touch-setting", "ray-shadows=1"]),
        ]
        for name, args in cases:
            with self.subTest(case=name):
                self.cli("save", *args, expected=1)
                self.assertEqual(self.config_path.read_bytes(), before)
        self.cli("preview", "--mode", "touch", "--dx", "dx11", "--touch-setting", "ray-gi=1", expected=1)
        self.cli("preview", "--mode", "touch", "--dx", "dx11", "--touch-setting", "ray-quality=0")

    def test_shortcuts_launch_without_gui_and_read_latest_saved_mode_settings(self):
        directory = self.root / "shortcuts"
        self.cli("shortcuts", "--shortcut-dir", str(directory))
        for mode in ("normal", "touch"):
            text = (directory / f"wuwa-{mode}.desktop").read_text()
            self.assertIn("Terminal=false", text)
            self.assertIn('"launch"', text)
            self.assertIn(f'"{mode}"', text)
            self.assertNotIn("--resources", text)
        self.cli("save", "--mode", "touch", "--resources", "uhd")
        launched = subprocess.run(["gio", "launch", str(directory / "wuwa-touch.desktop")],
                                  env=self.env, capture_output=True, text=True, timeout=10)
        self.assertEqual(launched.returncode, 0, launched.stdout + launched.stderr)
        deadline = time.monotonic() + 8
        while not (self.root / "argv.json").exists() and time.monotonic() < deadline:
            time.sleep(0.05)
        args = json.loads((self.root / "argv.json").read_text())
        self.assertIn("-krqlv=uhd", args)
        self.assertIn("-CloudGame", args)


if __name__ == "__main__":
    unittest.main(verbosity=2)
