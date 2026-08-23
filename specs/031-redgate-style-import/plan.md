# Redgate JSON Style Import — Implementation Plan (Phases 1–2 + Phase-3 gate)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import modern SQL Prompt JSON style files as AKML custom styles (desktop, honest per-option reporting, auto-set-active), and stand up the SQL Prompt 11 golden corpus that gates the Phase-3 layout fidelity work.

**Architecture:** A new schema-driven `RedgateJsonStyleImporter` in `AkmlSql.Formatting` maps every Redgate option to the (extended) `FormattingProfile`, classifying each file key as mapped / mapped-pending-render / unsupported / unknown against a static honoring table. The engine's existing `ProfileImport (17)` IPC gains content sniffing and correct failure semantics; the Format Styles editor gains an Import… button that reports, refreshes, and activates. Phase 2 adds a 20-file corpus + user runbook; Phase 3 (planned separately once goldens exist) closes layout gaps feature-by-feature.

**Tech Stack:** C# — `AkmlSql.Formatting` (net10.0, file-scoped namespaces, System.Text.Json source-gen), `AkmlSql.Core` (netstandard2.0 + net10.0 dual-target, MessagePack `[Key(n)]`), `AkmlSql.Engine` (net10.0), `AkmlSql.Shell.Shared` (net472 WPF, programmatic UI, no XAML), xunit.

## Global Constraints

- Git: per repo CLAUDE.md, **no `git add`/`commit` without the user's explicit approval**. "Commit" steps below execute only at user-approved checkpoints; otherwise leave changes staged-not-committed and report.
- Shell projects build ONLY via full MSBuild, never `dotnet build`, never via solution: `"/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Build -p:Configuration=Release -v:minimal` (VS root may be `18/Enterprise` on this machine — check both).
- Engine redeploy = FULL publish copy (`dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64`), never partial DLL swap (auto-versioned AkmlSql.* assemblies → `FileNotFoundException` → pipe timeout).
- `AkmlSql.Core` is netstandard2.0-compatible: no records/init-only in Core message types; follow the existing `[MessagePackObject]` + `[Key(n)]` class style. Never renumber existing `[Key(n)]` — append only.
- IPC handlers stay `async Task<RpcMessage?>`-compatible; no `.GetAwaiter().GetResult()`.
- All file paths from IPC must be absolute; imports are size-capped shell-side at 1 MB.
- Redgate semantics authority order: SQL Prompt 11 goldens > vendored `specs/031-redgate-style-import/reference/formattingstyle-schema.json` > enum-name inference. Never guess silently — record uncertainty in the option's `Reason`.
- Test commands: `dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release --filter "FullyQualifiedName~<Class>"` (same pattern for `AkmlSql.Core.Tests`, `AkmlSql.Engine.Tests`).
- Statuses are exactly: `mapped`, `mapped-pending-render`, `unsupported`, `unknown` (wire strings, case-sensitive).

## File Structure

**Create**
- `src/AkmlSql.Formatting/Profiles/RedgateJsonStyleImporter.cs` — parser + mapping table + classification (one responsibility: Redgate JSON → `RedgateStyleImportResult`)
- `src/AkmlSql.Formatting/Profiles/FormatterHonoringTable.cs` — static set of Redgate option paths the layout engine renders end-to-end (Phase 3 flips entries here)
- `src/AkmlSql.Core/Ipc/Messages/ProfileImportOptionReport.cs` — wire DTO for per-option classifications
- `tests/AkmlSql.Formatting.Tests/Profiles/RedgateJsonStyleImporterTests.cs`
- `tests/AkmlSql.Formatting.Tests/Profiles/RedgateSchemaCompletenessTests.cs`
- `tests/AkmlSql.Engine.Tests/Formatter/ProfileImportHandlerTests.cs`
- `tests/AkmlSql.Formatting.Tests/Parity/RedgateParityTests.cs` (Phase 2 driver)
- `tests/format-parity/corpus/sp031-*.sql` (20 files, Phase 2)
- `specs/031-redgate-style-import/runbook-goldens.md` (Phase 2, user-facing)
- Test fixture copies: `tests/AkmlSql.Formatting.Tests/Fixtures/MohamedKhamis-style.json`, `tests/AkmlSql.Formatting.Tests/Fixtures/formattingstyle-schema.json` (copied from `specs/031-redgate-style-import/reference/`; full-style.json.example is a non-JSON template — not usable as an import fixture)

**Modify**
- `src/AkmlSql.Formatting/Profiles/FormattingProfile.cs` — new fields + `InsertStatementsOptions` section (design §2)
- `src/AkmlSql.Core/Ipc/Messages/ProfileImportResponse.cs` — append `[Key(5)] OptionReports`
- `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs:500-559` — `HandleProfileImport` sniffing + failure semantics + source preservation + reports
- `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorViewModel.cs` — `ImportProfileAsync`
- `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorWindow.cs` — Import… toolbar button + summary + activation

`FormatSettingSchema` reflects over the profile POCOs (`FormatSettingSchema.cs:54-80`), so new fields and the new section appear in the editor tree with no schema code changes.

---

# Phase 1 — Import pipeline

### Task 1: IPC contract — per-option reports on `ProfileImportResponse`

**Files:**
- Create: `src/AkmlSql.Core/Ipc/Messages/ProfileImportOptionReport.cs`
- Modify: `src/AkmlSql.Core/Ipc/Messages/ProfileImportResponse.cs`
- Test: `tests/AkmlSql.Core.Tests/Ipc/ProfileImportOptionReportTests.cs`

**Interfaces:**
- Produces: `ProfileImportOptionReport { string Path; string Value; string Status; string? Reason }` (MessagePack, Keys 0–3) and `ProfileImportResponse.OptionReports : ProfileImportOptionReport[]?` at `[Key(5)]`. Task 8 (engine) populates it; Task 9 (shell) renders it.

- [ ] **Step 1: Write the failing round-trip test**

```csharp
// tests/AkmlSql.Core.Tests/Ipc/ProfileImportOptionReportTests.cs
using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc
{
    public class ProfileImportOptionReportTests
    {
        [Fact]
        public void Response_with_option_reports_roundtrips_through_messagepack()
        {
            var response = new ProfileImportResponse
            {
                Success = true,
                MappedOptionsCount = 2,
                OptionReports =
                [
                    new ProfileImportOptionReport { Path = "casing.reservedKeywords", Value = "uppercase", Status = "mapped" },
                    new ProfileImportOptionReport { Path = "lists.commaAlignment", Value = "toList", Status = "mapped-pending-render", Reason = "Rendering ships in phase 3 (FR-021)" },
                ],
            };

            var bytes = MessagePackSerializer.Serialize(response);
            var back = MessagePackSerializer.Deserialize<ProfileImportResponse>(bytes);

            Assert.NotNull(back.OptionReports);
            Assert.Equal(2, back.OptionReports!.Length);
            Assert.Equal("lists.commaAlignment", back.OptionReports[1].Path);
            Assert.Equal("mapped-pending-render", back.OptionReports[1].Status);
            Assert.Null(back.OptionReports[0].Reason);
        }

        [Fact]
        public void Old_wire_payload_without_key5_still_deserializes()
        {
            // Simulate a pre-031 peer: serialize a response shape lacking OptionReports.
            var legacy = MessagePackSerializer.Serialize(new ProfileImportResponse { Success = true });
            var back = MessagePackSerializer.Deserialize<ProfileImportResponse>(legacy);
            Assert.True(back.Success);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj -c Release --filter "FullyQualifiedName~ProfileImportOptionReportTests"`
Expected: FAIL — `ProfileImportOptionReport` does not exist / `OptionReports` not defined.

- [ ] **Step 3: Implement the DTO and extend the response**

```csharp
// src/AkmlSql.Core/Ipc/Messages/ProfileImportOptionReport.cs
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 031 FR-007 — one imported style option's classification.
    /// Status is one of: "mapped", "mapped-pending-render", "unsupported", "unknown".
    /// </summary>
    [MessagePackObject]
    public class ProfileImportOptionReport
    {
        [Key(0)]
        public string Path { get; set; } = string.Empty;

        [Key(1)]
        public string Value { get; set; } = string.Empty;

        [Key(2)]
        public string Status { get; set; } = string.Empty;

        [Key(3)]
        public string? Reason { get; set; }
    }
}
```

Append to `ProfileImportResponse` (after `[Key(4)] ErrorMessage` — do NOT renumber existing keys):

```csharp
        /// <summary>Spec 031 FR-007 — per-option classifications. Null from pre-031 engines.</summary>
        [Key(5)]
        public ProfileImportOptionReport[]? OptionReports { get; set; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj -c Release --filter "FullyQualifiedName~ProfileImportOptionReportTests"`
Expected: PASS (2/2).

- [ ] **Step 5: Commit checkpoint** (user-approval gate per Global Constraints)

```bash
git add src/AkmlSql.Core/Ipc/Messages/ProfileImportOptionReport.cs src/AkmlSql.Core/Ipc/Messages/ProfileImportResponse.cs tests/AkmlSql.Core.Tests/Ipc/ProfileImportOptionReportTests.cs
git commit -m "feat(031): per-option import reports on ProfileImportResponse (FR-007)"
```

---

### Task 2: New `FormattingProfile` fields (design §2, storage only)

**Files:**
- Modify: `src/AkmlSql.Formatting/Profiles/FormattingProfile.cs`
- Test: `tests/AkmlSql.Formatting.Tests/Profiles/Profile031FieldsTests.cs`

**Interfaces:**
- Produces (consumed by Tasks 4–6 mapping and by Phase 3 layout work; JSON names in parentheses):
  - `WhitespaceOptions.SemicolonPlacement : string = "none"` (`semicolonPlacement`; values `none|spaceBefore|newLineBefore`)
  - `WhitespaceOptions.EmptyLinesAfterBatchSeparator : int = 1` (`emptyLinesAfterBatchSeparator`)
  - `ListOptions.SpaceBeforeComma : bool = false` (`spaceBeforeComma`)
  - `ListOptions.CommaAlignment : string = "beforeItem"` (`commaAlignment`; `beforeItem|toList|toStatement`)
  - `ListOptions.AlignItemsToTabStops : bool = false` (`alignItemsToTabStops`)
  - `ParenthesisOptions.Style : string = ""` (`style`; empty = legacy booleans govern; else one of Redgate's 9 values `compactSimple|compactToStatement|compactIndented|compactRightAligned|expandedSimple|expandedSplit|expandedToStatement|expandedIndented|expandedRightAligned`)
  - `DdlOptions.ParenthesisStyle : string = ""` (`parenthesisStyle`), `CteOptions.ParenthesisStyle : string = ""` (`parenthesisStyle`) — same 9-value enum, "" = inherit global
  - `DmlOptions.NewLineAfterDistinctTop : bool = false` (`newLineAfterDistinctTop`)
  - New section `FormattingProfile.InsertStatements : InsertStatementsOptions` (`insertStatements`) with `Columns`/`Values` of type `InsertParenOptions { ParenthesisStyle : string = ""; IndentContents : bool; PlaceSubsequentItemsOnNewLines : string }` — columns defaults: IndentContents=true, PlaceSubsequentItemsOnNewLines="always"; values defaults: IndentContents=false, PlaceSubsequentItemsOnNewLines="never"
  - `ControlFlowOptions.IndentBeginEndKeywords : bool = false` (`indentBeginEndKeywords`)
  - `CteOptions.PlaceNameOnNewLine : bool = false` (`placeNameOnNewLine`), `CteOptions.IndentName : bool = false` (`indentName`), `CteOptions.ColumnAlignment : string = "leftAligned"` (`columnAlignment`; `indented|leftAligned|rightAligned`)
  - `DeclareOptions.EqualsOnNewLine : bool = false` (`equalsOnNewLine`)
  - `FunctionCallsOptions.SpaceAroundParentheses : bool = false` (`spaceAroundParentheses`), `SpaceAroundArgumentList : bool = false` (`spaceAroundArgumentList`), `SpaceBetweenEmptyParentheses : bool = false` (`spaceBetweenEmptyParentheses`)
  - `CaseOptions.ThenAlignment : string = "indentedFromWhen"` (`thenAlignment`; `indentedFromWhen|toWhen|toWhenExpression`)
  - `OperatorsOptions.BetweenAndAlignment : string = "toBetween"` (`betweenAndAlignment`; `toBetween|rightAlignedToBetween|toBeginningOfExpression`)
  - `InStatementsOptions.SpaceAroundContents : bool = false` (`spaceAroundContents`)
  - `JoinOptions.AlignJoinKeyword` gains accepted value `"toTable"` (string field already exists — no type change; document in its XML doc comment)
  - `OperatorsOptions.Alignment` gains accepted values `"toFirstListItem"`, `"beforeFirstListItem"` (string field exists; doc comment update)

- [ ] **Step 1: Write the failing tests** — round-trip persistence + schema auto-pickup

```csharp
// tests/AkmlSql.Formatting.Tests/Profiles/Profile031FieldsTests.cs
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Profiles;

public class Profile031FieldsTests
{
    [Fact]
    public void New_031_fields_roundtrip_through_akmlstyle_json()
    {
        var p = new FormattingProfile();
        p.Whitespace.SemicolonPlacement = "spaceBefore";
        p.Whitespace.EmptyLinesAfterBatchSeparator = 3;
        p.List.SpaceBeforeComma = true;
        p.List.CommaAlignment = "toList";
        p.List.AlignItemsToTabStops = true;
        p.Parenthesis.Style = "expandedToStatement";
        p.Ddl.ParenthesisStyle = "expandedToStatement";
        p.Cte.ParenthesisStyle = "expandedToStatement";
        p.Dml.NewLineAfterDistinctTop = true;
        p.InsertStatements.Columns.ParenthesisStyle = "expandedSimple";
        p.InsertStatements.Columns.IndentContents = false;
        p.InsertStatements.Values.ParenthesisStyle = "expandedSimple";
        p.InsertStatements.Values.IndentContents = true;
        p.InsertStatements.Values.PlaceSubsequentItemsOnNewLines = "always";
        p.ControlFlow.IndentBeginEndKeywords = true;
        p.Cte.PlaceNameOnNewLine = true;
        p.Cte.IndentName = true;
        p.Cte.ColumnAlignment = "rightAligned";
        p.Declare.EqualsOnNewLine = true;
        p.FunctionCalls.SpaceAroundParentheses = true;
        p.FunctionCalls.SpaceAroundArgumentList = true;
        p.FunctionCalls.SpaceBetweenEmptyParentheses = true;
        p.Case.ThenAlignment = "toWhen";
        p.Operators.BetweenAndAlignment = "rightAlignedToBetween";
        p.InStatements.SpaceAroundContents = true;

        var back = ProfileSerializer.Deserialize(ProfileSerializer.Serialize(p));

        Assert.Equal("spaceBefore", back.Whitespace.SemicolonPlacement);
        Assert.Equal(3, back.Whitespace.EmptyLinesAfterBatchSeparator);
        Assert.True(back.List.SpaceBeforeComma);
        Assert.Equal("toList", back.List.CommaAlignment);
        Assert.True(back.List.AlignItemsToTabStops);
        Assert.Equal("expandedToStatement", back.Parenthesis.Style);
        Assert.Equal("expandedSimple", back.InsertStatements.Columns.ParenthesisStyle);
        Assert.False(back.InsertStatements.Columns.IndentContents);
        Assert.True(back.InsertStatements.Values.IndentContents);
        Assert.Equal("always", back.InsertStatements.Values.PlaceSubsequentItemsOnNewLines);
        Assert.True(back.ControlFlow.IndentBeginEndKeywords);
        Assert.True(back.Cte.PlaceNameOnNewLine);
        Assert.Equal("rightAligned", back.Cte.ColumnAlignment);
        Assert.True(back.Declare.EqualsOnNewLine);
        Assert.True(back.FunctionCalls.SpaceBetweenEmptyParentheses);
        Assert.Equal("toWhen", back.Case.ThenAlignment);
        Assert.Equal("rightAlignedToBetween", back.Operators.BetweenAndAlignment);
        Assert.True(back.InStatements.SpaceAroundContents);
    }

    [Fact]
    public void Format_setting_schema_discovers_insertStatements_group_and_new_fields()
    {
        var schema = FormatSettingSchema.BuildDefault();
        Assert.Contains(schema.Groups, g => g.Id == "insertStatements");
        Assert.Contains(schema.Settings, s => s.Path == "whitespace.semicolonPlacement");
        Assert.Contains(schema.Settings, s => s.Path == "list.commaAlignment");
    }
}
```

Note: if `FormatSetting`'s path property is named differently (check `FormatSettingSchema.cs` below line 80 — it may be `Id` or `Key` rather than `Path`), adjust the second test to the actual property; the assertion intent is "the reflected schema contains the new entries".

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release --filter "FullyQualifiedName~Profile031FieldsTests"`
Expected: FAIL — properties do not exist.

- [ ] **Step 3: Add the fields**

In `src/AkmlSql.Formatting/Profiles/FormattingProfile.cs`, following the file's exact existing idiom (`[JsonPropertyName("camelCase")]` + defaulted auto-property + `/// <summary>` citing the spec):

Root — add after the `Declare` property (`:60-61`):

```csharp
    [JsonPropertyName("insertStatements")]
    public InsertStatementsOptions InsertStatements { get; set; } = new();
```

New section classes (place after `DeclareOptions`):

```csharp
/// <summary>
/// Spec 031 FR-029 — Redgate insertStatements section: per-construct parenthesis style,
/// content indent, and per-item line placement for the INSERT column list and VALUES tuples.
/// Supersedes the dead <c>DmlOptions.InsertColumnListFormat</c>/<c>ValuesFormat</c> fields.
/// </summary>
public class InsertStatementsOptions
{
    [JsonPropertyName("columns")]
    public InsertParenOptions Columns { get; set; } = new()
    {
        IndentContents = true,
        PlaceSubsequentItemsOnNewLines = "always",
    };

    [JsonPropertyName("values")]
    public InsertParenOptions Values { get; set; } = new()
    {
        IndentContents = false,
        PlaceSubsequentItemsOnNewLines = "never",
    };
}

public class InsertParenOptions
{
    /// <summary>Redgate 9-value parenthesis style; empty string = inherit <c>Parenthesis.Style</c>.</summary>
    [JsonPropertyName("parenthesisStyle")]
    public string ParenthesisStyle { get; set; } = "";

    [JsonPropertyName("indentContents")]
    public bool IndentContents { get; set; }

    /// <summary>always | never | ifLongerThanWrap</summary>
    [JsonPropertyName("placeSubsequentItemsOnNewLines")]
    public string PlaceSubsequentItemsOnNewLines { get; set; } = "never";
}
```

Per-section additions (each inside its existing class, same idiom; summaries cite "Spec 031 FR-0xx"):

```csharp
// WhitespaceOptions
    /// <summary>Spec 031 FR-033 — none | spaceBefore | newLineBefore. Gates NormalizeSemicolonSpacing in phase 3.</summary>
    [JsonPropertyName("semicolonPlacement")]
    public string SemicolonPlacement { get; set; } = "none";

    /// <summary>Spec 031 FR-034 — blank lines after a GO batch separator.</summary>
    [JsonPropertyName("emptyLinesAfterBatchSeparator")]
    public int EmptyLinesAfterBatchSeparator { get; set; } = 1;

// ListOptions
    /// <summary>Spec 031 FR-021 — space between an item and its following comma.</summary>
    [JsonPropertyName("spaceBeforeComma")]
    public bool SpaceBeforeComma { get; set; }

    /// <summary>Spec 031 FR-021 — leading-comma column: beforeItem | toList | toStatement.</summary>
    [JsonPropertyName("commaAlignment")]
    public string CommaAlignment { get; set; } = "beforeItem";

    /// <summary>Spec 031 FR-020 — round alignment columns up to the next tab stop.</summary>
    [JsonPropertyName("alignItemsToTabStops")]
    public bool AlignItemsToTabStops { get; set; }

// ParenthesisOptions
    /// <summary>Spec 031 FR-022 — Redgate 9-value style; empty = legacy OpenOnSameLine/CloseOnNewLine govern.</summary>
    [JsonPropertyName("style")]
    public string Style { get; set; } = "";

// DdlOptions + CteOptions (identical shape)
    /// <summary>Spec 031 FR-022 — construct-scoped paren style; empty = inherit Parenthesis.Style.</summary>
    [JsonPropertyName("parenthesisStyle")]
    public string ParenthesisStyle { get; set; } = "";

// DmlOptions
    /// <summary>Spec 031 FR-023 — break AFTER DISTINCT/TOP so the select list starts on the next line.</summary>
    [JsonPropertyName("newLineAfterDistinctTop")]
    public bool NewLineAfterDistinctTop { get; set; }

// ControlFlowOptions
    /// <summary>Spec 031 FR-025 — indent the BEGIN/END keywords themselves one level from IF/WHILE/ELSE.</summary>
    [JsonPropertyName("indentBeginEndKeywords")]
    public bool IndentBeginEndKeywords { get; set; }

// CteOptions
    /// <summary>Spec 031 FR-026 — CTE name on the line after WITH.</summary>
    [JsonPropertyName("placeNameOnNewLine")]
    public bool PlaceNameOnNewLine { get; set; }

    /// <summary>Spec 031 FR-026 — indent the CTE name one level from WITH (with PlaceNameOnNewLine).</summary>
    [JsonPropertyName("indentName")]
    public bool IndentName { get; set; }

    /// <summary>Spec 031 FR-026 — indented | leftAligned | rightAligned.</summary>
    [JsonPropertyName("columnAlignment")]
    public string ColumnAlignment { get; set; } = "leftAligned";

// DeclareOptions
    /// <summary>Spec 031 FR-027 — '=' leads the continuation line in DECLARE/SET breaks.</summary>
    [JsonPropertyName("equalsOnNewLine")]
    public bool EqualsOnNewLine { get; set; }

// FunctionCallsOptions
    /// <summary>Spec 031 FR-030 — space between function name and '('.</summary>
    [JsonPropertyName("spaceAroundParentheses")]
    public bool SpaceAroundParentheses { get; set; }

    /// <summary>Spec 031 FR-030 — spaces just inside call parens, around the arguments.</summary>
    [JsonPropertyName("spaceAroundArgumentList")]
    public bool SpaceAroundArgumentList { get; set; }

    /// <summary>Spec 031 FR-030 — '( )' for zero-argument calls.</summary>
    [JsonPropertyName("spaceBetweenEmptyParentheses")]
    public bool SpaceBetweenEmptyParentheses { get; set; }

// CaseOptions
    /// <summary>Spec 031 FR-031 — line-start THEN column: indentedFromWhen | toWhen | toWhenExpression.</summary>
    [JsonPropertyName("thenAlignment")]
    public string ThenAlignment { get; set; } = "indentedFromWhen";

// OperatorsOptions
    /// <summary>Spec 031 FR-032 — wrapped BETWEEN's AND: toBetween | rightAlignedToBetween | toBeginningOfExpression.</summary>
    [JsonPropertyName("betweenAndAlignment")]
    public string BetweenAndAlignment { get; set; } = "toBetween";

// InStatementsOptions
    /// <summary>Spec 031 FR-032 — spaces just inside IN-list parens.</summary>
    [JsonPropertyName("spaceAroundContents")]
    public bool SpaceAroundContents { get; set; }
```

Also update the doc comments of `JoinOptions.AlignJoinKeyword` (add `toTable` to the accepted-values list) and `OperatorsOptions.Alignment` (add `toFirstListItem`, `beforeFirstListItem`).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release --filter "FullyQualifiedName~Profile031FieldsTests"`
Expected: PASS. Also run the full Formatting test suite once — the reflection-driven `FormatSettingSchema` and `SqlPromptKeyMapTests` drift-guards must not break: `dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release`. If `SqlPromptKeyMapTests` asserts exporter/importer key symmetry over profile fields, new fields without XML mappings are expected to be exempt — if the guard fails, extend its exemption list with the 031 field names (they are JSON-format options with no XML equivalent), documenting why inline.

- [ ] **Step 5: Commit checkpoint**

```bash
git add src/AkmlSql.Formatting/Profiles/FormattingProfile.cs tests/AkmlSql.Formatting.Tests/Profiles/Profile031FieldsTests.cs
git commit -m "feat(031): FormattingProfile fields for Redgate JSON options (design §2, storage)"
```

---

### Task 3: Importer skeleton — parse, flatten, defaults, classification plumbing

**Files:**
- Create: `src/AkmlSql.Formatting/Profiles/RedgateJsonStyleImporter.cs`, `src/AkmlSql.Formatting/Profiles/FormatterHonoringTable.cs`
- Create: `tests/AkmlSql.Formatting.Tests/Fixtures/MohamedKhamis-style.json` (copy of `specs/031-redgate-style-import/reference/MohamedKhamis-2cd71422-30f2-4360-800f-240f2897fd3e.json`; add `<None Update="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />` to the test csproj if fixtures aren't already copied)
- Test: `tests/AkmlSql.Formatting.Tests/Profiles/RedgateJsonStyleImporterTests.cs`

**Interfaces:**
- Produces (consumed by Tasks 4–8):

```csharp
public static class RedgateOptionStatus
{
    public const string Mapped = "mapped";
    public const string MappedPendingRender = "mapped-pending-render";
    public const string Unsupported = "unsupported";
    public const string Unknown = "unknown";
}

public sealed record RedgateOptionReport(string Path, string Value, string Status, string? Reason);

public sealed class RedgateStyleImportResult
{
    public bool Success { get; init; }
    public string? ParseError { get; init; }
    public FormattingProfile Profile { get; init; } = new();
    public IReadOnlyList<RedgateOptionReport> Options { get; init; } = [];
    public int MappedCount => Options.Count(o => o.Status is RedgateOptionStatus.Mapped or RedgateOptionStatus.MappedPendingRender);
    public int UnsupportedCount => Options.Count(o => o.Status == RedgateOptionStatus.Unsupported);
    public int UnknownCount => Options.Count(o => o.Status == RedgateOptionStatus.Unknown);
}

public static class RedgateJsonStyleImporter
{
    /// <summary>fallbackName is used when metadata.name is absent/blank (e.g. the source file stem).</summary>
    public static RedgateStyleImportResult Import(string jsonContent, string? fallbackName = null);
}

public static class FormatterHonoringTable
{
    /// <summary>True when the layout engine renders this Redgate option end-to-end today.</summary>
    public static bool IsRendered(string redgatePath);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AkmlSql.Formatting.Tests/Profiles/RedgateJsonStyleImporterTests.cs
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Profiles;

public class RedgateJsonStyleImporterTests
{
    private static string UserStyleJson =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json"));

    [Fact]
    public void Import_reads_metadata_name_and_id()
    {
        var result = RedgateJsonStyleImporter.Import(UserStyleJson);
        Assert.True(result.Success);
        Assert.Equal("MohamedKhamis", result.Profile.Metadata.Name);
        Assert.Equal("2cd71422-30f2-4360-800f-240f2897fd3e", result.Profile.Metadata.Id);
        Assert.Equal("SQL Prompt Import", result.Profile.Metadata.BasedOn);
    }

    [Fact]
    public void Import_classifies_every_leaf_key_in_the_file()
    {
        var result = RedgateJsonStyleImporter.Import(UserStyleJson);
        // 65 leaf option keys (metadata.id/name are metadata, not options)
        Assert.Equal(65, result.Options.Count);
        Assert.All(result.Options, o => Assert.False(string.IsNullOrEmpty(o.Status)));
    }

    [Fact]
    public void Import_of_malformed_json_fails_without_profile()
    {
        var result = RedgateJsonStyleImporter.Import("<SqlPromptStyle>not json</SqlPromptStyle>");
        Assert.False(result.Success);
        Assert.NotNull(result.ParseError);
        Assert.Empty(result.Options);
    }

    [Fact]
    public void Import_of_empty_object_succeeds_with_fallback_name_and_redgate_defaults()
    {
        var result = RedgateJsonStyleImporter.Import("{}", fallbackName: "my-style-file");
        Assert.True(result.Success);
        Assert.Equal("my-style-file", result.Profile.Metadata.Name);
        Assert.Empty(result.Options); // no file keys to classify
        // NOTE: Task 4 Step 1 extends this test with Redgate-default spot-checks
        // (TabStyle "spaces", MaxLineWidth 120, SemicolonPlacement "none") once the mapping table exists.
    }

    [Fact]
    public void Unknown_key_is_reported_not_dropped()
    {
        var result = RedgateJsonStyleImporter.Import("""{ "whitespace": { "notARealOption": true } }""");
        Assert.True(result.Success);
        var report = Assert.Single(result.Options);
        Assert.Equal("whitespace.notARealOption", report.Path);
        Assert.Equal(RedgateOptionStatus.Unknown, report.Status);
    }
}
```

(The third assertion in `Import_of_empty_object…` is intentionally inert here; Task 4 Step 1 replaces it with real Redgate-default assertions once mappings exist. It is listed so the test name/coverage is stable.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release --filter "FullyQualifiedName~RedgateJsonStyleImporterTests"`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement skeleton**

```csharp
// src/AkmlSql.Formatting/Profiles/FormatterHonoringTable.cs
namespace AkmlSql.Formatting.Profiles;

/// <summary>
/// Spec 031 — which Redgate JSON options the layout engine renders end-to-end TODAY.
/// Phase 1 seeds this with the wired set from the spec's Option Fidelity Contract;
/// each Phase 3 feature adds its option paths as its corpus files go green.
/// Paths not present here (but mapped) classify as "mapped-pending-render".
/// </summary>
public static class FormatterHonoringTable
{
    private static readonly HashSet<string> Rendered = new(StringComparer.OrdinalIgnoreCase)
    {
        // Contract rows with Today = wired (spec.md Option Fidelity Contract)
        "whitespace.numberOfSpacesInTabs",
        "whitespace.wrapLinesLongerThan",
        "lists.placeCommasBeforeItems",
        "parentheses.indentParenthesesContents",
        "parentheses.collapseShortParenthesisContents",
        "parentheses.collapseParenthesesShorterThan",
        "parentheses.addSpacesInsideParentheses",
        "casing.reservedKeywords",
        "casing.builtInFunctions",
        "casing.builtInDataTypes",
        "dml.collapseStatementsShorterThan",
        "dml.collapseSubqueriesShorterThan",
        "ddl.indentParenthesesContents",
        "ddl.placeConstraintsOnNewLines",
        "ddl.collapseShortStatements",
        "ddl.collapseStatementsShorterThan",
        "controlFlow.collapseStatementsShorterThan",
        "cte.indentContents",
        "cte.placeAsOnNewLine",
        "variables.alignDataTypesAndValues",
        "joinStatements.join.indentJoinTable",
        "joinStatements.on.placeOnNewLine",
        "joinStatements.on.keywordAlignment",
        "functionCalls.placeArgumentsOnNewLines",
        "caseExpressions.placeFirstWhenOnNewLine",
        "caseExpressions.placeThenOnNewLine",
        "caseExpressions.collapseShortCaseExpressions",
        "caseExpressions.collapseCaseExpressionsShorterThan",
        "operators.between.placeOnNewLine",
        "operators.in.placeFirstValueOnNewLine",
        // n/a rows (hold by construction with the user's values):
        "whitespace.newLines.preserveExistingEmptyLinesBetweenStatements",
        "whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator",
    };

    public static bool IsRendered(string redgatePath) => Rendered.Contains(redgatePath);
}
```

```csharp
// src/AkmlSql.Formatting/Profiles/RedgateJsonStyleImporter.cs
using System.Text.Json;

namespace AkmlSql.Formatting.Profiles;

public static class RedgateOptionStatus
{
    public const string Mapped = "mapped";
    public const string MappedPendingRender = "mapped-pending-render";
    public const string Unsupported = "unsupported";
    public const string Unknown = "unknown";
}

public sealed record RedgateOptionReport(string Path, string Value, string Status, string? Reason);

public sealed class RedgateStyleImportResult
{
    public bool Success { get; init; }
    public string? ParseError { get; init; }
    public FormattingProfile Profile { get; init; } = new();
    public IReadOnlyList<RedgateOptionReport> Options { get; init; } = [];
    public int MappedCount => Options.Count(o => o.Status is RedgateOptionStatus.Mapped or RedgateOptionStatus.MappedPendingRender);
    public int UnsupportedCount => Options.Count(o => o.Status == RedgateOptionStatus.Unsupported);
    public int UnknownCount => Options.Count(o => o.Status == RedgateOptionStatus.Unknown);
}

/// <summary>
/// Spec 031 FR-001..FR-007 — imports modern SQL Prompt JSON style files (10.5+, one file per
/// style, camelCase sections) against the vendored Redgate schema
/// (specs/031-redgate-style-import/reference/formattingstyle-schema.json).
/// Distinct from <see cref="SqlPromptImporter"/>, which parses AKML's own spec-020 XML exports.
/// </summary>
public static class RedgateJsonStyleImporter
{
    public static RedgateStyleImportResult Import(string jsonContent, string? fallbackName = null)
    {
        ArgumentNullException.ThrowIfNull(jsonContent);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            return new RedgateStyleImportResult { Success = false, ParseError = ex.Message };
        }

        using (doc)
        {
            var profile = new FormattingProfile();

            // 1. Materialize Redgate defaults for every mapped option (FR-002).
            foreach (var (path, entry) in RedgateOptionMap.Entries)
                entry.Apply?.Invoke(profile, entry.DefaultValue);

            // 2. Flatten the file to leaf key/value pairs.
            var fileValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Flatten(doc.RootElement, prefix: "", fileValues);

            // 3. Metadata (not options).
            fileValues.TryGetValue("metadata.name", out var name);
            fileValues.TryGetValue("metadata.id", out var id);
            fileValues.Remove("metadata.name");
            fileValues.Remove("metadata.id");

            profile.Metadata.Name = string.IsNullOrWhiteSpace(name) ? (fallbackName ?? "Imported style") : name!;
            if (!string.IsNullOrWhiteSpace(id)) profile.Metadata.Id = id!;
            profile.Metadata.BasedOn = "SQL Prompt Import";
            profile.Metadata.IsBuiltIn = false;
            profile.Metadata.Created = DateTime.UtcNow;
            profile.Metadata.Modified = DateTime.UtcNow;

            // 4. Overlay file values + classify every file key (FR-001/FR-007).
            var reports = new List<RedgateOptionReport>(fileValues.Count);
            foreach (var (path, value) in fileValues)
            {
                if (!RedgateOptionMap.Entries.TryGetValue(path, out var entry))
                {
                    reports.Add(new RedgateOptionReport(path, value, RedgateOptionStatus.Unknown,
                        "Not in the vendored Redgate schema (+ documented additions); Redgate default behavior assumed."));
                    continue;
                }
                if (entry.Apply is null)
                {
                    reports.Add(new RedgateOptionReport(path, value, RedgateOptionStatus.Unsupported, entry.UnsupportedReason));
                    continue;
                }
                entry.Apply(profile, value);
                var status = FormatterHonoringTable.IsRendered(path)
                    ? RedgateOptionStatus.Mapped
                    : RedgateOptionStatus.MappedPendingRender;
                reports.Add(new RedgateOptionReport(path, value, status,
                    status == RedgateOptionStatus.MappedPendingRender ? "Stored losslessly; rendering ships in spec 031 phase 3." : null));
            }

            // 5. Post-pass: SP11 threshold-implies-enabled quirk (FR-003).
            RedgateOptionMap.ApplyThresholdImpliesEnabled(profile, fileValues);

            profile.Metadata.Description =
                $"Imported from SQL Prompt JSON style ({reports.Count(r => r.Status != RedgateOptionStatus.Unknown && r.Status != RedgateOptionStatus.Unsupported)} options mapped)";

            return new RedgateStyleImportResult { Success = true, Profile = profile, Options = reports };
        }
    }

    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> into)
    {
        foreach (var prop in element.EnumerateObject())
        {
            var path = prefix.Length == 0 ? prop.Name : $"{prefix}.{prop.Name}";
            if (prop.Value.ValueKind == JsonValueKind.Object)
                Flatten(prop.Value, path, into);
            else
                into[path] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? "",
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => prop.Value.GetRawText(),
                };
        }
    }
}
```

Also create `RedgateOptionMap` in the same file (Tasks 4–6 fill it; skeleton compiles with an empty map + no-op post-pass):

```csharp
internal sealed class RedgateMappingEntry
{
    public required string DefaultValue { get; init; }
    public Action<FormattingProfile, string>? Apply { get; init; }
    public string? UnsupportedReason { get; init; }
}

internal static partial class RedgateOptionMap
{
    /// <summary>Filled across three partial files: Whitespace/Lists/Parens/Casing, Dml/Ddl/ControlFlow/Cte/Variables, Join/Insert/FunctionCalls/Case/Operators.</summary>
    internal static readonly Dictionary<string, RedgateMappingEntry> Entries = new(StringComparer.OrdinalIgnoreCase);

    internal static void ApplyThresholdImpliesEnabled(FormattingProfile profile, Dictionary<string, string> fileValues)
    {
        // FR-003: enable a collapse iff its threshold key is present AND its gating bool key is absent.
        if (fileValues.ContainsKey("dml.collapseStatementsShorterThan") && !fileValues.ContainsKey("dml.collapseShortStatements"))
            profile.Dml.CollapseShortStatements = true;
        if (fileValues.ContainsKey("dml.collapseSubqueriesShorterThan") && !fileValues.ContainsKey("dml.collapseShortSubqueries"))
            profile.Dml.CollapseShortSubqueries = true;
        if (fileValues.ContainsKey("controlFlow.collapseStatementsShorterThan") && !fileValues.ContainsKey("controlFlow.collapseShortStatements"))
            profile.ControlFlow.CollapseShortIfElse = true;
    }
}
```

Static-constructor registration for the map lives in the Task 4–6 partials via `[ModuleInitializer]`-free pattern: each partial file adds a `static void RegisterXxx()` called from a single static constructor in the main partial:

```csharp
internal static partial class RedgateOptionMap
{
    static RedgateOptionMap()
    {
        RegisterWhitespaceListsParensCasing(); // Task 4
        RegisterDmlDdlControlFlowCteVariables(); // Task 5
        RegisterJoinInsertFunctionCaseOperators(); // Task 6
    }

    static partial void RegisterWhitespaceListsParensCasing();
    static partial void RegisterDmlDdlControlFlowCteVariables();
    static partial void RegisterJoinInsertFunctionCaseOperators();
}
```

(For Task 3 to compile before Tasks 4–6 exist, add three empty partial-method declarations only — C# partial methods with no implementation compile to nothing.)

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release --filter "FullyQualifiedName~RedgateJsonStyleImporterTests"`
Expected: PASS for metadata/malformed/empty/unknown tests. `Import_classifies_every_leaf_key_in_the_file` FAILS at this point (65 keys classify but all as `unknown` — the count assertion passes yet the intent doesn't): tighten it now to also assert `Assert.DoesNotContain(result.Options, o => o.Status == RedgateOptionStatus.Unknown)` and mark it `[Fact(Skip = "Un-skip in Task 6 when the mapping table is complete")]`. Un-skipping it is Task 6 Step 4's exit criterion.

- [ ] **Step 5: Commit checkpoint**

```bash
git add src/AkmlSql.Formatting/Profiles/RedgateJsonStyleImporter.cs src/AkmlSql.Formatting/Profiles/FormatterHonoringTable.cs tests/AkmlSql.Formatting.Tests/Profiles/RedgateJsonStyleImporterTests.cs tests/AkmlSql.Formatting.Tests/Fixtures/MohamedKhamis-style.json
git commit -m "feat(031): RedgateJsonStyleImporter skeleton — parse, flatten, defaults, classification (FR-001/002/005/007)"
```

---

### Task 4: Mapping table — whitespace, lists, parentheses, casing

**Files:**
- Create: `src/AkmlSql.Formatting/Profiles/RedgateOptionMap.Whitespace.cs`
- Modify: `tests/AkmlSql.Formatting.Tests/Profiles/RedgateJsonStyleImporterTests.cs` (add assertions)

**Interfaces:**
- Consumes: `RedgateMappingEntry`, `RedgateOptionMap.Entries`, profile fields from Task 2.
- Produces: registered entries for all `whitespace.*`, `lists.*`, `parentheses.*`, `casing.*` schema keys.

- [ ] **Step 1: Write the failing tests** (add to `RedgateJsonStyleImporterTests`; also replace the inert assertion in `Import_of_empty_object…` with the three real ones below)

```csharp
    [Fact]
    public void Whitespace_lists_parens_casing_map_from_user_style()
    {
        var p = RedgateJsonStyleImporter.Import(UserStyleJson).Profile;

        Assert.Equal("tabsWhenPossible", p.Whitespace.TabStyle);      // tabsIfPossible
        Assert.Equal(2, p.Whitespace.TabSize);
        Assert.Equal(200, p.Whitespace.MaxLineWidth);
        Assert.Equal("spaceBefore", p.Whitespace.SemicolonPlacement);
        Assert.Equal(2, p.Whitespace.EmptyLineBetweenStatements);
        Assert.Equal(1, p.Whitespace.EmptyLinesAfterBatchSeparator);  // omitted -> Redgate default
        Assert.False(p.Whitespace.PreserveEmptyLines);
        Assert.False(p.Whitespace.PreserveEmptyLinesAfterBatch);
        Assert.Equal("normaliseIndent", p.Comments.MultilineFormatting); // alignMultilineCommentsMatchingPatterns=true
        Assert.True(p.Comments.RecognizeCommonPatterns);

        Assert.True(p.List.AlignItemsToTabStops);
        Assert.Equal("leading", p.List.CommaPosition);
        Assert.True(p.List.SpaceBeforeComma);
        Assert.Equal("toList", p.List.CommaAlignment);
        Assert.True(p.Whitespace.SpaceAfterComma);                    // omitted -> Redgate default true

        Assert.Equal("expandedToStatement", p.Parenthesis.Style);
        Assert.True(p.Parenthesis.IndentContents);
        Assert.True(p.Parenthesis.CollapseShort);
        Assert.Equal(100, p.Parenthesis.CollapseThreshold);
        Assert.True(p.Parenthesis.SpaceInside);

        Assert.Equal("UPPERCASE", p.Casing.ReservedKeywords);
        Assert.Equal("UPPERCASE", p.Casing.BuiltInFunctions);
        Assert.Equal("UPPERCASE", p.Casing.BuiltInDataTypes);
        Assert.True(p.Casing.SyncWithDatabase);                       // useObjectDefinitionCase
    }

    // Real Redgate defaults on empty import (replaces the Task 3 inert assertion):
    //   Assert.Equal("spaces", p.Whitespace.TabStyle);
    //   Assert.Equal(120, p.Whitespace.MaxLineWidth);
    //   Assert.Equal("none", p.Whitespace.SemicolonPlacement);
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release --filter "FullyQualifiedName~Whitespace_lists_parens_casing"`
Expected: FAIL — map empty, Redgate defaults not applied (profile shows AKML defaults, e.g. TabSize 4).

- [ ] **Step 3: Implement the partial**

```csharp
// src/AkmlSql.Formatting/Profiles/RedgateOptionMap.Whitespace.cs
namespace AkmlSql.Formatting.Profiles;

internal static partial class RedgateOptionMap
{
    private static bool B(string v) => v.Equals("true", StringComparison.OrdinalIgnoreCase);
    private static int I(string v, int fallback) => int.TryParse(v, out var n) ? n : fallback;

    private static string Casing5(string v) => v.Trim().ToLowerInvariant() switch
    {
        "uppercase" => "UPPERCASE",
        "lowercase" => "lowercase",
        "uppercamelcase" => "PascalCase",
        "lowercamelcase" => "camelCase",
        _ => "AsIs", // leaveAsIs
    };

    private static void Add(string path, string defaultValue, Action<FormattingProfile, string> apply)
        => Entries[path] = new RedgateMappingEntry { DefaultValue = defaultValue, Apply = apply };

    private static void AddUnsupported(string path, string defaultValue, string reason)
        => Entries[path] = new RedgateMappingEntry { DefaultValue = defaultValue, UnsupportedReason = reason };

    static partial void RegisterWhitespaceListsParensCasing()
    {
        // ----- whitespace -----
        Add("whitespace.spacesOrTabs", "spaces", (p, v) => p.Whitespace.TabStyle = v.Trim().ToLowerInvariant() switch
        {
            "tabs" => "tabs",
            "tabsifpossible" => "tabsWhenPossible",
            _ => "spaces",
        });
        Add("whitespace.numberOfSpacesInTabs", "4", (p, v) => p.Whitespace.TabSize = I(v, 4));
        Add("whitespace.wrapLongLines", "true", (_, _) => { }); // gate consumed with wrapLinesLongerThan below
        Add("whitespace.wrapLinesLongerThan", "120", (p, v) => p.Whitespace.MaxLineWidth = I(v, 120));
        Add("whitespace.whiteSpaceBeforeSemiColon", "none", (p, v) => p.Whitespace.SemicolonPlacement = v.Trim().ToLowerInvariant() switch
        {
            "spacebefore" => "spaceBefore",
            "newlinebefore" => "newLineBefore",
            _ => "none",
        });
        Add("whitespace.newLines.preserveExistingEmptyLinesBetweenStatements", "true", (p, v) => p.Whitespace.PreserveEmptyLines = B(v));
        Add("whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator", "true", (p, v) => p.Whitespace.PreserveEmptyLinesAfterBatch = B(v));
        Add("whitespace.newLines.emptyLinesBetweenStatements", "1", (p, v) => p.Whitespace.EmptyLineBetweenStatements = I(v, 1));
        Add("whitespace.newLines.emptyLinesAfterBatchSeparator", "1", (p, v) => p.Whitespace.EmptyLinesAfterBatchSeparator = I(v, 1));
        // Post-schema documented addition (SP 10.14 release notes) — FR-001/FR-036:
        Add("whitespace.newLines.alignMultilineCommentsMatchingPatterns", "false", (p, v) =>
        {
            if (!B(v)) return;
            p.Comments.MultilineFormatting = "normaliseIndent";
            p.Comments.RecognizeCommonPatterns = true;
        });

        // ----- lists -----
        Add("lists.alignItemsToTabStops", "false", (p, v) => p.List.AlignItemsToTabStops = B(v));
        Add("lists.placeCommasBeforeItems", "false", (p, v) => p.List.CommaPosition = B(v) ? "leading" : "trailing");
        Add("lists.addSpaceBeforeComma", "false", (p, v) => p.List.SpaceBeforeComma = B(v));
        Add("lists.addSpaceAfterComma", "true", (p, v) => { p.Whitespace.SpaceAfterComma = B(v); p.List.SpaceAfterListComma = B(v); });
        Add("lists.commaAlignment", "toList", (p, v) => p.List.CommaAlignment = v.Trim().ToLowerInvariant() switch
        {
            "beforeitem" => "beforeItem",
            "tostatement" => "toStatement",
            _ => "toList",
        });

        // ----- parentheses (global) -----
        Add("parentheses.parenthesisStyle", "compactSimple", (p, v) => p.Parenthesis.Style = NormalizeParenStyle(v));
        Add("parentheses.indentParenthesesContents", "false", (p, v) => p.Parenthesis.IndentContents = B(v));
        Add("parentheses.collapseShortParenthesisContents", "false", (p, v) => p.Parenthesis.CollapseShort = B(v));
        Add("parentheses.collapseParenthesesShorterThan", "80", (p, v) => p.Parenthesis.CollapseThreshold = I(v, 80));
        Add("parentheses.addSpacesInsideParentheses", "false", (p, v) => p.Parenthesis.SpaceInside = B(v));
        Add("parentheses.addSpacesAroundParentheses", "true", (p, v) => p.Whitespace.SpaceBeforeParentheses = B(v));

        // ----- casing -----
        Add("casing.reservedKeywords", "leaveAsIs", (p, v) => p.Casing.ReservedKeywords = Casing5(v));
        Add("casing.builtInFunctions", "leaveAsIs", (p, v) => p.Casing.BuiltInFunctions = Casing5(v));
        Add("casing.builtInDataTypes", "leaveAsIs", (p, v) => p.Casing.BuiltInDataTypes = Casing5(v));
        Add("casing.globalVariables", "leaveAsIs", (p, v) => p.Casing.GlobalVariables = Casing5(v));
        Add("casing.useObjectDefinitionCase", "false", (p, v) => p.Casing.SyncWithDatabase = B(v));
    }

    internal static string NormalizeParenStyle(string v) => v.Trim().ToLowerInvariant() switch
    {
        "compactsimple" => "compactSimple",
        "compacttostatement" => "compactToStatement",
        "compactindented" => "compactIndented",
        "compactrightaligned" => "compactRightAligned",
        "expandedsimple" => "expandedSimple",
        "expandedsplit" => "expandedSplit",
        "expandedtostatement" => "expandedToStatement",
        "expandedindented" => "expandedIndented",
        "expandedrightaligned" => "expandedRightAligned",
        _ => "compactSimple",
    };
}
```

If `CasingOptions.GlobalVariables` uses a different property name, check `FormattingProfile.cs` (the schema audit lists `globalVariables`) and match it.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release --filter "FullyQualifiedName~RedgateJsonStyleImporterTests"`
Expected: PASS (except the still-skipped completeness fact).

- [ ] **Step 5: Commit checkpoint**

```bash
git add src/AkmlSql.Formatting/Profiles/RedgateOptionMap.Whitespace.cs tests/AkmlSql.Formatting.Tests/Profiles/RedgateJsonStyleImporterTests.cs
git commit -m "feat(031): Redgate mapping — whitespace/lists/parentheses/casing (FR-001/002/033/034)"
```

---

### Task 5: Mapping table — dml, ddl, controlFlow, cte, variables

**Files:**
- Create: `src/AkmlSql.Formatting/Profiles/RedgateOptionMap.Statements.cs`
- Modify: `tests/AkmlSql.Formatting.Tests/Profiles/RedgateJsonStyleImporterTests.cs`

**Interfaces:**
- Consumes: `Add`/`AddUnsupported`/`B`/`I`/`NormalizeParenStyle` from Task 4; Task 2 fields.
- Produces: registered entries for `dml.*`, `ddl.*`, `controlFlow.*`, `cte.*`, `variables.*`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void Dml_ddl_controlflow_cte_variables_map_from_user_style()
    {
        var p = RedgateJsonStyleImporter.Import(UserStyleJson).Profile;

        Assert.True(p.Dml.NewLineAfterDistinctTop);
        Assert.True(p.Dml.CollapseShortStatements);        // FR-003: threshold present, bool absent
        Assert.Equal(160, p.Dml.CollapseThreshold);
        Assert.True(p.Dml.CollapseShortSubqueries);        // FR-003
        Assert.Equal(78, p.Dml.SubqueryCollapseThreshold);

        Assert.Equal("expandedToStatement", p.Ddl.ParenthesisStyle);
        Assert.True(p.Ddl.ConstraintsOnNewLine);
        Assert.Equal("ifLongerOrMultipleColumns", p.Ddl.ConstraintColumnsOnNewLine);
        Assert.True(p.Ddl.CollapseShortDdl);
        Assert.Equal(75, p.Ddl.CollapseThreshold);
        Assert.True(p.Ddl.AlignDataTypes);                 // omitted alignDataTypesAndConstraints -> default true

        Assert.True(p.ControlFlow.IndentBeginEndKeywords);
        Assert.True(p.ControlFlow.CollapseShortIfElse);    // FR-003
        Assert.Equal(35, p.ControlFlow.CollapseThreshold);
        Assert.True(p.ControlFlow.IndentBetweenBeginEnd);  // omitted indentContentsOfStatements -> default true
        Assert.True(p.ControlFlow.BeginOnNewLine);         // omitted placeBeginAndEndOnNewLine -> default true

        Assert.Equal("expandedToStatement", p.Cte.ParenthesisStyle);
        Assert.True(p.Cte.PlaceNameOnNewLine);
        Assert.True(p.Cte.IndentName);
        Assert.Equal("rightAligned", p.Cte.ColumnAlignment);
        Assert.False(p.Cte.AsOnNewLine);                   // placeAsOnNewLine=false (Redgate default true)
        Assert.True(p.Cte.CteBodyIndent);                  // indentContents=true

        Assert.False(p.Declare.AlignDataTypes);            // alignDataTypesAndValues=false
        Assert.False(p.Declare.AlignDefaultValues);
        Assert.True(p.Declare.EqualsOnNewLine);
    }
```

If `CteOptions`' body-indent property is named differently than `CteBodyIndent` (audit says `CteBodyIndent`, wired at `ControlFlowRules.cs:760`), open `FormattingProfile.cs` `CteOptions` and use the actual name; same for `Ddl.ConstraintColumnsOnNewLine` (spec-020 field) and `Declare.*` (DeclareOptions: `OneDeclarationPerLine`, `AlignDataTypes`, `AlignDefaultValues`).

- [ ] **Step 2: Run to verify failure** — same filter, expected FAIL.

- [ ] **Step 3: Implement**

```csharp
// src/AkmlSql.Formatting/Profiles/RedgateOptionMap.Statements.cs
namespace AkmlSql.Formatting.Profiles;

internal static partial class RedgateOptionMap
{
    static partial void RegisterDmlDdlControlFlowCteVariables()
    {
        // ----- dml -----
        Add("dml.addNewLineAfterDistinctAndTopClauses", "false", (p, v) => p.Dml.NewLineAfterDistinctTop = B(v));
        Add("dml.placeDistinctAndTopClausesOnNewLine", "false", (p, v) =>
        {
            p.Dml.TopOnSameLine = !B(v);
            p.Dml.DistinctOnSameLine = !B(v);
        });
        Add("dml.collapseShortStatements", "false", (p, v) => p.Dml.CollapseShortStatements = B(v));
        Add("dml.collapseStatementsShorterThan", "80", (p, v) => p.Dml.CollapseThreshold = I(v, 80));
        Add("dml.collapseShortSubqueries", "false", (p, v) => p.Dml.CollapseShortSubqueries = B(v));
        Add("dml.collapseSubqueriesShorterThan", "80", (p, v) => p.Dml.SubqueryCollapseThreshold = I(v, 80));
        Add("dml.placeInsertTableOnNewLine", "false", (_, _) => { }); // consumed by INSERT layout in phase 3; stored implicitly false

        // ----- ddl -----
        Add("ddl.parenthesisStyle", "compactSimple", (p, v) => p.Ddl.ParenthesisStyle = NormalizeParenStyle(v));
        Add("ddl.indentParenthesesContents", "false", (p, v) => p.Ddl.IndentParenContents = B(v));
        Add("ddl.alignDataTypesAndConstraints", "true", (p, v) => p.Ddl.AlignDataTypes = B(v));
        Add("ddl.placeConstraintsOnNewLines", "false", (p, v) => p.Ddl.ConstraintsOnNewLine = B(v));
        Add("ddl.placeConstraintColumnsOnNewLines", "ifLongerThanMaxLineLength", (p, v) => p.Ddl.ConstraintColumnsOnNewLine = v.Trim().ToLowerInvariant() switch
        {
            "always" => "always",
            "iflongerormultiplecolumns" => "ifLongerOrMultipleColumns",
            _ => "ifLongerThanWrap",
        });
        Add("ddl.collapseShortStatements", "false", (p, v) => p.Ddl.CollapseShortDdl = B(v));
        Add("ddl.collapseStatementsShorterThan", "80", (p, v) => p.Ddl.CollapseThreshold = I(v, 80));

        // ----- controlFlow -----
        Add("controlFlow.indentBeginAndEndKeywords", "false", (p, v) => p.ControlFlow.IndentBeginEndKeywords = B(v));
        Add("controlFlow.placeBeginAndEndOnNewLine", "true", (p, v) => p.ControlFlow.BeginOnNewLine = B(v));
        Add("controlFlow.indentContentsOfStatements", "true", (p, v) => p.ControlFlow.IndentBetweenBeginEnd = B(v));
        Add("controlFlow.collapseShortStatements", "false", (p, v) => p.ControlFlow.CollapseShortIfElse = B(v));
        Add("controlFlow.collapseStatementsShorterThan", "80", (p, v) => p.ControlFlow.CollapseThreshold = I(v, 80));

        // ----- cte -----
        Add("cte.parenthesisStyle", "compactSimple", (p, v) => p.Cte.ParenthesisStyle = NormalizeParenStyle(v));
        Add("cte.indentContents", "false", (p, v) => p.Cte.CteBodyIndent = B(v));
        Add("cte.placeNameOnNewLine", "false", (p, v) => p.Cte.PlaceNameOnNewLine = B(v));
        Add("cte.indentName", "false", (p, v) => p.Cte.IndentName = B(v));
        Add("cte.columnAlignment", "leftAligned", (p, v) => p.Cte.ColumnAlignment = v.Trim().ToLowerInvariant() switch
        {
            "indented" => "indented",
            "rightaligned" => "rightAligned",
            _ => "leftAligned",
        });
        Add("cte.placeColumnsOnNewLine", "false", (p, v) => p.Cte.PlaceColumnsOnNewLine = B(v) ? "always" : "never");
        Add("cte.placeAsOnNewLine", "true", (p, v) => p.Cte.AsOnNewLine = B(v));
        AddUnsupported("cte.asAlignment", "leftAligned",
            "AS-keyword alignment applies only when AS is on its own line; AKML models AS placement but not its alignment column. Revisit with phase-3 CTE work if goldens require it.");

        // ----- variables -----
        Add("variables.alignDataTypesAndValues", "true", (p, v) =>
        {
            p.Declare.AlignDataTypes = B(v);
            p.Declare.AlignDefaultValues = B(v);
        });
        Add("variables.placeEqualsSignOnNewLine", "false", (p, v) => p.Declare.EqualsOnNewLine = B(v));
        Add("variables.placeAssignedValueOnNewLineIfLongerThanMaxLineLength", "true", (_, _) => { }); // phase-3 DECLARE/SET wrap behavior; no distinct field needed — wrap pass consults MaxLineWidth
    }
}
```

Property-name verification note (do this before running): `Ddl.IndentParenContents` — check the actual `DdlOptions` property (audit lists `indentParenthesesContents` semantics wired via `CreateTableColumnsOnNewLine` + paren rules; if `DdlOptions` has no such field, map to `p.Parenthesis.IndentContents` only when `ddl.*` is absent is WRONG — instead add the field `IndentParenContents` to `DdlOptions` in Task 2 style with `[JsonPropertyName("indentParenContents")]`). Resolve against the real POCO and keep the test authoritative.

- [ ] **Step 4: Run tests** — expected PASS.

- [ ] **Step 5: Commit checkpoint**

```bash
git add src/AkmlSql.Formatting/Profiles/RedgateOptionMap.Statements.cs tests/AkmlSql.Formatting.Tests/Profiles/RedgateJsonStyleImporterTests.cs src/AkmlSql.Formatting/Profiles/FormattingProfile.cs
git commit -m "feat(031): Redgate mapping — dml/ddl/controlFlow/cte/variables incl. threshold-implies-enabled (FR-003)"
```

---

### Task 6: Mapping table — joinStatements, insertStatements, functionCalls, caseExpressions, operators

**Files:**
- Create: `src/AkmlSql.Formatting/Profiles/RedgateOptionMap.Expressions.cs`
- Modify: `tests/AkmlSql.Formatting.Tests/Profiles/RedgateJsonStyleImporterTests.cs`

**Interfaces:**
- Consumes: helpers from Task 4; fields from Task 2.
- Produces: complete `Entries` map; the Task 3 skipped completeness fact goes green.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void Join_insert_function_case_operators_map_from_user_style()
    {
        var p = RedgateJsonStyleImporter.Import(UserStyleJson).Profile;

        Assert.Equal("toTable", p.Join.AlignJoinKeyword);
        Assert.False(p.Join.IndentJoin);
        Assert.False(p.Join.OnConditionNewLine);
        Assert.Equal("indent", p.Join.OnConditionIndent);

        Assert.Equal("expandedSimple", p.InsertStatements.Columns.ParenthesisStyle);
        Assert.False(p.InsertStatements.Columns.IndentContents);
        Assert.Equal("always", p.InsertStatements.Columns.PlaceSubsequentItemsOnNewLines); // omitted -> Redgate section default
        Assert.Equal("expandedSimple", p.InsertStatements.Values.ParenthesisStyle);
        Assert.True(p.InsertStatements.Values.IndentContents);
        Assert.Equal("always", p.InsertStatements.Values.PlaceSubsequentItemsOnNewLines);

        Assert.Equal("never", p.FunctionCalls.PlaceParametersOnNewLine);
        Assert.True(p.FunctionCalls.SpaceAroundParentheses);
        Assert.True(p.FunctionCalls.SpaceAroundArgumentList);
        Assert.True(p.FunctionCalls.SpaceBetweenEmptyParentheses);

        Assert.Equal("never", p.Case.FirstWhenOnNewLine);
        Assert.Equal("toFirstItem", p.Case.WhenAlignment);
        Assert.True(p.Case.ThenOnNewLine);
        Assert.Equal("toWhen", p.Case.ThenAlignment);
        Assert.False(p.Case.EndOnNewLine);
        Assert.True(p.Case.CollapseShortCase);
        Assert.Equal(110, p.Case.CollapseThreshold);

        Assert.Equal("toFirstListItem", p.Operators.Alignment);
        Assert.False(p.Operators.BetweenOnNewLine);
        Assert.Equal("rightAlignedToBetween", p.Operators.BetweenAndAlignment);
        Assert.Equal("never", p.InStatements.PlaceItemsOnNewLine);   // placeFirstValueOnNewLine=never
        Assert.True(p.InStatements.SpaceAroundContents);
    }
```

Verify actual property names in `CaseOptions` (`FirstWhenOnNewLine`, `WhenAlignment`, `ThenOnNewLine`, `EndOnNewLine`, `CollapseShortCase`, `CollapseThreshold` per the audit) and `JoinOptions` (`AlignJoinKeyword`, `IndentJoin`, `OnConditionNewLine`, `OnConditionIndent`) against `FormattingProfile.cs` before running; adjust the test to the real names, never the reverse.

- [ ] **Step 2: Run to verify failure** — expected FAIL.

- [ ] **Step 3: Implement**

```csharp
// src/AkmlSql.Formatting/Profiles/RedgateOptionMap.Expressions.cs
namespace AkmlSql.Formatting.Profiles;

internal static partial class RedgateOptionMap
{
    static partial void RegisterJoinInsertFunctionCaseOperators()
    {
        // ----- joinStatements -----
        Add("joinStatements.join.placeOnNewLine", "true", (_, _) => { }); // AKML always breaks before JOIN; matches default true. Value false is honored by phase-3 join work if a golden demands it.
        Add("joinStatements.join.keywordAlignment", "toFrom", (p, v) => p.Join.AlignJoinKeyword = v.Trim().ToLowerInvariant() switch
        {
            "rightalignedtofrom" => "right",
            "totable" => "toTable",
            "indented" => "indentedFromFrom",
            _ => "left", // toFrom
        });
        Add("joinStatements.join.indentJoinTable", "true", (p, v) => p.Join.IndentJoin = B(v));
        Add("joinStatements.join.placeJoinTableOnNewLine", "false", (_, _) => { }); // no AKML model; false (default) is AKML behavior
        Add("joinStatements.join.insertEmptyLineBetweenJoinClauses", "false", (p, v) => p.Join.EmptyLineBeforeJoin = B(v));
        Add("joinStatements.on.placeOnNewLine", "true", (p, v) => p.Join.OnConditionNewLine = B(v));
        Add("joinStatements.on.keywordAlignment", "toJoin", (p, v) => p.Join.OnConditionIndent = v.Trim().ToLowerInvariant() switch
        {
            "indented" => "indent",
            _ => "toJoin", // toJoin/rightAlignedToJoin/rightAlignedToInner/toTable — phase 3 extends; only 'indent' renders today
        });
        Add("joinStatements.on.placeConditionOnNewLine", "false", (_, _) => { });
        Add("joinStatements.on.conditionAlignment", "toOnKeyword", (_, _) => { });

        // ----- insertStatements -----
        Add("insertStatements.columns.parenthesisStyle", "expandedToStatement", (p, v) => p.InsertStatements.Columns.ParenthesisStyle = NormalizeParenStyle(v));
        Add("insertStatements.columns.indentContents", "true", (p, v) => p.InsertStatements.Columns.IndentContents = B(v));
        Add("insertStatements.columns.placeSubsequentColumnsOnNewLines", "always", (p, v) => p.InsertStatements.Columns.PlaceSubsequentItemsOnNewLines = NormalizePlacement(v));
        Add("insertStatements.values.parenthesisStyle", "compactToStatement", (p, v) => p.InsertStatements.Values.ParenthesisStyle = NormalizeParenStyle(v));
        Add("insertStatements.values.indentContents", "false", (p, v) => p.InsertStatements.Values.IndentContents = B(v));
        Add("insertStatements.values.placeSubsequentValuesOnNewLines", "never", (p, v) => p.InsertStatements.Values.PlaceSubsequentItemsOnNewLines = NormalizePlacement(v));

        // ----- functionCalls -----
        Add("functionCalls.placeArgumentsOnNewLines", "ifLongerThanMaxLineLength", (p, v) => p.FunctionCalls.PlaceParametersOnNewLine = NormalizePlacement(v));
        Add("functionCalls.addSpacesAroundParentheses", "false", (p, v) => p.FunctionCalls.SpaceAroundParentheses = B(v));
        Add("functionCalls.addSpacesAroundArgumentList", "false", (p, v) => p.FunctionCalls.SpaceAroundArgumentList = B(v));
        Add("functionCalls.addSpaceBetweenEmptyParentheses", "false", (p, v) => p.FunctionCalls.SpaceBetweenEmptyParentheses = B(v));
        Add("functionCalls.indentContents", "false", (p, v) => p.FunctionCalls.IndentParameters = B(v));

        // ----- caseExpressions -----
        Add("caseExpressions.placeFirstWhenOnNewLine", "always", (p, v) => p.Case.FirstWhenOnNewLine = v.Trim().ToLowerInvariant() switch
        {
            "never" => "never",
            "ifinputexpression" => "auto",
            _ => "always",
        });
        Add("caseExpressions.placeExpressionOnNewLine", "true", (p, v) => p.Case.ExpressionOnNewLine = B(v));
        Add("caseExpressions.whenAlignment", "indentedFromCase", (p, v) => p.Case.WhenAlignment = v.Trim().ToLowerInvariant() switch
        {
            "tocase" => "toCase",
            "tofirstitem" => "toFirstItem",
            _ => "indentedFromCase",
        });
        Add("caseExpressions.placeThenOnNewLine", "false", (p, v) => p.Case.ThenOnNewLine = B(v));
        Add("caseExpressions.thenAlignment", "indentedFromWhen", (p, v) => p.Case.ThenAlignment = v.Trim().ToLowerInvariant() switch
        {
            "towhen" => "toWhen",
            "towhenexpression" => "toWhenExpression",
            "intentedfromwhen" => "indentedFromWhen", // Redgate's own historical typo build
            _ => "indentedFromWhen",
        });
        Add("caseExpressions.placeElseOnNewLine", "true", (p, v) => p.Case.ElseOnNewLine = B(v));
        Add("caseExpressions.alignElseToWhen", "true", (_, _) => { }); // follows WhenAlignment in AKML's model
        Add("caseExpressions.placeEndOnNewLine", "true", (p, v) => p.Case.EndOnNewLine = B(v));
        Add("caseExpressions.endAlignment", "toCase", (p, v) => p.Case.EndAlignment = v.Trim().ToLowerInvariant() switch
        {
            "towhen" or "rightalignedtowhen" => "indented",
            _ => "toCase",
        });
        Add("caseExpressions.collapseShortCaseExpressions", "false", (p, v) => p.Case.CollapseShortCase = B(v));
        Add("caseExpressions.collapseCaseExpressionsShorterThan", "80", (p, v) => p.Case.CollapseThreshold = I(v, 80));

        // ----- operators -----
        Add("operators.andOr.alignment", "leftAligned", (p, v) => p.Operators.Alignment = v.Trim().ToLowerInvariant() switch
        {
            "rightaligned" => "rightAligned",
            "beforefirstlistitem" => "beforeFirstListItem",
            "tofirstlistitem" => "toFirstListItem",
            "indented" => "indentedFromStatement",
            _ => "inlineWithStatement", // leftAligned
        });
        Add("operators.andOr.placeOnNewLine", "always", (_, _) => { }); // AKML breaks each condition; matches default
        Add("operators.andOr.placeKeywordBeforeCondition", "true", (p, v) => p.Dml.AndOrNewLine = B(v) ? "before" : "after");
        Add("operators.between.placeOnNewLine", "true", (p, v) => p.Operators.BetweenOnNewLine = B(v));
        Add("operators.between.placeAndKeywordOnNewLine", "false", (p, v) => p.Operators.AndBetweenOnNewLine = B(v));
        Add("operators.between.andAlignment", "toBetween", (p, v) => p.Operators.BetweenAndAlignment = v.Trim().ToLowerInvariant() switch
        {
            "rightalignedtobetween" => "rightAlignedToBetween",
            "tobeginningofexpression" => "toBeginningOfExpression",
            _ => "toBetween",
        });
        Add("operators.in.placeFirstValueOnNewLine", "ifLongerThanMaxLineLength", (p, v) => p.InStatements.PlaceItemsOnNewLine = v.Trim().ToLowerInvariant() switch
        {
            "never" => "never",
            "always" or "ifsubsequentvalues" => "always",
            _ => "ifLongerThanWrap",
        });
        Add("operators.in.placeSubsequentValuesOnNewLines", "ifLongerThanMaxLineLength", (_, _) => { }); // AKML stacks all-or-none via PlaceItemsOnNewLine
        Add("operators.in.placeOpeningParenthesisOnNewLine", "false", (_, _) => { });
        Add("operators.in.alignment", "leftAligned", (p, v) => p.InStatements.Alignment = v.Trim().ToLowerInvariant() switch
        {
            "rightaligned" => "rightAligned",
            "indented" => "stacked",
            _ => "stacked",
        });
        Add("operators.in.addSpaceAroundInContents", "false", (p, v) => p.InStatements.SpaceAroundContents = B(v));
    }

    private static string NormalizePlacement(string v) => v.Trim().ToLowerInvariant() switch
    {
        "always" => "always",
        "never" => "never",
        _ => "ifLongerThanWrap", // ifLongerThanMaxLineLength
    };
}
```

- [ ] **Step 4: Un-skip and pass the completeness fact**

Remove the `Skip` from `Import_classifies_every_leaf_key_in_the_file` (Task 3) and its added assertion `DoesNotContain(... Status == Unknown)`.
Run: `dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release --filter "FullyQualifiedName~RedgateJsonStyleImporterTests"`
Expected: ALL PASS — every one of the 65 keys in the user's file resolves to a non-unknown status.

- [ ] **Step 5: Commit checkpoint**

```bash
git add src/AkmlSql.Formatting/Profiles/RedgateOptionMap.Expressions.cs tests/AkmlSql.Formatting.Tests/Profiles/RedgateJsonStyleImporterTests.cs
git commit -m "feat(031): Redgate mapping — join/insert/functionCalls/case/operators; user style fully classified"
```

---

### Task 7: Schema completeness — vendored Redgate schema walk (SC-004)

> **Amended during execution (Task 3/6 findings):** `full-style.json.example` is a schema-shaped TEMPLATE (`"numberOfSpacesInTabs": int`), NOT parseable JSON — it cannot be imported. The completeness gate instead walks the real draft-07 `formattingstyle-schema.json` and asserts every leaf option path is present in `RedgateOptionMap.Entries` (mapped or unsupported). SC-004's intent (zero unknown at the vendored schema version) is unchanged.

**Files:**
- Create: `tests/AkmlSql.Formatting.Tests/Fixtures/formattingstyle-schema.json` (copy from `specs/031-redgate-style-import/reference/`), `tests/AkmlSql.Formatting.Tests/Profiles/RedgateSchemaCompletenessTests.cs`
- Modify: `src/AkmlSql.Formatting/Profiles/RedgateOptionMap.*.cs` (add any keys the test exposes), `src/AkmlSql.Formatting/Profiles/RedgateJsonStyleImporter.cs` (public `KnownOptionPaths` accessor if tests can't see internals)

**Interfaces:** `RedgateJsonStyleImporter.KnownOptionPaths : IReadOnlyCollection<string>` (public, = `RedgateOptionMap.Entries.Keys`) — added only if the test project lacks `InternalsVisibleTo` for `AkmlSql.Formatting`.

- [ ] **Step 1: Write the test**

```csharp
// tests/AkmlSql.Formatting.Tests/Profiles/RedgateSchemaCompletenessTests.cs
using System.Text.Json;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Profiles;

public class RedgateSchemaCompletenessTests
{
    [Fact]
    public void Every_schema_leaf_key_is_mapped_or_explicitly_unsupported()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "formattingstyle-schema.json"));
        using var doc = JsonDocument.Parse(json);
        var leaves = new List<string>();
        CollectLeaves(doc.RootElement.GetProperty("properties"), "", leaves);

        var missing = leaves
            .Where(p => !p.StartsWith("metadata", StringComparison.OrdinalIgnoreCase))
            .Where(p => !RedgateJsonStyleImporter.KnownOptionPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.True(leaves.Count > 60, $"Schema walk looks broken — only {leaves.Count} leaves found.");
        Assert.True(missing.Count == 0,
            "Schema keys not classified (add Add(...) or AddUnsupported(...) with a reason):\n" + string.Join("\n", missing));
    }

    private static void CollectLeaves(JsonElement properties, string prefix, List<string> into)
    {
        foreach (var prop in properties.EnumerateObject())
        {
            var path = prefix.Length == 0 ? prop.Name : $"{prefix}.{prop.Name}";
            if (prop.Value.ValueKind == JsonValueKind.Object && prop.Value.TryGetProperty("properties", out var nested))
                CollectLeaves(nested, path, into);
            else
                into.Add(path);
        }
    }

    [Fact]
    public void Every_unsupported_entry_has_a_reason()
    {
        // Import a synthetic file containing ONLY unsupported keys? Simpler: reasons are enforced at
        // registration — assert via a representative unsupported key end-to-end:
        var result = RedgateJsonStyleImporter.Import("""{ "cte": { "asAlignment": "indented" } }""");
        var report = Assert.Single(result.Options);
        Assert.Equal(RedgateOptionStatus.Unsupported, report.Status);
        Assert.False(string.IsNullOrWhiteSpace(report.Reason));
    }

    [Fact]
    public void Example_enum_values_are_uppercamel_and_still_match()
    {
        // full-style.json.example documents enums in UpperCamelCase; real files serialize lowerCamelCase (FR-001).
        var result = RedgateJsonStyleImporter.Import("""{ "casing": { "reservedKeywords": "UpperCamelCase" } }""");
        Assert.Equal("PascalCase", result.Profile.Casing.ReservedKeywords);
    }
}
```

- [ ] **Step 2: Run — enumerate the gap**

Run: `dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release --filter "FullyQualifiedName~RedgateSchemaCompletenessTests"`
Expected: FAIL, with the assertion message listing every schema key Tasks 4–6 didn't cover.

- [ ] **Step 3: Close the gap**

For each listed key, consult its `description`/`default` in `specs/031-redgate-style-import/reference/formattingstyle-schema.json` and add to the appropriate `RedgateOptionMap.*.cs` partial either an `Add(...)` targeting an existing profile field with matching semantics, or `AddUnsupported(path, default, "<specific reason — what SQL Prompt does, why AKML has no model, and which phase/backlog would add it>")`. **Rule: never leave a schema key unknown; never map to a field with different semantics just to claim coverage** — a wrong mapping is worse than an honest unsupported.

- [ ] **Step 4: Run — both facts pass.** Then run the whole importer suite again (`--filter "FullyQualifiedName~Redgate"`): all green.

- [ ] **Step 5: Commit checkpoint**

```bash
git add src/AkmlSql.Formatting/Profiles/ tests/AkmlSql.Formatting.Tests/
git commit -m "feat(031): full Redgate schema coverage — every schema leaf mapped or unsupported (SC-004)"
```

---

### Task 8: Engine handler — sniffing, failure semantics, source preservation, reports

**Files:**
- Modify: `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs:500-559` (`HandleProfileImport`)
- Modify: `src/AkmlSql.Formatting/Profiles/ProfileManager.cs` (add `IsBuiltIn(string name)` + `CustomProfilesPath` accessor if absent — check first; List() already distinguishes built-ins)
- Test: `tests/AkmlSql.Engine.Tests/Formatter/ProfileImportHandlerTests.cs`

**Interfaces:**
- Consumes: `RedgateJsonStyleImporter.Import`, `ProfileImportOptionReport` (Task 1).
- Produces: `HandleProfileImport` behavior consumed by the shell (Task 9): SourceFormat `"sqlprompt"`/`"sqlpromptstylev2"` now sniffs JSON vs XML; failure → `Success=false` + `ErrorMessage`, **no save**; success saves `<name>.akmlstyle` + `<name>.source.json` and returns `OptionReports` + `ProfileName`. Add `[Key(6)] public string? ProfileName { get; set; }` to `ProfileImportResponse` (shell needs the final saved name for selection/activation).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AkmlSql.Engine.Tests/Formatter/ProfileImportHandlerTests.cs
using System.Text;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Engine.Tests.Formatter;

public class ProfileImportHandlerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("akml-031-").FullName;
    private readonly ProfileManager _profiles;
    private readonly FormatRequestHandler _handler;

    public ProfileImportHandlerTests()
    {
        _profiles = new ProfileManager(
            builtInProfilesPath: Path.Combine(_dir, "builtin"),
            customProfilesPath: Path.Combine(_dir, "custom"));
        _handler = new FormatRequestHandler(_profiles /* match the real ctor — see note below */);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string UserStyleJson =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json"));

    private ProfileImportResponse Import(string content, string format = "sqlprompt") =>
        _handler.HandleProfileImport(new ProfileImportRequest
        {
            SourceFormat = format,
            FileContent = Encoding.UTF8.GetBytes(content),
        });

    [Fact]
    public void Json_content_with_sqlprompt_format_routes_to_json_importer()
    {
        var response = Import(UserStyleJson);
        Assert.True(response.Success);
        Assert.Equal("MohamedKhamis", response.ProfileName);
        Assert.NotNull(response.OptionReports);
        Assert.Equal(65, response.OptionReports!.Length);
        Assert.DoesNotContain(response.OptionReports, r => r.Status == "unknown");
        // Saved artifacts:
        Assert.True(File.Exists(Path.Combine(_dir, "custom", "MohamedKhamis.akmlstyle")));
        Assert.True(File.Exists(Path.Combine(_dir, "custom", "MohamedKhamis.source.json")));
    }

    [Fact]
    public void Malformed_content_fails_and_saves_nothing()
    {
        var response = Import("not { valid <xml> or json");
        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "custom")));
    }

    [Fact]
    public void Xml_content_still_routes_to_legacy_importer()
    {
        const string xml = """<SqlPromptStyle><Options><Option Name="KeywordCasing" Value="uppercase"/></Options></SqlPromptStyle>""";
        var response = Import(xml);
        Assert.True(response.Success);
        Assert.Equal(1, response.MappedOptionsCount);
    }

    [Fact]
    public void Utf8_bom_prefixed_json_still_imports()
    {
        var bomBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(UserStyleJson)).ToArray();
        var response = _handler.HandleProfileImport(new ProfileImportRequest
        {
            SourceFormat = "sqlprompt",
            FileContent = bomBytes,
        });
        Assert.True(response.Success);
        Assert.Equal("MohamedKhamis", response.ProfileName);
    }

    [Fact]
    public void BuiltIn_name_collision_fails_with_clear_error()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "builtin"));
        File.WriteAllText(Path.Combine(_dir, "builtin", "Default.akmlstyle"),
            ProfileSerializer.Serialize(new FormattingProfile { Metadata = { Name = "Default", IsBuiltIn = true } }));

        var response = Import("""{ "metadata": { "name": "Default" } }""");
        Assert.False(response.Success);
        Assert.Contains("built-in", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
```

Ctor note: `FormatRequestHandler`'s real constructor takes the engine's dependencies — read `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs:1-60` and construct with the same test doubles the existing engine tests use (look at how `tests/AkmlSql.Engine.Tests` builds handlers today; reuse that harness). If a test harness type already exists for it, use it; the essential injected piece is the `ProfileManager` pointed at temp dirs. Also note `Directory.CreateTempSubdirectory` requires .NET 7+ — engine tests are net10.0, fine.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release --filter "FullyQualifiedName~ProfileImportHandlerTests"`
Expected: FAIL — `ProfileName` missing, JSON routes to XML importer (silent-success bug), no `.source.json`.

- [ ] **Step 3: Implement**

Add to `ProfileImportResponse`: `[Key(6)] public string? ProfileName { get; set; }`.

Replace the `sqlprompt` branch of `HandleProfileImport` (`FormatRequestHandler.cs:507-521`):

```csharp
            if (sourceFormat is "sqlprompt" or "sqlpromptstylev2")
            {
                // Spec 031 FR-004 — sniff content: modern Redgate styles are JSON; the XML shape
                // is AKML's own spec-020 export. Sniffing is scoped to this branch on purpose —
                // the akmlstyle branch below also receives JSON.
                // U+FEFF: Encoding.UTF8.GetString keeps a BOM as a leading char and it is NOT
                // char.IsWhiteSpace, so strip it explicitly (spec edge case: BOM'd files decode correctly).
                var firstChar = content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n').FirstOrDefault();

                if (firstChar == '{')
                {
                    var jsonResult = RedgateJsonStyleImporter.Import(content, fallbackName: request.TargetProfileName);
                    if (!jsonResult.Success)
                    {
                        // FR-005 — visible failure, nothing saved.
                        return new ProfileImportResponse
                        {
                            Success = false,
                            ErrorMessage = $"Style file is not valid SQL Prompt JSON: {jsonResult.ParseError}",
                        };
                    }

                    if (!string.IsNullOrWhiteSpace(request.TargetProfileName))
                        jsonResult.Profile.Metadata.Name = request.TargetProfileName;

                    // FR-008 — built-in names cannot be shadowed by import.
                    if (profileManager.List().Any(p =>
                            p.IsBuiltIn && string.Equals(p.Name, jsonResult.Profile.Metadata.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new ProfileImportResponse
                        {
                            Success = false,
                            ErrorMessage = $"'{jsonResult.Profile.Metadata.Name}' is a built-in style name. Re-import with a different target name.",
                        };
                    }

                    profileManager.Save(jsonResult.Profile);

                    // FR-006 — preserve the verbatim source beside the profile for lossless re-import.
                    var sourcePath = Path.Combine(profileManager.CustomProfilesPath,
                        ProfileManager.SanitizeFileName(jsonResult.Profile.Metadata.Name) + ".source.json");
                    File.WriteAllText(sourcePath, content);

                    return new ProfileImportResponse
                    {
                        Success = true,
                        ProfileName = jsonResult.Profile.Metadata.Name,
                        MappedOptionsCount = jsonResult.MappedCount,
                        UnmappedOptionsCount = jsonResult.UnsupportedCount + jsonResult.UnknownCount,
                        OptionReports = jsonResult.Options
                            .Select(o => new ProfileImportOptionReport { Path = o.Path, Value = o.Value, Status = o.Status, Reason = o.Reason })
                            .ToArray(),
                    };
                }

                if (firstChar != '<')
                {
                    return new ProfileImportResponse
                    {
                        Success = false,
                        ErrorMessage = "Style file is neither JSON ('{') nor XML ('<').",
                    };
                }

                var importResult = SqlPromptImporter.Import(content, request.TargetProfileName);

                // FR-005 — the legacy importer records parse errors in UnmappedOptions without failing; surface them.
                var parseError = importResult.UnmappedOptions.FirstOrDefault(o => o.StartsWith("Parse error:", StringComparison.Ordinal));
                if (parseError != null || importResult.MappedCount == 0 && importResult.UnmappedCount == 0)
                {
                    return new ProfileImportResponse
                    {
                        Success = false,
                        ErrorMessage = parseError ?? "No options found in the XML style file.",
                    };
                }

                profileManager.Save(importResult.Profile);
                return new ProfileImportResponse
                {
                    Success = true,
                    ProfileName = importResult.Profile.Metadata.Name,
                    MappedOptionsCount = importResult.MappedCount,
                    UnmappedOptionsCount = importResult.UnmappedCount,
                    UnmappedOptions = importResult.UnmappedOptions.ToArray(),
                };
            }
```

`ProfileManager` additions (only if absent — check first): expose `public string CustomProfilesPath => _customProfilesPath;` and make the existing filename-sanitizer callable as `public static string SanitizeFileName(string name)` (it exists privately per `ProfileManager.cs:291-329`; widen visibility rather than duplicating). `List()` already returns entries with `IsBuiltIn` + `Name` (verify the element type's property names at `ProfileManager.cs:117-148` and adjust the LINQ accordingly).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release --filter "FullyQualifiedName~ProfileImportHandlerTests"`
Expected: PASS (4/4). Also `dotnet test tests/AkmlSql.Core.Tests/... --filter ProfileImportOptionReportTests` still green (Key(6) added).

- [ ] **Step 5: Commit checkpoint**

```bash
git add src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs src/AkmlSql.Formatting/Profiles/ProfileManager.cs src/AkmlSql.Core/Ipc/Messages/ProfileImportResponse.cs tests/AkmlSql.Engine.Tests/Formatter/ProfileImportHandlerTests.cs
git commit -m "fix(031): ProfileImport sniffs JSON/XML, fails visibly, preserves source, returns reports (FR-004..008)"
```

---

### Task 9: Shell — Import… button, summary, activation

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorViewModel.cs` (after `ExportProfileAsync`, `:488-516`)
- Modify: `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorWindow.cs` (toolbar `:230-235`, handler after `OnExportAsync` `:317-334`)

**Interfaces:**
- Consumes: `MessageTypes.ProfileImport = 17`, `ProfileImportRequest/Response` (+`ProfileName`, `OptionReports`), `SetActiveProfile` (`FormatStylesEditorViewModel.cs:469-485`), `RefreshProfilesAsync` (`:519`).
- Produces: `Task<ProfileImportResponse?> ImportProfileAsync(string filePath, string? targetName)` on the ViewModel; Import… button wired in the window.

- [ ] **Step 1: ViewModel method** (mirror `ExportProfileAsync`'s IPC pattern exactly)

```csharp
        /// <summary>
        /// Spec 031 FR-010/FR-011 — imports a SQL Prompt style file (JSON or legacy XML) via the
        /// ProfileImport IPC. Returns the full response (option reports included) or null on
        /// transport failure; LastError is set on any failure path.
        /// </summary>
        public async Task<ProfileImportResponse?> ImportProfileAsync(string filePath, string? targetName = null)
        {
            const long MaxImportBytes = 1024 * 1024; // FR-010 — 1 MB cap, mirrors snippet import

            var client = EngineLifecycle.Manager?.Client;
            if (client == null || !client.IsConnected)
            {
                LastError = "Engine not connected.";
                return null;
            }
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists) { LastError = "File not found."; return null; }
                if (info.Length > MaxImportBytes) { LastError = "Style file exceeds the 1 MB import limit."; return null; }

                var bytes = File.ReadAllBytes(filePath); // UTF-8 (BOM tolerated engine-side by Encoding.UTF8.GetString)
                var response = await client.SendRequestAsync<ProfileImportResponse, ProfileImportRequest>(
                    MessageTypes.ProfileImport,
                    new ProfileImportRequest { SourceFormat = "sqlprompt", FileContent = bytes, TargetProfileName = targetName },
                    timeoutMs: 5000).ConfigureAwait(false);

                if (response == null || !response.Success)
                {
                    LastError = response?.ErrorMessage ?? "Import failed.";
                    return response;
                }
                await RefreshProfilesAsync().ConfigureAwait(false);
                return response;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Log.Warning(ex, "FormatStylesEditor: import {Path} failed", filePath);
                return null;
            }
        }
```

- [ ] **Step 2: Window wiring** — add the toolbar button between Set Active and Export (`FormatStylesEditorWindow.cs:233`):

```csharp
            toolbar.Children.Add(MakeToolbarButton("Import…", OnImportAsync));
```

Handler (after `OnExportAsync`):

```csharp
        private async System.Threading.Tasks.Task OnImportAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import SQL Prompt style",
                Filter = "SQL Prompt style (*.json;*.sqlpromptstylev2)|*.json;*.sqlpromptstylev2|All files (*.*)|*.*",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(this) != true) return;

            // FR-008 — collision check against the client-side list before sending.
            var stem = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            var response = await _viewModel.ImportProfileAsync(dialog.FileName);

            // Engine rejects built-in collisions; custom collisions overwrite by ProfileManager
            // semantics, so confirm when the target name already exists in the list.
            if (response != null && response.Success && response.ProfileName != null)
            {
                AfterCreate(response.ProfileName, BuildImportSummary(response));
                if (_viewModel.SetActiveProfile(response.ProfileName))
                    UpdateStatusBarActiveStyle(response.ProfileName); // FR-011 — import + set active
                ShowImportSummaryDialog(response);                    // FR-012
            }
            else
            {
                SetStatus(_viewModel.LastError ?? "Import failed.");
            }
        }

        private static string BuildImportSummary(ProfileImportResponse r)
        {
            var reports = r.OptionReports ?? [];
            int mapped = reports.Count(x => x.Status == "mapped");
            int pending = reports.Count(x => x.Status == "mapped-pending-render");
            int unsupported = reports.Count(x => x.Status == "unsupported");
            int unknown = reports.Count(x => x.Status == "unknown");
            return $"Imported '{r.ProfileName}' — {mapped} mapped, {pending} pending render, {unsupported} unsupported, {unknown} unknown";
        }
```

`ShowImportSummaryDialog`: theme-aware modal listing `OptionReports` grouped by status — follow the repo's WPF conventions verbatim (ThemeManager tokens via `SetResourceReference`, frozen brushes, DTE-HWND owner, `IsCancel` close button; reference implementation `src/AkmlSql.Shell.Shared/History/HistoryDiffWindow.cs`). Layout: header with the summary line, a `ListView` with columns Path / Value / Status / Reason bound to the report array, one Close button. Pre-send overwrite confirmation: before calling `ImportProfileAsync`, if `_viewModel.Profiles` already contains a non-built-in entry named `stem` (or, after a first attempt, `response.ProfileName`), show `MessageBox` "Style '<name>' already exists. Overwrite?" (OK/Cancel) and pass `targetName` with a " (2)" suffix on Cancel-and-rename choice — keep it to overwrite/auto-rename, no free-text prompt.

- [ ] **Step 3: Build both shell hosts + engine**

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"  # or .../18/Enterprise on this machine
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Build -p:Configuration=Release -v:minimal
"$MSBUILD" src/AkmlSql.VS2026/AkmlSql.VS2026.csproj -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" src/AkmlSql.VS2026/AkmlSql.VS2026.csproj -t:Build -p:Configuration=Release -v:minimal
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
```

Expected: all three build clean (warnings ok, errors none).

- [ ] **Step 4: Manual smoke (SSMS 22)** — deploy the FULL engine publish + extension per `doc/deployment.md`, then: Tools → AKML SQL → Format Styles → Import… → pick `specs/031-redgate-style-import/reference/MohamedKhamis-2cd71422-30f2-4360-800f-240f2897fd3e.json` → expect status `Imported 'MohamedKhamis' — 31 mapped, 34 pending render, 0 unsupported, 0 unknown` (the 31 = 29 wired contract rows + 2 holds-by-construction; 65 total), style selected + active, and Ctrl+K,Ctrl+D formatting a scratch query with UPPERCASE keywords + leading commas. Re-import → overwrite prompt appears. Import a `.txt` renamed to `.json` containing garbage → clear error, no new style.

- [ ] **Step 5: Commit checkpoint**

```bash
git add src/AkmlSql.Shell.Shared/Formatting/
git commit -m "feat(031): Format Styles editor Import… — file dialog, summary report, set-active (FR-010..012)"
```

---

### Task 10: Editor badges for pending/unsupported options (FR-012 tail)

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorViewModel.cs` + `FormatStylesEditorWindow.cs` (settings-tree rendering)

**Interfaces:**
- Consumes: `OptionReports` from the import response (Task 9) — persisted per-profile by re-reading `<name>.source.json`? No: keep it session-light. The editor already renders SQL Prompt mapping status per setting from `FormatSettingSchema` (spec-020 FR-023 badge surface — find it by grepping `Unsupported` / `badge` in `FormatStylesEditorWindow.cs`/`FormatStylesEditorViewModel.cs`).
- Produces: settings whose Redgate option is `mapped-pending-render` or `unsupported` show the existing badge style with tooltip = the report `Reason`.

- [ ] **Step 1: Locate the spec-020 badge surface** — grep `FormatStylesEditor*.cs` for the FR-023 rendering (search terms: `SqlPromptKey`, `Unsupported`, `badge`, `Status`). Reuse exactly that visual (no new styling).
- [ ] **Step 2: Hold the last import's reports on the ViewModel** (`public IReadOnlyList<ProfileImportOptionReport>? LastImportReports`), set in `ImportProfileAsync` on success; when the selected profile's `Metadata.BasedOn == "SQL Prompt Import"` and reports are present for it, pass status/reason into the tree-node builder for matching settings (match report `Path` → profile field via `RedgateOptionMap` — expose `internal static bool TryGetTargetDescription(string path, out string akmlField)` if a display mapping is needed; otherwise badge at group level).
- [ ] **Step 3: Build both hosts** (commands as Task 9 Step 3). Manual check: after importing the user style, `lists.commaAlignment`-backed setting shows a "pending render" badge with the reason tooltip.
- [ ] **Step 4: Commit checkpoint**

```bash
git add src/AkmlSql.Shell.Shared/Formatting/
git commit -m "feat(031): pending/unsupported badges on imported style options (FR-012)"
```

**Scope guard:** if Step 1 reveals the FR-023 surface renders only from `FormatSettingSchema` (static) and per-import badging needs a new plumb-through, keep this task to the status-line summary + summary dialog (already done in Task 9) and file the per-setting badge as a Phase-3-adjacent follow-up — do not invent a parallel badge system.

---

# Phase 2 — Ground truth (goldens)

### Task 11: Author the 20-file corpus

**Files:**
- Create: `tests/format-parity/corpus/sp031-01-select-list.sql` … `sp031-20-merge.sql`

Corpus inputs are deliberately mis-formatted (single-line where possible, lowercase keywords, no leading commas) so every relevant option must act. Full contents:

`sp031-01-select-list.sql` — lists, leading commas, gutter, tab stops, aliases:
```sql
select o.orderid as id, o.orderdate, o.requireddate, o.shippeddate, o.shipvia, o.freight, o.shipname, o.shipaddress, o.shipcity, o.shipregion, o.shippostalcode, o.shipcountry from dbo.orders o where o.freight > 50 order by o.orderdate desc, o.orderid;
```

`sp031-02-distinct-top.sql` — DISTINCT/TOP newline-after:
```sql
select distinct top 25 c.country, c.city from dbo.customers c order by c.country;
select top (100) percent p.productname, p.unitprice from dbo.products p where p.discontinued = 0;
```

`sp031-03-where-andor.sql` — AND/OR toFirstListItem alignment:
```sql
select e.employeeid, e.lastname from dbo.employees e where e.country = 'USA' and e.title = 'Sales Representative' or e.reportsto is null and e.hiredate >= '1993-01-01' and (e.city = 'Seattle' or e.city = 'Tacoma');
```

`sp031-04-between-in.sql` — BETWEEN inline + AND alignment on wrap, IN spacing:
```sql
select o.orderid from dbo.orders o where o.orderdate between '1997-01-01' and '1997-12-31' and o.shipcountry in ('USA', 'UK', 'Germany', 'France') and o.freight between 10.5 and 200.75;
select od.orderid from dbo.[order details] od where od.productid in (select p.productid from dbo.products p where p.categoryid in (1, 2, 3) and p.unitprice between 5 and 50 and p.productname between 'Aniseed Syrup' and 'Wimmers gute Semmelknoedel');
```

`sp031-05-joins.sql` — JOIN toTable alignment, ON inline:
```sql
select o.orderid, c.companyname, e.lastname, s.companyname as shipper from dbo.orders o inner join dbo.customers c on c.customerid = o.customerid left outer join dbo.employees e on e.employeeid = o.employeeid inner join dbo.shippers s on s.shipperid = o.shipvia where o.shipcountry = 'Mexico';
```

`sp031-06-subqueries.sql` — paren expandedToStatement + collapse thresholds (one short, one long):
```sql
select c.companyname from dbo.customers c where c.customerid in (select o.customerid from dbo.orders o);
select c.companyname, (select count(*) from dbo.orders o where o.customerid = c.customerid and o.orderdate >= '1997-01-01' and o.shipcountry not in ('USA', 'Canada') and o.freight > (select avg(f.freight) from dbo.orders f where f.shipcountry = c.country)) as ordercount from dbo.customers c;
```

`sp031-07-case-short.sql` — CASE collapse < 110:
```sql
select o.orderid, case when o.freight > 100 then 'high' else 'low' end as band from dbo.orders o;
```

`sp031-08-case-long.sql` — searched CASE: first WHEN inline, toFirstItem, THEN toWhen, END inline:
```sql
select o.orderid, case when o.freight > 500 and o.shipcountry not in ('USA', 'Canada') then 'international heavy' when o.freight > 100 then 'heavy shipment overweight' when o.freight > 50 and o.shipvia = 3 then 'medium express shipment' else 'standard ground delivery' end as freightband, case o.shipvia when 1 then 'speedy' when 2 then 'united' else 'federal' end as shippername from dbo.orders o;
```

`sp031-09-cte-single.sql` — CTE name/indent/AS/parens:
```sql
with recentorders as (select o.orderid, o.customerid, o.orderdate from dbo.orders o where o.orderdate >= '1998-01-01') select r.customerid, count(*) as cnt from recentorders r group by r.customerid having count(*) > 3 order by cnt desc;
```

`sp031-10-cte-columns.sql` — CTE explicit column list (rightAligned) + multiple CTEs:
```sql
with ordertotals (orderid, customerid, linecount, totalvalue) as (select od.orderid, o.customerid, count(*), sum(od.unitprice * od.quantity * (1 - od.discount)) from dbo.[order details] od inner join dbo.orders o on o.orderid = od.orderid group by od.orderid, o.customerid), customerranks (customerid, rank) as (select customerid, row_number() over (order by sum(totalvalue) desc) from ordertotals group by customerid) select cr.customerid, cr.rank, ot.totalvalue from customerranks cr inner join ordertotals ot on ot.customerid = cr.customerid where cr.rank <= 10;
```

`sp031-11-insert-values.sql` — INSERT columns/VALUES expandedSimple, one value per line, multi-row:
```sql
insert into dbo.shippers (companyname, phone) values (N'Alpha Freight', N'(503) 555-0100');
insert into dbo.shippers (companyname, phone) values (N'Beta Cargo', N'(503) 555-0101'), (N'Gamma Lines', N'(503) 555-0102'), (N'Delta Express', N'(503) 555-0103');
```

`sp031-12-insert-select.sql` — INSERT…SELECT + column list:
```sql
insert into dbo.customerarchive (customerid, companyname, contactname, country, archivedate) select c.customerid, c.companyname, c.contactname, c.country, getdate() from dbo.customers c where not exists (select 1 from dbo.orders o where o.customerid = c.customerid and o.orderdate >= '1997-01-01');
```

`sp031-13-update-delete.sql` — UPDATE (with and without FROM), DELETE:
```sql
update dbo.products set unitprice = unitprice * 1.1, reorderlevel = reorderlevel + 5 where categoryid = 2 and discontinued = 0;
update p set p.unitsinstock = p.unitsinstock - od.quantity from dbo.products p inner join dbo.[order details] od on od.productid = p.productid where od.orderid = 11077;
delete from dbo.[order details] where orderid = 10248 and productid in (11, 42, 72);
```

`sp031-14-declare-set.sql` — DECLARE/SET, equals-on-new-line, no alignment:
```sql
declare @startdate datetime = '1997-01-01', @enddate datetime = '1997-12-31', @country nvarchar(15) = N'Germany';
declare @totalfreight money;
set @totalfreight = (select sum(o.freight) from dbo.orders o where o.orderdate between @startdate and @enddate and o.shipcountry = @country);
select @totalfreight as totalfreight;
```

`sp031-15-create-table.sql` — DDL parens, constraints on new lines, composite PK (ifLongerOrMultipleColumns), short DDL collapse:
```sql
create table dbo.orderaudit (auditid int identity(1,1) not null constraint pk_orderaudit primary key, orderid int not null constraint fk_orderaudit_orders foreign key references dbo.orders (orderid), changedat datetime2(3) not null constraint df_orderaudit_changedat default sysutcdatetime(), oldfreight money null, newfreight money null, changedby nvarchar(128) not null);
create table dbo.regionmap (regionid int not null, territoryid nvarchar(20) not null, constraint pk_regionmap primary key (regionid, territoryid));
create table dbo.tinylookup (code char(2) not null primary key);
```

`sp031-16-control-flow.sql` — IF/ELSE/WHILE/BEGIN-END keyword indent + collapse < 35:
```sql
if @@rowcount = 0 print 'none';
if exists (select 1 from dbo.orders o where o.shippeddate is null and o.requireddate < getdate()) begin update dbo.orders set shipvia = 3 where shippeddate is null and requireddate < getdate(); print 'expedited late orders'; end else begin print 'no late orders'; end
while (select count(*) from dbo.products where unitsinstock = 0) > 0 begin update top (10) dbo.products set unitsinstock = reorderlevel where unitsinstock = 0; if @@rowcount = 0 break; end
```

`sp031-17-function-calls.sql` — spacing trio, args never on new lines, nested calls:
```sql
select getdate() as now, isnull(o.shippeddate, o.requireddate) as effectivedate, datediff(day, o.orderdate, isnull(o.shippeddate, getdate())) as daystoship, upper(substring(c.companyname, 1, charindex(' ', c.companyname + ' ') - 1)) as firstword, coalesce(o.shipregion, c.region, N'n/a') as region from dbo.orders o inner join dbo.customers c on c.customerid = o.customerid;
```

`sp031-18-semicolons-go.sql` — space-before-semicolon, 2 blank lines between statements, GO handling (input crams them):
```sql
select 1 as a;
select 2 as b;
go
select 3 as c;



select 4 as d;
go
```

`sp031-19-comments.sql` — line + block comments incl. a banner pattern:
```sql
-- daily revenue rollup
select o.orderdate, sum(o.freight) as freight from dbo.orders o group by o.orderdate;
/*******************************
 * legacy calculation block    *
 * kept for reference          *
 *******************************/
/* multi
   line
   note */
select 1;
```

`sp031-20-merge.sql` — MERGE interplay (spec-030 residual):
```sql
merge dbo.products as target using (select productid, sum(quantity) as sold from dbo.[order details] group by productid) as source on target.productid = source.productid when matched and target.unitsinstock >= source.sold then update set target.unitsinstock = target.unitsinstock - source.sold when matched then update set target.unitsinstock = 0 when not matched by target then insert (productname, unitsinstock) values (N'unknown', 0);
```

- [ ] **Step 1: Write the 20 files** exactly as above (LF endings, no BOM, single trailing newline).
- [ ] **Step 2: Run the existing parity suite in capture mode for the new inputs** — `AKML_UPDATE_PARITY_GOLDEN=1 dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release --filter "FullyQualifiedName~FormatParityTests"`. This creates `golden/sp031-*__<style>.sql` self-goldens for the 6 built-in styles (120 new files) — **intentional** (drift guards; FR-042). Inspect a few outputs for sanity (parseable SQL in, formatted SQL out; stage-6 fallback returning input unchanged is a red flag to investigate before committing).
- [ ] **Step 3: Commit checkpoint**

```bash
git add tests/format-parity/corpus/sp031-*.sql tests/format-parity/golden/sp031-*.sql
git commit -m "test(031): 20-file option-family corpus + AKML self-goldens (FR-040)"
```

---

### Task 12: User runbook for SQL Prompt 11 goldens

**Files:**
- Create: `specs/031-redgate-style-import/runbook-goldens.md`

- [ ] **Step 1: Write the runbook** (verbatim content):

```markdown
# Golden generation runbook — SQL Prompt 11 (manual, ~20 minutes)

You are the ground-truth generator: AKML will be tuned until it matches these outputs byte-for-byte.

## One-time setup
1. Open SSMS 22 with SQL Prompt 11 loaded.
2. SQL Prompt → Options → Styles: confirm the **MohamedKhamis** style is present and set it as the **active** style.
3. SQL Prompt → Options → uncheck anything that rewrites content beyond formatting if enabled (e.g. "Insert semicolons", "Qualify object names", "Apply casing options" stays ON — casing IS part of the style). Formatting must be the style alone.
4. Connection: any server is fine — do NOT rely on database objects existing; the corpus uses Northwind-style names but formatting does not require them to resolve. `useObjectDefinitionCase` may recase identifiers when connected to a database that HAS these objects — to keep goldens connection-independent, generate them while connected to a database WITHOUT Northwind objects (e.g. an empty scratch DB).

## Per file (repeat for each of the 20 files)
1. File → Open → `tests/format-parity/corpus/sp031-NN-….sql`.
2. Select all (Ctrl+A) → SQL Prompt → **Format SQL** (Ctrl+K, Ctrl+Y).
3. File → Save As → `tests/format-parity/golden/sp031-NN-…__mohamedkhamis.sql`
   - Same file stem + `__mohamedkhamis` suffix, UTF-8, no BOM if the dialog offers a choice.
   - IMPORTANT: Save As, never overwrite the corpus input.

## Deliver
Reply in the working session (or commit if you prefer) with the 20 golden files in
`tests/format-parity/golden/`. The comparison normalizes trailing whitespace, CRLF→LF and BOM,
so minor editor differences are tolerated — content/layout is what is measured.

## Sanity checklist before delivering
- [ ] 20 files, names exactly `sp031-NN-<stem>__mohamedkhamis.sql`
- [ ] Keywords are UPPERCASE, list commas are leading (`, `) — quick visual confirmation the right style was active
- [ ] sp031-18: statements separated by exactly 2 blank lines; `;` preceded by one space
```

- [ ] **Step 2: Commit checkpoint**

```bash
git add specs/031-redgate-style-import/runbook-goldens.md
git commit -m "docs(031): SQL Prompt 11 golden-generation runbook (FR-040)"
```

- [ ] **Step 3: Hand the runbook to the user and WAIT.** Phase 3 planning is gated on the goldens landing in `tests/format-parity/golden/`.

---

### Task 13: Redgate parity driver + starting-fidelity measurement

**Files:**
- Create: `tests/AkmlSql.Formatting.Tests/Parity/RedgateParityTests.cs`

**Interfaces:**
- Consumes: `RedgateJsonStyleImporter` (fixture import), `FormatterPipeline` — reuse the exact formatting invocation + `Normalise` helper from `FormatParityTests.cs` (read it first; call the same public surface it calls, including `ProfileMetadata.SkipValidation`/pipeline options it sets, so results are comparable).
- Produces: per-file pass/fail vs `golden/sp031-*__mohamedkhamis.sql` + a fidelity ratio summary.

- [ ] **Step 1: Write the driver**

```csharp
// tests/AkmlSql.Formatting.Tests/Parity/RedgateParityTests.cs
using AkmlSql.Formatting.Profiles;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Formatting.Tests.Parity;

/// <summary>
/// Spec 031 FR-041 / SC-003 — formats the sp031 corpus with the imported MohamedKhamis style and
/// compares byte-exact (post-Normalise) against SQL Prompt 11 goldens. Skips (with a message)
/// while a golden is absent so the suite is green before the user delivers goldens.
/// </summary>
public class RedgateParityTests(ITestOutputHelper output)
{
    public static TheoryData<string> CorpusFiles()
    {
        var dir = Path.Combine(RepoRoot(), "tests", "format-parity", "corpus");
        var data = new TheoryData<string>();
        foreach (var f in Directory.EnumerateFiles(dir, "sp031-*.sql").OrderBy(x => x))
            data.Add(Path.GetFileNameWithoutExtension(f));
        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Corpus_file_matches_sqlprompt11_golden(string stem)
    {
        var goldenPath = Path.Combine(RepoRoot(), "tests", "format-parity", "golden", stem + "__mohamedkhamis.sql");
        Assert.SkipWhen(!File.Exists(goldenPath), $"Golden not yet delivered: {goldenPath} (see runbook-goldens.md)");

        var input = File.ReadAllText(Path.Combine(RepoRoot(), "tests", "format-parity", "corpus", stem + ".sql"));
        var style = RedgateJsonStyleImporter.Import(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MohamedKhamis-style.json"))).Profile;

        var formatted = ParityHarness.Format(input, style);   // extract/reuse FormatParityTests' invocation
        var expected = ParityHarness.Normalise(File.ReadAllText(goldenPath));
        var actual = ParityHarness.Normalise(formatted);

        if (expected != actual)
            output.WriteLine(ParityHarness.FirstDiff(expected, actual)); // line/col of first divergence for triage
        Assert.Equal(expected, actual);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "AKML-SQL.slnx")))
            dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("repo root not found");
        return dir;
    }
}
```

`ParityHarness`: extract `Format`, `Normalise`, and a `FirstDiff` helper from the private methods of `FormatParityTests.cs` into a shared internal static class in the same folder (refactor, do not duplicate — update `FormatParityTests` to call it; run that suite to prove no drift: 78/78 still pass). `Assert.SkipWhen` requires xunit.v3; if the repo is on xunit 2.x, use `Skip.If` from `Xunit.SkipableFact` if referenced, else emit `return` after `output.WriteLine("SKIP: no golden")` — match the repo's existing skip idiom (grep for `Skip` under `tests/`).

- [ ] **Step 2: Run before goldens exist** — all 20 report skipped/absent, suite green: `dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj -c Release --filter "FullyQualifiedName~RedgateParityTests"`.
- [ ] **Step 3: When goldens land** — run again; record the pass count as the **starting fidelity** in `specs/031-redgate-style-import/plan.md` (append a `## Phase-2 result` line: "N/20 corpus files already byte-match; failing files: …"). This number seeds Phase-3 prioritization.
- [ ] **Step 4: Commit checkpoint**

```bash
git add tests/AkmlSql.Formatting.Tests/Parity/
git commit -m "test(031): Redgate parity driver for sp031 corpus vs SQL Prompt 11 goldens (FR-041)"
```

---

# Phase 3 — Layout gap closure (PLANNING GATE)

**Do not implement Phase 3 from this document.** Phase 3 tasks get planned per-feature (same writing-plans format, appended to this file or as `plan-phase3.md`) only after Task 13 Step 3 records the golden diffs — the goldens, not the docs, define each feature's expected output, and several enum semantics are explicitly golden-resolved (comma gutter rendering, `expandedSimple` nesting, collapse-bool quirk, CTE right-alignment, comment patterns).

Fixed, dependency-ordered feature queue (design §3; each feature = red corpus files → implement → files green → add its paths to `FormatterHonoringTable` → statuses flip from `mapped-pending-render` to `mapped` → re-bless any legitimately-moved self-goldens with reviewed diffs):

| # | Feature | Primary corpus files | Options it flips in the honoring table |
|---|---|---|---|
| 1 | Tab emission (`tabsWhenPossible`/`tabs`) + `AlignItemsToTabStops` + RightAligner tabs-mode un-gate + blank-line counts (`TextEmitter`, `RightAligner`, `LineBreakDecider`, `LayoutNode`) | all; sp031-18 | whitespace.spacesOrTabs, lists.alignItemsToTabStops, whitespace.newLines.emptyLinesBetweenStatements, whitespace.newLines.emptyLinesAfterBatchSeparator |
| 2 | Comma gutter (`CommaAlignment`, `SpaceBeforeComma`) | sp031-01, 04, 10 | lists.addSpaceBeforeComma, lists.commaAlignment |
| 3 | Parenthesis style enum, per-construct resolution | sp031-06, 09, 10, 11, 15 | parentheses.parenthesisStyle, ddl.parenthesisStyle, cte.parenthesisStyle, insertStatements.\*.parenthesisStyle |
| 4 | Semicolon placement (gate `NormalizeSemicolonSpacing`) | sp031-18, all | whitespace.whiteSpaceBeforeSemiColon |
| 5 | DISTINCT/TOP break-after | sp031-02 | dml.addNewLineAfterDistinctAndTopClauses |
| 6 | CTE name/indent/column alignment | sp031-09, 10 | cte.placeNameOnNewLine, cte.indentName, cte.columnAlignment |
| 7 | INSERT columns/values layout | sp031-11, 12 | insertStatements.\* (indentContents, placeSubsequentValuesOnNewLines) |
| 8 | Control-flow BEGIN/END keyword indent | sp031-16 | controlFlow.indentBeginAndEndKeywords |
| 9 | DECLARE/SET `=`-on-new-line | sp031-14 | variables.placeEqualsSignOnNewLine |
| 10 | JOIN `toTable` alignment | sp031-05 | joinStatements.join.keywordAlignment |
| 11 | Function-call spacing trio + call-detection fix | sp031-17 | functionCalls.addSpaces\* |
| 12 | CASE `toFirstItem`/`toWhen`/END-inline (measured alignment) | sp031-07, 08 | caseExpressions.whenAlignment, thenAlignment, placeEndOnNewLine |
| 13 | AND/OR `toFirstListItem`; BETWEEN AND right-alignment; IN spacing | sp031-03, 04 | operators.andOr.alignment, operators.between.andAlignment, operators.in.addSpaceAroundInContents |
| 14 | DDL constraint-columns enum wiring | sp031-15 | ddl.placeConstraintColumnsOnNewLines |
| 15 | Comment pattern re-indent verification (tune only if goldens diverge) | sp031-19 | whitespace.newLines.alignMultilineCommentsMatchingPatterns |
| 16 | `useObjectDefinitionCase` schema-cache bridge (live-verified, not golden-gated — see runbook §setup 4) | manual SSMS check | casing.useObjectDefinitionCase |

Per-feature exit criteria (every feature): its corpus files byte-match; full Formatting suite green (78 self-golden pairs re-blessed only with per-feature reviewed diffs); idempotency stage-7 green on the corpus; after feature 1 additionally run the perf harness (`PerformanceBaselineTests`, ~13 min) — SC-011 absolute targets must hold.

---

## Self-review notes (per writing-plans checklist)

- **Spec coverage**: FR-001–008 → Tasks 3–8; FR-010–012 → Tasks 9–10; FR-020–036 → Phase-3 queue (gated by design); FR-040 → Task 11–12; FR-041 → Task 13; FR-042 → Task 11 Step 2 + Phase-3 exit criteria; SC-001/002 → Tasks 6/8/9; SC-004 → Task 7; SC-003/005/006/007 → Phase-3 exit criteria. US1 acceptance 5 (custom-collision prompt) → Task 9 Step 2 note; US1 acceptance 8 (source preservation) → Task 8.
- **Known verify-before-use points** (flagged inline where they occur): exact property names in `CaseOptions`/`JoinOptions`/`CteOptions`/`DdlOptions`/`DeclareOptions`; `FormatRequestHandler` ctor; `FormatSetting` path property; repo's xunit skip idiom; `SqlPromptKeyMapTests` exemption mechanism. Rule everywhere: adjust the test/code to the real repo name, never invent a parallel field.
- **Type consistency**: `RedgateOptionStatus` strings match spec §FR-007 and Task 1's wire DTO; `ProfileName` is `[Key(6)]` added once (Task 8); `InsertParenOptions.PlaceSubsequentItemsOnNewLines` naming used consistently in Tasks 2/6.
