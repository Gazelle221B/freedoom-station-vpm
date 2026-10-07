"""Check host-baker output against its logical input sizes before shipping it."""
import re
from iwad import read_lumps


def check_baked(support):
    maps = (support / "baked_map_data.c").read_text()
    counts = re.search(r"numvertexes_baked\[\]\s*=\s*\{([^}]+)\}", maps)
    if not counts:
        raise ValueError("Missing baked vertex counts")
    expected = [int(x) for x in re.findall(r"\d+", counts[1])]
    arrays = re.findall(r"__vertexes(\d+)\[(\d+)\]\s*=\s*\{(.*?)\};", maps, re.S)
    if len(arrays) != len(expected):
        raise ValueError("Missing baked vertex array")
    for index, size, body in arrays:
        actual = len(re.findall(r"\{\s*0x[0-9a-f]+,\s*0x[0-9a-f]+\s*\}", body))
        if int(size) != expected[int(index)] or actual != int(size):
            raise ValueError("Baked vertex count exceeds logical vertices")
    textures = (support / "baked_texture_data.c").read_text()
    def bytes_for(symbol):
        match = re.search(r"\b"+symbol+r"\[\]\s*=\s*\{(.*?)\};", textures, re.S)
        if not match:
            raise ValueError("Missing baked array: "+symbol)
        return bytes(int(x,16) for x in re.findall(r"0x([0-9a-f]+)",match[1]))
    wad = (support / "doom1.wad").read_bytes()
    source_colormap = next(wad[start:start+size] for start,size,name in read_lumps(wad) if name.rstrip(b"\0")==b"COLORMAP")
    if bytes_for("colormaps") != source_colormap:
        raise ValueError("Baked COLORMAP differs from initialized lump")
    if len(bytes_for("translationtables")) != 768:
        raise ValueError("Baked translation tables include allocation padding")
    for name in re.findall(r"\b(textureData\d+)\[\]", textures):
        data = bytes_for(name)
        if len(data)<16 or data[14:16] != b"\0\0" or len(data) != 16+12*int.from_bytes(data[12:14],"little"):
            raise ValueError("Baked texture header size/padding mismatch: "+name)
    return {"vertexCounts":expected, "colormapBytes":len(source_colormap),"translationBytes":768}
