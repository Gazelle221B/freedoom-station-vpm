/* Host-only measurement wrapper. All CPU/MMU/UART execution uses stock src/. */
#include <stdarg.h>
#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>

static uint64_t probe_steps, probe_ram_bytes;
static uint64_t frame_steps, frame_bytes;
static unsigned frame_number;
static char line[256];
static unsigned line_length;
static void dump_frame(unsigned number);

static void observe_char(unsigned char c)
{
    if (c == '\r') return;
    if (c == '\n') {
        line[line_length] = 0;
        unsigned n;
        if (sscanf(line, "RVC_DOOM_FRAME_BEGIN n=%u", &n) == 1) {
            if (frame_number)
                fprintf(stderr, "RVC_PROBE_FRAME n=%u steps=%llu ram_write_bytes=%llu\n",
                        frame_number, (unsigned long long)(probe_steps - frame_steps),
                        (unsigned long long)(probe_ram_bytes - frame_bytes));
            frame_number = n;
            frame_steps = probe_steps;
            frame_bytes = probe_ram_bytes;
        }
        if (sscanf(line, "RVC_DOOM_FRAME_END n=%u", &n) == 1)
            dump_frame(n);
        line_length = 0;
        return;
    }
    if (line_length < sizeof(line) - 1) line[line_length++] = c;
}

static int probe_printf(const char *format, ...)
{
    va_list args;
    va_start(args, format);
    va_list copy;
    va_copy(copy, args);
    int length = vsnprintf(NULL, 0, format, copy);
    va_end(copy);
    if (length < 0) { va_end(args); return length; }
    char *buffer = malloc((size_t)length + 1);
    if (!buffer) { va_end(args); return -1; }
    vsnprintf(buffer, (size_t)length + 1, format, args);
    va_end(args);
    fwrite(buffer, 1, length, stdout);
    for (int i = 0; i < length; i++) observe_char((unsigned char)buffer[i]);
    free(buffer);
    return length;
}

#define printf probe_printf
#define mem_set_byte rvc_mem_set_byte
#define mem_set_half_word rvc_mem_set_half_word
#define mem_set_word rvc_mem_set_word
#include "../src/mem.h"
#undef mem_set_byte
#undef mem_set_half_word
#undef mem_set_word

static void count_ram(uint addr, unsigned bytes)
{
    if (addr >= 0x80000000u && addr < 0x88000000u)
        probe_ram_bytes += bytes;
}
void mem_set_byte(cpu_t *cpu, uint addr, uint value)
{ count_ram(addr, 1); rvc_mem_set_byte(cpu, addr, value); }
void mem_set_half_word(cpu_t *cpu, uint addr, uint value)
{ count_ram(addr, 2); rvc_mem_set_half_word(cpu, addr, value); }
void mem_set_word(cpu_t *cpu, uint addr, uint value)
{ count_ram(addr, 4); rvc_mem_set_word(cpu, addr, value); }

#define cpu_tick rvc_cpu_tick
#include "../src/cpu.h"
#undef cpu_tick
void cpu_tick(cpu_t *cpu)
{ probe_steps++; rvc_cpu_tick(cpu); }

#include "../src/main.c"

static void dump_frame(unsigned number)
{
    const char *prefix = getenv("RVC_DOOM_FRAMEBUFFER");
    if (!prefix || !*prefix) return;
    char path[512];
    if (snprintf(path, sizeof(path), "%s-%u.ppm", prefix, number) >= (int)sizeof(path))
        return;
    FILE *output = fopen(path, "wb");
    if (!output) { perror("framebuffer PPM"); return; }
    fprintf(output, "P6\n320 200\n255\n");
    for (unsigned i = 0; i < 320 * 200; i++) {
        uint32_t pixel;
        memcpy(&pixel, cpu.mem + 0x06000000 + i * 4, 4);
        unsigned char rgb[] = {pixel >> 16, pixel >> 8, pixel};
        fwrite(rgb, 1, 3, output);
    }
    fclose(output);
}
