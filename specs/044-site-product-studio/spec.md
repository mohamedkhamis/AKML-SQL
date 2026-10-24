# Feature Specification: Product Studio marketing website

**Created**: 2026-10-22

**Status**: Selected for implementation

**Input**: The owner selected concept A from `reports/marketing-homepage-2026-10-22`: a public homepage explaining why to use AKML, SQL Server/PostgreSQL support, and real product screenshots.

## User Scenarios & Testing

### US1 — Understand AKML and choose a workspace (P1)

A visitor understands the practical benefit of AKML, identifies the supported experience for their database, and reaches installation or browser setup guidance.

**Independent test**: Read the hero, benefits and database panels; follow both setup paths.

1. The hero says “Good SQL. Less friction.”, explains writing/reviewing/reusing SQL, offers Download and a product-tour link, and states the MIT/free fact.
2. SQL Server is identified with SSMS 22 and browser support; PostgreSQL is identified with browser plus an enabled AKML engine. No PostgreSQL-in-SSMS or unrestricted feature-parity claim is made.
3. With a missing release manifest, the homepage still renders and links to the existing download page without inventing a version.
4. Features, Download and the public footer describe the same supported experiences.

### US2 — Inspect real product evidence (P2)

A visitor explores completion, format styles, history and database workflows through real screenshots, and can enlarge them without losing their place.

**Independent test**: Select every product-tour item; open and close the viewer using keyboard and pointer.

1. Tour selection updates visible media and an accessible selection state.
2. Images show real AKML screens, have useful captions and alt text, and reserve their aspect ratio.
3. Without JavaScript, all tour examples remain visible and image links open the actual image.
4. Escape closes the enlarged image and focus returns to a visible trigger, including after gallery navigation.

### US3 — Read the selected design on every screen (P3)

A visitor sees concept A's split hero, restrained warm accent, benefit narrative and spacious sections, adapted to their screen and selected theme.

**Independent test**: Check light, dark and high contrast; widths 320–1920 px; navigate and reload; use keyboard and no-JavaScript modes.

1. Light mode follows the selected ivory/navy/orange direction; dark and high contrast remain legible equivalents.
2. Existing theme preference/system detection persists across enhanced navigation; the design does not override a saved choice.
3. Core content, navigation, docs, feedback, download and consent remain usable at narrow widths.

## Requirements

- FR-001: Implement the selected concept A narrative and composition, superseding spec 034's fixed Developer Dark visual direction; retain its theme behavior.
- FR-002: Present at least three concrete benefits, database-specific support, optional AI guidance, setup paths and practical FAQ.
- FR-003: Use actual product captures and sourced database visuals; disclose illustrative values and avoid private data or fabricated results.
- FR-004: Reuse the current screenshot viewer, download routing, documentation ingestion, feedback and consent behavior.
- FR-005: Keep AI optional, describe provider setup/possible charges, and avoid blanket local-data or complete competitor-parity claims.
- FR-006: Preserve native image/document links, release fallback, accessible navigation and theme controls.
- FR-007: Avoid horizontal overflow from 320 through 1920 px and retain usable content without JavaScript.
- FR-008: Maintain accurate page metadata and descriptive image alternatives; keep normal text at AA contrast.
- FR-009: Keep changes scoped to the public site and its documentation/assets/tests. No new database features, admin redesign, accounts, payment, analytics profiling or deployment are included.

## Edge Cases

Missing release data; unavailable or slow images; stored/OS high contrast; enhanced navigation to and from the homepage; JavaScript disabled; narrow screens/large browser font; gallery close while a different tour image is selected; supported features requiring an engine.

## Key Entities

Product benefit, database support panel, screenshot/tour item and setup link are static editorial content. The existing release manifest remains the only source of displayed release versions. No new persisted data model.

## Assumptions

English content. Existing sample-data captures may be reused with provenance; fresh database result captures are desirable only where a suitable sample environment is available. A connection-dialog capture is not evidence of successful database execution. No branch or other Git mutation is authorized.

## Success Criteria

- SC-001: A reader can identify the three benefits and correct database experience directly from the homepage, and reach each setup destination in one click.
- SC-002: Every tour item can be selected and enlarged; close restores useful keyboard focus.
- SC-003: No horizontal page overflow or broken local images at 320, 390, 768, 1440 and 1920 px in the checked themes.
- SC-004: All core discovery/setup links and product examples remain available without JavaScript.
- SC-005: Existing site checks and required build/theme gates pass; new support, tour and fallback behavior has meaningful coverage.
