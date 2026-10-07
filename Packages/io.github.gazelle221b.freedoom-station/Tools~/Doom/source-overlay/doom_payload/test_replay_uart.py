#!/usr/bin/env python3
"""Meaningful unit tests for replay_uart.py (stdlib unittest).

Covers defaults, cursor addressing, SGR colors/reset, CR/LF/TAB, wrapping,
scrolling, screen/line erase, raw byte preservation, marker-based frame
clipping, exporters, and a CLI smoke test.  Run with:

    py -m unittest test_replay_uart -v
"""

from __future__ import annotations

import os
import subprocess
import sys
import tempfile
import unittest

import replay_uart as r

ESC = b"\x1b"
CSI = ESC + b"["


def cell(term, x, y):
    return term.grid()[y][x]


def chars(term):
    return [[c[0] for c in row] for row in term.grid()]


class TestDefaults(unittest.TestCase):
    def test_initial_grid_and_cursor(self):
        t = r.Terminal()
        self.assertEqual((t.cols, t.rows), (80, 24))
        self.assertEqual(t.cursor, (0, 0))
        for row in t.grid():
            for ch, fg, bg in row:
                self.assertEqual(ch, r.BLANK_CHAR)
                self.assertEqual(fg, r.DEFAULT_FG)
                self.assertEqual(bg, r.DEFAULT_BG)

    def test_nondefault_grid_size(self):
        t = r.Terminal(cols=12, rows=5)
        self.assertEqual((len(t.grid()), len(t.grid()[0])), (5, 12))


class TestCursor(unittest.TestCase):
    def setUp(self):
        self.t = r.Terminal(cols=10, rows=5)

    def test_home(self):
        self.t.feed(b"ABCDE" + ESC + b"[H")
        self.assertEqual(self.t.cursor, (0, 0))
        self.assertEqual(chars(self.t)[0][:5], list(b"ABCDE"))

    def test_position_row_col_1based(self):
        self.t.feed(CSI + b"2;5H")
        self.assertEqual(self.t.cursor, (4, 1))
        self.t.feed(CSI + b"H")           # bare H = home
        self.assertEqual(self.t.cursor, (0, 0))

    def test_position_clamps(self):
        self.t.feed(CSI + b"99;99H")
        self.assertEqual(self.t.cursor, (9, 4))
        self.t.feed(CSI + b"0;0H")        # 0 means default = 1
        self.assertEqual(self.t.cursor, (0, 0))

    def test_moves_and_clamp(self):
        self.t.feed(CSI + b"B")           # down
        self.assertEqual(self.t.cursor, (0, 1))
        self.t.feed(CSI + b"3C")          # right x3
        self.assertEqual(self.t.cursor, (3, 1))
        self.t.feed(CSI + b"2A")          # up x2 -> clamp to 0
        self.assertEqual(self.t.cursor, (3, 0))
        self.t.feed(CSI + b"99D")         # left x99 -> clamp 0
        self.assertEqual(self.t.cursor, (0, 0))
        self.t.feed(CSI + b"99B")         # down beyond bottom: clamp, no scroll
        self.assertEqual(self.t.cursor, (0, 4))

    def test_no_scroll_on_cursor_down(self):
        self.t.feed(CSI + b"5B")
        self.assertEqual(self.t.cursor, (0, 4))
        self.assertEqual(chars(self.t), chars(r.Terminal(cols=10, rows=5)))


class TestSgr(unittest.TestCase):
    def test_fg_colors_30_37(self):
        t = r.Terminal(cols=10, rows=3)
        for code, color in zip(range(30, 38), r.ANSI_BASE):
            t.feed(CSI + ("%dmX" % code).encode())
            self.assertEqual(cell(t, t.cursor[0] - 1, 0)[1], color,
                             "SGR %d" % code)

    def test_bg_colors_40_47(self):
        t = r.Terminal(cols=10, rows=3)
        for code, color in zip(range(40, 48), r.ANSI_BASE):
            t.feed(CSI + ("%dmX" % code).encode())
            self.assertEqual(cell(t, t.cursor[0] - 1, 0)[2], color,
                             "SGR %d" % code)

    def test_reset_sgr0(self):
        t = r.Terminal(cols=6, rows=1)
        t.feed(CSI + b"31;41mR" + CSI + b"0mG")
        self.assertEqual(cell(t, 0, 0), (ord("R"), 0xAA0000, 0xAA0000))
        self.assertEqual(cell(t, 1, 0), (ord("G"), r.DEFAULT_FG, r.DEFAULT_BG))

    def test_default_fg_bg_39_49(self):
        t = r.Terminal(cols=6, rows=1)
        t.feed(CSI + b"31;44mR" + CSI + b"39;49mG")
        self.assertEqual(cell(t, 0, 0), (ord("R"), 0xAA0000, 0x0000AA))
        self.assertEqual(cell(t, 1, 0), (ord("G"), r.DEFAULT_FG, r.DEFAULT_BG))

    def test_bold_maps_to_bright(self):
        t = r.Terminal(cols=6, rows=1)
        t.feed(CSI + b"1;31mR")
        self.assertEqual(cell(t, 0, 0)[1], 0xFF5555)
        t.feed(CSI + b"0;1mW")
        self.assertEqual(cell(t, 1, 0)[1], 0xFFFFFF)
        t.feed(CSI + b"22mN")             # bold off -> default gray
        self.assertEqual(cell(t, 2, 0)[1], r.DEFAULT_FG)

    def test_unknown_sgr_ignored(self):
        t = r.Terminal(cols=6, rows=1)
        t.feed(CSI + b"4;5;31mR")          # underline/blink/red
        self.assertEqual(cell(t, 0, 0)[1], 0xAA0000)
        t.feed(CSI + b"38;5;196mG")        # extended: consumed, ignored
        self.assertEqual(cell(t, 1, 0)[1], 0xAA0000)


class TestControls(unittest.TestCase):
    def test_cr_resets_x_only(self):
        t = r.Terminal(cols=8, rows=3)
        t.feed(b"ABC\rD")
        self.assertEqual(chars(t)[0][0], ord("D"))
        self.assertEqual(cell(t, 1, 0)[0], ord("B"))
        self.assertEqual(t.cursor, (1, 0))

    def test_lf_advances_y_keeps_x(self):
        t = r.Terminal(cols=8, rows=3)
        t.feed(b"XY\nZ")
        self.assertEqual(chars(t)[0][0:2], list(b"XY"))
        self.assertEqual(cell(t, 2, 1)[0], ord("Z"))
        self.assertEqual(t.cursor, (3, 1))

    def test_tab_to_next_8(self):
        t = r.Terminal(cols=20, rows=1)
        t.feed(b"A\tB")
        self.assertEqual(cell(t, 8, 0)[0], ord("B"))
        t = r.Terminal(cols=20, rows=1)
        t.feed(b"123456789\tC")
        self.assertEqual(cell(t, 16, 0)[0], ord("C"))

    def test_backspace_clamps_left(self):
        t = r.Terminal(cols=8, rows=3)
        t.feed(b"AB\x08\x08\x08")
        self.assertEqual(t.cursor, (0, 0))


class TestWrapAndScroll(unittest.TestCase):
    def test_wrap_at_cols(self):
        t = r.Terminal(cols=5, rows=3)
        t.feed(b"ABCDE")                   # fills row 0 exactly
        self.assertEqual(t.cursor, (0, 1))
        t.feed(b"F")
        self.assertEqual(cell(t, 0, 1)[0], ord("F"))
        self.assertEqual(chars(t)[0], list(b"ABCDE"))

    def test_scroll_on_lf_at_bottom(self):
        t = r.Terminal(cols=4, rows=3)
        t.feed(b"AAA\r\nBBB\r\nCCC\r\nDDD")
        self.assertEqual(chars(t)[0], list(b"BBB "))
        self.assertEqual(chars(t)[1], list(b"CCC "))
        self.assertEqual(chars(t)[2], list(b"DDD "))
        self.assertEqual(t.cursor, (3, 2))

    def test_scroll_on_wrap_at_bottom(self):
        t = r.Terminal(cols=3, rows=2)
        t.feed(b"ABCABCABC")
        # 9 chars: two are dropped by the final scroll; the surviving copy
        # of "ABC" (last full row before the scroll) is at the top.
        self.assertEqual(chars(t)[0], list(b"ABC"))
        self.assertEqual(chars(t)[1], list(b"   "))
        self.assertEqual(t.cursor, (0, 1))


class TestErase(unittest.TestCase):
    def test_ed2_clears_whole_screen(self):
        t = r.Terminal(cols=6, rows=3)
        t.feed(b"AAAAAA\nBB" + CSI + b"2J")
        self.assertEqual(chars(t), chars(r.Terminal(cols=6, rows=3)))

    def test_ed2_keeps_cursor(self):
        t = r.Terminal(cols=6, rows=3)
        t.feed(b"AAAA\nBB\nC" + CSI + b"2J")
        self.assertEqual(t.cursor, (1, 2))

    def test_ed_erasures_from_cursor(self):
        t = r.Terminal(cols=6, rows=3)
        t.feed(b"AAAABB" + CSI + b"2H" + CSI + b"K")
        # cursor on row 1 col 0; erase to end of line
        self.assertEqual(chars(t)[1], [r.BLANK_CHAR] * 6)
        self.assertEqual(chars(t)[0], list(b"AAAABB"))

    def test_el2_whole_line(self):
        t = r.Terminal(cols=6, rows=2)
        t.feed(b"AAAAAAB" + CSI + b"H" + CSI + b"2K")
        self.assertEqual(chars(t)[0], [r.BLANK_CHAR] * 6)
        self.assertEqual(cell(t, 0, 1)[0], ord("B"))


class TestMarkerClipping(unittest.TestCase):
    def test_clip_before_marker_strips_preceding_crlf(self):
        data = b"frame bytes\r\nRVC_DOOM_FRAME_END 1\r\nnext frame"
        prefix, hit = r.clip_before_marker(data, b"RVC_DOOM_FRAME_END 1")
        self.assertEqual(prefix, b"frame bytes")
        self.assertEqual(hit, len(b"frame bytes\r\n"))

    def test_clip_keeps_plain_prefix(self):
        data = b"abcMARK"
        prefix, hit = r.clip_before_marker(data, b"MARK")
        self.assertEqual(prefix, b"abc")
        self.assertEqual(hit, 3)

    def test_clip_marker_leading_crlf_no_double_strip(self):
        data = b"abc\r\nCRLF_MARK"
        prefix, hit = r.clip_before_marker(data, b"\r\nCRLF_MARK")
        self.assertEqual(prefix, b"abc")
        self.assertEqual(data[hit:hit + 2], b"\r\n")

    def test_missing_marker_returns_full_data(self):
        data = b"no marker here"
        prefix, hit = r.clip_before_marker(data, b"RVC_DOOM_FRAME_END 2")
        self.assertEqual(prefix, data)
        self.assertIsNone(hit)

    def test_unescape_marker_escapes(self):
        self.assertEqual(r._unescape_marker(r"\x1b[2J"), b"\x1b[2J")
        self.assertEqual(r._unescape_marker(r"a\eb"), b"a\x1bb")
        self.assertEqual(r._unescape_marker(r"x\ny\rz"), b"x\ny\rz")
        self.assertEqual(r._unescape_marker(r"back\\slash"), b"back\\slash")

    def test_frame_marker_bare_and_keyed_forms(self):
        data = (b"one\r\nRVC_DOOM_FRAME_END 1\r\n"
                b"two\r\nRVC_DOOM_FRAME_END n=2\r\n")
        m1 = r._find_frame_marker(data, 1)
        self.assertEqual(m1, b"RVC_DOOM_FRAME_END 1")
        prefix, hit = r.clip_before_marker(data, m1)
        self.assertEqual(prefix, b"one")
        m2 = r._find_frame_marker(data, 2)
        self.assertEqual(m2, b"RVC_DOOM_FRAME_END n=2")
        prefix2, _ = r.clip_before_marker(data, m2)
        self.assertEqual(prefix2, b"one\r\nRVC_DOOM_FRAME_END 1\r\ntwo")

    def test_frame_marker_not_a_prefix_of_larger_number(self):
        data = (b"A\r\nRVC_DOOM_FRAME_END 1\r\nB\r\n"
                b"RVC_DOOM_FRAME_END 10\r\nC")
        m1 = r._find_frame_marker(data, 1)
        self.assertIsNotNone(m1)
        prefix, _ = r.clip_before_marker(data, m1)
        self.assertEqual(prefix, b"A")
        m10 = r._find_frame_marker(data, 10)
        self.assertIsNotNone(m10)
        prefix10, _ = r.clip_before_marker(data, m10)
        self.assertEqual(prefix10, b"A\r\nRVC_DOOM_FRAME_END 1\r\nB")
        self.assertIsNone(r._find_frame_marker(data, 4))


class TestRawBytes(unittest.TestCase):
    def test_high_bytes_preserved_in_cells(self):
        t = r.Terminal(cols=8, rows=1)
        payload = b"caf\xe9"                 # raw latin-1 byte 0xE9
        t.feed(payload)
        self.assertEqual([c[0] for c in t.grid()[0][:4]], list(payload))

    def test_text_export_roundtrip_latin1(self):
        t = r.Terminal(cols=8, rows=1)
        t.feed(b"caf\xe9")
        out = r.export_text(t)
        self.assertEqual(out.decode("latin-1"), "caf\xe9\n")
        self.assertTrue(b"\xe9" in out)      # byte kept, not lossy-decoded

    def test_unhandled_c0_ignored(self):
        t = r.Terminal(cols=8, rows=2)
        t.feed(b"A\x04\x07\x00B")
        self.assertEqual([c[0] for c in t.grid()[0][:2]], list(b"AB"))

    def test_8bit_csi_initiator(self):
        t = r.Terminal(cols=6, rows=2)
        t.feed(b"XX\x9b2J")                  # C1 CSI ED 2
        self.assertEqual(chars(t), chars(r.Terminal(cols=6, rows=2)))
        t = r.Terminal(cols=6, rows=2)
        t.feed(b"\x9b31mR")                  # C1 CSI SGR 31
        self.assertEqual(cell(t, 0, 0)[1], 0xAA0000)


class TestUnknownSequences(unittest.TestCase):
    def test_private_csi_ignored(self):
        t = r.Terminal(cols=8, rows=2)
        t.feed(b"AB" + CSI + b"?25l" + CSI + b">0c" + b"C")
        self.assertEqual([c[0] for c in t.grid()[0][:3]], list(b"ABC"))
        self.assertEqual(t.cursor, (3, 0))

    def test_osc_title_skipped(self):
        t = r.Terminal(cols=8, rows=2)
        t.feed(b"AB" + ESC + b"]0;my title\x07C")
        self.assertEqual([c[0] for c in t.grid()[0][:3]], list(b"ABC"))

    def test_escapeseq_other_ignored(self):
        t = r.Terminal(cols=8, rows=2)
        t.feed(b"AB" + ESC + b"(B" + b"C")    # charset select then text
        self.assertEqual([c[0] for c in t.grid()[0][:3]], list(b"ABC"))

    def test_long_csi_aborted(self):
        t = r.Terminal(cols=8, rows=2)
        t.feed(b"AB" + CSI + b"3" * 300 + b"H" + b"C")
        self.assertEqual([c[0] for c in t.grid()[0][:3]], list(b"ABC"))


class TestExports(unittest.TestCase):
    def test_text_export_layout(self):
        t = r.Terminal(cols=8, rows=3)
        t.feed(b"AB\nCD")
        self.assertEqual(r.export_text(t).decode("latin-1"), "AB\n  CD\n")

    def test_ppm_header_and_dims(self):
        t = r.Terminal(cols=8, rows=3)
        out = r.export_ppm(t, scale=2)
        head, _, rest = out.partition(b"\n255\n")
        self.assertEqual(head, b"P6\n80 42")
        self.assertEqual(len(rest), 80 * 42 * 3)

    def test_ppm_fg_and_bg_pixels(self):
        t = r.Terminal(cols=3, rows=1)
        t.feed(b"\x1b[31m!\x1b[0m ")          # red '!' then space
        out = r.export_ppm(t, scale=1)
        w = 3 * r.GLYPH_W
        header_len = len(b"P6\n%d %d\n255\n" % (w, r.GLYPH_H))
        def px(x, y):
            o = header_len + (y * w + x) * 3
            return tuple(out[o:o + 3])
        # '!' glyph row 0 sets bit at column 2
        self.assertEqual(px(2, 0), (0xAA, 0x00, 0x00))
        self.assertEqual(px(0, 0), (0x00, 0x00, 0x00))   # bg black
        # second cell is a space: all glyph pixels off
        self.assertEqual(px(5 + 2, 0), (0x00, 0x00, 0x00))

    def test_html_colors_and_escaping(self):
        t = r.Terminal(cols=8, rows=1)
        t.feed(b"\x1b[31mA&B\x1b[0m ")
        html = r.export_html(t)
        self.assertIn("color:#aa0000", html)
        self.assertIn("&amp;", html)
        self.assertIn("</pre></body></html>", html)


class TestCli(unittest.TestCase):
    def _run(self, args):
        here = os.path.dirname(os.path.abspath(__file__))
        return subprocess.run(
            [sys.executable, os.path.join(here, "replay_uart.py")] + args,
            capture_output=True, cwd=here)

    def test_cli_frame_selection(self):
        cap = (b"\x1b[2J\x1b[Hred frame\r\nRVC_DOOM_FRAME_END 1\r\n"
               b"\x1b[2J\x1b[Hsecond frame\r\nRVC_DOOM_FRAME_END 2\r\n")
        with tempfile.NamedTemporaryFile(suffix=".bin", delete=False) as fh:
            fh.write(cap)
            path = fh.name
        try:
            res = self._run([path, "--frame", "1", "--output", "text"])
            self.assertEqual(res.returncode, 0)
            self.assertEqual(res.stdout.decode("latin-1"), "red frame\n")
            self.assertIn(b"marker at", res.stderr)
            res2 = self._run([path, "--frame", "2", "--output", "text"])
            self.assertEqual(res2.stdout.decode("latin-1"), "second frame\n")
            res3 = self._run([path, "--frame", "9", "--output", "text"])
            self.assertEqual(res3.returncode, 2)      # missing marker
        finally:
            os.unlink(path)

    def test_cli_until_escaped_marker(self):
        cap = b"before\x1b[2Jclear\r\nEND"
        with tempfile.NamedTemporaryFile(suffix=".bin", delete=False) as fh:
            fh.write(cap)
            path = fh.name
        try:
            res = self._run([path, "--until", r"\x1b[2J", "--output", "text"])
            self.assertEqual(res.returncode, 0)
            self.assertEqual(res.stdout.decode("latin-1"), "before\n")
        finally:
            os.unlink(path)


if __name__ == "__main__":
    unittest.main(verbosity=2)
