#!/usr/bin/env python3
"""Replay captured UART ANSI byte streams and export terminal frames.

Bounded host tool for the rvc Doom/Linux port (see NOTES_DOOM.md: "UART
recording/replay ... outside src/").  Owns this file only; the emulator
sources under src/ and the shaders under _Nix/ are never modified.

Capture files are raw byte streams exactly as the guest wrote them to the
emulated UART ("preserve byte capture raw": input is never transcoded,
filtered, or re-encoded; every byte value reaches a cell unchanged).  This
tool interprets the ANSI terminal protocol in those bytes - SGR 30-37/40-47
plus reset (0/39/49), cursor addressing ESC[H and ESC[row;colH, screen
erase ESC[2J, line erase ESC[K, CR/LF/TAB/backspace, and basic cursor
movement - and exports a screen snapshot as:

  --output text  plain monospaced text, byte-faithful (latin-1 view)
  --output ppm   binary P6 PPM bitmap using an embedded 5x7 glyph font
  --output html  self-contained monospaced HTML with per-run color spans

Frame selection: the Doom payload terminates each frame with a raw marker on
the UART, for example:  ...frame bytes...\r\nRVC_DOOM_FRAME_END 1\r\n

--until MARKER replays exactly the bytes before the first occurrence of
MARKER (the marker itself is excluded).  If the marker is directly preceded
by CRLF that CRLF is clipped too - the input is cut just before
"\r\nRVC_DOOM_FRAME_END 1" - so the marker's own line ending never scrolls
the snapshot.  --frame N selects a frame by its terminator, accepting both the bare
form ("RVC_DOOM_FRAME_END N") and the keyed form ("RVC_DOOM_FRAME_END
n=N"); the Doom payload should emit the bare form.

Geometry findings from _Nix/rvc/framebuffer.shader and src/fb.h: the
emulator framebuffer is an 80x25 cell grid (wraps at column 80, scrolls at
row 25, texture row flip pos.y = 25 - pos.y - 1).  Its terminal model has
no ANSI color and no cursor addressing: fb.h interpret() keys all CSI off
ctrlSeq.y == 0x9b (C1 CSI), while the UART carries standard ESC [ (0x5b)
bytes verbatim (src/uart.h seeds the fifo with raw bytes and _Nix
types.h decode_for_commit() copies them without transformation).  With a
standard ESC[ stream this framebuffer therefore executes no ESC[ sequence
at all - no ESC[H / ESC[row;colH, no ESC[2J, no SGR.  The replay terminal
defaults to the standard ANSI 80x24 drawing grid, interprets full ANSI from
the captured stream (per NOTES_DOOM.md, ANSI interpretation belongs to the
host replay terminal), and can be sized with --cols/--rows (for example
--rows 25 for framebuffer parity).
"""

from __future__ import annotations

import argparse
import sys
import os

__version__ = "0.1.0"

# Payload frame marker convention: a bare line printed on the UART between
# frames.  Canonical form is "RVC_DOOM_FRAME_END <N>"; --frame also accepts
# the keyed form "RVC_DOOM_FRAME_END n=<N>" for payloads that emit it.
FRAME_MARKER = b"RVC_DOOM_FRAME_END "

# ---------------------------------------------------------------------------
# ANSI color model
# ---------------------------------------------------------------------------

# SGR 30-37 / 40-47 standard 8-color palette (classic VGA-ish values).
ANSI_BASE = (
    0x000000,  # 0 black
    0xAA0000,  # 1 red
    0x00AA00,  # 2 green
    0xAA5500,  # 3 yellow (brown)
    0x0000AA,  # 4 blue
    0xAA00AA,  # 5 magenta
    0x00AAAA,  # 6 cyan
    0xAAAAAA,  # 7 white (light gray)
)

# SGR 90-97 / 100-107 bright variants (bold on a base color maps here).
ANSI_BRIGHT = (
    0x555555,  # 0 bright black (gray)
    0xFF5555,  # 1 bright red
    0x55FF55,  # 2 bright green
    0xFFFF55,  # 3 bright yellow
    0x5555FF,  # 4 bright blue
    0xFF55FF,  # 5 bright magenta
    0x55FFFF,  # 6 bright cyan
    0xFFFFFF,  # 7 bright white
)

DEFAULT_FG = 0xAAAAAA   # SGR 39 / start of session
DEFAULT_BG = 0x000000   # SGR 49 / start of session
BLANK_CHAR = 0x20       # ord(' ')


def _hex_color(v: int) -> str:
    return "#%06x" % v


# ---------------------------------------------------------------------------
# Embedded 5x7 bitmap font (rows of 5 bits, bit 4 = leftmost column).
# Readable tiny font used by the PPM exporter; covers ASCII 32-126.
# ---------------------------------------------------------------------------

_F = {
    " ": (0, 0, 0, 0, 0, 0, 0),
    "!": (4, 4, 4, 4, 4, 0, 4),
    '"': (10, 10, 10, 0, 0, 0, 0),
    "#": (10, 10, 31, 10, 31, 10, 10),
    "$": (4, 15, 20, 14, 5, 30, 4),
    "%": (25, 26, 2, 4, 8, 11, 19),
    "&": (12, 18, 20, 8, 21, 18, 13),
    "'": (4, 4, 8, 0, 0, 0, 0),
    "(": (2, 4, 8, 8, 8, 4, 2),
    ")": (8, 4, 2, 2, 2, 4, 8),
    "*": (0, 10, 4, 31, 4, 10, 0),
    "+": (0, 4, 4, 31, 4, 4, 0),
    ",": (0, 0, 0, 0, 12, 4, 8),
    "-": (0, 0, 0, 31, 0, 0, 0),
    ".": (0, 0, 0, 0, 0, 12, 12),
    "/": (1, 2, 2, 4, 8, 8, 16),
    "0": (14, 17, 19, 21, 25, 17, 14),
    "1": (4, 12, 4, 4, 4, 4, 14),
    "2": (30, 1, 1, 2, 4, 8, 31),
    "3": (30, 1, 1, 14, 1, 1, 30),
    "4": (2, 6, 10, 18, 31, 2, 2),
    "5": (31, 16, 30, 1, 1, 17, 14),
    "6": (14, 16, 16, 30, 17, 17, 14),
    "7": (31, 1, 2, 4, 8, 8, 8),
    "8": (14, 17, 17, 14, 17, 17, 14),
    "9": (14, 17, 17, 15, 1, 1, 14),
    ":": (0, 12, 12, 0, 12, 12, 0),
    ";": (0, 12, 12, 0, 12, 4, 8),
    "<": (2, 4, 8, 16, 8, 4, 2),
    "=": (0, 0, 31, 0, 31, 0, 0),
    ">": (8, 4, 2, 1, 2, 4, 8),
    "?": (14, 17, 1, 2, 4, 0, 4),
    "@": (14, 17, 1, 13, 21, 21, 14),
    "A": (14, 17, 17, 31, 17, 17, 17),
    "B": (30, 17, 17, 30, 17, 17, 30),
    "C": (14, 17, 16, 16, 16, 17, 14),
    "D": (28, 18, 17, 17, 17, 18, 28),
    "E": (31, 16, 16, 30, 16, 16, 31),
    "F": (31, 16, 16, 30, 16, 16, 16),
    "G": (14, 17, 16, 23, 17, 17, 15),
    "H": (17, 17, 17, 31, 17, 17, 17),
    "I": (14, 4, 4, 4, 4, 4, 14),
    "J": (7, 2, 2, 2, 2, 18, 12),
    "K": (17, 18, 20, 24, 20, 18, 17),
    "L": (16, 16, 16, 16, 16, 16, 31),
    "M": (17, 27, 21, 21, 17, 17, 17),
    "N": (17, 25, 21, 19, 17, 17, 17),
    "O": (14, 17, 17, 17, 17, 17, 14),
    "P": (30, 17, 17, 30, 16, 16, 16),
    "Q": (14, 17, 17, 17, 21, 18, 13),
    "R": (30, 17, 17, 30, 20, 18, 17),
    "S": (15, 16, 16, 14, 1, 1, 30),
    "T": (31, 4, 4, 4, 4, 4, 4),
    "U": (17, 17, 17, 17, 17, 17, 14),
    "V": (17, 17, 17, 17, 17, 10, 4),
    "W": (17, 17, 17, 21, 21, 27, 17),
    "X": (17, 17, 10, 4, 10, 17, 17),
    "Y": (17, 17, 10, 4, 4, 4, 4),
    "Z": (31, 1, 2, 4, 8, 16, 31),
    "[": (14, 8, 8, 8, 8, 8, 14),
    "\\": (16, 8, 8, 4, 2, 2, 1),
    "]": (14, 2, 2, 2, 2, 2, 14),
    "^": (4, 10, 17, 0, 0, 0, 0),
    "_": (0, 0, 0, 0, 0, 0, 31),
    "`": (8, 4, 2, 0, 0, 0, 0),
    "a": (0, 0, 14, 1, 15, 17, 15),
    "b": (16, 16, 30, 17, 17, 17, 30),
    "c": (0, 0, 14, 16, 16, 17, 14),
    "d": (1, 1, 15, 17, 17, 17, 15),
    "e": (0, 0, 14, 17, 31, 16, 14),
    "f": (6, 9, 8, 28, 8, 8, 8),
    "g": (0, 15, 17, 17, 15, 1, 14),
    "h": (16, 16, 30, 17, 17, 17, 17),
    "i": (4, 0, 12, 4, 4, 4, 14),
    "j": (2, 0, 6, 2, 2, 18, 12),
    "k": (16, 16, 18, 20, 24, 20, 18),
    "l": (12, 4, 4, 4, 4, 4, 14),
    "m": (0, 0, 26, 21, 21, 21, 21),
    "n": (0, 0, 30, 17, 17, 17, 17),
    "o": (0, 0, 14, 17, 17, 17, 14),
    "p": (0, 30, 17, 17, 30, 16, 16),
    "q": (0, 15, 17, 17, 15, 1, 1),
    "r": (0, 0, 22, 25, 16, 16, 16),
    "s": (0, 0, 15, 16, 14, 1, 30),
    "t": (8, 8, 28, 8, 8, 9, 6),
    "u": (0, 0, 17, 17, 17, 19, 13),
    "v": (0, 0, 17, 17, 17, 10, 4),
    "w": (0, 0, 17, 17, 21, 21, 10),
    "x": (0, 0, 17, 10, 4, 10, 17),
    "y": (0, 17, 17, 17, 15, 1, 14),
    "z": (0, 0, 31, 2, 4, 8, 31),
    "{": (2, 4, 4, 8, 4, 4, 2),
    "|": (4, 4, 4, 4, 4, 4, 4),
    "}": (8, 4, 4, 2, 4, 4, 8),
    "~": (0, 0, 13, 18, 0, 0, 0),
}

GLYPH_W, GLYPH_H = 5, 7
_UNMAPPED_GLYPH = (31, 31, 31, 31, 31, 31, 31)  # solid block for unknown bytes


def _glyph_rows(ch: int):
    if 32 <= ch <= 126:
        g = _F.get(chr(ch))
        if g is not None:
            return g
    return _UNMAPPED_GLYPH


# ---------------------------------------------------------------------------
# Terminal model
# ---------------------------------------------------------------------------

class Terminal:
    """ANSI cell grid with fb.h-compatible wrap/scroll semantics."""

    def __init__(self, cols=80, rows=24, default_fg=DEFAULT_FG,
                 default_bg=DEFAULT_BG):
        self.cols = int(cols)
        self.rows = int(rows)
        self.default_fg = default_fg
        self.default_bg = default_bg
        self.reset()

    # -- state -------------------------------------------------------------

    def reset(self):
        self._grid = [[self._blank() for _ in range(self.cols)]
                      for _ in range(self.rows)]
        self._x = 0
        self._y = 0
        self._fg = self.default_fg
        self._bg = self.default_bg
        self._bold = False

    def _blank(self):
        return (BLANK_CHAR, self.default_fg, self.default_bg)

    @property
    def cursor(self):
        return (self._x, self._y)

    def grid(self):
        """Return a deep copy of the cell grid for inspection."""
        return [list(row) for row in self._grid]

    # -- helpers -----------------------------------------------------------

    def _resolved_fg(self):
        if not self._bold:
            return self._fg
        if self._fg == self.default_fg:
            return 0xFFFFFF
        if self._fg in ANSI_BASE:
            return ANSI_BRIGHT[ANSI_BASE.index(self._fg)]
        return self._fg

    def _scroll_up(self):
        del self._grid[0]
        self._grid.append([self._blank() for _ in range(self.cols)])

    def _wrap_check(self):
        if self._x >= self.cols:
            self._x = 0
            self._y += 1
            if self._y >= self.rows:
                self._y = self.rows - 1
                self._scroll_up()

    def _linefeed(self):
        self._y += 1
        if self._y >= self.rows:
            self._y = self.rows - 1
            self._scroll_up()

    def _put(self, ch):
        self._grid[self._y][self._x] = (ch, self._resolved_fg(), self._bg)

    def _erase_cell(self, y, x):
        self._grid[y][x] = (BLANK_CHAR, self._resolved_fg(), self._bg)

    # -- input -------------------------------------------------------------

    def feed(self, data: bytes):
        """Interpret one chunk of raw captured bytes (state machine)."""
        NORMAL, ESC, CHARSET, OSC, CSI, ABORTED = range(6)
        state = NORMAL
        csi_params = []
        csi_param = 0
        csi_len = 0
        osc_len = 0
        osc_st = False

        def flush_param():
            nonlocal csi_param
            csi_params.append(csi_param)
            csi_param = 0

        for b in data:
            if state == NORMAL:
                if b == 0x1B:
                    state = ESC
                elif b == 0x9B:            # 8-bit C1 CSI
                    state = CSI
                    csi_params, csi_param, csi_len = [], 0, 0
                elif b == 0x08:            # BS: fb.h clamps, no row wrap
                    self._x = max(0, self._x - 1)
                elif b == 0x09:            # TAB: fb.h formula
                    self._x = (self._x & ~7) + 8
                    self._wrap_check()
                elif b in (0x0A, 0x0B, 0x0C):  # LF / VT / FF
                    self._linefeed()
                elif b == 0x0D:            # CR: fb.h sets x only
                    self._x = 0
                elif b < 0x20 or b == 0x7F:
                    pass                    # unhandled C0/C1: ignored
                else:
                    self._put(b)
                    self._x += 1
                    self._wrap_check()
            elif state == ESC:
                if b == 0x5B:              # ESC [
                    state = CSI
                    csi_params, csi_param, csi_len = [], 0, 0
                elif b == 0x5D:            # ESC ] OSC: skip to BEL/ST
                    state = OSC
                    osc_len, osc_st = 0, False
                elif b == 0x28 or b == 0x29 or b == 0x2A:  # charset select
                    state = CHARSET
                elif b == 0x9B:            # ESC <C1 CSI> => CSI
                    state = CSI
                    csi_params, csi_param, csi_len = [], 0, 0
                else:
                    state = NORMAL         # unknown escape: reset (fb.h)
            elif state == CHARSET:
                state = NORMAL
            elif state == OSC:
                if osc_st:
                    state = NORMAL
                elif b == 0x07:
                    state = NORMAL
                elif b == 0x9C:
                    state = NORMAL
                elif b == 0x1B:
                    osc_st = True
                else:
                    osc_len += 1
                    if osc_len > 4096:     # guard against runaway OSC
                        state = NORMAL
            elif state == ABORTED:
                if 0x40 <= b <= 0x7E:      # end sequence at a final byte
                    state = NORMAL
            elif state == CSI:
                csi_len += 1
                if csi_len > 256:
                    state = ABORTED
                    continue
                if 0x30 <= b <= 0x39:      # digit
                    csi_param = min(csi_param * 10 + (b - 0x30), 1000000)
                elif b == 0x3B:            # ';'
                    flush_param()
                elif 0x20 <= b <= 0x2F or b in (0x3F, 0x3E, 0x3C, 0x3D, 0x21):
                    pass                    # private markers / intermediates
                elif 0x40 <= b <= 0x7E:    # final byte
                    flush_param()
                    self._csi(chr(b), csi_params)
                    state = NORMAL
                else:
                    state = NORMAL

    # -- CSI dispatch ------------------------------------------------------

    def _csi(self, final: str, params):
        if not params:
            params = [0]
        if final == "m":
            self._sgr(params)
        elif final in ("H", "f"):
            row = params[0] if params[0] else 1
            col = params[1] if len(params) > 1 and params[1] else 1
            self._x = min(max(col - 1, 0), self.cols - 1)
            self._y = min(max(row - 1, 0), self.rows - 1)
        elif final == "J":
            self._ed(params[0])
        elif final == "K":
            self._el(params[0])
        elif final == "A":
            self._y = max(self._y - (params[0] or 1), 0)
        elif final == "B":
            self._y = min(self._y + (params[0] or 1), self.rows - 1)
        elif final == "C":
            self._x = min(self._x + (params[0] or 1), self.cols - 1)
        elif final == "D":
            self._x = max(self._x - (params[0] or 1), 0)
        elif final == "G":
            self._x = min(max((params[0] or 1) - 1, 0), self.cols - 1)
        elif final == "d":
            self._y = min(max((params[0] or 1) - 1, 0), self.rows - 1)
        # any other final byte: ignored, state already reset

    def _sgr(self, params):
        i = 0
        while i < len(params):
            p = params[i]
            if p == 0:
                self._fg, self._bg, self._bold = (
                    self.default_fg, self.default_bg, False)
            elif p == 1:
                self._bold = True
            elif p == 22:
                self._bold = False
            elif 30 <= p <= 37:
                self._fg = ANSI_BASE[p - 30]
            elif p == 39:
                self._fg = self.default_fg
            elif 40 <= p <= 47:
                self._bg = ANSI_BASE[p - 40]
            elif p == 49:
                self._bg = self.default_bg
            elif 90 <= p <= 97:
                self._fg = ANSI_BRIGHT[p - 90]
            elif 100 <= p <= 107:
                self._bg = ANSI_BRIGHT[p - 100]
            elif p in (38, 48):
                # Extended color: consume the following parameters so the
                # parser stays in sync; the rendered value is out of scope.
                i += 1
                if i < len(params):
                    sub = params[i]
                    if sub == 5 and i + 1 < len(params):
                        i += 2
                    elif sub == 2 and i + 3 < len(params):
                        i += 4
                    else:
                        i += 1
                continue
            # other SGR (underlines etc.): ignored
            i += 1

    def _ed(self, mode):
        if mode == 2:
            for y in range(self.rows):
                for x in range(self.cols):
                    self._erase_cell(y, x)
        elif mode == 0:
            for x in range(self._x, self.cols):
                self._erase_cell(self._y, x)
            for y in range(self._y + 1, self.rows):
                for x in range(self.cols):
                    self._erase_cell(y, x)
        elif mode == 1:
            for x in range(0, self._x + 1):
                self._erase_cell(self._y, x)
            for y in range(0, self._y):
                for x in range(self.cols):
                    self._erase_cell(y, x)
        # mode 3 (scrollback): ignored

    def _el(self, mode):
        if mode == 2:
            for x in range(self.cols):
                self._erase_cell(self._y, x)
        elif mode == 0:
            for x in range(self._x, self.cols):
                self._erase_cell(self._y, x)
        elif mode == 1:
            for x in range(0, self._x + 1):
                self._erase_cell(self._y, x)


# ---------------------------------------------------------------------------
# Exporters
# ---------------------------------------------------------------------------

def export_text(term: Terminal) -> bytes:
    """Byte-faithful text: every cell char is the raw byte value (0-255),
    mapped through latin-1 so an encode('latin-1') round-trips exactly."""
    lines = []
    for row in term.grid():
        lines.append("".join(chr(c[0]) for c in row).rstrip())
    while lines and lines[-1] == "":
        lines.pop()
    if not lines:
        return b""
    while lines and lines[-1] == "":
        lines.pop()
    if not lines:
        return b""
    return ("\n".join(lines) + "\n").encode("latin-1")


def export_ppm(term: Terminal, scale: int = 2) -> bytes:
    """P6 PPM bitmap; each cell is a GLYPH_W x GLYPH_H glyph at scale."""
    scale = max(1, int(scale))
    w = term.cols * GLYPH_W * scale
    h = term.rows * GLYPH_H * scale
    img = bytearray(w * h * 3)

    def fill(x0, y0, color, extent):
        r = (color >> 16) & 0xFF
        g = (color >> 8) & 0xFF
        b = color & 0xFF
        for yy in range(y0, y0 + extent):
            base = (yy * w) * 3
            for xx in range(x0, x0 + extent):
                o = base + xx * 3
                img[o] = r
                img[o + 1] = g
                img[o + 2] = b

    grid = term.grid()
    for cy in range(term.rows):
        for cx in range(term.cols):
            ch, fg, bg = grid[cy][cx]
            rows = _glyph_rows(ch)
            for gy in range(GLYPH_H):
                bits = rows[gy]
                for gx in range(GLYPH_W):
                    on = (bits >> (4 - gx)) & 1
                    color = fg if on else bg
                    fill(cx * GLYPH_W * scale + gx * scale,
                         cy * GLYPH_H * scale + gy * scale,
                         color, scale)

    header = b"P6\n%d %d\n255\n" % (w, h)
    return header + bytes(img)


_HTML_ESCAPES = {"&": "&amp;", "<": "&lt;", ">": "&gt;"}


def _html_cell(ch: int) -> str:
    if ch == 0x20:
        return " "
    c = chr(ch)
    if c in _HTML_ESCAPES:
        return _HTML_ESCAPES[c]
    if 0x21 <= ch <= 0x7E:
        return chr(ch)
    return "&#%d;" % ch


def export_html(term: Terminal, title: str = "rvc-doom frame") -> str:
    """Self-contained monospaced HTML with run-length color spans."""
    runs = []
    for row in term.grid():
        for ch, fg, bg in row:
            key = (fg, bg)
            if runs and runs[-1][0] == key and runs[-1][2] == ch:
                runs[-1][1] += 1
            else:
                runs.append([key, 1, ch])
        runs.append([None, 0, -1])  # end of row marker

    parts = ["<!DOCTYPE html><html><head><meta charset=\"utf-8\">",
             "<title>%s</title>" % _text_escape(title),
             "<style>body{background:#000;margin:0;padding:8px}"
             "pre{display:table;font:14px/1.25 'DejaVu Sans Mono',Consolas,"
             "monospace;color:#aaaaaa;}span{white-space:pre}</style>",
             "</head><body><pre>"]
    for key, _count, ch in runs:
        if key is None:
            parts.append("\n")
            continue
        fg, bg = key
        parts.append("<span style=\"color:%s;background:%s\">%s</span>"
                     % (_hex_color(fg), _hex_color(bg), _html_cell(ch)))
    parts.append("</pre></body></html>")
    return "".join(parts)


def _text_escape(s: str) -> str:
    return s.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


# ---------------------------------------------------------------------------
# Capture handling
# ---------------------------------------------------------------------------

def clip_before_marker(data: bytes, marker: bytes):
    """Return (bytes to replay, marker offset or None).

    The prefix ends just before the first occurrence of 'marker'.  When the
    marker does not itself start with CR/LF and a CRLF directly precedes it,
    that CRLF is clipped too (payload convention: frames are terminated by
    "\\r\\nRVC_DOOM_FRAME_END N\\r\\n", and the terminator's own newline must
    not scroll the exported frame).
    """
    i = data.find(marker)
    if i < 0:
        return data, None
    end = i
    if (not marker.startswith((b"\r", b"\n"))
            and i >= 2 and data[i - 2:i] == b"\r\n"):
        end = i - 2
    return data[:end], i


def _find_frame_marker(data: bytes, n: int):
    """Locate frame n terminator as "RVC_DOOM_FRAME_END N" or
    "RVC_DOOM_FRAME_END n=N"; returns the matched marker bytes or None.
    The number must be followed by CR/LF or end of data so that frame 1
    never matches inside frame 10."""
    target = str(n).encode("ascii")
    pos = 0
    while True:
        i = data.find(FRAME_MARKER, pos)
        if i < 0:
            return None
        rest = data[i + len(FRAME_MARKER):]
        for form in (target, b"n=" + target):
            if rest.startswith(form):
                after = rest[len(form):]
                if not after or after[:1] in (b"\r", b"\n"):
                    return FRAME_MARKER + form
        pos = i + 1


def _unescape_marker(text: str) -> bytes:
    """Decode a small escape subset so ESC-type markers are typeable:
    \\x1b or \\e for ESC, plus \\n \\r \\t and a literal backslash."""
    out = bytearray()
    i = 0
    while i < len(text):
        c = text[i]
        if c != "\\" or i + 1 >= len(text):
            out.extend(text[i].encode("latin-1"))
            i += 1
            continue
        n = text[i + 1]
        if n in ("n", "r", "t", "e", "\\"):
            out.append({"\\n": 0x0A, "\\r": 0x0D, "\\t": 0x09,
                        "\\e": 0x1B, "\\\\": 0x5C}[text[i:i + 2]])
            i += 2
        elif n == "x" and i + 3 < len(text):
            try:
                out.append(int(text[i + 2:i + 4], 16))
                i += 4
            except ValueError:
                out.extend(text[i].encode("latin-1"))
                i += 1
        else:
            out.extend(text[i].encode("latin-1"))
            i += 1
    return bytes(out)


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def build_parser():
    p = argparse.ArgumentParser(
        prog="replay_uart.py",
        description="Replay a captured UART ANSI byte stream and export a "
                    "terminal frame snapshot (text, PPM, or HTML).")
    p.add_argument("capture", help="raw capture file, or - for stdin")
    p.add_argument("--cols", type=int, default=80,
                   help="grid width in cells (default 80; emulator fb is 80)")
    p.add_argument("--rows", type=int, default=24,
                   help="grid height in cells (default 24 ANSI; "
                        "emulator fb is 25)")
    p.add_argument("--output", choices=("text", "ppm", "html"), default="text",
                   help="export format (default text)")
    p.add_argument("--out", default=None,
                   help="output file (default stdout; '-' = stdout)")
    p.add_argument("--until", default=None, metavar="MARKER",
                   help="replay only the bytes before the first MARKER "
                        "(marker excluded; a directly preceding CRLF is "
                        "clipped; \\x1b \\e \\n \\r \\t escapes work)")
    p.add_argument("--frame", type=int, default=None, metavar="N",
                   help="select frame N (terminator RVC_DOOM_FRAME_END N, "
                        "or the n=N form)")
    p.add_argument("--scale", type=int, default=2,
                   help="pixels per font bit in PPM export (default 2)")
    p.add_argument("--title", default=None,
                   help="HTML page title (default from capture name)")
    p.add_argument("--version", action="version", version=__version__)
    return p


def _read_capture(path):
    if path == "-":
        return sys.stdin.buffer.read()
    with open(path, "rb") as fh:
        return fh.read()


def _write_output(path, payload: bytes):
    if path in (None, "-"):
        sys.stdout.buffer.write(payload)
    else:
        with open(path, "wb") as fh:
            fh.write(payload)


def main(argv=None):
    args = build_parser().parse_args(argv)
    if args.cols < 1 or args.rows < 1:
        sys.stderr.write("replay_uart: --cols/--rows must be >= 1\n")
        return 2
    if args.until and args.frame is not None:
        sys.stderr.write("replay_uart: use --until OR --frame, not both\n")
        return 2

    data = _read_capture(args.capture)
    prefix = data
    hit = None
    if args.frame is not None:
        marker = _find_frame_marker(data, args.frame)
        if marker is None:
            sys.stderr.write(
                "replay_uart: frame marker for %d not found in capture\n"
                % args.frame)
            return 2
        prefix, hit = clip_before_marker(data, marker)
    elif args.until is not None:
        marker = _unescape_marker(args.until)
        prefix, hit = clip_before_marker(data, marker)
        if hit is None:
            sys.stderr.write(
                "replay_uart: warning: marker %r not found; replaying the "
                "whole capture\n" % marker)

    term = Terminal(cols=args.cols, rows=args.rows)
    term.feed(prefix)

    if args.output == "text":
        payload = export_text(term)
    elif args.output == "ppm":
        payload = export_ppm(term, args.scale)
    else:
        title = args.title or (os.path.basename(args.capture) + " frame")
        payload = export_html(term, title).encode("utf-8")
    _write_output(args.out, payload)

    sys.stderr.write(
        "replay_uart: replayed %d of %d bytes%s, grid %dx%d, cursor %s, "
        "export %s\n"
        % (len(prefix), len(data),
           (", marker at %d" % hit) if hit is not None else "",
           term.cols, term.rows, term.cursor, args.output))
    return 0


if __name__ == "__main__":
    sys.exit(main())
