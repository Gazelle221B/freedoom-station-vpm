# VRChat Desktop performance comparison (2026-10-08)

The first actual-client comparison did **not** demonstrate an FPS improvement. Average rendered FPS was lower in the changed-core run, despite a small reduction in GPU busy time. These are observed whole-client results; they do not establish the cause of the FPS difference.

| Metric | Original core | Changed core | Change |
| --- | ---: | ---: | ---: |
| Average rendered FPS | 87.13 FPS | 83.23 FPS | -4.49% |
| Mean frame time | 11.48 ms | 12.02 ms | +4.70% |
| P99 frame time | 13.84 ms | 16.69 ms | +20.60% |
| Mean GPU busy time | 8.15 ms | 8.06 ms | -1.10% |
| Mean GPU time including waits | 9.18 ms | 9.29 ms | +1.13% |

## Conditions and procedure

- Existing `Assets/Doom/DoomWorld.unity`, built locally through the public VRChat Worlds SDK Build API. Only `main.shader`, `src/emu.h` and `src/mem.h` were switched between the two builds. The scene VM material's shader GUID matched this production shader, and the build log confirmed its import and compilation. Source was restored to the changed version after building the baseline.
- Windows; Intel Core i9-13900K; NVIDIA GeForce RTX 4070 Ti SUPER; NVIDIA Windows driver `32.0.16.1714`.
- Actual VRChat client engine `6000.0.67f1-DWR (6158d48dc694)`, Direct3D 11. SDK bundles built with Unity 2022.3.22f1.
- Desktop mode; 1920x1080 windowed launch settings; `--fps=1000` resolved in the client log. Local test world; no movement or gameplay input was scripted. Existing client/avatar/graphics preferences were reused. All selected presents had DXGI `SyncInterval=0`.
- Production instruction budget: 16,384 ticks / divisor 4 = 4,096 per frame. No replacement ShaderEmu guest image or GPU renderer.
- Candidate measured first, baseline second. Each cold-started the same production world and completed at least 600 seconds and 60,000 captured presentation frames of warmup. The Editor was closed before sampling.
- Three adjacent 30-second sampling windows per build. Intel-signed PresentMon Console 2.6.0 collected frame/GPU events by the client process ID. The two boundary frames at each end of each window were excluded; the dominant swapchain was selected.
- FPS is `1000 / mean(MsBetweenPresents)`, which describes VRChat rendering, not the emulated Freedoom game's own frame rate. GPU busy and GPU time are PresentMon's separate metrics; GPU time includes GPU wait time.

Baseline: 725.5 s / 60,447 warmup frames; 7,817 sampled frames. Candidate: 853.9 s / 63,580 warmup frames; 7,466 sampled frames.

| 30-second window | Original FPS | Changed FPS | Original GPU busy | Changed GPU busy |
| --- | ---: | ---: | ---: | ---: |
| 1 | 87.71 | 83.95 | 8.146 ms | 8.045 ms |
| 2 | 86.12 | 82.67 | 8.150 ms | 8.082 ms |
| 3 | 87.57 | 83.06 | 8.144 ms | 8.047 ms |

## Interpretation and limits

This is one sequential launch per build with three adjacent windows, not three independent launches or an isolated CPUTick microbenchmark. The guest state was not synchronized byte-for-byte; warmup time and instruction mix can differ. Desktop CPU/GPU scheduling and other whole-client effects remain part of the measurement. Consequently, the lower FPS is an observation under these conditions, not proof that either individual optimization causes a regression. The results do not support claiming that this patch improves VRChat FPS.

PresentMon reported lost ETW events. All selected rows had finite GPU busy/time metrics, with thousands of distinct values, but the warnings limit timing precision. Raw CSVs and warning logs are retained locally. This test does not cover headset/stereo rendering, remote multiplayer or emulated-game FPS.

`vrchat-performance.json` contains the aggregate and per-window values, source/bundle hashes, warmup counts and loss-warning counts. No client authentication logs, SDK bundle, payload or raw capture is distributed.

References: [PresentMon console/CSV definitions](https://github.com/GameTechDev/PresentMon/blob/v2.6.0/README-ConsoleApplication.md), [VRChat launch options](https://docs.vrchat.com/docs/launch-options), [local Build & Test](https://creators.vrchat.com/worlds/udon/using-build-test/).

To capture a sampling window after warmup (replace the PID/output path):

```powershell
PresentMon-2.6.0-x64.exe --process_id 12345 --timed 30 --terminate_after_timed --no_console_stats --session_name RvcSample1 --output_file sample1.csv
```

Repeat with separate output files/session names for three windows. Recompute the table values with Python's standard library:

```powershell
python summarize-presentmon.py sample1.csv sample2.csv sample3.csv
```
