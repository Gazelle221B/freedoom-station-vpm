#!/usr/bin/env python3
"""Render actual C UART captures for legacy/A/B/C; host Pillow required.

Captures, derived images and metrics belong in ignored/private output only.
"""
import argparse
from collections import Counter
import io
import json
from pathlib import Path
import re
from statistics import mean

from PIL import Image, ImageDraw, ImageFont
from compare_ascii import frame
import replay_uart as replay


def image(terminal):
    return Image.open(io.BytesIO(replay.export_ppm(terminal, 2))).convert('RGB')


def compose(panels, labels, columns):
    width, height = panels[0].size
    gap, header = 16, 26
    rows = (len(panels) + columns - 1) // columns
    result = Image.new('RGB', (columns * width + (columns-1)*gap, rows*(height+header)+ (rows-1)*gap), '#181818')
    draw = ImageDraw.Draw(result)
    for i, (panel, label) in enumerate(zip(panels, labels)):
        x, y = (i % columns)*(width+gap), (i // columns)*(height+header+gap)
        draw.text((x+6, y+6), label, font=ImageFont.load_default(), fill='white')
        result.paste(panel, (x, y+header))
    return result


def costs(path):
    values = re.findall(r'RVC_PROBE_FRAME n=(\d+) steps=(\d+) ram_write_bytes=(\d+)', path.read_text())
    samples = [dict(frame=int(n), steps=int(s), ramWriteBytes=int(b)) for n, s, b in values if 1 <= int(n) <= 4]
    if [r['frame'] for r in samples] != [1, 2, 3, 4]:
        raise ValueError(f'Expected four intervals from five frames: {path}')
    first = samples[:2]
    steps, ram = mean(r['steps'] for r in first), mean(r['ramWriteBytes'] for r in first)
    return dict(samples=samples, stepsPerFrame=steps, ramWriteBytesPerFrame=ram,
                stepsChangeVs46982255Percent=100*(steps/2255704-1),
                ramWriteChangeVs46982255Percent=100*(ram/944309-1),
                allFourStepsPerFrame=mean(r['steps'] for r in samples),
                allFourRamWriteBytesPerFrame=mean(r['ramWriteBytes'] for r in samples))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--build', type=Path, default=Path(__file__).resolve().parent / 'build')
    parser.add_argument('--out', required=True, type=Path)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    pictures, texts, result = {}, {}, {'reference': dict(commit='46982255', stepsPerFrame=2255704, ramWriteBytesPerFrame=944309)}
    for mode in ('baseline', 'A', 'B', 'C'):
        prefix = args.build / 'ascii-options/baseline/plain' if mode == 'baseline' else args.build / ('plain-'+mode)
        capture, metrics = Path(str(prefix)+'-uart.bin'), Path(str(prefix)+'-metrics.txt')
        result[mode] = costs(metrics)
        for n in (2, 3, 4):
            terminal = frame(capture, n)
            text = replay.export_text(terminal)
            (args.out / f'{mode}-frame{n}.txt').write_bytes(text)
            texts[mode,n] = text
            pictures[mode,n] = image(terminal)
            pictures[mode,n].save(args.out / f'{mode}-frame{n}.png')
        # Count game cells only: 24 drawn rows, 79 columns, final row blank.
        cells = b''.join(row[:79] for row in texts[mode,2].splitlines()[:24])
        histogram = Counter(chr(c) for c in cells)
        result[mode]['frame2Characters'] = dict(sorted(histogram.items()))
        result[mode]['horizontalBarCells'] = histogram['-'] + histogram['=']
        if mode != 'baseline' and result[mode]['horizontalBarCells']:
            raise ValueError(f'{mode}: horizontal bars still present')
        result[mode]['temporalChangedCells'] = []
        for first, second in ((2, 3), (3, 4)):
            a = b''.join(row[:79] for row in texts[mode,first].splitlines()[:24])
            b = b''.join(row[:79] for row in texts[mode,second].splitlines()[:24])
            result[mode]['temporalChangedCells'].append(dict(frames=[first,second], changed=sum(x!=y for x,y in zip(a,b)), cells=len(a)))
    current = args.build / 'ascii-options/legacy-current/plain-uart.bin'
    result['defaultCaptureComparison'] = []
    for n in (2,3,4):
        old = texts['baseline',n].splitlines()
        new = replay.export_text(frame(current,n)).splitlines()
        changed = [(x,y) for y,(a,b) in enumerate(zip(old,new)) for x,(c,d) in enumerate(zip(a,b)) if c!=d]
        # Startup weapon presentation can differ between real guest runs.
        # Fixed-input byte parity is covered by the pinned driver unit test.
        outside_weapon = [(x,y) for x,y in changed if not (28<=x<=50 and 14<=y<=20)]
        result['defaultCaptureComparison'].append(dict(frame=n, changedCells=len(changed),
                                                       changedOutsideWeapon=len(outside_weapon), exact=not changed))
        if outside_weapon:
            raise ValueError(f'Default background/HUD differ at frame {n}: {outside_weapon}')
    compose([pictures[m,2] for m in ('baseline','A','B','C')],
            ['46982255 LUT baseline - frame 2','A: no horizontal bars - frame 2',
             'B: smoothed histogram - frame 2','C: histogram + 2x2 luminance average - frame 2'], 1).save(args.out / 'doom-ascii-four-modes.png')
    keys = [(m,n) for m in ('B','C') for n in (2,3,4)]
    compose([pictures[k] for k in keys], [f'{m} / frame {n}' for m,n in keys], 3).save(args.out / 'doom-ascii-B-C-frames2-4.png')
    for m in ('B','C'):
        animation = [compose([pictures[m,n]],[f'{m} / frame {n}'],1) for n in (2,3,4)]
        animation[0].save(args.out / f'{m}-frames2-4.gif', save_all=True, append_images=animation[1:], duration=600, loop=0)
    (args.out / 'comparison.json').write_text(json.dumps(result, indent=2)+'\n')
    print(json.dumps({m: {k:v for k,v in result[m].items() if k not in ('samples','frame2Characters')} for m in ('baseline','A','B','C')}, indent=2))


if __name__ == '__main__':
    main()
