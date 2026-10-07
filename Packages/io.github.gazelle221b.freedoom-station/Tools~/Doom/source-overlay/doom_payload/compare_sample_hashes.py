#!/usr/bin/env python3
"""Compare renderer diagnostics from actual C and GPU UART captures."""
import argparse
import json
from pathlib import Path
import re


def traces(path):
    text = path.read_bytes().decode('ascii', errors='replace').replace('\r', '')
    pattern = r'RVC_ASCII_TRACE n=(\d+) samples=([0-9a-f]{8}) histogram=([0-9a-f]{8}) palette=([0-9a-f]{8}) view=([^ ]+) size=(\d+)'
    return {int(n): dict(samples=s, histogram=h, palette=p, view=v, width=int(w))
            for n, s, h, p, v, w in re.findall(pattern, text)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('c', type=Path)
    parser.add_argument('gpu', type=Path)
    parser.add_argument('--frames', type=int, default=3)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    if args.frames < 3:
        parser.error('Compare at least three consecutive frames')
    reference, actual = traces(args.c), traces(args.gpu)
    rows = [dict(frame=n, c=reference.get(n), gpu=actual.get(n),
                 matched=n in reference and n in actual and reference[n] == actual[n])
            for n in range(1, args.frames + 1)]
    result = dict(passed=all(r['matched'] for r in rows), frames=rows)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, indent=2))
    return 0 if result['passed'] else 2


if __name__ == '__main__':
    raise SystemExit(main())
