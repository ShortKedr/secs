English | [Русский](MONO_COMPILER_STUDY.ru.md)

# Stable Mono AOT Compiler Study

<!-- research-colors:start -->
### Colour scale

![Lag up to 5%](../../docs/benchmarks/heatmap/best.svg "Lag up to 5%") up to 5% · ![Lag between 5% and 30%](../../docs/benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 15% · ![Lag from 30%](../../docs/benchmarks/heatmap/worst.svg "Lag from 30%") from 30% · ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](../../docs/benchmarks/COLORS.md).


Only selected metrics within one scenario/window/environment are compared. Measured comparisons stay grey when neither the table nor its linked summary supplies independent-repeat envelopes. Stage shares and gameplay counters are not colored.
<!-- research-colors:end -->

[Research index](../../docs/space-battle/RESEARCH.md) | [Current report](../../docs/space-battle/REPORT.md)

## Decision

2026-10-12: retain the original production configuration. No repeatable,
general improvement justified enabling O3 or LTO by default. This is a measured
negative result, not an assertion that these flags can never help another scene
or machine. C# simulation, SECS, physics, AI, balance, RNG and renderer are unchanged.
The local publish script now accepts a separate `-ArtifactsDirectory`; defaults
remain compatible. CI tests the new measurement tools, not performance thresholds.
No remote operation was performed and no historical report was overwritten.

## Inputs And Actual Pipeline

Windows 11 Pro, Ryzen 9 9900X (24 logical processors), approximately 61.5 GiB
visible RAM; Chromium 151.0.7922.34, Playwright 1.62.1, Node 24.14.0.
SDK **8.0.425**, Mono/runtime **8.0.31**, LLVM/Clang **16.0.5**, Emscripten
**3.1.34**. No CoreCLR/R2R, NativeAOT-LLVM or .NET 10 experiment was used.

The baseline was the dirty workspace frozen on 2026-10-12, not the published historical
benchmark or the live Pages version. Local HEAD: `2f3625a408625b74fa0b42edaac6a20a121ccca5`.
81 source/config/asset files were frozen before comparison. Source manifest SHA256:
`EAB44BC1B7D6B1B93C270E70267E4CBA8C0DF331E377526FCEAF5B2D30F876DB`.

Executed Mono commands include `--wasm-exceptions --debug --llvm` and
`--aot=no-opt,static,direct-icalls,deterministic,mattr=simd,...,llvmonly,interp,asmonly`.
The SDK AOT mode is `LLVMOnlyInterp`; 33 assemblies were AOT compiled. `no-opt`
does not mean the following LLVM stages have no optimization. Actual emcc/clang,
wasm-ld/Binaryen commands, response files and binlogs are retained separately for
each variant. No conflicting optimization flag, fast-math or newly disabled
exception/safety check was found.

| Variant | Native C files | AOT bitcode / final link | WASM MB | Brotli MB | Build seconds |
|---|---|---|---:|---:|---:|
| A | -Oz | -O2 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 9.847 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 2.295 | ![Lag up to 5%](../../docs/benchmarks/heatmap/best.svg "Lag up to 5%") 75.6 |
| B | -Oz | -O3 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 10.287 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 2.324 | ![Lag from 30%](../../docs/benchmarks/heatmap/worst.svg "Lag from 30%") 117.6 |
| C | -O3 | -O3 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 10.272 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 2.312 | ![Lag from 30%](../../docs/benchmarks/heatmap/worst.svg "Lag from 30%") 114.0 |
| D | -O3, -flto | -O3, -flto | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 10.262 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 2.311 | ![Lag from 30%](../../docs/benchmarks/heatmap/worst.svg "Lag from 30%") 144.9 |

MB is decimal. Build durations are single observations, not independent speed
benchmarks. Link optimization also controls emcc compilation of AOT bitcode;
compile optimization separately controls `driver.c`, `pinvoke.c`, `corebindings.c`.

D initially failed: SDK `llvm-size` cannot size retained bitcode objects.
Fixing initial heap to A's actual **16 MiB** exposed a second failure: the frozen
SDK cache lacks LTO system libraries. A separate copied cache was prepared with
the same Emscripten `embuilder.py build MINIMAL --lto` (87.5 seconds, excluding
copying). The final D build uses that cache; maximum heap remains **256 MiB**.
`-flto` was verified in native C, AOT-bitcode compilation and final link, with
bitcode object magic retained before linking. Prebuilt Mono runtime archives
still contain WASM objects, including 267 members of `libmonosgen-2.0.a`.
This is partial-scope ordinary LLVM LTO, **not whole-program runtime optimization**.
Both failed attempts are retained. These workarounds were not added to production.

## Measurement Method

72 primary fresh-process replays: four variants, low/nominal/high, three
independent repetitions, separately for core and frontend. Order rotates;
four variants over three repetitions leave bounded position imbalance (at most
one). Identical seed7331, LightSwarm roster, endless reinforcements and game
settings; colliders off, viewport1440x900. Each replay warms a separate1200-tick
battle, then resets. Core measures ticks1-1200, fixed300-tick phases; frontend
measures301-600 after another reset/prelude. Ticks are not independent repeats.

18 confirmation replays: A/C/D, low/high, three fresh processes each, balanced
three-variant order; separate3600-tick warmup then Reset, measured1-2400 with
600-tick phases. Builds, CPU profiling and throughput never overlap. Existing
user processes were left alone; background activity, power and temperature were
not controlled. Do not pool primary and confirmation windows or their warmups.

Step is existing C# header[5]; export is header[45]; `Advance(1)` wall time
includes export/interop. Subtraction remainders are estimates and can be negative
at timer precision. Frontend uses original `advanceTime(1000/60)` with binary
ingestion, DOM update and Canvas draw submission. Automatic app RAF is disabled
only in served measurement pages; no production file is modified. This prevents
stale frontend views after direct bridge.Reset and excludes background rendering.
This is client CPU-path cost, **not RAF pacing, GPU busy time or real FPS**.

Fresh browser process/context/page per replay avoids process-shared WASM code
caching. Cold runtime/app-ready timestamps start at navigation and exclude
browser launch/polling delay. OS caches are warm; local files are served
uncompressed, so startup is not a network-download benchmark. Initial startup
uses the same nominal battle before selecting the measured profile. Two startup
timestamps and an outside-timer binary-header getter are injected only in served
app.js; original/served hashes and raw browser/runtime identities are retained.

## Results

Median of three independent run means, with their min-max range in brackets;
milliseconds. p95/p99 are medians of each run's percentiles over the identical
1-1200 window, not percentiles pooled across runs or profiles.

| Profile | Variant | Step mean [range] | Step p95 / p99 | Export mean |
|---|---|---:|---:|---:|
| low | A | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 2.416 [2.379-2.920] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 4.4 / ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 5.5 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0.528 |
| low | B | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 2.794 [2.392-2.818] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 5.3 / ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 6.1 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0.621 |
| low | C | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 2.595 [2.360-2.836] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 5.2 / ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 6.1 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0.587 |
| low | D | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 2.315 [2.241-2.797] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 4.0 / ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 4.5 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0.511 |
| nominal | A | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 3.147 [3.142-3.278] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 6.2 / ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 7.4 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0.751 |
| nominal | B | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 3.129 [3.089-3.162] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 6.0 / ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 7.0 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0.781 |
| nominal | C | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 3.085 [3.060-3.578] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 5.9 / ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 6.7 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0.760 |
| nominal | D | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 3.083 [2.984-3.134] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 5.9 / ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 6.4 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0.740 |
| high | A | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 4.703 [4.488-4.717] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 9.6 / ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 13.8 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 1.202 |
| high | B | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 4.492 [4.285-5.165] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 9.1 / ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 11.2 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 1.184 |
| high | C | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 4.304 [4.246-5.248] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 9.3 / ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 11.9 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 1.143 |
| high | D | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 4.312 [4.259-4.456] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 8.7 / ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 12.4 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 1.127 |

| Variant | Client low [range] | Client nominal [range] | Client high [range] |
|---|---:|---:|---:|
| A | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 6.555 [6.525-7.126] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 5.626 [5.073-6.469] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 7.567 [7.547-8.153] |
| B | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 6.617 [6.505-6.681] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 5.387 [5.144-6.673] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 7.396 [7.299-8.189] |
| C | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 6.562 [6.450-6.700] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 5.316 [5.260-5.406] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 7.639 [7.314-7.668] |
| D | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 6.806 [6.646-7.154] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 5.402 [5.357-6.428] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 8.762 [7.568-9.130] |

Primary core runtime-ready medians: A335-344ms, B348-350ms, C343-367ms,
D343-356ms across profile groups; app-ready respectively536-541,544-559,
542-569,540-553ms. No confirmed startup improvement. Maximum linear-memory
capacity: A/B121,044,992bytes; C/D120,848,384bytes, not live allocation totals.
All individual cold samples/ranges, client/export percentiles, fixed-phase
p95/p99 and memory telemetry remain in the ignored JSON results.

| Confirmation | A Step [range] | C Step [range] | D Step [range] |
|---|---:|---:|---:|
| low1-2400 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 3.147 [3.043-3.200] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 2.998 [2.987-3.151] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 2.997 [2.958-3.042] |
| high1-2400 | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 4.200 [3.952-5.007] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 4.018 [3.967-4.952] | ![Neutral or insufficient separation](../../docs/benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 4.185 [3.872-4.943] |

D's primary high8.3% observed reduction becomes **0.35%** in confirmation,
with broad range overlap. C's confirmation reductions are4.7% low /4.3% high,
also overlapping; primary low went the other way. In confirmation high1-600,
A/C/D means are4.513/4.562/4.926ms; p99 are13.5/16.1/14.0ms. Thus aggregate
means cannot conceal dense-phase/tail regressions. No p-value or universal
causal speedup is claimed from these limited, variable observations.

## Named Mono CPU Profile

Separate Release/O2 build with `WasmNativeStrip=false`, `WasmEmitSymbolMap=true`;
never a throughput or publish candidate. Chromium CDP samples synchronous
`Advance(1)` including binary export, after separate warmup/Reset. Initial and
late captures contain3112 and2745 samples respectively. Percentages below are
**self leaf-sample counts**, not inclusive stack time or exact CPU-time shares.

| Attribution | Ticks1-1200 | Ticks1201-2400 |
|---|---:|---:|
| Own C#/SECS, including Capture/export | 44.5% | 46.2% |
| Named managed library bodies | 3.3% | 3.8% |
| mono_interp_* execution | 17.5% | 14.9% |
| Dispatch/runtime helpers | 26.5% | 26.0% |
| GC | 1.7% | 1.3% |
| Named interop leaves | 0.10% | 0.07% |
| Other JS | 0.4% | 0.2% |
| Unknown | 5.9% | 7.5% |

Costs include actual simulation/Capture work, `World.Get<T>_GSHAREDVT`, generic
value-type ABI wrappers, `do_jit_call`, memory clearing/copying and interpreter
execution. AOT does not imply an interpreter-free hot path. This is **not the
old CoreCLR85% result**. The broad helper category is not a measurement of
interface/delegate dispatch alone; named paths distinguish wrappers from library
bodies, but inlining prevents exact source-level attribution. Opaque small WASM
modules stay unknown; the main-module symbol map is not applied to them.
GC and direct interop leaves are not dominant here, but their inclusive callers
must not be summed or interpreted as marshalling cost. Prebuilt runtime code is
unaffected by the tested compile flag. No bounds-check/branch-miss claim is made.

## Equivalence And Reproduction

Primary:11,106 comparisons, zero mismatches. Confirmation:48 structural
comparison groups, zero mismatches. Checkpoint checksum, all sampled tick
counters/populations, visible composition, ship tactics and reinforcements match
exactly within this browser/runtime. This is not native/WASM bitwise proof.
The initial profile and primary attempt failed only from the diagnostic stale
RAF views; they are retained but excluded. Subsequent captures/replays are clean.

Local-only raw root: `Benchmarks.Local/tmp/mono-study-20261012`; primary and
confirmation roots: `Benchmarks.Local/tmp/mono-compiler-primary-20261012-02` and
`Benchmarks.Local/tmp/mono-compiler-confirm-20261012-01`. Raw files are deliberately
ignored and unavailable in a GitHub checkout; no broken Markdown links to them.

```powershell
# New ignored Study directory; configured project-local SDK8.0.425/wasm-tools.
./tools/web/build-mono-study.ps1 -Study Benchmarks.Local/tmp/my-mono-study -Variant Freeze
./tools/web/build-mono-study.ps1 -Study Benchmarks.Local/tmp/my-mono-study -Variant A
./tools/web/build-mono-study.ps1 -Study Benchmarks.Local/tmp/my-mono-study -Variant B
./tools/web/build-mono-study.ps1 -Study Benchmarks.Local/tmp/my-mono-study -Variant C
./tools/web/prepare-mono-lto-cache.ps1 -Study Benchmarks.Local/tmp/my-mono-study
./tools/web/build-mono-study.ps1 -Study Benchmarks.Local/tmp/my-mono-study -Variant D -PrivateLtoCache
node tools/web/mono-compiler-study.mjs --help
node tools/web/profile-mono-cpu.mjs --help
node --test tools/web/test-mono-compiler-study.mjs tools/web/test-profile-mono-cpu.mjs
./tools/web/test-build-mono-study.ps1
```

Build/cache scripts target the installed Windows8.0.31 packs and refuse reuse.
Set `PLAYWRIGHT_BROWSERS_PATH` to the existing project-local browser cache and
`TEMP`/`TMP` inside Benchmarks.Local before comparisons; no automatic download.
Pass the four sites, labels and the same frozen source root to the study CLI.
Confirmation uses the exported `parseArgs`/`runStudy` API with
`config.profiles=["low","high"]`, `config.modes=["core"]`,2400 ticks,
600 phase ticks,3600 warmup ticks and A/C/D sites. The captured primary harness
version is retained with its original hash.

Accepted: isolated artifacts, source/command audits, independent replays,
statistical/trajectory guards and named profiling tools. Rejected as defaults:
B/C/D, blind interpreter removal, fast-math and reduced safety. IL stripping,
compression and dependency removal were not CPU experiments; Brotli size is
reported only as loading cost. No worker experiment or CPU-saving claim was made.
Other hardware/browsers, production network cold start, actual RAF/GPU performance,
longer multi-wave runs, .NET10 Mono and precise opaque-thunk attribution remain
unmeasured. Results do not justify changing the stable production toolchain.

## Validation

Normal local publish passed with actual -Oz/-O2 response files. Browser checks
passed: three profiles, combat, reset parity, controls, responsive Canvas, realtime
pause/resume. Standard action-client screenshots at ticks180/360 and the ordinary
UI at tick600 were visually inspected: loaded assets, moving ships, impacts and
warp effect, colliders off, no page errors. Tick360: SelfHits0, friendlyFireHits3201,
debrisHits3975. These screenshots are validation, not throughput samples.

32 Node tests passed; one real-junction test skipped because the sandbox denies
creation (mock metadata rejection passed). Six Python tests, three PowerShell
parser checks and five pre-build path guards passed. Documentation checker:
58 bilingual documents,1606 local links/anchors, numeric/code parity. Runtime
source/config hashes still match the frozen input; only Web README links changed.
CI was edited locally, not dispatched.
