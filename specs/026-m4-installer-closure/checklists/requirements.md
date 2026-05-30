# Specification Quality Checklist: M4 — Installer (IIS Deployment Option) Closure

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-05-28
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

- This is a closure spec that reconciles the M4 PRD (`doc/WEB/M4-iis-installer.md`) against work already merged under spec 021 Phase 5 (tasks T081–T095) and spec 025 (M3 bridge closure). It follows the established closure-spec pattern (specs 022 M0, 023 M1, 024 M2, 025 M3) and references exact code artefacts (`web-installer.iss`, `Web_PostInstall`, `PairingService.CurrentPin`, `AkmlSqlWebEngine` service, `INSTALL-SUMMARY.txt`) because the gaps are plumbing/integration-level, not greenfield design. This is the same convention spec 025 uses (e.g., `WebSocketTransport`, `HandshakeHandler`, `BridgeState`); reviewers should expect named components in FRs and SCs as part of the closure-spec discipline, not as out-of-scope implementation detail.

- Closure framing was confirmed with the user before writing (per the project-memory rule that milestone PRDs in this repo are stale-greenfield). The user explicitly endorsed: (a) staying with the shipped Windows service rather than the PRD's tray-app design; (b) the two-port architecture (separate IIS port default 80 + bridge port default 47291) over the current single-port wizard which has an HTTP.SYS bind conflict.

- **US2 was expanded during the plan-stage code audit.** The audit found that `EngineHandlerRegistry.cs:258` registers `HandshakeHandler` with the all-permissive parameterless constructor (`pairingRequired: () => false`, `pinValidator: _ => true`), so the LAN bridge currently auto-accepts every connection — spec 025 closed the bridge transport + TLS but left auth as a placeholder. A printed PIN would be cosmetic. The user was asked the discriminating question (is enforced LAN pairing a real security boundary M4 must ship, or deploy-now-harden-later) and chose **enforce it**. US2 therefore gained FR-013a..FR-013e (engine-side LAN auth composition) and SC-010; the engine-side wiring is absorbed into this closure the same way spec 025 absorbed its engine-host composition gap (its FR-027). FR numbering uses the repo's established letter-suffix convention (cf. spec 021 FR-013a) to avoid renumbering 27 downstream FRs.

- One material design decision is recorded in §Assumptions as a deliberate deviation from the PRD: the shipping `web-iis-setup.ps1` creates a dedicated `AkmlSqlWeb` IIS site (giving URLs like `http://localhost:80/`), while PRD §4.3 specified an "application under Default Web Site at `/akmlsql`" (giving URLs like `http://localhost/akmlsql/`). The dedicated-site path is already merged and working; the rewrite cost is not justified by the user-visible URL difference. The success page (FR-005) shows the dedicated-site URL form.

- The first interactive integration run (FR-035) is the gating evidence for SC-001, SC-002, SC-003. Until a Windows host with IIS + Inno Setup 7 + admin rights actually runs the installer, those three SCs are aspirational. The closure spec acknowledges this explicitly and treats observed deltas as follow-up tasks rather than blockers.

- Spec is ready for `/speckit.plan`. No further `/speckit.clarify` rounds are needed — the two design questions that required user input were resolved up-front per the closure-framing memory.
