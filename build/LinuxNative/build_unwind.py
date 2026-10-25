#!/usr/bin/env python3
"""Build GCC's unwinder against the pinned glibc headers, not host glibc."""
from pathlib import Path
import subprocess
import sys

root = Path(sys.argv[1]).resolve() / 'unwind'
build = root / 'build'
build.mkdir(exist_ok=True)

def run(name, command, cwd=build):
    with (root / (name + '.log')).open('w') as log:
        subprocess.run(list(map(str, command)), cwd=cwd, stdout=log, stderr=subprocess.STDOUT, check=True)

run('configure', [root / 'gcc-16.2.0/configure', '--prefix=' + str(root / 'prefix'),
    '--with-sysroot=' + str(root / 'sysroot'), '--with-native-system-header-dir=/usr/include',
    '--disable-bootstrap', '--disable-multilib', '--disable-nls', '--enable-languages=c', '--disable-shared',
    '--disable-libsanitizer', '--disable-libquadmath', '--disable-libgomp', '--disable-libatomic', '--disable-libssp', '--without-isl'])
run('build-gcc', ['make', '-j8', 'all-gcc'])
headers = str(root / 'sysroot/usr/include/x86_64-linux-gnu')
run('build-libgcc', ['make', '-j8', 'all-target-libgcc', 'CFLAGS_FOR_TARGET=-g -O2 -isystem ' + headers,
    'CPPFLAGS_FOR_TARGET=-isystem ' + headers])
objects = root / 'eh-objects'
objects.mkdir(exist_ok=True)
archives = list(build.glob('*/libgcc/libgcc.a'))
if len(archives) != 1:
    raise ValueError('Expected one target libgcc archive')
names = ['unwind-dw2.o', 'unwind-dw2-fde-dip.o', 'unwind-sjlj.o', 'unwind-c.o', 'heap-trampoline.o', 'emutls.o']
run('extract', ['ar', 'x', archives[0], *names], objects)
run('archive', ['ar', 'rcs', root / 'libgcc_eh.a', *names], objects)
print('STATIC_UNWINDER_READY', flush=True)
