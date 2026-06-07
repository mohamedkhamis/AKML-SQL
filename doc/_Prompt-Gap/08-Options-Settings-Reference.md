# 08 — Options & Settings Reference (every pane)

Scope: the full **SQL Prompt ▸ Options** dialog tree and the granular settings on each pane, plus global option management.

Status legend: ✅ done · 🟡 partial · ❌ missing · ➖ out of scope
**[verify in UI]** = exact label/pane placement to confirm in SQL Prompt 11.

---

## 0. Options dialog — global controls

| Control | Description | Where | Status |
|---|---|---|---|
| Import options | Load an entire options set from file | bottom of Options dialog | ✅ Import… button |
| Export options | Save the entire options set to file | bottom of Options dialog | ✅ Export… button |
| Restore Defaults (page) | Reset only the current page | top-right of each page | ✅ per-page link |
| Restore All Defaults | Reset every page | bottom-left of Options dialog | ✅ button + confirm |
| Per-page help (`?`) | Interactive help dialogs with friendly-name links | each page | 🟡 F1 URL only, no per-page dialog |
| Dark theme support | Options UI follows SSMS dark theme | — | ✅ Dark/Light/System |

## 1. Main / Behavior

| Setting | Description | Status |
|---|---|---|
| Enable code suggestions | Master on/off for the suggestions box | 🟡 toggle not consumed by completion engine |
| Automatically trigger suggestions | Auto-show vs on-demand (`Ctrl+Space`) only | 🟡 AutoTrigger flag not consumed |
| Display object definitions | Show the object definition box on selection | ❌ box always on, no toggle |
| Show tooltips for ▸ Objects | Object tooltips on hover | 🟡 EnableMsDescription config-only |
| Show tooltips for ▸ Parameters | Parameter/function-parameter tooltips | 🟡 EnableParameterHighlight config-only |
| Insertion keys | Keys that commit a suggestion/snippet (default `Enter`, `Tab`) | 🟡 SpaceCommits/DotCommits config-only |

## 2. Suggestions

### 2.1 Suggestions ▸ Behavior
| Setting | Description | Status |
|---|---|---|
| Automatically show suggestions after… | Auto-show toggle + delay/frequency | 🟡 AutoTrigger flag not consumed |
| Use ranked suggestions | Relevance-ranked ordering | ❌ no setting |
| Make popups transparent when Ctrl held | Semi-transparent popups | ❌ no setting |
| Show tooltips for (Objects / Parameters) | Tooltip toggles | 🟡 config-only, no UI |
| Show object definitions | Definition box toggle | ❌ no enable setting |
| Decrypt encrypted objects | Show creation script of encrypted objects | 🟡 EnableEncryptedDecryption config-only |

### 2.2 Suggestions ▸ Types of suggestion
| Setting | Description | Status |
|---|---|---|
| List all database columns after a SELECT statement | Show all columns right after `SELECT` | 🟡 ColumnScope not consumed by provider |
| Other suggestion-type toggles | Which object kinds to suggest [verify in UI] | 🟡 scope unconsumed; types partial |

### 2.3 Suggestions ▸ Connections
| Setting | Description | Status |
|---|---|---|
| Databases/schemas to suggest | Scope suggestions to chosen DBs/schemas | ❌ no setting |
| Load suggestions for linked servers | Linked-server objects (also avoids master-DB access) | ❌ no setting |

### 2.4 Suggestions ▸ Join conditions
| Setting | Description | Status |
|---|---|---|
| JOIN condition criteria | How `ON` conditions are suggested (FK-based, name-based) | ✅ JoinAssist + match-by-name |

## 3. Inserted code

### 3.1 Inserted code ▸ Objects & statements
| Setting | Description | Status |
|---|---|---|
| Full statement syntax | Insert full syntax for ALTER / EXEC / INSERT (column names, data types, defaults) | ✅ INSERT statements page |

### 3.2 Inserted code ▸ Qualification
| Setting | Description | Status |
|---|---|---|
| Qualify object names with owner | `owner.object` qualification | ✅ schema-mode dropdown |
| Qualify column names | `table.column` qualification (auto for ambiguity, JOINs, cross-DB/linked-server) | ✅ qualify-columns toggle |
| Which object kinds to qualify | Configure scope | ❌ single mode, no per-kind |

### 3.3 Inserted code ▸ Aliases
| Setting | Description | Status |
|---|---|---|
| Assign aliases | Auto-alias tables/views | ✅ Tables Alias toggle |
| Include AS in alias definition | Include/exclude `AS` | ❌ no setting |
| Custom aliases | Object→alias map | ❌ no setting |
| Prefixes to ignore | Ignore prefixes when generating aliases | ❌ no setting |

### 3.4 Inserted code ▸ Special characters
| Setting | Description | Status |
|---|---|---|
| Enclose all identifiers in square brackets | Auto-bracket identifiers | 🟡 dropdown UI, engine WhenRequired only |
| Add parentheses for functions/data types | Auto-parentheses (+ parameter tooltip) | ❌ no setting |
| Automatically insert closing characters | Auto-close quotes/comments/brackets | ❌ no setting |

## 4. Format ▸ Styles
| Setting | Description | Status |
|---|---|---|
| Active style selector | Choose the style applied by Format SQL | 🟡 ActiveProfile config, no Options selector |
| Format-time actions | Which actions run with Format SQL (casing, semicolons, qualification, wildcard expansion, brackets) | 🟡 profile-level, not Options toggles |
| Edit/create/import/export styles | (see file 02) | 🟡 edit+round-trip work; create/copy UI deferred |

## 5. Tabs
| Setting | Description | Status |
|---|---|---|
| Tabs ▸ Color | Server/database→environment mapping; environments & colors; gradient toggle; restore defaults | ✅ rules CRUD + gradient |
| Restore open queries on startup | Reopen previous session's tabs | ✅ Restore-on-startup dropdown |

## 6. Queries
| Setting | Description | Status |
|---|---|---|
| Queries ▸ History | SQL History retention period (default 7 days), enable/disable auto-trim | ✅ enable + retention + max-entries |

## 7. Prompt AI
| Setting | Description | Status |
|---|---|---|
| Enable AI code completion | Ghost-text completion on/off | ✅ Inline ghost text + Labs toggle |
| Automatically request AI code completion after (ms) | Delay (default 500) / manual-only mode | 🟡 GhostTextDelayMs config-only |
| Show icon for editor selection | Selection AI icon (on by default) | 🟡 ShowEditorIcon config-only |
| Generate initial suggestions using SQL History | Seed suggestions from recent history | ❌ no setting |
| Check Prompt AI availability | Menu command to verify AI availability | ❌ no command |

## 8. Query Results
| Setting | Description | Status |
|---|---|---|
| Excel rounding fix | Prevent Excel rounding of numbers >15 digits on export (now a regular option) | ✅ Grid page toggle |

## 9. Connections & memory
| Setting | Description | Status |
|---|---|---|
| Connection settings | Manage how SQL Prompt connects for schema | 🟡 SQL-auth toggle + creds manager only |
| Memory / cache | Manage cache + memory use (batch/schema cache) | ✅ Schema Cache page |

## 10. Settings & snippet folder locations
| Setting | Description | Status |
|---|---|---|
| Change settings folder | Relocate the settings folder | ❌ path read-only, no relocate |
| Change snippet folder | Relocate the snippet folder (and point at shares) | ✅ personal + team folder paths |

## 11. SQL Prompt Labs
| Setting | Description | Status |
|---|---|---|
| Experimental features | Opt-in early/experimental features | ✅ Labs page |

## 12. Sharing your settings
| Setting | Description | Status |
|---|---|---|
| Share settings | Share formatting styles, snippets, and code-analysis rules with colleagues (folders or Redgate Platform) | 🟡 export + team folder, no Platform |
