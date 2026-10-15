"""Record a successful package-backed consumer run for release-note provenance."""

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--kind", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--commit", required=True)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--runtime", required=True)
    parser.add_argument("--os", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9a-fA-F]{40}", args.commit) or not args.run_id.isdigit():
        print("FAIL: invalid source commit or GitHub run ID", file=sys.stderr)
        return 1
    if not all(value.strip() for value in (args.kind, args.version, args.runtime, args.os)):
        print("FAIL: empty evidence field", file=sys.stderr)
        return 1
    if not args.package.is_file():
        print(f"FAIL: package does not exist: {args.package}", file=sys.stderr)
        return 1
    data = {
        "kind": args.kind,
        "packageVersion": args.version,
        "sourceCommit": args.commit.lower(),
        "packageSha256": hashlib.sha256(args.package.read_bytes()).hexdigest(),
        "runId": args.run_id,
        "runtime": args.runtime.strip(),
        "os": args.os.strip(),
        "result": "passed",
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(data, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"PASS: recorded {args.kind}: {args.runtime} on {args.os}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
