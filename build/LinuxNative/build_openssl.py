#!/usr/bin/env python3
from pathlib import Path
import os,subprocess,json,hashlib,shlex,sys
root=Path(sys.argv[1]).resolve()/'runtime'
(root/'logs').mkdir(parents=True,exist_ok=True)
prefix=root/'openssl36-prefix';d=root/'src/openssl-3.6.5'
cc=root.parent/'fonts/cc';env=dict(os.environ,CC=str(cc),CFLAGS='-O2 -fPIC -ffunction-sections -fdata-sections')
def run(args):
 with (root/'logs/openssl-3.6.5.log').open('a') as log:
  log.write('\n'+shlex.join(map(str,args))+'\n');log.flush()
  subprocess.run(list(map(str,args)),cwd=d,env=env,stdout=log,stderr=subprocess.STDOUT,check=True)
if not (d/'Makefile').exists():run(['./Configure','linux-x86_64','no-shared','no-module','no-tests','--prefix='+str(prefix),'--openssldir=/etc/ssl','--libdir=lib'])
run(['make','-j8','build_libs']);run(['make','install_dev'])
files=list((prefix/'lib').glob('*.a'))
(root/'openssl-3.6.5-archives.json').write_text(json.dumps([dict(file=str(f.relative_to(root)),sha256=hashlib.sha256(f.read_bytes()).hexdigest(),bytes=f.stat().st_size) for f in files],indent=2)+'\n')
print('OPENSSL_3_6_5_ARCHIVES_BUILT',flush=True)
