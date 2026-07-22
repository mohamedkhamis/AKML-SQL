# Quickstart: Format Styles Window Promotion (spec 033)

## Build & test loop (inner)

```bash
# Engine-side changes (schema, ProfileManager, handlers) — fast suites first
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj          # ~13 s wall; schema-v2 + ProfileManager tests live here
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName!~PerformanceBaseline"   # ALWAYS filter — the untagged perf gate runs ~13 min otherwise
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj                      # message round-trip tests

# Shell-side changes (window, VM, FormattingPage, palette) — net472
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj      # ~25 s wall; VM/merge/page tests
```

## Shell extension build (per repo rules — never `dotnet build`, never solution build)

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build   -p:Configuration=Release -v:minimal
"$MSBUILD" "src/AkmlSql.VS2026/AkmlSql.VS2026.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.VS2026/AkmlSql.VS2026.csproj" -t:Build   -p:Configuration=Release -v:minimal
```

(`-t:Restore` is required once per session before `-t:Build`, else MSB4226.)

## Engine publish + deploy (dev machine; engine redeploy = FULL publish copy, never partial DLL swap)

```bash
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
```

Then run the elevated deploy script (stops `AkmlSqlWebEngine`, robocopies engine publish + SSMS extension, restarts service) — SSMS must be closed; the session is not admin, so the copy goes through a UAC-approved `Start-Process -Verb RunAs`.

## Manual verification script (maps to Success Criteria)

1. **SC-001 load-on-select**: SSMS → Tools → AKML SQL → Format Styles... Click "Default", then "Khamis Style" — tree values must visibly change (not identical defaults). Preview re-renders per selection.
2. **SC-002 edit round-trip**: Copy "Khamis Style" → select the copy → change e.g. `casing.reservedKeywords` and a comma option → Save → close window → Set the copy active → Format SQL a script → output reflects both edits. Reopen the window — the copy still shows the saved values.
3. **SC-003/004 readability**: tree shows exactly Global / Statements / Clauses / Expressions / Other; every enum setting is a dropdown; every setting shows a description; `tabSize` rejects `99999`.
4. **SC-005 read-only**: select "Default" — controls disabled, Save disabled, "Copy this style to edit" hint visible; double-click creates a copy.
5. **US3 lifecycle**: New Style… (name + based-on) → rename it → set active (✔ moves; status bar updates) → try Delete while active (blocked) → set another active → Delete (gone from `%AppData%\AKML SQL\profiles`).
6. **SC-006 import round-trip**: Import the Redgate `MohamedKhamis` style → edit one setting → Save → Export → exported file still carries the previously-unknown keys.
7. **US4 Options**: Options → Format → Styles: dropdown + "Edit formatting styles…" button + Behavior group. Open the editor from the button, create+activate a style, close — dropdown refreshes and selects it; press OK on Options — active style is NOT reverted.
8. **SC-007 discoverability**: "Format Styles..." appears in the SSMS AKML SQL menu (DTE-injected variant included) and in the Command Palette; VS 2026 Tools menu unchanged-working.
9. **Dirty guard**: edit a custom style, click another style → Save/Discard/Cancel prompt; Cancel keeps selection and edits.
10. **Mixed-version degrade** (optional, engine older than shell): window falls back to flat tree + text boxes without crashing.

## Watch-outs

- Clean `obj/bin` of shell projects if SDK versions changed (stale VSCT/CTO).
- The schema is cached per process on both sides — after engine redeploy, restart SSMS (shell static cache) to see v2.
- `ProfileManager` paths are NOT redirected by `AKML_APP_DATA_ROOT` — tests always inject temp dirs.
- Built-in styles regenerate via `AKML_UPDATE_BUILTIN_STYLES=1` (`BuiltInStyleGenerationTests`) — schema/attribute work must not change built-in `.akmlstyle` content; if a drift test fires, investigate rather than regenerate.
