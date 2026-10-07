import hashlib
from pathlib import Path
import struct
import tempfile
import unittest
import iwad

def sample_wad(names):
    data=bytearray(b'\0'*12); directory=[]
    for name in names:
        content=name.encode()
        directory.append((len(data),len(content),name.encode().ljust(8,b'\0')))
        data.extend(content)
    start=len(data)
    for item in directory: data.extend(struct.pack('<ii8s',*item))
    struct.pack_into('<4sii',data,0,b'IWAD',len(directory),start)
    return bytes(data)

class IwadTests(unittest.TestCase):
    def test_other_episodes_removed_with_whole_map_blocks(self):
        names=['PLAYPAL','E1M1',*iwad.MAP_LUMPS,'E2M1',*iwad.MAP_LUMPS,'E4M1',*iwad.MAP_LUMPS,'FREEDOOM','DEHACKED']
        original=sample_wad(names)
        result=iwad.e1m1_only(original)
        actual=[n.rstrip(b'\0').decode() for _,_,n in iwad.read_lumps(result)]
        self.assertEqual(actual,['PLAYPAL','E1M1',*iwad.MAP_LUMPS,'FREEDOOM','DEHACKED'])
        for start,size,name in iwad.read_lumps(result):
            self.assertEqual(result[start:start+size],name.rstrip(b'\0'))

    def test_missing_or_malformed_map_rejected(self):
        for names in [['PLAYPAL'],['E1M1','THINGS','PLAYPAL'],['E1M1',*iwad.MAP_LUMPS,'E1M1',*iwad.MAP_LUMPS]]:
            with self.assertRaises(ValueError): iwad.e1m1_only(sample_wad(names))

    def test_directory_and_lump_bounds_rejected(self):
        data=bytearray(sample_wad(['PLAYPAL']))
        with self.assertRaises(ValueError): iwad.read_lumps(data[:11])
        directory=struct.unpack_from('<i',data,8)[0]
        struct.pack_into('<i',data,directory,len(data)+1)
        with self.assertRaises(ValueError): iwad.read_lumps(data)

    def test_modified_cached_download_fails_hash_check(self):
        with tempfile.TemporaryDirectory() as tmp:
            p=Path(tmp)/'data.wad'; p.write_bytes(b'good')
            expected=hashlib.sha256(b'good').hexdigest()
            iwad.verify(p,expected)
            p.write_bytes(b'modified')
            with self.assertRaises(ValueError): iwad.verify(p,expected)

if __name__=='__main__': unittest.main()
