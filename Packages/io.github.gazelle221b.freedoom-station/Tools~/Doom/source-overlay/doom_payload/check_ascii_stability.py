#!/usr/bin/env python3
"""Report adjacent-frame ASCII changes from an actual C-rvc UART capture.

Captures and reports belong in ignored build/ or a private output directory.
"""
import argparse
import json
from pathlib import Path
import re
from statistics import mean

from compare_ascii import frame
import replay_uart as replay


def analyze(capture, metrics, frames):
    texts = [replay.export_text(frame(capture, n)).splitlines()[:24] for n in range(1, frames + 1)]
    transitions = []
    for n, (before, after) in enumerate(zip(texts, texts[1:]), 2):
        changed = [dict(x=x, y=y, before=chr(a), after=chr(b))
                   for y, (row_a, row_b) in enumerate(zip(before, after))
                   for x, (a, b) in enumerate(zip(row_a[:79], row_b[:79])) if a != b]
        transitions.append(dict(frame=n, changedCells=len(changed),
                                sceneChanges=sum(c['y'] < 20 for c in changed),
                                hudChanges=sum(c['y'] >= 20 for c in changed), cells=changed))
    samples = [dict(frame=int(n), steps=int(s), ramWriteBytes=int(b)) for n, s, b in
               re.findall(r'RVC_PROBE_FRAME n=(\d+) steps=(\d+) ram_write_bytes=(\d+)', metrics.read_text())]
    if len(samples) < frames - 1:
        raise ValueError('Missing C measurement intervals')
    initial = samples[:2]
    return dict(frames=frames, input='No gameplay bytes sent after the shell launch command',
                transitions=transitions, trailingZeroTransitions=next(
                    (i for i, t in enumerate(reversed(transitions)) if t['changedCells']), len(transitions)),
                initialStepsPerFrame=mean(t['steps'] for t in initial),
                initialRamWriteBytesPerFrame=mean(t['ramWriteBytes'] for t in initial),
                allStepsPerFrame=mean(t['steps'] for t in samples[:frames-1]),
                allRamWriteBytesPerFrame=mean(t['ramWriteBytes'] for t in samples[:frames-1]),
                costSamples=samples[:frames-1])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path)
    parser.add_argument('metrics', type=Path)
    parser.add_argument('--frames', type=int, default=24)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    if args.frames < 10:
        parser.error('Capture at least ten frames')
    result = analyze(args.capture, args.metrics, args.frames)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2) + '\n')
    print('frame changed scene HUD')
    for t in result['transitions']:
        print(t['frame'], t['changedCells'], t['sceneChanges'], t['hudChanges'])
    print('trailingZeroTransitions=', result['trailingZeroTransitions'])


if __name__ == '__main__':
    main()
