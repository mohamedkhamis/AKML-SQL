# Specification Quality Checklist: Multiple AI Agents with Guided Setup

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-07
**Last validated**: 2026-09-07 (refinement pass after `/speckit.analyze`)
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

**Validation result**: all items pass. Ready for implementation.

---

### Refinement pass — 2026-09-07

`/speckit.analyze` found four spec-level defects. All four are resolved. **Requirement numbering was
deliberately left unchanged** (58 FRs, FR-001 … FR-058), so every reference in `plan.md`, `tasks.md`,
`data-model.md`, `contracts/` and `quickstart.md` remains valid.

| Finding | Was | Now |
|---|---|---|
| **A2** — FR-003 | "MUST support **at least** 20 agents and MUST refuse to create more" — self-contradictory against V12/V21, which cap the list at 20 | "MUST support **up to** 20 agents, and MUST refuse to create a 21st with a message naming the limit" |
| **I2** — FR-022 | Listed ghost text among features that MUST report the no-agent state, while `contracts/chat-agent-selection.md` and task T031 both excluded it — a task citing a requirement it contradicted | Ghost text is **explicitly excluded** and MUST stay silent. Rationale recorded as Assumption 11. US1 gained scenario 7 pinning the silence; scenario 6 no longer lists ghost text. `contracts/chat-agent-selection.md` § Other AI surfaces updated to match |
| **I3** — FR-052 | "the user MUST be told which agent answered" — general, while `contracts/agent-resolution.md` restricted attribution to chat | Scoped to chat, with the reason stated: only chat names an agent at all (FR-042), so only chat can say when a fallback answered. The other features must not present a fallback answer as the selected agent's. Rationale recorded as Assumption 12 |
| **I4** — US3 scenario 1 | "a picker shows the agents by name with **the active one** selected" — contradicts S3, where the selection is the *resolved chat agent* | "with **the agent that will answer the next message** selected — the agent assigned to chat when one is assigned, otherwise the active agent (FR-006)" |

**Two decisions were made rather than deferred to a clarification marker**, both recorded in
Assumptions:

- **Ghost text stays silent** (Assumption 11) rather than reporting the no-agent state. It is the
  only AI feature the user does not invoke — it fires on typing. A notice there is an interruption
  the user cannot avoid by not asking. Every deliberately invoked feature still reports.
- **Fallback attribution follows agent attribution** (Assumption 12). Extending named attribution to
  the Explain/Fix/Optimize/Index panels is a reasonable future change but is not in this feature.

### Design-artifact pass — 2026-09-07 (I1, I8)

Two further findings were resolved in the design artifacts, at the user's direction.

**I1 — mirroring on save (was HIGH).** Four artifacts disagreed on whether the flat provider fields
are refreshed on save as well as on load. Resolved **in favour of both**, because the mirror is a
persisted invariant rather than an in-memory convenience: mirroring on load alone leaves the file
carrying the previously active agent between a save and the next load, which anything reading the
JSON directly would see — an older build after a downgrade (Assumption 9), a second host that has
not reloaded, a support engineer.

The mirroring step is factored out of `Normalize` into `AiAgentResolver.MirrorActiveAgent`, so the
write path can hold the invariant without running load-time repairs:

| Path | Runs | Why |
|---|---|---|
| `ConfigManager.Load()` / `Load(path)` | `Normalize` (migration, repairs, mirroring, derived `Enabled`) | Every reader sees consistent, migrated settings |
| `ConfigManager.Save(settings)` | `MirrorActiveAgent` **only** | Save persists what it was handed; migration and repair must never rewrite a caller's settings on the write path |

Two guards make this safe: `MirrorActiveAgent` is a **no-op when the agent list is empty** (that is
the pre-migration shape V14 rescues — blanking it on save would destroy the configuration migration
exists to recover), and `Enabled` stays derived in `Normalize` only, keeping the write path to a
single obligation.

Updated: `contracts/agent-model.md` (§ Where normalisation runs, § Mirroring), `data-model.md`
(E5, E6, V18, § Persistence), `contracts/options-agents-ui.md` (§ working copy), `plan.md` (Summary,
structure tree, risk table), `tasks.md` (T009, T010, T012), `quickstart.md` (scenario 69 now checks
the on-disk mirror immediately after save, before the downgrade check in 69a).

**I8 — fallback-notice scope (was MEDIUM).** Three artifacts gave three scopes ("once" / "once per
session per feature" / "once per feature per engine process"). Settled on **once per feature per
engine process** — the engine is where resolution happens and where the "already told them" flag
lives; a shell "session" is not a scope the engine can observe. An engine restart re-notifies, which
is correct: the condition is still true and the log is fresh. Updated: `data-model.md` V22,
`tasks.md` T066 and T095. `contracts/agent-resolution.md` already said this and is unchanged.

`spec.md` FR-049 ("MUST inform the user once, not on every request") is left as written — it is the
user-facing guarantee, and the process scope is the implementation of it.

### Findings still open

Ten lower-severity findings from `/speckit.analyze` remain, all in `plan.md` and `tasks.md`, none
blocking: I5 (engine test subdirectory does not exist), I6 (JSON fixture against Core.Tests
convention), I7 (plan tree omits files tasks touch), G1 (FR-023 no dedicated task), G2 (FR-035
keyboard navigation untested), G3 (FR-029's health badge lands in a later phase than the
requirement), G4 (FR-033 no page-level assertion), U1 (fallback-order UI unspecified), U2
(`baseline.md` undeclared), A1 (SC-011 has no absolute number).
