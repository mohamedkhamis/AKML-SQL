# Asset provenance

Prepared 22 October 2026. All files are local review assets. No generated image is presented as an actual AKML product screen.

| Local asset | Source | Notes |
| --- | --- | --- |
| `assets/workspace.png` | `src/AkmlSql.Site/wwwroot/img/screenshots/query-results.png` | Copied unchanged. Real browser SQL Server workspace with Northwind sample data. Existing capture, not newly executed for this proposal; visible version is 1.26.1010.2224. |
| `assets/completion.png` | `src/AkmlSql.Site/wwwroot/img/screenshots/light/05-join-suggestions-crop.png` | Copied unchanged. Actual SSMS 22 JOIN suggestions, Northwind schema. |
| `assets/styles.png` | `src/AkmlSql.Site/wwwroot/img/screenshots/dark/02-format-styles-crop.png` | Copied unchanged. Actual SSMS Format styles UI. |
| `assets/history.png` | `src/AkmlSql.Site/wwwroot/img/screenshots/dark/01-sql-history-crop.png` | Copied unchanged. Actual SSMS History UI from the existing public-site assets. |
| `assets/sql-editor-fresh.png` | Installed AKML browser application, `http://localhost:59416/` | Newly captured 22 October 2026 in an isolated browser context. Illustrative Northwind query typed and formatted; not executed. See `product-capture.json`. |
| `assets/postgresql-connect-fresh.png` | Installed AKML browser application, `http://localhost:59416/` | Newly captured 22 October 2026. Actual PostgreSQL connection dialog with synthetic values, no password, no database connection attempted. |
| `assets/sql-server.png` | [Microsoft SQL Server product page](https://www.microsoft.com/en-us/sql-server), “Cloud agility through Azure” product illustration | Official linked SQL Server 2025 artwork; display unchanged, for database identification. Its version label is not an AKML compatibility claim. |
| `assets/postgresql.png` | [Official PostgreSQL elephant PNG](https://www.postgresql.org/media/img/about/press/elephant.png) | Official artwork, unchanged. [Logo reference and usage-policy link](https://wiki.postgresql.org/wiki/Logo). |
| `screenshots/*` | The local concept HTML pages rendered by Chromium | New design-preview captures. These depict proposed marketing pages, not a deployed AKML website. |

Microsoft artwork download URL: `https://cdn-dynmedia-1.microsoft.com/is/image/microsoftcorp/272650-Overview-1.3-new-800x513?wid=800&qlt=90&fmt=png-alpha`.

Third-party names and marks identify their respective database products. Inclusion does not imply endorsement. Confirm applicable image/brand usage conditions for the chosen publication assets.

## PostgreSQL screenshot scope

After pairing the temporary browser context with the installed engine, the actual PostgreSQL connection dialog was captured and included in the product tour. The first attempt navigated before the engine pairing was retained; using the application's navigation resolved the capture issue. No controls were forced enabled and no production settings were changed. This verifies the dialog's availability, not PostgreSQL query execution. A fresh PostgreSQL query/results capture remains an explicit asset task before production implementation.

## Reproduction

`python reports/marketing-homepage-2026-10-22/build-previews.py` regenerates the three concept pages and gallery and copies the existing repository product images. It requires the downloaded database images and fresh editor capture already present in `assets/`.

`node reports/marketing-homepage-2026-10-22/review.cjs` runs the local sample browser checks and refreshes desktop, full-page, mobile and comparison screenshots. It reuses the repository's existing Playwright installation and browser.

`capture-product.cjs` is an opt-in live screenshot helper. It uses the installed web app and the existing engine-pairing pattern, reads the published pairing PIN in memory, and does not print or persist credentials. It creates a temporary paired browser context; it does not connect to a database or execute SQL. It may require the installed engine to expose the appropriate capabilities. Do not run it as part of routine preview verification.
