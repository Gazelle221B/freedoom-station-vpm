#!/usr/bin/env python3
"""Build an isolated env/v fixture, sharing the inventory with C and GPU tests."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess

import prepare_isa_tests as prep


def inventory():
    names, digest = prep.test_inventory()
    return [n.replace('-p-', '-v-') for n in names
            if n.startswith(('rv32ui-', 'rv32um-', 'rv32ua-'))], digest


def adapt_exit(source):
    old = '  do_tohost(code);\n  while (1);'
    new = '''  // Preserve the U-mode trap handler and eviction checks; adapt only final exit.
  register uintptr_t status asm("a0") = code == 1 ? 0 : (unsigned)code;
  register uintptr_t syscall asm("a7") = 93;
  asm volatile ("ecall" : : "r"(status), "r"(syscall) : "memory");
  while (1);'''
    if source.count(old) != 1:
        raise ValueError('Unexpected env/v terminate implementation')
    # The local bare-metal GCC rejects clear_cache's null range. RISC-V's
    # range-independent instruction-cache synchronization is exactly FENCE.I.
    return source.replace(old, new).replace('__builtin___clear_cache(0,0)',
                                            'asm volatile ("fence.i" ::: "memory")').replace(
        'fssr x0; 1:', '.word 0x00301073; 1:')  # same FCSR sentinel, jumped over


def run(command, **kwargs):
    subprocess.run([str(x) for x in command], check=True, **kwargs)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--riscv-tests', type=Path, required=True)
    parser.add_argument('--prefix', required=True)
    parser.add_argument('--out', type=Path, default=Path(__file__).parent/'build/mmu-v')
    args = parser.parse_args()
    source, out = args.riscv_tests.resolve(), args.out.resolve()
    out.mkdir(parents=True, exist_ok=True)
    env = out/'env'
    shutil.copytree(source/'env', env, dirs_exist_ok=True, symlinks=False)
    vm = env/'v/vm.c'
    vm.write_text(adapt_exit(vm.read_text()), encoding='utf-8')
    names, digest = inventory()
    # Independently verify that no Makefrag test is omitted from the common list.
    declared = []
    for group in ('rv32ui', 'rv32um', 'rv32ua'):
        text = (source/'isa'/group/'Makefrag').read_text()
        match = re.search(group+r'_sc_tests\s*=([\s\S]*?)\n'+group+r'_p_tests', text)
        if not match:
            raise ValueError('Unknown Makefrag layout: '+group)
        declared += [group+'-v-'+x for x in match[1].replace('\\', ' ').split()]
    if set(declared) != set(names):
        raise ValueError('test.sh/Makefrag virtual suite mismatch')
    elfdir = out/'elf'
    elfdir.mkdir(exist_ok=True)
    records = []
    for name in names:
        group, _, case = name.split('-', 2)
        entropy = hashlib.md5(name.encode()).hexdigest()[:7]
        target = elfdir/name
        command = [args.prefix+'gcc', '-march=rv32ima', '-mabi=ilp32',
                   '-static', '-mcmodel=medany', '-fvisibility=hidden',
                   '-nostdlib', '-nostartfiles', '-std=gnu99', '-O2', '-fno-builtin',
                   '-DENTROPY=0x'+entropy, '-I'+str(env/'v'),
                   '-I'+str(source/'isa/macros/scalar'), '-T'+str(env/'v/link.ld'),
                   env/'v/entry.S', env/'v/vm.c', env/'v/string.c',
                   source/'isa'/group/(case+'.S'), '-lgcc', '-o', target]
        run(command)
        info = prep.parse_elf(target)
        error = prep.validate_elf(info, name)
        if error:
            raise ValueError(name+': '+error)
        symbols = subprocess.check_output([args.prefix+'nm', str(target)], text=True)
        tohost = int(re.search(r'(?m)^([0-9a-f]+)\s+\w\s+tohost$', symbols)[1], 16)
        records.append(dict(test=name, tohost=tohost,
                            sha256=hashlib.sha256(target.read_bytes()).hexdigest(),
                            maxLoadEnd=max(s['vaddr']+s['memsz'] for s in info['segments'])))
        print('BUILT '+name, flush=True)
    manifest = dict(suite='rv32ui/um/ua env/v', testShSha256=digest,
                    expected=names, prepared=names, complete=True, records=records,
                    ramBase=0x80000000, ramBytes=126*1024*1024,
                    riscvTestsCommit=subprocess.check_output(
                        ['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip(),
                    envCommit=subprocess.check_output(
                        ['git', '-C', str(source/'env'), 'rev-parse', 'HEAD'], text=True).strip())
    (out/'manifest.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
    print('Built virtual suite: '+str(len(names)))


if __name__ == '__main__':
    main()
