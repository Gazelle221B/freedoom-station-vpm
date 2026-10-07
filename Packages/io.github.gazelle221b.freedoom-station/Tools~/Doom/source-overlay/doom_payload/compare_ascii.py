#!/usr/bin/env python3
"""Compare two C-rvc ASCII captures and counters; requires host Pillow.

All captures/images/golden data must remain in ignored build/ or private output.
"""
import argparse
import io
import json
from pathlib import Path
import re
from statistics import mean

from PIL import Image, ImageDraw, ImageFont
import replay_uart as replay


def frame(capture, number):
    data = capture.read_bytes()
    marker = replay._find_frame_marker(data, number)
    if marker is None:
        raise ValueError(f"Missing frame {number} in {capture}")
    prefix, _ = replay.clip_before_marker(data, marker)
    terminal = replay.Terminal(cols=80, rows=25)
    terminal.feed(prefix)
    return terminal


def costs(path):
    values = re.findall(r"RVC_PROBE_FRAME n=(\d+) steps=(\d+) ram_write_bytes=(\d+)", path.read_text())
    rows = [dict(frame=int(n), steps=int(s), ramWriteBytes=int(b)) for n, s, b in values if int(n) in (1, 2)]
    if len(rows) != 2:
        raise ValueError(f"Expected frame intervals 1->2 and 2->3 in {path}")
    return dict(samples=rows, stepsPerFrame=mean(r['steps'] for r in rows),
                ramWriteBytesPerFrame=mean(r['ramWriteBytes'] for r in rows))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path, help="directory with plain-uart.bin/plain-metrics.txt")
    parser.add_argument("after", type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    panels = []
    for directory, name in ((args.before, 'before'), (args.after, 'after')):
        terminal = frame(directory / 'plain-uart.bin', 2)
        (args.out / f'ascii-{name}.txt').write_bytes(replay.export_text(terminal))
        image = Image.open(io.BytesIO(replay.export_ppm(terminal, 2)))
        image.save(args.out / f'ascii-{name}.png')
        panels.append(image)
    canvas = Image.new('RGB', (panels[0].width * 2 + 24, panels[0].height + 50), '#181818')
    draw = ImageDraw.Draw(canvas)
    font = ImageFont.load_default()
    for i, (panel, label) in enumerate(zip(panels, ('Before: RGB average', 'After: weighted luminance + sqrt LUT'))):
        x = i * (panel.width + 24)
        draw.text((x + 8, 16), label, font=font, fill='white')
        canvas.paste(panel, (x, 50))
    canvas.save(args.out / 'doom-ascii-before-after.png')
    before, after = costs(args.before / 'plain-metrics.txt'), costs(args.after / 'plain-metrics.txt')
    previous = 2287142
    result = dict(before=before, after=after, previousStepsPerFrame=previous,
                  stepsChangePercent=100 * (after['stepsPerFrame'] / before['stepsPerFrame'] - 1),
                  previousStepsChangePercent=100 * (after['stepsPerFrame'] / previous - 1),
                  ramWriteChangePercent=100 * (after['ramWriteBytesPerFrame'] / before['ramWriteBytesPerFrame'] - 1),
                  secondsPerFrameAt250kHz=after['stepsPerFrame'] / 250000)
    result['passed'] = (after['stepsPerFrame'] <= min(previous, before['stepsPerFrame']) and
                        after['ramWriteBytesPerFrame'] <= before['ramWriteBytesPerFrame'])
    (args.out / 'doom-ascii-cost.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, indent=2))
    if not result['passed']:
        raise SystemExit('ASCII cost regression')


if __name__ == '__main__':
    main()
