#!/usr/bin/env python3
"""Measure process startup latency for a command, reproducibly enough to compare two builds.

Startup is the number this spike exists to learn, and it is the number easiest to measure
badly. Three properties matter more than precision:

* the *median* is reported, not the mean, because a single scheduler hiccup or an antivirus
  scan on a Windows runner moves a mean by tens of milliseconds and moves a median by none;
* warmup runs are discarded, because the first execution of a freshly written file pays for
  page cache misses that no user experiences twice;
* the command must exit 0 every time, and when it does not, its standard error is reported
  rather than swallowed. A build that fails fast looks wonderfully quick; a harness that hid
  the reason would report the failure as a win, and this spike exists to produce blockers.

Output is one JSON object on standard output, so the workflow can archive it and a later run
can be compared against it mechanically.
"""

from __future__ import annotations

import argparse
import json
import statistics
import subprocess
import sys
import time


def measure(command: list[str], runs: int, warmup: int) -> dict[str, object]:
    """Run *command* repeatedly and return its startup statistics in milliseconds."""
    samples: list[float] = []
    for index in range(warmup + runs):
        started = time.perf_counter()
        completed = subprocess.run(
            command,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.PIPE,
            check=False,
        )
        elapsed_ms = (time.perf_counter() - started) * 1000.0
        if completed.returncode != 0:
            detail = completed.stderr.decode("utf-8", errors="replace").strip()
            raise SystemExit(
                f"{command[0]} exited {completed.returncode} on iteration {index}; "
                "a failing command cannot be timed meaningfully.\n"
                f"Its standard error was:\n{detail or '(nothing)'}"
            )
        if index >= warmup:
            samples.append(elapsed_ms)

    return {
        "command": command,
        "runs": runs,
        "warmup": warmup,
        "median_ms": round(statistics.median(samples), 2),
        "min_ms": round(min(samples), 2),
        "max_ms": round(max(samples), 2),
        "mean_ms": round(statistics.fmean(samples), 2),
        "stdev_ms": round(statistics.stdev(samples), 2) if len(samples) > 1 else 0.0,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Measure command startup latency.")
    parser.add_argument("--label", required=True, help="Name for this configuration.")
    parser.add_argument("--runs", type=int, default=20, help="Timed iterations.")
    parser.add_argument("--warmup", type=int, default=3, help="Discarded iterations.")
    parser.add_argument("command", nargs=argparse.REMAINDER, help="Command, after --.")
    args = parser.parse_args()

    command = [token for token in args.command if token != "--"]
    if not command:
        parser.error("no command supplied; pass it after --")

    result = measure(command, args.runs, args.warmup)
    result["label"] = args.label
    json.dump(result, sys.stdout, indent=2, sort_keys=True)
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
