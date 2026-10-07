/*
 * syscalls.c - Bare-metal newlib syscall stubs for Doomgeneric
 *
 * Replaces the ecall-based stubs.h from the rvcore (emulated) variant.
 * All syscalls are implemented directly against bare-metal hardware.
 *
 * File I/O:  read-only WAD data blob (linked via objcopy)
 * Console:   16550 UART at 0x10000000 / 0x10000005 (byte-addressed)
 * Memory:    _sbrk bounded by _heap_start .. _heap_end link symbols
 * Time:      deterministic software counter
 * Exit:      ecall 93 (the sole permitted host syscall)
 */

#include <sys/stat.h>
#include <sys/time.h>
#include <sys/types.h>
#include <errno.h>
#include <stddef.h>
#include <stdio.h>
#include <stdint.h>
#include <string.h>
#include <unistd.h>

/* ===================================================================
 * UART (byte-addressed 16550 at 0x10000000)
 * =================================================================== */

static volatile uint8_t *const uart_thr = (volatile uint8_t *)0x10000000;
static volatile uint8_t *const uart_lsr = (volatile uint8_t *)0x10000005;

static void uart_putc(char c)
{
    while (!(*uart_lsr & 0x20))  /* wait for THR empty (bit 5) */
        ;
    *uart_thr = (uint8_t)c;
}

/* ===================================================================
 * WAD data (objcopy --add-section .wad=doom1.wad)
 * =================================================================== */

extern const uint8_t _binary_wad_start[];
extern const uint8_t _binary_wad_end[];

#define WAD_FD 3

static off_t           wad_cursor = 0;

/* ===================================================================
 * _open
 *
 * Only "doom1.wad" (with optional leading '/') is accepted.
 * Returns fd 3.  All other paths fail with ENOENT.
 * =================================================================== */

int _open(const char *path, int flags, mode_t mode)
{
    (void)flags;
    (void)mode;

    if (path[0] == '/')
        path++;

    if (strcmp(path, "doom1.wad") == 0) {
        wad_cursor = 0;
        return WAD_FD;
    }

    errno = ENOENT;
    return -1;
}

/* ===================================================================
 * _read
 *
 * Read from the WAD blob.  Only fd 3 is valid.
 * =================================================================== */

ssize_t _read(int fd, void *buf, size_t count)
{
    if (fd != WAD_FD) {
        errno = EBADF;
        return -1;
    }

    size_t wad_len = (size_t)(_binary_wad_end - _binary_wad_start);

    if ((size_t)wad_cursor >= wad_len)
        return 0; /* EOF */

    size_t remaining = wad_len - (size_t)wad_cursor;
    size_t to_copy   = (count < remaining) ? count : remaining;

    const uint8_t *src = _binary_wad_start + wad_cursor;
    uint8_t       *dst = (uint8_t *)buf;
    for (size_t i = 0; i < to_copy; i++)
        dst[i] = src[i];

    wad_cursor += (off_t)to_copy;
    return (ssize_t)to_copy;
}

/* ===================================================================
 * _write
 *
 * stdout (1) and stderr (2) go to UART.  Other fds fail.
 * =================================================================== */

ssize_t _write(int fd, const void *buf, size_t count)
{
    if (fd != 1 && fd != 2) {
        errno = EBADF;
        return -1;
    }

    const char *p = (const char *)buf;
    for (size_t i = 0; i < count; i++)
        uart_putc(p[i]);

    return (ssize_t)count;
}

/* ===================================================================
 * _lseek
 *
 * Seek within the WAD blob.
 * =================================================================== */

off_t _lseek(int fd, off_t offset, int whence)
{
    if (fd != WAD_FD) {
        errno = EBADF;
        return -1;
    }

    size_t wad_len = (size_t)(_binary_wad_end - _binary_wad_start);
    off_t  new_pos;

    switch (whence) {
    case SEEK_SET: new_pos  = offset;                 break;
    case SEEK_CUR: new_pos  = wad_cursor + offset;    break;
    case SEEK_END: new_pos  = (off_t)wad_len + offset; break;
    default:       errno    = EINVAL; return -1;
    }

    if (new_pos < 0) { errno = EINVAL; return -1; }

    wad_cursor = new_pos;
    return new_pos;
}

/* ===================================================================
 * _close  (no-op)
 * =================================================================== */

int _close(int fd)
{
    (void)fd;
    return 0;
}

/* ===================================================================
 * _fstat / _stat
 *
 * Minimal stub: regular file with size taken from the WAD blob bounds.
 * Doom calls stat() on WAD search paths; returning success keeps it
 * from aborting early.
 * =================================================================== */

int _fstat(int fd, struct stat *st)
{
    if (fd != WAD_FD) { errno = EBADF; return -1; }
    memset(st, 0, sizeof(*st));
    st->st_mode = S_IFREG;
    st->st_size = (off_t)(_binary_wad_end - _binary_wad_start);
    return 0;
}

int _stat(const char *path, struct stat *st)
{
    if (strcmp(path, "doom1.wad") && strcmp(path, "/doom1.wad")) {
        errno = ENOENT;
        return -1;
    }
    memset(st, 0, sizeof(*st));
    st->st_mode = S_IFREG;
    st->st_size = (off_t)(_binary_wad_end - _binary_wad_start);
    return 0;
}

/* ===================================================================
 * _sbrk
 *
 * Bounded heap between _heap_start and _heap_end (linker symbols).
 * =================================================================== */

extern char _heap_start[];
extern char _heap_end[];

static char *heap_cur = NULL;

void *_sbrk(intptr_t increment)
{
    if (heap_cur == NULL)
        heap_cur = _heap_start;

    char *prev = heap_cur;

    if (increment == 0)
        return (void *)prev;

    char *next = prev + increment;

    if (next > _heap_end || next < _heap_start) {
        errno = ENOMEM;
        return (void *)-1;
    }

    heap_cur = next;
    return (void *)prev;
}

/* ===================================================================
 * _gettimeofday
 *
 * Deterministic software clock.  Each call advances by one "tick";
 * with singletics the absolute value does not matter, only that it
 * increases monotonically.
 * =================================================================== */

static uint32_t fake_time = 1000000000;

int _gettimeofday(struct timeval *tv, struct timezone *tz)
{
    (void)tz;

    fake_time++;

    tv->tv_sec  = (time_t)(fake_time / 35);
    tv->tv_usec = (suseconds_t)((fake_time % 35) * 28571);

    return 0;
}

/* ===================================================================
 * _exit
 *
 * ecall 93: the sole permitted host syscall in this payload.
 * =================================================================== */

void _exit(int status)
{
    register int a0 __asm__("a0") = status;
    register int a7 __asm__("a7") = 93;
    __asm__ __volatile__("ecall" : : "r"(a0), "r"(a7) : "memory");
    __builtin_unreachable();
}

/* ===================================================================
 * Miscellaneous stubs required by newlib
 * =================================================================== */

int    _getpid(void)                            { return 1; }
int    _isatty(int fd)                          { (void)fd; return 0; }
int    _kill(pid_t pid, int sig)                { (void)pid; (void)sig; errno = EINVAL; return -1; }
int    _link(const char *o, const char *n)      { (void)o; (void)n; errno = EMLINK; return -1; }
int    _unlink(const char *p)                   { (void)p; errno = ENOENT; return -1; }
int    mkdir(const char *p, mode_t m)           { (void)p; (void)m; errno = EACCES; return -1; }
int    rmdir(const char *p)                     { (void)p; errno = ENOENT; return -1; }
int    usleep(useconds_t usec)                   { (void)usec; return 0; }
