English | [Русский](REPORT.ru.md)

# Space Battle benchmarks — real-game high

Preset: `game`.

Current campaign: 2026-10-15T02:39:29.9098413+05:00 — 2026-10-15T02:48:08.1498713+05:00. Profile: `high`.

The current headless simulation was measured with five ECS implementations: SECS, LeoECS Lite, LeoECS Classic, Morpeh and StaticEcs. Gameplay system revisions: `mounted-armament-v7`, `layered-squads-v3`, `circle-kernel-v1`, `layered-weapon-damage-v1`.

## Framework revisions used

Third-party ECS rows show exact upstream commits and their matching releases. The SECS row shows the base commit and project version; the manifest and JSON record hashes of the measured sources and DLLs.

| Framework | Source revision | Version |
| --- | --- | --- |
| SECS | `beca1e4c0277ca7f03b972a8d4ffef789b45ab19` | `0.1.0` |
| LeoECS Lite | `5053141d7d663c6b1855c8409ac93dfac807b239` | [2024.5.22](https://github.com/Leopotam/ecslite/releases/tag/2024.5.22) |
| LeoECS Classic | `1213c31c9ef8325d48e74a52b345c4db242d038b` | [2023.6.22](https://github.com/Leopotam/ecs/releases/tag/2023.6.22) |
| Morpeh | `9f6369384792a01d74df24efd35b08ae313e4f12` | [2024.1.1](https://github.com/scellecs/morpeh/releases/tag/2024.1.1) |
| StaticEcs | `23b1cf36d89497e8a126d38459d688e2b0a5dea3` | [2.2.9](https://github.com/Felid-Force-Studios/StaticEcs/releases/tag/v2.2.9) |

StaticEcs uses StaticPack source `7bfb6cf925901b9366dda884258d727a89fff76e`.

## Reading the results

The single `high` profile uses the real game preset with its normal `ChurnMultiplier=2`, seed 7331, 96 escorts and 3 carriers per team. The same simulation runs on all five ECS implementations. The archived low/nominal/high campaign used a different preset and gameplay workload; its timings are not directly comparable.

Game preset: LightSwarm fleet, Tactical deployment, EndlessReinforcements=true and ReplenishCarriers=false. Runs use Release, tiered compilation disabled and no realtime pacing.

The [archived finite campaign](REPORT_2026-10-14_FINITE.md) preserves the previous method and results.

The full-run mean covers 108,000 measured ticks after a separate 600-tick warmup battle. Desktop HUD Mean covers the latest up to 600 ticks and uses Battle.LastTickMs. The headless outer tick timer and HUD/stage timers have different boundaries; compare desktop and headless only at matching game settings and tick ranges.

Each case is one independent run of 108,000 ticks (30 simulated minutes), at seed 7331 and 60 ticks/s. The 16.67 ms budget is a simulation budget, not FPS. Headless quantiles use sorted[ceil((N−1)×p)]; the live HUD uses nearest rank ceil(N×p)−1. One run per case does not establish a stable ranking or confidence interval.

### Colour scale

![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") up to 5% · ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 15% · ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") from 30% · ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") neutral.

Colour shows lag from the best value in a comparable group: green → yellow → red. Groups entirely within 5% are grey. Colour describes effect size, not statistical significance. [Comparison rules](../benchmarks/COLORS.md).

The [Release client and initial HUD ticks](DESKTOP_TIMING.md) separately check cold startup, tiering and paced replay.

## Validation

Validation passed: five ECS cases, 540,000 measured ticks; hashes and provenance match; gameplay checksums, counters, tactics and entity traces match across ports. Store census contains gameplay entities only.

Local tick CSV files were checked for timing, stages, HUD, allocations and gameplay counters.

## Tick time

Each coloured cell compares five ECS for one metric in the high profile. The outer tick includes gameplay systems, physics and ECS commits; managed B/tick excludes native ECS storage.

| Profile | ECS | Mean ms | P95 ms | P99 ms | Max ms | >16.67 ms | Managed B/tick | Total outer tick s |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| high | SECS | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.8155 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 1.3228 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 1.6671 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 3.8497 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 601.6 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 88.071933 |
| high | LeoECSLite | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 1.1282 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 1.7972 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 2.2562 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 4.1613 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 681.0 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 121.850520 |
| high | LeoECS Classic | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 1.0081 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 1.5802 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 1.9417 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 3.9410 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg "Lag between 5% and 30%") 667.5 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 108.875424 |
| high | Morpeh | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.8582 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 1.3634 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 1.7206 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 4.3154 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg "Lag between 5% and 30%") 655.8 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 92.688794 |
| high | StaticEcs 2.2.9 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.8718 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 1.3969 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 1.7396 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 3.9212 | ![Neutral or insufficient separation](../benchmarks/heatmap/equal.svg "Neutral or insufficient separation") 0 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 619.2 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 94.154812 |

![Mean tick time](current/MeanMs.png)

![P99 tick time](current/P99Ms.png)

## Opening windows

Windows 1–600 and 1–2,000 are cumulative; the remainder 2,001–108,000 is disjoint.

| Profile | ECS | Cumulative outer mean 1–600 ms | Cumulative outer mean 1–2,000 ms | Remainder outer mean 2,001–108,000 ms | Full outer mean 1–108,000 ms |
| --- | --- | ---: | ---: | ---: | ---: |
| high | SECS | 0.8840 | 0.8005 | 0.8158 | 0.8155 |
| high | LeoECSLite | 1.0639 | 1.0381 | 1.1299 | 1.1282 |
| high | LeoECS Classic | 0.9986 | 0.9288 | 1.0096 | 1.0081 |
| high | Morpeh | 0.9450 | 0.8396 | 0.8586 | 0.8582 |
| high | StaticEcs 2.2.9 | 0.9531 | 0.8538 | 0.8721 | 0.8718 |

The first 2,000 ticks have a higher mean than the remainder for 0 of five ECS implementations.

[Summary CSV](current/summary.csv)

![Windows 1–600, 1–2,000, remainder and full run](current/InitialWindows.png)

## Simulation state and stages

Gameplay state matches across all five ports. Each stage is compared across five ECS in the high profile.

### Gameplay parity

| Profile | Checksum | Ships | Turrets | Shots | Hits | Created | Destroyed |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| high | 7EE0B6054181F4AE | 89 | 48 | 945589 | 728514 | 17469 | 17380 |

### Full-run stage means

#### high

| Stage | SECS ms | Lite ms | Classic ms | Morpeh ms | StaticEcs ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2015 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.2546 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg "Lag between 5% and 30%") 0.2169 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1988 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.2093 |
| ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1364 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.1736 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.1614 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1353 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1398 |
| thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0123 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0194 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0164 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0130 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0176 |
| steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0307 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0399 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.0367 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0317 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.0347 |
| weapons | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0265 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0364 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 0.0302 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0263 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.0295 |
| carriers | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.0015 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0016 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0012 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0012 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0015 |
| physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0792 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1736 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1307 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1316 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1310 |
| hits | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.2808 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.3639 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.3501 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2774 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2665 |
| lifecycle | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.0465 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0653 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0645 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0428 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0418 |
| Battle.LastTickMs | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.8154 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 1.1281 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 1.0080 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.8581 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.8717 |
| Outer tick | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.8155 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 1.1282 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 1.0081 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.8582 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.8718 |

### SECS stages: opening, remainder and full run

Stages are measured inside Battle.Step; the outer tick timer also includes work between stages and ECS commits.

#### high

| Stage | 1–600 ms | 1–2,000 ms | 2,001–108,000 ms | 1–108,000 ms |
| --- | ---: | ---: | ---: | ---: |
| collect | 0.2140 | 0.2030 | 0.2015 | 0.2015 |
| ai | 0.1707 | 0.1387 | 0.1364 | 0.1364 |
| thermal-inventory | 0.0120 | 0.0107 | 0.0123 | 0.0123 |
| steering | 0.0353 | 0.0286 | 0.0307 | 0.0307 |
| weapons | 0.0321 | 0.0250 | 0.0265 | 0.0265 |
| carriers | 0.0005 | 0.0008 | 0.0015 | 0.0015 |
| physics | 0.0835 | 0.0729 | 0.0793 | 0.0792 |
| hits | 0.2870 | 0.2780 | 0.2808 | 0.2808 |
| lifecycle | 0.0488 | 0.0427 | 0.0466 | 0.0465 |
| Outer tick | 0.8840 | 0.8005 | 0.8158 | 0.8155 |

## HUD windows: 600 ticks each

First, second and worst windows compare the same phase across all five ECS. The worst window has the largest mean HUD time among all ports; its tick range is shared.

Internal HUD timer mean over all 108,000 ticks:

| Profile | HUD mean ms: SECS | Lite | Classic | Morpeh | StaticEcs |
| --- | ---: | ---: | ---: | ---: | ---: |
| high | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.8154 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 1.1281 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 1.0080 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.8581 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.8717 |

### high

| Phase / ticks | HUD / stage mean ms | SECS | Lite | Classic | Morpeh | StaticEcs |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| first / 1–600 | HUD total | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.8839 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 1.0638 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.9984 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.9449 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.9530 |
| first / 1–600 | collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2140 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.2506 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.2255 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2246 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2182 |
| first / 1–600 | ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1707 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.1832 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.1796 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1692 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.1794 |
| first / 1–600 | thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0120 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0171 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0157 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 0.0137 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0188 |
| first / 1–600 | steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0353 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.0415 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.0397 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0374 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.0419 |
| first / 1–600 | weapons | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0321 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.0378 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0326 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0304 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.0364 |
| first / 1–600 | carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0005 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0005 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0004 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0003 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0006 |
| first / 1–600 | physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0835 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1597 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1262 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1379 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1367 |
| first / 1–600 | hits | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2870 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.3146 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.3170 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2867 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2813 |
| first / 1–600 | lifecycle | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 0.0488 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0588 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0618 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.0446 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0398 |
| second / 601–1200 | HUD total | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 1.0286 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 1.3429 | ![Lag between 5% and 30%](../benchmarks/heatmap/middle.svg "Lag between 5% and 30%") 1.1814 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 1.0434 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 1.0451 |
| second / 601–1200 | collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2629 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 0.3121 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2644 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2521 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2582 |
| second / 601–1200 | ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1789 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 0.2142 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.1928 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1732 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1751 |
| second / 601–1200 | thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0126 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0221 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0184 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg "Lag between 5% and 30%") 0.0138 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0187 |
| second / 601–1200 | steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0344 | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.0405 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0372 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0365 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg "Lag between 5% and 30%") 0.0376 |
| second / 601–1200 | weapons | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0274 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0349 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0289 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0266 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg "Lag between 5% and 30%") 0.0293 |
| second / 601–1200 | carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0010 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0012 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg "Lag between 5% and 30%") 0.0008 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0007 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0010 |
| second / 601–1200 | physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0967 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.2148 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1513 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1565 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1533 |
| second / 601–1200 | hits | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg "Lag between 5% and 30%") 0.3571 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.4268 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.4087 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.3360 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.3242 |
| second / 601–1200 | lifecycle | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.0575 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0763 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0789 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0479 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0477 |
| worst / 92401–93000 | HUD total | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 1.0650 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 1.6350 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 1.1994 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 1.0833 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 1.0838 |
| worst / 92401–93000 | collect | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2526 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.3379 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2502 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2442 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2521 |
| worst / 92401–93000 | ai | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.2032 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.2894 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg "Lag between 5% and 30%") 0.2185 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1971 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1990 |
| worst / 92401–93000 | thermal-inventory | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0164 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0321 | ![Lag between 5% and 30%](../benchmarks/heatmap/poor.svg "Lag between 5% and 30%") 0.0203 | ![Lag between 5% and 30%](../benchmarks/heatmap/good.svg "Lag between 5% and 30%") 0.0183 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0221 |
| worst / 92401–93000 | steering | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0431 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0666 | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.0482 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0463 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0467 |
| worst / 92401–93000 | weapons | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0321 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0525 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0336 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0324 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0337 |
| worst / 92401–93000 | carriers | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0016 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0016 | ![Lag between 5% and 30%](../benchmarks/heatmap/strong.svg "Lag between 5% and 30%") 0.0011 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0010 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0013 |
| worst / 92401–93000 | physics | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.1051 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.2518 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1561 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1665 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.1626 |
| worst / 92401–93000 | hits | ![Lag between 5% and 30%](../benchmarks/heatmap/fair.svg "Lag between 5% and 30%") 0.3523 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.5112 | ![Lag between 5% and 30%](../benchmarks/heatmap/bad.svg "Lag between 5% and 30%") 0.4006 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.3257 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.3166 |
| worst / 92401–93000 | lifecycle | ![Lag between 5% and 30%](../benchmarks/heatmap/weak.svg "Lag between 5% and 30%") 0.0586 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0918 | ![Lag from 30%](../benchmarks/heatmap/worst.svg "Lag from 30%") 0.0708 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0517 | ![Lag up to 5%](../benchmarks/heatmap/best.svg "Lag up to 5%") 0.0497 |

| Phase / ticks | Mean ships | Mean projectiles | Mean debris | Mean particles | Mean turrets |
| --- | ---: | ---: | ---: | ---: | ---: |
| first / 1–600 | 104.26 | 818.02 | 859.90 | 1558.18 | 48.44 |
| second / 601–1200 | 100.58 | 970.01 | 1513.26 | 1386.75 | 44.51 |
| worst / 92401–93000 | 108.08 | 880.04 | 1428.04 | 1240.26 | 44.83 |

Mean counts match across all five ECS and are shown once.

## Data provenance

- CPU: AMD Ryzen 9 9900X 12-Core Processor; RAM: 61.49 GiB.
- Runtime: .NET 8.0.32; OS: Microsoft Windows 10.0.26200; tiered compilation: `0`.
- Base commit: `beca1e4c0277ca7f03b972a8d4ffef789b45ab19`; measured file hashes describe the actual working tree and are recorded in [current/manifest.json](current/manifest.json).
- [Published JSON and charts](current/); [summary CSV](current/summary.csv); [hardware details](current/hardware.json). Local tick CSV files remain verification inputs; their hashes are in the manifest.
- StaticEcs source `23b1cf36d89497e8a126d38459d688e2b0a5dea3`; StaticPack source `7bfb6cf925901b9366dda884258d727a89fff76e`.

One run per ECS describes this machine and does not establish a stable ranking or confidence interval. Outer tick time includes gameplay systems, physics and ECS commits; Battle.LastTickMs and stage timings cover their own narrower scopes. Rendering and snapshot/report work are excluded. Managed allocation excludes native ECS storage.

## Reproduction

From the project root with .NET 8, a C# 13-capable SDK, the local NuGet cache, Python 3.12 and matplotlib. run-suite replaces the selected result directory: choose a different directory inside the project for a new experiment. The analyzer validates the data and builds both reports and charts.

```powershell
./tools/space-battle/run-suite.ps1 -Preset game -OutputDirectory docs/space-battle/current
python tools/space-battle/analyze_current.py --data-dir docs/space-battle/current --preset game --verify-ticks
```

If local tick CSV files are unavailable, omit --verify-ticks: the analyzer validates published JSON and hashes but does not repeat CSV verification.

## History and related documents

- [Archived finite battle, 2026-10-14](REPORT_2026-10-14_FINITE.md): the previous low/nominal/high matrix uses a different method and gameplay workload; timings are not directly comparable.
- [Archived 2026-10-11 campaign](REPORT_2026-10-11.md), [architecture](ARCHITECTURE.md), [physics](PHYSICS.md), [tactics](TACTICS_V3.md).
- [SECS microbenchmarks](../../SECS.Benchmarks/README.md): a separate workload without gameplay logic.
