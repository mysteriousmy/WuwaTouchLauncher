#!/usr/bin/env python3
"""Regenerate the Linux UI subsets using fonttools (OFL-1.1; see licenses/Noto-CJK.txt)."""
import argparse
from pathlib import Path
from fontTools import subset
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parent.parent

def ui_codepoints():
    text = "".join(p.read_text() for p in (ROOT / "src").glob("*.cs"))
    # Cover source literals, printable Latin, and UI punctuation. Arbitrary input uses fallback.
    return set(range(0x20, 0x250)) | {ord(c) for c in text if c.isprintable()} | {0x25A1, 0xFFFD}

def build(source_dir):
    destination = ROOT / "assets/fonts"
    destination.mkdir(exist_ok=True, parents=True)
    for style in ("Regular", "Bold"):
        font = TTFont(source_dir / f"NotoSansCJK-{style}.ttc", fontNumber=2, recalcTimestamp=False)
        options = subset.Options()
        options.name_IDs = ["*"]
        options.name_legacy = True
        options.name_languages = ["*"]
        sub = subset.Subsetter(options=options)
        sub.populate(unicodes=ui_codepoints())
        sub.subset(font)
        family = f"Wuwa Launcher UI {style}"
        names = {1: family, 2: "Regular", 3: f"1.0;WuwaLauncherUI-{style}", 4: family,
                 6: f"WuwaLauncherUI-{style}", 16: family, 17: "Regular"}
        for record in font["name"].names:
            if record.nameID in names:
                record.string = names[record.nameID].encode(record.getEncoding())
        cff = font["CFF "].cff
        cff.fontNames = [f"WuwaLauncherUI-{style}"]
        cff.topDictIndex[0].FamilyName = family
        cff.topDictIndex[0].FullName = family
        output = destination / f"WuwaLauncherUI-{style}.otf"
        font.save(output)
        print(f"{output.name}: {output.stat().st_size} bytes")

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", type=Path, default=Path("/usr/share/fonts/noto-cjk"))
    build(parser.parse_args().source_dir)
