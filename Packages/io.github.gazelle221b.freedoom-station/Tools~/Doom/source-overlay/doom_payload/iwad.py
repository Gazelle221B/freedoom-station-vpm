#!/usr/bin/env python3
"""Pinned IWAD acquisition and validated E1M1 map selection. Generated data is private."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import urllib.request
import zipfile

HERE = Path(__file__).resolve().parent
VERSION = '0.13.0'
BASE = f'https://github.com/freedoom/freedoom/releases/download/v{VERSION}/'
ARCHIVE = f'freedoom-{VERSION}.zip'
ARCHIVE_SHA = '3f9b264f3e3ce503b4fb7f6bdcb1f419d93c7b546f4df3e874dd878db9688f59'
WAD_SHA = '7323bcc168c5a45ff10749b339960e98314740a734c30d4b9f3337001f9e703d'
SHAREWARE_SHA = '1d7d43be501e67d927e415e0b8f3e29c3bf33075e859721816f652a526cac771'
ENGINE_PIN = 'b52f80968a25a90b2ab0cf6c97703876b2d56e59'
MAP_LUMPS = ('THINGS','LINEDEFS','SIDEDEFS','VERTEXES','SEGS','SSECTORS','NODES','SECTORS','REJECT','BLOCKMAP')

def verify(path, expected):
    actual = hashlib.sha256(path.read_bytes()).hexdigest()
    if actual != expected:
        raise ValueError(f'SHA-256 mismatch for {path}: {actual}, expected {expected}')
    return actual

def download(url, path, expected):
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        temporary = path.with_suffix(path.suffix+'.part')
        with urllib.request.urlopen(url, timeout=90) as response, temporary.open('wb') as output:
            while chunk := response.read(1024*1024):
                output.write(chunk)
        verify(temporary, expected)
        temporary.replace(path)
    verify(path, expected)
    return path

def acquire(kind, supplied=None):
    if kind not in ('freedoom','shareware'):
        raise ValueError('IWAD must be freedoom or shareware')
    expected = WAD_SHA if kind == 'freedoom' else SHAREWARE_SHA
    if kind == 'shareware':
        path = supplied or HERE/'cache/doom1.wad'
        if supplied is None:
            download(f'https://media.githubusercontent.com/media/cnlohr/embeddedDOOM/{ENGINE_PIN}/src/support/doom1.wad',path,expected)
    else:
        cache = HERE/f'cache/freedoom-{VERSION}'
        archive = download(BASE+ARCHIVE,cache/ARCHIVE,ARCHIVE_SHA)
        with zipfile.ZipFile(archive) as z:
            for name in ('freedoom1.wad','COPYING.txt','CREDITS.txt','CREDITS-MUSIC.txt'):
                target = cache/name
                content = z.read(f'freedoom-{VERSION}/{name}')
                if target.exists() and target.read_bytes() != content:
                    raise ValueError(f'Cached release member modified: {target}')
                target.write_bytes(content)
        path = supplied or cache/'freedoom1.wad'
    verify(path, expected)
    return path

def read_lumps(data):
    if len(data) < 12:
        raise ValueError('Truncated IWAD header')
    magic,count,directory = struct.unpack_from('<4sii',data)
    if magic != b'IWAD' or count < 0 or directory < 12 or directory+count*16 > len(data):
        raise ValueError('Invalid IWAD directory')
    lumps=[]
    for i in range(count):
        start,size,name=struct.unpack_from('<ii8s',data,directory+16*i)
        if start < 0 or size < 0 or start+size > len(data):
            raise ValueError('Lump outside IWAD')
        lumps.append((start,size,name))
    return lumps

def e1m1_only(data):
    lumps=read_lumps(data)
    keep=[]; index=0; found=False
    while index<len(lumps):
        name=lumps[index][2].rstrip(b'\0').decode('ascii')
        if re.fullmatch(r'E\dM\d',name):
            group=lumps[index:index+11]
            if len(group)!=11 or tuple(x[2].rstrip(b'\0').decode('ascii') for x in group[1:])!=MAP_LUMPS:
                raise ValueError('Unsupported map structure: '+name)
            if name=='E1M1':
                if found: raise ValueError('Duplicate E1M1')
                found=True; keep.extend(group)
            index+=11
        else:
            keep.append(lumps[index]); index+=1
    if not found: raise ValueError('E1M1 missing')
    output=bytearray(b'\0'*12); directory=[]
    for start,size,name in keep:
        directory.append((len(output),size,name)); output.extend(data[start:start+size])
    offset=len(output)
    for item in directory: output.extend(struct.pack('<ii8s',*item))
    struct.pack_into('<4sii',output,0,b'IWAD',len(directory),offset)
    return bytes(output)

def manifest(kind, wad):
    return {'iwad':kind,'version':VERSION if kind=='freedoom' else 'Doom shareware pinned IWAD',
            'url':BASE+ARCHIVE if kind=='freedoom' else f'https://media.githubusercontent.com/media/cnlohr/embeddedDOOM/{ENGINE_PIN}/src/support/doom1.wad',
            'sha256':verify(wad,WAD_SHA if kind=='freedoom' else SHAREWARE_SHA),'bytes':wad.stat().st_size}

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--iwad',choices=('freedoom','shareware'),default='freedoom')
    args=parser.parse_args()
    path=acquire(args.iwad)
    print(json.dumps(manifest(args.iwad,path),indent=2))
