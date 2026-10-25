"""Wine SDK locations shared by the Linux fixture build scripts."""
from pathlib import Path


def wine_include_args():
    for root in (Path('/usr/include/wine'), Path('/usr/include/wine/wine')):
        if (root / 'windows/windows.h').is_file():
            return ['-isystem', str(root / 'msvcrt'), '-isystem', str(root / 'windows')]
    raise RuntimeError('Wine development headers are missing; install libwine-dev or the equivalent package.')


def wine_library(name):
    for root in (Path('/usr/lib/wine/x86_64-windows'), Path('/usr/lib/x86_64-linux-gnu/wine/x86_64-windows')):
        path = root / ('lib' + name + '.a')
        if path.is_file():
            return str(path)
    raise RuntimeError('Wine import library is missing: ' + name)
