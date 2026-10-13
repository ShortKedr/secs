"""Local descriptive summary; no benchmark execution or historical-report writes."""
import argparse
import json
import statistics as stats
from pathlib import Path
import os
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from benchmark_colors import Metric, compact_legend
from colorize_research_reports import colorize_table, tables


def comparison_metric(summary, table, row, column, text):
    """Use the unrounded distribution while keeping its printed text intact."""
    if table == 0:
        scene = next(s for s in summary['kernel'] if s['scenario'] == row[0])
        version = scene['versions'][row[1]]
        value = version['hits']['meanMs'] if column == 5 else version['step'][{2: 'meanMs', 3: 'p95Ms', 4: 'p99Ms'}[column]]
    elif table == 1:
        entry = next(r for r in summary['browser'][row[0]]['rows'] if r['version'] == row[1] and r['endTick'] == int(row[2]))
        value = entry['step'][{3: 'meanMs', 4: 'p95Ms', 5: 'p99Ms'}[column]] if column <= 5 else entry[{6: 'physics', 7: 'hits', 8: 'export'}[column]]
    elif table == 2:
        entry = next(r for r in summary['native'] if (r['version'], r['port'], r['endTick']) == (row[0], row[1], int(row[2])))
        value = entry['step'][{3: 'MeanMs', 4: 'P95Ms', 5: 'P99Ms'}[column]] if column <= 5 else entry[{6: 'physics', 7: 'export'}[column]]
    elif table == 3:
        entry = next(r for r in summary['desktop'] if r['version'] == row[0])
        value = entry['gpuRenderMs'] if row[1] == 'GPU render' else entry['phases'][row[1]][{2: 'Mean', 3: 'P95', 4: 'P99'}[column]]
    else:
        raise ValueError('No comparison metric for instrumented counters')
    return Metric(text, comparison=value['median'], interval=(value['minimum'], value['maximum']))


def colorize_markdown(text, output, summary):
    """Compare revisions only within matching scene/profile/window/phase."""
    project = Path(__file__).resolve().parents[2]
    assets = Path(os.path.relpath(project / 'docs/benchmarks/heatmap', output.parent)).as_posix()
    details = Path(os.path.relpath(project / 'docs/benchmarks/COLORS.md', output.parent)).as_posix()
    rules = {
        0: [('column', (2, 3, 4, 5), (0,))],
        1: [('column', (3, 4, 5, 6, 7, 8), (0, 2))],
        2: [('column', (3, 4, 5, 6, 7), (1, 2))],
        3: [('column', (2,), (1,))],
    }
    lines, blocks = tables(text)
    if len(blocks) != 5:
        raise ValueError('Review physics table scopes after changing the generator')
    for ordinal, (start, end) in reversed(list(enumerate(blocks))):
        if ordinal not in rules:
            continue
        rows = [[v.strip() for v in line.strip().strip('|').split('|')] for line in lines[start + 2:end]]
        resolver = lambda r, c, value, part: comparison_metric(summary, ordinal, rows[r], c, value)
        colored = colorize_table(rows, rules[ordinal], 'en', assets, resolver=resolver, repeated=True)
        # GPU render has no percentile columns. Other desktop phases can compare
        # p95/p99 separately from means, never one percentile against another.
        if ordinal == 3:
            normal = [i for i, row in enumerate(rows) if row[1] != 'GPU render']
            normal_rows = [rows[i] for i in normal]
            resolver = lambda r, c, value, part: comparison_metric(summary, ordinal, normal_rows[r], c, value)
            shaded = colorize_table(normal_rows, [('column', (3, 4), (1,))], 'en', assets, resolver=resolver, repeated=True)
            for i, row in zip(normal, shaded):
                colored[i][3:5] = row[3:5]
        lines[start + 2:end] = ['| ' + ' | '.join(row) + ' |\n' for row in colored]
    result = ''.join(lines)
    first_break = result.index('\n') + 1
    legend = '\n' + '\n'.join(compact_legend(language='en', asset_path=assets, details_link=details))
    legend += '\n\nColors use unrounded independent-repeat summaries and envelopes. Input sizes, tick numbers and instrumented work counters are not quality scores.\n'
    return result[:first_break] + legend + result[first_break:]


def distribution(values):
    values = list(values)
    return dict(median=stats.median(values), minimum=min(values), maximum=max(values))


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def kernel(path):
    source = read(path)
    assert source["exactWithinRuntimeReplay"] and source["candidateAllocationFreeCriticalPath"]
    rows = []
    for scene in source["scenarios"]:
        row = dict(scenario=scene["scenario"], versions={})
        for label, repeats in scene["repeats"].items():
            assert len(repeats) >= 3
            row["versions"][label] = {phase: {metric: distribution(r[phase][metric] for r in repeats)
                                            for metric in ("meanMs", "p95Ms", "p99Ms")}
                                      for phase in ("step", "hits")}
            row["versions"][label]["managedLiveBytes"] = distribution(r["memoryBefore"]["managedLiveBytes"] for r in repeats)
        rows.append(row)
    return rows


def diagnostics(path):
    source = read(path)
    assert source["instrumented"] and source["exactWithinRuntimeReplay"]
    rows = []
    for scene in source["scenarios"]:
        row = dict(scenario=scene["scenario"], versions={})
        for label, repeats in scene["repeats"].items():
            values = {}
            for section in ("work", "grid"):
                keys = repeats[0]["workCounters"][0]["diagnostics"][section].keys()
                for key in keys:
                    # Step-only grid excludes the subsequent CCD sweep-grid rebuild.
                    field = "stepDiagnostics" if section == "grid" else "diagnostics"
                    per_repeat = [stats.mean(f[field][section].get(key, 0) for f in r["workCounters"])
                                  for r in repeats]
                    values[f"{section}.{key}"] = distribution(per_repeat)
            row["versions"][label] = values
        rows.append(row)
    return rows


def native(paths):
    studies = {label: read(path) for label, path in paths.items()}
    reference = {}
    rows = []
    for label, study in studies.items():
        groups = {}
        for run in study["Runs"]:
            for phase in run["Phases"]:
                key = (run["Port"], phase["EndTick"])
                signature = {k: phase[k] for k in ("Checksum", "Counters", "Tactics", "MeanCounts", "FinalCounts")}
                assert reference.setdefault(key, signature) == signature, (label, key)
                groups.setdefault(key, []).append(phase)
        for (port, tick), repeats in groups.items():
            assert len(repeats) >= 3
            rows.append(dict(version=label, port=port, endTick=tick,
                             step={k: distribution(p["Step"][k] for p in repeats) for k in ("MeanMs", "P95Ms", "P99Ms")},
                             physics=distribution(next(s["Timing"]["MeanMs"] for s in p["Stages"] if s["Name"] == "physics") for p in repeats),
                             export=distribution(p["Export"]["MeanMs"] for p in repeats),
                             allocatedBytes=distribution(p["AllocatedBytes"] for p in repeats),
                             managedHeapBytes=distribution(p["ManagedHeapBytes"] for p in repeats),
                             counters=repeats[0]["Counters"], populations=repeats[0]["FinalCounts"], checksum=repeats[0]["Checksum"]))
    return rows


def browser(path):
    study = read(path)
    reference, groups = {}, {}
    for run in study["runs"]:
        assert run["runtimeMode"] == "aot-binary"
        for phase in run["phases"]:
            signature = {k: phase[k] for k in ("checksum", "counters", "tactics", "meanCounts", "finalCounts")}
            assert reference.setdefault(phase["endTick"], signature) == signature
            groups.setdefault((run["revision"], phase["endTick"]), []).append(phase)
    rows = []
    for (label, tick), repeats in groups.items():
        assert len(repeats) >= 3
        rows.append(dict(version=label, endTick=tick,
                         step={k: distribution(p["coreStep"][k] for p in repeats) for k in ("meanMs", "p95Ms", "p99Ms")},
                         physics=distribution(p["stages"][6]["timing"]["meanMs"] for p in repeats),
                         hits=distribution(p["stages"][7]["timing"]["meanMs"] for p in repeats),
                         export=distribution(p["exportOnlyAtPhaseEnd"]["meanMs"] for p in repeats),
                         memory=repeats[0]["memory"], populations=repeats[0]["finalCounts"], counters=repeats[0]["counters"],
                         checksum=repeats[0]["checksum"]))
    return dict(profile=study.get("profile", "nominal"), seed=study.get("seed", 7331), rows=rows)


def desktop(root, paired=False):
    rows, expected = [], None
    for label in ("before", "final"):
        repeats = [read(root / (f"desktop-paired-{label}-{i}.json" if paired else f"{label}-desktop-{i}.json")) for i in (1, 2, 3)]
        for r in repeats:
            assert r["GlErrors"] == [] or r["GlErrors"] == 0
            assert r["ValidationPass"]
            signature = (r["FinalBattleChecksum"], r["GameDllSha256"], r["FinalAliveEntities"])
            expected = signature if expected is None else expected
            assert signature == expected
        fields = ("SimulationCpuMs", "SnapshotCaptureCpuMs", "RenderSubmissionCpuMs", "TotalCpuFrameMs")
        rows.append(dict(version=label,
                         phases={k: {m: distribution(r[k][m] for r in repeats) for m in ("Mean", "P95", "P99")} for k in fields},
                         gpuRenderMs=distribution(r["Gpu"]["RenderMs"]["Mean"] for r in repeats),
                         checksum=expected[0]))
    return rows


def fmt(value):
    return f'{value["median"]:.3f} [{value["minimum"]:.3f}, {value["maximum"]:.3f}]'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--paired-desktop", action="store_true")
    args = parser.parse_args()
    root = args.root.resolve()
    project = Path(__file__).resolve().parents[2]
    assert root.is_relative_to(project / "Benchmarks.Local" / "tmp")
    result = dict(kernel=kernel(root / "final-clean-pair" / "summary.json"),
                  diagnostics=diagnostics(root / "final2-diagnostic-pair" / "summary.json"),
                  native=native({"before": root / "native-before-endless.json", "final": root / "native-final-endless.json"}),
                  desktop=desktop(root, args.paired_desktop),
                  browser={name: browser(root / name) for name in ("web-final-endless.json", "web-low.json", "web-nominal.json", "web-high-clean.json")})
    suffix = "-paired" if args.paired_desktop else ""
    with (root / f"summary{suffix}.json").open("x", encoding="utf-8") as stream:
        json.dump(result, stream, indent=2)
    lines = ["# Physics Kernel Local Results", "", "Three independent warmed repeats per version; values are ms.",
             "Median of repeat means [minimum, maximum]; p95/p99 are median per-repeat tick percentiles, not independent ticks.",
             "Native micro uses .NET 8.0.31 FullOpts; desktop and real Mono AOT are separate. No cross-runtime checksum claim.", "",
             "## Native Synthetic", "", "| Scene | Version | Step | p95 | p99 | Hits (1024 sweeps) |", "|---|---|---:|---:|---:|---:|"]
    for scene in result["kernel"]:
        for label, v in scene["versions"].items():
            lines.append(f'| {scene["scenario"]} | {label} | {fmt(v["step"]["meanMs"])} | {fmt(v["step"]["p95Ms"])} | {fmt(v["step"]["p99Ms"])} | {fmt(v["hits"]["meanMs"])} |')
    lines += ["", "Fast scenes force eight substeps; dense-slow uses one. Inputs include mixed radii, disabled and moving targets, outside-grid bodies, both projectile geometries and owner filtering. These are not the historical Aether input scene.",
              "", "## Production Mono AOT", "", "| File/Profile | Version | End tick | Step | p95 | p99 | Physics | Hits | Export |", "|---|---|---:|---:|---:|---:|---:|---:|---:|"]
    for name, study in result["browser"].items():
        for r in study["rows"]:
            lines.append(f'| {name} | {r["version"]} | {r["endTick"]} | {fmt(r["step"]["meanMs"])} | {fmt(r["step"]["p95Ms"])} | {fmt(r["step"]["p99Ms"])} | {fmt(r["physics"])} | {fmt(r["hits"])} | {fmt(r["export"])} |')
    lines += ["", "## Native Endless", "", "| Version | Port | End tick | Step | p95 | p99 | Physics | Export |", "|---|---|---:|---:|---:|---:|---:|---:|"]
    for r in result["native"]:
        lines.append(f'| {r["version"]} | {r["port"]} | {r["endTick"]} | {fmt(r["step"]["MeanMs"])} | {fmt(r["step"]["P95Ms"])} | {fmt(r["step"]["P99Ms"])} | {fmt(r["physics"])} | {fmt(r["export"])} |')
    lines += ["", "## Desktop Renderer", "", "| Version | Phase | Mean | p95 | p99 |", "|---|---|---:|---:|---:|"]
    for r in result["desktop"]:
        for name, v in r["phases"].items():
            lines.append(f'| {r["version"]} | {name} | {fmt(v["Mean"])} | {fmt(v["P95"])} | {fmt(v["P99"])} |')
        lines.append(f'| {r["version"]} | GPU render | {fmt(r["gpuRenderMs"])} | n/a | n/a |')
    lines += ["", "## Instrumented Work", "", "| Scene | Version | Grid rebuilds | Membership visits | New constraints | Velocity visits | CCD narrow | Pruned intervals |", "|---|---|---:|---:|---:|---:|---:|---:|"]
    for scene in result["diagnostics"]:
        for label, v in scene["versions"].items():
            keys = ("grid.rebuilds", "grid.membershipsVisited", "work.newConstraints", "work.velocityConstraintVisits", "work.ccdNarrowTests", "work.prunedCcdIntervals")
            lines.append(f'| {scene["scenario"]} | {label} | ' + " | ".join(f'{v[k]["median"]:.1f}' for k in keys) + " |")
    lines += ["", "All numerical state/contact/impact signatures and game checkpoints/counters/populations match within their runtime. Reserved synthetic Step/hit allocations are zero; whole-game Step remains structurally allocating.",
              "Managed/process memory includes inputs and observers. Per-repeat memory and detailed timers/counters are in summary.json and raw directories.",
              "The pair buffer has a 2 MiB payload cap; larger constraint records add up to 640 KiB at the default contact reserve, plus 64 bytes of bounds per body. Actual reserved capacities can be smaller. There is no unbounded serial cache.",
              "Stage1 sort/stamp changes alone showed no reliable throughput win; cell-size probes had trade-offs. One-substep CCD does not benefit from later-interval pruning. Rendering and export are not counted as physics improvement.",
              "A preliminary eager CCD version regressed hit processing in real WASM (96-98% empty queries). Its measurements remain in web-endless.json; final code restores lazy preparation and avoids an extra hot rectangle wrapper.",
              "Dense real contacts and outside-window exact fallback retain a quadratic worst case. Full velocity passes remain: an unchanged connected graph can still have residual updates. No warm starting or fewer iterations were used.",
              "Historical published data, AI, balance, artwork and frontend were not edited; no commit/push/publication or unrelated process termination."]
    with (root / f"RESULTS{suffix}.md").open("x", encoding="utf-8") as stream:
        stream.write(colorize_markdown("\n".join(lines) + "\n", root / f"RESULTS{suffix}.md", result))
    print(root / f"RESULTS{suffix}.md")


if __name__ == "__main__":
    main()
