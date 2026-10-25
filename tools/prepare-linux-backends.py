#!/usr/bin/env python3
"""Build the hash-pinned local Avalonia Linux backends; no global NuGet/SDK mutations."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tarfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent.parent
BUILD = ROOT / 'build/LinuxBackends'
WORK = ROOT / 'artifacts/linux-overlay'
SOURCE = json.loads((BUILD / 'source.json').read_text())


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def normalize_package(path):
    # NuGet uses build-time ZIP timestamps, which otherwise break locked restore
    # after rebuilding identical deterministic assemblies on another checkout.
    temporary = path.with_suffix('.normalized')
    with zipfile.ZipFile(path) as source, zipfile.ZipFile(temporary, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as target:
        for name in sorted(source.namelist()):
            info = zipfile.ZipInfo(name, (2026, 10, 17, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            target.writestr(info, source.read(name))
    temporary.replace(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--force', action='store_true', help='rebuild even when the input stamp matches')
    parser.add_argument('--update-locks', action='store_true', help='intentional local backend dependency update')
    args = parser.parse_args()
    WORK.mkdir(parents=True, exist_ok=True)
    fingerprint = hashlib.sha256()
    for path in sorted(BUILD.rglob('*')):
        if path.is_file():
            fingerprint.update(str(path.relative_to(BUILD)).encode())
            fingerprint.update(path.read_bytes())
    fingerprint.update(Path(__file__).read_bytes())
    stamp = fingerprint.hexdigest()
    feed = WORK / 'feed'
    feed.mkdir(exist_ok=True)
    names = ('Skia', 'X11', 'Wayland')
    packages = [feed / f'Avalonia.{name}.{SOURCE["packageVersion"]}.nupkg' for name in names]
    stamp_path = WORK / 'build-stamp.json'
    if not args.force and not args.update_locks and stamp_path.exists():
        old = json.loads(stamp_path.read_text())
        if old.get('inputs') == stamp and all(p.exists() and old.get(p.name) == digest(p) for p in packages):
            print('Verified local Avalonia backends are current.')
            return
    archive = WORK / SOURCE['archive']
    # Source kits carry the same verified upstream archive for offline rebuilds.
    bundled = ROOT / 'artifacts/relink-sources' / SOURCE['archive']
    if not archive.exists() and bundled.is_file() and digest(bundled) == SOURCE['sha256']:
        shutil.copyfile(bundled, archive)
    if not archive.exists():
        temporary = archive.with_suffix('.download')
        try:
            with urllib.request.urlopen(SOURCE['url'], timeout=60) as response, temporary.open('wb') as stream:
                size = 0
                while chunk := response.read(1024 * 1024):
                    size += len(chunk)
                    if size > 100 * 1024 * 1024:
                        raise RuntimeError('Avalonia source archive exceeds 100 MiB')
                    stream.write(chunk)
            if digest(temporary) != SOURCE['sha256']:
                raise RuntimeError('Avalonia source hash mismatch')
            temporary.replace(archive)
        finally:
            temporary.unlink(missing_ok=True)
    if digest(archive) != SOURCE['sha256']:
        raise RuntimeError('Avalonia source hash mismatch')
    source_parent = WORK / 'Avalonia-source'
    if source_parent.exists():
        shutil.rmtree(source_parent)
    source_parent.mkdir()
    with tarfile.open(archive) as tar:
        tar.extractall(source_parent, filter='data')
    source = source_parent / ('Avalonia-' + SOURCE['commit'])
    subprocess.run(['patch', '--batch', '--forward', '-p1', '-i', str(BUILD / SOURCE['patch'])],
                   cwd=source, check=True, stdout=subprocess.DEVNULL)
    dotnet = ROOT / '.tools/dotnet/dotnet'
    if not dotnet.exists(): dotnet = Path(shutil.which('dotnet') or 'dotnet')
    for name, package in zip(names, packages):
        project = BUILD / name / f'Avalonia.{name}.csproj'
        with (WORK / f'{name.lower()}-build.log').open('w') as log:
            for command in ([str(dotnet), 'restore', str(project), '--force-evaluate' if args.update_locks else '--locked-mode'],
                            [str(dotnet), 'pack', str(project), '-c', 'Release', '--no-restore', '--nologo', '-o', str(feed)]):
                subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, check=True)
        normalize_package(package)
        # Only replace this repository's private, patched version. Never touch the user's cache.
        cached = ROOT / 'artifacts/p0/static-packages' / f'avalonia.{name.lower()}' / SOURCE['packageVersion']
        cached_package = cached / package.name.lower()
        if cached.exists() and (not cached_package.exists() or digest(cached_package) != digest(package)):
            shutil.rmtree(cached)
        print(f'Built {package.name}: {digest(package)}')
    stamp_path.write_text(json.dumps({'inputs': stamp, **{p.name: digest(p) for p in packages}}, indent=2) + '\n')


if __name__ == '__main__':
    main()
