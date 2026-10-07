/*
 * adapter.c - Bare-metal Doomgeneric adapter for RISC-V rv32ima
 *
 * Provides the DG_* platform layer for doomgeneric on bare-metal.
 * Replaces doomgeneric.c from the upstream doomgeneric distribution
 * (which hardcodes the rvcore-emulator VRAM_START memory layout).
 *
 * Memory layout:
 *   RAM:         0x80000000 - 0x87FFFFFF (128 MiB)
 *   Code/Data:   starts at 0x80000000
 *   Heap:        _heap_start .. 0x86000000
 *   Framebuffer: 0x86000000  (320x200x4, volatile target for DG_DrawFrame)
 *   Stack:       0x87F00000 (grows down)
 *   UART:        0x10000000 (16550, byte-addressed registers)
 *     THR offset 0, LSR offset 5 (bit 5 = 0x20 = THR empty)
 *
 * Frame protocol (UART markers for host harness / probe):
 *   RVC_DOOM_FRAME_BEGIN n=N    before framebuffer copy
 *   RVC_DOOM_FRAME_END   n=N    after framebuffer copy
 *   After RVC_DOOM_BARE_FRAMES presentations, exit via ecall 93.
 *
 * Compile with:
 *   -march=rv32ima -mabi=ilp32 -nostartfiles -ffreestanding -O2
 *   -DDOOMGENERIC_RESX=320 -DDOOMGENERIC_RESY=200
 */

#include "doomgeneric.h"
#include "m_argv.h"

#include <stddef.h>
#include <stdint.h>
#include <stdlib.h>

/* ---- Forward declarations from doomgeneric engine ---- */
extern void D_DoomMain(void);

/* ---- DG_ScreenBuffer: extern in doomgeneric.h, defined here ---- */
pixel_t *DG_ScreenBuffer = NULL;

/* ---- UART (byte-addressed 16550 at 0x10000000) ---- */
static volatile uint8_t *const uart_thr = (volatile uint8_t *)0x10000000;
static volatile uint8_t *const uart_lsr = (volatile uint8_t *)0x10000005;

static void uart_putc(char c)
{
    while (!(*uart_lsr & 0x20))  /* wait for THR empty */
        ;
    *uart_thr = (uint8_t)c;
}

static void uart_puts(const char *s)
{
    while (*s)
        uart_putc(*s++);
}

static void uart_put_dec32(uint32_t v)
{
    char buf[12];
    int pos = 11;
    buf[11] = '\0';
    if (v == 0) {
        buf[--pos] = '0';
    } else {
        while (v > 0 && pos > 0) {
            buf[--pos] = '0' + (char)(v % 10);
            v /= 10;
        }
    }
    uart_puts(&buf[pos]);
}

/* ---- Framebuffer ---- */
#define FB_ADDR   0x86000000
#define FB_WIDTH  320
#define FB_HEIGHT 200

static volatile pixel_t *const framebuffer = (volatile pixel_t *)FB_ADDR;

/* ---- WAD data (linked in via objcopy) ---- */
extern const uint8_t _binary_wad_start[];
extern const uint8_t _binary_wad_end[];
static size_t wad_size = 0;

/* ---- Frame / exit control ---- */
static unsigned frame_count  = 1;
#ifndef RVC_DOOM_BARE_FRAMES
#define RVC_DOOM_BARE_FRAMES 90
#endif
static unsigned max_frames   = RVC_DOOM_BARE_FRAMES;

/* ===================================================================
 * DG_* platform implementations (doomgeneric.h contract)
 * =================================================================== */

void DG_Init(void)
{
    wad_size = (size_t)(_binary_wad_end - _binary_wad_start);
}

void DG_DrawFrame(void)
{
    /* --- BEGIN marker --- */
    uart_puts("RVC_DOOM_FRAME_BEGIN n=");
    uart_put_dec32(frame_count);
    uart_puts("\r\n");

    /* Copy DG_ScreenBuffer to physical framebuffer */
    if (DG_ScreenBuffer != NULL) {
        const pixel_t *src = DG_ScreenBuffer;
        volatile pixel_t *dst = framebuffer;
        size_t count = (size_t)FB_WIDTH * (size_t)FB_HEIGHT;
        for (size_t i = 0; i < count; i++)
            dst[i] = src[i];
    }

    /* --- END marker --- */
    uart_puts("RVC_DOOM_FRAME_END n=");
    uart_put_dec32(frame_count);
    uart_puts("\r\n");

    if (frame_count >= max_frames) {
        /* Exit via ecall 93 (the one permitted host syscall) */
        __asm__ __volatile__(
            "li a7, 93\n\t"
            "li a0, 0\n\t"
            "ecall"
        );
    }
    frame_count++;
}

void DG_SleepMs(uint32_t ms)
{
    (void)ms;
}

uint32_t DG_GetTicksMs(void)
{
    /* Deterministic: each call advances by one "tick".  With
       singletics the exact value does not matter as long as it
       increases monotonically. */
    static uint32_t ticks = 0;
    return ticks++;
}

int DG_GetKey(int *pressed, unsigned char *doomKey)
{
    (void)pressed;
    (void)doomKey;
    return 0;
}

void DG_SetWindowTitle(const char *title)
{
    (void)title;
}

/* ===================================================================
 * Replacement doomgeneric_Create
 *
 * The upstream doomgeneric.c writes DG_ScreenBuffer = VRAM_START,
 * which is rvcore-emulator-specific.  This version allocates a
 * 320x200 x 4-byte buffer from the heap.
 * =================================================================== */

extern void M_FindResponseFile(void);

void doomgeneric_Create(int argc, char **argv)
{
    myargc = argc;
    myargv = argv;

    M_FindResponseFile();

    DG_ScreenBuffer = (pixel_t *)malloc(
        (size_t)FB_WIDTH * (size_t)FB_HEIGHT * sizeof(pixel_t));
    if (DG_ScreenBuffer == NULL) {
        uart_puts("FATAL: screen buffer alloc failed\r\n");
        __asm__ __volatile__("li a7, 93\n\tli a0, 1\n\tecall");
    }

    DG_Init();
    D_DoomMain();
}

/* ===================================================================
 * main() - C entry point (called from start.S)
 * =================================================================== */

int main(void)
{
    static char arg0[] = "doom";
    static char arg1[] = "-iwad";
    static char arg2[] = "doom1.wad";
    static char arg3[] = "-warp";
    static char arg4[] = "1";
    static char arg5[] = "1";
    static char arg6[] = "-singletics";
    static char arg7[] = "-nosound";

    static char *argv[] = {
        arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, NULL
    };
    int argc = 8;

    doomgeneric_Create(argc, argv);

    /* Main loop: doomgeneric_Tick drives the game engine */
    for (;;) {
        doomgeneric_Tick();
    }

    return 0;
}
