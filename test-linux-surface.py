#!/usr/bin/env python3
"""Check the actual Wayland window's pixels through niri, using temporary profiles."""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest

from PIL import Image, ImageStat

ROOT = Path(__file__).resolve().parent
BINARY = Path(os.environ.get("WUWA_TEST_LAUNCHER", ROOT / "dist/wuwa-touch-launcher"))
OUTPUT = Path(os.environ.get("WUWA_TEST_SURFACE_OUTPUT",
                             ROOT / "artifacts/avalonia-migration/surface-wayland"))


class LinuxSurfaceContracts(unittest.TestCase):
    def test_wayland_window_pixels_follow_background_opacity(self):
        self.assertTrue(os.environ.get("WAYLAND_DISPLAY"), "A running niri Wayland session is required")
        OUTPUT.mkdir(parents=True, exist_ok=True)
        readings = []
        with tempfile.TemporaryDirectory(prefix="wuwa-surface-") as directory:
            root = Path(directory)
            config = root / "profiles.json"
            env = dict(os.environ, WUWA_BACKEND="wayland", XDG_CONFIG_HOME=str(root / "config"),
                       XDG_STATE_HOME=str(root / "state"), XDG_DATA_HOME=str(root / "data"))
            env.pop("DISPLAY", None)
            env.pop("WUWA_RENDERER", None)
            for theme in ("dark", "light"):
                for opacity in (30, 100):
                    with self.subTest(theme=theme, opacity=opacity):
                        config.write_text(json.dumps({
                            "Version": 2, "Mode": "normal", "Linux": {"Integration": "manual"},
                            "Appearance": {"Theme": theme, "BackgroundOpacity": opacity, "WindowsEffect": "auto"},
                        }))
                        original = config.read_bytes()
                        name = f"{theme}-{opacity}"
                        with (OUTPUT / f"{name}.log").open("w") as log:
                            process = subprocess.Popen([str(BINARY), "gui", "--config", str(config)],
                                                       env=env, stdout=log, stderr=log)
                        try:
                            match = []
                            for _ in range(60):
                                windows = json.loads(subprocess.check_output(["niri", "msg", "--json", "windows"]))
                                match = [w for w in windows if w.get("pid") == process.pid]
                                if match or process.poll() is not None:
                                    break
                                time.sleep(.1)
                            self.assertTrue(match, (OUTPUT / f"{name}.log").read_text())
                            time.sleep(1)
                            image_path = (OUTPUT / f"{name}.png").resolve()
                            image_path.unlink(missing_ok=True)
                            subprocess.run(["niri", "msg", "action", "screenshot-window", "--id",
                                            str(match[0]["id"]), "--path", str(image_path)],
                                           check=True, capture_output=True)
                            for _ in range(50):
                                if image_path.is_file():
                                    try:
                                        with Image.open(image_path) as image:
                                            image.load()
                                        break
                                    except OSError:
                                        pass
                                time.sleep(.1)
                            with Image.open(image_path) as image:
                                alpha = image.getchannel("A")
                                # Empty heading area, away from text, cards and CSD shadows.
                                x, y = round(image.width * .90), round(image.height * .10)
                                background = ImageStat.Stat(alpha.crop((x, y, x + 8, y + 8))).mean[0]
                                foreground = alpha.getextrema()[1]
                            # Normalize a compositor's whole-window opacity without making
                            # text or controls translucent in the application itself.
                            self.assertGreater(foreground, 100)
                            ratio = background / foreground
                            readings.append({"theme": theme, "opacity": opacity, "background_alpha": background,
                                             "foreground_alpha": foreground, "ratio": ratio})
                            self.assertAlmostEqual(ratio, opacity / 100, delta=.035)
                            self.assertEqual(config.read_bytes(), original)
                        finally:
                            if process.poll() is None:
                                process.terminate()
                            process.wait(timeout=5)
        (OUTPUT / "measurements.json").write_text(json.dumps(readings, indent=2) + "\n")


if __name__ == "__main__":
    unittest.main(verbosity=2)
