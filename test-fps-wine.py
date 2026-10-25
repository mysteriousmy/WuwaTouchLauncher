#!/usr/bin/env python3
"""Actual bridge + test-owned child/plugin under an isolated Wine prefix and Xvfb."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from tools.native_test_build import wine_include_args, wine_library

ROOT = Path(__file__).resolve().parent
WINE = os.environ.get("WUWA_TEST_WINE") or shutil.which("wine")


def win(path):
    return "Z:" + str(path.resolve()).replace("/", "\\")


class FpsBridgeContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not WINE or not os.environ.get("DISPLAY"):
            raise RuntimeError("Run with xvfb-run -a and WUWA_TEST_WINE.")
        cls.temporary = tempfile.TemporaryDirectory(prefix="wuwa-fps-native-")
        cls.root = Path(cls.temporary.name)
        cls.env = os.environ | {"WINEPREFIX": str(ROOT / "artifacts/fps-wine-prefix"),
                               "WINEDEBUG": "-all", "WAYLAND_DISPLAY": "", "WINEDLLOVERRIDES": "winemenubuilder="}
        for source, entry, suffix, extra in (("fps-plugin-fixture", "DllMain", ".dll", ["/dll"]),
                                             ("fps-child-fixture", "mainCRTStartup", ".exe", [])):
            obj = cls.root / (source + ".obj")
            subprocess.run(["clang", "--target=x86_64-pc-windows-msvc", "-O2", "-fno-stack-protector",
                            "-fno-builtin", *wine_include_args(),
                            "-c", str(ROOT / "tests" / (source + ".c")), "-o", str(obj)], check=True)
            subprocess.run(["lld-link", "/nodefaultlib", "/subsystem:windows", "/entry:" + entry,
                            "/out:" + str(cls.root / (source + suffix)), *extra, str(obj),
                            wine_library("kernel32")], check=True)
        subprocess.run([WINE, "wineboot", "--init"], env=cls.env, capture_output=True, check=True, timeout=60)

    @classmethod
    def tearDownClass(cls):
        server = Path(WINE).parent / "wineserver"
        if server.exists():
            subprocess.run([str(server), "-k"], env=cls.env, capture_output=True, timeout=10)
        cls.temporary.cleanup()

    def run_bridge(self, target=None, broken=False, original=False):
        root = Path(tempfile.mkdtemp(dir=self.root))
        status, payload, started = (root / name for name in ("status.json", "request.txt", "started"))
        command = [WINE, str(ROOT / "dist/touch-bridge.exe"), "--status", win(status),
                   "--stop", win(root / "stop"), "--allow-filter", "0", "--filter", "0",
                   "--hotkey-enabled", "0", "--game", win(self.root / "fps-child-fixture.exe")]
        if target is not None:
            command += ["--fps-target", str(target), "--fps-plugin",
                        win(ROOT / "assets/ww_ulk_plugin_base.dll") if original else
                        win(self.root / ("missing.dll" if broken else "fps-plugin-fixture.dll"))]
        result = subprocess.run(command + ["--"], env=self.env | {"WUWA_FPS_PROBE": win(payload),
                               "WUWA_CHILD_STARTED": win(started)}, capture_output=True, timeout=35)
        self.assertTrue(status.exists(), result.stderr.decode(errors="replace"))
        return result.returncode, json.loads(status.read_text()), payload, started

    def test_original_pipe_protocol_reaches_only_new_child(self):
        code, status, payload, started = self.run_bridge(240)
        self.assertEqual(code, 0, status)
        self.assertTrue(started.exists())
        self.assertEqual(payload.read_text(), "0,0,45.0,0,0,240")
        self.assertEqual(status["fpsUnlockState"], "requested")
        self.assertEqual(status["fpsUnlockTarget"], 240)
        self.assertFalse(status["filtering"])

    def test_disabled_feature_never_loads_plugin(self):
        code, status, payload, started = self.run_bridge()
        self.assertEqual(code, 0, status)
        self.assertTrue(started.exists())
        self.assertFalse(payload.exists())
        self.assertEqual(status["fpsUnlockState"], "disabled")

    def test_failed_plugin_load_does_not_resume_game(self):
        code, status, payload, started = self.run_bridge(120, broken=True)
        self.assertEqual(code, 5, status)
        self.assertEqual(status["phase"], "fps-error")
        self.assertEqual(status["fpsUnlockState"], "failed")
        self.assertGreater(status["fpsUnlockError"], 0)
        self.assertFalse(started.exists())
        self.assertFalse(payload.exists())

    def test_original_supplied_plugin_loads_in_isolated_child(self):
        code, status, _, started = self.run_bridge(120, original=True)
        self.assertEqual(code, 0, status)
        self.assertTrue(started.exists())
        self.assertEqual(status["fpsUnlockError"], 0)
        self.assertIn(status["fpsUnlockState"], ("waiting-for-plugin", "requested"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
