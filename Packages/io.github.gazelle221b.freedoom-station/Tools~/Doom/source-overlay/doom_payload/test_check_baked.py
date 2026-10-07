from pathlib import Path
import tempfile
import unittest
from check_baked import check_baked
from test_iwad import sample_wad


class BakedBoundsTests(unittest.TestCase):
    def fixture(self, path):
        # A real little-endian WAD directory with one initialized COLORMAP.
        (path/"doom1.wad").write_bytes(sample_wad(["COLORMAP"]))
        (path/"baked_map_data.c").write_text("const int numvertexes_baked[] = {1};\nstatic const vertex_t __vertexes0[1] = {{0x1, 0x2}};")
        tex = b"WALL0000" + b"\x40\0\x40\0\x01\0\0\0" + bytes(12)
        def array(name, data):
            return name+"[] = {"+", ".join("0x%02x"%x for x in data)+"};\n"
        (path/"baked_texture_data.c").write_text(array("colormaps",b"COLORMAP")+array("translationtables",bytes(768))+array("textureData0",tex))

    def test_initialized_data_passes(self):
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp);self.fixture(path)
            self.assertEqual(check_baked(path)["vertexCounts"],[1])

    def test_original_vertex_byte_count_bug_is_detected(self):
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp);self.fixture(path)
            (path/"baked_map_data.c").write_text("const int numvertexes_baked[] = {1};\nstatic const vertex_t __vertexes0[8] = {"+", ".join(["{0x1, 0x2}"]*8)+"};")
            with self.assertRaisesRegex(ValueError,"vertex"):
                check_baked(path)

    def test_uninitialized_colormap_tail_is_detected(self):
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp);self.fixture(path)
            file=path/"baked_texture_data.c"
            file.write_text(file.read_text().replace("0x43, 0x4f, 0x4c, 0x4f, 0x52, 0x4d, 0x41, 0x50", "0x43, 0x4f, 0x4c, 0x4f, 0x52, 0x4d, 0x41, 0x50, 0xff"))
            with self.assertRaisesRegex(ValueError,"COLORMAP"):
                check_baked(path)
