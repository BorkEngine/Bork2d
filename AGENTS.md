# AGENTS.md — Bork2d Engineering Workflow

> **Purpose.** Operating instructions for AI agents across all Bork2d milestones: how work is
> branched, delegated to subagents, reviewed, merged, verified, and opened for human review.
>
> **Scope.** This file is **global**. Milestone-specific scope, work packages, gates, and
> decisions live in the *Current milestone* section (§8). Do not turn the whole document into a
> single-milestone checklist.
>
> **Current milestone:** M1 — Foundation and NativeAOT baseline
> ([spec](docs/specs/m1-foundation.md)).
>
> **Do not merge the final pull request.** The human owner reviews it manually.

## 1. Current state (verified 2026-10-25)

| Area | State |
| --- | --- |
| Branch | `develop`; `AGENTS.md` is committed separately. `m-1` is created from `develop`; wave 1 (WP1/WP2) is in progress. WP branches use `m-1-wp-N` (Git ref constraint). |
| Solution | `src/Bork2d/Bork2d.slnx`, five projects, builds with 0 warnings/errors. |
| `Bork2d.Api` | Partial contract scaffold. |
| `Bork2d.Common` | Scaffold: weak `EntityList`, stub transforms, placeholder loop. |
| `Bork2d.Runtime` / `Bork2d.Assets` / `Bork2d.Render` | Empty (`Class1` placeholders). |
| Tests / CI / `global.json` / `.editorconfig` | None. |
| `dotnet format --verify-no-changes` | **Fails today** (whitespace and no style baseline). WP1 fixes this. |
| Documentation | M0 complete; M1 spec ready. Docs describe the target, not the current code. |
| GitHub | `develop` unprotected; Actions enabled; 0 workflows; 0 open PRs. |

This is a snapshot, not a live status board. Live milestone status is tracked in the milestone
PR description and [`docs/roadmap.md`](docs/roadmap.md) progress log. Refresh this snapshot at
the start of each milestone.

## 2. Milestone lifecycle

Every milestone follows the same shape:

1. Read [`docs/roadmap.md`](docs/roadmap.md), the milestone spec under `docs/specs/`, and the
   linked architecture/API documents.
2. Update §8 of this file for the new current milestone (scope, work packages, wave plan,
   gates, decisions). Commit that change **separately to `develop`** (§7).
3. Create the milestone integration branch `m-<N>` from the current `develop`.
4. Decompose the milestone into work packages, assign each a branch `m-<N>-wp-<K>`, and define
   dependency waves (§4).
5. Run the work-package loop (§4): dispatch a subagent, review, merge, verify.
6. Run the full local gates for the milestone and open the final PR `m-<N>` → `develop` (§5).
7. Wait for **human** review. Apply requested changes via review-fix branches (§6).
8. After the PR is approved/merged, update the roadmap progress log and docs status, then start
   the next milestone.

Never start a later milestone before the current PR is approved unless the human explicitly
asks. Do not pull future-milestone features into the current branch.

## 3. Branch model

```
develop                     integration target; implementation never committed directly
  └── m-<N>                 milestone integration branch (m-1, m-2, ...)
        ├── m-<N>-wp-1      one work package per branch
        ├── m-<N>-wp-2
        └── ...
```

> **Git ref constraint.** A branch cannot be both `m-<N>` and have child refs under
> `m-<N>/...` (directory/file ref conflict). Work-package branches are therefore
> `m-<N>-wp-<K>`, not `m-<N>/wp-<K>`. The same applies to review-fix branches.

Rules:

- `develop` is the only long-lived integration branch. Implementation work reaches it only
  through the milestone PR.
- `m-<N>` is created once from `develop` at the start of milestone `<N>`. It is the only branch
  the orchestrator merges into.
- Each work package gets **exactly one** branch named `m-<N>-wp-<K>` (for M1: `m-1-wp-1` …
  `m-1-wp-8`). One subagent owns one branch.
- WP branches are created from the latest `origin/m-<N>` and are merged back only after review.
- Never force-push, rewrite published history, or delete a branch before the final PR is
  reviewed. Keep WP branches until the human approves the PR; delete only with human approval.
- Implementation never goes directly to `develop`. The **only** exception is the global process
  document `AGENTS.md` (§7), which the human explicitly asked to commit separately.
- The final PR targets `develop`; it is never auto-merged.

### 3.1 Parallel work

Parallelism is allowed only after a dependency and file-overlap check:

- Two work packages may run in the same **wave** only if neither depends on the other and they
  do not edit the same files/areas.
- All branches in a wave start from the same `origin/m-<N>` base. Each wave has a fixed merge
  order.
- A branch that is merged later in the wave must first absorb the updated `origin/m-<N>` with a
  **merge commit** (`git merge origin/m-<N>`), resolve conflicts, and re-run all gates. Do not
  rebase or force-push a WP branch.
- If absorbing the integration branch causes semantic churn or the diff stops being reviewable,
  downgrade the wave to sequential and record why in the PR description.
- The orchestrator records the wave plan before dispatching, and does not dispatch a branch
  whose base is ambiguous.

### 3.2 Current milestone (M1) wave plan

| Wave | WP(s) | Notes |
| --- | --- | --- |
| 1 | WP1 + WP2 | Parallel. WP1 owns build/quality/solution/test skeleton; WP2 owns API contracts. Merge WP1 first. WP2 absorbs `origin/m-1`, resolves formatting conflicts, re-runs gates, then merges. |
| 2 | WP3 | Depends on WP2. |
| 3 | WP4 | Depends on WP3. |
| 4 | WP5 | Depends on WP4. |
| 5 | WP6 | Depends on WP5. |
| 6 | WP7 | Depends on WP6. |
| 7 | WP8 | Final documentation pass; depends on WP7. |

WP1's formatting pass touches API files, so WP2 must absorb WP1 before review. If that overlap
creates excessive churn, run WP1 → WP2 sequentially and note it. There is no other safe
parallel pair in M1: the remaining packages form a strict dependency chain.

## 4. Work-package loop

### 4.1 Prepare the milestone branch

The orchestrator does this once per milestone:

```powershell
git fetch origin
git switch -c m-<N> origin/develop
git push -u origin m-<N>
```

`AGENTS.md` is already on `develop`; do not commit it to `m-<N>`.

Before each wave:

```powershell
git fetch origin
git switch m-<N>
git pull --ff-only origin m-<N>
git status --short          # must be clean
```

### 4.2 Create work-package branches

For every WP in the wave, from the same base:

```powershell
git switch -c m-<N>-wp-<K> origin/m-<N>
git push -u origin m-<N>-wp-<K>
git switch m-<N>
```

Record the base SHA (`git rev-parse origin/m-<N>`) in the subagent task packet. For M1, the
branches are `m-1-wp-1` … `m-1-wp-8`.

### 4.3 Create an isolated subagent workspace

Each subagent receives its own **full clone**, so it cannot disturb the integration checkout:

```powershell
$workspace = "D:\Develop\Bork2dEngine.work\m-<N>-wp-<K>"
if (Test-Path $workspace) { Remove-Item $workspace -Recurse -Force }
git clone https://github.com/BorkEngine/Bork2d.git $workspace
git -C $workspace switch m-<N>-wp-<K>       # tracks origin/m-<N>-wp-<K>
git -C $workspace status --short             # must be clean
```

For M1, the path is `D:\Develop\Bork2dEngine.work\m-1-wp-N`. Do not use `git worktree` for
subagents: a full clone gives independent refs, configuration, build outputs, and a safe place
for AOT publishes. Never put a subagent workspace inside the main repository.

### 4.4 Dispatch a subagent

Use a fresh `subagent` (not a fork of the orchestrator conversation) with a self-contained task
packet. The subagent must read this file, the milestone spec, and the relevant architecture
docs first. Template:

```text
Workspace: D:\Develop\Bork2dEngine.work\m-<N>-wp-<K>
Branch: m-<N>-wp-<K>
Base: <origin/m-<N> SHA>
Milestone: <N> — <name>; read docs/specs/<spec>.md.
Scope: <WP section> only.
Read first: AGENTS.md; docs/specs/<spec>.md (WP and semantics); docs/contributing.md;
docs/architecture/<relevant>.md.
Deliver: implementation + tests + same-change docs for the WP. No unrelated refactors.
Acceptance: the WP acceptance criteria and the milestone checklist items that apply.
Required checks (from the workspace):
  dotnet build src/Bork2d/Bork2d.slnx -c Debug
  dotnet build src/Bork2d/Bork2d.slnx -c Release
  dotnet test src/Bork2d/Bork2d.slnx -c Debug --no-build
  dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
  AOT publish/run when the WP touches runtime/AOT (see §8.3)
Commit style: m<N>(wp-<K>): <imperative summary>
Push: git push origin m-<N>-wp-<K>. Never push develop, never merge, never force-push.
Report: branch, base/head SHAs, commits, files, commands + results, deviations, risks,
unresolved questions, docs updated.
If blocked: stop and report; do not widen scope.
```

The subagent must not touch the main checkout, `AGENTS.md`, other work packages, global tools,
`develop`, or `m-<N>`.

### 4.5 Review the subagent work

The orchestrator reviews; do not merge on trust.

```powershell
git fetch origin
$review = "D:\Develop\Bork2dEngine.review\m-<N>-wp-<K>"
if (Test-Path $review) { Remove-Item $review -Recurse -Force }
git worktree add --detach $review origin/m-<N>-wp-<K>
```

For a parallel wave, confirm the branch absorbed the latest `origin/m-<N>` after the earlier
wave merge, and review the **final tree**, not only the first-parent diff.

Review checklist:

- [ ] Branch is based on the recorded `origin/m-<N>` SHA (plus an absorption merge if parallel).
- [ ] Commit messages follow `m<N>(wp-<K>): ...`; commits are scoped and readable.
- [ ] `git diff --check` passes; no whitespace errors.
- [ ] Diff contains only WP scope; no unrelated refactors/version bumps/formatting churn.
- [ ] No `bin/`, `obj/`, publish output, test results, `.vs/`, secrets, or tokens.
- [ ] Public surface is correct: only `Api` contracts and `Runtime` builder/app are public;
      implementation types stay `internal`; `InternalsVisibleTo` is intentional.
- [ ] No weak registry, `EntityList`, entity `Dispose`/`IsAlive`, or weak ownership in shipped
      code.
- [ ] `System.Threading.Lock` is used; no `lock (object)`; no thread-affinity assertions; no
      main-thread requirement; no `Thread.Sleep` in windowed/offscreen paths.
- [ ] AOT/trim analyzers clean; no runtime reflection/TOML/dynamic code/assembly scanning.
- [ ] Tests cover the WP acceptance criteria and are deterministic (GC/concurrency included).
- [ ] Docs touched in the same change as any contract change; status markers honest.
- [ ] Required checks in §8.3 ran in the review worktree and passed.

If review fails: send exact findings to the subagent, ask it to fix and push the **same**
branch, then re-review. Do not resolve semantic disagreements by silently patching in the
orchestrator checkout. Only mechanical merge conflicts/bookkeeping may be resolved by the
orchestrator, and they must be recorded.

Remove the review worktree after merge:

```powershell
git worktree remove $review --force
```

### 4.6 Merge into the milestone branch

Only after review and green gates, in the fixed wave order:

```powershell
git switch m-<N>
git pull --ff-only origin m-<N>
git merge --no-ff origin/m-<N>-wp-<K> -m "Merge m-<N>-wp-<K>: <WP title>"
git push origin m-<N>
```

For a parallel wave, merge the earlier branch first, then update the later branch:

```powershell
# in the later subagent workspace / review worktree
git fetch origin
git merge origin/m-<N>          # merge commit, no rebase/force-push
# resolve, run gates, commit if needed, push the WP branch
# then re-review and merge m-<N>-wp-<K>
```

Then run the §8.3 gates on `m-<N>` again (at minimum build, tests, format).

Do **not** update `AGENTS.md` per WP. Live status belongs in the milestone PR description and
`docs/roadmap.md`; `AGENTS.md` is process documentation only.

### 4.7 Cleanup

Keep the subagent clone and remote WP branch until the final PR is reviewed, so the human can
inspect history. After the PR is approved, remove clones and delete WP branches only with human
approval. Never delete `m-<N>` before the PR is merged.

## 5. Final pull request

After the last WP is merged and `m-<N>` is green:

1. Run the full §8.3 local gates on `m-<N>`, including AOT publish/run.
2. Confirm `git status` is clean and `git log --oneline origin/develop..m-<N>` shows the
   integration history.
3. Push `m-<N>`.
4. Open a **ready-for-review** PR from `m-<N>` to `develop` (not a draft unless the human asks).
   Title: `M<N> — <milestone name>` (M1: `M1 — Foundation and NativeAOT baseline`).
   Body: goal, WP list with wave/merge order, verification commands and results, CI scope,
   local AOT evidence, risks/deviations, docs updated, and the milestone checklist.
5. **Do not merge, enable auto-merge, or push to `develop`.** Wait for manual review.

`gh` is not installed. Use the GitHub REST API with the credential already stored in Git
Credential Manager; never print the token:

```powershell
$cred = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
$token = ($cred | Where-Object { $_ -like 'password=*' }) -replace '^password=',''
$headers = @{
    Authorization = "Bearer $token"
    Accept        = 'application/vnd.github+json'
    'User-Agent'  = 'bork2d-agent'
}
$body = @{
    title = 'M<N> — <milestone name>'
    head  = 'm-<N>'
    base  = 'develop'
    body  = '<PR description>'
    draft = $false
} | ConvertTo-Json
Invoke-RestMethod -Method Post `
    -Uri 'https://api.github.com/repos/BorkEngine/Bork2d/pulls' `
    -Headers $headers -Body $body -ContentType 'application/json'
```

The token has push/admin access today; treat it as a secret. If authentication fails, stop and
ask the human; do not try alternative credential paths.

## 6. Post-review changes

The human reviews the PR manually. If changes are requested:

1. Create `m-<N>-review-fix-<K>` from the current `m-<N>`.
2. Dispatch a subagent, or make the change directly if it is a trivial docs/typo fix.
3. Review and merge with the same §4.5–§4.6 process.
4. Push `m-<N>`; the open PR updates automatically.
5. Never amend or force-push `m-<N>` after the PR is open without explicit human approval.

## 7. Process document changes (`AGENTS.md`)

`AGENTS.md` is global process documentation, not part of any milestone deliverable.

- The human explicitly approved committing it **separately to `develop`**, outside the
  milestone PR flow. This is the only direct-to-`develop` commit allowed, and it must be
  docs-only.
- Commit message: `docs: <summary>`.
- Update §8 at the start of each milestone to point at the new spec, work packages, wave plan,
  gates, and decisions. Commit that separately before creating `m-<N>`.
- Milestone branches must start from a `develop` that contains the latest `AGENTS.md`. If
  `AGENTS.md` changes while a milestone is open, merge `develop` into `m-<N>` before the next
  wave; do not include process-doc changes in the milestone PR.
- Keep the workflow generic. Milestone-specific details belong only in §8.
- Never mix implementation with an `AGENTS.md` commit.

## 8. Current milestone: M1 — Foundation and NativeAOT baseline

**Spec:** [`docs/specs/m1-foundation.md`](docs/specs/m1-foundation.md).
**Branch:** `m-1`. **Final PR base:** `develop`.

### 8.1 Scope

M1 delivers the project graph and public/internal boundary, the builder/host contract, the
root-owned scene graph with remove/re-add lifetime, a thread-affinity-free public API, logging
through Microsoft.Extensions.Logging + ZLogger, warnings/style enforcement, a clean CI baseline,
and NativeAOT/trim analyzers plus a publish smoke app. M1 does **not** create a window, renderer,
pack, or asset system (M2/M3/M6+).

### 8.2 Work packages and waves

| WP | Branch | Scope | Depends on | Wave |
| --- | --- | --- | --- | --- |
| WP1 | `m-1-wp-1` | Repo/build/quality baseline, project graph, tests project, dependency test, CI skeleton, remove `Class1` | — | 1 |
| WP2 | `m-1-wp-2` | API contracts: `ISceneRoot`, `ITransform.DetachAll`, parent-required creators, `IEngine` additions, `Bork2dException`, `IGame.Teardown`, remove entity `IDisposable` | WP1 (soft; starts in wave 1, absorbs WP1 before merge) | 1 |
| WP3 | `m-1-wp-3` | Root-owned scene graph, Transform child storage, read-only root, reparent/cycle/world-preserving, structural + per-object locks, remove weak registry | WP2 | 2 |
| WP4 | `m-1-wp-4` | `Bork2dAppBuilder<TLayer>`, `Bork2dApp`, validation, one-tick `Run`, idempotent `Dispose`, no window/GPU | WP3 | 3 |
| WP5 | `m-1-wp-5` | `IEngine.LoggerFactory`, `WithLogging`, pretty ZLogger default, startup logs, categories | WP4 | 4 |
| WP6 | `m-1-wp-6` | NativeAOT/trim settings, AOT smoke app, Nerdbank.MessagePack policy + generated-shape smoke | WP5 | 5 |
| WP7 | `m-1-wp-7` | Complete xUnit coverage, concurrency/GC/dependency tests, CI build/test/format order, local AOT checks | WP6 | 6 |
| WP8 | `m-1-wp-8` | Documentation final pass: API/guide, architecture, decisions, roadmap, READMEs, status markers, resolve M1 doc conflicts | WP7 | 7 |

Do not renumber branches; the spec's WP numbers are the contract. One branch per WP, one
subagent per branch.

### 8.3 Required checks

Run from the workspace root (solution path `src/Bork2d/Bork2d.slnx`):

```powershell
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet build src/Bork2d/Bork2d.slnx -c Release
dotnet test src/Bork2d/Bork2d.slnx -c Debug --no-build
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
```

AOT publish/run — **required local gate**, not CI (see below):

```powershell
dotnet publish src/Bork2d/Bork2d.AotSmoke/Bork2d.AotSmoke.csproj `
    -c Release -r win-x64 --nologo
& src/Bork2d/Bork2d.AotSmoke/bin/Release/net11.0/win-x64/publish/Bork2d.AotSmoke.exe
```

Additional gates:

- `git diff --check` clean.
- All builds/tests have **zero warnings**; warnings and style violations are errors.
- AOT/trim warnings are zero; no reflection/dynamic-code warnings.
- Dependency test proves the reference matrix in
  [`docs/architecture/projects.md`](docs/architecture/projects.md#2-target-dependency-graph).
- No unintended public types in `Common`/`Assets`/`Render`.
- No `EntityList`/weak registry/entity `Dispose`/`IsAlive` in shipped code.
- M1 spec §9 checklist items that apply are demonstrated.

**CI scope (human decision 2026-10-25):** GitHub Actions on Windows runs **restore → format
verify → build (Debug) → tests**. AOT publish/run is **not** in CI; it is a required local gate
for WP6+, WP7, and the final `m-1` verification. This intentionally deviates from the M1 spec's
CI wording; WP8 must document the actual CI scope and keep AOT as a local/release gate. Because
CI does not catch AOT regressions, run the AOT gate locally on every runtime-affecting change.

Install SDK `11.0.100-rc.1` explicitly in CI; hosted Windows runners do not ship it by default.
Do not weaken a gate to make CI green; fix the code or record a justified, narrow suppression.

### 8.4 M1 documentation backlog (WP8)

Do not propagate these; fix them in WP8 and in same-change docs where a contract changes:

1. `runtime-lifecycle.md` says `Setup`/`Tick`/`Teardown` are "still main thread callbacks"; the
   accepted contract is **tick thread, no process-main-thread affinity**.
2. `runtime-lifecycle.md` validation item 7 says the pack path must exist; M1 `WithAssetsPack`
   validates only non-empty/path shape (pack implementation is M3). The list also has a
   duplicate item 7.
3. `runtime-lifecycle.md`/API reference describe `Run` as blocking until shutdown; M1 `Run` is
   exactly one `Setup` → `Tick` → `Teardown` smoke and returns.
4. `nativeaot.md` says `PublishAot` on `Project.Desktop` and Silk.NET/SDL validation in M1;
   M1 has no `Project.Desktop` and no SDL. Use the M1 smoke app; Silk.NET moves to M2/M6.
5. `projects.md` "current state" says no packages/`global.json`/tests; M1 changes all three.
6. `api/reference.md`, `docs/README.md`, `README.md`, `roadmap.md`, and `contributing.md`
   status markers become stale when M1 lands.
7. `scene-ownership.md`, `threading.md`, `logging.md`, and `nativeaot.md` status lines move
   from `Planned` to `Current` (fully or partially) as the code lands.
8. `m1-foundation.md` §10 open questions: record the resolutions actually chosen; add a
   decision-log entry if a public contract or format changes beyond D-023/D-024.
9. `docs/README.md` repository map lacks `AGENTS.md`, `Bork2d.Tests`, and the AOT smoke app; add
   them.
10. `contributing.md` "current scaffold predates the `.editorconfig`" note must be removed once
    WP1 enables the gate.
11. M1 spec/roadmap/contributing say CI includes AOT publish; actual CI is build/test/format
    only. Document AOT as a local/release gate and keep the M1 exit evidence honest.
12. `AGENTS.md` is global process documentation committed separately to `develop`; docs may
    mention it, but it is not part of the M1 PR.

### 8.5 Adopted M1 decisions (human, 2026-10-25)

These are settled; changes require a new human decision.

| Topic | Decision |
| --- | --- |
| Branch granularity | One branch per WP; parallel where independent. M1 wave plan in §3.2. |
| Subagent workspace | Full clone per WP at `D:\Develop\Bork2dEngine.work\m-1-wp-N`. |
| Merge style | `--no-ff` merge commit per WP. |
| Tests/AOT paths | `src/Bork2d/Bork2d.Tests`, `src/Bork2d/Bork2d.AotSmoke`, both in `Bork2d.slnx`. |
| CI | GitHub Actions build/test/format only; AOT publish/run is a required local gate. |
| `AGENTS.md` | Global process document; committed separately to `develop`, not in the M1 PR. |
| `DetachAll` scope | Direct children. |
| `CreateTransform` parent | Optional (`null` allowed). |
| Pack path validation | Non-empty/path shape only; pack implementation is M3. |
| Lifetime scopes | Deferred. |
| Per-object locking | One `Lock` per entity; lock-free paths where practical. |
| ZLogger/MEL | ZLogger 2.5.10; MEL/Abstractions 11.0.0-rc.1. |
| xUnit | 2.9.3 + `Microsoft.NET.Test.Sdk` 17.14.1 + runner 3.1.5. |
| Nerdbank.MessagePack | 1.4.95. |
| `InvariantGlobalization` | `true` for the AOT smoke app. |
| XML docs / CS1591 | XML docs by convention; do not enable CS1591-as-error in WP1. |
| `IWindow`/`IInput`/`IAssets` in M1 | Throwing stubs; no window/GPU/asset system yet. |

### 8.6 M1 definition of done

- [ ] `m-1` contains WP1–WP8, each reviewable as its own merge commit (WP1/WP2 in wave order).
- [ ] Build (Debug + Release) is warning-free; style violations are errors.
- [ ] `dotnet test` passes; dependency test passes; no unintentional public types.
- [ ] `dotnet format --verify-no-changes` passes.
- [ ] AOT smoke publishes and runs locally with zero AOT/trim warnings and exercises logging.
- [ ] Root ownership, read-only root, DetachAll, remove/re-add, GC lifetime, cycle rejection,
      world-preserving re-parent, entity IDs, layers, and any-thread concurrency are covered by
      tests.
- [ ] `Bork2dAppBuilder` validation and one-tick `Run` smoke pass.
- [ ] No weak `EntityList`, no entity `Dispose`/`IsAlive`, no main-thread requirement.
- [ ] Docs/API/guide/reference/roadmap/decision log/READMEs match the code; §8.4 fixed.
- [ ] CI (format/build/tests) is green; local AOT evidence is attached to the PR.
- [ ] Final ready-for-review PR `m-1` → `develop` is open; **not merged**; human review pending.

## 9. Engineering rules (global)

### Public boundary

- `Bork2d.Api`: public contracts and value types only. BCL plus
  `Microsoft.Extensions.Logging.Abstractions`; no GPU/OS/Silk/ZLogger/Avalonia.
- `Bork2d.Runtime`: public builder/app surface only; composition root.
- `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render`: `internal` by default; expose only via
  intentional `InternalsVisibleTo` (see [`docs/architecture/projects.md`](docs/architecture/projects.md)).
- Tests may reference everything and use internals.
- Adding a project reference or public type updates the dependency docs and dependency test in
  the same change.

### Scene ownership and lifetime

- Root-owned strong graph: `IEngine.Root` (`ISceneRoot`) owns children; no global
  camera/sprite registry, no weak registry, no concurrent-dictionary entity list.
- No `Dispose`/`IsAlive` on scene entities. Detach with `Parent = null`; re-attach the same
  object later, preserving state.
- `CreateCamera(parent)`/`CreateSpriteRender(parent)` require a non-null engine-owned parent;
  `CreateTransform(parent = null)` remains optional.
- `Root` is read-only/identity: parent/local setters throw `InvalidOperationException`.
- `ITransform.DetachAll()` detaches **direct children**; any transform can own children.
- Re-parenting preserves world transform; reject self/cycles/root parent; root cannot be
  parented.
- Entity IDs are monotonic and non-reused; discovery order is entity-ID order.
- No thread-affinity assertions.

### Threading

- Any thread may call the public API during `Setup`/`Tick`/`Teardown`; engine callbacks run on
  the tick thread.
- Structural changes use one scene-graph `System.Threading.Lock`; individual objects use
  per-object `Lock`s. Prefer `Interlocked`/`Volatile`/immutable snapshots on hot paths.
- Avoid `lock (object)`; never hold a lock across an await/blocking wait; keep critical
  sections short. Document lock ordering where multiple locks are unavoidable.
- Composite multi-property updates to one object are the game's synchronization responsibility.
- No `Thread.Sleep`/target-rate pacing in windowed/offscreen; headless timer pacing is the
  documented exception.

### AOT and serialization

- Runtime projects are NativeAOT/trim-safe. No runtime reflection, assembly scanning, runtime
  TOML, `Activator.CreateInstance`, `Reflection.Emit`, or dynamic code.
- `IsAotCompatible=true` and AOT/trim analyzers as errors on runtime libraries; `PublishAot=true`
  on smoke/desktop executables.
- Nerdbank.MessagePack generated shapes only; no reflection resolver. A missing shape is a
  build/startup error naming the type.
- Do not pull Silk.NET/SDL into a milestone before its scope (M1 excludes them; M2/M6 add them).
- AOT publish/run is a required local gate even when CI does not run it.

### Logging

- Microsoft.Extensions.Logging + ZLogger. `IEngine.LoggerFactory` is the public seam.
- Default: pretty ZLogger console, local time, milliseconds, colors when interactive; minimum
  level Debug in Debug builds, Information in Release; no file sink by default.
- Categories: `Bork2d.Runtime`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render`.
- Log startup mode and validation outcome. Do not log per sprite/per frame in hot paths.
- AOT smoke must exercise the default logging path with zero trim/AOT warnings.

### Style and tests

- Warnings-as-errors, `EnforceCodeStyleInBuild`, style rules at `error`, `dotnet format` gate.
- File-scoped namespaces; nullable enabled; explicit accessibility; one type per file;
  `_camelCase` private fields; guard clauses; no regions except generated code; no commented-out
  code; XML docs on public API.
- xUnit. Add tests with behavior changes. GC/lifetime tests must be deterministic; concurrency
  tests join all workers and assert IDs/order/state. No flaky retries without a tracking entry.
- Suppressions must be narrow, justified, and scoped; `NoWarn` is a review flag.
- Line endings: normalize to LF via `.gitattributes`/`.editorconfig` in WP1; run
  `git add --renormalize .` once, as part of the format baseline.

### Git hygiene

- Do not commit `bin/`, `obj/`, publish output, test results, `.vs/`, `*.user`, logs, or
  secrets.
- Do not add generated files or vendored packages without a documented reason.
- Pin package versions exactly; no floating versions. `global.json` pins the SDK.
- Do not change `.editorconfig`/`Directory.Build.props` per project without a decision entry.
- WP commits: `m<N>(wp-<K>): <imperative summary>`. Merge commits:
  `Merge m-<N>-wp-<K>: <WP title>`. Process docs: `docs: ...`. Bookkeeping: `chore(m<N>): ...`.
- Keep the working tree clean before switching/merging.
- The only direct-to-`develop` commit is the separate `AGENTS.md` process-doc commit (§7).

## 10. Environment and access (verified)

| Fact | Value |
| --- | --- |
| OS | Windows 11 Pro 10.0.26300 |
| Shell | PowerShell 7.6.8 (`pwsh`) |
| .NET SDK | `11.0.100-rc.1.26425.128`, target `net11.0` |
| MSVC / AOT | Visual Studio 18 Community; `dotnet publish -r win-x64` works and runs locally. |
| Git | 2.53.0.windows.2; remote `https://github.com/BorkEngine/Bork2d.git`; push/admin via GCM. |
| `gh` CLI | **Not installed.** Use the REST API; GitHub API access verified (`permissions.push=true`). |
| GitHub Actions | Enabled; 0 workflows; `develop` unprotected; 0 open PRs. |
| NuGet | nuget.org reachable; other configured sources: BeGet, Local, VS Offline. |
| Test toolchain | xUnit 2.9.3 + `Microsoft.NET.Test.Sdk` 17.14.1 + runner 3.1.5 works. |
| AOT logging spike | ZLogger 2.5.10 + MEL 11.0.0-rc.1 + `AddZLoggerConsole` publishes/runs AOT clean. |
| AOT MessagePack spike | Nerdbank.MessagePack 1.4.95 + `[GenerateShape]` + `new MessagePackSerializer()` publishes/runs AOT clean. |
| Disk | ~300 GB free on D:. |

Risks to watch:

- Hosted Windows runners do not ship .NET 11 RC; CI must install `11.0.100-rc.1` explicitly.
- `dotnet format` is sensitive to line endings; normalize before enabling the gate.
- ZLogger/Nerdbank are verified in isolated spikes, not yet inside the engine projects; re-run
  AOT from WP6 and WP7.
- CI does not run AOT; local AOT evidence is mandatory.
- No `gh` CLI: PR creation is API-only. Never echo the token; never commit credentials.

## 11. Authoritative documents

Read these before changing code:

1. [`docs/specs/m1-foundation.md`](docs/specs/m1-foundation.md) — current milestone spec, API
   shapes, semantics, acceptance criteria, open questions. **M1 scope wins.**
2. [`docs/architecture/scene-ownership.md`](docs/architecture/scene-ownership.md) — root-owned
   scene graph, remove/re-add, read-only root.
3. [`docs/architecture/threading.md`](docs/architecture/threading.md) — any-thread API,
   structural/per-object locks, tick-window rules.
4. [`docs/architecture/runtime-lifecycle.md`](docs/architecture/runtime-lifecycle.md) — builder,
   modes, validation, one-tick M1 smoke, shutdown.
5. [`docs/architecture/nativeaot.md`](docs/architecture/nativeaot.md) and
   [`docs/architecture/logging.md`](docs/architecture/logging.md) — AOT and logging constraints.
6. [`docs/contributing.md`](docs/contributing.md) — build, style, warnings, PR checklist.
7. [`docs/api/reference.md`](docs/api/reference.md) and
   [`docs/api/guide.md`](docs/api/guide.md) — public contract and examples.
8. [`docs/decisions.md`](docs/decisions.md) — D-022 (quality gate), D-023 (root ownership),
   D-024 (thread affinity).
9. [`docs/roadmap.md`](docs/roadmap.md) — milestone sequence and progress log.

When code and docs disagree, the current milestone spec plus the target architecture docs are
authoritative. Record the disagreement and reconcile it in the milestone's documentation WP.

## 12. Related documents

- [`docs/specs/m1-foundation.md`](docs/specs/m1-foundation.md) — current milestone.
- [`docs/contributing.md`](docs/contributing.md) — quality gates.
- [`docs/architecture/scene-ownership.md`](docs/architecture/scene-ownership.md) — ownership.
- [`docs/architecture/threading.md`](docs/architecture/threading.md) — threading.
- [`docs/architecture/runtime-lifecycle.md`](docs/architecture/runtime-lifecycle.md) — builder.
- [`docs/architecture/projects.md`](docs/architecture/projects.md) — dependency matrix.
- [`docs/architecture/nativeaot.md`](docs/architecture/nativeaot.md) — AOT.
- [`docs/architecture/logging.md`](docs/architecture/logging.md) — logging.
- [`docs/decisions.md`](docs/decisions.md) — D-022/D-023/D-024.
- [`docs/roadmap.md`](docs/roadmap.md) — milestone context.
