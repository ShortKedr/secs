English | [Русский](IOS_WEB.ru.md)

# iOS Web Investigation and Fix Protocol

Status snapshot: 2026-10-12. **The fix is NOT validated on an actual iPhone.**
This is a bounded compatibility/recovery investigation, not a new optimization
stage. Existing reports and measurement history remain unchanged.

## Evidence and Limits

- The original, privately supplied Jetsam report identifies `iPhone17,3`,
  iOS `26.6.2`, 16 KiB pages and `vm-pageshortage`. Recorded WebContent usage
  was 263.828 MiB, lifetime maximum 358.547 MiB; the GPU process recorded
  69.422 MiB. System wired memory was 5.361 GiB and free memory 16.719 MiB.
  These figures neither identify this page's URL nor attribute wired memory
  to a process. Do not sum them into a page footprint or copy the raw log
  into the repository.
- A fresh live capture fetched 51 resources; all fetched boot-integrity
  entries matched. The captured app blob `3595...` matches local source at
  `2ca6446`; `74f1686` was the documentation HEAD at the investigation snapshot.
  HTTP `max-age=600` makes a past mixed-cache session possible, not proven.
  This capture does not establish what the failing phone session loaded.
- The old runtime created six stable Canvas atlas backings: 37,743,264 RGBA
  bytes, about 35.99 MiB. Original PNG decoding, transient readbacks and GPU
  copies were not included. Replacing those canvases with six direct PNG
  Images keeps the same logical decoded byte count; **36 MiB of physical RAM
  saved has not been demonstrated**. Logical bytes are not resident footprint.
- Offline published WASM analysis found 29,505 defined functions, a largest
  body of 56,991 bytes, one body above 40 KiB and no Asyncify imports/exports.
  Local before/after counts were 28,206/28,210, with the same 56,991-byte
  maximum; module size changed from 8,946,481 to 8,941,126 bytes. These are
  code-inventory facts, not compiler-memory measurements.
- [WebKit bug 304810](https://bugs.webkit.org/show_bug.cgi?id=304810) includes
  an Asyncify reproduction and compiler-allocation reports. That workload
  is not our diagnosis; absence of Asyncify imports/exports alone does not
  rule out every WebKit compiler issue. No function padding or compiler
  changes are part of this fix.

## Necessary Fix Scope

- Generate six content-addressed PNG replacements during the build and use
  direct Image sources instead of runtime Canvas atlas construction. Match
  ships to the actual old runtime palette and threshold -1 behavior; retain
  threshold 32 for the others, all crops/pivots and existing desktop artwork.
- Version resource URLs with build identity. Reject app/boot identity mismatch
  before Mono starts; reject an incompatible browser/bridge contract.
- Guard frame/render errors, pause and expose `diagnostics.error` as
  `{stage,message,...}` with visible load-state/Retry. Do not keep a failing
  RAF loop or retry endlessly. Handle `contextlost` where available; Retry
  reloads the page, creating a fresh Mono instance rather than a duplicate.
- Keep resize idempotent and refresh heap views after WASM memory changes.
  Shared simulation rules, ticks, fleet definitions and counters stay unchanged.

## Bounded Validation

Use fresh pages, nominal configuration and seed 7331. `?diagnostics=<mode>&test=1`
starts paused, with no diagnostic RAF loop. Requests are manual and bounded
to at most 120 ticks; do not stress the phone or intentionally reproduce a crash.

| Mode | Controlled operation | PNG loading |
|---|---|---|
| `simulation` | Step and export, no drawing | None |
| `render` | Draw frozen tick-24 state, no Step | Six Images |
| `minimal` | Draw frozen tick-24 image-less primitives, no Step | None |
| `normal` | Step, export and draw | Six Images |

Warm a separate battle with one sample, then reset: simulation warming for
simulation/minimal pages, normal warming for normal/render pages. Before each
render/minimal window, reset and prepare with
`diagnosticSample({mode:'simulation',ticks:24,renders:0})`; freeze tick and
checksum while drawing. The ordinary sample uses `ticks:1,renders:1`.
Pure simulation pages reject other sample modes.

The optional C# `DiagnosticStep` calls the same Step path without Snapshot;
report Step, export and JS Canvas submission separately. Do not label Canvas
submission as GPU time. Keep WASM linear, managed, JS heap, canvas backing and
decoded atlas memory separate; unavailable JS heap and GPU memory are unknown,
not zero. Three fresh pages per mode are independent runs; ticks within a
window are not. Check unchanged image loads over two resets, unchanged
`canvasResizes` at the same viewport, visible errors, paused state and Retry
reload/recovery. Diagnostic fault injection is not an actual RAF test; the
ordinary-query RAF fault case is separate.

Commands below are a protocol for the main validator, not evidence of execution.
Run from the repository root with an existing generated site; replace `<unique>`
with a new identifier for every report. Never overwrite previous evidence.

```powershell
node tools/web/run-ios-smoke.mjs --site Benchmarks.Local/tmp/ios-web-fix-20261012/after-site/wwwroot --browser webkit --ticks 24 --repeats 1 --faults --retry reload --raf-faults --output "Benchmarks.Local/tmp/ios-web-fix-20261012/smoke-webkit-24-<unique>.json"
node tools/web/run-ios-smoke.mjs --site Benchmarks.Local/tmp/ios-web-fix-20261012/after-site/wwwroot --browser webkit --ticks 60 --repeats 3 --output "Benchmarks.Local/tmp/ios-web-fix-20261012/diagnostics-webkit-60-<unique>.json"
```

Use `--browser chromium` for the corresponding Chromium run. The final target
is three fresh pages per mode at 60 ticks in each engine. Old-app before
measurement is handled separately by main; do not assume the old app supports
the new diagnostic API. Desktop WebKit 26.5 is **not real iOS**.

The verified desktop preview is loopback-only at `http://127.0.0.1:8062/`;
a phone cannot reach the PC's loopback. Device checks require a separately
authorized secure HTTPS test host (WebCrypto integrity needs a secure context).
These URLs are templates, not deployed endpoints:

```text
https://<authorized-test-host>/<site-base>/?diagnostics=simulation&test=1
https://<authorized-test-host>/<site-base>/?diagnostics=minimal&test=1
https://<authorized-test-host>/<site-base>/?diagnostics=render&test=1
https://<authorized-test-host>/<site-base>/?diagnostics=normal&test=1
```

Open one mode at a time; keep it paused. From Safari Web Inspector, capture
state and only issue bounded samples. For frozen modes prepare tick 24 as
above. Stop on an error, navigation/reload or memory-pressure symptom; save
the time and state rather than retrying repeatedly.

## Private Device Evidence

To investigate wired-memory ownership, request existing, consented evidence
near the incident: private sysdiagnose and Instruments VM/process/GPU traces,
exact time/timezone, URL/navigation history and app/build/Safari/iOS versions.
Correlate Safari Web Inspector state/network/console with WebKit WebContent/GPU
crash logs, watchdog reports and any panic/respring records. Missing logs or
timestamps mean attribution remains unknown. Do not request an intentional
crash; keep raw logs private and outside Git, sharing only redacted summaries.

## Validation Ledger

Production Mono AOT built/run. Full Chromium checks pass, including reload
recovery, all three profiles, textures/effects, reset, zoom, follow and collider
debug. Both engines pass 12 fresh pages (3 per mode), 32 state comparisons,
synthetic draw/context-loss faults and recovery. Separate real RAF/null-context/
mixed-cache fault tests pass in both engines. No errors escaped to pageerror.
The unchanged action client reached tick360/checksum `599E654BA6B2097B`;
desktop/mobile WebKit and Chromium battle screenshots were inspected.

All three 600-tick profile checksums/counters match the frozen pre-fix build:
nominal `B5D2A00D96AA1C33`, low `25A5FEBDF96C9F7D`, high `5E5B41175780CD28`.
The 57 simulation-source hashes and desktop artwork are unchanged. This does
not establish real-iOS recovery or bitwise determinism between different engines.

Paired local builds, nominal/7331, 390x844/DPR2: three fresh pages per variant,
alternating order. One warm tick on a discarded battle, then 60 single ticks;
separate frozen tick24, 12 warm and 60 draw calls. Medians of page means with
min-max ranges (ms); samples within a page are not independent trials.

| Engine | Variant | Step | Snapshot C# | Snapshot + interop/accept | JS Canvas submission |
|---|---|---|---|---|---|
| Chromium | Before | 3.563 [3.545-3.868] | 0.433 [0.427-0.465] | 0.445 [0.440-0.468] | 4.388 [4.292-4.980] |
| Chromium | PNG | 3.672 [3.520-3.818] | 0.468 [0.468-0.498] | 0.452 [0.442-0.533] | 3.997 [3.798-4.218] |
| WebKit 26.5 | Before | 4.667 [4.650-4.783] | 0.683 [0.583-0.817] | 0.700 [0.617-0.733] | 6.017 [5.267-6.117] |
| WebKit 26.5 | PNG | 4.800 [4.733-5.200] | 0.683 [0.667-0.750] | 0.683 [0.600-0.717] | 5.267 [5.200-5.700] |

Snapshot+interop includes capture; columns are not additive. Renderer medians
decrease 8.9%/12.5%, but WebKit ranges overlap; Step is not accelerated and these
short windows do not prove sustained phone FPS. Source types change CANVAS6 ->
IMG6, while logical RGBA stays 37743264 bytes. WebKit tick24 pixel comparison
has maximum channel delta2; Chromium minification differs visibly at tiny sizes
(17550/741000 compared pixels exceed delta8). Crops/pivots/palette/alpha policy
are preserved, not exact raster filtering across source types/backends.

Memory is not a confirmed win: synchronous paired probes ended at linear
95 -> 114 MiB in both engines with almost identical managed readings ~24.575 MiB.
Two separate instrumented growth/reset/export traces reached 95 MiB in both
variants (ready79.125 MiB); growth occurs in coarse ~20% steps. Additional
repeated-reset diagnostic pages reached137 MiB. These are different controlled
paths, not physical footprint; the cause of the extra step in the paired probe
remains unisolated. No forced GC, smaller heap, object limits or compiler padding
was introduced to manufacture a memory result. GPU/system/wired ownership
remains unknown; real device evidence is still required.

| Evidence | Status | Result |
|---|---|---|
| Chromium controls/replay/assets | PASS | Same 3 profile outcomes |
| Chromium isolated modes | PASS | 3 fresh pages per mode; 32 state comparisons |
| Desktop WebKit isolated modes | PASS | 3 fresh pages per mode; 32 state comparisons |
| Real RAF stop / missing context / mixed cache | PASS | Both desktop engines, one visible fatal error |
| Actual iPhone recovery/memory behavior | Not validated | No device run; no system-memory attribution |

Local evidence root: `Benchmarks.Local/tmp/ios-web-fix-20261012/` (ignored).
Relevant existing locations are `deployed/inventory.json`, `source-before.json`,
`deployed-wasm-code.json`, `before-wasm-code.json`, `after-wasm-code.json`,
`before-site/`, `after-site/`, `browser-checks-final/` and
`browser-checks-final.txt`. Keep new diagnostic JSON/logs alongside these in
fresh paths; publish only necessary redacted conclusions and verified tables.
