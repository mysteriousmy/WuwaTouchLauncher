#!/usr/bin/env python3
"""Reproduce cross-RID StaticLink experiments with the approved CI package lock."""
import argparse
import hashlib
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent
ARTIFACT_ID = "11940101968"
ARCHIVE_SHA256 = "75c33cf5e280bc4745b7798049348171000e15da57bfea8ea77c8e28837746d8"
PACKAGES = {
    "StaticLink.Avalonia.12.1.4.1.nupkg": "16480636c0f503bd6054d1024a936517267f55d2e9c3f19628ba07ff632690c1",
    "StaticLink.Avalonia.Native.12.1.4.1.nupkg": "f4b4f39ed74cf098e2240bba2f51536cb4e78f963cf6f2df9e0e9f8ef5ffb6b0",
}


def prepare_feed():
    if (ROOT / "vendor/build-inputs/manifest.json").is_file():
        subprocess.run([sys.executable, str(ROOT / "tools/bootstrap-build-inputs.py")], check=True)
    evidence = ROOT / "artifacts/p0"
    evidence.mkdir(parents=True, exist_ok=True)
    archive = evidence / "staticlink-avalonia-nuget.zip"
    if not archive.exists():
        temporary = archive.with_suffix(".download")
        with temporary.open("wb") as stream:
            subprocess.run(["gh", "api", f"repos/greepar/StaticLink.Avalonia/actions/artifacts/{ARTIFACT_ID}/zip"],
                           stdout=stream, check=True, timeout=300)
        temporary.replace(archive)
    with archive.open("rb") as stream:
        if hashlib.file_digest(stream, "sha256").hexdigest() != ARCHIVE_SHA256:
            raise SystemExit("CI archive SHA-256 mismatch; refusing to use it")
    feed = evidence / "upstream-feed"
    feed.mkdir(exist_ok=True)
    with zipfile.ZipFile(archive) as bundle:
        for name, expected in PACKAGES.items():
            data = bundle.read(name)
            if hashlib.sha256(data).hexdigest() != expected:
                raise SystemExit(f"Package SHA-256 mismatch: {name}")
            target = feed / name
            if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest() == expected:
                continue
            with tempfile.NamedTemporaryFile(dir=feed, delete=False) as temporary:
                temporary.write(data)
                staged = Path(temporary.name)
            staged.replace(target)
    return feed


if __name__ == '__main__':
    prepare_feed()
    print('Verified pinned StaticLink archive and packages.')
