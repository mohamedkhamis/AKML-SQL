# Specification Quality Checklist: M3 — WebSocket Transport & Local-Agent Bridge Closure

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-05-27
**Feature**: [spec.md](../spec.md)

## Content Quality

- [X] No implementation details (languages, frameworks, APIs)
- [X] Focused on user value and business needs
- [X] Written for non-technical stakeholders
- [X] All mandatory sections completed

## Requirement Completeness

- [X] No [NEEDS CLARIFICATION] markers remain
- [X] Requirements are testable and unambiguous
- [X] Success criteria are measurable
- [X] Success criteria are technology-agnostic (no implementation details)
- [X] All acceptance scenarios are defined
- [X] Edge cases are identified
- [X] Scope is clearly bounded
- [X] Dependencies and assumptions identified

## Feature Readiness

- [X] All functional requirements have clear acceptance criteria
- [X] User scenarios cover primary flows
- [X] Feature meets measurable outcomes defined in Success Criteria
- [X] No implementation details leak into specification

## Notes

This is a **closure spec** — it captures the five genuinely-unmet items from the M3 PRD (`doc/WEB/M3-websocket-transport.md`) so the M3 Definition of Done can be retired against recorded evidence. Twenty-five of the thirty Phase-4 tasks in spec 021 (T056–T080) are already merged with detailed completion notes; this spec scopes to the named follow-ups and the docs-and-tests verification gap.

The spec follows the same closure-style structure used by specs 022 (M0), 023 (M1), and 024 (M2): shipped code already exists; the unmet work is plumbing + documentation + verification rather than new application surfaces.

**Acknowledged tension with the "no implementation details" rule**: the spec names specific file paths (`src/AkmlSql.Web/Services/IEngineBridge.cs:57`, `doc/m3-security.md`, `tests/AkmlSql.Web.E2E.Tests/UserStory2Tests.cs`, etc.) because the M3 PRD's deferred tasks themselves named those paths and the closure spec must point to the same artefacts the deferred tasks promised. The Functional Requirements describe *what* the work produces and *where it lives*; the *how* (Kestrel HTTPS configuration choices, schema-tree virtualisation tech, Playwright fixture shape) is left to plan.md.

**Five user stories map 1:1 to the named gaps**:

| User Story | Gap | Spec 021 reference |
|------------|-----|--------------------|
| US1 (P1) — WSS over LAN | Kestrel HTTPS variant of `WebSocketTransport` | T058 deferred |
| US2 (P1) — Threat model + firewall + quickstart-m3 docs | DoD §12 docs row | (PRD-level only — no spec 021 task) |
| US3 (P2) — Exponential-backoff reconnect | `BridgeState.Reconnecting` wired into a backoff loop | T068 follow-up note |
| US4 (P2) — Schema object tree rendering | DoD §12 "renders tree" | (no spec 021 task) |
| US5 (P3) — End-to-end coverage on the wire | DoD §12 "second machine on the LAN" | T078 + T079 deferred |

**Three follow-ups explicitly deferred** (matching how spec 021 left T065 and T066-partial open):

- TLS fingerprint pinning UI dialog
- Engine-side tray UI for the pairing pane (T065)
- In-flight WebSocket revocation when a bearer is revoked (T066 partial)

Each is named in the spec's "Out of Scope" section so the next M3-touching session can find it.
