"""Measure what an Ansible filter call actually costs.

Issue #37 asks two separate questions and they need separate numbers:

* **Per-invocation cost**, which decides whether a filter running once per host per file is viable
  at fleet scale, and whether Native AOT (#28) is worth its costs here.
* **The cost of temporary-file marshalling specifically**, which is the evidence that would reopen
  the deferred ``--stdout`` decision (G4, #26). Nothing else in this repository measures it.

Four timings, chosen so the second question has an answer rather than an argument:

===========  ==========================================================================
``bare``     ``--version`` and nothing else. The process floor: startup, host resolution,
             assembly loading. No marshalling, no work.
``prepared`` The tool spawned over files that already exist, and the output read back. The
             floor plus the real transformation, with no marshalling.
``full``     A whole filter call: temporary directory, two writes, the spawn, the read, the
             delete. This is what a playbook actually pays.
``memoized`` A cache hit. No process at all.
===========  ==========================================================================

``full - prepared`` is the marshalling overhead, and it is the number G4 turns on. ``bare`` says
how much of what remains is process startup rather than work, which is the number #28 turns on.

Usage::

    python spikes/ansible/measure-invocation.py --runs 20 --out timings.json

Following the trap recorded in ``spikes/native-aot/FINDINGS.md``: a failing run prints the tool's
standard error. A harness that hides the failure it detected inverts its own purpose.
"""

import argparse
import json
import os
import shutil
import statistics
import subprocess
import sys
import tempfile
import time

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "filter_plugins"))

# See test_filter.py: nothing under spikes/ can be gitignored, so no __pycache__ may be left behind.
sys.dont_write_bytecode = True

import namespace2xml_filter as n2x  # noqa: E402

# A fleet shape from the issue: 500 hosts x 3 files. Memoization collapses it to the number of
# distinct (data, scheme) pairs, which for a role applied uniformly is 3.
FLEET_HOSTS = 500
FLEET_FILES = 3


def synthetic_config(leaves):
    """Build a config with roughly ``leaves`` scalar leaves, shaped like real configuration.

    Nested mappings with a sequence at each level, because a flat dictionary of N keys exercises
    neither the sequence inference of Section 8.7 nor any depth.
    """
    config = {}

    for index in range(leaves):
        group = "group%d" % (index // 8)
        entry = config.setdefault(group, {"items": [], "enabled": True})
        entry["items"].append({"name": "item%d" % index, "value": index, "level": "DEBUG"})

    return config


def time_calls(label, thunk, runs, warmups):
    """Time a callable, discarding warmups, and return statistics in milliseconds."""
    for _ in range(warmups):
        thunk()

    samples = []

    for _ in range(runs):
        start = time.perf_counter()
        thunk()
        samples.append((time.perf_counter() - start) * 1000.0)

    samples.sort()

    return {
        "label": label,
        "runs": runs,
        "median_ms": round(statistics.median(samples), 3),
        "min_ms": round(samples[0], 3),
        "max_ms": round(samples[-1], 3),
        "stdev_ms": round(statistics.stdev(samples), 3) if len(samples) > 1 else 0.0,
    }


def measure(config, runs, warmups, executable):
    """Produce the four timings for one config."""
    profile = n2x.flatten(config, "cfg")
    scheme = n2x.synthesize_scheme("xml", "cfg", "configuration")

    def bare():
        completed = subprocess.run(
            [executable, "--version"], capture_output=True, text=True, check=False)

        if completed.returncode != 0:
            raise SystemExit(
                "'%s --version' exited %d:\n%s"
                % (executable, completed.returncode, (completed.stderr or "").strip()))

    directory = tempfile.mkdtemp(prefix="n2x-prepared-")

    try:
        input_path = os.path.join(directory, "input.txt")
        scheme_path = os.path.join(directory, "scheme.txt")
        output_dir = os.path.join(directory, "out")
        os.mkdir(output_dir)

        for path, text in ((input_path, profile), (scheme_path, scheme)):
            with open(path, "w", encoding="utf-8", newline="\n") as handle:
                handle.write(text)

        def prepared():
            n2x._run_and_read(executable, input_path, scheme_path, output_dir)

        def full():
            n2x.render(config, "xml", root="configuration", tool=executable, memoize=False)

        # Prime the memo once so the memoized timing is a hit rather than a miss.
        n2x.render(config, "xml", root="configuration", tool=executable, memoize=True)

        def memoized():
            n2x.render(config, "xml", root="configuration", tool=executable, memoize=True)

        return {
            "profile_bytes": len(profile.encode("utf-8")),
            "profile_records": profile.count("\n"),
            "bare": time_calls("bare", bare, runs, warmups),
            "prepared": time_calls("prepared", prepared, runs, warmups),
            "full": time_calls("full", full, runs, warmups),
            "memoized": time_calls("memoized", memoized, runs, warmups),
        }
    finally:
        shutil.rmtree(directory, ignore_errors=True)


def derive(result):
    """Turn the four timings into the two answers the issue asks for."""
    bare = result["bare"]["median_ms"]
    prepared = result["prepared"]["median_ms"]
    full = result["full"]["median_ms"]
    memoized = result["memoized"]["median_ms"]

    calls = FLEET_HOSTS * FLEET_FILES

    return {
        "marshalling_overhead_ms": round(full - prepared, 3),
        "marshalling_share_of_call": round((full - prepared) / full, 4) if full else 0.0,
        "startup_share_of_call": round(bare / full, 4) if full else 0.0,
        "work_ms": round(prepared - bare, 3),
        "fleet_calls": calls,
        "fleet_seconds_uncached": round(calls * full / 1000.0, 2),
        "fleet_seconds_memoized": round(
            (FLEET_FILES * full + (calls - FLEET_FILES) * memoized) / 1000.0, 2),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runs", type=int, default=15, help="timed runs per measurement")
    parser.add_argument("--warmups", type=int, default=3, help="discarded runs per measurement")
    parser.add_argument(
        "--leaves", type=int, nargs="+", default=[8, 200],
        help="config sizes to measure, in scalar leaves")
    parser.add_argument("--out", default=None, help="write the full results as JSON here")
    arguments = parser.parse_args()

    executable = os.environ.get("NAMESPACE2XML", "namespace2xml")

    report = {
        "tool": executable,
        "identity": n2x.tool_identity(executable),
        "platform": sys.platform,
        "python": sys.version.split()[0],
        "measurements": [],
    }

    print("tool:     %s" % executable)
    print("identity: %s" % report["identity"])
    print("platform: %s\n" % sys.platform)

    for leaves in arguments.leaves:
        config = synthetic_config(leaves)
        result = measure(config, arguments.runs, arguments.warmups, executable)
        result["leaves"] = leaves
        result["derived"] = derive(result)
        report["measurements"].append(result)

        print("--- %d leaves, %d records, %d bytes of profile ---"
              % (leaves, result["profile_records"], result["profile_bytes"]))

        for key in ("bare", "prepared", "full", "memoized"):
            timing = result[key]
            print("  %-9s median %8.3f ms   min %8.3f   max %8.3f   stdev %7.3f"
                  % (timing["label"], timing["median_ms"], timing["min_ms"], timing["max_ms"],
                     timing["stdev_ms"]))

        derived = result["derived"]
        print("  marshalling overhead     %8.3f ms  (%.1f%% of a call)"
              % (derived["marshalling_overhead_ms"], derived["marshalling_share_of_call"] * 100))
        print("  process startup floor    %8.3f ms  (%.1f%% of a call)"
              % (result["bare"]["median_ms"], derived["startup_share_of_call"] * 100))
        print("  transformation work      %8.3f ms" % derived["work_ms"])
        print("  %d hosts x %d files      %8.2f s uncached, %.2f s memoized\n"
              % (FLEET_HOSTS, FLEET_FILES, derived["fleet_seconds_uncached"],
                 derived["fleet_seconds_memoized"]))

    if arguments.out:
        with open(arguments.out, "w", encoding="utf-8", newline="\n") as handle:
            json.dump(report, handle, indent=2, sort_keys=True)
            handle.write("\n")

        print("wrote %s" % arguments.out)

    return 0


if __name__ == "__main__":
    sys.exit(main())
