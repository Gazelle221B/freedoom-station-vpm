#!/bin/sh
set -eu
task_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
task_target=$1
# This host debugger helper contains its absolute build sysroot. It is not
# executable guest code and no debugger is shipped in this minimal rootfs.
for task_debug in "$task_target"/usr/lib/libstdc++*-gdb.py; do
    if [ -f "$task_debug" ]; then rm -f "$task_debug"; fi
done
install -m 0755 "$task_dir/build/emdoom" "$task_target/emdoom"
install -m 0755 "$task_dir/doominit" "$task_target/doominit"
install -m 0755 "$task_dir/doomauto" "$task_target/doomauto"
mkdir -p "$task_target/usr/share/licenses/emdoom"
task_engine=${RVC_DOOM_ENGINE_SRC:-"$task_dir/cache/embeddeddoom"}
install -m 0644 "$task_engine/LICENSE.md" "$task_target/usr/share/licenses/emdoom/embeddeddoom-LICENSE.md"
install -m 0644 "$task_dir/licenses/DOOM-GPL-2.0.txt" "$task_target/usr/share/licenses/emdoom/DOOM-GPL-2.0.txt"
install -m 0644 "$task_dir/licenses/PAYLOAD-MODIFICATIONS.txt" "$task_target/usr/share/licenses/emdoom/PAYLOAD-MODIFICATIONS.txt"
install -m 0644 "$task_dir/build/iwad-manifest.json" "$task_target/usr/share/licenses/emdoom/IWAD.json"
task_iwad=$(cat "$task_dir/build/iwad-selection.txt")
case "$task_iwad" in
    freedoom)
        mkdir -p "$task_target/usr/share/licenses/freedoom"
        for task_notice in COPYING.txt CREDITS.txt CREDITS-MUSIC.txt; do
            install -m 0644 "$task_dir/cache/freedoom-0.13.0/$task_notice" "$task_target/usr/share/licenses/freedoom/$task_notice"
        done
        ;;
    shareware)
        install -m 0644 "$task_dir/licenses/shareware-data-LICENSE.txt" "$task_target/usr/share/licenses/emdoom/shareware-data-LICENSE.txt"
        install -m 0644 "$task_dir/licenses/PAYLOAD-SHAREWARE-MODIFICATIONS.txt" "$task_target/usr/share/licenses/emdoom/PAYLOAD-MODIFICATIONS.txt"
        ;;
    *) echo "Unknown IWAD: $task_iwad" >&2; exit 1 ;;
esac
