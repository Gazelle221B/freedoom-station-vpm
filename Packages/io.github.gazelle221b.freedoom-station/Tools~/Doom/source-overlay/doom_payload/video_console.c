/*
 * experiments/emdoom/video_console.c  (rvc DOOM variant tree)
 *
 * Terminal video driver for the PiMaker/rvc DOOM port ("MMU Linux stdout").
 *
 * Derived from cnlohr/mini-rv32ima experiments/emdoom/video_console.c
 * (https://github.com/cnlohr/mini-rv32ima, commit 84858f58cb41899705e2ff2d6ee3b2d5c0795bfe,
 * "Stubbed Video.c, for terminals"), which drives the DOOM engine of
 * cnlohr/embeddeddoom (https://github.com/cnlohr/embeddeddoom, commit
 * b52f80968a25a90b2ab0cf6c97703876b2d56e59), itself "lifted directly from
 * https://github.com/id-Software/DOOM" (embeddeddoom/README.md).
 *
 * Copyright (C) 1993-1996 by id Software, Inc.   (DOOM engine lineage)
 * Copyright (c) CNLohr                        (terminal driver lineage)
 * Modifications for rvc MMU-Linux stdout (2026-10-01)   (this file)
 *
 * License: see the LICENSE files shipped with the variant build tree.
 *   - doom_payload/licenses/embeddeddoom-LICENSE.md : original id Software DOOM license text, copied
 *                             verbatim from the upstream embeddeddoom repo
 *                             (the 1997 "Limited Use Software License Agreement").
 *   - doom_payload/licenses/DOOM-GPL-2.0.txt   : GPL v2 text as published in the official
 *                             id-Software/DOOM repository (LICENSE.TXT, master),
 *                             reflecting id's 1999 relicensing of the DOOM
 *                             source code under GPL-2.0.
 *   - doom_payload/licenses/LICENSE-NOTES.md      : provenance/discrepancy note.
 * The id-derived code in this file is distributed under GPL-2.0 following the
 * official id Software grant; the original license texts are preserved
 * verbatim and are not silently replaced.
 *
 * Include/layout note: quoted includes keep the upstream "embeddeddoom/src/..."
 * prefix. Compile with -I<build variant root> where <build variant root> is the
 * parent directory of the embeddeddoom checkout (the upstream Makefile clones
 * the engine to ./embeddeddoom inside this directory, and the variant Makefile
 * mirrors that).
 *
 * Changes vs upstream video_console.c (rvc-port requirements):
 *  - No MMIO access: all video output goes to stdout via a buffered write(1,...);
 *    HWEMIT() emits into that buffer. (Upstream wrote raw bytes to UART
 *    0x10000000, which only works under NOMMU user space; rvc runs an sv32 MMU
 *    Linux where that store would page-fault.)
 *  - ANSI mode renders 80x24 using the upstream RGB8 scheme (SGR 40-47
 *    background / SGR 30-37 foreground from palette bit planes) plus a 10-level
 *    ASCII luminance shade character.
 *  - Plain mode (env RVC_DOOM_TERMINAL=1): no escape sequences, 79 chars x 24
 *    rows, CRLF line ends; the terminal scrolls instead of cursor addressing.
 *  - Validation protocol (host measurement wrapper):
 *      * before every frame, on stderr, a standalone newline line:
 *          RVC_DOOM_FRAME_BEGIN n=<N>
 *      * after every frame, on stderr:
 *          \r\nRVC_DOOM_FRAME_END n=<N>\r\n
 *      * supplementary per-frame stderr line (rdcycle32 delta, wrap-safe,
 *        host/desktop fallback 0):  RVC_FRAME=<N> CYCLES=<delta>
 *      * env RVC_DOOM_FRAMES=N exits the process after N frames.
 *      * env RVC_DOOM_MARKERS=0 disables frame/cycle diagnostics for worlds.
 *    Logs go to stderr so stdout stays a clean video stream.
 *  - Input: Linux nonblocking raw termios (isatty-guarded termios + O_NONBLOCK
 *    on fd 0), FIONREAD probe, EAGAIN/EINTR/EOF handling with an is_eofd latch.
 *    Keyup handling preserved, release after 20 ticks. Upstream debug DOWN/UP
 *    prints are removed so they cannot corrupt the video stream.
 */

#include <stdlib.h>
#include <unistd.h>
#include <errno.h>
#include <string.h>
#include <signal.h>
#include <stdio.h>
#include <stdint.h>
#include <fcntl.h>
#include <sys/ioctl.h>
#include <termios.h>
#include <sys/time.h>

/* Header paths: see include/layout note above. */
#include "embeddeddoom/src/doomstat.h"
#include "embeddeddoom/src/i_system.h"
#include "embeddeddoom/src/v_video.h"
#include "embeddeddoom/src/m_argv.h"
#include "embeddeddoom/src/d_main.h"
#include "embeddeddoom/src/doomdef.h"
#include "embeddeddoom/src/r_main.h"

/* ------------------------------------------------------------------ */
/* Buffered stdout writer (replaces upstream HWEMIT raw UART poke).   */
/* ------------------------------------------------------------------ */

#define OUTBUF_SIZE 4096
static char obuf[OUTBUF_SIZE];
static size_t olen = 0;

static void raw_write_fd(int fd, const char *p, size_t n)
{
    while (n) {
        ssize_t w = write(fd, p, n);
        if (w > 0) {
            p += w;
            n -= (size_t)w;
        } else if (w < 0 && errno == EINTR) {
            continue;
        } else {
            /* EAGAIN (nonblocking stdout) or error: drop the rest rather
             * than stall the game loop. */
            break;
        }
    }
}

static void out_flush(void)
{
    if (olen) {
        raw_write_fd(1, obuf, olen);
        olen = 0;
    }
}

static void out_emit(const char *s, size_t n)
{
    if (olen + n > sizeof(obuf)) {
        out_flush();
        if (n >= sizeof(obuf)) {
            raw_write_fd(1, s, n);
            return;
        }
    }
    memcpy(obuf + olen, s, n);
    olen += n;
}

/* Upstream entry point, now stdout-buffered and MMIO-free. */
void HWEMIT(const char *s)
{
    out_emit(s, strlen(s));
}

/* ------------------------------------------------------------------ */
/* Validation helpers (stderr, so stdout stays a clean video stream). */
/* ------------------------------------------------------------------ */

static inline uint32_t rv_rdcycle32(void)
{
#if defined(__riscv) && !defined(IS_ON_DESKTOP_NOT_RV_EMULATOR)
    uint32_t c;
    __asm__ volatile("rdcycle %0" : "=r"(c));
    return c;
#else
    /* Host/desktop build: no RISC-V cycle counter available. */
    return 0;
#endif
}

static unsigned long frame_no = 0;
static uint32_t last_cycles = 0;
static int cfg_markers = 1;

static void log_line(const char *fmt, unsigned long a, unsigned int b)
{
    char tmp[96];
    int n = snprintf(tmp, sizeof(tmp), fmt, a, b);
    if (n > 0 && n < (int)sizeof(tmp)) {
        raw_write_fd(2, tmp, (size_t)n);
    }
    raw_write_fd(2, "\n", 1);
}

static void log_frame_start(void)
{
    frame_no++;
    if (!cfg_markers) return;
    uint32_t now = rv_rdcycle32();
    uint32_t delta = (uint32_t)(now - last_cycles); /* wrap-safe */
    last_cycles = now;
    log_line("RVC_DOOM_FRAME_BEGIN n=%lu", frame_no, 0);
    log_line("RVC_FRAME=%lu CYCLES=%u", frame_no, delta);
}

static void log_frame_end(void)
{
    if (!cfg_markers) return;
    char endtmp[96];
    int en = snprintf(endtmp, sizeof(endtmp),
                      "\r\nRVC_DOOM_FRAME_END n=%lu\r\n", frame_no);
    if (en > 0 && en < (int)sizeof(endtmp)) {
        raw_write_fd(2, endtmp, (size_t)en);
    }
}

/* ------------------------------------------------------------------ */
/* Configuration (environment)                                        */
/* ------------------------------------------------------------------ */

static int cfg_terminal = 0;              /* RVC_DOOM_TERMINAL=1 -> plain text */
static int cfg_ascii_style = 0;           /* RVC_DOOM_ASCII_MODE=A/B/C; default legacy */
static int cfg_ascii_trace = 0;           /* Marker-only stability diagnostics */
static unsigned long cfg_max_frames = 0;  /* RVC_DOOM_FRAMES=N -> exit after N */
static int cfg_env_parsed = 0;

static void parse_env(void)
{
    if (cfg_env_parsed) return;
    cfg_env_parsed = 1;
    const char *t = getenv("RVC_DOOM_TERMINAL");
    cfg_terminal = (t && *t && *t != '0') ? 1 : 0;

    const char *f = getenv("RVC_DOOM_FRAMES");
    if (f && *f) {
        cfg_max_frames = strtoul(f, NULL, 10);
    }
    const char *m = getenv("RVC_DOOM_MARKERS");
    cfg_markers = !(m && *m == '0');
    const char *trace = getenv("RVC_DOOM_ASCII_TRACE");
    cfg_ascii_trace = cfg_markers && trace && *trace == '1';
    const char *a = getenv("RVC_DOOM_ASCII_MODE");
    if (a && a[0] && !a[1]) {
        if (*a == 'A' || *a == 'a') cfg_ascii_style = 1;
        if (*a == 'B' || *a == 'b') cfg_ascii_style = 2;
        if (*a == 'C' || *a == 'c') cfg_ascii_style = 3;
    }
}/* ------------------------------------------------------------------ */
/* Input (Linux nonblocking raw termios, EOF-safe)                    */
/* ------------------------------------------------------------------ */

static int is_eofd;

static void CtrlC(int sig)
{
    (void)sig;
    exit(0);
}

static void ResetKeyboardInput(void);
static void CaptureKeyboardInput(void)
{
    /* Hook exit, because we want to re-enable keyboard. */
    atexit(ResetKeyboardInput);
    signal(SIGINT, CtrlC);

    /* stdin may be a pipe and/or already nonblocking (rvc sets O_NONBLOCK
     * on fd 0); only touch termios when stdin really is a tty. */
    if (isatty(0)) {
        struct termios term;
        if (tcgetattr(0, &term) == 0) {
            term.c_lflag &= ~(ICANON | ECHO); /* Disable echo as well */
            tcsetattr(0, TCSANOW, &term);
        }
    }

    /* Linux stdin is made nonblocking so polled reads never stall. */
    {
        int fl = fcntl(0, F_GETFL);
        if (fl >= 0) {
            fcntl(0, F_SETFL, fl | O_NONBLOCK);
        }
    }
}

static void ResetKeyboardInput(void)
{
    if (isatty(0)) {
        struct termios term;
        if (tcgetattr(0, &term) == 0) {
            term.c_lflag |= ICANON | ECHO;
            tcsetattr(0, TCSANOW, &term);
        }
    }
}

static int IsKBHit(void)
{
    if (is_eofd) {
        return 0;
    }
    int byteswaiting = 0;
    if (ioctl(0, FIONREAD, &byteswaiting) != 0) {
        byteswaiting = 0; /* not pollable (e.g. invalid fd): no input */
    }
    return byteswaiting > 0;
}

static int ReadKBByte(void)
{
    if (is_eofd) {
        return 0xffffffff;
    }
    char rxchar = 0;
    ssize_t rread;
    do {
        rread = read(fileno(stdin), (char *)&rxchar, 1);
    } while (rread < 0 && errno == EINTR);
    if (rread == 1) {
        return (unsigned char)rxchar;
    }
    if (rread == 0) {
        is_eofd = 1; /* EOF on a pipe: stop polling, keep running */
    }
    return 0xffffffff; /* EAGAIN (nonblocking) or EOF */
}

/* ------------------------------------------------------------------ */
/* Screen drawing                                                     */
/* ------------------------------------------------------------------ */

static byte lpalette[256 * 3];
static char ascii_palette[256];
static char ascii_fixed_palette[256], fixed_luma_shades[256], luma_shades[256];
static byte palette_luma[256], palette_order[256];
static uint16_t cdf_smoothed[256];
static int cdf_valid;
static uint16_t sample_x[79], sample_row[24];
static int samples_initialized;
static const char modern_shades[] = " .,:;+*o%#@";

static char ModernShade(uint32_t lum)
{
    /* ceil(255000*k*k/121): exactly floor(11*sqrt(Y/255)), capped at 10. */
    static const uint32_t thresholds[10] = {
        2108, 8430, 18967, 33720, 52686, 75868, 103265, 134877, 170703, 210744
    };
    unsigned int shade = 0;
    while (shade < 10 && lum >= thresholds[shade]) shade++;
    return modern_shades[shade];
}

void I_SetPalette(byte *palette)
{
    /* Palette changes can precede graphics initialization. */
    parse_env();
    if (!cfg_terminal) {
        memcpy(lpalette, palette, sizeof(lpalette));
        return;
    }
    if (!samples_initialized) {
        /* Same sampling positions as legacy, but no division in cell loops. */
        for (int x = 0; x < 79; x++) sample_x[x] = x * SCREENWIDTH / 79;
        for (int y = 0; y < 24; y++) sample_row[y] = (y * SCREENHEIGHT / 24) * SCREENWIDTH;
        samples_initialized = 1;
    }

    /* min(9, floor(10 * sqrt(Y / 255))), Y=.299R+.587G+.114B.
     * Squared bin boundaries give exactly that gamma without soft-float
     * sqrt on RV32IMA. Y*1000 is exact; 255000*(k/10)^2 = 2550*k*k.
     * ASCII writes 256 LUT bytes instead of copying 768 RGB bytes. */
    static const uint32_t boundaries[9] = {
        2550, 10200, 22950, 40800, 63750, 91800, 124950, 163200, 206550
    };
    static const char shades[] = " .:-=+*#%@";
    for (int i = 0; i < 256; i++) {
        uint32_t lum = 299u * palette[i * 3] +
                       587u * palette[i * 3 + 1] +
                       114u * palette[i * 3 + 2];
        if (cfg_ascii_style) {
            ascii_palette[i] = ModernShade(lum);
            if (cfg_ascii_style >= 2) {
                ascii_fixed_palette[i] = ascii_palette[i];
                palette_luma[i] = (lum + 500) / 1000;
            }
            continue;
        }
        unsigned int shade = 0;
        while (shade < 9 && lum >= boundaries[shade]) shade++;
        ascii_palette[i] = shades[shade];
    }
    if (cfg_ascii_style >= 2) {
        /* Stable counting sort by 8-bit luminance; equal luminance is grouped
         * during CDF construction, never separated by palette index. */
        uint16_t positions[256] = {0};
        for (int i = 0; i < 256; i++) positions[palette_luma[i]]++;
        unsigned int offset = 0;
        for (int y = 0; y < 256; y++) {
            unsigned int count = positions[y];
            positions[y] = offset;
            offset += count;
        }
        for (int i = 0; i < 256; i++) palette_order[positions[palette_luma[i]]++] = i;
        if (cfg_ascii_style == 3)
            for (int y = 0; y < 256; y++) fixed_luma_shades[y] = ModernShade(y * 1000u);
    }
}

static void UpdateAdaptivePlain(void)
{
    static byte previous_cell_levels[79 * 24];
    static int previous_cells_valid;
    static const byte shade_levels[128] = {
        ['.'] = 1, [','] = 2, [':'] = 3, [';'] = 4, ['+'] = 5,
        ['*'] = 6, ['o'] = 7, ['%'] = 8, ['#'] = 9, ['@'] = 10
    };
    uint16_t histogram[256] = {0};
    byte cells[79 * 24];
    unsigned int total = 79 * 24;
    int cell = 0;
    for (int y = 0; y < 24; y++) {
        const byte *row = screens[0] + sample_row[y];
        for (int x = 0; x < 79; x++) {
            byte p = row[sample_x[x]];
            if (cfg_ascii_style == 3) {
                byte q = row[sample_x[x] + 2];
                byte r = row[sample_x[x] + 4 * SCREENWIDTH];
                byte s = row[sample_x[x] + 4 * SCREENWIDTH + 2];
                /* Average luminance, never average palette indices. */
                byte lum = (palette_luma[p] + palette_luma[q] +
                            palette_luma[r] + palette_luma[s] + 2) >> 2;
                cells[cell++] = lum;
                histogram[lum]++;
            } else {
                cells[cell++] = p;
                histogram[p]++;
            }
        }
    }
    unsigned int first = 0, peak = 0, min_y = 255, max_y = 0;
    uint16_t luminance_counts[256] = {0};
    if (cfg_ascii_style == 3) {
        /* Equalize the displayed cell averages, not their four constituent
         * pixels. Otherwise a uniform black/white checkerboard average can
         * be mapped to black by a two-peak source-pixel CDF. */
        memcpy(luminance_counts, histogram, sizeof(luminance_counts));
    } else
        for (int i = 0; i < 256; i++) {
            byte p = palette_order[i];
            luminance_counts[palette_luma[p]] += histogram[p];
        }
    for (unsigned int y = 0; y < 256; y++) {
        unsigned int count = luminance_counts[y];
        if (!count) continue;
        if (!first) { first = count; min_y = y; }
        max_y = y;
        if (count > peak) peak = count;
    }
    int fallback = peak * 8 >= total * 7 || max_y - min_y <= 2;
    if (fallback) {
        cdf_valid = 0;
        previous_cells_valid = 0;
        memcpy(ascii_palette, ascii_fixed_palette, sizeof(ascii_palette));
        if (cfg_ascii_style == 3) memcpy(luma_shades, fixed_luma_shades, sizeof(luma_shades));
    } else {
        /* One division per frame. Q24 reciprocal keeps 1896 samples
         * accurate without per-bin/per-cell division or floating point.
         * Remove the first occupied bin (CDF min) so the darkest tone is 0. */
        uint32_t scale = ((1u << 24) - 1) / (total - first);
        unsigned int cumulative = 0;
        for (int y = 0; y < 256; y++) {
            cumulative += luminance_counts[y];
            unsigned int cdf = cumulative > first ? ((cumulative - first) * scale) >> 8 : 0;
            if (cdf_valid) cdf = (3u * cdf_smoothed[y] + cdf + 2) >> 2;
            cdf_smoothed[y] = cdf;
            luma_shades[y] = modern_shades[(cdf * 11u) >> 16];
        }
        cdf_valid = 1;
        if (cfg_ascii_style == 2)
            for (int i = 0; i < 256; i++) ascii_palette[i] = luma_shades[palette_luma[i]];
    }
    if (cfg_ascii_trace && cfg_ascii_style == 3) {
        uint32_t samples_hash = 2166136261u, histogram_hash = 2166136261u, palette_hash = 2166136261u;
        for (unsigned int i = 0; i < total; i++) samples_hash = (samples_hash ^ cells[i]) * 16777619u;
        for (int i = 0; i < 256; i++) histogram_hash = (histogram_hash ^ luminance_counts[i]) * 16777619u;
        for (int i = 0; i < 256; i++) palette_hash = (palette_hash ^ palette_luma[i]) * 16777619u;
        byte suspect = cells[10 * 79 + 45];
        fprintf(stderr, "RVC_ASCII_TRACE n=%lu samples=%08x histogram=%08x palette=%08x view=%d,%d,%08x size=%d cell45_10_luma=%u cdf=%u shade=%u fallback=%d\n",
                frame_no, samples_hash, histogram_hash, palette_hash, viewx, viewy, viewangle, viewwidth, suspect, cdf_smoothed[suspect],
                (unsigned char)luma_shades[suspect], fallback);
    }
    cell = 0;
    for (int y = 0; y < 24; y++) {
        for (int x = 0; x < 79; x++) {
            byte sample = cells[cell];
            char shade = cfg_ascii_style == 3 ? luma_shades[sample] : ascii_palette[sample];
            if (cfg_ascii_style == 3) {
                byte level = shade_levels[(unsigned char)shade];
                byte old = previous_cell_levels[cell];
                /* A one-level deadband absorbs near-threshold sample jitter.
                 * Large scene changes still update; fallback bypasses history. */
                int delta = (int)level - old;
                if (previous_cells_valid && delta >= -1 && delta <= 1)
                    shade = modern_shades[old];
                else if (level != old) previous_cell_levels[cell] = level;
            }
            cell++;
            out_emit(&shade, 1);
        }
        HWEMIT("\r\n");
    }
    if (cfg_ascii_style == 3) previous_cells_valid = !fallback;
}

static void UpdateTerminalANSI(void)
{
    /* 80 cols x 24 rows: SCREENWIDTH(320)/4 = 80, SCREENHEIGHT(200)/8 = 25,
     * clamped to 24 so the last line of a typical 80x24/80x25 terminal is
     * never scrolled by the trailing cursor activity. */
    const int lscreenw = SCREENWIDTH / 4;
    int lscreenh = SCREENHEIGHT / 8;
    if (lscreenh > 24) {
        lscreenh = 24;
    }

    const int rgb_bleach = 32;
    const char ascii_shademap[] = " .,:;ox%#@"; /* 10 levels */
    const int shademap_div = (256 / (sizeof(ascii_shademap) - 1));

    static int last_bg = -1;
    static int last_fg = -1;
    last_bg = last_fg = -1; /* color state resets every frame */

    char ctssgr[16];
    int x, y;

    for (y = 0; y < lscreenh; y++) {
        int ly = y * SCREENHEIGHT / lscreenh;
        char ctsline[32];
        snprintf(ctsline, sizeof(ctsline), "\x1b[%d;1H", y + 1);
        HWEMIT(ctsline);

        for (x = 0; x < lscreenw; x++) {
            int lx = x * SCREENWIDTH / lscreenw;
            int col = screens[0][lx + ly * SCREENWIDTH];
            int r = lpalette[col * 3 + 0] + rgb_bleach;
            int g = lpalette[col * 3 + 1] + rgb_bleach;
            int b = lpalette[col * 3 + 2] + rgb_bleach;

            /* RGB8 bit-plane scheme (upstream): SGR 40-47 from bit7,
             * SGR 30-37 from bit6. */
            int selbg = (!!(r & 128)) | (!!(g & 128)) * 2 | (!!(b & 128)) * 4;
            int selfg = (!!(r & 64)) | (!!(g & 64)) * 2 | (!!(b & 64)) * 4;

            if (selbg != last_bg) {
                snprintf(ctssgr, sizeof(ctssgr), "\x1b[4%dm", selbg);
                HWEMIT(ctssgr);
                last_bg = selbg;
            }
            if (selfg != last_fg) {
                snprintf(ctssgr, sizeof(ctssgr), "\x1b[3%dm", selfg);
                HWEMIT(ctssgr);
                last_fg = selfg;
            }

            int lum = (r + g + b) / 3;
            int si = lum / shademap_div;
            if (si >= (int)(sizeof(ascii_shademap) - 1)) {
                si = (int)(sizeof(ascii_shademap) - 2);
            }
            char sh = ascii_shademap[si];
            out_emit(&sh, 1);
        }
    }
}

static void UpdateTerminalPlain(void)
{
    if (cfg_ascii_style >= 2) { UpdateAdaptivePlain(); return; }
    /* RVC_DOOM_TERMINAL=1: no ANSI at all. 79 chars x 24 rows with CRLF
     * ends; the terminal scrolls instead of using cursor addressing. */
    const int lscreenw = 79;
    int lscreenh = SCREENHEIGHT / 8;
    if (lscreenh > 24) {
        lscreenh = 24;
    }

    int x, y;
    for (y = 0; y < lscreenh; y++) {
        for (x = 0; x < lscreenw; x++) {
            int col = screens[0][sample_x[x] + sample_row[y]];
            char sh = ascii_palette[col];
            out_emit(&sh, 1);
        }
        HWEMIT("\r\n");
    }
}/* ------------------------------------------------------------------ */
/* DOOM interface                                                     */
/* ------------------------------------------------------------------ */

void I_UpdateNoBlit(void)
{
    /* what is this? */
}

void I_InitGraphics(void)
{
    parse_env();
    CaptureKeyboardInput();
}

byte downmap[256];

void I_StartTic(void)
{
    event_t event;

    if (IsKBHit()) {
        event.type = ev_keydown;
        int hit = ReadKBByte();
        switch (hit) {
        case 10: hit = KEY_ENTER; break;
        case 'A': case 'a': hit = KEY_LEFTARROW; break;
        case 'S': case 's': hit = KEY_DOWNARROW; break;
        case 'D': case 'd': hit = KEY_RIGHTARROW; break;
        case 'W': case 'w': hit = KEY_UPARROW; break;
        case '/': hit = KEY_RCTRL; break;
        }
        event.data1 = hit;
        D_PostEvent(&event);
        downmap[(byte)event.data1] = 20;
    }

    /* Upstream keyup handling: 20 ticks after keydown, post the keyup. */
    int i;
    for (i = 0; i < 256; i++) {
        if (downmap[i]) {
            if (--downmap[i] == 0) {
                event.type = ev_keyup;
                event.data1 = i;
                D_PostEvent(&event);
            }
        }
    }
}

void I_ReadScreen(byte *scr)
{
    memcpy(scr, screens[0], SCREENWIDTH * SCREENHEIGHT);
}

void I_StartFrame(void)
{
}

void I_ShutdownGraphics(void)
{
    exit(0);
}

void I_FinishUpdate(void)
{
    log_frame_start();

    if (cfg_terminal) {
        UpdateTerminalPlain();
    } else {
        UpdateTerminalANSI();
    }

    out_flush();

    log_frame_end();
    if (cfg_max_frames && frame_no >= cfg_max_frames) {
        exit(0);
    }
}
