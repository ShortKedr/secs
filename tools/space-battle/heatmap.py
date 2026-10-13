"""Compatibility import for the common benchmark colour scale."""

from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from benchmark_colors import *  # noqa: F401,F403
