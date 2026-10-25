#!/usr/bin/env python3
"""Build the pinned font/SQLite closure without installing host configuration."""
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess

import sys
repo = Path(__file__).resolve().parents[2]
root = Path(sys.argv[1]).resolve() / "fonts"
root.mkdir(parents=True, exist_ok=True)
src, prefix = root / 'src', root / 'prefix'
prefix.mkdir(exist_ok=True)
(root / 'logs').mkdir(exist_ok=True)
zig = repo / 'artifacts/p0/static-packages/vezel.zig.toolsets.linux-x64/0.17.0.1/tools/zig'
for name, mode in [('cc', 'cc'), ('cxx', 'c++')]:
    wrapper = root / name
    # Zig 0.17 treats Meson's `-E ... -c` as compilation; remove the redundant
    # compile flag only for preprocessing, preserving all target/compiler flags.
    wrapper.write_text('#!/usr/bin/env python3\nimport os,sys\na=sys.argv[1:]\nif "-E" in a: a=[x for x in a if x!="-c"]\nos.execv(' + repr(str(zig)) + ',[' + repr(str(zig)) + ',' + repr(mode) + ',"-target","x86_64-linux-gnu.2.31","-fno-sanitize=undefined",*a])\n')
    wrapper.chmod(0o755)
env = dict(os.environ, CC=str(root / 'cc'), CXX=str(root / 'cxx'), PKG_CONFIG_PATH=str(prefix / 'lib/pkgconfig'))

def run(name, args, cwd=None):
    with (root / 'logs' / (name + '.log')).open('a') as log:
        log.write('\n' + shlex.join(map(str, args)) + '\n'); log.flush()
        subprocess.run(list(map(str, args)), cwd=cwd, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)

def cmake(name, directory, *options):
    build = root / ('build-' + name)
    run(name, ['cmake', '-S', src / directory, '-B', build, '-G', 'Ninja', '-DCMAKE_BUILD_TYPE=Release',
        '-DCMAKE_INSTALL_PREFIX=' + str(prefix), '-DCMAKE_INSTALL_LIBDIR=lib', '-DCMAKE_PREFIX_PATH=' + str(prefix),
        '-DCMAKE_POSITION_INDEPENDENT_CODE=ON', '-DBUILD_SHARED_LIBS=OFF', '-DCMAKE_C_FLAGS=-ffunction-sections -fdata-sections',
        '-DCMAKE_CXX_FLAGS=-ffunction-sections -fdata-sections', *options])
    run(name, ['cmake', '--build', build, '-j', '6'])
    run(name, ['cmake', '--install', build])
    print(name + ' built', flush=True)

def bzip():
    d = src / 'bzip2-1.0.8'
    run('bzip2', ['make', '-j6', 'libbz2.a', 'CC=' + str(root/'cc'), 'CFLAGS=-O2 -fPIC -ffunction-sections -fdata-sections'], d)
    (prefix/'include').mkdir(exist_ok=True); (prefix/'lib').mkdir(exist_ok=True)
    shutil.copy2(d/'bzlib.h', prefix/'include'); shutil.copy2(d/'libbz2.a', prefix/'lib')
    print('bzip2 built', flush=True)

def harfbuzz():
    build = root/'build-harfbuzz'
    if not (build/'build.ninja').exists():
        run('harfbuzz', ['meson', 'setup', build, src/'harfbuzz-14.6.0', '--prefix=' + str(prefix), '--libdir=lib',
            '--buildtype=release', '--default-library=static', '-Db_lto=false', '-Dauto_features=disabled',
            '-Dtests=disabled', '-Ddocs=disabled', '-Dutilities=disabled', '-Dcpp_args=-ffunction-sections -fdata-sections'])
    run('harfbuzz', ['ninja', '-C', build, '-j6']); run('harfbuzz', ['meson', 'install', '-C', build])
    print('harfbuzz built (core shaping for FreeType auto-hinting)', flush=True)

tasks = [lambda: cmake('zlib', 'zlib-ng-2.3.3', '-DZLIB_COMPAT=ON', '-DBUILD_TESTING=OFF', '-DWITH_NATIVE_INSTRUCTIONS=OFF'),
         lambda: cmake('expat', 'expat-2.9.0', '-DEXPAT_BUILD_TESTS=OFF', '-DEXPAT_BUILD_EXAMPLES=OFF', '-DEXPAT_BUILD_TOOLS=OFF', '-DEXPAT_BUILD_DOCS=OFF'),
         lambda: cmake('brotli', 'brotli-1.2.0', '-DBROTLI_DISABLE_TESTS=ON'), bzip, harfbuzz]
with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
    jobs = [pool.submit(t) for t in tasks]
    failures = []
    for job in jobs:
        try: job.result()
        except Exception as e: failures.append(str(e))
    if failures: raise RuntimeError(failures)

cmake('png', 'libpng-1.6.59', '-DPNG_SHARED=OFF', '-DPNG_TESTS=OFF', '-DPNG_TOOLS=OFF',
      '-DZLIB_LIBRARY=' + str(prefix/'lib/libz.a'), '-DZLIB_INCLUDE_DIR=' + str(prefix/'include'))
cmake('freetype', 'freetype-2.15.0', '-DFT_REQUIRE_ZLIB=ON', '-DFT_REQUIRE_BZIP2=ON', '-DFT_REQUIRE_PNG=ON',
      '-DFT_REQUIRE_BROTLI=ON', '-DFT_REQUIRE_HARFBUZZ=ON', '-DFT_DYNAMIC_HARFBUZZ=OFF',
      '-DZLIB_LIBRARY=' + str(prefix/'lib/libz.a'), '-DBZIP2_LIBRARY_RELEASE=' + str(prefix/'lib/libbz2.a'),
      '-DPNG_LIBRARY_RELEASE=' + str(prefix/'lib/libpng16.a'))
build = root/'build-fontconfig'
wrap = src/'fontconfig-2.18.3/subprojects/gperf.wrap'
wrap.write_text(wrap.read_text().replace('revision=meson', 'revision=c24359b4eab86d71c655c3b3fc969f13aac879ce'))
if not (build/'build.ninja').exists():
    run('fontconfig', ['meson', 'setup', build, src/'fontconfig-2.18.3', '--prefix=' + str(prefix), '--libdir=lib',
        '--buildtype=release', '--default-library=static', '-Dprefer_static=true', '-Db_lto=false',
        '-Ddoc=disabled', '-Dtools=disabled', '-Dtests=disabled', '-Dcache-build=disabled', '-Dnls=disabled',
        '-Dxml-backend=expat', '-Dbaseconfig-dir=/etc/fonts', '-Dconfig-dir=/etc/fonts/conf.d',
        '-Ddefault-fonts-dirs=/usr/share/fonts', '-Dadditional-fonts-dirs=/usr/local/share/fonts',
        '-Dcache-dir=/var/cache/fontconfig', '-Dtemplate-dir=/usr/share/fontconfig/conf.avail',
        '-Dc_args=-ffunction-sections -fdata-sections'])
run('fontconfig', ['ninja', '-C', build, '-j6'])
# Copy just the library: installing fontconfig would also copy system config paths.
shutil.copy2(build/'libfontconfig.a', prefix/'lib/libfontconfig.a')
print('fontconfig built; host configuration untouched', flush=True)

run('sqlite', [root/'cc', '-O2', '-fPIC', '-ffunction-sections', '-fdata-sections', '-DSQLITE_THREADSAFE=1',
    '-DSQLITE_ENABLE_COLUMN_METADATA', '-DSQLITE_ENABLE_FTS5', '-DSQLITE_ENABLE_RTREE', '-c',
    src/'sqlite-amalgamation-3540100/sqlite3.c', '-o', root/'sqlite3.o'])
run('sqlite', ['ar', 'rcs', prefix/'lib/libsqlite3.a', root/'sqlite3.o'])
print('sqlite built', flush=True)

# Give this independent fontconfig dependency closure its own native namespace.
# Do not bind its FreeType calls to the version privately bundled in Skia.
deps = ['freetype', 'harfbuzz', 'png16', 'z', 'bz2', 'brotlidec', 'brotlicommon', 'expat']
symbols = set()
for name in deps:
    result = subprocess.check_output(['nm', '-g', '--defined-only', str(prefix/'lib'/('lib'+name+'.a'))], text=True)
    for line in result.splitlines():
        parts = line.split()
        if len(parts) == 3 and parts[1] in 'TDBRSCVW': symbols.add(parts[2])
mapping = root/'font-symbols.txt'
mapping.write_text(''.join(s+' wuwa_fc_'+s+'\n' for s in sorted(symbols)))
isolated = root/'isolated'; isolated.mkdir(exist_ok=True)
for name in ['fontconfig', *deps]:
    run('isolate', ['objcopy', '--redefine-syms='+str(mapping), prefix/'lib'/('lib'+name+'.a'), isolated/('lib'+name+'.a')])
shutil.copy2(prefix/'lib/libsqlite3.a', isolated/'libsqlite3.a')
(root/'archives.json').write_text(json.dumps({p.name:{'sha256':hashlib.sha256(p.read_bytes()).hexdigest(),'bytes':p.stat().st_size}
    for p in sorted(isolated.glob('*.a'))},indent=2)+'\n')
print('STATIC_ARCHIVES_READY', flush=True)
