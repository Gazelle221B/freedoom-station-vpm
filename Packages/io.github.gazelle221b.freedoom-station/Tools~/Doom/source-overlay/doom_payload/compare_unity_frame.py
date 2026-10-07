#!/usr/bin/env python3
"""Save actual C replay beside Unity's GPU-rendered terminal and decoded grid.

Keep generated images/captures private and outside version control.
"""
import argparse
import io
from pathlib import Path

from PIL import Image
from compare_ascii import frame
from compare_ascii_modes import compose
import replay_uart as replay


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path, help='Stock C auto UART capture')
    parser.add_argument('unity', type=Path, help='Unity verification directory, including failed captures')
    parser.add_argument('--frame', type=int, default=12)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    c = Image.open(io.BytesIO(replay.export_ppm(frame(args.capture, args.frame), 2))).convert('RGB')
    c.save(args.out/'c-e1m1.png')
    name = 'e1m1' if (args.unity/'e1m1-console.png').exists() else f'frame-{args.frame}'
    gpu = Image.open(args.unity/(name+'-console.png')).convert('RGB')
    # Padding preserves the original pixels of both sources, without scaling.
    size = (max(c.width,gpu.width), max(c.height,gpu.height))
    panels = []
    for original in (c,gpu):
        panel = Image.new('RGB',size,'black'); panel.paste(original,(0,0)); panels.append(panel)
    compose(panels,[f'C rvc replay, frame {args.frame}', 'Unity Play Mode, actual GPU console.shader'],2).save(args.out/'c-vs-unity-gpu.png')
    # A second view renders the decoded GPU characters with the same replay
    # font/scale as C. This is explicitly separate from the real GPU screenshot.
    terminal = replay.Terminal(cols=80,rows=25)
    for y,row in enumerate((args.unity/(name+'.txt')).read_text().splitlines()[:25]):
        terminal.feed(f'\x1b[{y+1};1H'.encode()+row[:80].encode('ascii',errors='replace'))
    decoded = Image.open(io.BytesIO(replay.export_ppm(terminal,2))).convert('RGB')
    compose([c,decoded],[f'C rvc replay, frame {args.frame}', 'Unity decoded RT, same replay font and scale'],2).save(args.out/'c-vs-unity-grid.png')


if __name__ == '__main__':
    main()
