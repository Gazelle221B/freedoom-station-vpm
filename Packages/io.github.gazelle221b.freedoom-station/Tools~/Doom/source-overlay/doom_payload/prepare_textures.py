#!/usr/bin/env python3
"""Run the unchanged upstream toimg source with a PNG/BMP-only build profile."""
from pathlib import Path
import re
import shutil
import subprocess
import sys
import json
from make_font import make_font
root = Path(__file__).resolve().parent.parent
build = root / "doom_payload/build"
tool = build / "toimg"
(tool / "src").mkdir(parents=True, exist_ok=True)
shutil.copyfile(root / "toimg/src/main.rs", tool / "src/main.rs")
(tool / "Cargo.toml").write_text('[package]\nname="doom-toimg"\nversion="0.1.0"\nedition="2018"\n[dependencies]\nimage={version="=0.24.9",default-features=false,features=["png","bmp"]}\n')
subprocess.run(["cargo","build","--release"],cwd=tool,check=True)
output = build / "unity-textures"
output.mkdir(exist_ok=True)
sizes = {}
sources = [(root / "linux_payload.bin",2048,4096),
                                     (build / "doom-auto.dtb",256,16),
                                     (build / "doom-auto-verify.dtb",256,16)]
if (build / "doom-auto-trace.dtb").exists():
    sources.append((build / "doom-auto-trace.dtb",256,16))
for source, width, initial_height in sources:
    target=output / source.name
    shutil.copyfile(source,target)
    cmd=[str(tool / "target/release/doom-toimg"),str(target),str(width),str(initial_height)]
    first=subprocess.check_output(cmd,text=True)
    print(first)
    height=int(re.search(r"required size: x=\d+ y=(\d+)",first)[1])
    cmd[-1]=str(height)
    subprocess.run(cmd,check=True)
    sizes[source.name]=dict(width=width,height=height,bytes=source.stat().st_size)
(output / "textures.json").write_text(json.dumps(sizes,indent=2)+'\n')
make_font(output / 'doom-font.png')
if len(sys.argv)>1:
    destination=Path(sys.argv[1]); destination.mkdir(parents=True,exist_ok=True)
    for path in output.glob("*.png"): shutil.copyfile(path,destination / path.name)
    shutil.copyfile(output / "textures.json",destination / "textures.json")
