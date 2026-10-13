English | [Русский](COVER.ru.md)

# Current Gameplay Cover

[Project](../../README.md) | [Report](REPORT.md) | [SpaceBattle overview](README.md)

`cover.png` illustrates current gameplay from the production AOT web frontend
in headless Chromium. Only Canvas is captured; HTML controls and telemetry are
hidden without changing the viewport or camera. It is
not the frozen measured run, a benchmark, or evidence of realtime performance.
Historical `action.png` and `action.png.json`, their hash inventory, parity
records and all measurement files remain unchanged.

## Configuration And Local Capture

The authoritative parameters live in [cover-settings.json](cover-settings.json),
consumed by [cover.py](../../tools/space-battle/cover.py) and
[the browser capture](../../tools/web/capture-cover.mjs). Change that configuration to change
the scene; do not maintain a second set of capture constants in documentation
or workflow commands. The intended preset is seed 7331, 96 ships per team,
tick 180, 1600 x 1000 pixels and zoom 2. The settings file, not this description,
is authoritative. The profile is nominal; the current web host supports exactly
96 escorts per team. Other `ShipsPerTeam` values fail rather than being ignored.

Local reproduction uses Python, Node.js, Playwright Chromium and the pinned
.NET 8 SDK with its `wasm-tools` workload already available. Publish the same
production AOT web frontend and use the helper with the settings JSON; this
is a capture-only flow, with no benchmark suite or report analysis. Capture is
selected by simulation tick, not elapsed wall time: a slower renderer must still
reach the configured tick before saving. Fixed settings make the scene
repeatable; byte-identical PNGs across graphics drivers are not promised.

From the repository root (on Linux, run the PowerShell helper through `pwsh`):

```bash
python -m pip install -r tools/space-battle/cover-requirements.txt
(cd tools/web && npm ci && npx playwright install chromium)
pwsh -File tools/build-space-battle-web.ps1 \
  -OutputDirectory Benchmarks.Local/tmp/cover-web -BasePath /
python tools/space-battle/cover.py capture \
  --site Benchmarks.Local/tmp/cover-web/wwwroot \
  --source-commit "$(git rev-parse HEAD)"
python tools/space-battle/cover.py validate \
  --directory Benchmarks.Local/tmp/space-battle-cover --allow-dirty
```

On Windows PowerShell, run the build helper directly, run the npm commands from
`tools/web`, and pass the local HEAD hash as a literal `--source-commit` argument.
Use `PLAYWRIGHT_BROWSERS_PATH` consistently for browser installation and capture
when using a project-local browser cache. Uncommitted local previews are marked `SourceDirty`;
`--allow-dirty` is for local validation only and is never used by Actions.
Capture writes into `Benchmarks.Local/tmp/space-battle-cover`, not the documents.

## Validation And Publication

The [workflow](../../.github/workflows/space-battle-cover.yml) contract is a push
to `main` or `workflow_dispatch` on `main`. It builds and captures the actual
source commit using the real C# simulation and Canvas renderer of the AOT build.
It serves that build on loopback, not a potentially older live Pages deployment.
Native OpenGL, Xvfb and Mesa capture are no longer required. Chromium runs at
device scale factor 1; simulation stays paused outside explicit tick advancement.

The PNG and adjacent manifest are staged and validated as a pair before
publication. Validation checks the configured tick, dimensions and seed,
nonblank rendering and render counts, and compares recorded source provenance
against the actual captured source commit. [cover.png.json](cover.png.json) records
schema 2: source commit, Chromium version, AOT runtime mode, loaded web-build
file SHA256 hashes, settings, simulation checksum and render counts; these
describe this cover, not the historical benchmark. An invalid or mismatched
pair must not replace the published cover.

Seed, tick, nominal profile and zoom are checked against the actual browser state.
Loaded-file hashes cover WASM/boot resources, renderer modules, build identity,
the atlas manifest and all six generated PNG textures. The local validator
compares them with the exact served build and both manifests; original source
atlas PNG/JSON pairs are not loaded by the browser. External HTTP
resources are rejected. The activity gate requires at least 24 on-screen ships,
both teams still alive and at least one active projectile. It rejects an empty
or already finished battle, not a performance regression. Adjust the fixed tick
deliberately if gameplay changes make the preset fail. Raw browser timings remain
in the artifact; the cover manifest omits them and is not a benchmark report.

Failures retain diagnostics in the artifact and print the underlying browser
error in the job log; an absent PNG cannot silently become a successful update.

Only `docs/space-battle/cover.png` and `docs/space-battle/cover.png.json` are
publication outputs. A bot commit uses `GITHUB_TOKEN`, never a PAT. Commits made
with that token do not trigger other workflows, so the cover update does not
recursively run this workflow. Branch protection or repository
`contents: write` policy can block publication; the captured pair remains
available as a workflow artifact when publication fails.

Overlapping runs are superseded by the latest push. Before publication, the job
checks that `main` still points to the simulated commit; it skips a stale result.
The final push is fast-forward only, so a racing source push is never overwritten.
GitHub documents the [GITHUB_TOKEN recursion safeguard](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow).

This documents the intended automation contract, not a claim that a live
GitHub run or bot publication has been tested successfully.
