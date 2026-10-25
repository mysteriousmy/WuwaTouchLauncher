#!/usr/bin/env python3
"""Package local verified artifacts and their notices; never upload a release."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import tarfile
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("kind", choices=["linux-x64", "win-x64", "linux-x64-static-cli", "relink-source"])
    args = parser.parse_args()
    version = ET.parse(ROOT / "src/WuwaTouchLauncher.csproj").findtext(".//Version")
    releases = ROOT / "releases"
    releases.mkdir(exist_ok=True)
    basename = f"wuwa-launcher-{version}-{args.kind}"
    with tempfile.TemporaryDirectory(prefix="wuwa-package-") as temporary:
        stage = Path(temporary)
        if args.kind == "relink-source":
            for name in ("src", "assets", "build", "tools", "native", "tests", "licenses", "docs", "vendor"):
                shutil.copytree(ROOT / name, stage / name,
                                ignore=shutil.ignore_patterns("bin", "obj", "__pycache__", "LOCAL-HISTORY.md"))
            for pattern in ("*.sh", "*.ps1", "test-*.py"):
                for path in ROOT.glob(pattern):
                    shutil.copy2(path, stage / path.name)
            for name in ("README.md", "LICENSE", "global.json", "Directory.Build.props", "NuGet.Config", "config.example.json"):
                shutil.copy2(ROOT / name, stage / name)
            for name in ("relink-sources", "linux-native/sources", "linux-overlay/feed"):
                shutil.copytree(ROOT / "artifacts" / name, stage / "artifacts" / name)
            target = stage / "artifacts/p0"
            target.mkdir(parents=True)
            shutil.copy2(ROOT / "artifacts/p0/staticlink-avalonia-nuget.zip", target)
        else:
            if args.kind == "linux-x64-static-cli":
                binary = ROOT / "dist/linux-static-cli/wuwa-touch-cli"
            else:
                manifest_path = ROOT / "artifacts/publish" / args.kind / "build-manifest.json"
                manifest = json.loads(manifest_path.read_text())
                if manifest.get("custom_atomui_source"):
                    raise ValueError("A diagnostic relink cannot be packaged as the normal release.")
                binary = ROOT / ("dist/wuwa-touch-launcher" if args.kind == "linux-x64" else "dist/win-x64/wuwa-touch-launcher.exe")
                if digest(binary) != manifest["packed"]["sha256"]:
                    raise ValueError("Staged binary differs from the validated publish result.")
                # Keep audit hashes without publishing local absolute workspace paths.
                def portable(value):
                    if isinstance(value, str):
                        return value.replace(str(ROOT) + "/", "")
                    if isinstance(value, dict):
                        return {portable(k): portable(v) for k, v in value.items()}
                    if isinstance(value, list):
                        return [portable(v) for v in value]
                    return value
                (stage / "build-manifest.json").write_text(json.dumps(portable(manifest), indent=2) + "\n")
            shutil.copy2(binary, stage / binary.name)
            shutil.copy2(ROOT / "README.md", stage / "README.md")
            shutil.copy2(ROOT / "LICENSE", stage / "LICENSE")
            shutil.copy2(ROOT / "config.example.json", stage)
            shutil.copytree(ROOT / "licenses", stage / "licenses")
            shutil.copytree(ROOT / "docs", stage / "docs", ignore=shutil.ignore_patterns("LOCAL-HISTORY.md"))
            if args.kind == "linux-x64":
                shutil.copy2(ROOT / "install.sh", stage)
                (stage / "assets").mkdir()
                shutil.copy2(ROOT / "assets/wuwa-touch-launcher.svg", stage / "assets")
        hashes = {str(p.relative_to(stage)): digest(p) for p in sorted(stage.rglob("*")) if p.is_file()}
        (stage / "FILES.sha256").write_text("".join(f"{value}  {name}\n" for name, value in hashes.items()))
        if args.kind == "win-x64":
            archive = releases / (basename + ".zip")
            with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as output:
                for file in sorted(stage.rglob("*")):
                    if file.is_file():
                        output.write(file, file.relative_to(stage))
        else:
            archive = releases / (basename + ".tar.gz")
            with tarfile.open(archive, "w:gz", compresslevel=9) as output:
                for file in sorted(stage.iterdir()):
                    output.add(file, arcname=file.name)
    print(f"{digest(archive)}  {archive.name}")


if __name__ == "__main__":
    main()
