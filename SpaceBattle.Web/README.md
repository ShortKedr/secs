English | [Русский](README.ru.md)

# SpaceBattle Web

A browser view of the shared C# fleet simulation running on SKECS. Production uses .NET 8 Mono AOT WebAssembly; Canvas draws the scene. Ships fight autonomously while you control the camera and speed.

[Live demo](https://shortkedr.github.io/skecs/) | [Russian demo](https://shortkedr.github.io/skecs/?lang=ru) | [SpaceBattle overview](../docs/space-battle/README.md) | [Current report](../docs/space-battle/REPORT.md)

The live URL is the deployment target. Local source changes do not update it, and its build may differ from this checkout.

## Run Locally

Requirements: Node.js, Python for the static server, the .NET 8.0.4xx SDK selected by [global.json](global.json), and its `wasm-tools` workload. The helper uses the installed SDK; it does not install a workload.

From the repository root:

```powershell
npm ci --prefix tools/web --cache Benchmarks.Local/tmp/npm-cache
$publish = "Benchmarks.Local/tmp/web-local-" + [guid]::NewGuid().ToString("N")
./tools/build-space-battle-web.ps1 -OutputDirectory $publish -ArtifactsDirectory ($publish + "-build") -BasePath /
python -m http.server 8000 --directory "$publish/wwwroot"
```

Open [localhost:8000](http://localhost:8000/) or [its Russian interface](http://localhost:8000/?lang=ru). Choose a fresh output/build directory for another build. `-Interpreter` is available for local diagnostics; performance comparisons use production AOT.

## Controls

| Action | Input |
| --- | --- |
| Pause / resume | Space or pause button |
| Restart | Reset button; changing seed or profile also resets |
| Speed | Speed buttons |
| Pan / zoom | Drag the scene / mouse wheel; touch pan and pinch |
| Fit arena | Fit button |
| Select a ship | Click / tap |
| Follow selected ship | C or crosshair button |
| Toggle colliders | F2 or dashed-square button |
| Statistics | Chart button in footer |
| Fullscreen | F or fullscreen button |
| Language | EN/RU button or `?lang=ru` |

Manual pan/zoom, Fit, deselection and reset cancel follow. Camera tools and collision outlines do not affect combat. The default fleet has 96 escorts and three carriers per side, with endless corner reinforcements; see [current gameplay](../docs/space-battle/README.md#current-gameplay).

The **low**, **nominal**, and **high** profiles set combat activity to 0.5×, 1×, and 2×. The multiplier changes weapon cooldowns, carrier production intervals, and boost duration. It does not directly set the number of ships or double the actual workload: combat outcomes change as the battle runs. Changing profile starts a new battle.

The footer and statistics panel describe this browser session. Render FPS, ticks/s, Step time and memory have different scopes; they are not interchangeable with desktop headless measurements.

## Development and Compatibility

The [engineering guide](DEVELOPMENT.md) covers the bridge/render buffer, statistics definitions, browser checks, profiling and existing Pages workflow. [Compiler](../tools/web/MONO_COMPILER_STUDY.md) and [locality](../tools/HardwareStudy/LOCALITY_OPTIMIZATION.md) studies retain their own dated configurations and results.

The [iOS investigation](../docs/space-battle/IOS_WEB.md) remains unverified on an actual iPhone. Desktop WebKit smoke checks are a separate, bounded check.
