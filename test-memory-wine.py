#!/usr/bin/env python3
"""Exercise real remote writes using a build-time fixture layout, never a game."""
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from tools.native_test_build import wine_include_args, wine_library
import pefile

ROOT = Path(__file__).resolve().parent
WINE = os.environ.get("WUWA_TEST_WINE") or shutil.which("wine")
OPTIONS = ["r.NGX.DLSS.Enable=1", "r.NGX.DLSS.Quality.Auto=0",
           "r.NGX.DLSS.Quality=0", "r.DefaultFeature.AntiAliasing=0"]
ORIGINAL = [0, 0, 1, 1, -1, -1, 2, 2]


def win(path):
    return "Z:" + str(path.resolve()).replace("/", "\\")


class MemoryBridgeContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not WINE or not os.environ.get("DISPLAY"):
            raise RuntimeError("Run with xvfb-run -a and Wine installed.")
        cls.temporary = tempfile.TemporaryDirectory(prefix="wuwa-memory-native-")
        cls.root = Path(cls.temporary.name)
        cls.env = os.environ | {"WINEPREFIX": str(ROOT / "artifacts/memory-wine-proton-prefix"),
                               "WINEDEBUG": "-all", "WAYLAND_DISPLAY": "",
                               "WINEDLLOVERRIDES": "winemenubuilder="}
        cls.child = cls.root / "memory-child.exe"
        cls.bridge = cls.root / "memory-bridge.exe"
        layout = (ROOT / "native/memory-graphics-layout.h").read_text()
        # Each newly admitted CVar gets its own typed storage. The handwritten
        # core fixture retains delayed registration and fault-injection behavior.
        known_slots = {"0983aeb0", "0983af40", "0983af28", "09b7a970", "0983bea0", "09b85308",
                       "0983af58", "098f5500", "098f5710", "09b1a0e0", "098f5740", "09a32688",
                       "09864960", "0989a540", "099e3c50"}
        generated, extra_slots = [], []
        pattern = r'\{L"([^"\n]+)",\s*0x([0-9a-f]+),0x[0-9a-f]+,(GRAPHICS_\w+),\s*([-\d.]+),'
        for index, (_, slot, kind, minimum) in enumerate(re.findall(pattern, layout)):
            if slot in known_slots:
                continue
            name = "generated" + str(index)
            initial = max(0, float(minimum))
            floating = kind in ("GRAPHICS_FLOAT", "GRAPHICS_FLOAT_REF")
            field, table = ("f", "float") if floating else ("i", "int")
            if kind.endswith("_REF"):
                generated += [f"static {'float' if floating else 'INT'} {name}Value={initial};",
                    "static Reference %s={&%sRefTable,{0},&%sValue,{.%s=%s}};" % (name, table, name, field, initial),
                    f"__declspec(dllexport) Reference *{name}Slot=&{name};"]
            else:
                generated += ["static Variable %s={&%sTable,{0},{.%s={%s,%s}}};" % (name, table, field, initial, initial),
                    f"__declspec(dllexport) Variable *{name}Slot=&{name};"]
            extra_slots.append((slot, name + "Slot"))
        source = cls.root / "memory-child-fixture.c"
        source.write_text((ROOT / "tests/memory-child-fixture.c").read_text().replace(
            "static BOOL Desired(void)", "\n".join(generated) + "\nstatic BOOL Desired(void)"))
        cls.compile(source, cls.child)
        pe = pefile.PE(str(cls.child))
        exports = {s.name.decode(): s.address for s in pe.DIRECTORY_ENTRY_EXPORT.symbols}
        original_hash = layout[layout.index("static const BYTE"):layout.index("static const GraphicsSpec")]
        digest = hashlib.sha256(cls.child.read_bytes()).digest()
        layout = layout.replace(original_hash, "static const BYTE graphicsExpectedHash[32]={" +
                                ",".join(str(x) for x in digest) + "};\n")
        for actual, symbol in [("0983aeb0", "enableSlot"), ("0983af40", "autoSlot"),
                               ("0983af28", "qualitySlot"), ("09b7a970", "aaSlot"),
                               ("07e28c20", "intTable"), ("07e28b68", "boolTable"),
                               ("0983bea0", "fgSlot"), ("09b88da0", "worldSlot"),
                               ("08599480", "worldTable"), ("084774b8", "instanceTable"),
                               ("084b7520", "playerTable"), ("07689b48", "controllerTable"),
                               ("076856e0", "pawnTable"), ("07e28cd8", "floatTable"),
                               ("09b85308", "fpsSlot"), ("0983af58", "sharpnessSlot"),
                               ("098f5500", "scaleSlot"), ("098f5710", "vsyncSlot"),
                               ("09b1a0e0", "syncSlot"), ("098f5740", "anisotropySlot"),
                               ("09a32688", "lightingSlot"), ("07e28f00", "intRefTable"),
                               ("07e28fb8", "floatRefTable"), ("07e28e48", "boolRefTable"),
                               ("09864960", "fsrFgSlot"), ("0989a540", "xessFgSlot"),
                               ("099e3c50", "boolRefSlot"), *extra_slots]:
            layout = layout.replace("0x" + actual, hex(exports[symbol]))
        # Exercise reference layouts even when a product build has no admitted
        # mapping of a particular reference kind yet. These names are test-only.
        end = layout.index("\n};", layout.index("static const GraphicsSpec"))
        layout = layout[:end].rstrip() + ",\n" + (
            '{L"test.IntRef",%d,%d,GRAPHICS_INT_REF,0,1,3,FALSE},\n'
            '{L"test.FloatRef",%d,%d,GRAPHICS_FLOAT_REF,0,2,0,FALSE}' %
            (exports["intRefSlot"], exports["intRefTable"], exports["floatRefSlot"], exports["floatRefTable"])) + layout[end:]
        header = cls.root / "fixture-layout.h"
        header.write_text(layout)
        cls.compile(ROOT / "native/touch-bridge.c", cls.bridge,
                    [f'-DGRAPHICS_LAYOUT_HEADER="{header}"', '-DGRAPHICS_READY_TIMEOUT_MS=3500'])
        subprocess.run([WINE, "wineboot", "--init"], env=cls.env, capture_output=True, check=True, timeout=60)

    @classmethod
    def compile(cls, source, output, extra=()):
        obj = output.with_suffix(".obj")
        subprocess.run(["clang", "--target=x86_64-pc-windows-msvc", "-O2", "-fno-stack-protector",
                        "-fno-builtin", *wine_include_args(),
                        *extra, "-c", str(source), "-o", str(obj)], check=True)
        libraries = ["kernel32", "user32", "shell32", "ole32", "bcrypt"]
        subprocess.run(["lld-link", "/nodefaultlib", "/subsystem:windows", "/entry:mainCRTStartup",
                        "/out:" + str(output), str(obj),
                        *[wine_library(x) for x in libraries]], check=True)

    @classmethod
    def tearDownClass(cls):
        subprocess.run([str(Path(WINE).parent / "wineserver"), "-k"], env=cls.env,
                       capture_output=True, timeout=10)
        cls.temporary.cleanup()

    def run_bridge(self, options=OPTIONS, enabled=True, bad=False, unknown=False, cloud=True, unrelated=False,
                   late=False, scene=False, extended=False, nan=False, repoint=False, partial=False,
                   unavailable=(), fg_kind=0, independent=False, bool_ref=False):
        directory = Path(tempfile.mkdtemp(dir=self.root))
        status, output = directory / "status.json", directory / "values.json"
        child = self.child
        if unknown:
            child = directory / "updated-child.exe"
            child.write_bytes(self.child.read_bytes() + b"updated-version")
        command = [WINE, str(self.bridge), "--status", win(status), "--stop", win(directory / "stop"),
                   "--allow-filter", "0", "--filter", "0", "--hotkey-enabled", "0", "--game", win(child)]
        if enabled:
            command += ["--memory-graphics", "1"]
        for option in options:
            command += ["--graphics-set", option]
        for key in unavailable:
            command += ["--graphics-unavailable", key]
        command += ["--"] + (["-CloudGame"] if cloud else [])
        env = self.env | {"WUWA_MEMORY_OUTPUT": win(output)}
        env["WUWA_MEMORY_FG_KIND"] = str(fg_kind)
        if independent:
            env["WUWA_MEMORY_FG_INDEPENDENT"] = "1"
        if bool_ref:
            env["WUWA_MEMORY_BOOL_REF"] = "1"
        if bad:
            env["WUWA_MEMORY_BAD_TYPE"] = "1"
        if late:
            env["WUWA_MEMORY_LATE_MISMATCH"] = "1"
        if scene:
            env["WUWA_MEMORY_SCENE"] = "1"
        for key, active in {"EXTENDED": extended, "NAN": nan, "REPOINT": repoint, "PARTIAL": partial}.items():
            if active:
                env["WUWA_MEMORY_" + key] = "1"
        other = None
        if unrelated:
            other_path = directory / "other-child.exe"
            shutil.copy2(child, other_path)
            other = subprocess.Popen([WINE, str(other_path)], env=self.env |
                                     {"WUWA_MEMORY_OUTPUT": win(directory / "other.json")},
                                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            result = subprocess.run(command, env=env, capture_output=True, timeout=35)
            if other:
                self.assertEqual(other.wait(timeout=10), 0)
                self.assertEqual(json.loads((directory / "other.json").read_text())["values"], ORIGINAL)
        finally:
            if other and other.poll() is None:
                other.kill()
                other.wait()
        return result.returncode, json.loads(status.read_text()) if status.exists() else {}, \
            json.loads(output.read_text()) if output.exists() else None

    def test_delayed_registration_reapply_and_owned_child_only(self):
        code, status, values = self.run_bridge(unrelated=True)
        self.assertEqual(code, 0, status)
        self.assertEqual(status["memoryGraphicsState"], "applied")
        self.assertEqual(status["memoryGraphicsApplied"], 4)
        self.assertGreaterEqual(status["memoryGraphicsWrites"], 5)
        self.assertEqual(values, {"beforeReset": 1, "afterReset": 1, "values": [1, 1, 0, 0, 0, 0, 0, 0]})

    def test_disabled_mode_never_writes(self):
        code, status, values = self.run_bridge(options=[], enabled=False)
        self.assertEqual(code, 0)
        self.assertEqual(status["memoryGraphicsState"], "disabled")
        self.assertEqual(status["memoryGraphicsWrites"], 0)
        self.assertEqual(values["values"], ORIGINAL)

    def test_updated_game_runs_without_memory_writes(self):
        code, status, values = self.run_bridge(unknown=True)
        self.assertEqual(code, 0)
        self.assertEqual(status["memoryGraphicsState"], "unsupported-version")
        self.assertEqual(status["memoryGraphicsWrites"], 0)
        self.assertEqual(values["values"], ORIGINAL)

    def test_wrong_variable_type_rejects_entire_set(self):
        code, status, values = self.run_bridge(bad=True)
        self.assertEqual(code, 0)
        self.assertEqual(status["memoryGraphicsState"], "layout-mismatch")
        self.assertEqual(status["memoryGraphicsWrites"], 0)
        self.assertEqual(values["values"], ORIGINAL)

    def test_all_keep_settings_do_not_attach(self):
        code, status, values = self.run_bridge(options=[])
        self.assertEqual(code, 0)
        self.assertEqual(status["memoryGraphicsState"], "no-settings")
        self.assertEqual(values["values"], ORIGINAL)

    def test_later_mismatch_stops_and_rolls_back_only_valid_values(self):
        code, status, values = self.run_bridge(late=True)
        self.assertEqual(code, 0)
        self.assertEqual(status["memoryGraphicsState"], "layout-mismatch")
        self.assertEqual(status["memoryGraphicsApplied"], 0)
        self.assertGreaterEqual(status["memoryGraphicsWrites"], 5)
        self.assertEqual(values["beforeReset"], 1)
        self.assertEqual(values["afterReset"], 1)
        self.assertEqual(values["values"], [2, 2, 1, 1, -1, -1, 2, 2])

    def test_frame_generation_tracks_scene_and_repairs_engine_reset(self):
        code, status, values = self.run_bridge(options=OPTIONS + ["r.Streamline.DLSSG.Enable=1"], scene=True)
        self.assertEqual(code, 0, status)
        self.assertEqual(values, {"loginOn": 0, "playOn": 1, "pausedOn": 0, "resumeOn": 1,
                                 "repaired": 1, "changedWorldOn": 1, "invalidSceneOn": 0,
                                 "switchingOff": 0, "fg": [0, 0], "otherGraphicsApplied": 1})
        self.assertEqual(status["memoryGraphicsState"], "applied")
        self.assertEqual(status["memoryGraphicsApplied"], 5)
        self.assertEqual(status["memoryFrameGenerationState"], "waiting-scene")

    def test_frame_generation_off_stays_off_in_active_scene(self):
        code, status, values = self.run_bridge(options=OPTIONS + ["r.Streamline.DLSSG.Enable=0"], scene=True)
        self.assertEqual(code, 0, status)
        self.assertEqual(values, {"loginOn": 0, "playOn": 0, "pausedOn": 0, "resumeOn": 0,
                                 "repaired": 0, "changedWorldOn": 0, "invalidSceneOn": 0,
                                 "switchingOff": 1, "fg": [0, 0], "otherGraphicsApplied": 1})
        self.assertEqual(status["memoryFrameGenerationState"], "off")

    def test_fsr_and_xess_frame_generation_recover_from_pause_and_engine_reset(self):
        for kind, name in ((1, "r.FidelityFX.FI.Enabled"), (2, "r.XeFG.Enabled")):
            with self.subTest(algorithm=name):
                code, status, values = self.run_bridge(options=OPTIONS + [name + "=1"], scene=True, fg_kind=kind)
                self.assertEqual(code, 0, status)
                self.assertEqual(values, {"loginOn": 0, "playOn": 1, "pausedOn": 0, "resumeOn": 1,
                    "repaired": 1, "changedWorldOn": 1, "invalidSceneOn": 0, "switchingOff": 0,
                    "fg": [0,0], "otherGraphicsApplied": 1})
                self.assertEqual(status["memoryFrameGenerationState"], "waiting-scene")
                self.assertEqual(status["memoryGraphicsApplied"], 5)

    def test_frame_generation_algorithms_have_independent_reset_state(self):
        code, status, values = self.run_bridge(options=["r.Streamline.DLSSG.Enable=1",
            "r.FidelityFX.FI.Enabled=1", "r.XeFG.Enabled=1"], scene=True, fg_kind=1, independent=True)
        self.assertEqual(code, 0, status)
        self.assertEqual(values, {"otherInterrupted": 0, "otherStayedOn": 1, "repaired": 1,
                                 "pausedOn": 0, "loginOn": 0})
        self.assertEqual(status["memoryGraphicsApplied"], 3)

    def test_unavailable_requests_are_reported_without_blocking_known_values(self):
        code, status, values = self.run_bridge(unavailable=["eye-mode", "smooth-fps"])
        self.assertEqual(code, 0, status)
        self.assertEqual(status["memoryGraphicsState"], "applied")
        self.assertEqual(status["memoryGraphicsSkipped"], 2)
        self.assertEqual(status["memoryGraphicsApplied"], 4)
        self.assertEqual(values["values"], [1,1,0,0,0,0,0,0])
        code, status, values = self.run_bridge(options=[], unavailable=["eye-mode"])
        self.assertEqual(code, 0, status)
        self.assertEqual(status["memoryGraphicsState"], "unavailable")
        self.assertEqual(status["memoryGraphicsSkipped"], 1)
        self.assertEqual(status["memoryGraphicsWrites"], 0)
        self.assertEqual(values["values"], ORIGINAL)

    def test_experimental_scalability_colors_and_multipliers_accept_zero_initialization(self):
        options = ["r.Streamline.DLSSG.FramesToGenerate=5", "r.XeFG.MaxInterpolatedFrames=3",
                   "r.Kuro.AutoCoolUIEnable=1", "sg.ShadowQuality=3", "sg.EffectsQuality=2",
                   "r.DetailMode=3", "sg.TextureQuality=4", "r.Kuro.LODDistanceScale=3",
                   "foliage.DensityScale=0.6", "r.MarchingCubes.EnablePrePass=1",
                   "r.FidelityFX.FSR3.Enabled=1", "r.FidelityFX.FSR3.QualityMode=4",
                   "r.FidelityFX.FSR.PrimaryUpscale=1", "r.XeSS.Enabled=1", "r.XeSS.Quality=6",
                   "r.RayTracing.Enable=1", "sg.RayTracingQuality=3", "r.RayTracing.GlobalIllumination=3",
                   "r.RayTracing.Reflections=1", "r.RayTracing.Shadows=1", "r.Gamma=0.2",
                   "r.Client.Saturation=1.36", "r.Client.Contrast=0.64"]
        code, status, values = self.run_bridge(options=options, bool_ref=True)
        self.assertEqual(code, 0, status)
        self.assertEqual(status["memoryGraphicsState"], "applied")
        self.assertEqual(status["memoryGraphicsApplied"], 23)
        self.assertGreaterEqual(status["memoryGraphicsWrites"], 23)
        self.assertEqual(values, {"value": [1,85,102,119], "shadow": [1,85,102,119]})

    def test_boolean_reference_writes_only_one_byte_per_copy(self):
        code, status, values = self.run_bridge(options=["r.RayTracing.Enable=1"], bool_ref=True)
        self.assertEqual(code, 0, status)
        self.assertEqual(status["memoryGraphicsState"], "applied")
        self.assertEqual(status["memoryGraphicsApplied"], 1)
        self.assertEqual(values, {"value": [1,85,102,119], "shadow": [1,85,102,119]})

    EXTENDED_OPTIONS = OPTIONS + ["t.MaxFPS=45.5", "r.NGX.DLSS.Sharpness=-0.3",
                                  "test.IntRef=0", "test.FloatRef=0.64"]

    def test_float_and_reference_values_apply_and_repair_both_copies(self):
        code, status, values = self.run_bridge(options=self.EXTENDED_OPTIONS, extended=True)
        self.assertEqual(code, 0, status)
        self.assertEqual(status["memoryGraphicsState"], "applied")
        self.assertEqual(status["memoryGraphicsApplied"], 8)
        self.assertEqual(values, {"beforeReset": 1, "afterReset": 1, "fpsMilli": [45500,45500],
            "sharpMilli": [-300,-300], "intRef": [0,0,1], "floatRefMilli": [640,640], "enable": [1,1]})

    def test_nan_current_value_rejects_entire_set_without_writes(self):
        code, status, values = self.run_bridge(options=self.EXTENDED_OPTIONS, extended=True, nan=True)
        self.assertEqual(code, 0, status)
        self.assertEqual(status["memoryGraphicsState"], "layout-mismatch")
        self.assertEqual(status["memoryGraphicsWrites"], 0)
        self.assertEqual(values, {"beforeReset": 0, "afterReset": 0, "fpsMilli": [-999,60000],
            "sharpMilli": [-200,-200], "intRef": [1,1,1], "floatRefMilli": [1000,1000], "enable": [0,0]})

    def test_changed_reference_stops_writes_and_preserves_new_referent(self):
        code, status, values = self.run_bridge(options=self.EXTENDED_OPTIONS, extended=True, repoint=True)
        self.assertEqual(code, 0, status)
        self.assertEqual(status["memoryGraphicsState"], "layout-mismatch")
        self.assertEqual(status["memoryGraphicsApplied"], 0)
        self.assertEqual(values, {"beforeReset": 1, "afterReset": 1, "fpsMilli": [60000,60000],
            "sharpMilli": [-200,-200], "intRef": [0,0,1], "floatRefMilli": [1000,1000], "enable": [0,0]})

    def test_rollback_preserves_engine_changed_copy_and_restores_unchanged_copy(self):
        code, status, values = self.run_bridge(options=self.EXTENDED_OPTIONS, extended=True, partial=True)
        self.assertEqual(code, 0, status)
        self.assertEqual(status["memoryGraphicsState"], "layout-mismatch")
        self.assertEqual(values, {"beforeReset": 1, "afterReset": 1, "fpsMilli": [33000,60000],
            "sharpMilli": [-200,-200], "intRef": [1,1,1], "floatRefMilli": [1000,1000], "enable": [2,2]})

    def test_numeric_boundaries_and_post_processing_requests_are_applied(self):
        options = ["t.MaxFPS=1000", "r.NGX.DLSS.Sharpness=-1", "r.MaxAnisotropy=16",
                   "r.Kuro.GlobalLightQuality=4", "r.ScreenPercentage=200",
                   "r.AmbientOcclusionLevels=-1", "r.DistanceFieldAO=1", "r.DistanceFieldAOQuality=3",
                   "r.VolumetricFog=1", "r.Kuro.VRS.VolumeCloudQuality=3", "r.LightShaftQuality=1",
                   "r.MotionBlur.Amount=0.75", "r.Kuro.KuroBloomEnable=0", "r.Kuro.AutoExposurePlayerCustom=0",
                   "r.Kuro.KuroEnableScreenFilter=0", "r.BlueLightFilter.Disable=0",
                   "r.Tonemapper.BrightnessAndTextureDisable=0"]
        code, status, values = self.run_bridge(options=options, extended=True)
        self.assertEqual(code, 0, status)
        self.assertEqual(status["memoryGraphicsState"], "applied")
        self.assertEqual(status["memoryGraphicsApplied"], 17)
        self.assertEqual(values, {"beforeReset": 0, "afterReset": 0, "fpsMilli": [1000000,1000000],
            "sharpMilli": [-1000,-1000], "intRef": [1,1,1], "floatRefMilli": [1000,1000], "enable": [0,0]})

    def test_invalid_options_and_normal_mode_never_start_child(self):
        for options, enabled, cloud in [(["r.NGX.DLSS.Quality=2"], True, True),
                (["r.Streamline.DLSSG.Enable=2"], True, True), (["anything=0"], True, True),
                *[(["t.MaxFPS=" + value], True, True) for value in ("nan", "inf", "1e2", "45.", ".5", "1001", "-2", "0.1234567")],
                (["r.VSync=0.5"], True, True), (["r.MaxAnisotropy=0"], True, True),
                (["r.MaxAnisotropy=3"], True, True), (["r.MaxAnisotropy=17"], True, True),
                (["r.Kuro.GlobalLightQuality=5"], True, True), (["r.NGX.DLSS.Sharpness=1.1"], True, True),
                ([OPTIONS[0], OPTIONS[0]], True, True), ([OPTIONS[0]], False, True),
                ([OPTIONS[0]], True, False)]:
            with self.subTest(options=options, enabled=enabled, cloud=cloud):
                code, _, values = self.run_bridge(options, enabled, cloud=cloud)
                self.assertEqual(code, 2)
                self.assertIsNone(values)


if __name__ == "__main__":
    unittest.main(verbosity=2)
