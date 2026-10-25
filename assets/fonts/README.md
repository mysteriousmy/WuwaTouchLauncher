# Embedded UI fonts

`WuwaLauncherUI-Regular.otf` and `WuwaLauncherUI-Bold.otf` are renamed,
subsetted derivatives of Noto Sans CJK SC. They retain the font's original
copyright metadata and are distributed under OFL 1.1; see
`../../licenses/Noto-CJK.txt`.

They include the launcher's fixed Chinese UI text and printable Latin characters.
Avalonia's system fallback still handles characters outside the subset, including
user-supplied file names and other languages. These files do not replace system
fonts and are used on both desktop platforms.

Regenerate after changing UI text:

```sh
python3 tools/build-ui-fonts.py --source-dir /usr/share/fonts/noto-cjk
```

The script requires FontTools, reads the SC face (index 2) of the upstream regular
and bold TTC files, preserves outlines/layout features, and renames the modified
families. Current source: Noto CJK, https://github.com/notofonts/noto-cjk.
