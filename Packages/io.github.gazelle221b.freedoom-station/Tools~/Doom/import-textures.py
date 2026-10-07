#!/usr/bin/env python3
"""Install locally generated PNGs with the reviewed Unity GUID/import settings."""
import argparse
import json
from pathlib import Path
import shutil

HERE=Path(__file__).resolve().parent


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',required=True,type=Path,help='Source workspace doom_payload/build/unity-textures')
    parser.add_argument('--project',type=Path,required=True)
    args=parser.parse_args()
    templates=json.loads((HERE/'texture-imports.json').read_text(encoding='utf-8'))
    missing=[name[:-5] for name in templates if not (args.source/name[:-5]).is_file()]
    if missing: raise SystemExit('Generate the complete texture set first: '+', '.join(missing))
    if not (args.project/'Assets/Doom/DoomStation.prefab').is_file():
        raise SystemExit('Install the VPM station templates before importing textures')
    target=args.project/'Assets/Doom/Generated'
    target.mkdir(parents=True,exist_ok=True)
    for name,content in templates.items():
        shutil.copyfile(args.source/name[:-5],target/name[:-5])
        (target/name).write_text(content,encoding='utf-8',newline='\n')
    for name in ('textures.json','iwad-manifest.json'):
        if (args.source/name).is_file(): shutil.copyfile(args.source/name,target/name)
    print('Installed',len(templates),'local textures; keep Assets/Doom/Generated gitignored.')


if __name__=='__main__':
    main()
