#!/usr/bin/env python3
"""Render the stress benchmark matrix as one PNG per entity count."""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any

import matplotlib

matplotlib.use("Agg")
from matplotlib.ticker import FixedLocator, FuncFormatter, LogLocator, NullFormatter
import matplotlib.pyplot as plt
import numpy as np

from plot import COLORS, FRAMEWORKS, InputError, metadata_footer


SCENARIOS = (
    "DenseIteration",
    "SparseIteration",
    "WideIteration",
    "RandomLookup",
    "PayloadChurn",
    "Recycle10Percent",
    "MixedFrames",
)
ENTITY_COUNTS = (100000, 1000000)
REQUIRED_FIELDS = ("framework", "scenario", "entityCount", "meanNs", "stddevNs", "samplesNs")


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

    expected = {(framework, scenario, count) for framework in FRAMEWORKS for scenario in SCENARIOS for count in ENTITY_COUNTS}
    indexed: dict[tuple[str, str, int], dict[str, Any]] = {}
    for i, row in enumerate(rows):
        if not isinstance(row, dict):
            raise InputError(f"results[{i}] must be an object")
        missing = [field for field in REQUIRED_FIELDS if field not in row]
        if missing:
            raise InputError(f"results[{i}] missing fields: {', '.join(missing)}")
        framework, scenario, count = row["framework"], row["scenario"], row["entityCount"]
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
        samples = row["samplesNs"]
        if not isinstance(samples, list) or len(samples) < 5:
            raise InputError(f"results[{i}].samplesNs must contain at least 5 samples")
        for j, sample in enumerate(samples):
            finite_number(sample, f"results[{i}].samplesNs[{j}]")
        indexed[key] = {"meanNs": mean, "stddevNs": stddev}

    missing_cases = expected - indexed.keys()
    if missing_cases:
        raise InputError(f"missing {len(missing_cases)} result case(s), including {sorted(missing_cases)[0]}")
    return data["metadata"], indexed


def style_axis(ax: Any, values: list[float], errors: list[float]) -> None:
    uppers = [value + error for value, error in zip(values, errors)]
    lowers = [max(value - error, value * 0.01) for value, error in zip(values, errors)]
    x_min = min(lowers) * 0.8
    x_max = max(uppers) * 1.8
    ax.set_xscale("log")
    ax.set_xlim(x_min, x_max)
    ax.xaxis.set_major_locator(FixedLocator(np.geomspace(x_min, x_max, 4)))
    ax.xaxis.set_major_formatter(FuncFormatter(lambda value, _: f"{value:,.0f}" if value >= 1000 else f"{value:.3g}"))
    ax.xaxis.set_minor_locator(LogLocator(base=10, subs=np.arange(2, 10) * 0.1))
    ax.xaxis.set_minor_formatter(NullFormatter())
    ax.grid(axis="x", which="both", color="#D9DEE5", linewidth=0.7)
    ax.set_axisbelow(True)
    ax.spines[["top", "right", "left"]].set_visible(False)
    ax.spines["bottom"].set_color("#AAB2BD")
    ax.tick_params(axis="y", length=0, labelsize=8)
    ax.tick_params(axis="x", labelsize=8, colors="#4B5563")
    ax.set_xlabel("milliseconds / operation (log scale)", fontsize=8, color="#4B5563")


def render_count(metadata: dict[str, Any], results: dict[tuple[str, str, int], dict[str, Any]], count: int, output: Path) -> None:
    fig, axes = plt.subplots(2, 4, figsize=(16, 8.5), facecolor="white")
    fig.patch.set_facecolor("white")
    y = np.arange(len(FRAMEWORKS))

    for panel_index, scenario in enumerate(SCENARIOS):
        ax = axes.flat[panel_index]
        ax.set_facecolor("white")
        values, errors = [], []
        for framework_index, framework in enumerate(FRAMEWORKS):
            result = results[(framework, scenario, count)]
            value = result["meanNs"] / 1_000_000
            error = result["stddevNs"] / 1_000_000
            values.append(value)
            errors.append(error)
            ax.barh(framework_index, value, xerr=error, color=COLORS[framework], edgecolor="white", height=0.64,
                    error_kw={"ecolor": "#30343B", "elinewidth": 1.1, "capsize": 3})
            label = f"{value:,.0f}" if value >= 1000 else f"{value:,.3g}"
            ax.annotate(label, (value + error, framework_index), xytext=(5, 0), textcoords="offset points",
                        va="center", fontsize=8, clip_on=False)
        style_axis(ax, values, errors)
        ax.set_title(scenario, fontsize=10, fontweight="semibold", pad=8)
        ax.set_yticks(y, FRAMEWORKS)
        ax.invert_yaxis()

    method_ax = axes.flat[7]
    method_ax.set_axis_off()
    method_ax.text(0.04, 0.94, "Methodology", transform=method_ax.transAxes, fontsize=11, fontweight="semibold",
                   color="#20252B", va="top")
    method_ax.text(0.04, 0.82,
                   "256 layouts\n256-byte payload\n1% hot entities\n120 mixed frames = one operation",
                   transform=method_ax.transAxes, fontsize=10, color="#30343B", va="top", linespacing=1.8)
    versions = metadata.get("versions", {})
    version_labels = (
        ("lite", "LeoECS Lite"),
        ("classic", "Classic"),
        ("morpeh", "Morpeh"),
        ("staticEcs", "StaticEcs"),
        ("staticPack", "StaticPack"),
    )
    version_lines = [f"{label}: {versions[key]}" for key, label in version_labels
                     if isinstance(versions, dict) and versions.get(key) not in (None, "")]
    if version_lines:
        method_ax.text(0.04, 0.38, "Library versions", transform=method_ax.transAxes, fontsize=9,
                       fontweight="semibold", color="#20252B", va="top")
        method_ax.text(0.04, 0.29, "\n".join(version_lines), transform=method_ax.transAxes, fontsize=8,
                       color="#4B5563", va="top", linespacing=1.5)

    secs_revision = str(metadata.get("versions", {}).get("SECS", ""))
    title = f"ECS Stress Benchmarks: {count:,} entities"
    if "working tree" in secs_revision:
        title += " | Optimized SECS"
    fig.suptitle(title, fontsize=16, fontweight="bold", y=0.98, color="#20252B")
    fig.text(0.5, 0.942, "Error bars: +/-1 SD   |   256 layouts   |   256-byte payload   |   1% hot entities   |   MixedFrames: 120 frames / operation",
             ha="center", va="top", fontsize=8.5, color="#4B5563")
    fig.text(0.5, 0.026, metadata_footer(metadata), ha="center", va="bottom", fontsize=8, color="#4B5563", linespacing=1.4)
    fig.subplots_adjust(left=0.16, right=0.985, top=0.88, bottom=0.17, wspace=0.30, hspace=0.36)
    output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output, dpi=180, facecolor="white", bbox_inches="tight")
    plt.close(fig)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    here = Path(__file__).resolve().parent
    parser.add_argument("input", nargs="?", type=Path, default=here / "stress-results.json", help="stress results JSON")
    parser.add_argument("--output-dir", type=Path, default=here.parent / "docs" / "benchmarks", help="directory for PNG outputs")
    args = parser.parse_args()
    try:
        with args.input.open("r", encoding="utf-8") as source:
            metadata, results = validate(json.load(source))
        for count, suffix in ((100000, "100k"), (1000000, "1m")):
            render_count(metadata, results, count, args.output_dir / f"stress-{suffix}.png")
    except (OSError, json.JSONDecodeError, InputError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    print(f"Wrote stress charts to {args.output_dir}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
