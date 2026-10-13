English | [Русский](DEVELOPMENT.ru.md)

# Web Engineering Guide

[Local launch and controls](README.md) | [SpaceBattle](../docs/space-battle/README.md)

## Runtime and Rendering Contract

Production uses stable .NET 8 Mono AOT with `WasmEnableSIMD=false`; local interpreter diagnostics remain supported. The current rules are listed in the [gameplay index](../docs/space-battle/README.md#current-gameplay). Weapon tuning and earlier V5/V6 validation records live in [Armament](../docs/space-battle/ARMAMENT.md); their checksums are revision-specific, not current expected values.

Hulls, mounting bases and gun heads use the same native team palette:
cyan RGB `(0.2, 0.85, 0.95)` and red `(1, 0.37, 0.4)`. Two tinted variants of
each ship/gun atlas are baked as PNG during the build; no runtime pixel readback
or color filter is added. Alpha, pivots, recoil, projectiles and warp artwork
are preserved. Orange overheat indicators remain distinct.

This is a real .NET 8 Blazor WebAssembly host for the unchanged
[SpaceBattle.Core](../SpaceBattle.Core), [SpaceBattle.SECS](../SpaceBattle.SECS)
and [SECS](../SECS) simulation with the shared [circle physics kernel](../docs/space-battle/PHYSICS.md).
Aether is no longer a production dependency. C# runs the simulation in
the browser; production uses AOT-compiled WebAssembly. JavaScript Canvas is a thin
renderer, not a JavaScript rewrite of the battle. Direct JS exports expose a
reused render buffer in WASM memory, without JSON snapshots or per-frame full
checksums. Debug/test checksums are computed only on explicit request.
Web host and renderer sources are in this directory.

Current gameplay adds [mounted armament `mounted-armament-v7`](../docs/space-battle/ARMAMENT.md):
independent turret entities and slow heavy hull cannons on large ships, rendered
in both clients. Binary frame v3 retains 16-double rows, uses an 80-double header,
and appends turret rows after particles. Header fields 63/64 count alive/visible
mounts; 65/66 count heavy/turret shots. Turret row fields 9/10/11 are parent
serial, recoil and normalized heat. All ECS totals include mounted entities.
Reserved header field 72 now identifies fleet composition (0 Uniform, 1 LightSwarm);
`FleetRevision()` identifies its rules without changing header/row dimensions.

Both clients share a [generated projectile/particle atlas](../SpaceBattle.Game/Assets/PROVENANCE.md#effects-atlas).
Whole projectile sprites rotate with actual velocity; particles retain lifetime
alpha on centered square quads, using their own square atlas cells. Native and
web keep the two sprite families separate and reject invalid particle cells.
The shared artwork has a controlled contrast lift, verified against
[fixed effect-only brightness budgets](../SpaceBattle.Game/Assets/PROVENANCE.md#contrast-checks).
Images replace primitive strokes/circles and texture squares without changing the
binary frame, physics, damage or gameplay revision.

## GitHub Pages

The [GitHub Actions workflow](../.github/workflows/space-battle-pages.yml)
builds and deploys the static demo to
[the `/secs/` Pages target](https://shortkedr.github.io/secs/).
The demo links back to the bilingual benchmark report. The Russian demo URL is
[`?lang=ru`](https://shortkedr.github.io/secs/?lang=ru).
The target link alone does not establish that a deployment has completed.

Finalized output directories are immutable; choose a fresh output/build directory
for another publication. Node.js and `tools/web` dependencies prepare the PNGs.

Actions runs real Chromium and bounded desktop WebKit checks before deployment and retains desktop/mobile
screenshots as the `space-battle-browser-checks` artifact. It checks all profiles,
same-browser reset parity, combat counters, camera/selection controls, touch,
responsive layout and error recovery. Run the same checks locally:

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH = "$PWD/Benchmarks.Local/tmp/playwright-browsers"
npm ci --prefix tools/web --cache Benchmarks.Local/tmp/npm-cache
./tools/build-space-battle-web.ps1
node tools/web/node_modules/playwright/cli.js install chromium webkit
node tools/web/check-web.mjs --base /secs/
```

The published site's [license inventory](wwwroot/licenses/NOTICE.txt) and original
third-party notices are included in `wwwroot/licenses/`.

## Metrics and Reproduction

The chart button in the footer opens live statistics (EN/RU); the compact footer
always shows total ECS entities, render FPS, real ticks/s and mean Step time.
Alive counts include docked ships and offscreen entities, not just visible sprites.
The panel separates ships/docked ships, projectiles, debris and particles, plus
physics bodies/fixtures, combat counters, native ECS/accessor memory, managed heap,
archetypes/chunks and runtime GC collection totals. Managed heap is not total
browser/WASM/process memory; native and accessor bytes are separate storage figures.

Tick mean, nearest-rank p95/p99, maximum and stage means use the latest 600 executed
ticks (up to 10 simulated seconds at 60 Hz), including initial warmup. They are
refreshed at most every 250 ms and retained on pause; reset clears the window.
These consecutive ticks are diagnostic samples, not independent benchmark runs.
Collection happens outside Core Step and does not alter its measured scope.
The binary header carries the metrics; no per-frame JSON serialization is added.

Real ticks/s counts only normal RAF-driven simulation over a wall-time window;
offline `advanceTime` does not inflate it. Pause/hidden tabs show zero ticks/s.
Render FPS is frame cadence, not tick throughput. Canvas CPU is smoothed CPU time
for Canvas drawing, not GPU time or the full frame; render export CPU is the last
Core capture/binary-buffer preparation, including throttled metric refresh when due.
The requested tick budget is `1000 / (60 * speed)` ms and its used percentage is
mean Core Step divided by that budget. It excludes rendering/export and does not
promise the requested speed. Backlog/discarded ticks expose the browser catch-up
limit; discarded time is not skipped gameplay state or an executed tick.

This demo runs SECS only; it is not the full five-framework comparison.
Browser timings describe the current browser, WebAssembly runtime, device and
execution conditions. Rendering, interop, warmup and tab scheduling can affect
the experience; browser readings are not interchangeable with the measured
desktop headless Step results or the separate OpenGL frontend profile.
No cross-platform checksum equality or realtime FPS guarantee is promised.

The [browser profiler](../tools/web/profile-web.mjs) separates Core Step,
render-buffer preparation, full frontend frames and realtime tick throughput.
With the local server above running, use:

```powershell
node tools/web/profile-web.mjs --url http://localhost:8000/ --warmup-ticks 600 --output Benchmarks.Local/tmp/web-performance.json
```

It uses nominal seed 7331 in a 1440x900 Chromium viewport. Change `--warmup-ticks`
to inspect other phases of the battle. The forced-UI frame samples update the HUD
every tick; the normal loop updates it less frequently. Compare on the same device,
browser and battle phase, without other browser checks running concurrently.

For the measured comparison and its limitations, read the
[report](../docs/space-battle/REPORT.md). To reproduce the desktop campaign,
follow the [benchmark methodology](../docs/space-battle/README.md) and
[implementation/build inventory](../Benchmarks.Local/README.md).
Publishing or running this demo does not regenerate or replace those measurements.
