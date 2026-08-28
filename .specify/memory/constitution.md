# AKML SQL Constitution

<!--
Sync Impact Report
- Version change: none (unfilled template) → 1.0.0 (initial ratification)
- Modified principles: none (no prior ratified principles; all five are new,
  replacing the template's [PRINCIPLE_*] placeholder slots)
- Added sections: Core Principles (I–V), Additional Constraints,
  Development Workflow, Governance
- Removed sections: none
- Follow-up TODOs: none — no placeholders intentionally deferred
-->

## Core Principles

### I. Process Isolation & Host Safety

The shell extensions (net472, in-process inside SSMS 22 / VS 2026) MUST remain thin
UI and command layers. All parsing, formatting, analysis, refactoring, schema-cache,
and AI work MUST run in the out-of-process .NET 10 engine (or the shared net10.0
libraries consumed by engine and web) behind the named-pipe IPC contract
(`akmlsql-engine-{user-SID}-{shell-PID}`, MessagePack frames, 16 MB max frame).
New capability belongs engine-side or in a shared library — never duplicated into a
shell project; shell code shared across hosts goes through `AkmlSql.Shell.Shared`
(.projitems), not copy-paste.

Rationale: the host process is not ours — a crash or hang in-process takes down the
user's SSMS/VS session. Isolation also keeps the .NET Framework ↔ .NET 10 boundary
explicit and testable.

### II. Build Integrity (Gates Are Law)

Shell projects MUST be built with full MSBuild — never `dotnet build` (VSSDK
CodeTaskFactory). The solution MUST build green in one pass before work is
considered done. The theme-token drift gate (`build.ps1` step 1,
`generate-theme-css.ps1 -CheckOnly`) MUST stay green: theme CSS is generated from
`docs/theme-tokens.json`, never hand-edited. After SDK/toolchain version changes,
`obj`/`bin` MUST be cleaned before rebuilding.

Rationale: this repo has already burned days on CTO cross-contamination and VS
restore doom loops (see CLAUDE.md Build Gotchas) — both were gate violations that
a clean, gated build would have caught immediately.

### III. Tests & Parity Corpora Are Non-Regressible

Every `src/` project MUST have a matching xunit test project under `tests/`. New
behavior lands with tests covering it. The golden corpora are ratchets:
`tests/format-parity/` goldens and `tests/completion-corpus/` CorpusGateTests pass
rates may only go up — a change that lowers a corpus score is a regression to fix,
not a baseline to bless, unless the user explicitly approves a re-baseline.

Rationale: with 130+ analysis rules, ~977 formatter goldens, and 1,342 completion
cases, regressions are invisible without hard gates.

### IV. Git Consent (NON-NEGOTIABLE)

No git mutations — `add`, `commit`, `push`, PR creation, `amend`, `reset`, `rebase`,
branch operations — without the user's explicit instruction at that moment. Code
changes are delivered uncommitted; prior approval does not carry over to later
changes. If a commit seems needed, say so and wait.

Rationale: the owner reviews every change entering the repository; agent-initiated
git mutations have no undo from the user's perspective.

### V. Simplicity & Convention Fidelity

Changes MUST be minimal and scoped to the request: no speculative generality, no
opportunistic refactors, no unrequested configurability. New code MUST match the
surrounding file's naming, comment density, and structural idioms. Existing systems
(theme tokens, `ConfigManager`, `LoggerFactory`, the IPC layer) MUST be reused or
extended before a parallel mechanism is introduced.

Rationale: this is a solo-maintained, review-heavy codebase — a tidy, reviewable
diff beats clever abstraction every time.

## Additional Constraints

**Security & robustness bounds**: Validate paths with `Path.GetFullPath()` canonical
checks, never substring checks; file paths accepted over IPC MUST be absolute.
Enforce the established size limits (10 MB per editor document, 1 MB snippet JSON).
Config writes MUST be atomic (temp file + rename). IPC stays local-only with
owner-SID ACLs — no network listener. Secrets (AI keys, SQL credentials) are wrapped
at rest (DPAPI / non-extractable AES-GCM) and never logged.

**Technology pins**: .NET Framework 4.7.2 shell + netstandard2.0/net10.0 shared
libraries + net10.0 engine/web; VS SDK 17.14.x for both shell targets; Inno Setup 7
for installers; Serilog for logging; System.Text.Json for JSON; MessagePack for IPC.
Version bumps to these pins require a documented reason and a full clean build.

**Documentation currency**: When structure, conventions, commands, or build steps
change, update `CLAUDE.md` and the affected `doc/*.md` files in the same change.
Per-spec progress and deferred follow-ups are recorded in `doc/progress.md`, never
dropped silently.

## Development Workflow

Work is spec-driven through the Spec Kit skills: `speckit-specify` → `speckit-plan`
→ `speckit-tasks` → (`speckit-analyze`) → `speckit-implement`, one folder per
feature under `specs/`. The plan's Constitution Check gate MUST pass (or explicitly
justify violations in Complexity Tracking) before implementation begins. Each
feature's `quickstart.md` validation scenarios are the acceptance gate for "done".

Features land on feature branches and merge to `master` via reviewed PRs — no direct
commits to `master`, no auto-merge. Deferred work is recorded in the feature's
`tasks.md` and `doc/progress.md` with reasons, so scope is never lost silently.

## Governance

This constitution supersedes other practices when they conflict. Amendments require:
a documented rationale, the user's explicit approval, and a semantic version bump —
MAJOR for principle removals or backward-incompatible redefinitions, MINOR for new
principles/sections or materially expanded guidance, PATCH for clarifications and
wording. Compliance is reviewed at the plan/tasks Constitution Check gates and by
`/skill:speckit-analyze`; violations are resolved in the spec/plan/tasks, never by
ignoring the principle. `CLAUDE.md` remains the runtime development-guidance file;
where the two disagree, the constitution wins and `CLAUDE.md` is updated to match.

**Version**: 1.0.0 | **Ratified**: 2026-08-27 | **Last Amended**: 2026-08-27
