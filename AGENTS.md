# AGENTS.md — Bork2d Agent Workflow

Act as the lead: read the owning docs, split the task into independent subtasks, delegate each to a
subagent in an isolated workspace, review, and merge. The lead owns integration, documentation,
cleanup, and the PR.

This file is process documentation. Never add task status or reports here; the PR body is the
report. A direct user instruction wins over this file. If documents conflict, stop and ask.

## 1. Reference documents
| Document | Read it for |
| --- | --- |
| [README.md](README.md) | Overview, requirements, commands, Pong example. |
| [docs/architecture.md](docs/architecture.md) | Projects, dependencies, threads, frame handoff, invariants. |
| [docs/engine.md](docs/engine.md) | Public contract, scene, time, assets, AOT/surface rules. |
| [docs/render.md](docs/render.md) | Vulkan renderer, planning, textures, shaders, capture. |
| [docs/runtime.md](docs/runtime.md) | Builder, host loops, window/input, lifecycle, logging, AOT. |
| [docs/bake.md](docs/bake.md) | Asset pipeline, processors, CLI, incremental builds, MSBuild. |
| [.editorconfig](.editorconfig) | Enforced formatting, naming, and style. |
| [Directory.Build.props](Directory.Build.props) | Target framework, nullable, warnings-as-errors, analyzers. |
| [global.json](global.json) | Pinned .NET SDK. |
| [.github/workflows/ci.yml](.github/workflows/ci.yml) | What CI runs and when. |
| [scripts/check-docs.ps1](scripts/check-docs.ps1) | Local Markdown link/anchor check. |

- Docs describe the **as-built** state. Update the owning doc in the same change as the behavior.
- At completion, write docs as already merged into `develop`: present tense, no status markers.
- Do not create status, progress, report, planning, or evidence docs. The PR body is the report; a
  new owning doc needs the user's request.
- Do not create a decision log unless asked. Record decisions in the PR body and, if behavior
  changes, in the owning doc.
- Run `pwsh scripts/check-docs.ps1` after Markdown changes. Do not edit `AGENTS.md` unless asked.

## 2. Code style
The build enforces [.editorconfig](.editorconfig) and
[Directory.Build.props](Directory.Build.props); do not weaken or override them. Run `dotnet format`
and fix the code.

- File-scoped namespaces; usings outside the namespace; `System` usings first; braces always;
  explicit accessibility; no `this.`; predefined types; four spaces, LF, final newline.
- PascalCase types/members/constants; `_camelCase` private fields; `I`-prefixed interfaces;
  camelCase parameters/locals; namespace matches project plus folder.
- Prefer one primary type per file, named after the file. Small private helpers and grouped test
  helper types may share a file.
- XML docs on public API and on internal code whose behavior, threading, lifetime, or exceptions
  are not obvious. Comments explain *why*, not *what*.
- `internal` by default; `public` only for the documented contract. Public API changes require user
  approval before editing.
- Prefer `sealed` unless designed for inheritance; prefer interfaces and composition, especially in
  hot paths. No `#region`, commented-out/dead code, `TODO`, `FIXME`, or placeholders in shipped code.
- Guard public inputs with `ArgumentNullException.ThrowIfNull` where it fits. `var` only when the
  type is apparent; explicit types for built-in types.
- Async methods end in `Async`; no `async void`; no blocking on async work in hot paths. Follow the
  existing pattern when startup must block.
- Use `System.Threading.Lock` (the `lock` statement or `EnterScope()`), never lock on a plain
  `object`; no thread-affinity assertions; keep critical sections short and never hold a lock
  across an `await` or blocking wait.
- Do not reformat unrelated code. Code, comments, docs, commits, and PR text are in English.

## 3. Workflow
### 3.1 Branches
`develop` → `<task-slug>/develop` → `<task-slug>/task-N-<short-slug>`.

Example: `implementing-text-render/develop` and
`implementing-text-render/task-1-create-engine-interfaces`.

- `develop` is the integration target; never commit implementation directly to it.
- Create the task branch from `origin/develop` and push it.
- Create each subtask branch from the current task branch when its turn starts; one branch per
  subagent. Use lower-case kebab-case; no bare `<task-slug>` branch.
- No force-push, rebase, or published-history rewrite. Keep the task branch until the PR is merged.

### 3.2 Plan and split
1. Read the owning docs and restate the task in one paragraph.
2. Resolve architecture, API, semantic, scope, dependency, and irreversible decisions before
   dispatch (§4).
3. Split into subtasks that can be reviewed and merged independently; define scope, non-goals,
   files, acceptance, tests, and gates.
4. Number them `task-1`, `task-2`, ...; derive the short slug from the title.
5. Default to sequential. Parallelize only with no dependency and disjoint files; record base SHAs
   and merge order. If unsure, serialize.
6. Create and push branches; record `git rev-parse origin/<task-slug>/develop` per subtask.
7. Send the user the plan before dispatch: subtasks, branches, order, blocking questions.

If a parallel branch is merged after the task branch advanced, merge the current task branch into it,
resolve conflicts, and re-run gates. No rebase or force-push.

### 3.3 Subagent workspace and brief
Each subagent gets a full clone outside the repository:

```powershell
$workspace = "D:\Develop\Bork2dEngine.work\<task-slug>-task-N"
git clone https://github.com/BorkEngine/Bork2d.git $workspace
git -C $workspace switch <task-slug>/task-N-<short-slug>
```

Do not use `git worktree` for subagent workspaces or place one inside the main checkout. If the path
exists, verify it before removing or reusing it.

The brief includes workspace, branch, base SHA, task goal; scope and non-goals; docs to read;
acceptance, tests, and gates; commit convention; push/report instructions; stop conditions (§4). The
subagent must not push `develop` or the task branch, merge, force-push, touch the main checkout,
`AGENTS.md`, other subtasks, or other workspaces, create docs reports, make decisions, or
print/commit credentials.

### 3.4 Subagent work and lead review
The subagent implements, tests, updates owning docs, runs gates, commits coherently, pushes, and
reports branch, base/head SHA, commits, files, commands/results, docs, deviations, and open
questions. On ambiguity, a missing tool/dependency, an unexpected error, or a user decision, it
stops and reports to the lead; it never guesses or works around.

The lead:
1. Fetches and compares with the task branch: base SHA, scope, no unrelated/generated files.
2. Runs the required gates independently in a review worktree or the subagent workspace.
3. Applies the review checklist (§8).
4. On failure, messages the **same** subagent with exact findings. It fixes the same branch, pushes,
   and reports; re-review. Do not silently take over semantic work.
5. On success, merges with `--no-ff` and pushes the task branch.
6. Re-runs gates on the task branch (never red) and cleans up per §10.

```powershell
git switch <task-slug>/develop
git pull --ff-only origin <task-slug>/develop
git merge --no-ff origin/<task-slug>/task-N-<short-slug> -m "Merge <task-slug>/task-N-<short-slug>: <summary>"
git push origin <task-slug>/develop
```

Only mechanical conflict resolution or bookkeeping may be done by the lead directly; record it.
Semantic disagreement goes back to the subagent and, if needed, to the user.

### 3.5 Docs, cleanup, and PR
After all subtasks are merged, update docs as-built and as-merged; run the link check. Docs are part
of the deliverable. Clean up (§10), merge the latest `origin/develop` if it advanced, and run the
full gates (§5) on the final task branch. Confirm a clean status and a pushed branch.

Open the PR with `gh pr create` (requires a working `gh` session):

```powershell
gh pr create --base develop --head "<task-slug>/develop" --title "<title>" --body-file "<path outside repo>"
```

The PR body is the only report; do not duplicate it in a PR comment or docs file. Include goal and
scope, merged subtasks/branches, user decisions, verification commands/results (including skipped
gated tests), docs updated, and `Follow-ups: none`.

A PR is finished only when every subtask is merged, docs are as-built, all applicable gates pass,
and no bug, `TODO`, unresolved question, or follow-up remains. If any exists, keep working or ask;
do not present it as finished. Do not merge, enable auto-merge, or push to `develop`; the user
merges. After merge, no follow-up work for the same task. If the user requests changes before merge,
create `<task-slug>/fix-<N>` from the task branch, run the same loop, and push; never force-push.

## 4. Decisions
Do not make architecture or other important decisions. Stop and ask **before** editing when work
involves architecture, project/dependency structure, threading, lifecycle, data formats; public API
or contract names/signatures/defaults/units/semantics/exceptions/ownership; behavior docs or tests
treat as a contract; scope or acceptance changes; new dependencies/tools/platform requirements;
irreversible choices; conflicting docs; missing information or a blocker needing design changes.

- Use `ask_user_question`; do not bury a decision in prose and proceed.
- Give 2–4 concrete options with tradeoffs; put the recommended one first and label it
  `(Recommended)`; include "stop/do nothing" when real.
- Include what is blocked, what you found, and what each option changes.
- Batch related questions. Ask as many as needed; 50+ is acceptable. Wait for the answer; do not
  continue on an assumption.
- Record the decision in the PR body and, if behavior changes, in the owning doc.
- The lead may decide mechanical details (private helper names, local structure, test organization,
  formatting, commit wording) and subtask order unless it changes scope or risk. When in doubt, ask.
- Subagents escalate to the lead; the lead asks the user. Subagents never ask the user directly. An
  unapproved decision is rejected and reworked, not merged.

## 5. Build, test, and verification gates
Prerequisites: Windows with `pwsh`; the SDK pinned by [global.json](global.json) targeting
`net11.0`; a Vulkan 1.3 loader/GPU for windowed/offscreen and GPU tests (no Vulkan SDK needed to
build); MSVC for AOT.

```powershell
dotnet build src/Bork2d/Bork2d.slnx -c Debug
dotnet build src/Bork2d/Bork2d.slnx -c Release
dotnet test src/Bork2d/Bork2d.slnx -c Debug --no-build
dotnet format src/Bork2d/Bork2d.slnx --verify-no-changes --no-restore
pwsh scripts/check-docs.ps1
git diff --check
```

When the example changes:

```powershell
dotnet build examples/pong/Pong.slnx -c Debug
dotnet build examples/pong/Pong.slnx -c Release
dotnet format examples/pong/Pong.slnx --verify-no-changes --no-restore
```

Environment-gated suites are opt-in; run them for the affected area and report skips with reasons:

```powershell
$env:BORK2D_RUN_WINDOW_TESTS = '1'; dotnet test src/Bork2d/Bork2d.slnx -c Debug; Remove-Item Env:BORK2D_RUN_WINDOW_TESTS
$env:BORK2D_RUN_GPU_TESTS = '1'; dotnet test src/Bork2d/Bork2d.slnx -c Debug --filter "Category=Gpu"; Remove-Item Env:BORK2D_RUN_GPU_TESTS
```

Renderer performance tests are Release-only; run the relevant GPU tests in Release for hot-path
changes and report the measurement.

AOT publish is a local gate (CI does not run it):

```powershell
dotnet publish examples/pong/Pong.Desktop/Pong.Desktop.csproj -c Release -r win-x64
```

It must finish with zero AOT/trim warnings. If the change affects observable host/runtime behavior
and a Vulkan 1.3 desktop is available, run the published `Pong.Desktop.exe` and report the result;
do not invent a headless/offscreen switch.

| Change touches | Required |
| --- | --- |
| Any code | Standard gates above. |
| `examples/pong` | Pong build/format and AOT publish. |
| Runtime, renderer, host, engine, AOT | AOT publish, zero warnings. |
| Window/platform | Real-window tests. |
| Renderer/GPU hot path | Real-GPU tests and Release measurement. |

CI runs docs, format, Debug build, and Debug tests on PRs to `develop` and pushes to `develop`. It
does not build the example or run AOT. Never weaken a gate, add a broad `NoWarn`, or skip a test to
make CI green; fix the code or ask for a narrow, justified exception.

## 6. Testing expectations
- Use xUnit in `Bork2d.Tests`. Every behavior change gets tests; every bug fix gets a regression test.
- Tests are deterministic: no sleeps, wall-clock races, retries, or order dependence. Use the
  existing seams/test doubles (`FakeTime`, `FakeSdlApi`, `FakeRenderer`, `FakeWindow`,
  `CapturingLoggerProvider`, `TestSupport`).
- Concurrency tests join workers and assert state/relationships, not scheduling order.
- Do not commit binary fixtures, generated packs, or generated images; build them in temp dirs.
  Authored sources under `examples/pong/Pong.Assets/Assets` are content, not fixtures.
- Use `[GpuFact]`, `[WindowFact]`, and `[SymlinkFact]` for environment-dependent tests; do not
  silently skip normal-suite tests.
- Public-surface and dependency-matrix tests are contracts; update them with approved API/reference
  changes.
- Run targeted tests while working and the full suite before review. Report exact commands/results
  and skip reasons. A flaky test is a defect; fix it or ask before quarantining.

## 7. Git and commit conventions
- Subtask: `task-N: <imperative summary>`
- Task docs / bookkeeping: `docs: <summary>` / `chore: <summary>`
- Merge: `Merge <task-slug>/task-N-<short-slug>: <summary>`

- Keep commits coherent and scoped; no unrelated refactors, formatting churn, or version bumps.
- Do not commit `bin/`, `obj/`, publish output, test results, `.vs/`, `.idea/`, logs, secrets, or
  generated files. Do not add/update dependencies without approval.
- Do not force-push, rewrite published history, or delete branches outside §10.
- Do not commit implementation directly to `develop`; the only exception is an explicit user request
  to change `AGENTS.md`, kept docs-only and separate from implementation.
- Keep the working tree clean before switching/merging; run `git diff --check` before review.

## 8. Review checklist
- [ ] Based on the recorded task-branch SHA, or a recorded absorption merge.
- [ ] Only subtask scope; no unrelated refactors, formatting churn, version bumps, generated files.
- [ ] Commits follow §7 and the history is readable.
- [ ] `git diff --check` passes; no build output, test results, IDE files, logs, secrets, tokens.
- [ ] Public API, references, and dependency direction unchanged unless approved; approved changes
      update docs and contract tests.
- [ ] Style (§2) and owning architecture invariants are followed.
- [ ] Tests cover acceptance and failure modes; deterministic suite passes; gated tests run or skips
      reported.
- [ ] No `TODO`, `FIXME`, dead/commented-out code, placeholders, broad suppressions, or follow-ups.
- [ ] Docs updated in the same change, as-built, and link check passes.
- [ ] Subagent raised every ambiguity; no unapproved decision was made.
- [ ] Required gates ran on the final tree, not just the first commit.
- [ ] Merge is `--no-ff`; the task branch is green afterward.

## 9. Communication and progress reporting
Report at task start (goal, subtasks, branches, order, blocking questions); after each review
(subtask, branch, result, gates, next step); on a decision/blocker (question tool with options);
before the PR (scope, gates/results, docs, cleanup, risks); after the PR (URL and one-paragraph
summary). Do not report every edit or command. Never hide a failure, skipped gate, or unresolved
question behind a success summary. Subagents report to the lead only.

## 10. Cleanup protocol
Use `D:\Develop\Bork2dEngine.work\` for subagent workspaces and
`D:\Develop\Bork2dEngine.review\` for review worktrees/clones; both are outside the repository.

After a subtask is merged: delete its workspace and its local/remote subtask branch; keep the merge
commit and the task branch.

```powershell
git push origin --delete <task-slug>/task-N-<short-slug>
git branch -d <task-slug>/task-N-<short-slug>
Remove-Item -LiteralPath "D:\Develop\Bork2dEngine.work\<task-slug>-task-N" -Recurse -Force
```

Before the PR: remove remaining subagent workspaces, subtask branches, and review worktrees. Keep
only the task branch, the PR head. After the user merges:

```powershell
git switch develop; git pull --ff-only origin develop
git push origin --delete <task-slug>/develop; git branch -d <task-slug>/develop
```

Before any delete, resolve and verify the absolute path; delete only under the two roots above and
only the current task's workspace. Never delete the main checkout, `D:\Develop`, `artifacts`, user
files, or `develop`. Do not use `git branch -D` until verified merged; never delete the task branch
before the PR is merged; never force-push. Cleanup never replaces review or gates.
