#!/usr/bin/env python3
"""Pinned UPX tool and lossless, checked final executable packing."""
import hashlib
from pathlib import Path
import subprocess
import tarfile
import urllib.request

VERSION = '5.2.1'
ARCHIVE_SHA256 = '402162aad30af47e60dbd767fb2e64ca394ace9727ba1f40283641f1d1b91657'
ROOT = Path(__file__).resolve().parents[2]


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def tool():
    directory = ROOT / '.tools' / ('upx-' + VERSION)
    directory.mkdir(parents=True, exist_ok=True)
    name = 'upx-' + VERSION + '-amd64_linux'
    archive = directory / (name + '.tar.xz')
    if not archive.exists():
        temporary = archive.with_suffix('.download')
        try:
            url = 'https://github.com/upx/upx/releases/download/v' + VERSION + '/' + archive.name
            with urllib.request.urlopen(url, timeout=60) as response:
                data = response.read(664049)
            if len(data) != 664048 or hashlib.sha256(data).hexdigest() != ARCHIVE_SHA256:
                raise ValueError('UPX download mismatch')
            temporary.write_bytes(data)
            temporary.replace(archive)
        finally:
            temporary.unlink(missing_ok=True)
    if sha(archive) != ARCHIVE_SHA256:
        raise ValueError('UPX archive checksum mismatch')
    executable = directory / name / 'upx'
    with tarfile.open(archive) as bundle:
        data = bundle.extractfile(name + '/upx').read()
    executable.parent.mkdir(exist_ok=True)
    if not executable.exists() or sha(executable) != hashlib.sha256(data).hexdigest():
        executable.write_bytes(data)
        executable.chmod(0o755)
    version = subprocess.check_output([str(executable), '--version'], text=True).splitlines()[0]
    if version != 'upx ' + VERSION:
        raise ValueError('Unexpected UPX version: ' + version)
    return executable


def pack(source, destination):
    upx = tool()
    subprocess.run([str(upx), '--best', '-o', str(destination), str(source)], check=True)
    subprocess.run([str(upx), '-t', str(destination)], check=True)
    restored = destination.with_suffix('.roundtrip')
    try:
        subprocess.run([str(upx), '-d', '-o', str(restored), str(destination)], check=True)
        if sha(restored) != sha(source):
            raise ValueError('UPX decompression changed the native executable')
    finally:
        restored.unlink(missing_ok=True)
    if destination.stat().st_size >= source.stat().st_size:
        raise ValueError('UPX did not reduce executable size')
    return {'version': VERSION, 'arguments': ['--best'], 'archive_sha256': ARCHIVE_SHA256,
            'integrity_test': 'passed', 'decompression_sha256': sha(source)}
