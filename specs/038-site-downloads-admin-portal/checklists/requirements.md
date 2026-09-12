# Specification Quality Checklist: Site Download Experience and Full Admin Portal

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-09-12
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

**Validation run 1 (2026-09-12)** — 15/16. Three `[NEEDS CLARIFICATION]` markers open, all in one
decision cluster: how identifiable the visitor data is allowed to be.

**Validation run 2 (2026-09-12)** — 16/16. **Complete.** All three markers resolved by owner
decision and recorded in the spec's `## Clarifications` section:

| Marker | Requirement | Decision |
|--------|-------------|----------|
| Q1 | FR-038 | Store **full client addresses** for the whole retention period |
| Q2 | FR-022 | **Persistent first-party cookie identity**, stable across days |
| Q3 | FR-037 | **365-day** retention for identifiable records |

### What the decisions added to the spec

The owner chose the maximum-detail option in all three cases, which reverses the site's existing
privacy design (truncated addresses only, daily re-salted identity). That is a legitimate product
decision, but it is not free, so the spec absorbed the consequences rather than leaving them to be
discovered during implementation:

- **New requirement group FR-043–FR-050** (Consent and data protection): affirmative consent before
  the identifier is issued, a fully working site for those who refuse, remembered refusal,
  withdrawal, unattributed-traffic reporting, log/export containment of addresses, and subject
  access.
- **New User Story 5** (visitor decides whether to be tracked, P3, ships with US3) — carved out so
  the consent path is independently testable and cannot be dropped as end-of-story polish.
- **FR-022a / FR-022b**: new-vs-returning reporting, and a prohibition on re-linking a visitor by
  any other signal when the identifier is absent.
- **SC-014–SC-019**: consent refusal leaves no trace, no address reachable unauthenticated,
  unattributed share is reported, deletion/access in under 5 minutes, retention boundary honoured,
  returning-visitor figures reconcile.
- **Edge cases**: consent refused, cleared cookies, one human on several devices, withdrawal after
  consent, shared/carrier-grade addresses where address-grouping and person-grouping legitimately
  disagree.
- **Assumptions rewritten**: the earlier "no consent banner is assumed" is replaced by "a consent
  mechanism is in scope"; identity is per-browser not per-human; individual figures under-report
  relative to totals by design; pre-existing anonymised history is not retrofitted into individuals.

### Resolved by documented default rather than a marker

Per the 3-marker limit, these were settled in the **Assumptions** section: default release
visibility, hidden releases remaining reachable by direct link, country-only geo granularity,
demotion of page-visit metrics, single-administrator model, shared date range across sections, and
the boundary of "full admin portal management".

### Numbering note for the planning phase

`FR-022a` and `FR-022b` are deliberate insertions next to `FR-022`, not a numbering error —
renumbering would have invalidated the cross-references in FR-022b, FR-029, FR-043 and the
Assumptions section. FR IDs otherwise run FR-001 → FR-050 with no gaps; SC IDs run SC-001 → SC-019.

### Carried into planning

- The performance defect in US1 is diagnosable now: `wwwroot/releases.json` holds 16 releases (ten
  from one day), `Download.razor` recomputes its filtered previous-releases list as a property
  accessed twice per render, and each release costs two filesystem probes (existence + size). The
  spec states the outcome (SC-001/SC-002/SC-003); the plan should confirm the cause before fixing.
- FR-038 depends on the forwarded-headers configuration already present in the site: behind a proxy
  or CDN the stored address is the proxy's, which silently invalidates US3 entirely.
