# AGENTS.md — Bork2d Engineering Workflow

> **Purpose.** Operating instructions for AI agents across all Bork2d milestones: how work is
> branched, delegated to subagents, reviewed, merged, verified, and opened for human review.
>
> **Scope.** This file is **global process documentation**. It must not contain milestone
> status, progress, work packages, wave plans, branch names, or per-milestone decisions. Those
> live in [`docs/specs/`](docs/specs/), [`docs/roadmap.md`](docs/roadmap.md),
> [`docs/decisions.md`](docs/decisions.md), and the milestone pull-request description.
>
> **Do not modify this file.** An agent changes `AGENTS.md` only when the user explicitly asks
> for an `AGENTS.md` change (§1).
>
> **Do not merge the final pull request.** The human owner reviews it manually.
>
> **Human decision gate (mandatory).** Agents and subagents are **not** allowed to make
> product, architecture, API, unit, semantic, or workflow decisions. Stop and ask the human
> whenever the spec/docs are vague or conflicting; a public API/contract, unit, or semantic
> change is needed; more than one reasonable implementation exists; a tool, access, or
> dependency is missing; an unexpected error occurs; or scope/branches/CI need to change.
> Subagents stop and report to the orchestrator; the orchestrator asks the human. Never
> silently choose, guess, or implement a workaround.

## 1. This document (`AGENTS.md`)

`AGENTS.md` is global process documentation, not a milestone deliverable.

- **Do not modify `AGENTS.md` unless the user explicitly requests it.** A task that touches
  workflow, or a passing mention of process, is not consent.
- If the file looks stale or incomplete, stop and report what is wrong and what you would change;
  do not edit it on your own initiative.
- Never add milestone-specific content: status, progress, a "current state" snapshot,
  work-package tables, wave plans, concrete branch names, dates, per-milestone decisions, or
  definition-of-done checklists. That content belongs in the milestone spec, the roadmap, the
  decision log, and the milestone PR description.
- Keep the workflow generic and reusable across milestones. This file describes *how* work
  flows, not *what* the current milestone contains.
- When the user does request a change, it is committed **directly to `develop`** as a docs-only
  commit — the only allowed direct-to-`develop` commit — never mixed with implementation and
  never on a milestone or work-package branch. Commit message: `docs: <summary>`.
- A milestone branch must start from a `develop` that contains the latest `AGENTS.md`. If the
  file changes while a milestone is open, merge `develop` into `m-<N>/common` before the next
  wave; do not include process-doc changes in the milestone PR.
- Never mix implementation with an `AGENTS.md` commit.

## 2. Milestone lifecycle

Every milestone follows the same shape:

1. Read [`docs/roadmap.md`](docs/roadmap.md), the current milestone spec under `docs/specs/`, and
   the linked architecture/API documents.
2. Confirm the current milestone and its scope from the roadmap and the milestone PR. Never infer
   scope from `AGENTS.md`.
3. Create the milestone integration branch `m-<N>/common` from the current `develop`.
4. Decompose the milestone into work packages, assign each a branch `m-<N>/wp-<K>`, and define
   dependency waves (§4).
5. Run the work-package loop (§4): dispatch a subagent, review, merge, verify.
6. Run the full local gates (§7) and open the final PR `m-<N>/common` → `develop` (§5).
7. Wait for **human** review. Apply requested changes via review-fix branches (§6).
8. After the PR is approved/merged, update the roadmap progress log and docs status, then start
   the next milestone.

Never start a later milestone before the current PR is approved unless the human explicitly
asks. Do not pull future-milestone features into the current branch.

Scope, work packages, wave plans, gates, decisions, and status are recorded in the milestone spec
and the milestone PR description, not in this file.

## 3. Branch model

```
develop                     integration target; implementation never committed directly
  └── m-<N>/common          milestone integration branch
        ├── m-<N>/wp-1      one work package per branch
        ├── m-<N>/wp-2
        └── ...
```

> `m-<N>/common` and `m-<N>/wp-<K>` are siblings under the `m-<N>/` namespace. Do not create a
> bare `m-<N>` branch; that name is reserved for the namespace.

Rules:

- `develop` is the only long-lived integration branch. Implementation work reaches it only
  through the milestone PR.
- `m-<N>/common` is created once from `develop` at the start of milestone `<N>`. It is the only
  branch the orchestrator merges into.
- Each work package gets **exactly one** branch named `m-<N>/wp-<K>`. One subagent owns one
  branch.
- WP branches are created from the latest `origin/m-<N>/common` and are merged back only after
  review.
- Never force-push, rewrite published history, or delete a branch before the final PR is
  reviewed. Keep WP branches until the human approves the PR; delete only with human approval.
- Implementation never goes directly to `develop`. The **only** exception is the global process
  document `AGENTS.md` (§1), which requires an explicit user request.
- The final PR targets `develop`; it is never auto-merged.

### 3.1 Parallel work

Parallelism is allowed only after a dependency and file-overlap check:

- Two work packages may run in the same **wave** only if neither depends on the other and they
  do not edit the same files/areas.
- All branches in a wave start from the same `origin/m-<N>/common` base. Each wave has a fixed
  merge order.
- A branch that is merged later in the wave must first absorb the updated
  `origin/m-<N>/common` with a **merge commit** (`git merge origin/m-<N>/common`), resolve
  conflicts, and re-run all gates. Do not rebase or force-push a WP branch.
- If absorbing the integration branch causes semantic churn or the diff stops being reviewable,
  downgrade the wave to sequential and record why in the PR description.
- The orchestrator records the wave plan in the milestone PR description before dispatching, and
  does not dispatch a branch whose base is ambiguous.

## 4. Work-package loop

### 4.1 Prepare the milestone branch

The orchestrator does this once per milestone:

```powershell
git fetch origin
git switch -c m-<N>/common origin/develop
git push -u origin m-<N>/common
```

`AGENTS.md` is already on `develop`; do not commit it to `m-<N>/common`.

Before each wave:

```powershell
git fetch origin
git switch m-<N>/common
git pull --ff-only origin m-<N>/common
git status --short          # must be clean
```

### 4.2 Create work-package branches

For every WP in the wave, from the same base:

```powershell
git switch -c m-<N>/wp-<K> origin/m-<N>/common
git push -u origin m-<N>/wp-<K>
git switch m-<N>/common
```

Record the base SHA (`git rev-parse origin/m-<N>/common`) in the subagent task packet.

### 4.3 Create an isolated subagent workspace

Each subagent receives its own **full clone**, so it cannot disturb the integration checkout:

```powershell
$workspace = "D:\Develop\Bork2dEngine.work\m-<N>-wp-<K>"
if (Test-Path $workspace) { Remove-Item $workspace -Recurse -Force }
git clone https://github.com/BorkEngine/Bork2d.git $workspace
git -C $workspace switch m-<N>/wp-<K>       # tracks origin/m-<N>/wp-<K>
git -C $workspace status --short             # must be clean
```

Do not use `git worktree` for subagents: a full clone gives independent refs, configuration,
build outputs, and a safe place for AOT publishes. Never put a subagent workspace inside the main
repository.

### 4.4 Dispatch a subagent

Use a fresh `subagent` (not a fork of the orchestrator conversation) with a self-contained task
packet. The subagent must read this file, the milestone spec, and the relevant architecture docs
first. Template:

```text
Workspace: D:\Develop\Bork2dEngine.work\m-<N>-wp-<K>
Branch: m-<N>/wp-<K>
Base: <origin/m-<N>/common SHA>
Milestone: <N> — <name>; read docs/specs/<spec>.md.
Scope: <WP section> only.
Read first: AGENTS.md; docs/specs/<spec>.md (WP and semantics); docs/contributing.md;
docs/architecture/<relevant>.md.
Deliver: implementation + tests + same-change docs for the WP. No unrelated refactors.
Acceptance: the WP acceptance criteria and the milestone checklist items that apply.
Required checks (from the workspace, see §7):
  dotnet build src/Bork2d/Bork2d.slnx -c Debug
  dotnet build src/Bork2d/Bork2d.slnx -c Release
  dotnet test src/Bork2d/Bork2d.slnx -c Debug --no-build
  dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
  AOT publish/run when the WP touches runtime/AOT
Commit style: m<N>(wp-<K>): <imperative summary>
Push: git push origin m-<N>/wp-<K>. Never push develop, never merge, never force-push.
Report: branch, base/head SHAs, commits, files, commands + results, deviations, risks,
unresolved questions, docs updated.
If the spec/docs are vague or conflicting, a public API/unit/semantic change is needed,
multiple implementations are plausible, a tool/access/dependency is missing, or an
unexpected error occurs: STOP and report to the orchestrator. Do not choose, guess, or
implement a workaround.
If blocked: stop and report; do not widen scope.
```

The subagent must not touch the main checkout, `AGENTS.md`, other work packages, global tools,
`develop`, or `m-<N>/common`, and must not make product/API/architecture decisions.

### 4.5 Review the subagent work

The orchestrator reviews; do not merge on trust.

```powershell
git fetch origin
$review = "D:\Develop\Bork2dEngine.review\m-<N>-wp-<K>"
if (Test-Path $review) { Remove-Item $review -Recurse -Force }
git worktree add --detach $review origin/m-<N>/wp-<K>
```

For a parallel wave, confirm the branch absorbed the latest `origin/m-<N>/common` after the
earlier wave merge, and review the **final tree**, not only the first-parent diff.

Review checklist:

- [ ] Branch is based on the recorded `origin/m-<N>/common` SHA (plus an absorption merge if
      parallel).
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
- [ ] No unapproved product/API/unit/architecture decision was made; ambiguities were escalated.
- [ ] Required checks (§7) ran in the review worktree and passed.

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
git switch m-<N>/common
git pull --ff-only origin m-<N>/common
git merge --no-ff origin/m-<N>/wp-<K> -m "Merge m-<N>/wp-<K>: <WP title>"
git push origin m-<N>/common
```

For a parallel wave, merge the earlier branch first, then update the later branch:

```powershell
# in the later subagent workspace / review worktree
git fetch origin
git merge origin/m-<N>/common          # merge commit, no rebase/force-push
# resolve, run gates, commit if needed, push the WP branch
# then re-review and merge m-<N>/wp-<K>
```

Then run the §7 gates on `m-<N>/common` again (at minimum build, tests, format).

Do **not** update `AGENTS.md` per WP. Live status belongs in the milestone PR description and
`docs/roadmap.md`; `AGENTS.md` is process documentation only.

### 4.7 Cleanup

Keep the subagent clone and remote WP branch until the final PR is reviewed, so the human can
inspect history. After the PR is approved, remove clones and delete WP branches only with human
approval. Never delete `m-<N>/common` before the PR is merged.

## 5. Final pull request

After the last WP is merged and `m-<N>/common` is green:

1. Run the full §7 local gates on `m-<N>/common`, including AOT publish/run.
2. Confirm `git status` is clean and `git log --oneline origin/develop..m-<N>/common` shows the
   integration history.
3. Push `m-<N>/common`.
4. Open a **ready-for-review** PR from `m-<N>/common` to `develop` (not a draft unless the human
   asks). Title: `M<N> — <milestone name>`.
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
    head  = 'm-<N>/common'
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

1. Create `m-<N>/fix-<K>` from the current `m-<N>/common`.
2. Dispatch a subagent, or make the change directly if it is a trivial docs/typo fix.
3. Review and merge with the same §4.5–§4.6 process.
4. Push `m-<N>/common`; the open PR updates automatically.
5. Never amend or force-push `m-<N>/common` after the PR is open without explicit human
   approval.

## 7. Required checks and CI

Run from the repository root. Use the solution named in the milestone spec (currently
`src/Bork2d/Bork2d.slnx`):

```powershell
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet build src/Bork2d/Bork2d.slnx -c Release
dotnet test src/Bork2d/Bork2d.slnx -c Debug --no-build
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
```

AOT publish/run — **required local gate**, not CI. Use the AOT smoke project in the solution
(currently `Bork2d.AotSmoke`):

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
- The milestone spec's acceptance criteria and exit checklist are demonstrated.

**CI policy (human decision, standing):** CI runs on **pull requests targeting `develop`** and on
**pushes to `develop`** after a milestone merge. Do not run CI on milestone/task branch pushes.
Workflow triggers are `on: pull_request: branches: [develop]` and `on: push: branches: [develop]`.
AOT publish/run is **not** in CI; it is a required local gate. Because CI does not catch AOT
regressions, run the AOT gate locally on every runtime-affecting change.

Install the SDK pinned by `global.json` explicitly in CI; hosted Windows runners do not ship
prerelease .NET SDKs by default. Do not weaken a gate to make CI green; fix the code or record a
justified, narrow suppression.

## 8. Engineering rules (global)

### Public boundary

- `Bork2d.Api`: public contracts and value types only. BCL plus
  `Microsoft.Extensions.Logging.Abstractions`; no GPU/OS/Silk/ZLogger/Avalonia.
- `Bork2d.Runtime`: public builder/app surface only; composition root.
- `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render`: `internal` by default; expose only via
  intentional `InternalsVisibleTo` (see
  [`docs/architecture/projects.md`](docs/architecture/projects.md)).
- Tests may reference everything and use internals.
- Adding a project reference or public type updates the dependency docs and dependency test in
  the same change.

### Scene ownership and lifetime

- Root-owned strong graph: `IEngine.Root` (`ISceneRoot`) owns children; no global camera/sprite
  registry, no weak registry, no concurrent-dictionary entity list.
- No `Dispose`/`IsAlive` on scene entities. Detach with `Parent = null`; re-attach the same
  object later, preserving state.
- `CreateCamera(parent)`/`CreateSpriteRender(parent)` require a non-null engine-owned parent;
  `CreateTransform(parent = null)` remains optional.
- `Root` is read-only/identity: parent/local setters throw `InvalidOperationException`.
- `ITransform.DetachAll()` detaches **direct children**; any transform can own children.
- Re-parenting preserves world transform; reject self/cycles/root parent; root cannot be
  parented.
- The tree is **unordered**: no entity IDs, no ordered discovery, no child
  enumeration/traversal helper. Change this only through an explicit milestone spec or decision
  entry.
- Rotation public unit is **degrees**, normalized to `[0, 360)` on assignment; `LocalRotation`
  and `WorldRotation` return normalized values.
- One engine-owned structural `Lock` serializes parent changes; per-object `Lock`s protect local
  properties. No `SceneGraph` holder.
- No thread-affinity assertions.

### Threading

- Any thread may call the public API during `Setup`/`Tick`/`Teardown`; engine callbacks run on
  the tick thread.
- Structural changes use one engine-owned `System.Threading.Lock`; individual objects use
  per-object `Lock`s. Prefer `Interlocked`/`Volatile`/immutable snapshots on hot paths.
- Avoid `lock (object)`; never hold a lock across an await/blocking wait; keep critical sections
  short. Document lock ordering where multiple locks are unavoidable.
- Composite multi-property updates to one object are the game's synchronization responsibility.
- No `Thread.Sleep`/target-rate pacing in windowed/offscreen; headless timer pacing is the
  documented exception.
- Parent changes are engine-globally serialized; property access uses per-object locks. Frame
  snapshots are captured after `Tick`; workers must not write scene state after `Tick` returns.

### AOT and serialization

- Runtime projects are NativeAOT/trim-safe. No runtime reflection, assembly scanning, runtime
  TOML, `Activator.CreateInstance`, `Reflection.Emit`, or dynamic code.
- `IsAotCompatible=true` and AOT/trim analyzers as errors on runtime libraries; `PublishAot=true`
  on smoke/desktop executables.
- Nerdbank.MessagePack generated shapes only; no reflection resolver. A missing shape is a
  build/startup error naming the type.
- Do not add Silk.NET/SDL or other GPU/OS dependencies ahead of the milestone that owns them.
- AOT publish/run is a required local gate even when CI does not run it.

### Logging

- Microsoft.Extensions.Logging + ZLogger. `IEngine.LoggerFactory` is the public seam.
- Default: ZLogger console with local-time milliseconds, level, and category prefix; no colors;
  minimum level Debug in Debug builds, Information in Release; no file sink by default.
- Categories: `Bork2d.Runtime`, `Bork2d.Common`, `Bork2d.Assets`, `Bork2d.Render`.
- Log startup mode and validation outcome. Do not log per sprite/per frame in hot paths.
- The AOT smoke app must exercise the default logging path with zero trim/AOT warnings.

### Performance

- Scene/transform hot paths must avoid virtual dispatch and unnecessary inheritance. `Transform`
  properties are non-virtual; `SceneRoot` is not a `Transform`.
- Prefer sealed/internal types, direct fields/accessors, and structs where they do not fight the
  ownership model. Per-object locks for property access are a measured baseline, not a mandate
  to add more locks.
- Measure before/after performance-sensitive changes; report the method used.

### Style and tests

- Warnings-as-errors, `EnforceCodeStyleInBuild`, style rules at `error`, `dotnet format` gate.
- File-scoped namespaces; nullable enabled; explicit accessibility; one type per file;
  `_camelCase` private fields; guard clauses; no regions except generated code; no commented-out
  code; XML docs on public API.
- xUnit. Add tests with behavior changes. GC/lifetime tests must be deterministic; concurrency
  tests join all workers and assert relationships/state, not entity IDs or order. No flaky
  retries without a tracking entry.
- Suppressions must be narrow, justified, and scoped; `NoWarn` is a review flag.
- Line endings: normalize to LF via `.gitattributes`/`.editorconfig`; run
  `git add --renormalize .` once when the format baseline changes.

### Git hygiene

- Do not commit `bin/`, `obj/`, publish output, test results, `.vs/`, `*.user`, logs, or
  secrets.
- Do not add generated files or vendored packages without a documented reason.
- Pin package versions exactly; no floating versions. `global.json` pins the SDK.
- Do not change `.editorconfig`/`Directory.Build.props` per project without a decision entry.
- WP commits: `m<N>(wp-<K>): <imperative summary>`. Merge commits:
  `Merge m-<N>/wp-<K>: <WP title>`. Process docs: `docs: ...`. Bookkeeping: `chore(m<N>): ...`.
- Keep the working tree clean before switching/merging.
- The only direct-to-`develop` commit is the separate `AGENTS.md` process-doc commit (§1), and
  it requires an explicit user request.

## 9. Environment and access

| Fact | Value |
| --- | --- |
| OS | Windows 11 Pro 10.0.26300 |
| Shell | PowerShell 7.6.8 (`pwsh`) |
| .NET SDK | Pinned in `global.json`; target `net11.0`. |
| MSVC / AOT | Visual Studio 18 Community; `dotnet publish -r win-x64` works and runs locally. |
| Git | 2.53.0.windows.2; remote `https://github.com/BorkEngine/Bork2d.git`; push/admin via GCM. |
| `gh` CLI | **Not installed.** Use the REST API; GitHub API access with push permission verified. |
| GitHub Actions | Enabled; workflow triggers on **pull requests targeting `develop`** and **pushes to `develop`**. No milestone/task branch push CI. `develop` is unprotected. |
| NuGet | nuget.org reachable; other configured sources: BeGet, Local, VS Offline. |
| Disk | ~300 GB free on D:. |

Risks to watch:

- Hosted Windows runners may not ship the pinned prerelease SDK; install it explicitly in CI.
- `dotnet format` is sensitive to line endings; normalize before enabling or changing the gate.
- CI does not run AOT; local AOT evidence is mandatory for runtime-affecting changes.
- No `gh` CLI: pull-request creation is API-only. Never echo the token; never commit credentials.

## 10. Authoritative documents

Read these before changing code:

1. The current milestone spec under `docs/specs/` — scope, API shapes, semantics, acceptance
   criteria, open questions. **The current milestone spec wins over other documents for scope.**
2. [`docs/architecture/scene-ownership.md`](docs/architecture/scene-ownership.md) — root-owned
   scene graph, remove/re-add, read-only root.
3. [`docs/architecture/threading.md`](docs/architecture/threading.md) — any-thread API,
   structural/per-object locks, tick-window rules.
4. [`docs/architecture/runtime-lifecycle.md`](docs/architecture/runtime-lifecycle.md) — builder,
   modes, validation, shutdown.
5. [`docs/architecture/nativeaot.md`](docs/architecture/nativeaot.md) and
   [`docs/architecture/logging.md`](docs/architecture/logging.md) — AOT and logging constraints.
6. [`docs/contributing.md`](docs/contributing.md) — build, style, warnings, PR checklist.
7. [`docs/api/reference.md`](docs/api/reference.md) and
   [`docs/api/guide.md`](docs/api/guide.md) — public contract and examples.
8. [`docs/decisions.md`](docs/decisions.md) — accepted decisions. Do not add, change, or reverse
   one without a human decision.
9. [`docs/roadmap.md`](docs/roadmap.md) — milestone sequence and progress log.

When code and docs disagree, the current milestone spec plus the accepted architecture decisions
are authoritative. Record the disagreement and reconcile it in the milestone's documentation
work package.

## 11. Related documents

- [`docs/specs/`](docs/specs/) — current and historical milestone specs.
- [`docs/contributing.md`](docs/contributing.md) — quality gates.
- [`docs/architecture/scene-ownership.md`](docs/architecture/scene-ownership.md) — ownership.
- [`docs/architecture/threading.md`](docs/architecture/threading.md) — threading.
- [`docs/architecture/runtime-lifecycle.md`](docs/architecture/runtime-lifecycle.md) — builder.
- [`docs/architecture/projects.md`](docs/architecture/projects.md) — dependency matrix.
- [`docs/architecture/nativeaot.md`](docs/architecture/nativeaot.md) — AOT.
- [`docs/architecture/logging.md`](docs/architecture/logging.md) — logging.
- [`docs/decisions.md`](docs/decisions.md) — accepted decisions.
- [`docs/roadmap.md`](docs/roadmap.md) — milestone context.
