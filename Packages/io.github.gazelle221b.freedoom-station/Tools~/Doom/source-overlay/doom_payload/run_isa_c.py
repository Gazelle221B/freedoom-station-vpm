#!/usr/bin/env python3
"""Run the common ISA inventory against the unchanged C core; stop on failure."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

import prepare_isa_tests as prep


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runner', type=Path, required=True)
    parser.add_argument('--elf-dir', type=Path, required=True)
    parser.add_argument('--manifest', type=Path)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--max-steps', type=int, default=2000000)
    args = parser.parse_args()
    names, digest = prep.test_inventory()
    manifest = json.loads(args.manifest.read_text()) if args.manifest else None
    if manifest:
        if not manifest['complete'] or manifest['testShSha256'] != digest:
            raise ValueError('Incomplete or stale shared manifest')
        names = manifest['expected']
    records = {r['test']: r for r in manifest['records']} if manifest else {}
    args.out.mkdir(parents=True, exist_ok=True)
    results = []
    for name in names:
        path = args.elf_dir/name
        if name in records and hashlib.sha256(path.read_bytes()).hexdigest() != records[name]['sha256']:
            raise ValueError('Fixture hash mismatch: '+name)
        command = [str(args.runner), str(path), str(args.max_steps), str(records.get(name, {}).get('tohost', 0))]
        result = subprocess.run(command, capture_output=True, text=True, timeout=60)
        (args.out/(name+'.trace')).write_text(result.stdout+result.stderr, encoding='utf-8')
        match = re.search(r'^RESULT (.+)$', result.stdout, re.MULTILINE)
        data = json.loads(match[1]) if match else dict(passed=False, reason='No final result')
        data.update(test=name, processExit=result.returncode)
        if manifest:
            data['passed'] = (data['passed'] and data['satp'] >> 31 == 1 and
                              data['pageFaults'] > 0 and data['userEcalls'] == 1 and
                              data['exitPrivilege'] == 1)
        results.append(data)
        print(name+(' PASS' if data['passed'] and result.returncode == 0 else ' FAIL'), flush=True)
        if not data['passed'] or result.returncode != 0:
            break
    report = dict(expected=names, tests=results,
                  passed=sum(r['passed'] and r['processExit'] == 0 for r in results),
                  failed=sum(not r['passed'] or r['processExit'] != 0 for r in results),
                  notRun=names[len(results):], complete=len(results) == len(names))
    (args.out/'results.json').write_text(json.dumps(report, indent=2)+'\n', encoding='utf-8')
    if report['failed'] or not report['complete']:
        raise SystemExit(1)


if __name__ == '__main__':
    main()
