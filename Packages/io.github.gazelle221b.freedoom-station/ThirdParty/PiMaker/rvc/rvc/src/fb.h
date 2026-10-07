#ifndef FB_H
#define FB_H



#define RAM_ADDR(lin) uint2(lin % 2048, 64 + (lin / 2048))
#define RAM_MAX (2048 * (4096 - 64) * 4 * 4)


/* shift by two to ignore byte offset */
#define RAM_L1_ARRAY_IDX(a) (((a >> 2) & 127) | (((a >> 11) & 0x3) << 7))


#define BUFFER_MAX 63
uint getbuf(uart_buffer buffer, uint index) {
    uint value = 0;
    [forcecase]
    switch (index) {
        case 0: value = buffer.buf0; break;
case 1: value = buffer.buf1; break;
case 2: value = buffer.buf2; break;
case 3: value = buffer.buf3; break;
case 4: value = buffer.buf4; break;
case 5: value = buffer.buf5; break;
case 6: value = buffer.buf6; break;
case 7: value = buffer.buf7; break;
case 8: value = buffer.buf8; break;
case 9: value = buffer.buf9; break;
case 10: value = buffer.buf10; break;
case 11: value = buffer.buf11; break;
case 12: value = buffer.buf12; break;
case 13: value = buffer.buf13; break;
case 14: value = buffer.buf14; break;
case 15: value = buffer.buf15; break;
case 16: value = buffer.buf16; break;
case 17: value = buffer.buf17; break;
case 18: value = buffer.buf18; break;
case 19: value = buffer.buf19; break;
case 20: value = buffer.buf20; break;
case 21: value = buffer.buf21; break;
case 22: value = buffer.buf22; break;
case 23: value = buffer.buf23; break;
case 24: value = buffer.buf24; break;
case 25: value = buffer.buf25; break;
case 26: value = buffer.buf26; break;
case 27: value = buffer.buf27; break;
case 28: value = buffer.buf28; break;
case 29: value = buffer.buf29; break;
case 30: value = buffer.buf30; break;
case 31: value = buffer.buf31; break;
case 32: value = buffer.buf32; break;
case 33: value = buffer.buf33; break;
case 34: value = buffer.buf34; break;
case 35: value = buffer.buf35; break;
case 36: value = buffer.buf36; break;
case 37: value = buffer.buf37; break;
case 38: value = buffer.buf38; break;
case 39: value = buffer.buf39; break;
case 40: value = buffer.buf40; break;
case 41: value = buffer.buf41; break;
case 42: value = buffer.buf42; break;
case 43: value = buffer.buf43; break;
case 44: value = buffer.buf44; break;
case 45: value = buffer.buf45; break;
case 46: value = buffer.buf46; break;
case 47: value = buffer.buf47; break;
case 48: value = buffer.buf48; break;
case 49: value = buffer.buf49; break;
case 50: value = buffer.buf50; break;
case 51: value = buffer.buf51; break;
case 52: value = buffer.buf52; break;
case 53: value = buffer.buf53; break;
case 54: value = buffer.buf54; break;
case 55: value = buffer.buf55; break;
case 56: value = buffer.buf56; break;
case 57: value = buffer.buf57; break;
case 58: value = buffer.buf58; break;
case 59: value = buffer.buf59; break;
case 60: value = buffer.buf60; break;
case 61: value = buffer.buf61; break;
case 62: value = buffer.buf62; break;
case 63: value = buffer.buf63; break;

    }
    return value;
}

float4 tex_get_fb(uint2 px) {
    if (px.y >= 25) return (float4)0; // init empty newlines with 0
    px.y = 25 - px.y - 1;
    px.x += 1;
    return _SelfTexture2D[px];
}

#define NO_CHAR 0x80000000
struct fbupd {
    uint2 pos;
    uint upshift;
    uint char;
};
static fbupd fblog[BUFFER_MAX + 1];

void interpret(inout uint4 ctrlSeq, inout uint2 cursor_pos, inout uint char) {
    if (ctrlSeq.x == 8) {
        // backspace
        if (cursor_pos.x > 0) {
            cursor_pos.x -= 1;
        } else {
            cursor_pos.x = 0;
            // if (cursor_pos.y > 0) {
            //     cursor_pos.y -= 1;
            // }
        }
        char = 0;
    } else if (ctrlSeq.x == 10) {
        cursor_pos.y += 1;
    } else if (ctrlSeq.x == 13) {
        cursor_pos.x = 0;
    } else if (ctrlSeq.x == 9) {
        cursor_pos.x = (cursor_pos.x & 0xf8) + 8;
    } else if (ctrlSeq.x == 0x1b) {
        // ANSI escape sequence
        if (ctrlSeq.y == 0x9b) {
            // CSI
            if (ctrlSeq.z == 'A') {
                // up
                if (cursor_pos.y > 0) {
                    cursor_pos.y -= 1;
                }
            } else if (ctrlSeq.z == 'B') {
                // down
                if (cursor_pos.y < 25) {
                    cursor_pos.y += 1;
                }
            } else if (ctrlSeq.z == 'C') {
                // right
                if (cursor_pos.x < 80) {
                    cursor_pos.x += 1;
                }
            } else if (ctrlSeq.z == 'D') {
                // left
                if (cursor_pos.x > 0) {
                    cursor_pos.x -= 1;
                }
            } else if (ctrlSeq.z == 'H') {
                // home
                cursor_pos.x = 0;
                cursor_pos.y = 0;
            } else if (ctrlSeq.z == 'F') {
                // end
                cursor_pos.x = 80;
                cursor_pos.y = 25;
            } else if (!all(ctrlSeq.zw)) { // detect and reset on unknown sequence
                return;
            }
        } else if (ctrlSeq.y == 0) {
            return;
        }
    }
    // unrecognized or single-char control will reset
    ctrlSeq = (uint4)0;
}

float4 update_fb(uint2 pos, uart_buffer buffer, inout uint4 scratch1, inout uint4 scratch2) {
    float4 active = tex_get_fb(pos);
    [branch]
    if (buffer.ptr != 0xffffffff) {

    uint2 cursor_pos = uint2(scratch1.x, scratch1.y);
    uint2 orig_cursor_pos = cursor_pos;

    uint4 ctrlSeq = uint4(scratch1.z, scratch2.x, scratch2.y, scratch2.z);

    uint upshifts = 0;
    uint bufidx = 0;
    uint last = min(buffer.ptr, (uint)BUFFER_MAX);

    // generate log and upshift value
    [loop]
    for (bufidx = 0; bufidx <= last; bufidx++) {
        uint c = getbuf(buffer, bufidx);

        fbupd upd = (fbupd)0;
        upd.pos = cursor_pos;
        upd.upshift = upshifts;

        if (any(ctrlSeq)) {
            upd.char = NO_CHAR;
            if (ctrlSeq.y == 0) ctrlSeq.y = c;
            else if (ctrlSeq.z == 0) ctrlSeq.z = c;
            else ctrlSeq.w = c; // FIXME: longer ctrl sequences?
            interpret(ctrlSeq, cursor_pos, upd.char);
        } else if (c == 10 || c == 9 || c == 13 || c == 8 || c == 0x1b) {
            upd.char = NO_CHAR;
            ctrlSeq = uint4(c, 0, 0, 0);
            interpret(ctrlSeq, cursor_pos, upd.char);
        } else {
            cursor_pos += uint2(1, 0);
            upd.char = c;
        }

        fblog[bufidx] = upd;

        [branch]
        if (cursor_pos.x >= 80) {
            cursor_pos.x = 0;
            cursor_pos.y += 1;
        }
        [loop]
        while (cursor_pos.y >= 25) {
            cursor_pos.y--;
            upshifts++;
        }
    }

    uint2 new_cursor_pos = cursor_pos;
    uint2 active_pos = pos + uint2(0, upshifts);
    active = tex_get_fb(active_pos);

    // apply log
    [loop]
    for (bufidx = 0; bufidx <= last; bufidx++) {
        fbupd upd = fblog[bufidx];
        if (upd.char == NO_CHAR) continue;
        uint backshift = upshifts - upd.upshift;
        upd.pos -= uint2(0, backshift);
        [branch]
        if (upd.pos.x == pos.x && upd.pos.y == pos.y) {
            active = float4(upd.char / 255.0f, 7 / 255.0f, 0, 0);
        }
    }

    scratch1.x = new_cursor_pos.x;
    scratch1.y = new_cursor_pos.y;
    scratch1.z = ctrlSeq.x;
    scratch2.x = ctrlSeq.y;
    scratch2.y = ctrlSeq.z;
    scratch2.z = ctrlSeq.w;
    scratch2.w = 0;
    scratch1.w = 0;
    }
    return active;
}

#endif
