#!/usr/bin/env python3
"""Exercise presented Avalonia/AtomUI controls in NativeAOT; never launch a game."""
import json
import os
from pathlib import Path
import select
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent
BINARY = Path(os.environ.get("WUWA_TEST_LAUNCHER", ROOT / "dist/wuwa-touch-launcher"))
BACKEND = os.environ.get("WUWA_TEST_UI_BACKEND", "x11")


class GuiContracts(unittest.TestCase):
    def test_avalonia_navigation_edits_and_lutris_visibility(self):
        with tempfile.TemporaryDirectory(prefix="wuwa-avalonia-test-") as directory:
            root = Path(directory)
            config = root / "profiles.json"
            config.write_text(json.dumps({
                "Version": 2, "Mode": "touch", "GameExecutable": "/fixture/Client-Win64-Shipping.exe",
                "Linux": {"Integration": "auto"},
                "Normal": {"Resources": "keep", "DirectX": "auto"},
                "Touch": {"Resources": "hd", "DirectX": "dx12"},
                "TouchOnly": {"Values": {"fps": "240"}},
            }))
            original = config.read_bytes()
            lutris = root / "data/lutris/games/wuthering-waves.yml"
            lutris.parent.mkdir(parents=True)
            lutris.write_text("game:\n  exe: /fixture/Client-Win64-Shipping.exe\n  prefix: /fixture/prefix\n"
                              "wine:\n  version: fixture-runner\n")
            output = Path(os.environ.get("WUWA_TEST_UI_OUTPUT",
                ROOT / "artifacts/avalonia-migration" / ("ui-" + BACKEND)))
            output.mkdir(parents=True, exist_ok=True)
            env = dict(os.environ, XDG_CONFIG_HOME=str(root / "config"),
                       XDG_STATE_HOME=str(root / "state"), XDG_DATA_HOME=str(root / "data"))
            env.pop("WUWA_RENDERER", None)
            server = None
            read_fd = None
            try:
                if BACKEND == "x11":
                    read_fd, write_fd = os.pipe()
                    server = subprocess.Popen(
                        ["Xvfb", "-displayfd", str(write_fd), "-screen", "0", "1440x1000x24", "-nolisten", "tcp"],
                        pass_fds=(write_fd,), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                    os.close(write_fd)
                    self.assertTrue(select.select([read_fd], [], [], 5)[0], "Xvfb did not initialize")
                    env.update(DISPLAY=":" + os.read(read_fd, 20).decode().strip(),
                               WAYLAND_DISPLAY="", WUWA_BACKEND="x11")
                elif BACKEND == "wayland":
                    self.assertTrue(env.get("WAYLAND_DISPLAY"), "A native Wayland compositor is required")
                    env.pop("DISPLAY", None)
                    env.pop("WUWA_RENDERER", None)
                    env["WUWA_BACKEND"] = "wayland"
                else:
                    self.fail("Unsupported test backend")
                (output / "result.json").unlink(missing_ok=True)
                with (output / "process.log").open("w") as log:
                    process = subprocess.run([str(BINARY), "ui-smoke", "--config", str(config),
                                              "--output", str(output)],
                                             env=env, stdout=log, stderr=log, timeout=90)
                self.assertTrue((output / "result.json").exists(), (output / "process.log").read_text())
                report = json.loads((output / "result.json").read_text())
                self.assertEqual(process.returncode, 0, report)
                self.assertTrue(report["Passed"], report)
                self.assertEqual(report["Backend"], "Wayland" if BACKEND == "wayland" else "X11")
                for contract in ("native-aot", "software-renderer-without-gpu-context",
                                 "initial-render-does-not-change-profile",
                                 "session-footer-starting", "session-footer-switches-to-running", "session-exit-enables-launch",
                                 "touch-opens-dropdown", "touch-tap-commits-dropdown-option",
                                 "touch-drag-does-not-select-option", "mouse-dropdown-selection-preserved", "keyboard-dropdown-selection-preserved",
                                 "missing-resource-option-disabled-with-hint", "touch-cannot-select-missing-resource",
                                 "resource-detection-separates-installed-tiers", "memory-injection-risk-hint", "memory-experimental-all-frame-generation-levels",
                                 "memory-radio-exclusive-and-selections-retained",
                                 "memory-all-54-options-except-display-and-resolution",
                                 "memory-file-mode-disables-runtime", "memory-all-mode-hides-individual-checkboxes",
                                 "auto-lutris-hides-manual-source", "auto-lutris-hides-manual-overrides",
                                 "lutris-hides-manual-overrides", "all-56-graphics-options",
                                 "memory-mixed-default-all-file", "memory-mixed-checkbox-default-off",
                                 "memory-mixed-checkbox-updates-profile",
                                 "adaptive-fullscreen-default-off", "adaptive-fullscreen-home-toggle",
                                 "adaptive-fullscreen-physical-pixels", "adaptive-fullscreen-session-snapshot",
                                 "adaptive-fullscreen-owns-display-settings", "adaptive-fullscreen-can-be-disabled",
                                 "adaptive-fullscreen-off-enables-manual-settings", "adaptive-fullscreen-hidden-in-normal-mode",
                                 "graphics-search-across-categories", "edits-survive-navigation-and-mode-switch",
                                 "close-dialog-button-visible:close-cancel", "close-dialog-button-visible:close-discard",
                                 "close-dialog-button-visible:close-save", "close-cancel-preserves-draft",
                                 "theme-controls-and-palette:light", "theme-controls-and-palette:dark",
                                 "system-theme-change:Light", "system-theme-change:Dark",
                                 "explicit-theme-ignores-system-change", "linux-hides-windows-material-options",
                                 "background-opacity-and-clear-content:30", "background-opacity-and-clear-content:100",
                                 "background-opacity-and-clear-content:72", "unified-window-frame:close-dialog",
                                 "linux-csd-underlay-is-transparent:opacity-30",
                                 "linux-csd-underlay-is-transparent:opacity-100",
                                 "linux-csd-underlay-is-transparent:opacity-72",
                                 "linux-csd-underlay-is-transparent:close-dialog",
                                 "linux-does-not-call-windows-memory-api",
                                 "hidden-manual-values-retained", "save-succeeds"):
                    self.assertIn(contract, report["Checks"])
                saved = json.loads((output / "profiles-test.json").read_text())
                self.assertEqual(saved["Linux"]["Wrapper"], "测试 龘𠮷 --launcher-check")
                self.assertEqual(saved["Linux"]["Integration"], "auto")
                self.assertEqual(saved["TouchOnly"]["Values"]["shadow"], "3")
                self.assertEqual(saved["TouchOnly"]["MemoryMode"], "mixed")
                self.assertTrue(saved["TouchOnly"]["MemoryOptions"]["native-fg"])
                self.assertTrue(saved["TouchOnly"]["AdaptiveFullscreen"])
                self.assertEqual(saved["TouchOnly"]["Values"]["ray-quality"], "3")
                self.assertEqual(saved["TouchOnly"]["Values"]["fps"], "240")
                self.assertEqual(saved["TouchOnly"]["Values"]["unlock-fps"], "240")
                self.assertEqual(saved["TouchOnly"]["Hotkey"], "Ctrl+Shift+F9")
                self.assertEqual(saved["Normal"]["CustomArguments"], "-windowed")
                self.assertEqual(saved["Touch"]["Resources"], "hd")
                self.assertEqual(saved["Touch"]["DirectX"], "dx12")
                self.assertEqual(saved["Appearance"],
                                 {"Theme": "system", "BackgroundOpacity": 72, "WindowsEffect": "auto"})
                self.assertEqual(config.read_bytes(), original, "Diagnostics must leave the user's profile intact")
            finally:
                if read_fd is not None:
                    os.close(read_fd)
                if server is not None:
                    server.terminate()
                    server.wait(timeout=5)


if __name__ == "__main__":
    unittest.main(verbosity=2)
