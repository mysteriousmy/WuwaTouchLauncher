#!/usr/bin/env python3
"""Restore hash-pinned build inputs included in the public source checkout."""
import hashlib
import json
from pathlib import Path
import shutil
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    manifest = ROOT / 'vendor/build-inputs/manifest.json'
    for item in json.loads(manifest.read_text()):
        target = ROOT / item['destination']
        if target.is_file() and sha(target) == item['sha256']:
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(dir=target.parent, delete=False) as output:
            temporary = Path(output.name)
            try:
                for part in item['parts']:
                    source = ROOT / part['path']
                    if sha(source) != part['sha256']:
                        raise ValueError('Build input checksum mismatch: ' + part['path'])
                    with source.open('rb') as stream:
                        shutil.copyfileobj(stream, output)
                output.flush()
                if sha(temporary) != item['sha256']:
                    raise ValueError('Reassembled input checksum mismatch: ' + item['destination'])
            except BaseException:
                temporary.unlink(missing_ok=True)
                raise
        temporary.replace(target)
    print('BUILD_INPUTS_PASS: pinned source archives and NuGet inputs restored.')


if __name__ == '__main__':
    main()
