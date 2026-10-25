#!/usr/bin/env python3
"""Reject non-Windows link inputs before invoking the pinned MSVC-ABI linker."""
import hashlib
import json
from pathlib import Path
import shlex
import subprocess
import struct
import sys


def check_object(data, label):
    if data[:2] not in (b'\x64\x86', b'\x00\x00') and not data.startswith(b'BC\xc0\xde'):
        raise ValueError(f'Not an AMD64 COFF/import/LLVM object: {label}')
    if data.startswith(b'\x00\x00'):
        if data[2:4] == b'\xff\xff':
            if data[6:8] != b'\x64\x86': raise ValueError(f'Not an AMD64 import/bigobj: {label}')
        else:
            # MSVC's sdknames members are architecture-neutral COFF aliases
            # containing only debug metadata, not executable machine code.
            count = struct.unpack_from('<H', data, 2)[0]
            start = 20 + struct.unpack_from('<H', data, 16)[0]
            if not count or start + 40 * count > len(data): raise ValueError(f'Invalid neutral COFF: {label}')
            for i in range(count):
                name = data[start + i * 40:start + i * 40 + 8].rstrip(b'\0')
                if name not in (b'.debug$S', b'.drectve'):
                    raise ValueError(f'Unexpected neutral COFF section {name!r}: {label}')


def verify(path):
    data = path.read_bytes()
    if data.startswith(b'!<arch>\n'):
        offset = 8
        while offset < len(data):
            header = data[offset:offset + 60]
            if len(header) != 60 or header[58:] != b'`\n':
                raise ValueError(f'Invalid archive: {path}')
            size = int(header[48:58]); name = header[:16].strip()
            member = data[offset + 60:offset + 60 + size]
            if name not in (b'/', b'//', b'/SYM64/'):
                check_object(member, f'{path}:{name!r}')
            offset += 60 + size + size % 2
    else:
        check_object(data, path)
    return hashlib.sha256(data).hexdigest()


def main():
    root = Path(sys.argv[1]); settings = json.loads((root / 'toolchain.json').read_text())
    args = sys.argv[2:]; expanded = []
    for arg in args:
        expanded.extend(shlex.split(Path(arg[1:]).read_text(encoding='utf-8-sig')) if arg.startswith('@') else [arg])
    directories = [Path(a.split(':', 1)[1]) for a in expanded if a.lower().startswith('/libpath:')]
    inputs = {}
    for arg in expanded:
        low = arg.lower()
        if any(x in low for x in ('.so', '.a"', '/linux-', 'x86_64-linux', '-lfontconfig', '-lwayland', '-lx11', '-lstdc++')) or low.endswith('.a'):
            raise ValueError(f'Linux/Unix link input rejected: {arg}')
        name = arg.split(':', 1)[1] if low.startswith('/defaultlib:') else arg
        if not name.lower().endswith(('.lib', '.obj')) or low.startswith('/nodefaultlib:'):
            continue
        path = Path(name)
        if not path.is_file():
            path = next((p for d in directories for p in d.iterdir() if p.name.lower() == name.lower()), None)
        if path is None or not path.is_file():
            raise ValueError(f'Unresolved Windows native input: {name}')
        inputs[str(path.resolve())] = verify(path)
    if not inputs:
        raise ValueError('No native inputs checked')
    (root / 'link-inputs.json').write_text(json.dumps(inputs, indent=2) + '\n')
    print(f'WINDOWS_LINK_INPUTS_PASS: {len(inputs)} AMD64 COFF libraries/objects; no ELF inputs', flush=True)
    return subprocess.run([settings['lld_link'], *args], check=False).returncode


if __name__ == '__main__':
    sys.exit(main())
