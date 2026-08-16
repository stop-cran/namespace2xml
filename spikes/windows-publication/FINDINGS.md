VIABLE WITH CAVEATS

# SPIKE — Windows secure publication (namespace2xml v3)

**Question.** Can spec-conformant, TOCTOU-safe secure publication be implemented on Windows from
C# on net10.0 — and can the tool *actually write files*, not merely reject every Windows write?

**Answer.** Yes. A working prototype is in this folder (`SecureWriter.cs`) and its adversarial
harness (`AdversarialHarness.cs`) passes **31/31** checks on `Microsoft Windows NT 10.0.26310`,
`.NET 10.0.10`, x64. Regenerate with `dotnet run` (captured in `run-output.txt`). The technique is
the direct Win32 analogue of POSIX `openat(dirfd, comp, O_NOFOLLOW)`: retain the output-root and
each intermediate directory as a **HANDLE**, and open every path component **relative to that
handle** with `NtCreateFile` (`OBJECT_ATTRIBUTES.RootDirectory = parentHandle`,
`ObjectName = "<bareComponent>"`), passing `FILE_OPEN_REPARSE_POINT` so the kernel never follows a
reparse point. No full path string is ever re-parsed, so the path-string TOCTOU is eliminated *by
construction*. The resulting kernel handle wraps cleanly in a .NET `SafeFileHandle` and yields a
normal `FileStream`, so the rest of the codebase writes bytes with no special-casing.

**Why "WITH CAVEATS", not a bare "VIABLE":**

1. **Hard links are invisible to any no-follow walk** (POSIX `O_NOFOLLOW` and Windows
   `FILE_OPEN_REPARSE_POINT` alike). A hard link planted inside the root *before* the run, pointing
   at a file outside the root, lets a truncate/write reach the shared inode. The spec already places
   this out of the supported contract (§21.3, "unrelated mutation of that root"), and an *optional*
   `NumberOfLinks > 1` refusal is demonstrated — but it is heuristic. This is a documented residual,
   identical to the POSIX side, not a defect in the approach.
2. **Symbolic-link creation was unavailable in this environment** (no
   `SeCreateSymbolicLinkPrivilege` / Developer Mode; confirmed at runtime). The two symlink cases are
   detected-and-reported, not silently skipped. Coverage is preserved because the writer's reparse
   check is **tag-agnostic** (it tests `FILE_ATTRIBUTE_REPARSE_POINT`, not a specific tag), and
   privilege-free **directory junctions** exercise the identical refusal path at both an intermediate
   component (B03) and the final component (B11).

Neither caveat requires a "path rejected" blanket fallback. Ordinary Windows writes succeed (B01,
B02, and the parent-directory-creating happy paths).

---

## 1. Verdict

`VIABLE WITH CAVEATS` — see the summary above for the two caveats and why they do not block adoption.

---

## 2. Normative path-safety requirements (quoted from `docs/specification.md`)

### §16.2 `filename`
> The path must be relative to the configured output root.
> Absolute paths and paths resolving outside the output root are errors.

The ordered **portable segment algorithm** (applied identically on every OS so identical inputs
produce identical relative paths):
> 1. split the scheme-written path only at literally written `/` and `\`;
> 2. substitute captures and selector-derived parts as decoded opaque text inside the segment;
> 3. reject an empty assembled segment;
> 4. record whether the decoded segment equals `.` or `..`, or whether its portion before the first
>    dot case-insensitively equals one of `CON`, `PRN`, `AUX`, `NUL`, `COM1` through `COM9`, or
>    `LPT1` through `LPT9`;
> 5. retain ASCII letters, digits, `-`, `_`, and `.`, and encode every other UTF-8 byte—including
>    `%`—as `%HH` using uppercase hexadecimal;
> 6. percent-encode every trailing dot as `%2E` and every trailing space as `%20`;
> 7. prefix the result with `%5F` when step 4 recorded a dot-segment or reserved-device condition.

> Statically written `.` and `..` segments are prohibited. The composed-segment safety rules apply to
> every output segment after substitution, including wholly literal segments. Reserved device names
> are deterministically renamed with the prefix rather than rejected. Captured data cannot create
> traversal because it is encoded.

### §17.5 File-level collisions
> A canonical destination path is the portable-encoded relative path with `/` separators, no `.` or
> `..` segments, and no redundant separators.
> … also compute a portability key by uppercasing ASCII letters in the canonical path. … Two
> nonidentical canonical paths with the same portability key are a blocking `PATH001` collision
> rather than a merge.

### §21.1 Output-root confinement (the governing clause)
> Every output path must remain inside `--output`.
> … An existing non-directory output root is `PATH001`.
>
> The implementation must:
> - reject rooted, drive-absolute, drive-relative, UNC, device, and extended-length `filename` forms,
>   including `C:\x`, `C:x`, `\\server\share`, `\\?\`, and `\\.\`;
> - normalize platform separators;
> - reject `.` and `..` path segments after filename expansion;
> - reject canonical paths outside the output root;
> - **open and publish through handle-relative or equivalent no-follow filesystem operations;**
> - **verify symbolic-link, junction, and reparse-point containment when opening each destination;**
> - **fail with `PATH001` before creating directories or opening destinations if the host platform or
>   filesystem cannot provide the primitives needed to establish secure containment;**
> - create required directories only after validation.

### §21.2 Global validation gate
> Before opening or truncating any destination, the tool must: 1. complete pipeline steps 1 through
> 18 …; 2. serialize every planned output completely into immutable in-memory byte buffers; 3.
> validate every final path and create the complete deterministic directory plan.

### §21.3 Direct publication
> - create each destination's missing parent directories immediately before that destination,
>   ancestor first …;
> - create or truncate each destination only after its complete byte buffer exists;
> - flush and close each destination before beginning the next one.
>
> … No rollback is attempted. …
> The output root is considered semantically owned by one CLI invocation during publication, like a
> compiler output directory. **Concurrent writers or unrelated mutation of that root are outside the
> supported execution contract.**

### §15.4 / diagnostics
> Publication is the exception because external side effects have begun: `PATH002` stops publication
> immediately as specified in Section 21.3.

§22 diagnostic registry:
> `PATH001` | error | Invalid, escaping, or insecure output path | once per destination
> `PATH002` | error | Publication/open/write/flush failure | once, for the failing destination

§22 mapping rows:
> Invalid, escaping, insecure, traversal, portability-key-colliding, or uncontainable destination
> path → `PATH001`
> Destination open, create, write, flush, or close failure after publication starts → `PATH002`

Diagnostics carry `phase = "publication"` for step 20, and `destination` is a `/`-separated path
relative to the output root (§6.1, §6.4.3). Conformance items that bind here: **#29** (output-root
confinement incl. symlink/reparse escape), **#51** (`..` rejection, encoded captures), **#64**
(portable device-name handling, ASCII-case-insensitive collision detection).

**Two layers fall out of the spec, and neither subsumes the other:**
- a **string / planning layer** (§16.2, §17.5, the first four §21.1 bullets) that neutralises
  adversarial *strings* — implemented here as `PathValidator`;
- a **runtime layer** (the last three §21.1 bullets, §21.3) that neutralises an adversarial
  *filesystem* (reparse points, TOCTOU) — implemented here as `SecureWriter`.

---

## 3. The working approach (enough to reimplement)

### 3.1 Shape of the walk
```
open ROOT  by absolute NT path  ─►  handle H0        (trust anchor; following reparse in the
for each intermediate component c_i:                  root path itself is acceptable)
    open c_i RELATIVE to H_{i-1}, no-follow  ─► H_i
    query FileAttributeTagInformation(H_i)
    if FILE_ATTRIBUTE_REPARSE_POINT  ─►  refuse PATH001   (never traverse INTO a reparse point)
open LEAF relative to H_{n-1}:
    probe no-follow; if reparse       ─►  refuse PATH001
    create/truncate no-follow         ─►  kernel handle
wrap handle in SafeFileHandle ─► new FileStream(sfh, FileAccess.Write) ─► write buffer ─► flush/close
```

### 3.2 P/Invoke signatures (`NativeMethods.cs`)
```csharp
[DllImport("ntdll.dll", ExactSpelling = true)]
static extern int NtCreateFile(
    out IntPtr FileHandle, uint DesiredAccess, in OBJECT_ATTRIBUTES ObjectAttributes,
    out IO_STATUS_BLOCK IoStatusBlock, IntPtr AllocationSize, uint FileAttributes,
    uint ShareAccess, uint CreateDisposition, uint CreateOptions, IntPtr EaBuffer, uint EaLength);

[DllImport("ntdll.dll", ExactSpelling = true)]
static extern int NtQueryInformationFile(
    IntPtr FileHandle, out IO_STATUS_BLOCK IoStatusBlock, IntPtr FileInformation,
    uint Length, int FileInformationClass);           // FileAttributeTagInformation = 35

[DllImport("ntdll.dll", ExactSpelling = true)]
static extern uint RtlNtStatusToDosError(int status); // NTSTATUS → Win32 for diagnostics

[StructLayout(LayoutKind.Sequential)]
struct UNICODE_STRING     { ushort Length; ushort MaximumLength; IntPtr Buffer; }   // lengths in BYTES
[StructLayout(LayoutKind.Sequential)]
struct OBJECT_ATTRIBUTES  { int Length; IntPtr RootDirectory; IntPtr ObjectName;
                            uint Attributes; IntPtr SecurityDescriptor; IntPtr SecurityQualityOfService; }
[StructLayout(LayoutKind.Sequential)]
struct IO_STATUS_BLOCK    { IntPtr StatusPointer; UIntPtr Information; }
[StructLayout(LayoutKind.Sequential)]
struct FILE_ATTRIBUTE_TAG_INFORMATION { uint FileAttributes; uint ReparseTag; }
```

### 3.3 The exact OBJECT_ATTRIBUTES setup for a *relative* open (the crux)
The single detail that makes or breaks this: for a relative open the `ObjectName` is the **bare
component with no leading backslash**, and `RootDirectory` is the parent handle. (A leading `\`
would make it an absolute name and ignore `RootDirectory`.) The `UNICODE_STRING.Buffer` must point at
pinned UTF-16 for the duration of the call, and `Length`/`MaximumLength` are **byte** counts.

```csharp
static unsafe int RelativeOpen(IntPtr parent, string name, uint access, uint fileAttributes,
    uint share, uint disposition, uint options, out IntPtr handle)
{
    fixed (char* p = name)                                   // pin the component text
    {
        var us = new UNICODE_STRING {
            Length        = (ushort)(name.Length * sizeof(char)),   // BYTES, no NUL
            MaximumLength = (ushort)(name.Length * sizeof(char)),
            Buffer        = (IntPtr)p
        };
        UNICODE_STRING* pus = &us;
        var oa = new OBJECT_ATTRIBUTES {
            Length                   = sizeof(OBJECT_ATTRIBUTES),   // 48 on x64
            RootDirectory            = parent,                      // ◄── open RELATIVE to this handle
            ObjectName               = (IntPtr)pus,                 // ◄── bare "component", NO leading '\'
            Attributes               = OBJ_CASE_INSENSITIVE,        // match Windows default name semantics
            SecurityDescriptor       = IntPtr.Zero,
            SecurityQualityOfService = IntPtr.Zero
        };
        return NtCreateFile(out handle, access, in oa, out _, IntPtr.Zero,
            fileAttributes, share, disposition, options, IntPtr.Zero, 0);
    }
}
```

Flag choices per step:

| open | DesiredAccess | Disposition | CreateOptions |
|---|---|---|---|
| root anchor (absolute, `RootDirectory=NULL`, `ObjectName="\??\C:\…\root"`) | `FILE_LIST_DIRECTORY\|FILE_TRAVERSE\|…\|SYNCHRONIZE` | `FILE_OPEN` | `FILE_DIRECTORY_FILE\|FILE_SYNCHRONOUS_IO_NONALERT` |
| intermediate dir (relative) | `DIR_ACCESS` (list/traverse/add-file/add-subdir/read-attrs/sync) | `FILE_OPEN_IF` (create if missing) | `FILE_DIRECTORY_FILE\|FILE_OPEN_REPARSE_POINT\|FILE_SYNCHRONOUS_IO_NONALERT` |
| leaf probe (relative) | `FILE_READ_ATTRIBUTES\|SYNCHRONIZE` | `FILE_OPEN` | `FILE_OPEN_REPARSE_POINT\|FILE_SYNCHRONOUS_IO_NONALERT` |
| leaf create/truncate (relative) | `GENERIC_WRITE\|SYNCHRONIZE` | `FILE_OVERWRITE_IF` | `FILE_NON_DIRECTORY_FILE\|FILE_OPEN_REPARSE_POINT\|FILE_SYNCHRONOUS_IO_NONALERT` |

`FILE_SYNCHRONOUS_IO_NONALERT` (which requires `SYNCHRONIZE`) makes the handle a *synchronous* file
object, which is exactly what `new FileStream(safeHandle, FileAccess.Write)` expects (its `isAsync`
defaults to `false`).

Reparse detection after opening a component:
```csharp
FILE_ATTRIBUTE_TAG_INFORMATION info;
NtQueryInformationFile(h, out _, (IntPtr)(&info), (uint)sizeof(...), /*FileAttributeTagInformation*/ 35);
bool isReparse = (info.FileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) != 0;  // tag-agnostic
```

### 3.4 Why it is TOCTOU-safe *by construction*
Once we hold handle `H` to a real directory `D`, opening `"c"` relative to `H` resolves `"c"` **inside
the object `D` references**, atomically, in the kernel. A handle is a stable reference to a file
*object*, not to a *name*: after we hold `H`, an attacker renaming `D`'s own entry cannot redirect our
subsequent opens. `FILE_OPEN_REPARSE_POINT` guarantees that during each single atomic resolution the
kernel returns the reparse point itself rather than following it, so we can inspect-and-refuse before
ever using it as the next `RootDirectory`. Because we **never hand a multi-component path string to
the API for re-resolution**, there is no window in which a previously-validated component can be
swapped. The leaf create additionally uses `FILE_OPEN_REPARSE_POINT`, so even a symlink planted
between the probe and the create is *replaced* (link object overwritten) rather than *followed* — the
link target is never touched. A timing test is therefore unnecessary and, per the task, unreliable;
the property is structural.

### 3.5 Handing the kernel handle to managed I/O (deliverable #4)
```csharp
var safe = new SafeFileHandle(fileHandle, ownsHandle: true);
using var fs = new FileStream(safe, FileAccess.Write);   // works: synchronous disk handle
fs.Write(buffer, 0, buffer.Length);
fs.Flush();
```
Confirmed working (every `WRITTEN` line in `run-output.txt`).

---

## 4. Adversarial corpus → observed behaviour → enforcement → diagnostic

`design` = guaranteed by the handle-relative no-follow writer; `check` = explicit rule in the
`PathValidator` string layer. Case IDs match `AdversarialHarness.cs` / `run-output.txt`.

| Case | Input | Observed behaviour | Enforced by | Code |
|---|---|---|---|---|
| A01 | `\rooted.txt` | rejected (rooted form) | check §21.1 | `PATH001` |
| A02/A03 | `C:\Windows\x.txt`, `C:x` | rejected (drive-absolute / drive-relative) | check §21.1 | `PATH001` |
| A04 | `\\server\share\x` | rejected (UNC) | check §21.1 | `PATH001` |
| A05/A06 | `\\?\C:\x`, `\\.\PIPE\x` | rejected (extended-length / device) | check §21.1 | `PATH001` |
| A07/A08 | `..\escape.txt`, `a/../b` | rejected (`..` segment) | check §16.2/§21.1 (+design: writer never emits `..`) | `PATH001` |
| A09 | `a/./b` | rejected (`.` segment) | check | `PATH001` |
| A10 | `a//b` | rejected (empty/redundant segment) | check §16.2 step 3 | `PATH001` |
| A11–A14 | `CON`,`nul`,`COM1`,`LPT9.txt` | renamed → `%5FCON`,`%5Fnul`,`%5FCOM1`,`%5FLPT9.txt` | check §16.2 step 7 | none (renamed) |
| A15 | `COM0` | kept `COM0` (not reserved) | check | none |
| A16 | `file.txt:stream` | colon encoded → `file.txt%3Astream` (ADS neutralised) | **check only** | none (encoded) |
| A17/A18 | `name.`, `name ` | trailing encoded → `name%2E`, `name%20` | check §16.2 step 6 | none (encoded) |
| A19 | `résumé.txt` | UTF-8 %HH → `r%C3%A9sum%C3%A9.txt` | check §16.2 step 5 | none |
| A21 | `a%b` | `%` encoded → `a%25b` | check | none |
| A22 | `Foo.txt` + `FOO.txt` | 2nd/4th rejected (portability-key collision) | check §17.5 | `PATH001` |
| B01 | `a/b/c.txt` on clean root | written; parents created | design §21.3 | — |
| B02 | 34-deep path (484 chars, full path ≫ 260) | written | design (never forms a long string) | — |
| B03 | junction `viajunction`→outside, `viajunction/pwned.txt` | refused at the junction; nothing escaped | **design** | `PATH001` |
| B04 | dir symlink→outside | env: symlink uncreatable → reported; generalised by B03/B11 | design | `PATH001` |
| B05 | file symlink as leaf | env: uncreatable → reported; generalised by B11 | design | `PATH001` |
| B06 | raw `CON` to the writer | **real file** named `CON` created (no DOS-device magic at NT layer) | design immunity | — |
| B07 | raw `name` and `name.` | two **distinct** files (NT layer keeps the trailing dot; Win32 would collide) | design (faithful) | — |
| B08 | raw `host.txt:evil` | **ADS created** — the writer does *not* stop this | **neither** → must be `check` (A16) | (would be insecure) |
| B09 | hard link `hard.txt` == outside/secret.txt | write truncated the **shared inode** (escape); `NumberOfLinks`=2 | **neither** (out-of-contract §21.3); optional check | residual |
| B10 | `C:/Windows/system32/x.txt` to the writer | refused (`C:` is an invalid NT component) | design defence-in-depth | `PATH001` |
| B11 | junction as the **leaf** `finaljunction` | refused by the leaf no-follow probe; victim intact | **design** | `PATH001` |
| — | TOCTOU swap mid-walk | immune; no path re-derivation | design (§3.4) | n/a |
| — | platform lacks no-follow primitives | pre-flight refusal before any create/open | check §21.1 | `PATH001` |

Two rows deserve emphasis because they define the layer boundary:
- **B06 (device name) and B07 (trailing dot) prove a design property**: `NtCreateFile` relative opens
  do **not** apply Win32 path magic. `CON` becomes a real file; `name.` stays distinct from `name`.
  Win32 `CreateFileW` would (respectively) open the console device and silently strip the dot, causing
  a name collision. So the NT-layer writer is *more* faithful, and the string layer's job for these is
  cross-platform *determinism*, not safety.
- **B08 (ADS) proves the converse**: a raw `:` IS meaningful to the NT namespace, so `host.txt:evil`
  creates an alternate data stream. The writer does **not** defend against this; it is neutralised
  only by the validator encoding `:`→`%3A` (A16). This is the one adversarial string that is *not*
  enforced by the writer design and therefore mandates the explicit check.

---

## 5. Rejected alternatives (each with the concrete reason)

**5.1 `File.Open` / `FileStream` + `FileOptions` (managed only).**
There is no managed way to express either "open relative to a directory handle" or "do not follow
reparse points" on net10.0. `FileOptions`/`FileStreamOptions` expose `Asynchronous`, `WriteThrough`,
`DeleteOnClose`, `RandomAccess`, `SequentialScan`, `Encrypted` — nothing for no-follow or `openat`.
`FileStream`, `File.Open`, and `SafeFileHandle`-returning `File.OpenHandle` all take a **path string**
and resolve it through `CreateFileW`, which re-walks the whole path from the drive root every call.
There is no `openat` in the BCL. Managed-only is therefore *structurally incapable* of §21.1's
"handle-relative or equivalent no-follow" requirement. **Rejected.**

**5.2 `CreateFileW` with `FILE_FLAG_OPEN_REPARSE_POINT` on the final component, ancestors validated by
handle.**
`FILE_FLAG_OPEN_REPARSE_POINT` correctly prevents following a reparse point that *is the last
component*, but `CreateFileW` still takes a **full path string** and re-resolves every ancestor from
the drive root during that call. Any ancestor junction/symlink — including one an attacker plants
*after* you validated that ancestor by handle — is followed during resolution. Validating ancestors
by handle and then issuing a `CreateFileW(fullPath, …)` throws that validation away, because the
kernel does not traverse your handles; it traverses the string. The only way to make `CreateFileW`
safe is to never pass it a multi-component path — i.e., open one component at a time relative to a
parent — and `CreateFileW` has **no `RootDirectory` parameter**, so it cannot do that. That capability
lives only on `NtCreateFile`. **Rejected** (it is exactly the race §21.1 is written to prevent).

**5.3 `GetFinalPathNameByHandleW` on a retained handle, then verify the realised path is under the
realised root.**
This *is* useful — the prototype uses it, but **only for display**. As an enforcement mechanism it
races in two places. (a) To call it you must first **open the destination**; opening it by name with
an ordinary `CreateFileW` has *already followed* any symlink/junction, so if the path escaped you have
already opened (and, with a create/truncate disposition, already truncated) the out-of-root target
before the check runs. (b) Even a read-only open-then-check-then-reopen-for-write is a textbook
check-then-use race: `GetFinalPathNameByHandleW` tells you where **this handle** points, not that a
**subsequent open by name** will reach the same object; the name can be re-pointed in between. It
verifies a handle, not a future lookup. Good for telemetry/logging, unsafe for the gate. **Rejected as
the enforcement primitive.**

**5.4 `Path.GetFullPath` + `File.ResolveLinkTarget`.**
Both are pure string/metadata operations with zero atomicity relative to the write. `Path.GetFullPath`
normalises `..` **lexically** and never touches the filesystem, so it cannot see a junction at all.
`File.ResolveLinkTarget` reads a link's target at one instant and hands back a **path string** you
would then re-open by name — reintroducing the resolve-then-open-by-string race, and it only resolves
link kinds it recognises. Composing them yields "compute a path, then trust it later", which is the
canonical TOCTOU shape. Fundamentally: **any design that emerges from the handle domain into a path
string and later re-opens by that string races.** Safety requires staying in the handle domain the
whole way down, which only `NtCreateFile(RootDirectory=…)` provides. **Rejected.**

---

## 6. Residual risks and what could not be tested here

1. **Hard links (residual, by design of the filesystem).** No path-layer no-follow mechanism on any OS
   can detect that a directory entry is a second link to an inode elsewhere. B09 demonstrates a
   pre-planted hard link inside the root causing a write to reach a file outside the root. The spec
   scopes this out: §21.3 declares "unrelated mutation of that root are outside the supported execution
   contract" and offers no atomic guarantee. **Optional hardening** (demonstrated): before truncating an
   *existing* leaf, query `FILE_STANDARD_INFORMATION.NumberOfLinks` and refuse `PATH001` when `> 1`.
   Limits: it is heuristic (legitimate multi-link files exist and it cannot say where the other links
   are), and it only helps for an already-existing destination — a freshly created name cannot alias an
   external inode. Recommend documenting the §21.3 contract and offering the `NumberOfLinks` refusal as
   an opt-in strict mode.
2. **Symbolic links were not creatable in this environment.** No `SeCreateSymbolicLinkPrivilege` /
   Developer Mode (verified: `New-Item -ItemType SymbolicLink` → "Administrator privilege required").
   B04/B05 are reported as skipped, not hidden. The refusal path they would exercise is proven anyway,
   because the writer keys off `FILE_ATTRIBUTE_REPARSE_POINT` (set for *every* reparse tag) and
   privilege-free junctions drive the same code at both intermediate (B03) and leaf (B11) positions. To
   execute the literal symlink cases in CI, enable Developer Mode or grant the privilege to the runner.
3. **Non-NTFS / remote / network roots not exercised.** FAT/exFAT have no reparse points (and no escape
   vector), but ReFS, SMB/UNC, and redirected filesystems can differ in reparse and case semantics. The
   §21.1 pre-flight — "fail with `PATH001` before creating directories or opening destinations if the
   host platform or filesystem cannot provide the primitives" — is the required backstop; the
   abstraction below surfaces it as a capability flag.
4. **PATH001 vs PATH002 boundary for a reparse detected during publication.** The prototype detects
   every containment violation *before* opening/truncating the destination and classifies it `PATH001`
   ("insecure"/"uncontainable", §22), reserving `PATH002` for genuine post-open I/O faults. This reading
   is consistent with §21.1 (confinement) and §15.4 (publication side effects), but the spec does not
   state it in exactly these words — worth a one-line confirmation from the spec owner.
5. **Leaf-symlink policy.** The prototype *refuses* a reparse-point leaf. Because the create uses
   `FILE_OPEN_REPARSE_POINT`, *replacing* the link in place would also be safe (target untouched, §21.4
   allows replacing an existing destination). Refuse-vs-replace is a policy choice; refusing gives the
   clearer diagnostic and is recommended.
6. **TOCTOU** was validated by construction (§3.4), not by a timing loop, per the task's guidance that a
   timing test is not reliable evidence.

---

## 7. What this means for the implementation plan

**The plan's premise is confirmed.** There is no `CreateFileW`-based or managed-only route to §21.1
compliance; Windows requires `NtCreateFile` relative opens with `OBJECT_ATTRIBUTES.RootDirectory`.
Equally important, the plan should record that secure publication is **two layers**, and that the
runtime layer is the only one that can satisfy the last three §21.1 bullets.

**POSIX and Windows can and should share one abstraction.** The two designs are structurally identical
— walk component-by-component anchored to a directory handle, refuse reparse points, never re-derive a
path string. Only the primitive differs (`openat`/`O_NOFOLLOW` vs `NtCreateFile`/`RootDirectory`/
`FILE_OPEN_REPARSE_POINT`). Proposed interface:

```csharp
interface ISecureDirectory : IDisposable
{
    // Open/create a subdirectory relative to THIS handle, refusing reparse points.
    // Throws ContainmentViolation (→ PATH001).
    ISecureDirectory OpenOrCreateChildDirectory(string component);

    // Create or truncate a non-directory child relative to THIS handle, no-follow.
    // Throws ContainmentViolation (→ PATH001) on a reparse leaf; PublicationFault (→ PATH002) on I/O.
    Stream CreateOrTruncateChildFile(string component);
}

interface ISecureRootFactory
{
    // True iff the host platform/filesystem provides no-follow, handle-relative primitives.
    bool SupportsSecureContainment { get; }              // gates the §21.1 pre-flight PATH001

    // Open the (existing, directory) output root as the trust anchor.
    // Throws (→ PATH001) if the root is not a directory or containment is unavailable.
    ISecureDirectory OpenRoot(string outputRootPath);
}
```

- **Windows impl**: this prototype — `NtCreateFile(RootDirectory=parent, ObjectName=bareComponent,
  FILE_OPEN_REPARSE_POINT, FILE_DIRECTORY_FILE|FILE_NON_DIRECTORY_FILE)`, tag query,
  `SafeFileHandle`→`FileStream`. `SupportsSecureContainment` is `true` on NTFS/ReFS; a probe can lower
  it for filesystems without reparse support.
- **POSIX impl**: `openat(dirfd, comp, O_NOFOLLOW|O_DIRECTORY|O_CLOEXEC)`, `fstatat`,
  `openat(…, O_CREAT|O_TRUNC|O_NOFOLLOW|O_CLOEXEC)`, then a `FileStream` over the fd.
- **The publisher (§21.3) is platform-agnostic**: split the canonical relative path on `/`, call
  `OpenOrCreateChildDirectory` for every component but the last, `CreateOrTruncateChildFile` for the
  last, write the pre-serialised buffer, flush/close, dispose ancestor-first. Containment, reparse
  refusal, and TOCTOU-immunity live entirely inside the abstraction; the publisher contains no
  platform code.
- **The string layer (`PathValidator`) sits above the abstraction and is 100% portable** — pure text,
  and the spec mandates identical portable encoding on every OS (§16.2), so the same code runs on both
  platforms. It owns the ADS-colon, device-name, trailing-dot/space, `.`/`..`, structural-form, and
  portability-key rules.
- **Diagnostics map uniformly**: `ContainmentViolation` / invalid-name / non-directory-root /
  "primitives unavailable" → `PATH001`; post-open open/write/flush/close faults → `PATH002` (buffered
  and still emitted in the single JSON array per §6.4.3, `phase = "publication"`).
- **Recommended optional strict mode**: the `NumberOfLinks > 1` refusal from §6(1), off by default to
  honour §21.4's "replacing an existing destination is allowed", on for hostile multi-tenant roots.

Net: implement `ISecureDirectory`/`ISecureRootFactory` once per platform behind the publisher, keep
`PathValidator` shared, and the tool writes files normally on Windows while satisfying §21.1 — no
blanket rejection, no per-platform publisher branches.
