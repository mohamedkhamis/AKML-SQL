# `tests/AkmlSql.Core.Tests/Theme/` — placeholder

This folder holds the theme-related xunit suites added by spec 020:

| File (planned) | Purpose | Spec task |
|---|---|---|
| `HardcodedHexScannerTests.cs` | Walks `src/AkmlSql.Shell.Shared/` for any `#[0-9A-Fa-f]{6,8}` literal outside the semantic-colour allow-list. SC-001 gate. | T011 |
| `VisualReferenceCoverageTests.cs` | Every surface in FR-005..FR-014 has a `VisualReferencePath` that resolves to a real section in `doc/SQL-PROMPT/`. | T012 |

Both tests land in the same Phase 2 (Foundational) so all later UI work is gated by SC-001 from the first commit.

Delete this placeholder once the first real test file lands.
