English | [Русский](README.ru.md)

# Space Battle Frontend

[Benchmark report](../docs/space-battle/REPORT.md) |
[Repository overview](../README.md) |
[Asset provenance](Assets/PROVENANCE.md)

Interactive Silk.NET/OpenGL 3.3 frontend for `Battle<SecsStore>`. The default
configuration starts 96 escorts per team plus carriers (198 total), all controlled
by AI. Use `--ships 2048` for the 4096-escort stress configuration.

Controls: Space pauses, +/- changes simulation speed, R restarts the seeded
battle, Home fits the arena, WASD/arrows pan, middle/right drag pans, wheel zooms
at the cursor, left click selects a ship. The top HUD controls are clickable.
F3 or the bar-chart icon toggles detailed simulation statistics; `--statistics`
opens the panel on launch, including for automated captures.

## Live Statistics

The footer also shows physics bodies/fixtures, archetypes/chunks, native ECS and
accessor storage bytes, and managed heap in MiB, sampled every 250 ms outside Step.
Managed heap is not total process memory. Storage figures are separate, not added
to an invented total. The first frame has no prior render CPU sample and shows `--`.

The always-on summary separates alive ECS entities (ships, including docked
ships, projectiles, debris and particles) from the render snapshot. Capture JSON
also separates alive ships, docked ships, snapshot ships and submitted ships;
submitted ships pass the camera bounds check, not a pixel visibility test.
Team heat/shield averages cover undocked snapshot ships only.

Tick last/mean/p95/p99/max and per-stage means use the shared, bounded rolling
window of at most 600 ticks. Samples are recorded after Step, without sample
allocations; percentile/stage snapshots refresh at most every 250 ms. Restart
clears history. This is live telemetry, not an independent benchmark.
The detailed panel shows sample count, stage means and the mean percentage of
the `1000 / (TickRate * speed)` ms simulation budget. Wall ticks/s counts only
real-time Update ticks, excluding preload, capture fast-forward and profiling;
it is zero on pause. Render FPS remains independent of simulation pause.
Render CPU is elapsed scene/HUD generation and GL submission time for the
previous draw, not GPU time or present time. Rate windows restart after capture
so PNG/readback stalls are excluded from subsequent live rates.

For isolated-build validation, capture tick 300 with `--statistics --validate-controls
--validate-render --width 960 --height 540`, then repeat at 1440 x 900 and
3840 x 2160. Toggle the panel and check selection/controls at each size.
The diagnostic JSON includes the statistics snapshot, count invariants and
bounded-window validation; control checks cover pause, reset and both toggles.
`--hidden --validate-controls --validate-live-statistics` separately checks the
real Update/Render loop, positive tick/FPS rates, paused render cadence, history,
speed-dependent budget and reset, then exits automatically. It must run without
capture or replay profiling. OS window bounds may clamp a requested large size;
the capture JSON records actual framebuffer dimensions.

`Assets/ships-atlas.png` is optional during development and copied on build.
The transparent atlas has four columns and three rows, row-major classes 0-10;
cell 11 is reserved. `ships-atlas.json` supplies measured alpha bounds plus four
pixels of transparent padding for the current nonuniform 1448 x 1086 image.
Aspect ratios are preserved; alpha values below 33 are discarded to remove faint
generated background residue. Without a manifest, uniform 4 x 3 cells are used.
Without the image, the HUD labels the procedural fallback.
Atlas ships are team tinted; the sprite nose should point toward the top of its
cell. Shields, health, heat, trails and effects are GPU triangle batches.

Run the built executable normally for a visible window. Automated captures use
a hidden OpenGL window unless `--visible` is passed:

```powershell
SpaceBattle.Game.exe --capture docs/space-battle/tick-900.png --capture-tick 900 --exit-after-capture --validate-render --visible
```

Run from the repository root; capture paths must stay inside that working
directory. Tick fast-forward runs before rendering
and is not charged to the wall-time accumulator. The PNG is read from the actual
OpenGL framebuffer; its adjacent JSON contains entity counts and nonblank pixel
statistics. Capture metadata also records the battle checkpoint checksum as a
16-digit hex string and SHA-256 hashes of the loaded Core, Game and SECS adapter assemblies
for provenance. Repeat with ticks 0, 900 and 3600. `--ships-per-team`, `--seed`,
`--width` and `--height` customize a run. `--zoom` sets the initial runtime/capture
zoom (finite values from 0.25 through 24, default 1); Home/Fit still fits the arena.
For a denser action capture use `--ships-per-team 256 --zoom 2 --width 1600 --height 1000`.
The process exits after capture only when
requested. P99 is the last 600 simulation ticks, not a benchmark measurement.
`--validate-controls` exercises the runtime command logic before fast-forward:
pause/resume, speed, pan, cursor-anchored zoom, fit, selection and restart. This
does not inject OS keyboard/mouse events or replace a manual input smoke test.
Projectiles and debris are team-neutral (`RenderEntity.Team = -1`). Projectile
colors encode weapon kind only, never allegiance. The HUD displays total live
projectiles and cumulative shots globally; per-team rows show alive ships, heat
and shields. No per-team projectile count or friendly-fire immunity is inferred.

## Frontend Profiling

After building, run from the repository root:

```powershell
SpaceBattle.Game/bin/Release/net8.0/SpaceBattle.Game.exe --profile-frames 3600 --profile-warmup 120 --capture-tick 900 --ships-per-team 96 --profile-output docs/space-battle/frontend.json --profile-csv docs/space-battle/frontend.csv --hidden
```

This is an **uncapped deterministic fixed replay**, not a real-time game FPS
claim. VSync and both Silk rate caps are disabled. Each rendered frame runs
exactly one Step, then one Battle.Capture. Fast-forward uses `--capture-tick`
without requiring `--capture`. The first 120 frames are warmup by default; only
the following requested frames contribute samples. The process closes itself
after collecting the final wall interval and writing the report. Profiling and
PNG capture are mutually exclusive. Run a separate action capture after profiling.

`--hidden` suppresses the window in any mode. Interactive Update and controls
remain unchanged outside profiling. In profile mode, input cannot alter the
replay or camera; Escape can abort. `--validate-controls` may exercise command
logic before replay starts, outside timing. Frame/warmup counts are bounded to
one million, measured count must be positive. `--profile-csv PATH` is optional.
All output paths must remain under the current project directory.

CPU phase values are host elapsed Stopwatch timings, not hardware CPU-cycle
counters. SimulationCPU covers Step (live statistics are excluded in profiling); SnapshotCaptureCPU
covers only Battle.Capture. RenderSubmissionCPU covers scene/HUD generation,
telemetry queries, title update, and GL clear/upload/draw submission. It excludes
simulation, snapshot capture and GPU query bookkeeping. TotalCPUFrame spans
Render callback entry through query/error bookkeeping after drawing, excluding
sample storage and the automatic swap after the callback. It includes async
query polling. These phases can include driver stalls; they are not GPU timings.

WallFrameInterval runs from Render callback entry to the next entry. It includes
Silk automatic present, event processing and bookkeeping. The final interval is
collected in one additional callback **without an extra Step or draw**. Its FPS
and 16.67 ms miss rate describe fixed-replay wall throughput only. Exact present
CPU time and VSync/present wait remain JSON null and CSV empty: the code never
subtracts GPU time from CPU time to estimate present.

GPU timing uses GL_TIME_ELAPSED from before Clear through the final HUD draw.
Its elapsed GPU timeline may include idle gaps while CPU geometry is generated;
it is not pure GPU busy utilization. A preallocated 32-query ring polls
QueryResultAvailable and reads 64-bit nanoseconds only when ready, without a
per-frame blocking result read. Busy-ring frames or unsupported/zero-width timer
queries are null, not fabricated zero timings. Supported samples that really
report zero remain identifiable as ZeroSamples. All-zero/no-positive results on
a supported timer fail validation rather than claiming success.

All sample storage and query ownership are allocated before frame timing. JSON,
CSV, percentiles and assembly hashes are generated only after timing. A single
GL.Finish drains pending tail queries after the final measured wall interval;
that drain is excluded from every reported timing. Queries are deleted before
disposing the GL context. JSON records per-phase min/mean/p50/p95/p99/max,
nearest-rank percentiles, wall throughput, CPU/wall deadline misses, GPU sample
and null counts, GL errors, driver/vendor/version, complete battle config,
assembly SHA-256 hashes and the final battle checkpoint. The optional CSV has
one row per measured frame with all timing columns, misses and entity counts.
