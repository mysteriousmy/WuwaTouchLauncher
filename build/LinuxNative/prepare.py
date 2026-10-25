#!/usr/bin/env python3
"""Prepare the pinned glibc 2.31 native closure; never install host libraries."""
import argparse
import hashlib
import json
import io
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
DEFAULT = ROOT / 'artifacts/linux-native/glibc231'


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def sources():
    return json.loads((HERE / 'sources.json').read_text())


def fetch_sources():
    cache = ROOT / 'artifacts/linux-native/sources'
    cache.mkdir(parents=True, exist_ok=True)
    for entry in sources():
        target = cache / entry['file']
        if not target.exists():
            previous = ROOT / 'artifacts/glibc231' / entry['group'] / entry['file']
            if previous.is_file() and sha(previous) == entry['sha256']:
                shutil.copy2(previous, target)
            else:
                temporary = target.with_suffix(target.suffix + '.download')
                try:
                    with urllib.request.urlopen(entry['url'], timeout=60) as response, temporary.open('wb') as stream:
                        remaining = entry['bytes'] + 1
                        while remaining:
                            chunk = response.read(min(1024 * 1024, remaining))
                            if not chunk:
                                break
                            stream.write(chunk)
                            remaining -= len(chunk)
                    if temporary.stat().st_size != entry['bytes'] or sha(temporary) != entry['sha256']:
                        raise ValueError('Source archive mismatch: ' + entry['file'])
                    temporary.replace(target)
                finally:
                    temporary.unlink(missing_ok=True)
        if target.stat().st_size != entry['bytes'] or sha(target) != entry['sha256']:
            raise ValueError('Source archive mismatch: ' + str(target))
    return cache


def wrappers(native):
    zig = ROOT / 'artifacts/p0/static-packages/vezel.zig.toolsets.linux-x64/0.17.0.1/tools/zig'
    if not zig.is_file():
        raise ValueError('Restore the locked linux-x64 project first to obtain Zig 0.17.0.1')
    (native / 'fonts').mkdir(parents=True, exist_ok=True)
    for name, mode in [('cc', 'cc'), ('cxx', 'c++')]:
        path = native / 'fonts' / name
        path.write_text('#!/usr/bin/env python3\nimport os,sys\na=sys.argv[1:]\nif "-E" in a: a=[x for x in a if x!="-c"]\nos.execv(' + repr(str(zig)) + ',[' + repr(str(zig)) + ',' + repr(mode) + ',"-target","x86_64-linux-gnu.2.31","-fno-sanitize=undefined",*a])\n')
        path.chmod(0o755)


def recipe_hash():
    digest = hashlib.sha256()
    for path in sorted(HERE.iterdir()):
        if path.is_file() and path.suffix in {".py", ".json"}:
            digest.update(path.name.encode() + b'\0' + path.read_bytes())
    return digest.hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=DEFAULT)
    parser.add_argument('--sources-only', action='store_true')
    parser.add_argument('--from-source', action='store_true', help='Build into a new empty output directory without the verified legacy cache.')
    args = parser.parse_args()
    cache = fetch_sources()
    if args.sources_only:
        print('LINUX_NATIVE_SOURCES_VERIFIED', flush=True)
        return
    native = args.output.resolve()
    if not native.is_relative_to(ROOT / 'artifacts/linux-native') or native == ROOT / 'artifacts/linux-native':
        raise ValueError('Use a dedicated directory below artifacts/linux-native/')
    manifest_path = native / 'manifest.json'
    if args.from_source and native.exists() and any(native.iterdir()):
        raise ValueError('--from-source requires a new empty output directory')
    if manifest_path.exists():
        manifest = json.loads(manifest_path.read_text())
        if manifest['recipe_sha256'] != recipe_hash():
            raise ValueError('Native recipes changed; choose a new --output directory to rebuild')
        for name, expected in manifest['files'].items():
            if sha(native / name) != expected:
                raise ValueError('Native cache mismatch: ' + name)
        wrappers(native)
        print('LINUX_NATIVE_CACHE_VERIFIED', flush=True)
        return
    native.mkdir(parents=True, exist_ok=True)
    seed = json.loads((HERE / 'verified-cache.json').read_text())
    legacy = ROOT / 'artifacts/glibc231'
    reused = not args.from_source and all((legacy / entry['source']).is_file() and sha(legacy / entry['source']) == entry['sha256'] for entry in seed.values())
    if reused:
        for name, entry in seed.items():
            target = native / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(legacy / entry['source'], target)
    else:
        # Never mix an incomplete former build with a different checkout/recipe.
        if any(native.iterdir()):
            raise ValueError('Incomplete native build; choose a new empty --output directory')
        for entry in sources():
            archive = cache / entry['file']
            destination = native / entry['extract']
            destination.mkdir(parents=True, exist_ok=True)
            if archive.suffix == '.deb':
                # Debian payloads are fixed by SHA-256. Keep their absolute
                # sysroot symlinks inside this isolated tree, never the host /lib.
                members = subprocess.check_output(['ar', 't', str(archive)], text=True).splitlines()
                payloads = [name for name in members if name.startswith('data.tar.')]
                if len(payloads) != 1:
                    raise ValueError('Expected one Debian data archive')
                payload = subprocess.check_output(['ar', 'p', str(archive), payloads[0]])
                def sysroot_filter(member, path):
                    if member.issym() and member.linkname.startswith('/'):
                        member = member.replace(linkname=os.path.relpath(
                            destination / member.linkname.lstrip('/'), (destination / member.name).parent))
                    return tarfile.data_filter(member, path)
                with tarfile.open(fileobj=io.BytesIO(payload)) as bundle:
                    bundle.extractall(destination, filter=sysroot_filter)
            elif archive.suffix == '.zip':
                with zipfile.ZipFile(archive) as bundle:
                    for name in bundle.namelist():
                        if not (destination / name).resolve().is_relative_to(destination):
                            raise ValueError('Unsafe ZIP path')
                    bundle.extractall(destination)
            else:
                with tarfile.open(archive) as bundle:
                    bundle.extractall(destination, filter='data')
        wrappers(native)
        for script in ['build_fonts.py', 'build_icu.py', 'build_openssl.py', 'build_unwind.py']:
            subprocess.run(['python3', str(HERE / script), str(native)], check=True)
    wrappers(native)
    files = {name: sha(native / name) for name in seed}
    manifest_path.write_text(json.dumps({'glibc_target': '2.31', 'recipe_sha256': recipe_hash(),
        'origin': 'verified-existing-archives' if reused else 'pinned-source-build', 'sources': sources(), 'files': files}, indent=2) + '\n')
    print('LINUX_NATIVE_READY: ' + str(native), flush=True)


if __name__ == '__main__':
    main()
