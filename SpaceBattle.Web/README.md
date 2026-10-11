English | [Русский](README.ru.md)

# SpaceBattle.Web

**[Run the live demo](https://shortkedr.github.io/secs/)** |
[Benchmark report](../docs/space-battle/REPORT.md) |
[Repository](../README.md)

## Runtime and Scenario

This is a real .NET 8 Blazor WebAssembly host for the unchanged
[SpaceBattle.Core](../SpaceBattle.Core), [SpaceBattle.SECS](../SpaceBattle.SECS)
and [SECS](../SECS) simulation with Aether.Physics2D. C# runs the simulation in
the browser; production uses AOT-compiled WebAssembly. JavaScript Canvas is a thin
renderer, not a JavaScript rewrite of the battle. Direct JS exports expose a
reused render buffer in WASM memory, without JSON snapshots or per-frame full
checksums. Debug/test checksums are computed only on explicit request.
Web host and renderer sources are in this directory.

The three existing churn profiles are low=0.5, nominal=1 and high=2, with
96 initial escorts and 3 carriers per team. Shared Core retains predictive
intercept aiming, deterministic spread and team-neutral projectile/debris
damage, including friendly and self hits. See the
[measured simulation systems](../docs/space-battle/SYSTEMS.md) for the historical rule details.
Current source uses [tactical AI `tactical-lock-v2`](../docs/space-battle/TACTICS.md):
kill-cost target locks, 2-4 ship focus groups and wounded-ship screening.
The benchmark report remains a `predictive-neutral-v1` snapshot, not a timing
measurement of this new AI. Updating local source does not publish the live demo.

## Publish and Serve Locally

From the repository root, use .NET SDK 8.0.425 (or a later 8.0.4xx patch,
as selected by [global.json](global.json)) and the
[publish helper](../tools/build-space-battle-web.ps1), which runs `dotnet publish`.
The pinned SDK needs the `wasm-tools` workload for AOT. CI installs it in its
ephemeral runner. A local workload installation outside this repository is a
user-managed prerequisite, not something the helper installs automatically.
`-Interpreter` is an explicit local-debug option, never used for Pages.
Its default `-OutputDirectory` is `Benchmarks.Local/tmp/web-publish`; its default
`-BasePath` is `/secs/`, the published GitHub Pages path:

```powershell
./tools/build-space-battle-web.ps1
```

For a local static server, publish with `-BasePath /` so assets resolve from the
server root. With Python installed, serve the published `wwwroot` directory:

```powershell
./tools/build-space-battle-web.ps1 -OutputDirectory Benchmarks.Local/tmp/web-publish -BasePath /
Set-Location Benchmarks.Local/tmp/web-publish/wwwroot
python -m http.server 8000
```

Open [the local demo](http://localhost:8000/) or
[its Russian interface](http://localhost:8000/?lang=ru). Republish with the
default `/secs/` base path for Pages; it is not the local server's root path.

## GitHub Pages

The [GitHub Actions workflow](../.github/workflows/space-battle-pages.yml)
builds and deploys the static demo to
[the `/secs/` Pages target](https://shortkedr.github.io/secs/).
The demo links back to the bilingual benchmark report. The Russian demo URL is
[`?lang=ru`](https://shortkedr.github.io/secs/?lang=ru).
The target link alone does not establish that a deployment has completed.

Actions runs real Chromium checks before deployment and retains desktop/mobile
screenshots as the `space-battle-browser-checks` artifact. It checks all profiles,
same-browser reset parity, combat counters, camera/selection controls, touch,
responsive layout and error recovery. Run the same checks locally:

```powershell
./tools/build-space-battle-web.ps1
$env:PLAYWRIGHT_BROWSERS_PATH = "$PWD/Benchmarks.Local/tmp/playwright-browsers"
npm ci --prefix tools/web --cache Benchmarks.Local/tmp/npm-cache
node tools/web/node_modules/playwright/cli.js install chromium
node tools/web/check-web.mjs --base /secs/
```

The published site's [license inventory](wwwroot/licenses/NOTICE.txt) and original
third-party notices are included in `wwwroot/licenses/`.

## Metrics and Reproduction

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
