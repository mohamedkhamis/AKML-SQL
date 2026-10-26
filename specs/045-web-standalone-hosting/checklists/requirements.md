# Specification Quality Checklist: Web edition hosting without IIS — a standalone Windows service by default

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-26
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

- Validation pass 1 (2026-10-26): one fix applied. A build-script file name in Out of Scope was
  replaced with a product-level description.
- Validation pass 2 (2026-10-26), after the user's answers (Q1: B plus a firewall allow rule,
  Q2: A, Q3: A):
  - The answers are recorded under Clarifications, and the spec has been rewritten to match.
  - **Shared address (Q1 = B)**: the standalone host serves the pages at the engine's own address
    (FR-013), and the page's address becomes its default engine (FR-014). In LAN mode the address
    is HTTPS for every computer (FR-017), and one inbound firewall allow rule is added (FR-018).
    Setup asks only for the engine port when standalone is chosen. `/WEB_PORT` and
    `/WEB_HTTPS_PORT` apply to IIS only and stop setup if passed with another host (FR-044).
  - **Upgrades keep their host (Q2 = A)**: FR-030 and FR-041. Because the two hosts now use
    different addresses, every switch of host changes the address, and setup warns about it
    before installing (FR-033, FR-035).
  - **No offer to enable IIS (Q3 = A)**: setup never changes Windows features (FR-006), and the
    old "Enable IIS now" path is listed as out of scope.
  - No [NEEDS CLARIFICATION] markers remain. A scan for installer tooling and Windows internals
    (service-control commands, setup script files, the HTTP stack, the registry, feature-install
    commands, build scripts) found no matches.
- Validation pass 3 (2026-10-26), after a review:
  - Added FR-030a. Installs from before this feature recorded no hosting choice. Without this
    rule, an install that was never hosted would stay that way on upgrade, which would leave
    the machines from the original complaint still broken. They now get standalone, and SC-012
    checks it.
  - FR-044 no longer contradicts FR-041. When FR-041 moves a silent upgrade from IIS to
    standalone, IIS-only ports are ignored and logged rather than stopping setup.
  - FR-019 and FR-020 (serve only the web edition's files, never its state) had no acceptance
    criteria. They now have Story 1 scenario 9 and SC-013.
- "IIS", "Windows service", "Windows Firewall", "HTTPS" and the `/WEB_HOST`-style silent flags
  appear as product terms the user sees or types, not as implementation choices. Whether the
  engine's existing service also serves the pages, or a second service does, is left to planning
  (Assumptions).
- Interpretation to confirm: the firewall rule is added in LAN mode only. In localhost mode the
  address answers only on this computer, so a rule would open nothing (Assumptions).
