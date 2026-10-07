#!/usr/bin/env python3
"""Generate the console atlas from the existing replay tool's 5x7 font."""
import struct
import zlib
import sys
from pathlib import Path
from replay_uart import _glyph_rows

def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data))

def make_font(path):
    width, height = 256, 64
    pixels = bytearray(width * height * 4)
    for c in range(32, 127):
        for y, row in enumerate(_glyph_rows(c)):
            for x in range(5):
                if row & (1 << (4-x)):
                    i = (((c // 32) * 8 + y) * width + (c % 32) * 8 + x + 1) * 4
                    pixels[i:i+4] = b'\xff\xff\xff\xff'
    scanlines = b''.join(b'\0'+pixels[y*width*4:(y+1)*width*4] for y in range(height))
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR',struct.pack('>IIBBBBB',width,height,8,6,0,0,0))
    png += chunk(b'IDAT',zlib.compress(scanlines)) + chunk(b'IEND',b'')
    path = Path(path); path.parent.mkdir(parents=True,exist_ok=True); path.write_bytes(png)

if __name__ == '__main__':
    make_font(sys.argv[1])
