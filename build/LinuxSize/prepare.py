#!/usr/bin/env python3
"""Filter, compare and Brotli-compress ICU data from the pinned native closure."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

import brotli

ICU_DATA_SHA256 = "d5cf2a40dccbe471781ec7af85693bff542ff12f0b670c9630c4e72d60714b8b"


def run(args):
    subprocess.run([str(arg) for arg in args], check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--native-root", type=Path)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    native = (args.native_root or repo / "artifacts/linux-native/glibc231").resolve()
    output = args.output.resolve()
    if not output.is_relative_to(repo / "artifacts"):
        raise ValueError("The isolated output must be under artifacts/")
    data_dir = output / "icu"
    data_dir.mkdir(parents=True, exist_ok=True)
    data = native / "runtime/prefix/share/icu/78.3/icudt78l.dat"
    if hashlib.sha256(data.read_bytes()).hexdigest() != ICU_DATA_SHA256:
        raise ValueError("ICU data changed; revalidate the filter for this version")
    if brotli.__version__ != "1.2.0":
        raise ValueError("Use Brotli 1.2.0 for reproducible build-time compression")
    tool = native / "runtime/prefix/bin/icupkg"
    names = subprocess.check_output([str(tool), "-l", str(data)], text=True).splitlines()
    # .NET uses managed codepage conversion. The app does not use ICU's unit,
    # transliteration, or rule-based spelling APIs. Keep every locale and all
    # collation, calendar, timezone, Unicode, display-name, and break data.
    removed = [name for name in names if name.startswith(("unit/", "translit/", "rbnf/")) or name.endswith(".cnv")]
    removal_list = data_dir / "removed.txt"
    removal_list.write_text("\n".join(removed) + "\n")
    filtered = data_dir / "icudt78l.dat"
    run([tool, "-r", removal_list, data, filtered])
    raw = filtered.read_bytes()
    packed_path = data_dir / "icudt78l.dat.br"
    cached = data_dir / "manifest.json"
    old = json.loads(cached.read_text()) if cached.exists() else {}
    packed = packed_path.read_bytes() if packed_path.exists() else b""
    if (old.get("filtered_sha256") != hashlib.sha256(raw).hexdigest() or
        old.get("compressed_sha256") != hashlib.sha256(packed).hexdigest() or
        old.get("brotli") != brotli.__version__):
        packed = brotli.compress(raw, quality=11)
    if brotli.decompress(packed) != raw:
        raise ValueError("Compressed ICU roundtrip failed")
    packed_path.write_bytes(packed)
    assembly = data_dir / "data.S"
    assembly.write_text(
        '.section .rodata.wuwa_icu_data,"a",@progbits\n.balign 16\n'
        '.global wuwa_icu_brotli_begin\n.hidden wuwa_icu_brotli_begin\n'
        '.global wuwa_icu_brotli_end\n.hidden wuwa_icu_brotli_end\n'
        'wuwa_icu_brotli_begin:\n.incbin ' + json.dumps(str(packed_path), ensure_ascii=False) + '\n'
        'wuwa_icu_brotli_end:\n.section .note.GNU-stack,"",@progbits\n')
    cc = native / "fonts/cc"
    run([cc, "-c", assembly, "-o", data_dir / "data.o"])
    run([cc, "-c", repo / "build/LinuxSize/icu_loader.c", "-I" + str(native / "runtime/prefix/include"),
         "-I" + str(native / "fonts/prefix/include"), "-DWUWA_ICU_DATA_SIZE=" + str(len(raw)),
         "-Os", "-fPIC", "-ffunction-sections", "-fdata-sections", "-o", data_dir / "loader.o"])
    # A bounded differential probe: no data-file fallback, every shipped locale.
    runtime = repo / "artifacts/p0/static-packages/staticlink.avalonia/12.1.4.1/static/linux-x64/native/zig-runtime"
    compare = data_dir / "compare"
    run([cc, "-O2", "-I" + str(native / "runtime/prefix/include"), repo / "build/LinuxSize/icu_compare.c",
         native / "runtime/prefix/lib/libicui18n.a", native / "runtime/prefix/lib/libicuuc.a",
         native / "runtime/prefix/lib/libicudata.a", runtime / "libc++.a", runtime / "libc++abi.a",
         native / "unwind/libgcc_eh.a", "-lm", "-lpthread", "-ldl", "-o", compare])
    original_result = subprocess.check_output([str(compare), str(data)])
    filtered_result = subprocess.check_output([str(compare), str(filtered)])
    (data_dir / "original-locales.txt").write_bytes(original_result)
    (data_dir / "filtered-locales.txt").write_bytes(filtered_result)
    if original_result != filtered_result:
        raise ValueError("ICU locale behavior changed after filtering")
    manifest = {
        "icu": "78.3", "brotli": brotli.__version__, "original_sha256": ICU_DATA_SHA256,
        "original_bytes": data.stat().st_size, "filtered_bytes": len(raw), "compressed_bytes": len(packed),
        "filtered_sha256": hashlib.sha256(raw).hexdigest(), "compressed_sha256": hashlib.sha256(packed).hexdigest(),
        "removed": removed,
        "locale_comparison": original_result.decode().splitlines()[-1],
    }
    (data_dir / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"ICU: {data.stat().st_size} -> {len(raw)} filtered -> {len(packed)} embedded bytes", flush=True)


if __name__ == "__main__":
    main()
