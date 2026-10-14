English | [Русский](REPORT.ru.md)

# Space Battle — ECS high workload and synthetic churn

Format 2 campaign: 2026-10-14T23:20:11.4454531+05:00 — 2026-10-14T23:47:56.0621543+05:00. Primary profile: `high` (0 synthetic operations/tick).

All profiles use the game preset with gameplay `ChurnMultiplier=2`. `high-churn-1` and `high-churn-4` add 1 and 4 inert Lifetime component transitions per tick from a fixed 32-entity pool. They do not alter gameplay state. The outer tick timer includes this pre-Step hook; Battle.LastTickMs and stage timers exclude it. Legacy low/nominal/high results used gameplay churn values 0.5/1/2 and a different matrix; their timings are not directly comparable.

The [archived finite campaign](REPORT_2026-10-14_FINITE.md) preserves the previous method and results.

The full-run mean covers 108,000 measured ticks after a separate 600-tick warmup battle. Desktop HUD Mean covers the latest up to 600 ticks and uses Battle.LastTickMs; the outer headless timer also includes synthetic churn. Compare desktop and headless only at matching gameplay settings and tick ranges.

## Validation

Validation passed: 15 cases across five ECS implementations; 1,620,000 measured ticks; hashes and provenance match; gameplay checksums, counters, tactics, and entity traces match across ports and all three churn profiles. Synthetic operation totals match the configured rate. Store census includes the fixed 32-entity synthetic pool.

Local tick CSV files were checked, including synthetic operations, adds/removes and hook time on every tick.

## Tick time

| Profile | Synthetic ops/tick | ECS | Hook mean ms | Cumulative outer mean 1–600 ms | Cumulative outer mean 1–2,000 ms | Remainder outer mean 2,001–108,000 ms | Full outer mean 1–108,000 ms | Full P99 ms | Managed B/tick |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| high **primary** | 0 | SECS | 0.0000 | 0.8445 | 0.7551 | 1.0255 | 1.0205 | 1.9106 | 601.4 |
| high **primary** | 0 | LeoECSLite | 0.0000 | 1.1293 | 1.1138 | 1.0942 | 1.0946 | 2.2896 | 681.0 |
| high **primary** | 0 | LeoECS Classic | 0.0000 | 1.0578 | 1.1421 | 0.9800 | 0.9830 | 2.0860 | 667.5 |
| high **primary** | 0 | Morpeh | 0.0000 | 0.9873 | 0.9090 | 1.0233 | 1.0212 | 1.9724 | 654.2 |
| high **primary** | 0 | StaticEcs 2.2.9 | 0.0000 | 0.9804 | 0.9493 | 1.1202 | 1.1170 | 2.3339 | 621.3 |
| high-churn-1 | 1 | SECS | 0.0004 | 0.8210 | 0.7517 | 0.9601 | 0.9563 | 1.8880 | 601.5 |
| high-churn-1 | 1 | LeoECSLite | 0.0003 | 1.1055 | 1.0537 | 1.2127 | 1.2098 | 2.6085 | 682.2 |
| high-churn-1 | 1 | LeoECS Classic | 0.0003 | 1.0984 | 0.9494 | 1.0006 | 0.9997 | 2.3102 | 667.5 |
| high-churn-1 | 1 | Morpeh | 0.0004 | 0.9142 | 0.8863 | 0.9417 | 0.9406 | 1.9477 | 654.3 |
| high-churn-1 | 1 | StaticEcs 2.2.9 | 0.0002 | 1.0079 | 0.9201 | 1.0174 | 1.0156 | 2.1216 | 621.3 |
| high-churn-4 | 4 | SECS | 0.0005 | 0.8753 | 0.7915 | 0.8100 | 0.8097 | 1.6833 | 601.5 |
| high-churn-4 | 4 | LeoECSLite | 0.0004 | 1.1000 | 1.0365 | 1.0660 | 1.0654 | 2.1975 | 682.2 |
| high-churn-4 | 4 | LeoECS Classic | 0.0005 | 1.0444 | 0.9211 | 0.9686 | 0.9677 | 1.9736 | 667.5 |
| high-churn-4 | 4 | Morpeh | 0.0007 | 0.9206 | 0.8872 | 0.9211 | 0.9205 | 1.9148 | 654.3 |
| high-churn-4 | 4 | StaticEcs 2.2.9 | 0.0004 | 0.9698 | 0.9587 | 0.9979 | 0.9972 | 2.0405 | 621.3 |

In the primary high profile, the first 2,000 ticks have a higher mean than the remainder for 2 of five ECS implementations. The direct synthetic hook means range from 0.24 to 0.75 µs/tick; full-run Mean differences are much larger and vary in direction. With one run per case, those differences do not isolate churn cost.

[Summary CSV](current/summary.csv)

## Primary high stage means

| Stage | SECS ms | Lite ms | Classic ms | Morpeh ms | StaticEcs ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| collect | 0.2224 | 0.2462 | 0.2095 | 0.2230 | 0.2373 |
| ai | 0.1748 | 0.1730 | 0.1575 | 0.1601 | 0.1825 |
| thermal-inventory | 0.0181 | 0.0198 | 0.0173 | 0.0175 | 0.0245 |
| steering | 0.0426 | 0.0400 | 0.0371 | 0.0393 | 0.0463 |
| weapons | 0.0366 | 0.0367 | 0.0311 | 0.0328 | 0.0407 |
| carriers | 0.0021 | 0.0016 | 0.0012 | 0.0014 | 0.0022 |
| physics | 0.0981 | 0.1632 | 0.1245 | 0.1578 | 0.1635 |
| hits | 0.3643 | 0.3529 | 0.3433 | 0.3355 | 0.3608 |
| lifecycle | 0.0614 | 0.0610 | 0.0614 | 0.0536 | 0.0590 |
| Battle.LastTickMs | 1.0203 | 1.0945 | 0.9829 | 1.0211 | 1.1169 |

![Mean by profile](current/MeanMs.png)

![P99 by profile](current/P99Ms.png)

![Windows 1–600, 1–2,000, remainder and full run](current/InitialWindows.png)

![Synthetic hook time](current/SyntheticChurnMs.png)

## Gameplay parity

| Profile | Synthetic ops/tick | Checksum | Ships | Turrets | Shots | Hits | Created | Destroyed |
| --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| high | 0 | 7EE0B6054181F4AE | 89 | 48 | 945589 | 728514 | 17469 | 17380 |
| high-churn-1 | 1 | 7EE0B6054181F4AE | 89 | 48 | 945589 | 728514 | 17469 | 17380 |
| high-churn-4 | 4 | 7EE0B6054181F4AE | 89 | 48 | 945589 | 728514 | 17469 | 17380 |

## SECS stages: opening, remainder and full run

Stages are measured inside Battle.Step; the outer tick row also includes the pre-Step hook. At nonzero churn it does not equal the sum of stage rows.

### high — 0 synthetic ops/tick

| Stage | 1–600 ms | 1–2,000 ms | 2,001–108,000 ms | 1–108,000 ms |
| --- | ---: | ---: | ---: | ---: |
| collect | 0.2007 | 0.1894 | 0.2230 | 0.2224 |
| ai | 0.1647 | 0.1316 | 0.1756 | 0.1748 |
| thermal-inventory | 0.0125 | 0.0109 | 0.0183 | 0.0181 |
| steering | 0.0377 | 0.0290 | 0.0428 | 0.0426 |
| weapons | 0.0317 | 0.0249 | 0.0368 | 0.0366 |
| carriers | 0.0004 | 0.0007 | 0.0021 | 0.0021 |
| physics | 0.0790 | 0.0676 | 0.0987 | 0.0981 |
| hits | 0.2731 | 0.2619 | 0.3662 | 0.3643 |
| lifecycle | 0.0444 | 0.0389 | 0.0618 | 0.0614 |
| Outer tick | 0.8445 | 0.7551 | 1.0255 | 1.0205 |

### high-churn-1 — 1 synthetic ops/tick

| Stage | 1–600 ms | 1–2,000 ms | 2,001–108,000 ms | 1–108,000 ms |
| --- | ---: | ---: | ---: | ---: |
| collect | 0.1955 | 0.1867 | 0.2176 | 0.2170 |
| ai | 0.1588 | 0.1305 | 0.1596 | 0.1591 |
| thermal-inventory | 0.0123 | 0.0108 | 0.0163 | 0.0162 |
| steering | 0.0361 | 0.0288 | 0.0382 | 0.0380 |
| weapons | 0.0305 | 0.0245 | 0.0328 | 0.0327 |
| carriers | 0.0004 | 0.0007 | 0.0017 | 0.0017 |
| physics | 0.0762 | 0.0671 | 0.0947 | 0.0942 |
| hits | 0.2666 | 0.2616 | 0.3390 | 0.3375 |
| lifecycle | 0.0441 | 0.0406 | 0.0596 | 0.0593 |
| Outer tick | 0.8210 | 0.7517 | 0.9601 | 0.9563 |

### high-churn-4 — 4 synthetic ops/tick

| Stage | 1–600 ms | 1–2,000 ms | 2,001–108,000 ms | 1–108,000 ms |
| --- | ---: | ---: | ---: | ---: |
| collect | 0.2087 | 0.1969 | 0.1962 | 0.1962 |
| ai | 0.1671 | 0.1372 | 0.1345 | 0.1346 |
| thermal-inventory | 0.0130 | 0.0114 | 0.0130 | 0.0129 |
| steering | 0.0370 | 0.0300 | 0.0314 | 0.0313 |
| weapons | 0.0326 | 0.0262 | 0.0272 | 0.0272 |
| carriers | 0.0004 | 0.0007 | 0.0014 | 0.0013 |
| physics | 0.0799 | 0.0701 | 0.0754 | 0.0753 |
| hits | 0.2875 | 0.2754 | 0.2823 | 0.2822 |
| lifecycle | 0.0486 | 0.0430 | 0.0480 | 0.0479 |
| Outer tick | 0.8753 | 0.7915 | 0.8100 | 0.8097 |

## Provenance

- CPU: AMD Ryzen 9 9900X 12-Core Processor; RAM: 61.49 GiB.
- Runtime: .NET 8.0.32; OS: Microsoft Windows 10.0.26200; tiered compilation: `0`.
- Base commit: `d5269ea87c29edf89f8b6d2e309428d5d8601ca4`; measured file hashes describe the actual working tree and are recorded in [current/manifest.json](current/manifest.json).
- [JSON, local tick CSV files and charts](current/); [summary CSV](current/summary.csv); [hardware details](current/hardware.json).
- StaticEcs: [2.2.9 release](https://github.com/Felid-Force-Studios/StaticEcs/releases/tag/v2.2.9), source `23b1cf36d89497e8a126d38459d688e2b0a5dea3`; StaticPack source `7bfb6cf925901b9366dda884258d727a89fff76e`.

One run per case describes this machine and does not establish a stable ranking or confidence interval. Outer tick time includes the synthetic hook, gameplay systems, physics, and ECS commits; Battle.LastTickMs and stage timings exclude the hook. Rendering and snapshot/report work are excluded. Managed allocation excludes native ECS storage.
