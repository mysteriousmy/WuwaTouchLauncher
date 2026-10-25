#!/usr/bin/env python3
"""Exercise the published Native AOT launcher with an isolated fake UMU process."""
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

LAUNCHER = Path(__file__).parent / "dist/wuwa-touch-launcher"
FAKE_RUNNER = r'''
import json, os
from pathlib import Path
import time

state = Path(os.environ["XDG_STATE_HOME"]) / "wuwa-touch-launcher"
state.mkdir(parents=True, exist_ok=True)
(state / "runner-pid").write_text(str(os.getpid()))
with (state / "invocations").open("a") as f:
    f.write("started\n")
def write_status(phase):
    data = {"phase": phase, "filtering": phase == "active",
            "targetCount": 1 if phase == "active" else 0, "exitCode": 0}
    temp = state / "status.tmp"
    temp.write_text(json.dumps(data))
    temp.replace(state / "status.json")
write_status("active")
if os.environ["TEST_MODE"] == "hold":
    while not (state / "finish").exists():
        write_status("active")
        time.sleep(0.1)
else:
    time.sleep(0.2)
write_status("game-exited")
# Reproduce Proton helpers keeping the outer runner and stdout/stderr alive.
time.sleep(600)
'''


class LauncherLifecycle(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="wuwa-launcher-test-")
        self.root = Path(self.temp.name)
        self.env = os.environ.copy()
        self.env["XDG_CONFIG_HOME"] = str(self.root / "config")
        self.env["XDG_STATE_HOME"] = str(self.root / "state")
        self.state = self.root / "state/wuwa-touch-launcher"
        config_dir = self.root / "config/wuwa-touch-launcher"
        config_dir.mkdir(parents=True)
        self.config_file = config_dir / "config.json"
        runner = self.root / "fake-runner.py"
        runner.write_text(FAKE_RUNNER)
        game = self.root / "game with spaces 鸣潮.exe"
        bridge = self.root / "bridge.exe"
        game.touch()
        bridge.touch()
        proton = self.root / "proton"
        proton.mkdir()
        (proton / "proton").touch()
        self.config = {
            "GameExecutable": str(game), "BridgeExecutable": str(bridge),
            "RunnerCommand": [sys.executable, str(runner)],
            "GameArguments": ["-SkipSplash"],
            "Environment": {
                "WINEPREFIX": str(self.root), "PROTONPATH": str(proton),
                "TEST_MODE": "complete",
            },
        }
        self.save_config()
        self.parents = []
        self.runners = set()

    def save_config(self):
        self.config_file.write_text(json.dumps(self.config))

    def remember_runner(self):
        p = self.state / "runner-pid"
        if p.exists():
            self.runners.add(int(p.read_text()))

    def start(self, *args):
        p = subprocess.Popen(
            [str(LAUNCHER), "legacy", "launch", *args], env=self.env,
            stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True,
        )
        self.parents.append(p)
        return p

    def wait_active(self):
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            status = self.state / "status.json"
            if status.exists() and json.loads(status.read_text())["phase"] == "active":
                self.remember_runner()
                return
            time.sleep(0.05)
        self.fail("Fake runner never became active")

    def tearDown(self):
        self.remember_runner()
        for p in self.parents:
            if p.poll() is None:
                p.kill()
            p.communicate(timeout=5)
        for pid in self.runners:
            try:
                os.kill(pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
        self.temp.cleanup()

    def test_terminal_bridge_releases_lock_while_runner_alive(self):
        first = self.start()
        out, err = first.communicate(timeout=5)
        self.remember_runner()
        self.assertEqual(first.returncode, 0, out + err)
        self.assertIn("game-exited", out)
        for pid in self.runners:
            os.kill(pid, 0)  # The regression requires the outer runner to still exist.
        second = self.start()
        out, err = second.communicate(timeout=5)
        self.remember_runner()
        self.assertEqual(second.returncode, 0, out + err)
        self.assertEqual((self.state / "invocations").read_text().count("started"), 2)

    def test_active_bridge_rejects_duplicate_then_releases(self):
        self.config["Environment"]["TEST_MODE"] = "hold"
        self.save_config()
        first = self.start()
        self.wait_active()
        second = self.start()
        out, err = second.communicate(timeout=5)
        self.assertEqual(second.returncode, 1)
        self.assertIn("启动器已在运行", err)
        self.assertIsNone(first.poll())
        self.assertEqual((self.state / "invocations").read_text().count("started"), 1)
        (self.state / "finish").touch()
        out, err = first.communicate(timeout=5)
        self.assertEqual(first.returncode, 0, out + err)

    def test_menu_failure_sends_notification(self):
        bin_dir = self.root / "bin"
        bin_dir.mkdir()
        notifier = bin_dir / "notify-send"
        notifier.write_text(
            f"#!{sys.executable}\nimport json, os, sys\nfrom pathlib import Path\n"
            "Path(os.environ['XDG_STATE_HOME'], 'notification.json').write_text(json.dumps(sys.argv[1:]))\n"
        )
        notifier.chmod(0o755)
        self.env["PATH"] = str(bin_dir) + os.pathsep + self.env["PATH"]
        self.config["GameExecutable"] = str(self.root / "missing.exe")
        self.save_config()
        process = self.start("--notify")
        out, err = process.communicate(timeout=5)
        self.assertEqual(process.returncode, 1)
        notification = json.loads((self.root / "state/notification.json").read_text())
        self.assertIn("--urgency=critical", notification)
        self.assertIn("找不到游戏", notification[-1])

    def test_missing_or_incomplete_resources_reject_launch_before_profile_changes(self):
        ini = self.setup_graphics_profile()
        original = ini.read_bytes()
        resource = self.root / "UHD/pakchunk1.pak"
        self.config["RequiredResourceFiles"] = {str(resource): 8}
        self.save_config()
        for content in (None, b"partial"):
            with self.subTest(content=content):
                if content is not None:
                    resource.parent.mkdir(exist_ok=True)
                    resource.write_bytes(content)
                process = self.start()
                out, err = process.communicate(timeout=5)
                self.assertEqual(process.returncode, 1, out + err)
                self.assertIn("画质基础资源缺失或尚未下载完整", err)
                self.assertEqual(ini.read_bytes(), original)
                self.assertFalse((self.state / "invocations").exists())

    def test_complete_resource_sizes_allow_launch(self):
        resource = self.root / "UHD.pak"
        resource.write_bytes(b"complete")
        self.config["RequiredResourceFiles"] = {str(resource): 8}
        self.save_config()
        process = self.start()
        out, err = process.communicate(timeout=5)
        self.assertEqual(process.returncode, 0, out + err)
        self.assertEqual((self.state / "invocations").read_text(), "started\n")

    def setup_graphics_profile(self):
        ini = self.root / "Engine.ini"
        ini.write_text("[SystemSettings]\nt.MaxFPS=60\nr.VSync=1\n[Other]\nUserSetting=before\n")
        self.config["IniOverrides"] = [{
            "Path": str(ini), "Section": "SystemSettings",
            "Values": {"t.MaxFPS": "120", "r.VSync": "0", "r.MaxAnisotropy": "16"},
        }]
        self.config["Environment"]["TEST_MODE"] = "hold"
        self.save_config()
        return ini

    def test_graphics_profile_restores_keys_preserving_later_edits(self):
        ini = self.setup_graphics_profile()
        process = self.start()
        self.wait_active()
        active = ini.read_text()
        self.assertIn("t.MaxFPS=120", active)
        self.assertIn("r.MaxAnisotropy=16", active)
        self.assertNotIn("t.MaxFPS=60", active)
        ini.write_text(active.replace("UserSetting=before", "UserSetting=after")
                       .replace("r.VSync=0", "r.VSync=2"))
        (self.state / "finish").touch()
        out, err = process.communicate(timeout=5)
        self.assertEqual(process.returncode, 0, out + err)
        restored = ini.read_text()
        self.assertIn("t.MaxFPS=60", restored)
        self.assertNotIn("r.MaxAnisotropy=", restored)
        self.assertIn("UserSetting=after", restored)
        self.assertIn("r.VSync=2", restored)
        self.assertFalse((self.state / "graphics-restore.json").exists())

    def test_graphics_profile_recovers_after_launcher_interruption(self):
        ini = self.setup_graphics_profile()
        original = ini.read_text()
        first = self.start()
        self.wait_active()
        first.kill()
        first.communicate(timeout=5)
        self.assertIn("t.MaxFPS=120", ini.read_text())
        self.assertTrue((self.state / "graphics-restore.json").exists())
        (self.state / "finish").touch()
        deadline = time.monotonic() + 5
        while json.loads((self.state / "status.json").read_text())["phase"] != "game-exited":
            self.assertLess(time.monotonic(), deadline)
            time.sleep(0.05)
        second = self.start()
        out, err = second.communicate(timeout=5)
        self.remember_runner()
        self.assertEqual(second.returncode, 0, out + err)
        self.assertEqual(ini.read_text(), original)
        self.assertFalse((self.state / "graphics-restore.json").exists())

    def test_graphics_dry_run_does_not_edit_files(self):
        ini = self.setup_graphics_profile()
        original = ini.read_bytes()
        process = self.start("--dry-run")
        out, err = process.communicate(timeout=5)
        self.assertEqual(process.returncode, 0, out + err)
        self.assertEqual(ini.read_bytes(), original)
        self.assertFalse((self.state / "graphics-restore.json").exists())

    def setup_quality_database(self):
        database = self.root / "画质设置.db"
        with sqlite3.connect(database) as db:
            db.execute("CREATE TABLE LocalStorage (key TEXT PRIMARY KEY, value TEXT)")
            db.executemany("INSERT INTO LocalStorage VALUES (?,?)", [
                ("ImageQuality", "3"), ("OtherSetting", "keep"),
            ])
        self.config["GameSettingsDatabase"] = str(database)
        self.config["GameImageQuality"] = 4
        self.config["Environment"]["TEST_MODE"] = "hold"
        self.save_config()
        return database

    def quality_value(self, database):
        with sqlite3.connect(database) as db:
            return db.execute("SELECT value FROM LocalStorage WHERE key='ImageQuality'").fetchone()[0]

    def test_quality_preset_applies_and_restores_without_reverting_other_rows(self):
        database = self.setup_quality_database()
        process = self.start()
        self.wait_active()
        self.assertEqual(self.quality_value(database), "4")
        with sqlite3.connect(database) as db:
            db.execute("UPDATE LocalStorage SET value='later' WHERE key='OtherSetting'")
        (self.state / "finish").touch()
        out, err = process.communicate(timeout=5)
        self.assertEqual(process.returncode, 0, out + err)
        self.assertEqual(self.quality_value(database), "3")
        with sqlite3.connect(database) as db:
            self.assertEqual(db.execute("SELECT value FROM LocalStorage WHERE key='OtherSetting'").fetchone()[0], "later")
            self.assertEqual(db.execute("PRAGMA integrity_check").fetchone()[0], "ok")

    def test_quality_preset_preserves_later_manual_choice(self):
        database = self.setup_quality_database()
        process = self.start()
        self.wait_active()
        with sqlite3.connect(database) as db:
            db.execute("UPDATE LocalStorage SET value='2' WHERE key='ImageQuality'")
        (self.state / "finish").touch()
        out, err = process.communicate(timeout=5)
        self.assertEqual(process.returncode, 0, out + err)
        self.assertEqual(self.quality_value(database), "2")
        self.assertFalse((self.state / "quality-restore.json").exists())

    def test_quality_preset_recovers_after_interruption(self):
        database = self.setup_quality_database()
        first = self.start()
        self.wait_active()
        first.kill()
        first.communicate(timeout=5)
        self.assertEqual(self.quality_value(database), "4")
        (self.state / "finish").touch()
        deadline = time.monotonic() + 5
        while json.loads((self.state / "status.json").read_text())["phase"] != "game-exited":
            self.assertLess(time.monotonic(), deadline)
            time.sleep(0.05)
        second = self.start()
        out, err = second.communicate(timeout=5)
        self.remember_runner()
        self.assertEqual(second.returncode, 0, out + err)
        self.assertEqual(self.quality_value(database), "3")
        self.assertFalse((self.state / "quality-restore.json").exists())


if __name__ == "__main__":
    unittest.main(verbosity=2)
