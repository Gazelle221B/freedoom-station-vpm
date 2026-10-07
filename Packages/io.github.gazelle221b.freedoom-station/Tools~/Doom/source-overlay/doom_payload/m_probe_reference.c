/* Independent 64-bit C reference for DoomMulhProbe. No rvc core modifications.
 * cc -std=c99 -O2 m_probe_reference.c -o m_probe_reference
 * ./m_probe_reference > m-reference.bin
 * File: LE uint32 magic,count; count records a,b,MULH,MULHSU,MULHU,MUL,
 * DIV,DIVU,REM,REMU. Generated records contain no WAD and stay in build/.
 */
#include <stdint.h>
#include <stdio.h>

static void word(uint32_t value) {
    for (unsigned i=0; i<4; i++) putchar((value >> (8*i)) & 255u);
}
static void record(uint32_t a,uint32_t b) {
    int64_t sa=(a & 0x80000000u)?(int64_t)a-4294967296LL:a;
    int64_t sb=(b & 0x80000000u)?(int64_t)b-4294967296LL:b;
    /* Convert signed products to uint64_t before shifting: fully defined C. */
    word(a); word(b);
    word((uint32_t)((uint64_t)(sa*sb)>>32));
    word((uint32_t)((uint64_t)(sa*(int64_t)b)>>32));
    word((uint32_t)(((uint64_t)a*b)>>32));
    word((uint32_t)((uint64_t)a*b));
    word(b==0?UINT32_MAX:(uint32_t)(sa/sb));
    word(b==0?UINT32_MAX:a/b);
    word(b==0?a:(uint32_t)(sa%sb));
    word(b==0?a:a%b);
}
static uint32_t next(uint32_t *state) {
    *state^=*state<<13; *state^=*state>>17; *state^=*state<<5;
    return *state;
}
int main(void) {
    const uint32_t boundary[]={0,1,UINT32_MAX,0x80000000,0x7fffffff,0xffff,
        0x10000,0xfffe,0x10001,0xffff0000,0x80000001,0x7ffffffe,2,0xfffffffe,
        0x55555555,0xaaaaaaaa,0xff,0x100,0x7fff,0x8000};
    unsigned size=sizeof boundary/sizeof *boundary;
    word(0x5256434d); word(size*size+10000);
    for(unsigned i=0;i<size;i++) for(unsigned j=0;j<size;j++) record(boundary[i],boundary[j]);
    uint32_t state=0x5256434d;
    for(unsigned i=0;i<10000;i++) {
        uint32_t a=next(&state),b=next(&state); record(a,b);
    }
    return ferror(stdout)?1:0;
}
