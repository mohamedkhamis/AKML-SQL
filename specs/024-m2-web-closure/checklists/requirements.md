# Specification Quality Checklist: M2 — Web Edition Formatter & Analyser MVP Closure

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-05-26
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

This is a **closure spec** — it captures the five genuinely-unmet verification items for the M2 milestone (spec 021 Phase 3, User Story 1) so the M2 PRD's Definition of Done can be retired against recorded evidence.

The spec follows the same closure-style structure used by spec 023 (M1 closure), which has the same shape: shipped code already exists; the unmet work is verification, audit, and parity-evidence rather than new code paths.

**Acknowledged tension with "no implementation details" rule**: the spec names specific file paths (`tests/AkmlSql.Web.Tests/Format/FormatterServiceTests.cs`, `specs/021-web-edition/M2-THEME-PARITY-AUDIT.md`, etc.) because the M2 PRD's deferred tasks themselves named those paths and the closure spec must point to the same artefacts the deferred tasks promised. The Functional Requirements describe *what* the verification produces and *where it lives*; *how* the verification is implemented (Playwright API surface, screenshot tooling choice, diff format) is left to plan.md.

The five user stories map 1:1 to spec 021's five deferred Phase 3 tasks:

| User Story | Spec 021 task | Deferral reason captured in spec 021 |
|------------|---------------|--------------------------------------|
| US1 (P1) — Theme parity audit | T036 | Needs interactive workstation session |
| US2 (P2) — Formatter parity tests | T041 | Needs parity corpus from spec 020 |
| US3 (P2) — Analyser parity tests | T047 | Needs desktop baseline |
| US4 (P3) — Playwright US1 E2E | T053 | Needs Playwright + running `dotnet run` |
| US5 (P4) — Bundle-size audit | T054 | Needs Release publish on Windows with Brotli |
