"""UPX PE verification: compare mapped bytes and loader metadata after unpacking.

UPX's PE writer rebuilds import/resource/debug tables and zeroes file padding.
It does not promise byte-identical PE files (`--exact` is unsupported). Compare
those tables structurally and all other bytes exactly; never skip .rdata.
"""
import hashlib
import importlib.util
from pathlib import Path
import struct
import subprocess


class Pe:
    def __init__(self, path):
        self.data = path.read_bytes(); self.mask = []
        pe = self.u32(60)
        if self.data[pe:pe + 6] != b'PE\0\0\x64\x86' or self.u16(pe + 24) != 0x20b:
            raise ValueError('Expected AMD64 PE32+')
        self.directories = pe + 24 + 112
        self.sections = []
        at = pe + 24 + self.u16(pe + 20)
        for i in range(self.u16(pe + 6)):
            name = self.data[at:at + 8].rstrip(b'\0')
            virtual, rva, size, raw = struct.unpack_from('<IIII', self.data, at + 8)
            self.sections.append((name, virtual, rva, size, raw))
            if size > virtual: self.exclude(raw + virtual, size - virtual)
            at += 40

    def u16(self, at): return struct.unpack_from('<H', self.data, at)[0]
    def u32(self, at): return struct.unpack_from('<I', self.data, at)[0]
    def exclude(self, at, size):
        if at < 0 or size < 0 or at + size > len(self.data): raise ValueError('PE range outside file')
        self.mask.append((at, size))
    def offset(self, rva):
        for _, _, start, size, raw in self.sections:
            if start <= rva < start + size: return raw + rva - start
        raise ValueError(f'PE RVA outside raw sections: {rva:x}')
    def directory(self, index): return struct.unpack_from('<II', self.data, self.directories + 8 * index)
    def string(self, rva, hint=False):
        at = self.offset(rva); start = at + (2 if hint else 0)
        end = self.data.index(0, start, min(start + 4096, len(self.data)))
        self.exclude(at, end + 1 - at)
        return self.data[start:end].decode('ascii')

    def imports(self):
        rva, size = self.directory(1); at = self.offset(rva); mapping = {}
        self.exclude(at, size)
        for descriptor in range(256):
            lookup, stamp, chain, name, iat = struct.unpack_from('<IIIII', self.data, at + descriptor * 20)
            if not any((lookup, stamp, chain, name, iat)): break
            dll = self.string(name).lower()
            for index in range(16384):
                thunk = self.offset((lookup or iat) + index * 8)
                value = struct.unpack_from('<Q', self.data, thunk)[0]
                self.exclude(thunk, 8); self.exclude(self.offset(iat + index * 8), 8)
                if not value: break
                symbol = ('ordinal', value & 0xffff) if value >> 63 else ('name', self.string(value, hint=True))
                mapping[iat + index * 8] = (dll, symbol)
            else: raise ValueError('Unterminated import thunk table')
        else: raise ValueError('Unterminated import directory')
        # UPX clears the optional IAT directory; the import descriptors retain
        # exact IAT slots, which are checked above against the original image.
        self.exclude(self.directories + 12 * 8, 8)
        return mapping

    def resources(self):
        rva, size = self.directory(2)
        if not rva: return {}
        base = self.offset(rva); resources = {}
        def walk(relative, key=()):
            if len(key) > 4: raise ValueError('Resource nesting exceeds limit')
            at = base + relative
            count = self.u16(at + 12) + self.u16(at + 14)
            for i in range(count):
                name, entry = struct.unpack_from('<II', self.data, at + 16 + i * 8)
                if name >> 31:
                    start = base + (name & 0x7fffffff)
                    label = self.data[start + 2:start + 2 + self.u16(start) * 2].decode('utf-16le')
                else: label = name
                if entry >> 31: walk(entry & 0x7fffffff, (*key, label))
                else:
                    data_rva, length, codepage, reserved = struct.unpack_from('<IIII', self.data, base + entry)
                    raw = self.offset(data_rva)
                    resources[(*key, label)] = (codepage, reserved, self.data[raw:raw + length])
        walk(0)
        # Resource directory placement/order may change. Every resource payload,
        # type/name/language and codepage must remain exactly equal.
        self.exclude(base, size)
        return resources

    def debug(self):
        rva, size = self.directory(6)
        if not rva: return []
        at = self.offset(rva); self.exclude(at, size)
        entries = []
        if size % 28: raise ValueError('Invalid debug directory')
        for offset in range(at, at + size, 28):
            flags, stamp, major, minor, kind, length, _, raw = struct.unpack_from('<IIHHIIII', self.data, offset)
            # UPX strips the CodeView/PDB directory entry; keep the original EXE
            # and PDB together for debugging. CET is runtime metadata and must
            # survive. Unknown entry kinds require a separate review.
            if kind not in (0, 2, 20): raise ValueError('Unreviewed debug directory kind')
            if kind == 20: entries.append((flags, stamp, major, minor, kind, self.data[raw:raw + length]))
        return sorted(entries)


def verify_roundtrip(source, restored):
    left, right = Pe(source), Pe(restored)
    if left.sections != right.sections: raise ValueError('UPX changed section layout')
    for method in ('imports', 'resources', 'debug'):
        if getattr(left, method)() != getattr(right, method)():
            raise ValueError('UPX changed PE ' + method)
    # Union includes the old and rebuilt table locations. All allowed ranges
    # were parsed from the original/restored metadata, never guessed by section.
    masks = left.mask + right.mask
    hashes = []
    for pe in (left, right):
        normalized = bytearray(pe.data)
        for at, size in masks: normalized[at:at + size] = b'\0' * size
        hashes.append(hashlib.sha256(normalized).hexdigest())
    if hashes[0] != hashes[1]: raise ValueError('UPX changed bytes outside verified PE metadata/padding')
    return {'mode': 'pe-mapped-bytes-and-loader-metadata', 'mapped_sha256': hashes[0],
            'restored_sha256': hashlib.sha256(right.data).hexdigest(),
            'codeview_directory': 'stripped; original executable and PDB retained',
            'byte_identical_file': left.data == right.data}


def pack(source, destination):
    spec = importlib.util.spec_from_file_location('pinned_upx', Path(__file__).parents[1] / 'LinuxSize/upx.py')
    upx = importlib.util.module_from_spec(spec); spec.loader.exec_module(upx)
    tool = upx.tool()
    subprocess.run([str(tool), '--best', '-o', str(destination), str(source)], check=True)
    subprocess.run([str(tool), '-t', str(destination)], check=True)
    restored = destination.with_suffix('.roundtrip.exe')
    try:
        subprocess.run([str(tool), '-d', '-o', str(restored), str(destination)], check=True)
        proof = verify_roundtrip(source, restored)
    finally: restored.unlink(missing_ok=True)
    if destination.stat().st_size >= source.stat().st_size: raise ValueError('UPX did not reduce size')
    return {'version': upx.VERSION, 'arguments': ['--best'], 'archive_sha256': upx.ARCHIVE_SHA256,
            'integrity_test': 'passed', 'roundtrip': proof}
