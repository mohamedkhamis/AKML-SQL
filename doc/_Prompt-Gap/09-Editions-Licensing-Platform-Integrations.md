# 09 — Editions, Licensing, Platform Features & Integrations

Scope: hosts/IDEs, license tiers and what they gate, the Command Palette, Bulk Actions, the Redgate Platform, and external integrations.

Status legend: ✅ done · 🟡 partial · ❌ missing · ➖ out of scope

---

## 1. Hosts / IDE support

| Host | Notes | Status |
|---|---|---|
| SQL Server Management Studio (SSMS) | Primary host; SSMS 21 & 22 (x64 + ARM64) supported in current builds | 🟡 SSMS 22 x64 only; no 21/ARM64 |
| Visual Studio | 2019 / 2022; some shortcuts differ (Rename `Shift+F2`; Command Palette `Alt+P`) | 🟡 VS 2026 (18.x) only, not 2019/2022 |
| Azure Data Studio | Separate, reduced-feature extension (formatting, snippets, format-by-selection, custom styles) | ❌ no ADS extension (Blazor Web edition instead) |
| Microsoft Fabric | Autocomplete + formatting for Fabric-specific queries | ❌ no Fabric support |
| Menu placement | SQL Prompt appears under the **Extensions** menu in SSMS 21/22 / VS; can be promoted to top-level | 🟡 always top-level "AKML SQL" menu, not under Extensions |
| SQL Server version support | Through SQL Server 2025 (preview) incl. database-scoped configuration previews | 🟡 TSql170 parser = SQL 2022; no 2025 preview |
| Entra ID auth | Dedicated Redgate Entra ID app for Azure SQL DB authentication | 🟡 AAD-Integrated/Managed-Identity reuse only; no dedicated app, interactive/MFA unsupported |

## 2. Licensing tiers (what's gated)

| Capability | Perpetual | Subscription (standalone) | Toolbelt Essentials (subscription) | Status |
|---|---|---|---|---|
| Core writing/formatting/refactoring/snippets/analysis/tabs | ✓ | ✓ | ✓ | ➖ free/MIT, no tiers |
| SQL Prompt AI (generate/explain/fix/optimize/ghost-text) | ✗ | ✓ | ✓ | ➖ free, BYO-key AI (no tier gate) |
| Query Index Analysis (AI/ML) | ✗ | ✓ (v11.05+) | ✓ | ➖ free; AI index suggestions exist, no gate |
| Redgate Platform (cloud share styles/snippets/CA rules) | ✗ | ✗ | ✓ | ➖ no Redgate-cloud analog |
| Bulk Actions / cross-tool integrations | — | — | ✓ (TB/TBE) | ➖ free/MIT, no tiers (see §4) |
| License management | Help ▸ Manage License (log out/in to refresh entitlements) | | | ➖ no license server (MIT) |
| Org opt-out of AI | Available | | | 🟡 per-user config opt-in/out; no org enforcement |

## 3. Command Palette

| Feature | Description | Where / Shortcut | Status |
|---|---|---|---|
| Command Palette | Find & run "hidden" SQL Prompt commands, common SSMS commands, and search DB objects | `Alt + S` (SSMS) / `Alt + P` (VS) | 🟡 commands only (AKML+DTE); no DB-object search; shortcut `Ctrl+Shift+P` |
| Run any action by typing | e.g. type "snippet", "format", an object name | palette | 🟡 fuzzy command search only; objects not searchable |

## 4. Bulk Actions (Toolbelt / Toolbelt Essentials)

| Feature | Description | Where | Status |
|---|---|---|---|
| Bulk formatting | Apply a style across an entire codebase at once | Bulk Actions menu / Command Palette | 🟡 CLI only; in-IDE wizard exists but unwired |
| Bulk code analysis | Run analysis across many objects/files at once | Bulk Actions menu / Command Palette | ✅ in-IDE folder scan + CLI analyzer |
| Command-line formatter | Apply styles via CLI / PowerShell / batch for automation | external CLI | ✅ akmlsql-format CLI (file/dir/stdin, --check/--diff/--report) |

## 5. Redgate Platform (cloud, TBE subscription)

| Feature | Description | Status |
|---|---|---|
| Share formatting styles | Store/share styles in cloud team spaces | ➖ no Redgate-cloud analog (local/file styles) |
| Share snippets | Share snippets via Snippet Manager → Platform | ➖ no Redgate-cloud analog (local snippet files) |
| Share code-analysis rules | Sync CA rule sets across the team | ➖ no Redgate-cloud analog (per-project .casettings) |
| Auto-download shared items | Shared items download and become available automatically | ➖ no Redgate-cloud analog |

## 6. External / cross-tool integrations

| Feature | Description | Status |
|---|---|---|
| SQL Dependency Tracker | See "uses" / "used by" dependencies of an object from within SQL Prompt (TB/TBE) | ➖ Redgate companion product (AKML has in-editor Find References) |
| Redgate Data Modeler | "Open Data Modeler" from Object Explorer context menu (SSMS) → opens schema in browser | ➖ Redgate companion product |
| Open in Excel | Export grid results to Excel (see file 03) | ✅ GridExportManager .xlsx export (EnableOpenInExcel) |
| SQL Test / tSQLt snippets | Importable test snippets (see file 05) | ❌ no tSQLt test snippets |
| GitHub snippet repo | Clone community snippets (see file 05) | ❌ no GitHub snippet repo |

## 7. Getting-started / housekeeping

| Feature | Description | Status |
|---|---|---|
| Requirements | OS / SSMS / VS / .NET prerequisites; SQL Server permissions may be needed | ✅ deployment.md prereqs + .NET 4.7.2 manifest dependency |
| Install & run | Add-in install; menu under Extensions | ✅ Inno Setup installer (vswhere detect, silent /TARGETS) |
| Check for Updates (CFU) | In-product update; now launches the unified Toolbelt Essentials installer | ✅ AkmlSql.Updater manifest check; self-update, no TBE |
| Privacy information | Usage data / privacy docs | ✅ ai-privacy-commitment.md + telemetry off-by-default + AI disclosure modes |
| Log files | Diagnostic logs location for support | ✅ %AppData%/AKML SQL/logs + View Logs command |
| Quick reference guide (PDF) | Official one-page shortcut/feature reference | ❌ no PDF quick-ref (markdown docs only) |

---

## Consolidated keyboard-shortcut master list

| Action | SSMS | VS |
|---|---|---|
| Show suggestions | `Ctrl+Space` | `Ctrl+Space` |
| Toggle suggestions | `Ctrl+Shift+P` | `Ctrl+Shift+P` |
| Refresh suggestions | `Ctrl+Shift+D` | `Ctrl+Shift+D` |
| Format SQL | `Ctrl+K, Ctrl+Y` | `Ctrl+K, Ctrl+Y` |
| Apply casing | `Ctrl+B, Ctrl+U` | same |
| Qualify object names | `Ctrl+B, Ctrl+Q` | same |
| Expand wildcards | `Ctrl+B, Ctrl+W` | same |
| Insert semicolons | `Ctrl+B, Ctrl+C` | same |
| Add/remove square brackets | `Ctrl+B, Ctrl+B` | same |
| Inline stored procedure | `Ctrl+B, Ctrl+I` | same |
| Encapsulate as new proc | `Ctrl+B, Ctrl+E` | same |
| Rename scripted object | `F2` | `Shift+F2` |
| Toggle code analysis | `Ctrl+Shift+A` | `Ctrl+Shift+A` |
| Open Issue Details | `Ctrl` (in underline) | same |
| Open Prompt AI | `Alt+Z` | `Alt+Z` |
| Manual AI completion | `Ctrl+Alt+Up` | `Ctrl+Alt+Up` |
| Command Palette | `Alt+S` | `Alt+P` |
