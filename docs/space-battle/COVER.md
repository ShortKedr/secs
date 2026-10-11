English | [Русский](COVER.ru.md)

# Current Gameplay Cover

[Project](../../README.md) | [Report](REPORT.md) | [Benchmark methodology](README.md)

`cover.png` illustrates current gameplay from the native OpenGL frontend. It is
not the frozen measured run, a benchmark, or evidence of realtime performance.
Historical `action.png` and `action.png.json`, their hash inventory, parity
records and all measurement files remain unchanged.

## Configuration And Local Capture

The authoritative parameters live in [cover-settings.json](cover-settings.json),
consumed by [cover.py](../../tools/space-battle/cover.py). Change that configuration to change
the scene; do not maintain a second set of capture constants in documentation
or workflow commands. The intended preset is seed 7331, 96 ships per team,
tick 180, 1600 x 1000 pixels and zoom 2. The settings file, not this description,
is authoritative.

Local reproduction uses Python, .NET 8 and the real native renderer under Linux
Xvfb/Mesa. Build the frontend and use the helper with the settings JSON; this
is a capture-only flow, with no benchmark suite or report analysis. Capture is
selected by simulation tick, not elapsed wall time: a slower renderer must still
reach the configured tick before saving. Fixed settings make the scene
repeatable; byte-identical PNGs across graphics drivers are not promised.

From the repository root, after building the frontend into the same isolated
artifact directory as the workflow:

```bash
python -m pip install -r tools/space-battle/cover-requirements.txt
xvfb-run -a -s "-screen 0 1920x1200x24 +extension GLX +render -noreset" \
  python tools/space-battle/cover.py capture \
  --assembly Benchmarks.Local/tmp/cover-build/bin/SpaceBattle.Game/release/SpaceBattle.Game.dll \
  --source-commit "$(git rev-parse HEAD)"
python tools/space-battle/cover.py validate \
  --directory Benchmarks.Local/tmp/space-battle-cover --allow-dirty
```

On Windows, omit the `xvfb-run` prefix and pass the local HEAD hash as a literal
`--source-commit` argument. Uncommitted local previews are marked `SourceDirty`;
`--allow-dirty` is for local validation only and is never used by Actions.
Capture writes into `Benchmarks.Local/tmp/space-battle-cover`, not the documents.

## Validation And Publication

The [workflow](../../.github/workflows/space-battle-cover.yml) contract is a push
to `main` or `workflow_dispatch` on `main`. It builds and captures the actual
source commit using the real OpenGL frontend under Linux Xvfb/Mesa, not a mock
renderer or the browser demo.

The PNG and adjacent manifest are staged and validated as a pair before
publication. Validation checks the configured tick, dimensions and seed,
nonblank rendering and render counts, and compares recorded source provenance
against the actual captured source commit. [cover.png.json](cover.png.json) records
the source commit, compiled-binary SHA256 hashes, seed, simulation checksum and render counts; these
describe this cover, not the historical benchmark. An invalid or mismatched
pair must not replace the published cover.

Seed is recorded from the exact native launch arguments, because the raw native
sidecar has no seed field. The activity gate requires at least 24 rendered ships,
both teams still alive and at least one active projectile. It rejects an empty
or already finished battle, not a performance regression. Adjust the fixed tick
deliberately if gameplay changes make the preset fail. Raw native timings remain
in the artifact; the cover manifest omits them and is not a benchmark report.

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
