English | [Русский](REPORT.ru.md)

# Space Battle benchmarks

Current campaign: 2026-10-13T15:03:13.9771756+05:00 — 2026-10-13T15:11:20.5557936+05:00.

Measured the current headless simulation on five ECS: `mounted-armament-v7`, `layered-squads-v3`, `circle-kernel-v1`, `layered-weapon-damage-v1`.

**Validation passed:** 15 cases, 1,620,000 measured ticks. Within each profile all 30 checkpoints, final checksums, gameplay and tactical counters, and per-minute entity counts match.

Local tick CSV verification recomputed means, quantiles, spread, budget misses and allocations. Input CSV hashes are retained in the manifest.

## Reading the results

Each case is one independent run of 108,000 ticks (30 simulated minutes), following a separate 600-tick warmup. Low/nominal/high change only churn: 0.5/1/2. Seed 7331, 96 escorts and 3 carriers per team, 60 ticks/s. Headless defaults use a uniform fleet, legacy random deployment and no endless reinforcements. The graphical game uses a different gameplay preset; see [client settings](../../SpaceBattle.Game/README.md).

Step time includes gameplay systems, physics and structural commits. Rendering, setup, snapshots and telemetry serialization are excluded. The 16.67 ms budget is a simulation budget, not FPS. These are observations on this machine; one run per case does not establish a stable ranking or confidence interval. Allocations count managed bytes on the Step thread and exclude native storage.

## Tick time

| Profile | ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| low | secs | 0.1638 | 0.2584 | 0.4543 | 6.0640 | 0 | 109.0 |
| low | lite | 0.2002 | 0.3228 | 0.5329 | 7.0941 | 0 | 146.1 |
| low | classic | 0.1810 | 0.2915 | 0.5171 | 6.4586 | 0 | 133.5 |
| low | morpeh | 0.1700 | 0.2769 | 0.4887 | 5.9493 | 0 | 152.0 |
| low | static | 0.1944 | 0.3153 | 0.5151 | 6.1519 | 0 | 109.4 |
| nominal | secs | 0.2225 | 0.3351 | 0.4740 | 5.9797 | 0 | 197.0 |
| nominal | lite | 0.2850 | 0.4444 | 0.6047 | 6.3300 | 0 | 238.4 |
| nominal | classic | 0.2615 | 0.4021 | 0.5596 | 6.3914 | 0 | 232.4 |
| nominal | morpeh | 0.2489 | 0.3873 | 0.5286 | 5.9502 | 0 | 249.0 |
| nominal | static | 0.2816 | 0.4362 | 0.5989 | 6.0453 | 0 | 198.7 |
| high | secs | 0.3522 | 0.5533 | 0.7024 | 6.1444 | 0 | 413.2 |
| high | lite | 0.4693 | 0.7366 | 0.9177 | 6.3862 | 0 | 492.8 |
| high | classic | 0.4253 | 0.6697 | 0.8484 | 6.3780 | 0 | 469.2 |
| high | morpeh | 0.3987 | 0.6422 | 0.8367 | 6.4360 | 0 | 466.1 |
| high | static | 0.4394 | 0.7172 | 0.9454 | 9.0184 | 0 | 416.1 |

![Mean Step time](current/MeanMs.png)

![P99 Step time](current/P99Ms.png)

## Simulation state and stages

| Profile | Checksum | Ships | Turrets | Shots | Hits | Created | Destroyed |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| low | 69D77D8C4F53CE02 | 36 | 56 | 162943 | 144404 | 1533 | 1497 |
| nominal | C2521F09C1B2C7E3 | 38 | 48 | 270351 | 230461 | 2754 | 2716 |
| high | 28184C58A2877695 | 56 | 46 | 414013 | 327079 | 4763 | 4707 |

Mean stage costs for nominal/SECS; complete data for every port is in JSON.

| Stage | Mean ms |
| --- | ---: |
| collect | 0.0467 |
| ai | 0.0377 |
| thermal-inventory | 0.0048 |
| steering | 0.0083 |
| weapons | 0.0183 |
| carriers | 0.0006 |
| physics | 0.0194 |
| hits | 0.0725 |
| lifecycle | 0.0142 |

## Data provenance

- CPU: AMD Ryzen 9 9900X 12-Core Processor; RAM: 61.49 GiB.
- Runtime: .NET 8.0.31; OS: Microsoft Windows 10.0.26200; tiered compilation: `0`.
- Source: `07e7ad04a18a15bfe24c130426fa8f2f2352b981`; source SHA256: [current/manifest.json](current/manifest.json).
- [JSON](current/), [summary.csv](current/summary.csv), [hardware.json](current/hardware.json).

The manifest records source and input hashes; each JSON records exact upstream pins and SHA256 hashes of all measured DLLs. Provenance.Sources.SECS in the headless host is a historical descriptive string; the campaign manifest supplies its exact revision and source hashes.

## Reproduction

From the project root, with .NET 8 and available NuGet packages. Charts require Python and matplotlib. Run measurements sequentially without other heavy work. This command replaces current; use another OutputDirectory inside the project for experiments.

```powershell
$env:DOTNET_CLI_HOME = "$PWD/Benchmarks.Local/.dotnet"
$env:NUGET_PACKAGES = "$PWD/Benchmarks.Local/nuget-packages"
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet build SpaceBattle.Headless/SpaceBattle.Headless.csproj -c Release --no-restore -m:1 -p:GeneratePackageOnBuild=false
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate-adapters
dotnet SpaceBattle.Headless/bin/Release/net8.0/SpaceBattle.Headless.dll --validate-ports
./tools/space-battle/run-suite.ps1 -SkipBuild -OutputDirectory docs/space-battle/current
python tools/space-battle/analyze_current.py --verify-ticks
```

Without --verify-ticks the analyzer verifies published JSON and hashes and builds the report without requiring local CSVs. Long tick CSVs stay local; published JSON is sufficient for summary tables and charts.

## History and related documents

- [Archived 2026-10-11 campaign](REPORT_2026-10-11.md): predictive-neutral-v1, Aether, old frontend replay and entity composition. Its values are not compared with this campaign because gameplay rules, physics and workload changed.
- [Architecture](ARCHITECTURE.md), [physics](PHYSICS.md), [armament](ARMAMENT.md), [tactics](TACTICS_V3.md).
- [SECS microbenchmarks](../../SECS.Benchmarks/README.md): separate workloads without gameplay logic.
