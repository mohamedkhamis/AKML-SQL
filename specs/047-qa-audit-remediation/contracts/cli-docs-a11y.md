# Contract: CLI release gate, documentation and accessibility (CLI-001, DOC-001–003, WEB-007–009)

## 1. Analyzer CLI

- **Project.** `src/AkmlSql.Analyzer/AkmlSql.Analyzer.csproj` keeps `PublishTrimmed` and `PublishSingleFile` and
  adds:

  ```xml
  <JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>
  ...
  <ItemGroup>
    <TrimmerRootAssembly Include="AkmlSql.Analysis" />
    <TrimmerRootAssembly Include="AkmlSql.Core" />
  </ItemGroup>
  ```

- **Fixtures** (committed): `tests/fixtures/cli/multi.sql` (the audit's three statements),
  `tests/fixtures/cli/sem1.sql` (`select 1 from dbo.Customers`) and `tests/fixtures/cli/pe004-off.casettings`
  (`{"rules":{"PE004":{"enabled":false,"severity":"warning"}}}`).
- **Release gate.** The step "Analyzer CLI (smoke)" in `build.ps1`, and the same step in
  `doc/Deploy-Build-Release.ps1`, runs right after the publish against the published exe:

  | Run | Must |
  | --- | --- |
  | `--file multi.sql` | print `PE002`, `PE003`, `PE004` |
  | `--file multi.sql --check` | exit code 1 |
  | `--file sem1.sql --rules ST004` | print exactly one `information ST004` |
  | `--file multi.sql --settings pe004-off.casettings` | print `PE003` and not `PE004` (exercises the reflection-based settings read) |

  Any miss fails the build with the command and its output. Nothing is shipped from a failed build.
- **Permanent test.** `tests/AkmlSql.E2E.Tests/PublishedAnalyzerTests.cs` (`[Trait("Category","PublishedCli")]`)
  publishes `-c Release -r win-x64 -o <temp>` once per run (class fixture) and asserts the same four rows.

## 2. Documentation content

| File | Change |
| --- | --- |
| `doc/topics/connecting.md` | New section **Connecting in the browser**: the Connect to database dialog; Windows authentication = the engine's account (with the WEB-013 visibility note); **Save** keeps name, server and login; with **Remember**, Save checks the login and the engine machine keeps the password (encrypted for the engine's account); the browser never stores passwords; web and SSMS passwords are separate (no automatic reuse; see WEB-006); saved connections and pairings belong to the address you opened (`localhost` ≠ `127.0.0.1`), and Export/Import settings moves preferences. |
| `doc/topics/troubleshooting.md` | New section **Web edition**, with these entries as `###` headings, each giving symptom → cause → fix: "Handshake failed" (wrong port or engine stopped; the Bridge port line in INSTALL-SUMMARY.txt; Add engine…), "No engine paired yet", "My saved connections are gone" (another address, cleared site data, best-effort storage; Diagnostics › Browser storage), "It asks for my password again" (Save with Remember, the login test, Forget), "Stored procedures are missing from the explorer" (WEB-013). |
| `doc/topics/browser-setup.md` | A short IIS note: the page and the engine use two ports; the installer publishes the engine's port to the page (`akml-engine.json`). Link to the troubleshooting section. |
| `doc/configuration.md` | First paragraph → "Every `config.json` setting of AKML SQL and the per-project `.casettings` file, with defaults and examples." The `deployment.md#web-edition` link → a published page (`/docs/topics/browser-setup`). `codeAnalysis` table: `squiggleStyle` (`marker` default, `underline`), `squiggleStyleVersion`, `showAdvisoryHints`. |
| `doc/analysis-rules.md` | Global settings table matches configuration.md. A note on the `SYNTAX` finding (web edition). |
| `doc/ipc-api.md` | Messages 227–230, capability `pairing.manage.v1`, `SchemaPhasePayload` key 8, the `TotalScanned` meaning. |

Screenshots are retaken with the existing docs screenshot flow after Stories 2 and 3 land: the dialog with Remember,
Settings › Connections with Paired browsers, and the status bar failure notice.

## 3. Documentation checks (`tests/AkmlSql.Site.Tests/Docs/DocsQualityTests.cs`)

1. Every published document's `Summary` is at least 50 characters.
2. No rendered link in a published document is relative and unresolved (pointing at a file the site does not
   publish). The test reads the renderer's link-rewrite outcome, adding an internal hook only if none exists.
3. The troubleshooting document's headings contain each of: "Handshake failed", "No engine paired yet", "saved
   connections are gone", "asks for my password again".
4. `connecting`'s plain text contains "Remember" and "engine machine".

## 4. Accessibility

| Item | Where | Change |
| --- | --- | --- |
| Editor name | `akml-editor.js` `EditorView.contentAttributes` | `aria-label="SQL query editor — {tab name}"` (updated on tab switch), `aria-multiline="true"`, `role="textbox"`; extends `akml-editor.attributes.test.mjs` |
| Active badge | `ConnectionPickerComponent.razor` `.akml-connections-badge--active` | the semantic success badge token pair (≥ 4.5:1 in all three themes); a new pair only via `docs/theme-tokens.json` + `generate-theme-css.ps1` |
| Muted style badge | `Styles.razor` `.akml-styles-badge.muted` | no `opacity`; `color: var(--akml-text-secondary)` (≥ 4.5:1 on the selected row) |
| Tab close | `TabStrip.razor` (`Closable`) | the close control is a sibling button of the tab element, `aria-label="Close {title}"`; the tab is no longer interactive content containing a button; test ids `doc-tab`, `doc-tab-close` and the keys are kept |
| History filters | History page selects | visually hidden `<label for>` "Server" / "Database" |
| Style preview | `/styles` preview `<pre>` | `tabindex="0"`, `aria-label="Preview"`; heading order fixed |
| Connections table | the 5th `<th>` | visually hidden "Actions" |
| Page headings | each routed page | one visually hidden `<h1>` with the page name |
| Landmarks | `MainLayout.razor` | the chrome is in `header` / `nav` / `main` (status bar in `footer`) |

**Gate.** `tests/AkmlSql.Web.E2E.Tests/AxeAccessibilityTests.cs` (package `Deque.AxeCore.Playwright`, test project
only) runs axe with tags `wcag2a`, `wcag2aa`, `best-practice` on `/`, `/snippets`, `/styles`, `/history`, `/settings`
and `/settings/connections` × {light, dark, high contrast}. It asserts zero violations and prints each violation's id
and selector on failure. Keyboard check: Tab reaches `doc-tab-close` and the style preview.
