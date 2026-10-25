#!/usr/bin/env python3
"""Publish isolated NativeAOT + static graphics builds, audit and UPX-pack them."""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = ROOT / "artifacts/p0/static-packages"
DOTNET = str(ROOT / ".tools/dotnet/dotnet") if (ROOT / ".tools/dotnet/dotnet").exists() else "dotnet"


def sha(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def run(command, **kwargs):
    subprocess.run([str(c) for c in command], cwd=ROOT, check=True, **kwargs)


def snapshot(destination):
    for directory in ("src", "assets", "build"):
        shutil.copytree(ROOT / directory, destination / directory,
                        ignore=shutil.ignore_patterns("bin", "obj", "__pycache__"))
    for name in ("global.json", "Directory.Build.props", "NuGet.Config"):
        shutil.copy2(ROOT / name, destination / name)
    return source_hashes(destination)


def source_hashes(destination):
    return {str(p.relative_to(destination)): sha(p)
              for p in sorted(destination.rglob("*")) if p.is_file() and
              not {"bin", "obj", ".artifacts", ".git", "GeneratedFiles"}.intersection(p.relative_to(destination).parts)}


def use_atomui_source(source, library):
    atom = source / "libs/AtomUI"
    shutil.copytree(library, atom, ignore=shutil.ignore_patterns("bin", "obj", ".artifacts", ".git", "GeneratedFiles"))
    project = source / "src/WuwaTouchLauncher.csproj"
    text = project.read_text()
    package = '<PackageReference Include="AtomUI.Desktop.Controls" Version="6.2.7" />'
    if package not in text:
        raise ValueError("Update the relink adapter for the selected AtomUI version.")
    text = text.replace(package, f'<ProjectReference Include="{atom}/src/AtomUI.Desktop.Controls/AtomUI.Desktop.Controls.csproj" />')
    project.write_text(text)
    props = source / "Directory.Build.props"
    props.write_text(props.read_text().replace("<Project>", f'<Project><Import Project="{atom}/build/AtomUI.Repository.props" '
                    'Condition="\'$(MSBuildProjectName)\' == \'WuwaTouchLauncher\'" />'
                    '<PropertyGroup Condition="\'$(MSBuildProjectName)\' == \'WuwaTouchLauncher\'">'
                    '<BaseIntermediateOutputPath>obj/</BaseIntermediateOutputPath>'
                    '<IntermediateOutputPath>obj/$(Configuration)/</IntermediateOutputPath>'
                    '<BaseOutputPath>bin/</BaseOutputPath><OutputPath>bin/$(Configuration)/</OutputPath>'
                    '</PropertyGroup>', 1))
    (source / "Directory.Build.targets").write_text(f'<Project><Import Project="{atom}/build/AtomUI.Repository.targets" '
                    'Condition="\'$(MSBuildProjectName)\' == \'WuwaTouchLauncher\'" /></Project>')


def windows_toolchain(work, sdk):
    native = work / "native"
    native.mkdir()
    tools = {name: shutil.which(name) for name in
             ("clang", "lld-link", "llvm-readobj", "llvm-ar", "llvm-rc", "pwsh")}
    if any(value is None for value in tools.values()):
        raise ValueError("Windows cross-build needs LLVM and PowerShell Core.")
    directories = [sdk / p for p in ("sdk/lib/um/x86_64", "sdk/lib/ucrt/x86_64", "crt/lib/x86_64")]
    if not all(d.is_dir() for d in directories):
        raise ValueError("Prepare the pinned SDK: see build/WindowsNative/README.md.")
    roots, hashes = [], {}
    for directory in directories:
        names = {}
        for path in sorted(directory.iterdir()):
            if path.is_file():
                names.setdefault(path.name.lower(), path.resolve())
        roots.append({"type": "directory", "name": str(directory), "contents": [
            {"type": "file", "name": name, "external-contents": str(path)} for name, path in names.items()]})
        hashes.update({str(directory.relative_to(sdk) / name): sha(path) for name, path in names.items()})
    digest = hashlib.sha256(json.dumps(hashes, sort_keys=True).encode()).hexdigest()
    pins = json.loads((ROOT / "build/WindowsNative/sources.json").read_text())
    if digest != pins["sdk_libraries_sha256"]:
        raise ValueError("Windows SDK libraries differ from the validated pin: " + digest)
    (native / "sdk-casefold.json").write_text(json.dumps({"version": 0, "case-sensitive": False, "roots": roots}))
    (native / "toolchain.json").write_text(json.dumps({"lld_link": tools["lld-link"], "sdk_digest": digest}))
    run([tools["clang"], "--target=x86_64-pc-windows-msvc", "-fms-extensions", "-c",
         ROOT / "build/WindowsNative/static-longjmp.c", "-isystem", sdk / "crt/include",
         "-isystem", sdk / "sdk/include/ucrt", "-o", native / "static-longjmp.obj"])
    launcher = native / "link"
    launcher.write_text("#!/bin/sh\nexec " + shlex.join(
        [sys.executable, str(ROOT / "build/WindowsNative/link.py"), str(native)]) + ' "$@"\n')
    launcher.chmod(0o755)
    powershell = native / "powershell"
    powershell.write_text("#!/bin/sh\nexec " + shlex.quote(tools["pwsh"]) + ' "$@"\n')
    powershell.chmod(0o755)
    return native, digest


def inspect_elf(binary):
    dynamic = subprocess.check_output(["readelf", "-dW", str(binary)], text=True)
    needed = sorted(re.findall(r"\(NEEDED\).*?\[(.*?)\]", dynamic))
    expected = sorted(["libc.so.6", "libdl.so.2", "libm.so.6", "libpthread.so.0", "librt.so.1", "ld-linux-x86-64.so.2"])
    if needed != expected:
        raise ValueError("Native dependency closure changed: " + repr(needed))
    symbols = subprocess.check_output(["readelf", "--dyn-syms", "-W", str(binary)], text=True)
    floor = max(set(re.findall(r"GLIBC_(\d+\.\d+)", symbols)), key=lambda v: tuple(map(int, v.split("."))))
    if tuple(map(int, floor.split("."))) > (2, 31):
        raise ValueError("glibc symbol floor exceeds 2.31: " + floor)
    exports = [line.split()[-1] for line in symbols.splitlines()
               if len(line.split()) >= 8 and line.split()[4] in {"GLOBAL", "WEAK"} and line.split()[6] != "UND"]
    if len(exports) > 16:
        raise ValueError("NativeAOT export pruning did not take effect")
    return {"needed": needed, "max_glibc_symbol": floor, "exports": exports}


def inspect_pe(binary, work):
    imports = subprocess.check_output(["llvm-readobj", "--coff-imports", "--file-headers", str(binary)], text=True)
    (work / "pe-imports.txt").write_text(imports)
    dlls = sorted(set(re.findall(r"^  Name: (.+)$", imports, re.M)))
    system = {"user32.dll", "ole32.dll", "oleaut32.dll", "advapi32.dll", "bcrypt.dll", "crypt32.dll",
              "iphlpapi.dll", "kernel32.dll", "ncrypt.dll", "secur32.dll", "ws2_32.dll", "gdi32.dll",
              "dxgi.dll", "d3d9.dll", "shell32.dll"}
    unexpected = [d for d in dlls if d.lower() not in system and
                  not d.lower().startswith(("api-ms-win-crt-", "api-ms-win-core-"))]
    if "IMAGE_FILE_MACHINE_AMD64" not in imports or unexpected:
        raise ValueError("Unexpected Windows imports: " + repr(unexpected))
    return {"system_imports": dlls}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("rid", choices=["linux-x64", "win-x64"])
    parser.add_argument("--output", type=Path)
    parser.add_argument("--sdk-root", type=Path, default=ROOT / "artifacts/p0/windows-sdk")
    parser.add_argument("--atomui-source", type=Path, help="Rebuild with this modified AtomUI source directory.")
    args = parser.parse_args()
    if args.atomui_source and not args.output:
        parser.error("--atomui-source requires a separate --output directory")
    if subprocess.check_output([DOTNET, "--version"], text=True, cwd=ROOT).strip() != "10.0.402":
        raise ValueError("Install the SDK pinned in global.json (10.0.402).")
    module("static_feed", ROOT / "tools/prepare-static-feed.py").prepare_feed()
    feed = ROOT / "artifacts/linux-overlay/feed"
    if not all((feed / f"Avalonia.{name}.12.1.4.1.nupkg").is_file() for name in ("Skia", "X11", "Wayland")):
        run([sys.executable, ROOT / "tools/prepare-linux-backends.py"])
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
    work = ROOT / "artifacts/publish-builds" / (args.rid + "-" + stamp)
    work.mkdir(parents=True)
    source = work / "source"
    snapshot(source)
    if args.atomui_source:
        use_atomui_source(source, args.atomui_source.resolve())
    hashes = source_hashes(source)
    (work / "source-hashes.json").write_text(json.dumps(hashes, indent=2) + "\n")
    source_digest = hashlib.sha256(json.dumps(hashes, sort_keys=True).encode()).hexdigest()
    environment = dict(os.environ, NUGET_PACKAGES=str(PACKAGES))
    flags = ["-p:UseAotAnywhere=true", "-p:RestoreConfigFile=" + str(ROOT / "NuGet.Config"),
             "-p:RestorePackagesPath=" + str(PACKAGES), "-p:RuntimeIdentifier=" + args.rid]
    lock = ROOT / "src" / f"packages.{args.rid}.lock.json"
    if lock.exists() and not args.atomui_source:
        shutil.copy2(lock, source / "src/packages.lock.json")
        flags += ["-p:RestoreLockedMode=true"]
    if args.atomui_source:
        flags += ["-p:AvaloniaVersion=12.1.4", "-p:AtomUITargetFrameworks=net10.0",
                  "-p:LibTargetFrameworks=netstandard2.0", "-p:RestoreLockedMode=false"]
    # Native preparation needs the pinned Zig package, including on a clean checkout.
    with (work / "restore.log").open("w") as log:
        run([DOTNET, "restore", source / "src/WuwaTouchLauncher.csproj", "-r", args.rid, *flags],
            env=environment, stdout=log, stderr=subprocess.STDOUT)
    metadata = {}
    if args.rid == "linux-x64":
        native = ROOT / "artifacts/linux-native/glibc231"
        run([sys.executable, ROOT / "build/LinuxNative/prepare.py", "--output", native])
        run([sys.executable, ROOT / "build/LinuxSize/prepare.py", "--output", ROOT / "artifacts/linux-release",
             "--native-root", native])
        environment["CMAKE_PREFIX_PATH"] = f"{native}/runtime/openssl36-prefix:{native}/runtime/prefix"
        environment["PKG_CONFIG_PATH"] = f"{native}/runtime/openssl36-prefix/lib/pkgconfig:{native}/runtime/prefix/lib/pkgconfig"
        system_libraries = Path(subprocess.check_output(["cc", "-print-file-name=libgcc_s.so.1"], text=True).strip()).resolve().parent
        flags += ["-p:LinkerFlavor=bfd", "-p:WuwaCompactLinux=true", "-p:WuwaCompressIcu=true",
                  "-p:WuwaLinuxGlibcVersion=2.31", "-p:WuwaLinuxSystemLibraryDir=" + str(system_libraries),
                  "-p:WuwaLinuxNativeRoot=" + str(native),
                  "-p:WuwaCompactIcuDirectory=" + str(ROOT / "artifacts/linux-release/icu")]
        metadata["native_manifest_sha256"] = sha(native / "manifest.json")
        metadata["icu"] = json.loads((ROOT / "artifacts/linux-release/icu/manifest.json").read_text())
    else:
        native, digest = windows_toolchain(work, args.sdk_root.resolve())
        environment["PATH"] = str(native) + os.pathsep + environment["PATH"]
        flags += ["-p:WuwaWindowsNative=true", "-p:_AotAnywhereWinDirectLinkActive=false",
                  "-p:WuwaWindowsNativeRoot=" + str(native), "-p:WuwaWindowsSdkRoot=" + str(args.sdk_root.resolve())]
        metadata["sdk_libraries_sha256"] = digest
    raw = work / "unpacked"
    command = [DOTNET, "publish", str(source / "src/WuwaTouchLauncher.csproj"), "-c", "Release", "-r", args.rid, "--no-restore",
               *flags, "-bl:" + str(work / "publish.binlog"), "-o", str(raw)]
    (work / "command.json").write_text(json.dumps(command, indent=2) + "\n")
    print("NativeAOT build log: " + str(work / "publish.log"), flush=True)
    with (work / "publish.log").open("w") as log:
        completed = subprocess.run(command, env=environment, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT)
    log_text = (work / "publish.log").read_text()
    if completed.returncode:
        print("\n".join(log_text.splitlines()[-50:]))
        raise SystemExit(completed.returncode)
    if re.search(r"warning (?:IL\d+|AOT\d+|SYSLIB\d+)", log_text):
        raise ValueError("AOT/trimming/interop warning requires review: " + str(work / "publish.log"))
    # Save the per-RID lock beside the build so a second platform cannot overwrite it.
    shutil.copy2(source / "src/packages.lock.json", work / "packages.lock.json")
    hashes = source_hashes(source)
    (work / "source-hashes.json").write_text(json.dumps(hashes, indent=2) + "\n")
    source_digest = hashlib.sha256(json.dumps(hashes, sort_keys=True).encode()).hexdigest()
    name = "wuwa-touch-launcher" + (".exe" if args.rid == "win-x64" else "")
    binary = raw / name
    extra = [p.name for p in raw.iterdir() if p.name != name and p.suffix not in {".dbg", ".pdb"}]
    if extra:
        raise ValueError("Unexpected runtime companion files: " + repr(extra))
    metadata.update(inspect_elf(binary) if args.rid == "linux-x64" else inspect_pe(binary, work))
    output = (args.output or ROOT / "artifacts/publish" / args.rid).resolve()
    if not output.is_relative_to(ROOT / "artifacts") or output == ROOT / "artifacts":
        raise ValueError("Output must be a dedicated directory under artifacts/.")
    if output.exists():
        if not (output / "build-manifest.json").is_file():
            raise ValueError("Output contains unrelated files; choose another output directory.")
        output.rename(work / "previous-publish")
    output.mkdir(parents=True)
    packer = ROOT / ("build/LinuxSize/upx.py" if args.rid == "linux-x64" else "build/WindowsNative/upx.py")
    packed = output / name
    upx = module("publisher_upx", packer).pack(binary, packed)
    version = ET.parse(source / "src/WuwaTouchLauncher.csproj").findtext(".//Version")
    result = {"version": version, "rid": args.rid, "native_aot": True, "source_sha256": source_digest,
              "custom_atomui_source": bool(args.atomui_source),
              "build_directory": str(work), "unpacked": {"path": str(binary), "sha256": sha(binary), "bytes": binary.stat().st_size},
              "packed": {"path": str(packed), "sha256": sha(packed), "bytes": packed.stat().st_size},
              "upx": upx, **metadata}
    (output / "build-manifest.json").write_text(json.dumps(result, indent=2) + "\n")
    print(f"PUBLISH_PASS: {args.rid}: {binary.stat().st_size} -> {packed.stat().st_size} bytes", flush=True)
    print(packed, flush=True)


if __name__ == "__main__":
    main()
