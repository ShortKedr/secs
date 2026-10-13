English | [Русский](README.ru.md)

# Hardware Optimization Study

[Research index](../../docs/space-battle/RESEARCH.md) | [Current campaign](../../docs/space-battle/REPORT.md)

Local diagnostic tools only. Do not overwrite frozen benchmark results or publish builds. Preserve pre-change source and Release DLLs under an unused `Benchmarks.Local/tmp` directory, including uncommitted source, before editing. `StudyAssemblies` compiles the identical harness against a preserved assembly set without rebuilding it.

```powershell
$root = (Get-Location).Path
$env:DOTNET_CLI_HOME = "$root/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$root/Benchmarks.Local/nuget-packages"
dotnet build tools/HardwareStudy/HardwareStudy.csproj -c Release `
  --artifacts-path Benchmarks.Local/tmp/my-study-build `
  "-p:StudyAssemblies=$root/Benchmarks.Local/tmp/my-preserved-dlls" -p:NuGetAudit=false
dotnet Benchmarks.Local/tmp/my-study-build/bin/HardwareStudy/release/HardwareStudy.dll `
  --game --repeats 5 --output Benchmarks.Local/tmp/my-new-study.json
python tools/HardwareStudy/compare.py Benchmarks.Local/tmp/my-before.json Benchmarks.Local/tmp/my-after.json
```

Without `StudyAssemblies`, build current project references. Never copy changed assemblies over the preserved baseline. Output must be new and project-local. `--port` selects `secs`, `lite`, `classic`, `morpeh`, `static`, or `all`. `--seed`, `--ships`, `--churn`, `--ticks`, `--phase-ticks` and `--repeats` control matching workloads.

`--game` matches the nominal frontend: LightSwarm, endless reinforcement, no carrier replacement. Without it, the legacy Uniform workload remains a separate regression scenario. Warmup is a separate full-length replay. Independent measured battles start at zero. Checksum, all gameplay/AI counters and population means/endpoints must match after every phase. Capture(false) is measured separately, not subtracted from Step. Native Step allocations, managed heap and native storage bytes are different measurements; adapter/snapshot metadata is managed memory, absent from native storage totals.

`--physics-timings` enables opt-in contact-candidate/solver/hit-candidate/narrow-phase clocks. This instrumentation has overhead; never compare instrumented with uninstrumented runs. `--store-probes` runs fresh-world native SECS accessor and final-archetype microprobes instead of battles. It does not establish an end-to-end speedup or change gameplay counters. Birth row-operation counts are code-audited, not instrumented copy-byte/repair counts.

Browser measurements require production stable .NET 8 Mono AOT builds and use `tools/web/profile-revisions.mjs --sites <comma-separated-wwwroot-directories> --labels <matching-labels> --repeats 3 --output <new-project-local-json>`. It alternates version order and warms separate 600-tick replays. Step, export/interop, phase-end export, gameplay/AI counters, populations, linear WASM memory and telemetry-managed heap are separate. `--physics-timings` uses the same opt-in clock distinction. The browser does not measure pure Canvas time here; use the frontend probes separately and do not subtract mismatched windows. Local Mono interpreter diagnostics remain supported, but do not replace production AOT measurements.

Do not run builds or other probes concurrently with timing. Report medians and ranges of independent run means, not fastest ticks; uncontrolled desktop background activity remains a limitation. Native and browser checksums are only compared within the same runtime/config, not as cross-platform bit-determinism proof.

```powershell
python -m unittest discover -s tools/HardwareStudy -p test_compare.py
python tools/HardwareStudy/compare.py --browser Benchmarks.Local/tmp/my-browser.json
```
