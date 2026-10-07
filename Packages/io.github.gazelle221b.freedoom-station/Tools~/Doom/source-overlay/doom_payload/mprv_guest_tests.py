#!/usr/bin/env python3
"""Build self-checking MPRV/xRET guest ELFs and a shared C/GPU inventory."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

import prepare_isa_tests as prep


def return_case(instruction, target, through_s=False):
    cause = 11 if target == 3 else 9 if target == 1 else 8
    retain = instruction == 'mret' and target == 3
    register = 'mepc' if instruction == 'mret' else 'sepc'
    status = (1 << 17) | (target << 11 if instruction == 'mret' else target << 8)
    bootstrap = ''
    if through_s:
        # Enter S through a real MRET; it must already clear MPRV.
        bootstrap = '''la t0, supervisor
csrw mepc, t0
li t0, 0x20800
csrw mstatus, t0
mret
supervisor:
'''
        status = target << 8
    body = f'''{bootstrap}la t0, target
csrw {register}, t0
li t0, {status}
csrw {'sstatus' if through_s else 'mstatus'}, t0
{instruction}
target:
ecall
j fail
'''
    handler = f'''csrr t0, mcause
li t1, {cause}
bne t0, t1, fail
csrr t0, mstatus
li t1, 0x20000
and t0, t0, t1
{'beqz' if retain else 'bnez'} t0, fail
j pass
'''
    return body, handler, ''


def load_case(mpp):
    body = f'''li t0, 0x80080001
csrw satp, t0
li t0, {0x20000 | (mpp << 11)}
csrw mstatus, t0
li t0, 0x00400000
lw t1, 0(t0)
li t2, 0x12345678
bne t1, t2, fail
csrr t0, mstatus
li t1, 0x20000
and t0, t0, t1
beqz t0, fail
j pass
'''
    # Code PC=0x80000000 is deliberately unmapped in satp. M fetch must use PA.
    flags = 0xc7 if mpp == 1 else 0xd7
    data = f'''.org 0x1000
.word 0
.word 0x20000801
.org 0x2000
.word {0x20001000 | flags}
.org 0x4000
.word 0x12345678
'''
    return body, 'j fail\n', data


def cases():
    entries = []
    for target, name in ((0, 'u'), (1, 's'), (3, 'm')):
        entries.append((f'mprv-mret-to-{name}', return_case('mret', target)))
    for target, name in ((0, 'u'), (1, 's')):
        entries.append((f'mprv-sret-m-to-{name}', return_case('sret', target)))
        entries.append((f'mprv-sret-s-to-{name}', return_case('sret', target, True)))
    for mpp, name in ((0, 'u'), (1, 's')):
        entries.append((f'mprv-m-fetch-load-{name}', load_case(mpp)))
    return entries


def assembly(body, handler, data):
    return f'''.option norvc
.option norelax
.section .text
.globl _start
_start:
la t0, trap_handler
csrw mtvec, t0
{body}
trap_handler:
{handler}
pass:
li a0, 0
j exit
fail:
li a0, 1
exit:
li a7, 93
ecall
j exit
{data}'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prefix', required=True)
    parser.add_argument('--out', type=Path, default=Path(__file__).parent/'build/mprv-guest')
    parser.add_argument('--runner', type=Path)
    args = parser.parse_args()
    elf_dir, textures = args.out/'elf', args.out/'textures'
    elf_dir.mkdir(parents=True, exist_ok=True)
    textures.mkdir(parents=True, exist_ok=True)
    linker = args.out/'guest.ld'
    linker.write_text('ENTRY(_start)\nPHDRS { ram PT_LOAD FLAGS(7); }\n'
                      'SECTIONS { . = 0x80000000; .text : { *(.text .text.*) } :ram\n'
                      '.data : { *(.data .data.*) } :ram\n'
                      '.bss (NOLOAD) : { *(.bss .bss.*) *(COMMON) } :ram }\n', encoding='utf-8')
    records, results = [], []
    for name, (body, handler, data) in cases():
        source, elf = elf_dir/(name+'.S'), elf_dir/name
        source.write_text(assembly(body, handler, data), encoding='utf-8')
        subprocess.run([args.prefix+'gcc', '-march=rv32ima', '-mabi=ilp32', '-nostdlib',
                        '-nostartfiles', '-static', '-Wl,-T,'+str(linker), '-Wl,--no-relax',
                        '-Wl,-e,_start', str(source), '-o', str(elf)], check=True)
        info = prep.parse_elf(elf)
        error = prep.validate_elf(info, name)
        if error:
            raise ValueError(name+': '+error)
        width, height, pixels = prep.elf_to_textures(info)
        for lane, label in enumerate('rgba'):
            prep.write_png(str(textures/(name+'.'+label+'.png')), width, height,
                           [pixel[lane] for pixel in pixels])
        records.append({'test': name, 'sha256': hashlib.sha256(elf.read_bytes()).hexdigest()})
        if args.runner:
            result = subprocess.run([str(args.runner), str(elf), '20000'], capture_output=True,
                                    text=True, timeout=30)
            (args.out/(name+'.trace')).write_text(result.stdout+result.stderr, encoding='utf-8')
            match = re.search(r'^RESULT (.+)$', result.stdout, re.MULTILINE)
            record = json.loads(match[1]) if match else {'passed': False}
            record.update(test=name, processExit=result.returncode)
            results.append(record)
            print(name, 'PASS' if record['passed'] and result.returncode == 0 else 'FAIL', flush=True)
    manifest = {'suite': 'self-checking MPRV/xRET guest sequences', 'complete': True,
                'expected': [name for name, _ in cases()], 'records': records}
    (textures/'manifest.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
    if args.runner:
        report = {'total': len(results), 'passed': sum(r['passed'] and r['processExit']==0 for r in results),
                  'tests': results}
        (args.out/'results.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
        if report['passed'] != len(cases()):
            raise SystemExit(1)


if __name__ == '__main__':
    main()
