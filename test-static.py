#!/usr/bin/env python3
"""Validate the static CLI in an empty root filesystem, including real SQLite I/O."""
import json
import os
from pathlib import Path
import shutil
import sqlite3
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent
BINARY = ROOT / "dist/linux-static-cli/wuwa-touch-cli"

class StaticContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.build = tempfile.TemporaryDirectory(prefix="wuwa-static-test-")
        cls.context = Path(cls.build.name)
        cls.image = f"wuwa-static-test:{os.getpid()}"
        shutil.copy2(BINARY, cls.context / "wuwa-touch-cli")
        shutil.copy2(BINARY.parent / "touch-bridge.exe", cls.context)
        subprocess.run([
            "docker", "run", "--rm", "--network", "none", "--user", f"{os.getuid()}:{os.getgid()}",
            "-v", f"{ROOT / 'tests/static-runner.c'}:/input.c:ro",
            "-v", f"{cls.context}:/output", "wuwa-static-build:10.0.402",
            "clang", "-O2", "-static", "/input.c", "/usr/lib/libsqlite3.a", "-lm", "-lpthread",
            "-o", "/output/test-runner"], check=True, capture_output=True)
        (cls.context / "Dockerfile").write_text(
            'FROM scratch\nCOPY wuwa-touch-cli touch-bridge.exe test-runner /\n'
            'ENTRYPOINT ["/wuwa-touch-cli"]\n')
        subprocess.run(["docker", "build", "-t", cls.image, str(cls.context)],
                       check=True, capture_output=True)

    @classmethod
    def tearDownClass(cls):
        subprocess.run(["docker", "image", "rm", cls.image], check=True, capture_output=True)
        cls.build.cleanup()

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="wuwa-static-fixture-")
        self.root = Path(self.temp.name)
        self.config_path = self.root / "profiles.json"
        self.client = self.root / "Client"
        game = self.client / "Binaries/Win64/Client-Win64-Shipping.exe"
        game.parent.mkdir(parents=True)
        game.write_bytes(b"test fixture")
        (self.root / "prefix").mkdir()
        self.ini = self.client / "Saved/Config/WindowsNoEditor/Engine.ini"
        self.ini.parent.mkdir(parents=True)
        self.ini.write_text("[SystemSettings]\nt.MaxFPS=60\n")
        self.db_path = self.client / "Saved/LocalStorage/LocalStorage.db"
        self.db_path.parent.mkdir(parents=True)
        with sqlite3.connect(self.db_path) as db:
            db.execute("CREATE TABLE LocalStorage (key TEXT PRIMARY KEY, value TEXT)")
            db.executemany("INSERT INTO LocalStorage VALUES (?,?)",
                           [("ShadowQuality", "1"), ("ImageQuality", "2"),
                            ("IsUsingQualityPreset", '"___1B___"')])
        self.config = {
            "Version": 2, "Mode": "normal",
            "GameExecutable": "/fixture/Client/Binaries/Win64/Client-Win64-Shipping.exe",
            "Linux": {"Integration": "manual", "RunnerKind": "wine", "Runner": "/test-runner",
                      "Prefix": "/fixture/prefix"},
            "Normal": {"Resources": "keep", "Quality": "keep", "DirectX": "dx12"},
            "Touch": {"Resources": "keep", "Quality": "keep", "DirectX": "dx12"},
            "TouchOnly": {"Values": {"shadow": "3", "fps": "120"}},
        }
        self.config_path.write_text(json.dumps(self.config))

    def tearDown(self):
        self.temp.cleanup()

    def cli(self, *args, expected=0):
        result = subprocess.run([
            "docker", "run", "--rm", "--network", "none", "--read-only",
            "--user", f"{os.getuid()}:{os.getgid()}",
            "--tmpfs", "/tmp", "-e", "XDG_STATE_HOME=/fixture/state",
            "-e", "XDG_CONFIG_HOME=/fixture/config",
            "-v", f"{self.root}:/fixture", self.image, *args],
            text=True, capture_output=True, timeout=20)
        self.assertEqual(result.returncode, expected, result.stdout + result.stderr)
        return result

    def test_static_elf_has_no_dynamic_loader_or_needed_libraries(self):
        headers = subprocess.check_output(["readelf", "-l", BINARY], text=True)
        dynamic = subprocess.check_output(["readelf", "-d", BINARY], text=True)
        self.assertNotIn("INTERP", headers)
        self.assertNotIn("(NEEDED)", dynamic)
        self.assertIn("静态命令行版", self.cli("--help").stdout)

    def test_empty_root_saves_and_reads_shared_profiles(self):
        self.cli("save", "--config", "/fixture/profiles.json", "--mode", "touch",
                 "--touch-setting", "ray-quality=2", "--hotkey", "Ctrl+Shift+F9")
        config = json.loads(self.cli("config", "--config", "/fixture/profiles.json").stdout)
        self.assertEqual(config["Mode"], "touch")
        self.assertEqual(config["TouchOnly"]["Values"]["ray-quality"], "2")
        self.assertEqual(config["TouchOnly"]["Hotkey"], "Ctrl+Shift+F9")
        self.assertEqual(config["Normal"], self.config["Normal"] | {
            "SkipSplash": True, "CustomArguments": ""})
        plan = json.loads(self.cli("preview", "--config", "/fixture/profiles.json").stdout)
        self.assertEqual(plan["Mode"], "touch")
        self.assertEqual(plan["DatabaseOverrides"]["RayTracing"], "2")

    def test_empty_root_applies_sqlite_overrides_and_restores_after_launch(self):
        before = self.ini.read_bytes()
        self.cli("launch", "--config", "/fixture/profiles.json", "--mode", "touch")
        self.assertEqual((self.root / "observed-shadow.txt").read_text(), "3")
        with sqlite3.connect(self.db_path) as db:
            values = dict(db.execute("SELECT key,value FROM LocalStorage"))
        self.assertEqual(values["ShadowQuality"], "1")
        self.assertEqual(values["IsUsingQualityPreset"], '"___1B___"')
        self.assertEqual(self.ini.read_bytes(), before)
        self.assertFalse((self.root / "state/wuwa-touch-launcher/graphics-database-restore.json").exists())

    def test_static_cli_identifies_gui_limit_and_no_args_shows_help(self):
        self.assertIn("此构建不含 GUI", self.cli().stdout)
        result = self.cli("gui", "--config", "/fixture/profiles.json", expected=1)
        self.assertIn("这是静态命令行版", result.stderr)
        self.assertNotIn("Dynamic loading", result.stderr)

if __name__ == "__main__":
    unittest.main(verbosity=2)
