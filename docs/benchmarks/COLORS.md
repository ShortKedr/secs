English | [Русский](COLORS.ru.md)

# Colour scale for benchmark reports

[Documentation map](../README.md) · [SECS results](current/REPORT.md) · [SpaceBattle results](../space-battle/REPORT.md)

The same colours describe relative differences in comparable tables and charts. The printed numbers, units, error bars and source data remain the evidence; colour helps scan them.

![Lag up to 5%](heatmap/best.svg) up to 5% · ![Lag between 5% and 30%](heatmap/middle.svg) 15% · ![Lag from 30%](heatmap/worst.svg) from 30% · ![Neutral](heatmap/equal.svg) neutral.

## What the colours mean

The reference is the best unrounded value within one explicitly comparable group. For a metric where less is better, lag is `(value / best - 1) × 100%`. Where more is better, it is `(1 - value / best) × 100%`. The scale is fixed across reports: green through 5%, green to yellow through 15%, yellow to red through 30%, red from 30%. Nine discrete shades preserve the original scale; the colours are never stretched to the minimum and maximum of a table.

If every value is within 5% of the reference, the whole group is grey. Grey means neutral or insufficient separation; it does not prove equality. Unknown values and groups with only one known value are unranked. With a zero reference, zero matches zero and a positive lower-is-better value has undefined relative lag; it receives the last shade only when it exceeds any explicit absolute threshold. No artificial epsilon is added.

The thresholds describe observed effect size. They are not significance tests, confidence intervals or a universal framework ranking. Ordinary workload samples, adjacent ticks and phases of one simulation are not independent runs.

## Which values can be compared

- Framework timings: the same operation, entity count, profile, units and recorded environment. A stage is compared with the same stage in other implementations.
- Before/after timings: an explicit pair under the recorded equivalent workload. An experiment that changes work or gameplay outcome needs its own interpretation.
- Managed allocations or memory: the same metric and scope, compared separately from timing. Byte counts from different storage or process scopes are not mixed.
- Comparison bar charts: each panel uses its own documented comparison group. Axis labels identify the implementations; bar colours carry the same meaning as table markers. Time-series plots retain series colours to identify their lines, rather than assign one quality score to a changing trajectory.

Population, gameplay events, checksum, input sizes, sample counts, parameters and stage shares retain ordinary formatting. Their magnitude does not express performance quality. Archive results retain their date and configuration; their colours do not make them current measurements.

## Reported variability

When real independent run summaries are available, the reference is the best median of those summaries. Colours beyond 5% require conservative separation of the observed min/max envelopes. An explicitly published interval may be used with its explicitly reported central value; endpoints are not invented as new runs. Overlapping envelopes, incomplete variability data and small separation stay grey. These envelopes are descriptive, not confidence intervals.

An absolute threshold is used only when its metric, units and reason are explicitly recorded. It supplements the relative rule and does not change the printed result. The existing SpaceBattle policy is [heatmap_policy.json](../../tools/space-battle/heatmap_policy.json); it currently has no absolute thresholds.

## Reproduction

[benchmark_colors.py](../../tools/benchmark_colors.py) supplies the shared palette and classification. Report generators use local SVG markers and the same palette for PNG charts; no external images, CSS or network calls are needed. The [legacy import](../../tools/space-battle/heatmap.py) keeps the archived SpaceBattle generator compatible.

Regenerate reports and charts from saved inputs to update presentation. New timing runs are unnecessary for a colour change.
