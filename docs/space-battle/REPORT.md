English | [Русский](REPORT.ru.md)

# Space Battle benchmarks

Current campaign: 2026-10-13T15:03:13.9771756+05:00 — 2026-10-13T15:11:20.5557936+05:00.

Measured the current headless simulation on five ECS: `mounted-armament-v7`, `layered-squads-v3`, `circle-kernel-v1`, `layered-weapon-damage-v1`.

**Validation passed:** 15 cases, 1,620,000 measured ticks. Within each profile all 30 checkpoints, final checksums, gameplay and tactical counters, and per-minute entity counts match.

Local tick CSV verification recomputed means, quantiles, spread, budget misses and allocations. Input CSV hashes are retained in the manifest.

## Reading the results

Each case is one independent run of 108,000 ticks (30 simulated minutes), following a separate 600-tick warmup. Low/nominal/high change only churn: 0.5/1/2. Seed 7331, 96 escorts and 3 carriers per team, 60 ticks/s. Headless defaults use a uniform fleet, legacy random deployment and no endless reinforcements. The graphical game uses a different gameplay preset; see [client settings](../../SpaceBattle.Game/README.md).

Step time includes gameplay systems, physics and structural commits. Rendering, setup, snapshots and telemetry serialization are excluded. The 16.67 ms budget is a simulation budget, not FPS. These are observations on this machine; one run per case does not establish a stable ranking or confidence interval. Allocations count managed bytes on the Step thread and exclude native storage.

### Colour scale

![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") up to 5% · ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 15% · ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") from 30% · ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](../benchmarks/COLORS.md).

## Tick time

| Profile | ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| low | secs | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1638 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2584 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.4543 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 6.0640 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 109.0 |
| low | lite | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 0.2002 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.3228 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.5329 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 7.0941 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 146.1 |
| low | classic | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg "Lag between 5% and 30%") 0.1810 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.2915 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 0.5171 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 6.4586 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 133.5 |
| low | morpeh | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1700 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.2769 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.4887 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 5.9493 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 152.0 |
| low | static | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.1944 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 0.3153 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.5151 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 6.1519 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 109.4 |
| nominal | secs | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2225 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.3351 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.4740 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 5.9797 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 197.0 |
| nominal | lite | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.2850 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.4444 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.6047 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 6.3300 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 238.4 |
| nominal | classic | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.2615 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.4021 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.5596 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 6.3914 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 232.4 |
| nominal | morpeh | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.2489 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 0.3873 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.5286 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 5.9502 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 249.0 |
| nominal | static | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.2816 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.4362 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.5989 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 6.0453 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 198.7 |
| high | secs | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.3522 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.5533 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.7024 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 6.1444 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 413.2 |
| high | lite | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.4693 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.7366 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.9177 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 6.3862 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 492.8 |
| high | classic | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 0.4253 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 0.6697 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 0.8484 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 6.3780 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 469.2 |
| high | morpeh | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.3987 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 0.6422 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.8367 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 6.4360 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 466.1 |
| high | static | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.4394 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.7172 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.9454 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 9.0184 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 416.1 |

![Mean Step time](current/MeanMs.png)

![P99 Step time](current/P99Ms.png)

## Simulation state and stages

| Profile | Checksum | Ships | Turrets | Shots | Hits | Created | Destroyed |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| low | 69D77D8C4F53CE02 | 36 | 56 | 162943 | 144404 | 1533 | 1497 |
| nominal | C2521F09C1B2C7E3 | 38 | 48 | 270351 | 230461 | 2754 | 2716 |
| high | 28184C58A2877695 | 56 | 46 | 414013 | 327079 | 4763 | 4707 |

Mean stage time in nominal, ms. Each row compares the same stage across five ports; complete data is in JSON.

| Stage | SECS | Lite | Classic | Morpeh | StaticEcs |
| --- | ---: | ---: | ---: | ---: | ---: |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0467 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.0557 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0477 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0476 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 0.0536 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0377 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 0.0457 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.0423 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg "Lag between 5% and 30%") 0.0411 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.0475 |
| thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0048 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0065 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 0.0059 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0052 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0070 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0083 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 0.0102 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.0094 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0088 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0112 |
| weapons | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0183 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.0229 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0199 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0191 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0239 |
| carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0006 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0006 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0004 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 0.0005 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0008 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0194 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0404 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0311 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0353 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0389 |
| hits | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0725 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.0850 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.0865 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0755 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 0.0827 |
| lifecycle | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0142 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.0180 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.0183 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg "Lag between 5% and 30%") 0.0158 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.0159 |

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
