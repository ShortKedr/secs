"""Verify that a GitHub draft contains precisely the planned release assets."""

import json
import sys
import argparse
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("state", type=Path)
    parser.add_argument("--version", required=True)
    parser.add_argument("--cover", required=True)
    parser.add_argument("--published", action="store_true", help="Require a published release instead of a draft")
    args = parser.parse_args()
    state = json.loads(args.state.read_text(encoding="utf-8"))
    expected = {
        f"ShortKedr.SKECS.{args.version}.nupkg",
        f"SKECS.Unity.{args.version}.zip",
        "SHA256SUMS.txt",
        f"{args.cover}.png",
    }
    assets = state.get("assets")
    actual = [asset.get("name") for asset in assets] if isinstance(assets, list) else []
    expected_draft = not args.published
    if state.get("isDraft") is not expected_draft or len(actual) != len(expected) or set(actual) != expected:
        phase = "draft" if expected_draft else "published release"
        print(f"FAIL: expected {phase} with {sorted(expected)}, got {state}", file=sys.stderr)
        return 1
    print(f"PASS: {'draft' if expected_draft else 'published release'} has exactly {len(expected)} required assets")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
