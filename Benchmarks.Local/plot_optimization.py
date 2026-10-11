#!/usr/bin/env python3
"""Plot paired SECS timings before and after the local optimization."""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.ticker import FuncFormatter, LogLocator, NullFormatter
import numpy as np


BASE_SCENARIOS = ("Create", "Iterate", "Churn", "Destroy")
STRESS_SCENARIOS = (
    "DenseIteration",
    "SparseIteration",
    "WideIteration",
    "RandomLookup",
    "PayloadChurn",
    "Recycle10Percent",
    "MixedFrames",
)
BASE_COUNTS = (1_000, 100_000)
STRESS_COUNTS = (100_000, 1_000_000)
OLD_COLOR = "#64748B"
NEW_COLOR = "#16836B"
REQUIRED_FIELDS = ("framework", "scenario", "entityCount", "meanNs")


class InputError(ValueError):
    pass


def positive_number(value: Any, field: str, *, allow_zero: bool = False) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise InputError(f"{field} must be a number")
    result = float(value)
    if not math.isfinite(result) or (result < 0 if allow_zero else result <= 0):
        qualifier = "non-negative" if allow_zero else "positive"
        raise InputError(f"{field} must be finite and {qualifier}")
    return result


def load_secs(path: Path, allowed: set[tuple[str, int]]) -> tuple[dict[str, Any], dict[tuple[str, int], dict[str, float]]]:
    try:
        with path.open("r", encoding="utf-8") as source:
            data = json.load(source)
    except (OSError, json.JSONDecodeError) as exc:
        raise InputError(f"{path}: {exc}") from exc

    if not isinstance(data, dict) or not isinstance(data.get("metadata"), dict):
        raise InputError(f"{path}: top-level object must contain a metadata object")
    rows = data.get("results")
    if not isinstance(rows, list):
        raise InputError(f"{path}: top-level object must contain a results array")

    indexed: dict[tuple[str, int], dict[str, float]] = {}
    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            raise InputError(f"{path}: results[{index}] must be an object")
        if row.get("framework") != "SECS":
            continue
        missing = [field for field in REQUIRED_FIELDS if field not in row]
        if missing:
            raise InputError(f"{path}: results[{index}] missing fields: {', '.join(missing)}")
        scenario, count = row["scenario"], row["entityCount"]
        if not isinstance(scenario, str) or isinstance(count, bool) or not isinstance(count, int) or count <= 0:
            raise InputError(f"{path}: results[{index}] has an invalid scenario or entityCount")
        key = (scenario, count)
        if key not in allowed:
            continue
        if key in indexed:
            raise InputError(f"{path}: duplicate SECS result case {key}")
        result = {"meanNs": positive_number(row["meanNs"], f"results[{index}].meanNs")}
        if "stddevNs" in row:
            result["stddevNs"] = positive_number(row["stddevNs"], f"results[{index}].stddevNs", allow_zero=True)
        indexed[key] = result
    return data["metadata"], indexed


def number_label(value: float) -> str:
    digits = 6 if value < 1 else 3
    return f"{value:,.{digits}f}".rstrip("0").rstrip(".")


def plot_pair(ax: Any, old: dict[str, float], new: dict[str, float], *, stress: bool) -> None:
    factor = 1_000_000 if stress or max(old["meanNs"], new["meanNs"]) >= 1_000_000 else 1_000
    unit = "ms" if factor == 1_000_000 else "us"
    old_value, new_value = old["meanNs"] / factor, new["meanNs"] / factor
    old_error = old.get("stddevNs", 0.0) / factor
    new_error = new.get("stddevNs", 0.0) / factor
    speedup = old["meanNs"] / new["meanNs"]

    ax.set_xscale("log")
    ax.set_xlim(min(old_value, new_value) * 0.42, max(old_value, new_value) * 3.8)
    ax.barh(1, old_value, xerr=old_error, color=OLD_COLOR, height=0.48, label="Original",
            error_kw={"ecolor": "#30343B", "elinewidth": 1, "capsize": 2})
    ax.barh(0, new_value, xerr=new_error, color=NEW_COLOR, height=0.48, label="Optimized",
            error_kw={"ecolor": "#30343B", "elinewidth": 1, "capsize": 2})
    ax.set_yticks((1, 0), ("Original", "Optimized"))
    ax.set_ylim(-0.55, 1.55)
    ax.set_xlabel(f"{unit} / operation (log scale)", fontsize=8, color="#475569")
    ax.annotate(number_label(old_value), (old_value, 1), xytext=(5, 0),
                textcoords="offset points", va="center", fontsize=8, color="#334155", clip_on=False)
    ax.annotate(number_label(new_value), (new_value, 0), xytext=(5, 0),
                textcoords="offset points", va="center", fontsize=8, color="#146C58", clip_on=False)
    callout = f"{speedup:.2f}x speedup" if speedup >= 1 else f"{1 / speedup:.2f}x slower"
    ax.text(0.98, 0.96, f"old / new  {speedup:.2f}x\n{callout}", transform=ax.transAxes,
            ha="right", va="top", fontsize=8, color="#20252B",
            bbox={"boxstyle": "round,pad=0.3", "facecolor": "white", "edgecolor": "#D9DEE5"})
    ax.grid(axis="x", which="both", color="#E2E8F0", linewidth=0.7)
    ax.set_axisbelow(True)
    ax.spines[["top", "right", "left"]].set_visible(False)
    ax.spines["bottom"].set_color("#AAB2BD")
    ax.tick_params(axis="y", length=0, labelsize=8)
    ax.tick_params(axis="x", labelsize=7, colors="#64748B")
    ax.xaxis.set_major_locator(LogLocator(base=10, subs=(1, 2, 5), numticks=6))
    ax.xaxis.set_major_formatter(FuncFormatter(lambda value, _: number_label(value)))
    ax.xaxis.set_minor_formatter(NullFormatter())


def render(old_results: dict[tuple[str, int], dict[str, float]],
           new_results: dict[tuple[str, int], dict[str, float]], output: Path) -> int:
    rows = [(scenario, count, False) for scenario in BASE_SCENARIOS for count in BASE_COUNTS]
    rows += [(scenario, count, True) for scenario in STRESS_SCENARIOS for count in STRESS_COUNTS]
    fig, axes = plt.subplots(len(rows), 1, figsize=(11.5, 21), facecolor="white", squeeze=False)
    fig.patch.set_facecolor("white")

    for index, (scenario, count, stress) in enumerate(rows):
        ax = axes[index, 0]
        key = (scenario, count)
        if key in old_results and key in new_results:
            plot_pair(ax, old_results[key], new_results[key], stress=stress)
        else:
            ax.set_axis_off()
            absent = "original and optimized" if key not in old_results and key not in new_results else (
                "original" if key not in old_results else "optimized")
            ax.text(0.02, 0.5, f"No matched SECS pair ({absent} case missing)", transform=ax.transAxes,
                    va="center", color="#64748B", fontsize=9)
        ax.set_title(f"{'Stress' if stress else 'Base'}  |  {scenario}  |  {count:,} entities",
                     loc="left", fontsize=9, fontweight="semibold", color="#20252B", pad=3)

    matched = sum(1 for scenario, count, _ in rows if (scenario, count) in old_results and (scenario, count) in new_results)
    fig.suptitle("SECS Optimization: Original vs Optimized", x=0.055, y=0.997, ha="left",
                 fontsize=16, fontweight="bold", color="#17212B")
    fig.text(0.055, 0.986,
             f"{matched} matched cases  |  labels show mean time  |  whiskers show +/-1 SD when present  |  lower time is better",
             ha="left", va="top", fontsize=9, color="#475569")
    fig.text(0.055, 0.974, ".NET 8.0.31 x64 | native SECS | original 872e0df vs optimized working tree | 20 base / 8 stress samples",
             ha="left", va="top", fontsize=8, color="#475569")
    fig.subplots_adjust(left=0.19, right=0.97, top=0.958, bottom=0.025, hspace=0.92)
    output.parent.mkdir(parents=True, exist_ok=True)
    fig.savefig(output, dpi=180, facecolor="white", bbox_inches="tight")
    plt.close(fig)
    return matched


def main() -> int:
    here = Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-results", type=Path, default=here / "results-baseline-repeat.json")
    parser.add_argument("--base-stress", type=Path, default=here / "stress-baseline-repeat.json")
    parser.add_argument("--optimized-results", type=Path, default=here / "results-optimized.json")
    parser.add_argument("--optimized-stress", type=Path, default=here / "stress-results-optimized.json")
    parser.add_argument("-o", "--output", type=Path, default=here.parent / "docs" / "benchmarks" / "optimization.png")
    args = parser.parse_args()

    allowed_base = {(scenario, count) for scenario in BASE_SCENARIOS for count in BASE_COUNTS}
    allowed_stress = {(scenario, count) for scenario in STRESS_SCENARIOS for count in STRESS_COUNTS}
    try:
        _, old_base = load_secs(args.base_results, allowed_base)
        _, new_base = load_secs(args.optimized_results, allowed_base)
        _, old_stress = load_secs(args.base_stress, allowed_stress)
        _, new_stress = load_secs(args.optimized_stress, allowed_stress)
    except InputError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2
    old_results, new_results = old_base | old_stress, new_base | new_stress
    matched = sum(1 for key in old_results.keys() & new_results.keys())
    if matched == 0:
        print("error: no matching SECS cases found; no chart written", file=sys.stderr)
        return 2
    matched = render(old_results, new_results, args.output)
    print(f"Wrote {args.output} ({matched} matched SECS cases)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
