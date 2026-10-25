#!/usr/bin/env python3
"""Check that the committed Linux font subsets cover source Chinese UI text."""
from pathlib import Path
import unittest
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parent

class UiFontContracts(unittest.TestCase):
    def test_ui_subsets_cover_chinese_literals_and_remain_small(self):
        text = "".join(p.read_text() for p in (ROOT / "src").glob("*.cs"))
        required = {ord(c) for c in text if "\u4e00" <= c <= "\u9fff"}
        self.assertGreater(len(required), 300)
        for style in ("Regular", "Bold"):
            with self.subTest(style=style):
                path = ROOT / f"assets/fonts/WuwaLauncherUI-{style}.otf"
                with TTFont(path) as font:
                    self.assertFalse(required - font.getBestCmap().keys(),
                                     "Run tools/build-ui-fonts.py after changing Chinese UI text")
                    names = {n.toUnicode() for n in font["name"].names if n.nameID in (1, 16)}
                    self.assertEqual(names, {f"Wuwa Launcher UI {style}"})
                self.assertLess(path.stat().st_size, 400_000)

if __name__ == "__main__":
    unittest.main(verbosity=2)
