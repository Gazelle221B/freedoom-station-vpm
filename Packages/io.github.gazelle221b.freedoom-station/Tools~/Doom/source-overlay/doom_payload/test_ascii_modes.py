"""Real-driver properties for the experimental modes, without duplicating the CDF algorithm."""
import math
import os
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import unittest

import test_video_console as legacy

RAMP = " .,:;+*o%#@"
GRAY = bytes(c for i in range(256) for c in (i,i,i))
FRAME_BYTES = 320*200


def bands(values):
    return b''.join(bytes([values[min(len(values)-1, y*len(values)//200)]])*320 for y in range(200))


class AsciiModesTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory(prefix='doom_ascii_modes_')
        cls.directory = Path(cls.temp.name)
        cls.engine = legacy._find_engine_src()
        cls.cc = shutil.which('gcc') or shutil.which('cc')
        if not cls.engine or not cls.cc:
            raise AssertionError('Pinned embeddeddoom headers and host C compiler are required')
        cls.binary = cls.directory/'driver'
        ok, diagnostic = legacy._compile_harness(legacy.DRIVER,cls.engine,cls.binary,cls.cc,ascii_statics=True)
        if not ok: raise AssertionError(diagnostic)

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def run_driver(self, mode, arguments, data, style=None, binary=None):
        env = os.environ.copy()
        env.update(RVC_DOOM_TERMINAL='1',RVC_DOOM_MARKERS='0')
        env.pop('RVC_DOOM_FRAMES',None)
        env.pop('RVC_DOOM_ASCII_MODE',None)
        if style is not None: env['RVC_DOOM_ASCII_MODE']=style
        result = subprocess.run([str(binary or self.binary),mode,*map(str,arguments)],input=data,capture_output=True,env=env)
        self.assertEqual(result.returncode,0,result.stderr.decode(errors='replace'))
        return result

    def snap(self, palette, frames, style):
        dump=self.directory/'state'; outputs=[self.directory/f'frame{i}' for i in range(len(frames))]
        self.run_driver('snap',[dump,*outputs],palette+b''.join(frames),style)
        data=dump.read_bytes(); position=0; records={k:[] for k in 'LOVDS'}
        sizes={'L':256,'O':256,'V':1,'D':512,'S':256}
        while position<len(data):
            tag=chr(data[position]); position+=1; size=sizes[tag]
            payload=data[position:position+size]; position+=size
            self.assertEqual(len(payload),size)
            records[tag].append(struct.unpack('<256H',payload) if tag=='D' else payload)
        return records,[p.read_bytes() for p in outputs]

    def test_default_bytes_match_46982255(self):
        source=self.directory/'legacy.c'; binary=self.directory/'legacy'
        source.write_bytes((legacy.HERE/'test-fixtures/video_console-46982255.c').read_bytes())
        ok, diagnostic=legacy._compile_harness(source,self.engine,binary,self.cc)
        self.assertTrue(ok,diagnostic)
        framebuffer=bytes((i*13+i//320)&255 for i in range(FRAME_BYTES))
        old=self.directory/'old'; new=self.directory/'new'
        self.run_driver('rawframe',[old],GRAY+framebuffer,binary=binary)
        self.run_driver('rawframe',[new],GRAY+framebuffer)
        self.assertEqual(old.read_bytes(),new.read_bytes())

    def test_mode_selection_and_unknown_keep_default(self):
        for value,want in [(None,0),('legacy',0),('X',0),('AB',0),('a',1),('A',1),('b',2),('C',3)]:
            result=self.run_driver('asciiprobe',[],GRAY,value)
            self.assertEqual(result.stdout,f'ascii_style={want}\n'.encode())

    def test_a_all_eleven_bins_independent_sqrt_oracle(self):
        result=self.run_driver('lut',[],GRAY,'A')
        expected=bytes(ord(RAMP[min(10,int(11*math.sqrt(i/255)))]) for i in range(256))
        self.assertEqual(result.stdout,expected)
        self.assertEqual(set(result.stdout),set(RAMP.encode()))
        self.assertNotIn(b'-',result.stdout); self.assertNotIn(b'=',result.stdout)

    def test_b_palette_order_and_equal_luminance_grouping(self):
        palette=bytearray(GRAY)
        for i in range(256): palette[i*3:i*3+3]=bytes([(i*73+11)&255])*3
        palette[17*3:17*3+3]=palette[90*3:90*3+3]=bytes([127])*3
        framebuffer=bytearray(bands([0,17,255]))
        for y in range(67,134): framebuffer[y*320+160:y*320+320]=bytes([90])*160
        records,outputs=self.snap(bytes(palette),[bytes(framebuffer)],'B')
        luma,order=records['L'][0],records['O'][0]
        self.assertEqual(list(order),sorted(range(256),key=lambda i:luma[i]))
        self.assertEqual(records['V'],[b'\x01'])
        self.assertEqual(len(set(outputs[0].split(b'\r\n')[12])),1)

    def test_b_c_monocolor_narrow_and_dominant_fallback(self):
        # 21/24 sampled rows black meets the >=7/8 threshold exactly.
        dominated=b'\x00'*(175*320)+b'\xff'*(25*320)
        for style in ('B','C'):
            for framebuffer in (bytes([127])*FRAME_BYTES,bands([127,129]),dominated):
                records,outputs=self.snap(GRAY,[framebuffer],style)
                self.assertEqual(records['V'],[b'\x00'])
                self.assertEqual(len(outputs[0]),24*(79+2))
                if framebuffer!=dominated: self.assertEqual(outputs[0],(b'o'*79+b'\r\n')*24)
                else: self.assertEqual(outputs[0],(b' '*79+b'\r\n')*21+(b'@'*79+b'\r\n')*3)

    def test_c_averages_luminance_not_palette_indices(self):
        palette=bytearray(768); palette[:3]=b'\xff'*3
        palette[125*3:125*3+3]=b'\xff\x00\x00'  # index-average decoy, not gray
        framebuffer=bytearray([250])*FRAME_BYTES
        for y in range(24):
            row=(y*200//24)*320
            for x in range(79):
                offset=row+x*320//79
                framebuffer[offset]=framebuffer[offset+2]=0
        records,outputs=self.snap(bytes(palette),[bytes(framebuffer)],'C')
        self.assertEqual(records['V'],[b'\x00']) # every displayed average is gray
        self.assertEqual(outputs[0],(b'o'*79+b'\r\n')*24)

    def test_cdf_mix_is_one_to_three_and_monotonic(self):
        first=bands([0,64,128,255]); second=bands([0,128,255])
        for style in ('B','C'):
            direct,_=self.snap(GRAY,[second],style)
            sequence,_=self.snap(GRAY,[first,second],style)
            self.assertEqual(sequence['V'],[b'\x01',b'\x01'])
            old,mixed=sequence['D']; fresh=direct['D'][0]
            self.assertNotEqual(old,fresh)
            self.assertEqual(mixed,tuple((3*a+b+2)//4 for a,b in zip(old,fresh)))
            for cdf in (old,mixed,fresh):
                self.assertEqual(list(cdf),sorted(cdf)); self.assertEqual(cdf[0],0)
                self.assertGreater(cdf[255],65520)

    def test_fallback_invalidates_and_next_normal_reseeds(self):
        normal=bands([0,64,128,255]); solid=bytes([127])*FRAME_BYTES
        for style in ('B','C'):
            direct,_=self.snap(GRAY,[normal],style)
            cleared,_=self.snap(GRAY,[normal,solid],style)
            reseeded,_=self.snap(GRAY,[solid,normal],style)
            self.assertEqual(cleared['V'],[b'\x01',b'\x00'])
            self.assertEqual(reseeded['V'],[b'\x00',b'\x01'])
            self.assertEqual(reseeded['D'][1],direct['D'][0])

    def test_c_deadband_holds_one_level_but_follows_large_changes(self):
        first = bands([0,64,128,255])
        shifted = bytearray(first)
        # Replace a sample cell's four points; most histogram bins remain fixed.
        y, x = (10*200//24)*320, 45*320//79
        for offset in (y+x,y+x+2,y+x+4*320,y+x+4*320+2):
            shifted[offset] = 128
        _, initial = self.snap(GRAY,[first],'C')
        _, direct = self.snap(GRAY,[bytes(shifted)],'C')
        _, sequence = self.snap(GRAY,[first,bytes(shifted)],'C')
        index = 10*81+45
        self.assertEqual(abs(RAMP.index(chr(initial[0][index]))-RAMP.index(chr(direct[0][index]))),4)
        self.assertNotEqual(sequence[1][index],initial[0][index])
        # Adjacent ranks at the darkest two occupied bins must be held.
        finer = bands([0,32,64,96,128,160,192,224,255])
        _, initial = self.snap(GRAY,[finer],'C')
        adjacent = bytearray(finer)
        for offset in (y+x,y+x+2,y+x+4*320,y+x+4*320+2):
            adjacent[offset] = 128
        _, fresh = self.snap(GRAY,[bytes(adjacent)],'C')
        _, held = self.snap(GRAY,[finer,bytes(adjacent)],'C')
        self.assertEqual(abs(RAMP.index(chr(fresh[0][index]))-RAMP.index(chr(initial[0][index]))),1)
        self.assertEqual(held[1][index],initial[0][index])

    def test_c_frozen_input_converges_and_fallback_does_not_stick(self):
        first, frozen = bands([0,64,128,255]), bands([0,128,255])
        _, outputs = self.snap(GRAY,[first]+[frozen]*39,'C')
        self.assertTrue(all(t == outputs[-1] for t in outputs[-10:]))
        _, reset = self.snap(GRAY,[first,bytes([127])*FRAME_BYTES],'C')
        self.assertEqual(reset[1],(b'o'*79+b'\r\n')*24)
