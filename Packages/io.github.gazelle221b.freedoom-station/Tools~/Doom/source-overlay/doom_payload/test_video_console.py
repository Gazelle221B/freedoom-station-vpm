#!/usr/bin/env python3
"""Regression tests for the video_console.c ASCII-LUT change.

The plain-terminal path installs ascii_palette[256] in I_SetPalette() when
RVC_DOOM_TERMINAL is set; UpdateTerminalPlain() emits one LUT char per cell.
Contract under test: shade = min(9, floor(10*sqrt(Y/255))), Y=.299R+.587G+.114B.

Assertions use the independent float sqrt oracle above; a boundary case (10*s
within 1e-9 of an integer) allows +-1 shade to absorb float rounding. Palette
changes may precede graphics init, so env must be parsed lazily once
(cfg_env_parsed) - verified generically, not tied to any observed engine call.

The REAL driver is compiled through test_video_console.c (which includes it
textually for static access) with -ffunction-sections/-fdata-sections/
--gc-sections. Real pinned engine headers are required; they are looked up in
doom_payload/{cache,build/host,build/target}/embeddeddoom/src or via
RVC_DOOM_ENGINE_SRC (path to a source dir). Missing prerequisites fail loudly.

Run from the repo root:
    python3 -m unittest discover -s doom_payload -p 'test_*.py' -v
"""

import math
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
REPO = HERE.parent
DRIVER = HERE / "video_console.c"
HARNESS = HERE / "test_video_console.c"
# Pre-LUT-change driver pinned so the ANSI reference never becomes a
# self-comparison once the change is committed (driver identical at 80cb).
REF_DRIVER_COMMIT = "2bb6d5f4e9e8ec924ff05767d9ec0f3af85a27ab"
SHADES = " .:-=+*#%@"          # LUT chars, index 0..9 (10 levels)
PAL_BYTES = 768                # 256 entries * 3 bytes


def _sqrt_shade(r, g, b):
    """Independent oracle: (shade, boundary_hit, prec_boundary)."""
    y = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0
    val = 10.0 * math.sqrt(y)
    round_v = round(val)
    return min(9, math.floor(val)), math.isclose(val, round_v, abs_tol=1e-9), round_v


def neutral_palette(v):
    return bytes([v]) * PAL_BYTES


def _engine_roots():
    roots = []
    for sub in ("cache", "build/host", "build/target"):
        roots.append(HERE / sub / "embeddeddoom" / "src")
    override = os.environ.get("RVC_DOOM_ENGINE_SRC")
    if override:
        roots.insert(0, Path(override))
    return roots


def _find_engine_src():
    for cand in _engine_roots():
        if (cand / "v_video.h").is_file() and (cand / "doomdef.h").is_file():
            return cand
    return None


def _compile_harness(driver_path, engine_src, out_path, cc, new_statics=True, ascii_statics=False):
    cmd = [
        cc, "-O1", "-g",
        "-ffunction-sections", "-fdata-sections", "-Wl,--gc-sections",
        "-DE1M1ONLY=1", "-DNORMALUNIX", "-DLINUX", "-DMAXPLAYERS=1",
        "-DDISABLE_NETWORK", "-DSET_MEMORY_DEBUG=0", "-DRANGECHECK",
        "-DIS_ON_DESKTOP_NOT_RV_EMULATOR",
        '-DVC_SOURCE="%s"' % driver_path,
    ]
    if new_statics:
        cmd.append("-DVC_HAS_LUT_STATICS")
    if ascii_statics:
        cmd.append("-DVC_HAS_ASCII_STATICS")
    cmd += ["-I%s" % engine_src.parent.parent,  # parent of the embeddeddoom checkout
            str(HARNESS), "-o", str(out_path)]
    proc = subprocess.run(cmd, capture_output=True, text=True)
    return (True, "") if proc.returncode == 0 else (False, proc.stdout + proc.stderr)


class VideoConsoleAsciiLutTests(unittest.TestCase):

    @classmethod
    def setUpClass(cls):
        engine_src = _find_engine_src()
        if engine_src is None:
            raise AssertionError(
                "engine headers not found; set RVC_DOOM_ENGINE_SRC or populate "
                "doom_payload/{cache,build/host,build/target}/embeddeddoom/src "
                "(searched: %s)" % ", ".join(str(p) for p in _engine_roots()))
        cls.engine_src = engine_src
        cls.tmp = Path(tempfile.mkdtemp(prefix="tvc_regress_"))
        cls.cc = shutil.which("gcc") or shutil.which("cc")
        if cls.cc is None:
            raise AssertionError("no C compiler (gcc/cc) found on PATH")
        cls.bin = cls.tmp / "vin_harness"
        ok, diag = _compile_harness(DRIVER, engine_src, cls.bin, cls.cc)
        if not ok:
            raise AssertionError("video_console.c fails to compile in the harness:\n" + diag)
        cls.ref_bin = None

    @classmethod
    def tearDownClass(cls):
        if not os.environ.get("RVC_DOOM_KEEP_BUILD"):
            shutil.rmtree(cls.tmp, ignore_errors=True)

    # -- helpers ----------------------------------------------------------

    def _run(self, mode, args=(), data=b"", terminal=None, markers=None):
        env = os.environ.copy()
        env.pop("RVC_DOOM_ASCII_MODE", None)
        for key, val in (("RVC_DOOM_TERMINAL", terminal),
                         ("RVC_DOOM_MARKERS", markers)):
            if val is None:
                env.pop(key, None)
            else:
                env[key] = "1" if val else "0"
        return subprocess.run([str(self.bin), mode, *map(str, args)],
                              input=data, capture_output=True, env=env)

    def _lut(self, palettes):
        proc = self._run("lut", data=b"".join(palettes), terminal=True)
        self.assertEqual(proc.returncode, 0, proc.stderr.decode(errors="replace"))
        return [proc.stdout[i * 256:(i + 1) * 256] for i in range(len(palettes))]

    def _assert_shade(self, shade, r, g, b, where):
        exp, boundary, round_v = _sqrt_shade(r, g, b)
        if boundary:
            allowed = {min(9, max(0, round_v - 1)), min(9, round_v)}
            self.assertIn(shade, allowed, where + " (float boundary %d)" % round_v)
        else:
            self.assertEqual(shade, exp, where)

    def _expected_plain_frame(self, palette):
        lut = [SHADES[_sqrt_shade(palette[i * 3], palette[i * 3 + 1],
                                  palette[i * 3 + 2])[0]] for i in range(256)]
        rows = []
        for y in range(24):
            ly = y * 200 // 24
            cells = [lut[((x * 320 // 79) + 320 * ly) & 0xFF] for x in range(79)]
            rows.append("".join(cells) + "\r\n")
        return "".join(rows).encode()

    # -- LUT contract -----------------------------------------------------

    def test_lut_all_256_neutral_colors_match_sqrt_oracle(self):
        palettes = [neutral_palette(v) for v in range(256)]
        blocks = self._lut(palettes)
        for v in range(256):
            actual = SHADES.index(chr(blocks[v][0]))
            self._assert_shade(actual, v, v, v, "neutral v=%d" % v)
            self.assertEqual(blocks[v], SHADES[actual].encode() * 256,
                             "uniform palette v=%d" % v)

    def test_lut_black_white_endpoints(self):
        blocks = self._lut([neutral_palette(0), neutral_palette(255)])
        self.assertEqual(blocks[0], b" " * 256)
        self.assertEqual(blocks[1], b"@" * 256)

    def test_lut_green_gt_red_gt_blue(self):
        primaries = {"red": (255, 0, 0), "green": (0, 255, 0), "blue": (0, 0, 255)}
        blocks = self._lut([bytes(rgb) * 256 for rgb in primaries.values()])
        chars = {name: blocks[i][0] for i, name in enumerate(primaries)}
        grades = {name: _sqrt_shade(*rgb)[0] for name, rgb in primaries.items()}
        self.assertGreater(grades["green"], grades["red"])
        self.assertGreater(grades["red"], grades["blue"])
        for name in primaries:
            self.assertEqual(chars[name], ord(SHADES[grades[name]]),
                             "shade char for %s" % name)

    def test_lut_refresh_different_palettes(self):
        blocks = self._lut([neutral_palette(100), neutral_palette(200)])
        self.assertEqual(blocks[0], (SHADES[_sqrt_shade(100, 100, 100)[0]] * 256).encode())
        self.assertEqual(blocks[1], (SHADES[_sqrt_shade(200, 200, 200)[0]] * 256).encode())
        self.assertNotEqual(blocks[0], blocks[1])
        pal_a, pal_b = bytearray(neutral_palette(0)), bytearray(neutral_palette(255))
        pal_a[5 * 3:5 * 3 + 3] = bytes([255, 255, 255])
        pal_b[5 * 3:5 * 3 + 3] = bytes([0, 0, 0])
        blocks = self._lut([bytes(pal_a), bytes(pal_b)])
        self.assertEqual((blocks[0][5], blocks[0][4]), (ord("@"), ord(" ")))
        self.assertEqual((blocks[1][5], blocks[1][4]), (ord(" "), ord("@")))

    # -- env handling -----------------------------------------------------

    def test_env_parsed_once_lazily_from_setpalette(self):
        """Palette-before-graphics: env applies at I_SetPalette, once only."""
        for terminal, markers, expect in ((True, False, "terminal=1 markers=0"),
                                          (False, True, "terminal=0 markers=1")):
            proc = self._run("envprobe", data=neutral_palette(0),
                             terminal=terminal, markers=markers)
            self.assertEqual(proc.returncode, 0, proc.stderr)
            self.assertEqual(proc.stdout.decode().strip(), expect)
        # Env flipped after the palette call must not re-apply (cfg_env_parsed).
        proc = self._run("envprobe", ["change"], data=neutral_palette(0),
                         terminal=True, markers=False)
        self.assertEqual(proc.stdout.decode().strip(), "terminal=1 markers=0")

    # -- frame output -----------------------------------------------------

    def test_plain_frame_per_cell_79x24_crlf_no_ansi(self):
        palette = neutral_palette(127)
        out = self.tmp / "plain.out"
        proc = self._run("frames", [out], data=palette, terminal=True)
        self.assertEqual(proc.returncode, 0, proc.stderr)
        data = out.read_bytes()
        self.assertEqual(len(data), 79 * 24 + 2 * 24)
        self.assertEqual(data.count(b"\r\n"), 24)
        self.assertNotIn(b"\x1b", data)
        self.assertEqual(data, self._expected_plain_frame(palette))

    def test_plain_frame_refresh_with_second_palette(self):
        pal_a, pal_b = neutral_palette(100), neutral_palette(200)
        f1, f2 = self.tmp / "f1.out", self.tmp / "f2.out"
        proc = self._run("frames", [f1, f2], data=pal_a + pal_b, terminal=True)
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertEqual(f1.read_bytes(), self._expected_plain_frame(pal_a))
        self.assertEqual(f2.read_bytes(), self._expected_plain_frame(pal_b))
        self.assertNotEqual(f1.read_bytes(), f2.read_bytes())

    # -- diagnostics markers ----------------------------------------------

    def test_markers_default_on_stderr(self):
        proc = self._run("frames", [self.tmp / "m.out"], data=neutral_palette(0),
                         terminal=True)
        self.assertEqual(proc.stderr,
                         b"RVC_DOOM_FRAME_BEGIN n=1\nRVC_FRAME=1 CYCLES=0\n"
                         b"\r\nRVC_DOOM_FRAME_END n=1\r\n")

    def test_markers_env0_no_diag(self):
        proc = self._run("frames", [self.tmp / "m.out"], data=neutral_palette(0),
                         terminal=True, markers=False)
        self.assertEqual(proc.stderr, b"")

    # -- ANSI reference ---------------------------------------------------

    def test_ansi_output_unchanged_vs_pinned_reference(self):
        """ANSI bytes must match the frozen pre-LUT driver, never HEAD."""
        if self.ref_bin is None:
            ref_src = self.tmp / "ref_video_console.c"
            try:
                fixture = HERE / "test-fixtures/video_console-2bb6d5f4.c"
                ref_src.write_bytes(fixture.read_bytes())
                if ref_src.read_bytes() == DRIVER.read_bytes():
                    raise AssertionError(
                        "pinned reference driver %s equals the current driver; "
                        "self-comparison - keep the pin on a pre-LUT-change "
                        "commit" % REF_DRIVER_COMMIT)
            except (OSError, subprocess.SubprocessError) as exc:
                raise AssertionError("cannot extract pinned reference driver %s "
                                     "(ANSI-unchanged check needs it): %s"
                                     % (REF_DRIVER_COMMIT, exc))
            ref_bin = self.tmp / "vin_harness_ref"
            ok, diag = _compile_harness(ref_src, self.engine_src, ref_bin,
                                        self.cc, new_statics=False)
            if not ok:
                raise AssertionError("pinned reference driver %s fails to compile:\n"
                                     % REF_DRIVER_COMMIT + diag)
            self.ref_bin = ref_bin
        palette = neutral_palette(127)
        out_new, out_ref = self.tmp / "ansi_new.out", self.tmp / "ansi_ref.out"
        env = os.environ.copy()
        env.pop("RVC_DOOM_TERMINAL", None)
        env.pop("RVC_DOOM_MARKERS", None)
        for bin_path, out in ((self.bin, out_new), (self.ref_bin, out_ref)):
            proc = subprocess.run([str(bin_path), "frames", str(out)], input=palette,
                                  capture_output=True, env=env)
            self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertEqual(out_new.read_bytes(), out_ref.read_bytes(),
                         "ANSI path changed; only the plain LUT was meant to move")
        self.assertIn(b"\x1b[", out_new.read_bytes())
        self.assertNotIn(b"\r\n", out_new.read_bytes())


if __name__ == "__main__":
    unittest.main()
