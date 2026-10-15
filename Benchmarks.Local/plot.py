#!/usr/bin/env python3
"""Render the local ECS benchmark results as a comparison chart."""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any

import matplotlib
from matplotlib.ticker import FixedLocator, FuncFormatter, LogLocator, NullFormatter

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
from benchmark_colors import EQUAL_COLOR, PALETTE, colors


FRAMEWORKS = ("SKECS", "LeoECS Lite", "LeoECS Classic", "Morpeh", "StaticEcs")
SCENARIOS = ("Create", "Iterate", "Churn", "Destroy")
ENTITY_COUNTS = (1000, 100000)
REQUIRED_FIELDS = ("framework", "scenario", "entityCount", "meanNs", "stddevNs", "samplesNs", "allocatedBytes")


class InputError(ValueError):
    pass


def finite_number(value: Any, field: str, *, allow_zero: bool = False) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise InputError(f"{field} must be a number")
    result = float(value)
    if not math.isfinite(result) or (result < 0 if allow_zero else result <= 0):
        qualifier = "non-negative" if allow_zero else "positive"
        raise InputError(f"{field} must be finite and {qualifier}")
    return result


def validate(data: Any) -> tuple[dict[str, Any], dict[tuple[str, str, int], dict[str, Any]]]:
    if not isinstance(data, dict) or not isinstance(data.get("metadata"), dict):
        raise InputError("top-level object must contain a metadata object")
    rows = data.get("results")
    if not isinstance(rows, list):
        raise InputError("top-level object must contain a results array")

    indexed: dict[tuple[str, str, int], dict[str, Any]] = {}
    expected = {(framework, scenario, count) for framework in FRAMEWORKS for scenario in SCENARIOS for count in ENTITY_COUNTS}
    for i, row in enumerate(rows):
        if not isinstance(row, dict):
            raise InputError(f"results[{i}] must be an object")
        missing = [field for field in REQUIRED_FIELDS if field not in row]
        if missing:
            raise InputError(f"results[{i}] missing fields: {', '.join(missing)}")
        framework, scenario, count = row["framework"], row["scenario"], row["entityCount"]
        if framework == "SECS":  # Keep preserved benchmark captures readable.
            framework = "SKECS"
        if framework not in FRAMEWORKS:
            raise InputError(f"results[{i}].framework is unexpected: {framework!r}")
        if scenario not in SCENARIOS:
            raise InputError(f"results[{i}].scenario is unexpected: {scenario!r}")
        if isinstance(count, bool) or not isinstance(count, int) or count <= 0:
            raise InputError(f"results[{i}].entityCount must be a positive integer")
        key = (framework, scenario, count)
        if key in indexed:
            raise InputError(f"duplicate result case: {key}")
        if key not in expected:
            raise InputError(f"unexpected result case: {key}")
        mean = finite_number(row["meanNs"], f"results[{i}].meanNs")
        stddev = finite_number(row["stddevNs"], f"results[{i}].stddevNs", allow_zero=True)
        allocated = finite_number(row["allocatedBytes"], f"results[{i}].allocatedBytes", allow_zero=True)
        samples = row["samplesNs"]
        if not isinstance(samples, list) or not samples:
            raise InputError(f"results[{i}].samplesNs must be a non-empty array")
        for j, sample in enumerate(samples):
            finite_number(sample, f"results[{i}].samplesNs[{j}]")
        indexed[key] = {"meanNs": mean, "stddevNs": stddev, "allocatedBytes": allocated}

    missing_cases = expected - indexed.keys()
    if missing_cases:
        example = sorted(missing_cases)[0]
        raise InputError(f"missing {len(missing_cases)} result case(s), including {example}")
    return data["metadata"], indexed


def metadata_footer(metadata: dict[str, Any]) -> str:
    aliases = {
        ".NET/runtime": ("runtime", "runtimeVersion", "dotnetRuntime", "dotnetVersion", "frameworkDescription"),
        "Date": ("date", "timestamp", "createdAt", "runDate"),
        "Machine": ("machine", "machineName", "host", "computerName"),
    }
    parts = []
    for label, keys in aliases.items():
        value = next((metadata[k] for k in keys if metadata.get(k) not in (None, "")), None)
        if value is not None:
            if label == "Machine":
                # Historical inputs may still contain a private hostname.
                value = "local-machine"
            parts.append(f"{label}: {value}")
    extra = metadata.get("footer") or metadata.get("description")
    if extra:
        parts.append(str(extra))
    if not parts:
        return "Benchmark metadata unavailable"
    return "\n".join("   |   ".join(parts[index:index + 2]) for index in range(0, len(parts), 2))


def render(metadata: dict[str, Any], results: dict[tuple[str, str, int], dict[str, Any]], output: Path) -> None:
    fig, axes = plt.subplots(2, 4, figsize=(20, 8.5), facecolor="white")
    fig.patch.set_facecolor("white")
    y = np.arange(len(FRAMEWORKS))

    for row_index, count in enumerate(ENTITY_COUNTS):
        for col_index, scenario in enumerate(SCENARIOS):
            ax = axes[row_index, col_index]
            ax.set_facecolor("white")
            values = []
            panel_colors = colors([results[(framework, scenario, count)]["meanNs"] for framework in FRAMEWORKS])
            upper_bounds = []
            lower_bounds = []
            for framework_index, framework in enumerate(FRAMEWORKS):
                result = results[(framework, scenario, count)]
                value = result["meanNs"] / count
                error = result["stddevNs"] / count
                values.append(value)
                upper_bounds.append(value + error)
                lower_bounds.append(max(value - error, value * 0.01))
                ax.barh(
                    framework_index,
                    value,
                    xerr=error,
                    color=panel_colors[framework_index],
                    edgecolor="white",
                    height=0.64,
                    error_kw={"ecolor": "#30343B", "elinewidth": 1.1, "capsize": 3},
                )
                ax.annotate(f"{value:,.3g}", (value + error, framework_index), xytext=(5, 0),
                            textcoords="offset points", va="center", fontsize=8, clip_on=False)
            ax.set_xscale("log")
            x_min = min(lower_bounds) * 0.8
            x_max = max(upper_bounds) * 1.8
            ax.set_xlim(x_min, x_max)
            ax.xaxis.set_major_locator(FixedLocator(np.geomspace(x_min, x_max, 4)))
            ax.xaxis.set_major_formatter(FuncFormatter(lambda value, _: f"{value:.3g}"))
            ax.xaxis.set_minor_locator(LogLocator(base=10, subs=np.arange(2, 10) * 0.1))
            ax.xaxis.set_minor_formatter(NullFormatter())
            title = f"{scenario}\n{count:,} entities" if col_index == 0 else scenario
            ax.set_title(title, fontsize=11, fontweight="semibold", pad=9)
            ax.set_yticks(y, FRAMEWORKS)
            ax.invert_yaxis()
            ax.grid(axis="x", which="both", color="#D9DEE5", linewidth=0.7)
            ax.set_axisbelow(True)
            ax.spines[["top", "right", "left"]].set_visible(False)
            ax.spines["bottom"].set_color("#AAB2BD")
            ax.tick_params(axis="y", length=0, labelsize=9)
            ax.tick_params(axis="x", labelsize=8, colors="#4B5563")
            ax.set_xlabel("ns / entity (log scale, lower is better)", fontsize=8, color="#4B5563")
    skecs_revision = str(metadata.get("revisions", {}).get("SKECS", metadata.get("revisions", {}).get("SECS", "")))
    title = "ECS Framework Comparison: Optimized SKECS" if "working tree" in skecs_revision else "ECS Framework Benchmark Comparison"
    fig.suptitle(title, fontsize=16, fontweight="bold", y=0.98, color="#20252B")
    fig.text(0.5, 0.942, "Error bars: +/-1 SD   |   Create: 2 components   |   Churn: add/remove third component",
             ha="center", va="top", fontsize=9, color="#4B5563")
    fig.legend(handles=[plt.Rectangle((0, 0), 1, 1, color=dict(PALETTE)[name]) for name in ("best", "middle", "worst")]
               + [plt.Rectangle((0, 0), 1, 1, color=EQUAL_COLOR)],
               labels=("≤5% lag", "15% lag", "≥30% lag", "within 5% / neutral"),
               loc="upper center", bbox_to_anchor=(0.5, 0.916), ncol=4, frameon=False, fontsize=8)
    fig.text(0.5, 0.045, metadata_footer(metadata), ha="center", va="bottom", fontsize=8, color="#4B5563", linespacing=1.4)
    static_revision = metadata.get("revisions", {}).get("staticEcs", "")
    static_version = {
        "6323d710c22cfff70100fe3748de432cb5fc70c8": "1.2.01",
        "23b1cf36d89497e8a126d38459d688e2b0a5dea3": "2.2.9",
    }.get(static_revision, f"source {str(static_revision)[:8]}")
    fig.text(0.5, 0.012, f"LeoECS Lite 2024.5.22 | Classic 2023.6.22 | Morpeh 2024.1.1 | StaticEcs {static_version}",
             ha="center", va="bottom", fontsize=8, color="#4B5563")
    fig.subplots_adjust(left=0.12, right=0.985, top=0.86, bottom=0.17, wspace=0.50, hspace=0.36)
    output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output, dpi=180, facecolor="white", bbox_inches="tight")
    plt.close(fig)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    here = Path(__file__).resolve().parent
    parser.add_argument("input", nargs="?", type=Path, default=here / "results.json", help="benchmark results JSON")
    parser.add_argument("-o", "--output", type=Path, default=here.parent / "docs" / "benchmarks" / "comparison.png", help="output PNG path")
    args = parser.parse_args()
    try:
        with args.input.open("r", encoding="utf-8") as source:
            metadata, results = validate(json.load(source))
        render(metadata, results, args.output)
    except (OSError, json.JSONDecodeError, InputError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    print(f"Wrote {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
