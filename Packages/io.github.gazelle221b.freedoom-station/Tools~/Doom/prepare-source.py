#!/usr/bin/env python3
"""Materialize the reviewed payload sources; no payload output is committed."""
import argparse
from pathlib import Path
import shutil
import subprocess

PIN = 'da936a719b4254e91ba422361d7c1d1d0e775b8f'
HERE = Path(__file__).resolve().parent


def run(*command, cwd=None):
    subprocess.run(command, cwd=cwd, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--destination', type=Path, default=HERE/'cache/rvc-doom')
    parser.add_argument('--upstream', default='https://github.com/PiMaker/rvc.git', help='Optional local mirror for offline checks')
    parser.add_argument('--skip-submodules', action='store_true', help='For offline source-overlay checks only')
    args = parser.parse_args()
    dest = args.destination.resolve()
    # Never reset or overwrite an existing developer checkout.
    if dest.exists():
        raise SystemExit('Destination already exists; choose a fresh source workspace: '+str(dest))
    run('git', 'clone', '--no-checkout', args.upstream, str(dest))
    run('git', 'checkout', '--detach', PIN, cwd=dest)
    run('git', 'apply', '--check', str(HERE/'c-core-permissions.patch'), cwd=dest)
    run('git', 'apply', str(HERE/'c-core-permissions.patch'), cwd=dest)
    for path in (HERE/'source-overlay').rglob('*'):
        if path.is_file():
            target=dest/path.relative_to(HERE/'source-overlay')
            target.parent.mkdir(parents=True,exist_ok=True)
            shutil.copyfile(path,target)
            if target.name in ('doomauto','doominit') or target.suffix=='.sh':
                target.chmod(target.stat().st_mode | 0o111)
    if not args.skip_submodules:
        # Upstream uses an SSH URL; this setup needs no GitHub SSH credentials.
        run('git','-c','submodule.linux.url=https://github.com/PiMaker/linux-rvc.git',
            'submodule','update','--init','--recursive','linux','opensbi','riscv-tests',cwd=dest)
    print('Prepared pinned source workspace:',dest)


if __name__=='__main__':
    main()
