#!/usr/bin/env python3
"""Preserve NativeAOT's export script when using the pinned Zig 0.17 driver.

Zig resolves its own glibc sysroot first. Replay that invocation through its
bundled LLD, adding the export script that zig cc currently drops for executables.
No system linker, alternate libc, or runtime library is substituted.
"""
import argparse
import json
from pathlib import Path
import shlex
import subprocess
import sys


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--zig", required=True)
    parser.add_argument("--create-launcher", type=Path)
    parser.add_argument("--wrap", action="append", default=[])
    parser.add_argument("--defer-object", action="append", default=[])
    parser.add_argument("args", nargs=argparse.REMAINDER)
    options = parser.parse_args()
    zig = str(Path(options.zig).resolve())
    if options.create_launcher:
        fixed = [str(Path(__file__).resolve()), "--zig", zig]
        for symbol in options.wrap:
            fixed.extend(["--wrap", symbol])
        for obj in options.defer_object:
            fixed.extend(["--defer-object", str(Path(obj).resolve())])
        options.create_launcher.parent.mkdir(parents=True, exist_ok=True)
        options.create_launcher.write_text(
            "#!/usr/bin/env python3\nimport runpy, sys\n"
            f"sys.argv = {fixed!r} + ['--'] + sys.argv[1:]\n"
            f"runpy.run_path({str(Path(__file__).resolve())!r}, run_name='__main__')\n")
        options.create_launcher.chmod(0o755)
        return
    args = options.args[1:] if options.args[:1] == ["--"] else options.args
    if args[:1] != ["cc"] or "-o" not in args:
        raise RuntimeError("Expected the Linux AotAnywhere executable link invocation")
    version = subprocess.check_output([zig, "version"], text=True).strip()
    if version != "0.17.0":
        raise RuntimeError(f"Revalidate this bounded adaptation for Zig {version}")
    scripts = [arg.split("=", 1)[1] for arg in args if arg.startswith("-Wl,--version-script=")]
    if len(scripts) != 1 or not Path(scripts[0]).is_file():
        raise RuntimeError("Expected one existing NativeAOT export script")
    output = Path(args[args.index("-o") + 1]).resolve()
    temporary = output.with_name(output.name + ".zig-driver")
    driver_args = args.copy()
    for obj in options.defer_object:
        if driver_args.count(obj) != 1:
            raise RuntimeError("Expected exactly one deferred wrapper object")
        driver_args.remove(obj)
    driver_args[driver_args.index("-o") + 1] = str(temporary)
    try:
        driver = subprocess.run([zig, *driver_args, "-v"], text=True, capture_output=True)
        if driver.returncode:
            sys.stderr.write(driver.stdout + driver.stderr)
            raise subprocess.CalledProcessError(driver.returncode, [zig, *driver_args])
        lines = [line for line in driver.stderr.splitlines() if line.startswith("ld.lld ")]
        if len(lines) != 1:
            raise RuntimeError("Zig's resolved link invocation changed; refusing to guess")
        linker_args = shlex.split(lines[0])[1:]
        if linker_args[linker_args.index("-o") + 1] != str(temporary):
            raise RuntimeError("Resolved output path differs from the requested output")
        linker_args.extend(["--version-script=" + scripts[0]])
        linker_args.extend("--wrap=" + symbol for symbol in options.wrap)
        linker_args.extend(options.defer_object)
        command = [zig, "ld.lld", *linker_args]
        output.with_name(output.name + ".link.json").write_text(json.dumps(command, indent=2) + "\n")
        subprocess.run(command, check=True)
        temporary.replace(output)
    finally:
        temporary.unlink(missing_ok=True)


if __name__ == "__main__":
    main()
