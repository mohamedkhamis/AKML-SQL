# Implementation Plan: Format Styles Window Promotion — the dedicated SQL Prompt-grade style editor

**Branch**: `033-format-styles-window` (work currently carried on `030-closure-followups`, same convention as specs 031/032) | **Date**: 2026-07-22 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/033-format-styles-window/spec.md`

## Summary

Promote the existing `FormatStylesEditorWindow` (spec 020) from a preview-only browser into the single, SQL Prompt-grade home for all SQL style editing, and slim the Options "Format › Styles" page to Redgate's exact split (active-style dropdown + "Edit formatting styles…" button + Behavior toggles). Approach is engine-authoritative: enrich the reflection-generated setting schema to v2 (5-category hierarchy via `ParentId`, `AllowedEnumValues`/`Description`/`Min`/`Max` from a new `[SettingMeta]` attribute on the profile POCOs — there are **no C# enums** to reflect over), add two small IPC reads (`ProfileGet` 34/134 returning **raw file text**, `ProfileRename` 35/135 as an atomic engine-side transaction), and wire the window's editing loop: load-on-select → dirty tracking → **JsonNode merge-save** over the raw loaded JSON (preserving `metadata` + `ExtensionData`) via the existing `ProfileSave`. Lifecycle (New-based-on / Rename / Delete-guarded / ✔ active) lands in a sectioned style list; the dead legacy `ProfileEditorDialog` stack (5 files) is deleted; SSMS DTE-menu + Command Palette gaps close. Full findings in [research.md](./research.md); shapes in [data-model.md](./data-model.md) + [contracts/](./contracts/).

## Technical Context

**Language/Version**: C# (LangVersion latest). `AkmlSql.Formatting` + `AkmlSql.Engine` target **net10.0** (engine is NOT trimmed — `PublishTrimmed=false`, so attribute reflection in the schema builder is safe); `AkmlSql.Core` dual-targets **netstandard2.0 + net10.0** (wire DTOs, MessagePack `[Key(n)]` append-only, System.Text.Json **9.*** on the netstandard2.0 target — CLAUDE.md's "8.x" is stale); shell code is net472 WPF in the shared `.projitems` project compiled into SSMS 22 + VS 2026 hosts.
**Primary Dependencies**: MessagePack (IPC), System.Text.Json (+`System.Text.Json.Nodes` for the merge-save — already used in this VM on net472), VS SDK 17.14 (DialogWindow, status bar, DTE owner), existing `ThemeRegistry`/`ThemeTokens`/`ComboBoxTheming` WPF theme system.
**Storage**: `.akmlstyle` JSON profiles — 8 read-only built-ins beside the engine assembly, customs at `%AppData%\AKML SQL\profiles` (custom-first shadowing, `OrdinalIgnoreCase`), `<name>.source.json` import sidecars; active style = `AppSettings.Formatter.ActiveProfile` in shell-owned `config.json` (atomic writes via ConfigManager). No new stores.
**Testing**: xunit — `tests/AkmlSql.Formatting.Tests` (schema v2 + ProfileManager; ~13 s), `tests/AkmlSql.Engine.Tests` (handlers via direct instantiation + `InProcessTransport`; **always** `--filter "FullyQualifiedName!~PerformanceBaseline"` — untagged ~13-min gate in-project), `tests/AkmlSql.Core.Tests` (DTO round-trips), `tests/AkmlSql.Shell.Shared.Tests` (net472+WPF, xunit 2.9.2/StaFact; internals directly instantiable via `.projitems` import; ThemeRegistry = one StaFact per window class). New injectable IPC seam in the VM is a prerequisite for all VM tests (no fake-IPC exists today).
**Target Platform**: Desktop only — SSMS 22 + VS 2026 via `AkmlSql.Shell.Shared`; engine changes ride the normal engine publish. Web edition untouched (its trimmed WASM never calls `FormatSettingSchema`).
**Project Type**: out-of-process .NET 10 engine + shared net472 WPF shell; this feature touches engine (schema/IPC/ProfileManager), Core (2 DTO pairs + 2 message ids), and shell (window/VM/Options page/palette/SSMS menu).
**Performance Goals**: preview loop unchanged (100 ms debounce, ≤250 ms p95 budget from spec 020); ProfileGet is a single small file read (<10 ms typical); schema build stays a process-lifetime Lazy (attribute reads add one reflection pass at first request).
**Constraints**: IPC additive-only (new ids 34/134 + 35/135; zero existing key-layout changes; schema enrichment rides inside the existing `SchemaJson` string); setting/group **ids byte-frozen** (SqlPromptKey `ExplicitKeyMap` keys on them); mixed-version tolerance both directions (v1 schema → shell degrades to flat/free-text; old engine → 34/35 unsupported surfaces in status bar); built-in profiles remain immutable; `AllowedValues` are exact stored spellings (no enum converter exists); shell builds per-project full MSBuild only.
**Scale/Scope**: 18 FRs / 5 user stories; ~178 settings across 18 groups get `[SettingMeta]` (~60 enum-like, 14 ranged ints); 6 new flattened `insertStatements.*` ids; 2 new IPC pairs; ~10 shell files touched + 5 deleted; 9 SC gates.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

No `.specify/memory/constitution.md` exists. The de-facto project principles in `CLAUDE.md` are used as gates (same convention as specs 030–032):

| Gate (from CLAUDE.md) | Assessment |
|---|---|
| **Out-of-process boundary** — style/profile logic engine-side; front ends orchestrate | ✅ Schema enrichment, raw-read, rename transaction, delete/save hardening are all engine/Formatting-library code. Shell adds orchestration + WPF only; the merge-save is shell-side by design (the engine's `ProfileSave` contract is unchanged) and is pure JSON manipulation of engine-provided text. |
| **IPC wire compatibility** — codes unchanged, additive fields only | ✅ Two new request/response id pairs (34/134, 35/135 verified free); zero existing `[Key]` layouts change; schema v2 rides inside the existing `SchemaJson` string. Mixed-version degrade specified both directions (FR-013, contracts). |
| **Shared `.projitems` / per-host MSBuild** | ✅ All shell work in `AkmlSql.Shell.Shared` (both hosts get it); per-host cost is limited to the SSMS `EnsureTopLevelMenu` array (host-specific by nature). Build via per-project full MSBuild per repo rules. |
| **TDD for engine/Core logic** | ✅ Schema-v2 completeness tests (aggregate-offenders idiom), ProfileManager `TryReadRaw`/`Rename` tests, handler tests incl. the delete-bool and save-cap regressions, DTO round-trips — all failing-test-first. VM tests gated on the new IPC seam (planned as an early task). |
| **Async/IPC conventions** (`async Task<RpcMessage?>`, no `.GetAwaiter().GetResult()`) | ✅ New handlers are thin `IRpcRequestHandler` adapters like the existing seven; VM keeps `SendRequestAsync` + `SwitchToMainThreadAsync` discipline; no blocking waits added. |
| **Security conventions** (path validation, size caps, absolute paths) | ✅ Rename/Get go through `SanitizeFileName` + `ValidatePathWithinBase`; `ProfileSave` gains the missing 1 MB cap; built-in immutability enforced at ProfileManager level (directory-derived, not JSON-trusted). |
| **WPF conventions** (ThemeManager/ThemeTokens, frozen brushes, static fonts, DTE owner, themed combos) | ✅ Enum dropdowns via `ComboBoxTheming.Apply` (plain-string items rule); sub-dialogs are `ThemeAwareWindow` with `Owner = styles window` (nested-modal rule); broken lock-glyph template rebuilt in code; no hardcoded chrome hex. |
| **Never commit/push without explicit user instruction** | ✅ Applies throughout; plan artifacts left uncommitted until instructed. |

**Post-design re-check**: PASS — no violations introduced by Phase 1 design; Complexity Tracking empty.

## Project Structure

### Documentation (this feature)

```text
specs/033-format-styles-window/
├── spec.md              # Feature specification (approved 2026-07-22)
├── plan.md              # This file
├── research.md          # Phase 0 — R1..R12 decisions with file:line evidence
├── data-model.md        # Phase 1 — SettingMeta, schema v2, DTOs, VM state machine
├── quickstart.md        # Phase 1 — build/test/deploy/manual-verification script
├── contracts/
│   ├── ipc-profile-messages.md      # ProfileGet 34/134, ProfileRename 35/135 (+ delete/save hardening)
│   └── style-editor-schema-v2.md    # Schema v2 JSON body contract
└── tasks.md             # Phase 2 (/speckit.tasks — NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/AkmlSql.Formatting/Profiles/
├── SettingMetaAttribute.cs          # NEW — Description/AllowedValues/Min/Max attribute
├── FormattingProfile.cs             # ~178 properties annotated (18 POCOs)
├── FormatSettingSchema.cs           # v2: ParentId category map, attribute read, insertStatements flatten, SchemaVersion 2
├── ProfileManager.cs                # NEW TryReadRaw + Rename (atomic, sidecar-aware)
└── ProfileSerializer.cs             # unchanged (side effects documented; reason ProfileGet is raw)

src/AkmlSql.Core/Ipc/
├── RpcMessage.cs                    # ProfileGet=34/134, ProfileRename=35/135
└── Messages/                        # NEW ProfileGetRequest/Response, ProfileRenameRequest/Response

src/AkmlSql.Engine/
├── Formatter/FormatRequestHandler.cs    # HandleProfileGet/HandleProfileRename; delete-bool + save-cap fixes
├── Handlers/Formatting/FormattingHandlers.cs  # two new typed adapters
└── EngineHandlerRegistry.cs             # register both

src/AkmlSql.Shell.Shared/
├── Formatting/FormatStylesEditorWindow.cs    # 2-level tree, enum combos, sectioned list + ✔/lock, Save button,
│                                             #   context menu, New-based-on/Rename dialogs, sample editing (T069)
├── Formatting/FormatStylesEditorViewModel.cs # IPC seam, load-on-select, dirty, ProfileJsonMerger, lifecycle ops
├── Formatting/ProfileJsonMerger.cs           # NEW — pure merge (testable without IPC)
├── Dialogs/Pages/FormattingPage.cs           # Edit-styles button, Behavior header, post-close refresh (clobber fix)
├── Productivity/CommandPalette/CommandRegistry.cs        # -EditProfile +FormatStyles
├── Productivity/CommandPalette/CommandPaletteViewModel.cs # switch arms
├── PackageGuids.cs                           # remove CmdEditProfile
├── AkmlSql.Shell.Shared.projitems            # remove 5 legacy entries, add new files
└── Ui/  (DELETED: ProfileEditorDialog.cs, ProfileEditorViewModel.cs,
     OptionCategoryTreeBuilder.cs, SqlPreviewRenderer.cs) + Commands/EditProfileCommand.cs

src/AkmlSql.Ssms22/AkmlSqlPackage.cs          # EnsureTopLevelMenu: + (CmdFormatStyles, "Format Styles...")

tests/
├── AkmlSql.Formatting.Tests/Profiles/        # schema-v2 completeness, ProfileManager TryReadRaw/Rename
├── AkmlSql.Engine.Tests/                     # handler tests (filtered runs), import→get→edit→save round-trip
├── AkmlSql.Core.Tests/Ipc/                   # DTO round-trips for the two new pairs
└── AkmlSql.Shell.Shared.Tests/               # ProfileJsonMerger, VM flows via IPC seam, FormattingPage refresh
```

**Structure Decision**: single existing solution; no new projects. Feature spans the established engine/Core/shell layering with the same file-per-concern conventions each area already uses.

## Complexity Tracking

No constitution-gate violations; table intentionally empty.
