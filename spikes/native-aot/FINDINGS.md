# Native AOT: findings

**Status: open. Non-blocking. Nothing here changes the shipped configuration.**

Contract bundle at the time of writing: `r13+b04e99d11ccf`.

## The question

A configuration transformer is invoked from scripts, from `make`, from Ansible tasks, and from
agent loops — many times, briefly, each time paying process startup before doing microseconds of
actual work. If startup dominates, ahead-of-time compilation is worth its costs. If it does not,
it is a second supported artifact to build, sign, test and explain, bought for nothing.

So the question is not "does Native AOT work". It is **"what does startup actually cost, and would
removing it be noticed"**.

## What is verified in this session

| Claim | Evidence |
|---|---|
| The code carries no AOT, trim, or single-file analyzer complaints. | `dotnet build -p:EnableAotAnalyzer=true -p:EnableTrimAnalyzer=true -p:EnableSingleFileAnalyzer=true -p:TreatWarningsAsErrors=false` → **0 warnings, 0 errors**. |
| Just-in-time startup on `win-arm64` is ~66 ms. | `measure-startup.py`, 15 timed runs after 3 warmups: median **66.03 ms**, min 61.28, max 81.01, stdev 5.25. |
| An ahead-of-time publish cannot be linked on this workstation. | `error : Platform linker not found … in particular the Desktop Development for C++ workload`. No Visual Studio is installed; `vswhere.exe` is absent. |

The analyzer result is real but **weak**, and should not be quoted as "the codebase is AOT-clean".
The analyzers see only what is compiled, and at `preview.1` the pipeline is largely unimplemented.
`Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Logging.Console` are both
referenced; both have historically been the source of AOT trouble, and neither is yet exercised
along a path that would provoke it. **Re-run this at M6, not before, and treat today's zero as a
baseline rather than a verdict.**

## What is not yet known

Linking and cross-platform startup. Both require toolchains this workstation does not have, so
they are measured by `.github/workflows/native-aot-spike.yml`, which runs on `ubuntu-latest`,
`windows-latest` and `macos-latest` on manual dispatch and uploads one artifact per platform:

- `jit.json`, `aot.json` — startup statistics from `measure-startup.py`;
- `aot-publish.log` — the full publish log, kept **especially** when it fails, because a precise
  blocker list from three platforms is the more useful outcome;
- `outcome.txt` — the RID, the publish outcome, and payload sizes.

The ahead-of-time publish step is `continue-on-error`. A platform that cannot link must not
suppress the platform that can.

## The bar this has to clear

Native AOT ships only if all of these hold. Written down now, before there is a number to be
charitable towards:

1. **Startup improves by more than 30 ms in median terms** on at least two of the three platforms.
   Below that it is inside the noise a user's shell already contributes, and nobody will notice.
2. **The conformance corpus passes against the ahead-of-time binary, byte for byte.** Determinism
   is the product. An artifact that is fast and differently-wrong is worse than no artifact.
3. **It does not become a second contract.** One `--version`, one contract bundle, one set of
   diagnostics. If the AOT build can differ observably from the JIT build, it is not shippable at
   any speed, because a report filed against one of them would not be actionable against the other.

## A measurement trap, recorded because it cost time here

The first version of `measure-startup.py` sent the measured process's standard error to
`DEVNULL`. It correctly rejected a non-zero exit — and then reported only the exit code, `2147516546`.

The actual cause was that this workstation is **Windows on ARM**, and a `win-x64` apphost cannot
load an `arm64` `hostfxr.dll`. The `0x800700C1` message saying exactly that had been discarded by
the harness. A timing harness that hides the failure it detected inverts its own purpose: the
blocker list *is* the deliverable when the publish does not work.

It now captures standard error and prints it with the failure. Do not undo that for tidiness.
