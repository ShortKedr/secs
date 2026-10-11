English | [Русский](README.ru.md)

# Experimental CoreCLR Web Host

[Production Mono AOT host](../SpaceBattle.Web/README.md) | [Space Battle report](../docs/space-battle/REPORT.md)

This alternative links the same C# simulation, SECS adapter, binary bridge,
Canvas renderer and artwork. It does not rewrite gameplay in JavaScript.
Desktop and the existing Pages workflow remain unchanged.

## Pinned Toolchain

Windows x64, PowerShell 7, .NET SDK `11.0.100-rc.1.26425.128`, runtime and
crossgen2 `11.0.0-rc.1.26425.128`, the matching `wasm-tools` workload and Node 24.
The static root generator uses build-only Mono.Cecil `0.11.6` (MIT).
All SDK files, workload packs, NuGet caches and generated artifacts stay under
`Benchmarks.Local`, which is ignored by Git.

Microsoft calls [browser CoreCLR experimental](https://github.com/dotnet/runtime/blob/ab19415702aa8139d5369e47c73edb47343c34ad/docs/workflow/building/coreclr/wasm.md).
The [R2R diagnostics issue](https://github.com/dotnet/runtime/issues/132410)
also documents unresolved tooling problems; it is not a speed guarantee.
Current runtime `main` targets a newer development release, so this project
does not silently copy its targets into the pinned .NET 11 SDK.

Download the Windows x64 SDK ZIP listed in the official
[.NET 11 release manifest](https://github.com/dotnet/core/blob/main/release-notes/11.0/releases.json),
verify its SHA-512 against that manifest, and extract it into
`Benchmarks.Local/tmp/dotnet11-coreclr`. From the repository root:

```powershell
$root = $PWD.Path
$env:DOTNET_ROOT = "$root/Benchmarks.Local/tmp/dotnet11-coreclr"
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_CLI_HOME = "$root/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$root/Benchmarks.Local/nuget-packages"
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& "$env:DOTNET_ROOT/dotnet.exe" workload install wasm-tools --skip-manifest-update --temp-dir "$root/Benchmarks.Local/tmp/dotnet11-workload-downloads"
./tools/build-space-battle-coreclr.ps1 -NodeExecutable 'C:/Program Files/nodejs/node.exe'
```

The helper refuses existing output directories and SDK version drift. Use a
different `-OutputDirectory` for each comparison. `-Interpreter` builds an
explicit IL-only control; `-AllowPartial` bypasses the hot-code gate for
diagnostics and must not be described or deployed as a verified AOT upgrade.
Direct `dotnet publish` is an interpreter control, not the R2R procedure.

## Compilation And Evidence

RC1's ordinary SDK rejects `PublishReadyToRun=true` for `browser-wasm` with
`NETSDK1095`. The dedicated [helper](../tools/build-space-battle-coreclr.ps1)
instead publishes and trims IL, runs the pinned crossgen2 with `-O`,
`--targetos:browser --targetarch:wasm --obj-format:wasm`, and stages its outputs
using the SDK's existing `R2RWebcilPath` asset metadata hook.
The SDK generates resource fingerprints, SHA-256 integrity, Webcil image/table
sizes and compression. No hand-edited runtime pack or boot-manifest rewrite
is needed. Compilation and staging use the identical final DLL hashes.

The [static root generator](../tools/CoreClrR2RRoots/Program.cs) reads the final
Core IL and substitutes `SecsStore` for the battle's store type. Its deterministic
MIBC contains closed component accessors, all query processors and their bridge
loops. This is **static compilation input**, not measured PGO, sample weights or
a gameplay change. Unmapped/open interface callsites stop the build.
The audit requires every corresponding compiled store/query/processor bridge,
not just an arbitrary `ForEach` export. The pinned WASM compiler's `--map` crashes;
the helper retains `--verbose` logs and reads native function exports instead.

Compiler arguments/logs and both MSBuild binlogs remain in an isolated build
directory. `r2r-inventory.json` verifies packaged bytes, hashes, table sizes
and exported WASM functions. Compilation coverage is necessary but is **not**
proof that the runtime chose those functions rather than interpreter fallback.
The bridge's mode string describes build configuration only.

The [Chromium probe](../tools/web/probe-coreclr-r2r.mjs) records a CPU profile
and maps WASM function indices to compiler exports and native symbols. It
distinguishes compiled simulation frames from `InterpExecMethod` samples.
Its elapsed time includes profiling overhead and is **not a benchmark**.
Use independent uninstrumented runs only after hot-path verification; retain
checksums and workload counters, and do not assume float replay equivalence
between different runtimes.

```powershell
$env:PLAYWRIGHT_BROWSERS_PATH = "$PWD/Benchmarks.Local/tmp/playwright-browsers"
node tools/web/probe-coreclr-r2r.mjs Benchmarks.Local/tmp/web-coreclr-r2r/wwwroot Benchmarks.Local/tmp/coreclr-probe
node --test tools/web/test-coreclr-r2r.mjs
```

Add `--require-no-interpreter` to the probe to reject observed interpreter leaf
samples. The current experiment fails that runtime gate. Even a zero-sample
result would only be bounded sampling evidence, not a universal fallback ban.

## Current Validation

The IL-only CoreCLR control passed the full existing browser checker: all three
profiles, seeded reset parity, combat, controls, live statistics and responsive
desktop/mobile rendering. All 53 trimmed DLLs can be emitted as R2R modules
and packaged by RC1. The initial partial R2R prototype executes compiled
`Battle.Step` and physics, but does not automatically emit the closed struct
generic `SecsStore.ForEach` / `SECS.Query.ForEach` specializations. Its observed
profile was dominated by `InterpExecMethod`, so it is not a performance upgrade.

Explicit static roots resolve the query compilation gap: 276 generic interface
IL sites expand into 104 roots, including 15 adapter loops, 15 query loops,
15 processor bridges and 23 `Get` / 23 `Set` specializations. The packaged output
passes the full static gate. Browser CPU stacks actually enter compiled battle,
physics, component access and query functions; nevertheless **85.0% of observed
leaf samples still execute `InterpExecMethod`**, with transitions under physical
`Query` / `RayCast` and contact processing. This is mixed R2R, not an
interpreter-free hot path; a leaf sample alone does not identify every managed
callee responsible for fallback.

Three independent uninstrumented Chromium replays, alternating runtime order,
nominal / seed 7331, separate 30-tick warmup then reset, measured ticks 1-120:

| Runtime | Median run mean Core Step, ms | Run-mean range, ms |
| --- | ---: | ---: |
| Mono .NET 8 AOT | 10.245 | 10.152-10.492 |
| CoreCLR .NET 11 RC1 R2R | 252.131 | 251.627-252.723 |

All six replays match checksum `905660D8DFD501EC`, entity counts and combat
counters. The experiment is about 24.6 times slower on this **short opening
segment**, not a steady-state or full-battle estimate. Profiling was disabled
for these timings. Local raw evidence is
`Benchmarks.Local/tmp/coreclr-r2r-comparison-short.json` and the CPU profile is
in `Benchmarks.Local/tmp/coreclr-r2r-rooted-probe`; these ignored files are not
published benchmark artifacts. No speedup, complete absence of fallback,
production migration or remote deployment is claimed.
Original benchmark measurements and historical reports are not modified.

## Serve And Licenses

After a successful local publish, serve its `wwwroot` with an ordinary static
server. The default base is `/`; `-BasePath /secs/` is available for a separately
approved deployment. This helper does not deploy or change remote state.

```powershell
python -m http.server 8041 --directory Benchmarks.Local/tmp/web-coreclr-r2r/wwwroot
```

The distribution retains the shared Aether/Lucide/artwork notices and copies
the pinned .NET 11 package inventories and Emscripten 6.0.2 license/authors.
SDKs, compilers and build-only dependencies are not distributed in the site.
