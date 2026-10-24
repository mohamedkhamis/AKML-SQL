# AKML public website enhancement plan

Prepared 22 October 2026, Africa/Cairo. Status: design proposal for owner selection.

## Outcome

Make the homepage answer “Why should I use AKML?” within the first screen, then show real product evidence, explain SQL Server and PostgreSQL availability, and lead visitors to the right installation or setup guide.

This proposal covers the **public product/marketing website**. It does not propose new database features. Three complete local homepage samples accompany it; the production Blazor application has not been changed or deployed.

Open [the design gallery](index.html), [the side-by-side image](screenshots/comparison.png), or an individual sample:

| Option | Direction | Why choose it | Tradeoff |
| --- | --- | --- | --- |
| **A — Product Studio (recommended)** | Ivory, navy, orange; split hero; benefits followed by product evidence | Balances accessibility, product credibility and clear setup choices for a broad audience | Screenshots in the split hero need the supplied full-size viewer to read code |
| B — Developer Dark | Dark canvas, mint accents; centered statement; wide workspace screenshot | Closest to a developer tool aesthetic and the earlier Developer Dark direction in spec 034 | Less visual separation from the dark product screenshots; needs careful contrast work |
| C — Editorial Light | White, blue, serif display type; open editorial composition | Calm, distinctive presentation with strong reading hierarchy | More departure from the current visual style; product appears lower on the first screen |

The layout direction and the visitor's light/dark theme are separate decisions. The selected production direction must still support the site's light, dark and high-contrast behavior.

## What the current site misses

Source review found that `Components/Pages/Home.razor` still introduces AKML primarily as an SSMS 22 extension. Its hero, metadata, proof strip and final download pitch do not explain the newer browser/PostgreSQL workflow. A feature inventory follows, but the visitor has to translate the feature names into reasons to use the product.

The current comparison wording also promises full SQL Prompt replication and says everything stays on the user's machine. Replace these broad statements with specific supported behavior and accurate AI/provider guidance. The repository records deferred desktop commands and separate browser AI behavior; those blanket promises are too broad.

The existing project already provides useful assets, `ProductScreenshot.razor`, a screenshot viewer, documentation ingestion, themes, downloads, feedback and consent-aware metrics. Reuse these systems.

## Recommended homepage content

| Order | Section | Proposed content | Visitor outcome |
| --- | --- | --- | --- |
| 1 | Hero | **Good SQL. Less friction.** “Write, review and reuse SQL with completion, formatting, analysis and history — in SSMS or your browser.” Primary: Download AKML. Secondary: Explore the product. | Understand the purpose and see the next step |
| 2 | Trust facts | Free and open source; MIT license; SSMS 22 and browser edition | Understand ownership and product format without invented popularity statistics |
| 3 | Why use AKML? | Find schema names as you type; make SQL easier to review; recover and reuse previous queries | Connect capabilities to everyday problems |
| 4 | Database choices | Separate SQL Server and PostgreSQL panels with database imagery, capabilities and prerequisites | Find the right product experience |
| 5 | Product tour | Completion/JOIN help, format styles, history, browser workspace; actual screenshots with captions and zoom | See evidence, not only marketing claims |
| 6 | Optional AI | Explain a query, discuss an error and choose a provider; link to setup and privacy guidance | Understand both usefulness and setup requirements |
| 7 | Getting started | “I use SSMS 22” versus “I want a web workspace”; installation and engine-pairing guidance | Follow an appropriate setup path |
| 8 | FAQ | Cost, AI optionality, browser engine requirements, PostgreSQL availability | Resolve adoption questions |
| 9 | Footer | Documentation, feedback, source, license and existing privacy links | Find support and supporting information |

Avoid unmeasured promises such as “10× faster,” fabricated testimonials, inflated feature counts, universal compatibility claims, and “no setup” for the browser database workflow. Do not describe query analysis as a guarantee that queries are safe or fast.

## Support facts to preserve

| Capability | SQL Server / SSMS 22 | SQL Server / browser | PostgreSQL / browser |
| --- | --- | --- | --- |
| Query execution and schema browsing | SSMS integration | Connected AKML engine | Connected AKML engine with PostgreSQL enabled |
| Completion | AKML engine | Live schema-aware help with engine; limited offline fallback | PostgreSQL engine language services |
| Formatting and analysis | AKML engine | Can run locally in the browser | Engine required |
| History and snippets | Available | Available | Available with dialect-aware behavior |
| Result editing | Do not imply AKML browser grid features are SSMS features | Eligibility and key/provenance restrictions apply | Eligibility and key/provenance restrictions apply |
| SQL Server refactoring tools | Advertise only implemented tools | Advertise only implemented tools | Do not imply parity; T-SQL-only surfaces are disabled |
| AI | Optional provider setup | Optional provider setup | Optional provider setup and dialect-aware behavior |

PostgreSQL is **not an SSMS extension target**. Source implementation and the spec 042 acceptance record establish the browser support; a fresh compatibility campaign was not run for this design proposal. Do not publish a supported server-version matrix until the release's verified results have been checked. Microsoft SQL Server artwork is identification, not a support-version claim.

## Screenshot and visual plan

Use actual product screens as the main visual material. Database logos/illustrations identify ecosystems; they should not compete with the AKML product.

The delivered samples use the existing Northwind query/results capture, existing SSMS feature captures, a fresh browser editor capture with an illustrative SQL query, and a fresh PostgreSQL connection-dialog capture with demo values. Both fresh captures show the installed Web 1.26.1022.0701 UI. No database connection or query execution was performed for these new captures. See [ASSETS.md](ASSETS.md) for exact origins. Desktop and mobile screenshots of all three concepts are in [screenshots](screenshots/).

Before the selected design ships, refresh a consistent set on the intended release:

1. SQL Server browser workspace: schema, readable query and Northwind results.
2. PostgreSQL browser workspace: public synthetic demo schema, query results and language help; show the PostgreSQL indicator.
3. SSMS 22: JOIN suggestions, format preview and query history.
4. PostgreSQL connection dialog with synthetic host/database labels and no passwords.
5. SQL Server and PostgreSQL analysis examples, captioned by dialect.

Capture dark and light variants at a consistent desktop size. Use tight feature framing where needed, retaining the relevant UI context. Do not expose private database names, credentials, personal history or tokens. Do not fabricate execution results or present a mock interface as a real capture. Keep an asset manifest with source, release, date, theme, dimensions and demo dataset. Follow each third-party asset's applicable usage terms before publication.

Use responsive images with explicit dimensions, a prioritized hero image, lazy-loaded lower images, readable captions, alt text, and a keyboard-accessible full-size viewer. Target a compressed hero below 250 KB and total initial image transfer below 500 KB, subject to screenshot readability; these are proposed budgets, not measured results of the sample PNGs.

## Implementation sequence after design selection

| Phase | Deliverable | Acceptance |
| --- | --- | --- |
| 1 — Agree content and design | Selected A/B/C direction; final hero and benefits; support matrix verified against intended release | Owner selects the visual direction; no unsupported database/feature claims |
| 2 — Record the feature contract | New feature spec, plan and tasks through the repository's Spec Kit workflow; record the chosen direction as a change to the old spec 034 visual direction | Constitution check passes; source changes remain uncommitted; no branch creation without explicit instruction |
| 3 — Prepare assets | Fresh SQL Server/PostgreSQL/SSMS captures; documented brand images; optimized responsive variants | Images are real, current, readable and use sample data |
| 4 — Implement homepage | Hero, benefit narrative, database panels, product tour, setup choices and FAQ in existing Blazor site | Desktop and mobile visitor can identify value, database support and next step |
| 5 — Align adjacent pages | Update Features, Download introduction, host notice, navigation, metadata and relevant public setup docs | Homepage and adjacent pages give consistent support information; retain download routing, docs ingestion and feedback access |
| 6 — Verify and release preparation | Component tests, responsive browser checks, theme/navigation checks, full required build, deployment-ready review | Required checks pass; record evidence and limitations; deploy only under the owner's requested release scope |

Suggested work order is P0 value proposition + truthful support + CTA, P1 screenshots + feature tour + setup guidance, P2 metadata + polish + measurement. Estimates should be set after selecting the design and confirming the screenshot environment; no unsupported delivery date is promised.

## Repository implementation map

| Area | Existing location / action |
| --- | --- |
| Homepage | `src/AkmlSql.Site/Components/Pages/Home.razor` |
| Shared navigation/footer | `src/AkmlSql.Site/Components/Layout/MainLayout.razor` |
| Feature descriptions | `src/AkmlSql.Site/Components/Pages/Features.razor` |
| Download guidance | `src/AkmlSql.Site/Components/Pages/Download.razor`; preserve manifest/fallback and tracking behavior |
| Support notice | `src/AkmlSql.Site/Components/HostSupportNotice.razor` |
| Screenshot rendering | Extend `Components/ProductScreenshot.razor` and existing `wwwroot/js/screenshot-viewer.js` as needed |
| Visual styling | `wwwroot/css/site.css`; use existing generated theme tokens; never hand-edit generated theme CSS |
| Theme source | `docs/theme-tokens.json` if new tokens are necessary; run generator and drift check |
| Images | `src/AkmlSql.Site/wwwroot/img/`; add provenance outside publicly ingested internal planning files |
| Tests | Existing `tests/AkmlSql.Site.Tests` plus relevant browser theme/navigation/screenshot checks |
| Progress | Record implementation and verification in `doc/progress.md`; update project memory only if recorded product facts change |

Keep this proposal under `reports/`. It is an internal design review, not a public documentation article, and should not enter automatic `doc/` ingestion.

## Acceptance and measurement

- At 320, 390, 768, 1440 and 1920 px, no horizontal page overflow, hidden CTAs or unreadable content. Code in images can be enlarged.
- Keyboard users can reach navigation, tour buttons, image dialog, FAQ and setup choices; Escape closes the image dialog and focus returns to its trigger.
- Light/dark/high-contrast themes survive enhanced navigation and reload; no new inline-script CSP exception is introduced.
- With JavaScript disabled, the core message, database facts, main image and download/docs links remain usable.
- Every image has appropriate alt text; normal text meets WCAG AA contrast. Test actual rendered colors and focus indicators, not just token values.
- Home, Features and Download metadata describe the supported experiences accurately. Use dynamic release information rather than hard-coded versions.
- Preserve existing download endpoints, availability/fallback behavior, consent, feedback and documentation search.
- Run relevant Site tests and the theme drift gate, then the required full solution restore/build through Visual Studio MSBuild before declaring implementation complete. This is future production verification; it was not needed for standalone HTML proposals.
- Ask five first-time readers to identify what AKML does, which experience supports their database, and how to start; target at least four correct responses within 30 seconds. This study has not yet been run.
- Measure homepage-to-download and setup-guide engagement using the site's existing consent-aware analytics. Establish a baseline before launch and review afterward. Add only minimal aggregate CTA events if the existing metrics cannot answer these questions; no new visitor profiling is part of this work.

## Design-sample verification

The accompanying `verification.json` records Chromium checks for each concept at 1440, 768, 390 and 320 px, plus gallery checks at 1440, 390 and 320 px. The first run found no horizontal overflow, broken displayed images, missing in-page anchors or JavaScript page errors. Product tour switching, image zoom/Escape, FAQ expansion and design-selection feedback passed for all three concepts.

These checks validate local sample behavior, not the production Blazor implementation, all browsers, all accessibility requirements, or database compatibility. The sample pages use original PNG captures for visual review; production asset budgets remain work to do.

## Sources reviewed

- `PROJECT_MEMORY.md`, `CLAUDE.md`, `.specify/memory/constitution.md`.
- Current Site Home, Features, Download, layout, screenshot component inventory and app settings.
- Spec 034 product-site specification/tasks and spec 038 downloads/admin specification.
- Spec 042 PostgreSQL baseline, spec 043 latest baseline, latest relevant `doc/progress.md` entries.
- Browser formatter/analyser service routing and connection-dialog source.
- Existing `SiteScreenshotTour.cs` and installed-engine test harness.
- [Microsoft SQL Server product page](https://www.microsoft.com/en-us/sql-server), [PostgreSQL logo reference](https://wiki.postgresql.org/wiki/Logo).
