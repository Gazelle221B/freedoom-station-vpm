# Existing RVC improvements inspired by ShaderEmu

Reference: [MichaelMoroz/ShaderEmu](https://github.com/MichaelMoroz/ShaderEmu/tree/30e6b115ce54d4538498589b65257d9d30bc05a9), commit `30e6b115ce54d4538498589b65257d9d30bc05a9` (`experiments/rvc_opt`). Its MIT notice is retained in `ShaderEmu-LICENSE.txt` and Third Party Notices.

This change adapts two CPU-side ideas to the existing PiMaker/RVC core:

- Convert Unity's `_Time.x` to the CLINT timer once per CPUTick pass. Each instruction still overwrites CLINT mtime immediately before the existing interrupt checks, preserving guest reads/writes and interrupt ordering.
- Write aligned 32-bit RAM values through the existing word cache once instead of merging four bytes. Unchanged words still avoid cache writes. MMIO, unaligned/byte/halfword stores and RAM word zero retain their original path. Zero is the cache's empty-entry sentinel; bypassing its byte fallback would change existing behavior.

The cache layout, commit pass, integer MULH implementation, Sv32 permissions and SRET handling are retained. This works with Unity's existing D3D11/FXC pixel shaders. ShaderEmu's FXC2 intrinsics, replacement guest images and GPU command renderer are outside this change.

## Regression checks

Environment: Unity 2022.3.22f1, Windows, NVIDIA GeForce RTX 4070 Ti SUPER, Direct3D11. The production shader files were installed into the existing local Doom world for validation. Results below describe that host, rather than a newly installed VPM project.

| Check | Original core | Changed core |
| --- | --- | --- |
| Physical RISC-V ISA fixtures, including M/U/S CSR cases | 61 passed; 0 failed; 0 skipped | 61 passed; 0 failed; 0 skipped |
| Production memory helpers on GPU | 512 cases passed; 2,048 output words checked | 512 cases passed; 2,048 output words checked |
| Linux/Freedoom startup and advancing terminal | Started; 3 advancing frames | Started; 3 advancing frames |
| C Freedoom scene/HUD comparison | Passed on offline recheck with the correct Freedoom reference | Passed in the verification driver |

The new `Memory` mode exercises cold/warm/unchanged full-word writes, byte offsets, halfword and unaligned stores across words, cache collisions/overflow, RAM word zero, an out-of-range write, CLINT registers and UART. Expected ordinary RAM values come from an independent byte-addressed host model; the probe includes production `mem.h` rather than a copied memory implementation.

The first baseline run selected an older host runner's shareware `golden-auto.txt`, so its original verification result reported failure. Its captured terminal passed the existing row comparator when rechecked against `golden-freedoom-auto.txt`. The package runner already defaults to the correct Freedoom reference.

The first changed-core game run passed the C comparator, then hit an existing missing screenshot-directory exception before saving the final report. `Capture` now creates its output directory. A separate regression started with a missing directory and successfully wrote a 1920x1080 PNG. The complete game verification was rerun after this fix; the sanitized evidence file records its final result.

An exploratory GPU sampler produced the same timing for every recorded sample in this batch-mode host. Those readings are unsuitable for a speed comparison and are not used as performance evidence. No speed claim was based on that sampler. A subsequent actual VRChat Desktop comparison and fresh local SDK builds are documented in [VRCHAT-PERFORMANCE.md](VRCHAT-PERFORMANCE.md). That comparison did not show an FPS improvement; headset performance and SDK upload remain unmeasured.

## Reproduce

Use the existing package preparation/import steps first, including local payload and ISA fixtures. Set `UNITY_EDITOR` to Unity 2022.3.22f1 and `RVC_DOOM_SOURCE` to the locally prepared source tree. Then run:

```powershell
& 'Packages/io.github.gazelle221b.freedoom-station/Tools~/Doom/run-doom.ps1' -Mode Memory -Project 'D:\YourUnityProject' -Timeout 900
& 'Packages/io.github.gazelle221b.freedoom-station/Tools~/Doom/run-doom.ps1' -Mode Isa -Project 'D:\YourUnityProject' -Timeout 900
& 'Packages/io.github.gazelle221b.freedoom-station/Tools~/Doom/run-doom.ps1' -Mode Verify -Project 'D:\YourUnityProject' -Ticks 4096 -Timeout 1200
```

`Memory` generates a local probe under `Assets/Doom/Generated` and writes a JSON report under `DOOM_MEMORY_OUT` (default: `UserSettings/Doom/memory`). ISA and game results use their existing output variables. No WAD, payload binary, golden frame, capture or raw Unity/license log is included with this change.
