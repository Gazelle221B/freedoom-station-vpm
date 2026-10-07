/*
 * test_video_console.c - host harness for video_console.c ASCII-LUT tests.
 *
 * Includes the real driver textually (VC_SOURCE) so the test TU reaches its
 * file-scope statics (ascii_palette, cfg_*, parse_env, and - with
 * VC_HAS_ASCII_STATICS - cfg_ascii_style, palette_luma, palette_order,
 * cdf_smoothed, cdf_valid, luma_shades, fixed_luma_shades) and the public
 * I_* entry points. Engine headers come from -I<variant root> (parent of the
 * embeddeddoom checkout), the same layout the payload build uses.
 * -ffunction-sections/-fdata-sections/--gc-sections drop the unused input
 * machinery. Screens and optional renderer-view diagnostics are stubbed below.
 *
 * Modes:
 *   lut                stdin 768*N palette bytes -> stdout 256*N LUT bytes
 *   envprobe [change]  stdin 768 palette bytes; I_SetPalette(); with "change",
 *                      flip RVC_DOOM_TERMINAL/MARKERS via setenv() first, then
 *                      print "terminal=<d> markers=<d>".
 *   asciiprobe         stdin 768 palette bytes; I_SetPalette(); print
 *                      "ascii_style=<d>" for the parsed RVC_DOOM_ASCII_MODE.
 *   frames <o1> [<o2>] stdin 768|1536 palette bytes; fill screens[0] with
 *                      pattern[i]=i&0xFF; per palette I_SetPalette() then
 *                      I_FinishUpdate() with fd1 dup2'd to <oN>.
 *   rawframe <o1> [<o2>]  stdin 768|1536 palette bytes then 64000 bytes of
 *                      synthetic frame per palette; screens[0]=frame then
 *                      I_FinishUpdate() with fd1 dup2'd to <oN>.
 *   snap <osnap> <o1> ... [<o64>] stdin 768 palette bytes then 64000*nframes
 *                      frame bytes (nframes = 1..64). Runs I_SetPalette() then
 *                      one I_FinishUpdate() per frame. Appends tagged state
 *                      records to <osnap>: 'L'+palette_luma[256],
 *                      'O'+palette_order[256] once, then per frame
 *                      'V'+cdf_valid(1 byte), 'D'+cdf_smoothed[256] uint16 LE,
 *                      'S'+luma_shades[256]. Frame rendering goes to <oN>.
 *                      Requires VC_HAS_ASCII_STATICS (the new driver statics).
 *
 * Exit: 0 ok, 2 usage/IO error, 1 internal.
 */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <fcntl.h>
#include <errno.h>
#include <stdint.h>

#ifndef VC_SOURCE
#define VC_SOURCE "video_console.c"
#endif
#include VC_SOURCE

/* Linked frame paths reference screens[0] (v_video.h: extern byte* screens[5]). */
byte *screens[5] = { 0, 0, 0, 0, 0 };
/* Optional real-driver view diagnostics have no engine in this harness. */
fixed_t viewx, viewy;
angle_t viewangle;
int viewwidth = 320;

static int read_all(int fd, unsigned char *buf, size_t want)
{
    size_t got = 0;
    while (got < want) {
        ssize_t n = read(fd, buf + got, want - got);
        if (n > 0) {
            got += (size_t)n;
        } else if (n == 0) {
            break;
        } else if (n < 0) {
            return -1;
        }
    }
    return (int)got;
}

#ifdef VC_HAS_LUT_STATICS
/* Modes touching LUT-era statics (ascii_palette, cfg_*); compiled
 * out when targeting the pre-LUT reference driver, which lacks them. */
static int mode_lut(void)
{
    unsigned char pal[768];
    for (;;) {
        int got = read_all(0, pal, sizeof(pal));
        if (got == 0) {
            break;
        }
        if (got != 768) {
            fprintf(stderr, "lut: expected 768-byte palette chunks, got %d\n", got);
            return 2;
        }
        I_SetPalette(pal);
        if (fwrite(ascii_palette, 1, 256, stdout) != 256) {
            return 1;
        }
    }
    fflush(stdout);
    return 0;
}

static int mode_envprobe(int change)
{
    unsigned char pal[768];
    if (read_all(0, pal, sizeof(pal)) != 768) {
        fprintf(stderr, "envprobe: expected 768 bytes of palette\n");
        return 2;
    }
    I_SetPalette(pal); /* palette may be installed before graphics init */
    if (change) {
        setenv("RVC_DOOM_TERMINAL", "0", 1);
        setenv("RVC_DOOM_MARKERS", "1", 1);
    }
    printf("terminal=%d markers=%d\n", cfg_terminal, cfg_markers);
    return 0;
}

#ifdef VC_HAS_ASCII_STATICS
static int mode_asciiprobe(void)
{
    unsigned char pal[768];
    if (read_all(0, pal, sizeof(pal)) != 768) {
        fprintf(stderr, "asciiprobe: expected 768 bytes of palette\n");
        return 2;
    }
    I_SetPalette(pal);
    printf("ascii_style=%d\n", cfg_ascii_style);
    return 0;
}
#endif /* VC_HAS_ASCII_STATICS */
#endif /* VC_HAS_LUT_STATICS */
static int mode_frames(int argc, char **argv)
{
    unsigned char pal[2 * 768];
    int total = read_all(0, pal, sizeof(pal));
    if (total <= 0 || total % 768 != 0) {
        fprintf(stderr, "frames: expected 768 or 1536 bytes of palette(s), got %d\n", total);
        return 2;
    }
    int npal = total / 768;
    if (argc < 2 + npal) {
        fprintf(stderr, "frames: %d palette(s) need %d output arg(s)\n", npal, npal);
        return 2;
    }
    byte *fb = malloc((size_t)SCREENWIDTH * (size_t)SCREENHEIGHT);
    if (!fb) {
        fprintf(stderr, "frames: out of memory\n");
        return 1;
    }
    for (int i = 0; i < SCREENWIDTH * SCREENHEIGHT; i++) {
        fb[i] = (byte)(i & 0xFF);
    }
    screens[0] = fb;
    for (int j = 0; j < npal; j++) {
        int fd = open(argv[2 + j], O_WRONLY | O_CREAT | O_TRUNC, 0644);
        if (fd < 0 || dup2(fd, 1) < 0) {
            fprintf(stderr, "frames: cannot open output %s\n", argv[2 + j]);
            return 2;
        }
        close(fd);
        I_SetPalette(pal + j * 768);
        I_FinishUpdate();
    }
    return 0;
}

/* Synthetic full-size frames: stdin palette bytes, then one SCREENWIDTH*
 * SCREENHEIGHT byte frame per palette. Only public I_* entry points, so it
 * also compiles against the pinned reference drivers. */
static int mode_rawframe(int argc, char **argv)
{
    /* npal = output file count (1..2). Layout: exactly npal*768 palette
     * bytes, then one SCREENWIDTH*SCREENHEIGHT frame per palette. Reading
     * exactly npal*768 palette bytes first keeps frame pixels from being
     * misread as a second palette when only one palette is supplied. */
    int npal = argc - 2;
    if (npal < 1 || npal > 2) {
        fprintf(stderr, "rawframe: need 1 or 2 output file(s)\n");
        return 2;
    }
    unsigned char pal[2 * 768];
    if (read_all(0, pal, (size_t)npal * 768) != npal * 768) {
        fprintf(stderr, "rawframe: expected %d palette bytes\n", npal * 768);
        return 2;
    }
    byte *fb = malloc((size_t)SCREENWIDTH * (size_t)SCREENHEIGHT);
    if (!fb) {
        fprintf(stderr, "rawframe: out of memory\n");
        return 1;
    }
    for (int j = 0; j < npal; j++) {
        if (read_all(0, fb, (size_t)SCREENWIDTH * SCREENHEIGHT) !=
            (int)(SCREENWIDTH * SCREENHEIGHT)) {
            fprintf(stderr, "rawframe: short frame %d\n", j);
            return 2;
        }
        int fd = open(argv[2 + j], O_WRONLY | O_CREAT | O_TRUNC, 0644);
        if (fd < 0 || dup2(fd, 1) < 0) {
            fprintf(stderr, "rawframe: cannot open output %s\n", argv[2 + j]);
            return 2;
        }
        close(fd);
        screens[0] = fb;
        I_SetPalette(pal + j * 768);
        I_FinishUpdate();
    }
    return 0;
}

#ifdef VC_HAS_ASCII_STATICS
static int write_some(int fd, const void *buf, size_t n)
{
    const unsigned char *p = (const unsigned char *)buf;
    while (n) {
        ssize_t w = write(fd, p, n);
        if (w > 0) {
            p += w;
            n -= (size_t)w;
        } else if (w < 0 && errno == EINTR) {
            continue;
        } else {
            return -1;
        }
    }
    return 0;
}

static void snap_tag(int fd, char tag, const void *data, size_t n)
{
    write_some(fd, &tag, 1);
    write_some(fd, data, n);
}

static int mode_snap(int argc, char **argv)
{
    if (argc < 4 || argc > 67) {
        fprintf(stderr, "snap: usage snap <osnap> <o1> ... [<o64>]\n");
        return 2;
    }
    unsigned char pal[768];
    if (read_all(0, pal, sizeof(pal)) != 768) {
        fprintf(stderr, "snap: expected 768 bytes of palette\n");
        return 2;
    }
    int nframes = argc - 3;
    byte *fb = malloc((size_t)SCREENWIDTH * SCREENHEIGHT * 2u);
    if (!fb) {
        fprintf(stderr, "snap: out of memory\n");
        return 1;
    }
    int sfd = open(argv[2], O_WRONLY | O_CREAT | O_TRUNC, 0644);
    if (sfd < 0) {
        fprintf(stderr, "snap: cannot open snapshot %s\n", argv[2]);
        return 2;
    }
    I_SetPalette(pal);
    snap_tag(sfd, 'L', palette_luma, sizeof(palette_luma));
    snap_tag(sfd, 'O', palette_order, sizeof(palette_order));
    for (int j = 0; j < nframes; j++) {
        if (read_all(0, fb, (size_t)SCREENWIDTH * SCREENHEIGHT) !=
            (int)(SCREENWIDTH * SCREENHEIGHT)) {
            fprintf(stderr, "snap: short frame %d\n", j);
            return 2;
        }
        int fd = open(argv[3 + j], O_WRONLY | O_CREAT | O_TRUNC, 0644);
        if (fd < 0 || dup2(fd, 1) < 0) {
            fprintf(stderr, "snap: cannot open output %s\n", argv[3 + j]);
            return 2;
        }
        close(fd);
        screens[0] = fb;
        I_FinishUpdate();
        unsigned char v = (unsigned char)(cdf_valid ? 1 : 0);
        snap_tag(sfd, 'V', &v, 1);
        unsigned char le[512];
        for (int i = 0; i < 256; i++) {
            le[2 * i] = (unsigned char)(cdf_smoothed[i] & 0xFF);
            le[2 * i + 1] = (unsigned char)((cdf_smoothed[i] >> 8) & 0xFF);
        }
        snap_tag(sfd, 'D', le, sizeof(le));
        snap_tag(sfd, 'S', luma_shades, sizeof(luma_shades));
    }
    close(sfd);
    return 0;
}
#endif /* VC_HAS_ASCII_STATICS */

int main(int argc, char **argv)
{
    if (argc < 2) {
        fprintf(stderr,
                "usage: %s lut|envprobe [change]|asciiprobe|frames <o1> [<o2>]|"
                "rawframe <o1> [<o2>]|snap <osnap> <o1> [<o2>]\n", argv[0]);
        return 2;
    }
#ifdef VC_HAS_LUT_STATICS
    if (strcmp(argv[1], "lut") == 0) {
        return mode_lut();
    }
    if (strcmp(argv[1], "envprobe") == 0) {
        return mode_envprobe(argc > 2 && strcmp(argv[2], "change") == 0);
    }
#ifdef VC_HAS_ASCII_STATICS
    if (strcmp(argv[1], "asciiprobe") == 0) {
        return mode_asciiprobe();
    }
#endif
#endif
    if (strcmp(argv[1], "frames") == 0) {
        return mode_frames(argc, argv);
    }
    if (strcmp(argv[1], "rawframe") == 0) {
        return mode_rawframe(argc, argv);
    }
#ifdef VC_HAS_ASCII_STATICS
    if (strcmp(argv[1], "snap") == 0) {
        return mode_snap(argc, argv);
    }
#endif
    fprintf(stderr, "unknown mode: %s\n", argv[1]);
    return 2;
}
