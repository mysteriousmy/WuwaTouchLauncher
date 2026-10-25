#!/usr/bin/env python3
"""Build the pinned static ICU archive and packaging tool."""
from pathlib import Path
import os,shutil,subprocess,json,hashlib,shlex,sys
root=Path(sys.argv[1]).resolve()/'runtime'
root.mkdir(parents=True,exist_ok=True)
prefix=root/'prefix';(prefix/'lib').mkdir(parents=True,exist_ok=True)
(root/'logs').mkdir(exist_ok=True)
cc=root.parent/'fonts/cc';cxx=root.parent/'fonts/cxx'
env=dict(os.environ,CC=str(cc),CXX=str(cxx),CFLAGS='-O2 -fPIC -ffunction-sections -fdata-sections',CXXFLAGS='-O2 -fPIC -ffunction-sections -fdata-sections')
def run(name,args,cwd):
 with (root/'logs'/f'{name}.log').open('a') as log:
  log.write('\n'+shlex.join(map(str,args))+'\n');log.flush()
  subprocess.run(list(map(str,args)),cwd=cwd,env=env,stdout=log,stderr=subprocess.STDOUT,check=True)
def icu():
 d=root/'src/icu/source'
 if not (d/'Makefile').exists():run('icu',['./configure','--prefix='+str(prefix),'--enable-static','--disable-shared','--disable-tests','--disable-samples','--disable-extras','--with-data-packaging=archive'],d)
 (d/'lib').mkdir(exist_ok=True);(d/'bin').mkdir(exist_ok=True)
 for part in ['stubdata','common','i18n','io','tools/toolutil']:run('icu',['make','-C',part,'-j6'],d)
 for f in (d/'lib').glob('*.a'):shutil.copy2(f,prefix/'lib'/f.name)
 shutil.copy2(d/'stubdata/libicudata.a',prefix/'lib/libicudata.a')
 for part in ['common','i18n','io']:shutil.copytree(d/part/'unicode',prefix/'include/unicode',dirs_exist_ok=True)
 (prefix/'share/icu/78.3').mkdir(parents=True,exist_ok=True)
 shutil.copy2(d/'data/in/icudt78l.dat',prefix/'share/icu/78.3/icudt78l.dat')
 print('ICU_ARCHIVES_BUILT',flush=True)
icu()
run('icu',['make','-C','tools/icupkg','-j4'],root/'src/icu/source')
(prefix/'bin').mkdir(exist_ok=True)
shutil.copy2(root/'src/icu/source/bin/icupkg',prefix/'bin/icupkg')
files=list((prefix/'lib').glob('*.a'))+list((prefix/'share/icu/78.3').glob('*.dat'))
(root/'archives.json').write_text(json.dumps([dict(file=str(f.relative_to(root)),sha256=hashlib.sha256(f.read_bytes()).hexdigest(),bytes=f.stat().st_size) for f in files],indent=2)+'\n')
