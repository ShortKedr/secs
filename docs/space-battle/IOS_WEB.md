English | [Русский](IOS_WEB.ru.md)

# iOS Web Compatibility Investigation

Status snapshot: 2026-10-12. **The fix is NOT validated on an actual iPhone.**
This is a bounded compatibility/recovery investigation, not a new optimization
stage. Existing reports and measurement history remain unchanged.

## Evidence and Limits

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
