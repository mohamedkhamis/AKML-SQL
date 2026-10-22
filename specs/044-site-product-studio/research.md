# Research

- Decision: retain static SSR. Rationale: reuse docs/download/consent/CSP; avoid a second framework.
- Decision: extend ProductScreenshot with optional explicit paths and retain its theme link hooks. Rationale: new browser captures differ from SSMS naming; no second viewer needed.
- Decision: real aria-pressed buttons select panels only after JS initializes. Rationale: all panels remain usable without JS.
- Decision: additive Marketing tokens generated for Site and Web. Rationale: preserve ThemeCssSyncTests and unchanged editor/desktop colors.
- Decision: retain theme selection/OS rules. Rationale: design selection does not supersede visitor preferences.
- Decision: update superseded presentation assertions; retain release fallback and support coverage. Features' existing seven SSMS rows remain; homepage now has a gallery.
- Decision: isolated published preview, separate analytics/download paths and empty mail/admin configuration. Rationale: deployed E2E targets IIS; dotnet run can differ in static compression.

Read-only research agent checked tests, viewer, theme generation and preview configuration. No unresolved research questions remain.
