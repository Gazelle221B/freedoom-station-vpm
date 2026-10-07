#!/usr/bin/env python3
"""Install the locally verified board into an existing DoomStation project.

Copies only the packaged board assets, original notices, and editor/runtime
scripts. Payload textures and all CPU/MMU files are outside this file list.
"""
import argparse
import hashlib
import json
import shutil
from pathlib import Path


def install(project: Path, backup: Path):
    here = Path(__file__).resolve().parent
    repo = here.parent.parent
    project = project.resolve()
    if not (project / 'ProjectSettings/ProjectVersion.txt').is_file():
        raise ValueError('Target is not a Unity project')
    station = project / 'Assets/Doom/DoomStation.prefab'
    if not station.is_file():
        raise ValueError('An existing DoomStation installation is required')
    station_meta = station.with_name(station.name + '.meta')
    expected_meta = here / 'UnityAssets/Assets/Doom/DoomStation.prefab.meta'
    def guid(path):
        return next(line for line in path.read_text(encoding='utf-8').splitlines()
                    if line.startswith('guid: '))
    if guid(station_meta) != guid(expected_meta):
        raise ValueError('DoomStation GUID differs; review the scene references first')

    files = [(p, p.relative_to(here / 'UnityAssets'))
             for p in (here / 'UnityAssets').rglob('*') if p.is_file()]
    files += [(p, Path('Assets/Doom/LicenseBoard/Documents') / p.name)
              for p in (here / 'Documents').iterdir() if p.is_file()]
    files.append((here / 'documents.json', Path('Assets/Doom/LicenseBoard/documents.json')))
    for section, names in (
        ('Runtime', ['DoomLicenseBoard', 'DoomLicenseButton']),
        ('Editor', ['DoomLicenseBoardBuild', 'DoomLicenseFont', 'DoomDisplayAudit', 'DoomVerify',
                    'DoomWorldErrorTests', 'DoomFramebufferProbe']),
    ):
        for name in names:
            for suffix in ('.cs', '.cs.meta'):
                src = repo / 'doom_payload/unity' / section / (name + suffix)
                if src.is_file():
                    files.append((src, Path('Assets/Doom') / section / src.name))
    records = []
    for src, rel in sorted(files, key=lambda pair: str(pair[1])):
        dst = project / rel
        if dst.is_file() and dst.read_bytes() != src.read_bytes():
            saved = backup.resolve() / rel
            if saved.exists():
                raise ValueError('Choose a fresh backup directory: ' + str(saved))
            saved.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(dst, saved)
        dst.parent.mkdir(parents=True, exist_ok=True)
        if not dst.is_file() or dst.read_bytes() != src.read_bytes():
            shutil.copy2(src, dst)
        digest = hashlib.sha256(src.read_bytes()).hexdigest()
        if hashlib.sha256(dst.read_bytes()).hexdigest() != digest:
            raise ValueError('Installed hash mismatch: ' + str(rel))
        records.append({'file': rel.as_posix(), 'sha256': digest, 'bytes': src.stat().st_size})
    return records


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', required=True, type=Path)
    parser.add_argument('--backup', required=True, type=Path)
    parser.add_argument('--manifest', required=True, type=Path)
    args = parser.parse_args()
    records = install(args.project, args.backup)
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(json.dumps({'files': records, 'allMatched': True}, indent=2) + '\n', encoding='utf-8')
    print('Installed and hash-verified', len(records), 'files')


if __name__ == '__main__':
    main()
