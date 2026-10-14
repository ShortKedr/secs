English | [Русский](README.ru.md)

# Scene Search And Video Capture

[SpaceBattle overview](../../../docs/space-battle/README.md) | [Research index](../../../docs/space-battle/RESEARCH.md)

Reusable tools for three real SpaceBattle moments: coordinated squad kills,
lethal HeavyCannon hits, and corner warp arrivals followed by a dense two-sided
battle. They use the current C# / SECS simulation, not scripted combat.

## Quick Start

Run from the repository root in PowerShell 7. Requirements: the pinned .NET 8
SDK with `wasm-tools`, Node.js, Chromium from `tools/web`'s pinned Playwright,
and `ffmpeg` / `ffprobe` on PATH. The original validation used SDK 8.0.425, Mono 8.0.31 and
Chromium 151.0.7922.34. No SDK or browser is installed automatically.

For a fresh dependency setup, keep caches inside the project:

```powershell
npm ci --prefix tools/web --cache Benchmarks.Local/tmp/npm-cache
$env:PLAYWRIGHT_BROWSERS_PATH = Join-Path $PWD 'Benchmarks.Local/tmp/playwright-browsers'
npm exec --prefix tools/web -- playwright install chromium
```

Search:

```powershell
./tools/space-battle/find-scenes.ps1 -WorkDirectory Benchmarks.Local/tmp/scene-tools -Profiles nominal,high -Seeds 7331 -Ticks 4200
```

The first call prepares an isolated production/probe Mono AOT pair using
[prepare-scenes.ps1](../prepare-scenes.ps1). Later calls reuse it after checking
source and build hashes. An existing interrupted directory or changed source
requires a new `-WorkDirectory`; completed builds are never overwritten.
Use `-DotnetExecutable` to select an existing SDK. The project-local
`Benchmarks.Local/tmp/dotnet-aot/dotnet.exe` is preferred when present.

The command prints the fresh `search.json` path. It contains ranked episodes,
exact causal evidence, resolved game configuration, source/build identity,
independent replay checkpoints, search budgets and missing event categories.
`-OutputDirectory` selects a fresh ignored search directory. Defaults are two
profiles, one seed, 4200 ticks, three candidates per category.

Capture the first ranked heavy episode from that file:

```powershell
./tools/space-battle/capture-scene.ps1 -Search Benchmarks.Local/tmp/scene-tools/search-example/search.json -Type heavy -Rank 1 -OutputDirectory Benchmarks.Local/tmp/scene-tools/video-heavy
```

Replace `search-example` with the printed directory. `-Type group` and `-Type warp`
select the other categories. Use `-Rank 2` for another candidate, or `-Id` for an
exact episode. `-Slowmo 2` repeats each exact heavy-scene frame twice; it does not
change simulation time, rules or the outcome. `-Annotate 0` gives clean footage.
Defaults: 1920x1080, 60 fps, H.264/yuv420p MP4 and silent AAC. Width/height must
be even. `-Ffmpeg` / `-Ffprobe` accept explicit executable paths.
Video frames use JPEG quality 0.97 before H.264 encoding to reduce PNG readback
cost. This adds lossy compression, not different simulation rules. Use
`-FrameCodec png` for lossless intermediate frames; keyframes and pixel evidence
remain PNG in either mode.

Group captures use `group-cinema-v2`: a separate pre-recording survey plans smooth
framing around the selected contributors and victim, not every changing AI target.
Position uses two triangular filters (36/18 ticks); missing focus holds for 30 ticks
and fades over 60. Anticipatory log-zoom changes by at most a factor of 1.25 per
second and keeps surveyed live focus inside the safe frame without emergency cuts.
This offline look-ahead affects presentation only. `groupCameraPolicy` and the exact
per-frame camera in the manifest record the policy; heavy/warp cameras are unchanged.

Every capture has a fresh directory containing `capture.mp4`, `manifest.json`
and up to 16 keyframes. The manifest maps each video frame/PTS to an integer tick,
records the camera, hashes, browser version, replay/removal checks and complete
FFprobe/decode results. A failed capture keeps `failure.json` and any partial
output, not a supposedly completed video. Nothing is published or committed.

## Detection And Verification

- Group: 2-4 current small-class members share a target; at least two contribute
  actual shield/hull damage, with a matching lethal hit and destruction. Changing
  roster/leader/target invalidates old contributions. Proximity is not proof.
- Heavy: the projectile birth, relative-motion CCD hit and actual destruction
  agree by serial and entity generation. The actual victim may differ from the
  intended target. Birth ownership can identify a shooter that has since died.
- Warp: explicit gate and arrival events identify the wave, including births
  sharing a tick with ordinary production. Arrived participants must hit/kill an
  enemy; return fire and a local battle are required. Default qualifying density
  is 24 ships per side and 100 projectiles within radius 110 around a participant
  hit, with 16 arrivals, 12 hostile hits and one confirmed kill. These are scene
  selection heuristics, not balance settings or proof of artistic quality.

The diagnostic logger is injected into a frozen source copy only. It logs current
post-AI rosters, projectile births, exact resolved hits, lifecycle removals and
warp arrivals. Unique source anchors must match; a changed/ambiguous anchor fails
closed instead of guessing where to insert code. Its allocations/timings are
not simulation throughput measurements.

The search repeats diagnostic evidence, then requires identical checksums,
counters, populations and team counts in the unmodified production build of the
same snapshot and browser. Capture independently repeats these checkpoints again,
checks actual victim/projectile removal and verifies unchanged production bytes.
Checksums are bounded same-environment evidence, not full snapshots or a claim
of native/WASM bit-identical behavior. Videos use the production Canvas/assets;
camera motion, brackets and timeline annotations are presentation only.

## Limits And Failure Cases

Search is limited to eight seeds, twelve profile/seed pairs, 18000 ticks per pair
and 216000 scouting ticks total. Verification replays are accounted separately.
Candidates are capped at twelve per category; histories, waves, paths and shot
tracking have hard budgets. Exhausting a budget fails, rather than returning
partial causal proof. Missing categories are reported, never manufactured by
changing fleets, balance, RNG or tick rate. Short scans can have truncated tails.

Capture is limited to 180 seconds / 10800 output frames. Only the exact Chromium
version recorded in the search is accepted. After a browser update, repeat the
search; after a game/source update, prepare a new work directory. Three categories
are not guaranteed for every seed. Ranking is heuristic; visually inspect the
keyframes and final clip. Long early-deployment lead-ins and conservative roster
checks can be less cinematic or miss valid collaborations.

Own ephemeral localhost servers and browsers are closed; existing preview
processes are not touched. Outputs, temporary browser profiles and build caches
stay under ignored `Benchmarks.Local`. Historical reports and measurements are
not regenerated. For custom criteria use the Node entry points
[find-scenes.mjs](find-scenes.mjs), [capture-scene.mjs](capture-scene.mjs) and the
pure [candidate detector](scene-candidates.mjs), not changes to the simulation.

Tests:

```powershell
node --test (Get-ChildItem tools/space-battle/scenes/test-*.mjs).FullName
```
