# Options Dialog Phase 2 — New AppSettings, Engine Wiring, Page Split, New Pages

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend `AppSettings` with the 4 new sub-objects + Labs that the SQL Prompt pages need, wire them through the engine (`CompletionEngine`, `WildcardExpansionHandler`, `JoinOnFkProvider`), refactor `SettingsWindow.cs` from a 3,228-line monolith into per-page files (~600-line chrome host + 19 focused page files), and add the 5 missing SQL Prompt sub-pages (Suggestion Types, Qualification & Brackets, INSERT statements, JOIN completion, Labs).

**Architecture:** Phase 2 sequences as three logically-independent sub-blocks that can be merged incrementally if desired:
- **Block A — Engine + Settings (~2 days):** New `AppSettings` POCOs (additive, no UI yet); engine reads them; round-trip JSON tests; `EnginePolicyTests` integration tests. Lands as one commit.
- **Block B — Page split (~3 days):** Introduce `IPageBuilder` / `PageContext` / `RowFactory`. Refactor existing 15 pages into per-file builders one at a time. Each refactor is its own commit so regressions bisect cleanly.
- **Block C — New pages + tree integration (~2 days):** Add 5 new page files using the patterns established in Block B; surface them in the navigation tree; extend `OnResetThisPageClick` switch; add `OnResetThisPageClick_AllPagesHaveCase` regression test.

`SettingsWindow.cs` shrinks from 3,228 LoC to ~600 LoC. Each new page file is 80-250 LoC. The 4 chrome tests from Phase 1 must continue to pass throughout Block B.

**Tech Stack:** .NET Framework 4.7.2 (shell/WPF), .NET 10 (engine + tests), xunit, Xunit.StaFact (Phase 1 chrome tests), MessagePack IPC between shell and engine.

**Spec:** `docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md` §7.

**Branching:** This plan executes on a fresh branch `018-options-dialog-phase2` off `017-options-dialog-phase1` (or off `master` after Phase 1 merges). Do NOT execute on `017-*` directly — that branch is in PR review.

**Prerequisites the executor must verify before starting:**
1. Phase 1 commits are present: `git log --oneline 09c26aa..HEAD` shows `5efe39a`, `b0d20ec`, `77ab6ab`, `eacb6e5`, `9c25601`.
2. Phase 1 chrome tests pass: `dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj` shows 4 passed.
3. The user's no-auto-commit rule applies: every "Commit" step in this plan stops for explicit user approval.
4. MSBuild path: `/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe` (VS 2026 / 18.x; Phase 1 confirmed this).

---

## Pre-flight: Confirm scope is still relevant

- [ ] **Step 0.1: Sanity-check the codebase still has the expected shape**

```bash
wc -l src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
grep -c "private CheckBox\|private ComboBox\|private Slider\|private TextBox" src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
grep -n "public class IntelliSenseSettings" src/AkmlSql.Core/Config/AppSettings.cs
ls src/AkmlSql.Engine/Completion/Providers/JoinOnFkProvider.cs src/AkmlSql.Engine/Completion/WildcardExpansionHandler.cs
```

Expected (within ±10%):
- `SettingsWindow.cs`: ~3,228 lines
- 106 control fields
- `IntelliSenseSettings` class exists at line ~156 of `AppSettings.cs`
- Both engine integration files exist

If shapes have shifted dramatically, **stop and report** — this plan was written against the 2026-05-07 codebase and may need updating.

- [ ] **Step 0.2: Confirm no in-flight uncommitted work**

```bash
git status --short
```

Expected: empty (or just untracked plan files in `docs/superpowers/`). If there are unstaged changes in `src/`, **stop** — don't risk losing them.

---

# BLOCK A — New AppSettings + Engine Wiring

Block A delivers no UI changes. It adds the data model and engine plumbing so that Block C's UI pages have something real to bind to. **Strict TDD here:** write the round-trip / integration test first, watch it fail, implement.

## Task A.1: Add new AppSettings POCOs (no UI yet)

**Files:**
- Modify: `src/AkmlSql.Core/Config/AppSettings.cs`
- Test: `tests/AkmlSql.Engine.Tests/Config/SettingsImportExportTests.cs` (NEW)

- [ ] **Step A.1.1: Read the existing `IntelliSenseSettings` to understand the extension point**

Read `src/AkmlSql.Core/Config/AppSettings.cs` lines 156-208. Note the existing fields (`Enabled`, `AutoTrigger`, `AutoAlias`, `JoinAssist`, etc.) — these stay. We're adding new sub-objects.

- [ ] **Step A.1.2: Write the failing round-trip test**

If `tests/AkmlSql.Engine.Tests/Config/` doesn't exist, create it. Then create `tests/AkmlSql.Engine.Tests/Config/SettingsImportExportTests.cs`:

```csharp
using System.Text.Json;
using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Engine.Tests.Config
{
    public class SettingsImportExportTests
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        [Fact]
        public void RoundTrip_NewSubObjects_PreservesValues()
        {
            var input = new AppSettings();
            input.IntelliSense.SuggestionTypes.IncludeSystemObjects = true;
            input.IntelliSense.SuggestionTypes.IncludeKeywords = false;
            input.IntelliSense.SuggestionTypes.ColumnScope = ColumnSuggestionScope.All;
            input.IntelliSense.Qualification.SchemaMode = SchemaQualifyMode.Always;
            input.IntelliSense.Qualification.BracketMode = BracketMode.Always;
            input.IntelliSense.Qualification.QualifyColumnsWithTableOrAlias = false;
            input.IntelliSense.InsertOptions.IncludeColumns = false;
            input.IntelliSense.InsertOptions.IncludeDefaultsAsComments = false;
            input.IntelliSense.InsertOptions.IncludeProcParamInfo = false;
            input.IntelliSense.JoinOptions.MatchByColumnName = false;
            input.Labs.GhostTextCompletion = true;
            input.Labs.ParallelSchemaCache = true;

            var json = JsonSerializer.Serialize(input, Options);
            var roundTripped = JsonSerializer.Deserialize<AppSettings>(json, Options)!;

            Assert.True(roundTripped.IntelliSense.SuggestionTypes.IncludeSystemObjects);
            Assert.False(roundTripped.IntelliSense.SuggestionTypes.IncludeKeywords);
            Assert.Equal(ColumnSuggestionScope.All, roundTripped.IntelliSense.SuggestionTypes.ColumnScope);
            Assert.Equal(SchemaQualifyMode.Always, roundTripped.IntelliSense.Qualification.SchemaMode);
            Assert.Equal(BracketMode.Always, roundTripped.IntelliSense.Qualification.BracketMode);
            Assert.False(roundTripped.IntelliSense.Qualification.QualifyColumnsWithTableOrAlias);
            Assert.False(roundTripped.IntelliSense.InsertOptions.IncludeColumns);
            Assert.False(roundTripped.IntelliSense.InsertOptions.IncludeDefaultsAsComments);
            Assert.False(roundTripped.IntelliSense.InsertOptions.IncludeProcParamInfo);
            Assert.False(roundTripped.IntelliSense.JoinOptions.MatchByColumnName);
            Assert.True(roundTripped.Labs.GhostTextCompletion);
            Assert.True(roundTripped.Labs.ParallelSchemaCache);
        }

        [Fact]
        public void Deserialize_OldConfigMissingNewFields_DefaultsCleanly()
        {
            // An old config.json from before Phase 2: only has the existing fields.
            var oldJson = @"{
                ""intelliSense"": {
                    ""enabled"": true,
                    ""autoTrigger"": true,
                    ""joinAssist"": true,
                    ""autoAlias"": false,
                    ""maxSuggestions"": 50
                }
            }";

            var settings = JsonSerializer.Deserialize<AppSettings>(oldJson, Options)!;

            // Old fields preserved
            Assert.True(settings.IntelliSense.Enabled);
            Assert.True(settings.IntelliSense.JoinAssist);
            Assert.False(settings.IntelliSense.AutoAlias);

            // New fields default-construct
            Assert.NotNull(settings.IntelliSense.SuggestionTypes);
            Assert.NotNull(settings.IntelliSense.Qualification);
            Assert.NotNull(settings.IntelliSense.InsertOptions);
            Assert.NotNull(settings.IntelliSense.JoinOptions);
            Assert.NotNull(settings.Labs);

            // Defaults match the spec (§7.2)
            Assert.False(settings.IntelliSense.SuggestionTypes.IncludeSystemObjects);
            Assert.True(settings.IntelliSense.SuggestionTypes.IncludeKeywords);
            Assert.Equal(ColumnSuggestionScope.ReferencedOnly, settings.IntelliSense.SuggestionTypes.ColumnScope);
            Assert.Equal(SchemaQualifyMode.NonDefaultOnly, settings.IntelliSense.Qualification.SchemaMode);
            Assert.Equal(BracketMode.WhenRequired, settings.IntelliSense.Qualification.BracketMode);
            Assert.True(settings.IntelliSense.InsertOptions.IncludeColumns);
            Assert.True(settings.IntelliSense.JoinOptions.MatchByColumnName);
            Assert.False(settings.Labs.GhostTextCompletion);
        }
    }
}
```

- [ ] **Step A.1.3: Run the test — it must FAIL (types don't exist yet)**

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName~SettingsImportExportTests" -v normal
```

Expected: compile error — `IntelliSense.SuggestionTypes` doesn't exist, `ColumnSuggestionScope` enum doesn't exist, etc.

If the test compiles and runs, you've already added the types — go to A.1.5 to verify behavior.

- [ ] **Step A.1.4: Add the new POCOs to `src/AkmlSql.Core/Config/AppSettings.cs`**

In `IntelliSenseSettings` (currently ends around line 208), add four new sub-object properties as the FIRST four properties of the class (before `Enabled`):

```csharp
[JsonPropertyName("suggestionTypes")]
public SuggestionTypesSettings SuggestionTypes { get; set; } = new();

[JsonPropertyName("qualification")]
public QualificationSettings Qualification { get; set; } = new();

[JsonPropertyName("insertOptions")]
public InsertOptionsSettings InsertOptions { get; set; } = new();

[JsonPropertyName("joinOptions")]
public JoinOptionsSettings JoinOptions { get; set; } = new();
```

Then, AFTER the closing `}` of `IntelliSenseSettings` (around line 208), add these new classes:

```csharp
public enum ColumnSuggestionScope { All, ReferencedOnly }
public enum SchemaQualifyMode { Always, NonDefaultOnly, Never }
public enum BracketMode { Always, WhenRequired, Never }

/// <summary>Which categories of database objects appear in the suggestion list.</summary>
public class SuggestionTypesSettings
{
    [JsonPropertyName("includeSystemObjects")]
    public bool IncludeSystemObjects { get; set; }

    [JsonPropertyName("suggestAllColumnsAfterSelect")]
    public bool SuggestAllColumnsAfterSelect { get; set; }

    [JsonPropertyName("columnScope")]
    public ColumnSuggestionScope ColumnScope { get; set; } = ColumnSuggestionScope.ReferencedOnly;

    [JsonPropertyName("includeKeywords")]
    public bool IncludeKeywords { get; set; } = true;
}

/// <summary>How object names are formatted when inserted from the suggestion list.</summary>
public class QualificationSettings
{
    [JsonPropertyName("schemaMode")]
    public SchemaQualifyMode SchemaMode { get; set; } = SchemaQualifyMode.NonDefaultOnly;

    [JsonPropertyName("bracketMode")]
    public BracketMode BracketMode { get; set; } = BracketMode.WhenRequired;

    [JsonPropertyName("qualifyColumnsWithTableOrAlias")]
    public bool QualifyColumnsWithTableOrAlias { get; set; } = true;
}

/// <summary>What metadata is inserted when writing INSERT INTO statements.</summary>
public class InsertOptionsSettings
{
    [JsonPropertyName("includeColumns")]
    public bool IncludeColumns { get; set; } = true;

    [JsonPropertyName("includeDefaultsAsComments")]
    public bool IncludeDefaultsAsComments { get; set; } = true;

    [JsonPropertyName("includeProcParamInfo")]
    public bool IncludeProcParamInfo { get; set; } = true;
}

/// <summary>JOIN completion behavior.</summary>
public class JoinOptionsSettings
{
    [JsonPropertyName("matchByColumnName")]
    public bool MatchByColumnName { get; set; } = true;
}
```

Find the top-level `AppSettings` class (around line 15). Add a new property:

```csharp
[JsonPropertyName("labs")]
public LabsSettings Labs { get; set; } = new();
```

After the closing `}` of `AppSettings`, add the `LabsSettings` POCO:

```csharp
/// <summary>
/// Experimental / preview feature flags. Per-feature opt-ins for in-flight work.
/// Labs entries may change or be removed without notice; production code reads
/// these to gate ghost-text AI completion, parallel schema cache, etc.
/// </summary>
public class LabsSettings
{
    [JsonPropertyName("ghostTextCompletion")]
    public bool GhostTextCompletion { get; set; }

    [JsonPropertyName("parallelSchemaCache")]
    public bool ParallelSchemaCache { get; set; }

    [JsonPropertyName("sharedSnippetSync")]
    public bool SharedSnippetSync { get; set; }
}
```

- [ ] **Step A.1.5: Run the test — it must PASS**

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName~SettingsImportExportTests" -v normal
```

Expected: 2 passed.

If `RoundTrip_NewSubObjects_PreservesValues` fails because of casing, the `JsonPropertyName` attributes on the new properties don't match the test's casing assumption — fix the attributes to match.

If `Deserialize_OldConfigMissingNewFields_DefaultsCleanly` fails because new fields are null instead of default-constructed, the `= new()` initializer is missing somewhere.

- [ ] **Step A.1.6: Build the engine and Core to confirm no break**

```bash
dotnet build src/AkmlSql.Core/AkmlSql.Core.csproj -c Release
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release
```

Expected: 0 errors. Same for `dotnet build` on Engine.Tests.

- [ ] **Step A.1.7: Prepare commit**

```bash
git add src/AkmlSql.Core/Config/AppSettings.cs tests/AkmlSql.Engine.Tests/Config/SettingsImportExportTests.cs
```

Suggested message:

```
Add AppSettings POCOs for SQL Prompt parity sub-pages (Phase 2 Block A)

Adds 4 new sub-objects to IntelliSenseSettings + a new top-level
LabsSettings, with associated enums (ColumnSuggestionScope,
SchemaQualifyMode, BracketMode):

- SuggestionTypes: which categories of DB objects appear in completions
- Qualification: schema/bracket policies for inserted code
- InsertOptions: column lists / type comments / default comments in INSERT
- JoinOptions: JOIN completion matching strategy

All fields default-construct with values matching the SQL Prompt
defaults documented in spec §7.2. Old config.json files from before
Phase 2 deserialize cleanly with the new fields auto-populated.

Round-trip and back-compat tests added in
tests/AkmlSql.Engine.Tests/Config/SettingsImportExportTests.cs.

No UI yet — Block C of Phase 2 wires these to options pages. No engine
behavior change yet — Tasks A.2-A.4 wire engine to read them.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §7.2
      docs/superpowers/plans/2026-05-07-options-dialog-phase2.md Block A.1
```

**Ask the user:** "AppSettings additions ready. Approve commit?"

---

## Task A.2: Wire `CompletionEngine` to read SuggestionTypes + Qualification

**Files:**
- Modify: `src/AkmlSql.Engine/Completion/CompletionEngine.cs`
- Modify: `src/AkmlSql.Engine/Completion/Providers/ObjectProvider.cs` (or wherever system-object filtering lives)
- Modify: `src/AkmlSql.Engine/Completion/Providers/KeywordProvider.cs`
- Test: `tests/AkmlSql.Engine.Tests/Completion/EnginePolicyTests.cs` (NEW)

- [ ] **Step A.2.1: Recon — find where the engine reads `IntelliSenseSettings`**

```bash
grep -rn "IntelliSenseSettings\|appSettings.IntelliSense\|settings.IntelliSense" src/AkmlSql.Engine/ | head -20
```

Note the integration points. The completion path likely reads settings in `CompletionEngine.cs` (or a helper) once per request and passes the relevant flags to providers.

If no central settings-read site exists, identify the request handler that has access to `AppSettings` (probably `CompletionRequestHandler` or similar). The engine wiring tasks add reads there.

- [ ] **Step A.2.2: Write failing tests for the new flags**

Create `tests/AkmlSql.Engine.Tests/Completion/EnginePolicyTests.cs`:

```csharp
using System.Linq;
using AkmlSql.Core.Config;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Schema;
using Xunit;

namespace AkmlSql.Engine.Tests.Completion
{
    public class EnginePolicyTests
    {
        [Fact]
        public void IncludeKeywords_False_ExcludesKeywordsFromCompletions()
        {
            // Arrange: minimal CompletionEngine setup with IncludeKeywords = false.
            var settings = new AppSettings();
            settings.IntelliSense.SuggestionTypes.IncludeKeywords = false;
            var engine = TestEngineFactory.CreateWithSettings(settings);

            // Act: get completions at top of an empty document.
            var result = engine.GetCompletions("", caretPosition: 0);

            // Assert: no keyword items.
            Assert.DoesNotContain(result.Items, item => item.Kind == "keyword");
        }

        [Fact]
        public void IncludeSystemObjects_False_ExcludesSystemProcs()
        {
            var settings = new AppSettings();
            settings.IntelliSense.SuggestionTypes.IncludeSystemObjects = false;
            var engine = TestEngineFactory.CreateWithSettings(settings);

            // Stage a schema cache that includes a system proc named "sp_help".
            engine.Schema.AddProcedure("sys", "sp_help", isMsShipped: true);

            var result = engine.GetCompletions("EXEC ", caretPosition: 5);

            Assert.DoesNotContain(result.Items, item => item.Label == "sp_help");
        }

        [Fact]
        public void Qualification_SchemaModeAlways_PrefixesAllObjects()
        {
            var settings = new AppSettings();
            settings.IntelliSense.Qualification.SchemaMode = SchemaQualifyMode.Always;
            var engine = TestEngineFactory.CreateWithSettings(settings);

            engine.Schema.AddTable("dbo", "Customers");

            var result = engine.GetCompletions("SELECT * FROM Cust", caretPosition: 18);
            var customers = result.Items.Single(i => i.Label == "Customers");

            // Insert text should be prefixed: "dbo.Customers".
            Assert.Equal("dbo.Customers", customers.InsertText);
        }
    }
}
```

`TestEngineFactory.CreateWithSettings` is a helper to construct a `CompletionEngine` with a specific `AppSettings`. If it doesn't exist, find an analogous helper in existing engine tests (e.g., look for `new CompletionEngine` calls in `tests/AkmlSql.Engine.Tests/`) and adapt.

If schema-staging methods (`AddProcedure`, `AddTable`) don't exist on the test engine's schema, find the existing test schema-cache pattern and use it.

- [ ] **Step A.2.3: Run tests — they must FAIL**

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName~EnginePolicyTests"
```

Expected: 3 failures (the engine doesn't yet read the new flags).

- [ ] **Step A.2.4: Implement engine wiring**

Three changes:

**(a) `KeywordProvider`:** add a check at the top of its main provider method (probably `GetCompletionsAsync` or similar). Read `appSettings.IntelliSense.SuggestionTypes.IncludeKeywords` — if false, return an empty result.

Find where `KeywordProvider` accesses settings. If it doesn't currently, add an `AppSettings settings` constructor parameter and store it as a field. Make sure the `CompletionEngine` passes its settings reference when constructing providers.

**(b) `ObjectProvider`:** filter out system objects when `IncludeSystemObjects = false`. The schema cache likely tracks `IsMsShipped` or similar — use it to filter.

**(c) `CompletionEngine` qualification logic:** find where `InsertText` is set for object completions (table, view, function, proc). Add a helper:

```csharp
private string QualifyName(string schema, string name)
{
    return _settings.IntelliSense.Qualification.SchemaMode switch
    {
        SchemaQualifyMode.Always => $"{schema}.{name}",
        SchemaQualifyMode.NonDefaultOnly when schema != "dbo" => $"{schema}.{name}",
        _ => name,
    };
}
```

Replace existing object-completion `InsertText = item.Name` with `InsertText = QualifyName(item.Schema, item.Name)`.

For brackets, defer that to a future ticket — `BracketMode.WhenRequired` is the safe default. (TODO: full BracketMode handling can land in a Phase 2 follow-up if needed; the test for it isn't in this task.)

- [ ] **Step A.2.5: Run tests — they must PASS**

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName~EnginePolicyTests"
```

Expected: 3 passed.

If a test fails because the schema cache test helper API doesn't match, adjust the test setup to match the real API. Don't soften assertions to make tests pass.

- [ ] **Step A.2.6: Run the full Engine test suite to confirm no regression**

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
```

Expected: all pre-existing tests still pass.

- [ ] **Step A.2.7: Prepare commit**

```bash
git add src/AkmlSql.Engine/ tests/AkmlSql.Engine.Tests/Completion/EnginePolicyTests.cs
```

Suggested message:

```
Wire CompletionEngine to honor new IntelliSense settings (Phase 2 A.2)

Three new flags now affect completions:
- SuggestionTypes.IncludeKeywords: KeywordProvider returns empty when off
- SuggestionTypes.IncludeSystemObjects: ObjectProvider filters
  is_ms_shipped objects when off
- Qualification.SchemaMode: object completions are now prefixed with
  schema name per the SchemaQualifyMode enum (Always / NonDefaultOnly /
  Never), replacing the legacy unconditional unqualified InsertText

BracketMode handling is deferred — current behavior is "WhenRequired"
(safe default; the Engine emits brackets only when the identifier
contains reserved characters or matches a keyword).

EnginePolicyTests covers all three new flags end-to-end with a real
CompletionEngine and a stubbed schema cache.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §7.3
      docs/superpowers/plans/2026-05-07-options-dialog-phase2.md A.2
```

**Ask the user:** "Engine wiring (CompletionEngine + KeywordProvider + ObjectProvider) ready. Approve commit?"

---

## Task A.3: Wire `WildcardExpansionHandler` to read InsertOptions

**Files:**
- Modify: `src/AkmlSql.Engine/Completion/WildcardExpansionHandler.cs`
- Test: `tests/AkmlSql.Engine.Tests/Completion/WildcardExpansionHandlerTests.cs` (existing — extend)

- [ ] **Step A.3.1: Recon `WildcardExpansionHandler`**

```bash
grep -n "FormatterSettings\|InsertColumns\|class WildcardExpansionHandler" src/AkmlSql.Engine/Completion/WildcardExpansionHandler.cs
```

Note where it currently reads settings (likely `FormatterSettings.InsertColumnsIncludeTypes`). After Block A, it should read `InsertOptionsSettings.*` instead — but the legacy flag stays for back-compat. If both are present, the new flag wins.

- [ ] **Step A.3.2: Read the existing tests**

```bash
cat tests/AkmlSql.Engine.Tests/Completion/WildcardExpansionHandlerTests.cs
```

Identify the test helper pattern (likely takes `AppSettings` or specific flags). Use it.

- [ ] **Step A.3.3: Write failing tests for the new InsertOptions fields**

Add three new tests to the existing file:

```csharp
[Fact]
public void IncludeColumns_False_ExpandsToAsterisk()
{
    var settings = new AppSettings();
    settings.IntelliSense.InsertOptions.IncludeColumns = false;
    var handler = new WildcardExpansionHandler(settings);

    var schema = TestSchemas.WithTable("dbo", "Customers", "Id INT", "Name NVARCHAR(50)");
    var result = handler.Expand("SELECT * FROM dbo.Customers", caretPos: 7, schema);

    // With IncludeColumns=false, the expansion should leave * as-is (no per-column rewrite).
    Assert.Null(result.ExpandedSql);
}

[Fact]
public void IncludeDefaultsAsComments_False_OmitsDefaultCommentsFromInsert()
{
    var settings = new AppSettings();
    settings.IntelliSense.InsertOptions.IncludeColumns = true;
    settings.IntelliSense.InsertOptions.IncludeDefaultsAsComments = false;
    var handler = new WildcardExpansionHandler(settings);

    var schema = TestSchemas.WithTable("dbo", "Customers",
        "Id INT NOT NULL DEFAULT(0)", "Name NVARCHAR(50) NULL");
    var result = handler.Expand("SELECT * FROM dbo.Customers", caretPos: 7, schema);

    Assert.NotNull(result.ExpandedSql);
    Assert.DoesNotContain("DEFAULT", result.ExpandedSql, System.StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void IncludeProcParamInfo_False_OmitsParamCommentsFromExec()
{
    // Similar pattern for EXEC expansion of stored proc params.
    var settings = new AppSettings();
    settings.IntelliSense.InsertOptions.IncludeProcParamInfo = false;
    var handler = new WildcardExpansionHandler(settings);

    var schema = TestSchemas.WithProcedure("dbo", "GetCustomer",
        "@id INT", "@name NVARCHAR(50)");
    var result = handler.Expand("EXEC dbo.GetCustomer", caretPos: 20, schema);

    if (result.ExpandedSql != null)
    {
        Assert.DoesNotContain("--", result.ExpandedSql);
    }
}
```

If the existing test file uses different names (`Expand` vs `Process`, `result.Sql` vs `result.ExpandedSql`), adapt these stubs to match. The asserted *behavior* — IncludeColumns gates column-list expansion; IncludeDefaults gates DEFAULT comments; IncludeProcParamInfo gates `--` comments on params — is what matters.

- [ ] **Step A.3.4: Run tests — must FAIL**

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName~WildcardExpansionHandlerTests"
```

Expected: 3 new tests fail; pre-existing tests still pass.

- [ ] **Step A.3.5: Update `WildcardExpansionHandler` to read InsertOptions**

In the handler, find the column-expansion branch:
- If `settings.IntelliSense.InsertOptions.IncludeColumns == false`, short-circuit and return null/no-expansion.
- When emitting per-column comments, skip the `DEFAULT(...)` portion if `IncludeDefaultsAsComments == false`.
- When emitting EXEC param comments, skip param comments entirely if `IncludeProcParamInfo == false`.

If the handler currently reads `FormatterSettings.InsertColumnsIncludeTypes`, leave that path intact for back-compat — new InsertOptions flags override only when explicitly set. (Since they default-construct, the defaults are the same as the legacy behavior.)

- [ ] **Step A.3.6: Run tests — must PASS**

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName~WildcardExpansionHandlerTests"
```

Expected: all green.

- [ ] **Step A.3.7: Prepare commit**

```bash
git add src/AkmlSql.Engine/Completion/WildcardExpansionHandler.cs tests/AkmlSql.Engine.Tests/Completion/WildcardExpansionHandlerTests.cs
```

Suggested message:

```
Wire WildcardExpansionHandler to InsertOptionsSettings (Phase 2 A.3)

InsertOptions.IncludeColumns gates column-list expansion entirely.
InsertOptions.IncludeDefaultsAsComments gates the DEFAULT() suffix in
column comments. InsertOptions.IncludeProcParamInfo gates the --comment
suffix on EXEC parameters.

Legacy FormatterSettings.InsertColumnsIncludeTypes path retained for
back-compat — its behavior is preserved when InsertOptions defaults are
unchanged.

Three integration tests added.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §7.3
      docs/superpowers/plans/2026-05-07-options-dialog-phase2.md A.3
```

**Ask the user:** "WildcardExpansionHandler wiring ready. Approve commit?"

---

## Task A.4: Wire `JoinOnFkProvider` to read JoinOptions

**Files:**
- Modify: `src/AkmlSql.Engine/Completion/Providers/JoinOnFkProvider.cs`
- Test: `tests/AkmlSql.Engine.Tests/Completion/JoinOnFkProviderTests.cs` (existing — extend or NEW if missing)

- [ ] **Step A.4.1: Recon**

```bash
grep -n "MatchByColumnName\|class JoinOnFkProvider" src/AkmlSql.Engine/Completion/Providers/JoinOnFkProvider.cs
ls tests/AkmlSql.Engine.Tests/Completion/ | grep -i join
```

If a test file exists, extend it. Otherwise create `tests/AkmlSql.Engine.Tests/Completion/JoinOnFkProviderTests.cs`.

- [ ] **Step A.4.2: Write failing test**

```csharp
[Fact]
public void MatchByColumnName_False_NoFallbackJoinSuggestionsWhenNoFk()
{
    var settings = new AppSettings();
    settings.IntelliSense.JoinOptions.MatchByColumnName = false;
    var provider = new JoinOnFkProvider(settings);

    // Two tables with a same-named column ("Id") but no FK between them.
    var schema = TestSchemas
        .WithTable("dbo", "Orders", "Id INT", "Total DECIMAL(10,2)")
        .WithTable("dbo", "Audit",  "Id INT", "Action NVARCHAR(50)");

    var suggestions = provider.GetJoinOnSuggestions("Orders", "Audit", schema);

    // With MatchByColumnName=false: no FK exists, no fallback by-name suggestion.
    Assert.Empty(suggestions);
}

[Fact]
public void MatchByColumnName_True_FallsBackToColumnNameMatch()
{
    var settings = new AppSettings();
    settings.IntelliSense.JoinOptions.MatchByColumnName = true;  // default
    var provider = new JoinOnFkProvider(settings);

    var schema = TestSchemas
        .WithTable("dbo", "Orders", "Id INT", "CustomerId INT")
        .WithTable("dbo", "Audit",  "Id INT", "Action NVARCHAR(50)");

    var suggestions = provider.GetJoinOnSuggestions("Orders", "Audit", schema);

    // No FK, but matching "Id" column → fallback suggestion exists.
    Assert.Contains(suggestions, s => s.Sql.Contains("Id = "));
}
```

- [ ] **Step A.4.3: Run tests — must FAIL**

Pre-existing tests still pass; new tests fail because flag is unread.

- [ ] **Step A.4.4: Update `JoinOnFkProvider` to honor `MatchByColumnName`**

Find the fallback branch (where there's no FK and the provider falls back to matching column names). Wrap it in:

```csharp
if (_settings.IntelliSense.JoinOptions.MatchByColumnName)
{
    // existing fallback logic
}
// else: return no suggestions
```

If `JoinOnFkProvider` doesn't currently take settings, add a constructor parameter and update the engine's provider construction to pass it.

- [ ] **Step A.4.5: Run tests — must PASS**

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName~JoinOnFkProviderTests"
```

- [ ] **Step A.4.6: Run the full Engine + Engine.Tests suite**

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
```

Expected: all green. If anything that wasn't touched is failing, escalate — there may be a settings-construction site you missed.

- [ ] **Step A.4.7: Prepare commit**

```bash
git add src/AkmlSql.Engine/Completion/Providers/JoinOnFkProvider.cs tests/AkmlSql.Engine.Tests/Completion/JoinOnFkProviderTests.cs
```

Suggested message:

```
Wire JoinOnFkProvider to JoinOptions.MatchByColumnName (Phase 2 A.4)

When MatchByColumnName=false, the provider no longer falls back to
column-name-based JOIN suggestions in the absence of a foreign key.
Default true preserves existing behavior.

Two integration tests cover both polarities.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §7.3
      docs/superpowers/plans/2026-05-07-options-dialog-phase2.md A.4
```

**Ask the user:** "JoinOnFkProvider wiring ready. Approve commit?"

**Block A complete.** All AppSettings additions are in place and the engine reads them. UI has not been touched yet.

---

# BLOCK B — Page Split Refactor

Block B refactors `SettingsWindow.cs` from a 3,228-line monolith into a thin chrome host plus 15 per-page files. **No behavior changes** — every existing setting must continue to load and save identically. The 4 chrome tests from Phase 1 are the regression net; they MUST stay green throughout Block B.

## Task B.1: Introduce page-split foundation (`IPageBuilder`, `PageContext`, `RowFactory`)

**Files:**
- Create: `src/AkmlSql.Shell.Shared/Dialogs/Pages/IPageBuilder.cs`
- Create: `src/AkmlSql.Shell.Shared/Dialogs/Pages/PageContext.cs`
- Create: `src/AkmlSql.Shell.Shared/Dialogs/Pages/RowFactory.cs`
- Create: `src/AkmlSql.Shell.Shared/Dialogs/Pages/PageControls.cs`

This task introduces the abstraction without using it yet — Tasks B.2+ migrate pages onto it.

- [ ] **Step B.1.1: Create `IPageBuilder.cs`**

```csharp
using System.Windows;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Builds one page of the Options dialog. Implementations are stateless —
    /// each Build call produces a fresh UIElement and a corresponding
    /// IPageControls for Save/Load.
    /// </summary>
    internal interface IPageBuilder
    {
        /// <summary>The page key used as TreeViewItem.Tag and in Reset/Search lookups.</summary>
        string Key { get; }

        /// <summary>Display label shown in the page header (breadcrumb format).</summary>
        string Display { get; }

        /// <summary>Constructs the WPF panel + a controls bag the host uses to load/save.</summary>
        (UIElement Element, IPageControls Controls) Build(PageContext ctx);
    }

    /// <summary>
    /// Per-page handle for loading settings into / saving settings from the page's
    /// WPF controls. Each page implementation provides its own concrete record.
    /// </summary>
    internal interface IPageControls
    {
        void Load(AppSettings settings);
        void Save(AppSettings settings);
        void Reset(AppSettings defaults);
    }
}
```

- [ ] **Step B.1.2: Create `PageContext.cs`**

```csharp
using System;
using System.Windows;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Carries the per-build context a page needs: theme brushes, settings reference,
    /// the row factory (for zebra striping), and the search-registration callback.
    /// </summary>
    internal sealed class PageContext
    {
        public PageContext(
            object theme,
            AppSettings settings,
            RowFactory rows,
            Action<string, string, string, FrameworkElement> registerSearch)
        {
            Theme = theme;
            Settings = settings;
            Rows = rows;
            RegisterSearch = registerSearch;
        }

        /// <summary>The active theme brush set (boxed; concrete type lives in SettingsWindow).</summary>
        public object Theme { get; }

        public AppSettings Settings { get; }

        public RowFactory Rows { get; }

        /// <summary>
        /// Registers a setting in the search index. (label, description, kind, row).
        /// </summary>
        public Action<string, string, string, FrameworkElement> RegisterSearch { get; }
    }
}
```

The `Theme` is boxed as `object` because `ThemeBrushSet` is a private nested type inside `SettingsWindow`. Tasks B.2+ will surface this concretely or move `ThemeBrushSet` to its own file. For now, page builders cast back via a known accessor — see Task B.2 for the pattern.

Alternative: lift `ThemeBrushSet` to a top-level type in `Pages/`. **Recommended** — do this in Task B.1 to avoid casting in every page. Move `ThemeBrushSet` from `SettingsWindow.cs` to `Pages/PageTheme.cs` and rename to `PageTheme`. Update `SettingsWindow.cs` to use the new type. (One extra step here saves cleanup churn later.)

If you take that path, `PageContext.Theme` becomes `PageTheme Theme` directly. Recommended.

- [ ] **Step B.1.3: Create `RowFactory.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Single source of truth for option-row WPF construction. Each Add* method
    /// returns a Border (zebra-striped via the row counter) containing a labeled
    /// control; the Border itself can be flashed or scrolled-to by the search popup.
    /// </summary>
    internal sealed class RowFactory
    {
        private int _zebraIndex;
        private readonly PageTheme _theme;

        public RowFactory(PageTheme theme)
        {
            _theme = theme;
        }

        public (Border Row, CheckBox Control) AddToggle(StackPanel parent, string label, string description = "")
        {
            // Mirror the existing AddToggle implementation in SettingsWindow.cs.
            // Use _theme to pick row alt-bg per (_zebraIndex++ % 2).
            // Wire the description as a tooltip + a small caption beneath the label.
            // Return the Border + the CheckBox.
            // ...implementation copied verbatim from SettingsWindow.AddToggle, parameterized on _theme...
        }

        public (Border Row, ComboBox Control) AddDropdown(StackPanel parent, string label, string[] items, string description = "")
        {
            // mirror existing AddDropdown
        }

        public (Border Row, Slider Control, TextBlock ValueLabel) AddSlider(StackPanel parent, string label, double min, double max, double tickFrequency, string description = "")
        {
            // mirror existing AddSlider
        }

        public (Border Row, TextBox Control) AddTextBox(StackPanel parent, string label, string description = "")
        {
            // mirror existing AddTextBox
        }

        public Border AddInfoRow(StackPanel parent, string label, string value)
        {
            // mirror existing AddInfoRow
        }

        public Border AddReadOnlyField(StackPanel parent, string label, string value)
        {
            // mirror existing AddReadOnlyField
        }

        public void AddGroupHeader(StackPanel parent, string text)
        {
            // mirror existing AddGroupHeader
        }

        public void AddGroupSeparator(StackPanel parent)
        {
            // mirror existing AddGroupSeparator
        }

        // ResetZebra is called by SettingsWindow at the start of each page build.
        public void ResetZebra() => _zebraIndex = 0;
    }
}
```

The implementations are mechanical copies of the existing methods in `SettingsWindow.cs` (search for `AddToggle`, `AddDropdown`, etc.) — paste them in, replacing `_theme` field access with the `_theme` constructor parameter, replacing `_zebraIndex` field access with `_zebraIndex` field on `RowFactory`.

- [ ] **Step B.1.4: Lift `ThemeBrushSet` → `PageTheme` (separate file)**

Cut the entire `ThemeBrushSet` nested class from `SettingsWindow.cs` (lines ~35-115 — visual scan: it's the class with all the `Brush` properties). Paste into a new `src/AkmlSql.Shell.Shared/Dialogs/Pages/PageTheme.cs` and rename to `PageTheme`. Update its access modifier to `internal sealed`.

In `SettingsWindow.cs`, change all `ThemeBrushSet` references to `PageTheme`. Add `using AkmlSql.Shell.Shared.Dialogs.Pages;` if needed.

Build the SSMS22 project to verify nothing broke:

```bash
"/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
```

Expected: 0 errors.

- [ ] **Step B.1.5: Run all chrome tests — must PASS**

```bash
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj
```

Expected: 4/4 passing. Block B introduces no behavior change yet.

- [ ] **Step B.1.6: Prepare commit**

```bash
git add src/AkmlSql.Shell.Shared/Dialogs/Pages/ src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Suggested message:

```
Add page-split foundation: IPageBuilder, PageContext, RowFactory, PageTheme

No behavior change. Introduces the abstractions Tasks B.2+ will use to
migrate the 15 existing page builders out of SettingsWindow.cs:

- IPageBuilder: Build(PageContext) → (UIElement, IPageControls)
- PageContext: theme + settings + RowFactory + search-register callback
- RowFactory: AddToggle/AddDropdown/AddSlider/AddTextBox/etc. with
  zebra striping via instance counter (no longer a SettingsWindow field)
- PageTheme: lifted from SettingsWindow's nested ThemeBrushSet to a
  top-level type so page files can take it as a field without cross-
  cutting dependencies

All 4 chrome tests still pass; SettingsWindow continues to host its
existing 15 page builders inline. Migration begins in B.2.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §5, §7.1
      docs/superpowers/plans/2026-05-07-options-dialog-phase2.md B.1
```

**Ask the user:** "Page-split foundation ready. Approve commit?"

---

## Task B.2: Migrate the smallest page (Snippets) as the template

**Files:**
- Create: `src/AkmlSql.Shell.Shared/Dialogs/Pages/SnippetsPage.cs`
- Modify: `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` (remove `BuildSnippetsPage` + 5 control fields)

The smallest page (Snippets has ~7 controls) is the lowest-risk first migration. The pattern proven here is then mechanical for the other 14.

- [ ] **Step B.2.1: Read the existing `BuildSnippetsPage`**

```bash
grep -n "BuildSnippetsPage\|_chkSnipEnabled\|_chkSnipShowInCompletion" src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Read the method body. Note: control field declarations are at the top of `SettingsWindow` (around line 200); `LoadSettingsToControls` reads from settings → controls (around line 2700); `SaveControlsToSettings` writes controls → settings (around line 2880); `OnResetThisPageClick` has a `case "Snippets":` arm.

- [ ] **Step B.2.2: Create `SnippetsPage.cs`**

```csharp
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class SnippetsPage : IPageBuilder
    {
        public string Key => "Snippets";
        public string Display => "Snippets";

        public (UIElement Element, IPageControls Controls) Build(PageContext ctx)
        {
            var panel = new StackPanel { Margin = new Thickness(20, 18, 28, 18) };
            ctx.Rows.ResetZebra();

            // Page header: ctx.RowFactory currently doesn't own the page header —
            // host SettingsWindow still calls AddPageHeader. This page's Build is
            // called AFTER AddPageHeader. So just render the body here.

            ctx.Rows.AddGroupHeader(panel, "General");
            var (rowEnabled, chkEnabled) = ctx.Rows.AddToggle(panel,
                "Enable snippets",
                "Master toggle for the snippet feature.");
            ctx.RegisterSearch("Enable snippets", "Master toggle", "Toggle", rowEnabled);

            var (rowShowInCompletion, chkShowInCompletion) = ctx.Rows.AddToggle(panel,
                "Show in completion popup",
                "Include snippet shortcuts in the IntelliSense list.");
            ctx.RegisterSearch("Show in completion popup", "Include snippets in IntelliSense", "Toggle", rowShowInCompletion);

            // ... copy each remaining row from BuildSnippetsPage, registering search entries as we go ...

            return (panel, new SnippetsControls(chkEnabled, chkShowInCompletion /* + remaining */));
        }
    }

    internal sealed class SnippetsControls : IPageControls
    {
        private readonly CheckBox _enabled;
        private readonly CheckBox _showInCompletion;
        // ... + remaining

        public SnippetsControls(CheckBox enabled, CheckBox showInCompletion /* + remaining */)
        {
            _enabled = enabled;
            _showInCompletion = showInCompletion;
            // ...
        }

        public void Load(AppSettings settings)
        {
            _enabled.IsChecked = settings.Snippets.Enabled;
            _showInCompletion.IsChecked = settings.Snippets.ShowInCompletion;
            // ...
        }

        public void Save(AppSettings settings)
        {
            settings.Snippets.Enabled = _enabled.IsChecked == true;
            settings.Snippets.ShowInCompletion = _showInCompletion.IsChecked == true;
            // ...
        }

        public void Reset(AppSettings defaults)
        {
            // SettingsWindow.OnResetThisPageClick previously did:
            //   _settings.Snippets = defaults.Snippets;
            //   LoadSettingsToControls();
            // Mirror that: just reload from defaults.
            Load(defaults);
        }
    }
}
```

The exact constructor parameters and Load/Save/Reset bodies depend on every control on the page. Use the existing `BuildSnippetsPage` as the source of truth — every `_chkSnipFoo` → constructor parameter; every `_chkSnipFoo.IsChecked = settings.Snippets.Foo` → Load body; every `settings.Snippets.Foo = _chkSnipFoo.IsChecked == true` → Save body.

- [ ] **Step B.2.3: Wire `SnippetsPage` into `SettingsWindow`**

In `SettingsWindow.cs`:
1. Add a private field: `private readonly Dictionary<string, IPageBuilder> _pageBuilders = new();`
2. In the constructor (or wherever `BuildPages` is called), populate: `_pageBuilders["Snippets"] = new SnippetsPage();`
3. In `BuildPages`, when iterating to build "Snippets", call `_pageBuilders["Snippets"].Build(ctx)` instead of `BuildSnippetsPage()`. Store the returned `IPageControls` in a sibling dict: `_pageControlsByKey["Snippets"] = controls`.
4. In `LoadSettingsToControls`, after the existing per-field block for Snippets, also call `_pageControlsByKey["Snippets"].Load(_settings)` — and DELETE the existing per-field block for Snippets (it'd double-set).
5. In `SaveControlsToSettings`, similarly call `Save` on the page controls and delete the inline Snippets block.
6. In `OnResetThisPageClick`, the `"Snippets"` case becomes: `_pageControlsByKey["Snippets"].Reset(defaults); break;` — and the `LoadSettingsToControls()` call at the end of `OnResetThisPageClick` should still run (for any other settings affected; Snippets reset itself already).

Delete from `SettingsWindow.cs`:
- The `_chkSnipEnabled`, `_chkSnipShowInCompletion`, etc. control fields (5-7 fields total).
- The entire `BuildSnippetsPage()` method body.

- [ ] **Step B.2.4: Build and run all tests**

```bash
"/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj
```

Expected: 0 build errors, 4/4 tests pass. The `PageHeader_HasRestoreLink_ForEveryPage` test specifically needs to find "Restore Defaults" on the Snippets page — if it fails, the page header isn't being added by the new flow.

- [ ] **Step B.2.5: Manual smoke test (deferred to user)**

The user will deploy & open SSMS, confirm Snippets page still loads/saves correctly. Don't run SSMS yourself.

- [ ] **Step B.2.6: Prepare commit**

```bash
git add src/AkmlSql.Shell.Shared/Dialogs/Pages/SnippetsPage.cs src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Suggested message:

```
Migrate Snippets page to per-file builder (Phase 2 B.2)

First page-split migration. Establishes the pattern for the remaining
14 pages (B.3 through B.16):

- New Pages/SnippetsPage.cs implements IPageBuilder + IPageControls
- SettingsWindow holds _pageBuilders dictionary and dispatches to it
- Per-control fields _chkSnipEnabled, _chkSnipShowInCompletion, etc.
  removed from SettingsWindow — controls are now owned by SnippetsControls
- LoadSettingsToControls / SaveControlsToSettings / OnResetThisPageClick
  delegate to IPageControls.{Load,Save,Reset} for the Snippets key

No user-visible change. All 4 chrome tests still pass.

Refs: docs/superpowers/plans/2026-05-07-options-dialog-phase2.md B.2
```

**Ask the user:** "First page split done (Snippets). Approve commit before migrating the next 14?"

---

## Tasks B.3 through B.16: Migrate remaining 14 pages

Each task follows the **exact same template as B.2**. The pattern is mechanical; the implementer should not deviate.

For each page below, do:
1. Read its `BuildXxxPage` method.
2. Create `Pages/XxxPage.cs` mirroring `SnippetsPage.cs` shape.
3. Wire into `_pageBuilders` and `_pageControlsByKey`.
4. Delete control fields and `BuildXxxPage` method from `SettingsWindow.cs`.
5. Run build + 4 chrome tests.
6. Commit with message: `Migrate <page name> page to per-file builder (Phase 2 B.<n>)`.

| Task | Page Key | Class name | Approx. controls |
|---|---|---|---|
| B.3 | `Code Analysis` | `CodeAnalysisPage` | 4 |
| B.4 | `Refactoring` | `RefactoringPage` | ~6 |
| B.5 | `History` | `HistoryPage` | ~8 |
| B.6 | `Tabs & UI` | `TabsColorPage` | ~8 |
| B.7 | `Safety` | `ExecutionWarningsPage` (display "Queries › Execution Warnings") | ~8 |
| B.8 | `Grid` | `QueryResultsPage` (display "Queries › Query Results") | ~5 |
| B.9 | `Editor` | `EditorProductivityPage` (display "Editor › Productivity") | ~6 |
| B.10 | `Execution` | `ExecutionPage` (display "Queries › Execution") | ~5 |
| B.11 | `Navigation` | `EditorNavigationPage` (display "Editor › Navigation") | ~6 |
| B.12 | `AI Assistance` | `AiAssistancePage` | ~16 (largest after IntelliSense) |
| B.13 | `Schema Cache` | `SchemaCachePage` (display "Suggestions › Database") | ~8 |
| B.14 | `IntelliSense` | `IntelliSensePage` (display "Suggestions › Behavior") | ~16 |
| B.15 | `Formatting` | `FormatStylesPage` (display "Format › Styles") | ~8 |
| B.16 | `General` | `MiscellaneousMainPage` (display "Miscellaneous › Main") | ~6 |

After B.16, `SettingsWindow.cs` should be ~600-800 LoC (chrome + dispatch only). The 17 `BuildXxxPage` method count from recon decomposes as: 15 page builders + 2 helpers (e.g., `BuildSearchBox`, `BuildBottomBar`) — only the 15 page builders move out.

**Each task is its own commit. The user approves each commit. The 4 chrome tests must stay green throughout.**

If the implementer hits a page that doesn't fit the template (e.g., the AI page has dynamic controls or the IntelliSense page has cross-references to other pages' settings), **stop and report** — that page may need a tailored approach.

---

## Task B.17: Final cleanup of `SettingsWindow.cs`

After B.16, `SettingsWindow.cs` should have no `BuildXxxPage` methods left and minimal control fields. This task confirms cleanup.

- [ ] **Step B.17.1: Check for orphan code**

```bash
grep -n "BuildGeneralPage\|BuildIntelliSensePage\|BuildSchemaCachePage\|BuildFormattingPage\|BuildSnippetsPage\|BuildCodeAnalysisPage\|BuildRefactoringPage\|BuildHistoryPage\|BuildTabsPage\|BuildSafetyPage\|BuildAiPage\|BuildGridPage\|BuildEditorPage\|BuildExecutionPage\|BuildNavigationPage" src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Expected: empty (no remaining method definitions).

```bash
grep -n "private CheckBox\|private ComboBox\|private Slider\|private TextBox" src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Expected: empty or near-empty (only chrome-level controls if any). The 106 fields should now be 0-3.

- [ ] **Step B.17.2: Confirm `BuildPages` only dispatches**

`BuildPages` should now look like:

```csharp
private void BuildPages()
{
    foreach (var builder in _pageBuilders.Values)
    {
        var ctx = CreatePageContext();
        var (panel, controls) = builder.Build(ctx);
        var wrappedPanel = WrapWithPageHeader(panel, builder.Display);  // adds AddPageHeader chrome
        _pages[builder.Key] = WrapInScrollViewer(wrappedPanel);
        _pageControlsByKey[builder.Key] = controls;
    }
}
```

If it has any inline page-construction left, finish migrating.

- [ ] **Step B.17.3: Run all tests one final time**

```bash
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
```

Expected: all green.

- [ ] **Step B.17.4: Final SettingsWindow.cs LoC check**

```bash
wc -l src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Target: 600-800 LoC. If still > 1,000, find what's holding it up — there may be inline helpers that should move to `Pages/` or to `Pages/RowFactory.cs`.

- [ ] **Step B.17.5: Prepare commit (if any cleanup edits made)**

```bash
git add src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

If no edits were needed (B.16 left it clean), skip this step. Otherwise:

```
Final cleanup of SettingsWindow.cs after page split (Phase 2 B.17)

SettingsWindow is now the dialog chrome host: sidebar, content host,
search, bottom bar, page dispatch. All 15 page builders live in
Pages/*.cs. File shrunk from 3,228 LoC to ~XXX LoC.

Refs: docs/superpowers/plans/2026-05-07-options-dialog-phase2.md B.17
```

**Block B complete.** Ready for new pages.

---

# BLOCK C — New SQL Prompt Pages

Block C adds the 5 missing SQL Prompt pages, each backed by Block A's new AppSettings, using Block B's IPageBuilder pattern. Each new page is its own commit.

## Task C.1: Add `SuggestionTypesPage`

**Files:**
- Create: `src/AkmlSql.Shell.Shared/Dialogs/Pages/SuggestionTypesPage.cs`
- Modify: `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` (register builder, add tree leaf, extend Reset switch)

- [ ] **Step C.1.1: Create the page**

Mirror an existing per-file builder (e.g., `SnippetsPage` after B.2) for shape. Settings rows:

```csharp
ctx.Rows.AddGroupHeader(panel, "What appears in the suggestion list");

var (rowSysObjs, chkSysObjs) = ctx.Rows.AddToggle(panel,
    "List system objects",
    "Include system stored procs and functions (sp_*, sys.*) in suggestions.");
ctx.RegisterSearch("List system objects",
    "System procs/funcs in completions", "Toggle", rowSysObjs);

var (rowAllCols, chkAllCols) = ctx.Rows.AddToggle(panel,
    "List all database columns after SELECT",
    "Show every column from every table immediately after SELECT.");
ctx.RegisterSearch(...);

var (rowKeywords, chkKeywords) = ctx.Rows.AddToggle(panel,
    "Show keywords in suggestions",
    "Include SQL keywords (SELECT, FROM, etc.) in the list.");
ctx.RegisterSearch(...);

ctx.Rows.AddGroupSeparator(panel);
ctx.Rows.AddGroupHeader(panel, "Column suggestions");

var (rowScope, cboScope) = ctx.Rows.AddDropdown(panel,
    "Suggest columns from",
    new[] { "Referenced tables only", "All tables" },
    "Whether typing in WHERE/SELECT shows columns from only the FROM-clause tables, or every table in the database.");
ctx.RegisterSearch(...);
```

Map the dropdown to the `ColumnSuggestionScope` enum (index 0 = `ReferencedOnly`, 1 = `All`).

`SuggestionTypesControls.Load`:
```csharp
public void Load(AppSettings settings)
{
    var s = settings.IntelliSense.SuggestionTypes;
    _sysObjs.IsChecked = s.IncludeSystemObjects;
    _allCols.IsChecked = s.SuggestAllColumnsAfterSelect;
    _keywords.IsChecked = s.IncludeKeywords;
    _scope.SelectedIndex = (int)s.ColumnScope;
}
```

`Save`:
```csharp
public void Save(AppSettings settings)
{
    var s = settings.IntelliSense.SuggestionTypes;
    s.IncludeSystemObjects = _sysObjs.IsChecked == true;
    s.SuggestAllColumnsAfterSelect = _allCols.IsChecked == true;
    s.IncludeKeywords = _keywords.IsChecked == true;
    s.ColumnScope = (ColumnSuggestionScope)_scope.SelectedIndex;
}
```

`Reset`: `Load(defaults)`.

- [ ] **Step C.1.2: Register in `SettingsWindow`**

In the constructor where `_pageBuilders` gets populated:
```csharp
_pageBuilders["SuggestionTypes"] = new SuggestionTypesPage();
```

Add the tree leaf to `AddTreeGroup("Suggestions", ...)`:
```csharp
AddTreeGroup("Suggestions", expanded: true,
    ("Behavior", "IntelliSense"),
    ("Types of suggestion", "SuggestionTypes"),  // NEW
    ("Database", "Schema Cache"));
```

Add the case to `OnResetThisPageClick`:
```csharp
case "SuggestionTypes":
    _pageControlsByKey["SuggestionTypes"].Reset(defaults);
    break;
```

- [ ] **Step C.1.3: Build + run all tests**

```bash
"/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj
```

Expected: 0 errors. Chrome tests pass — including `PageHeader_HasRestoreLink_ForEveryPage` which now sees a 16th leaf (the new "Types of suggestion") and confirms it has a Restore link.

- [ ] **Step C.1.4: Prepare commit**

```bash
git add src/AkmlSql.Shell.Shared/Dialogs/Pages/SuggestionTypesPage.cs src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Message:

```
Add Suggestions › Types of suggestion page (Phase 2 C.1)

New page surfaces SuggestionTypesSettings (added in A.1, wired to the
engine in A.2):
- List system objects toggle
- List all database columns after SELECT toggle
- Show keywords in suggestions toggle
- Suggest columns from dropdown (Referenced tables only / All tables)

Tree leaf added under Suggestions group; page key registered in
OnResetThisPageClick for per-page restore.

Refs: docs/superpowers/specs/2026-05-06-options-dialog-redgate-parity-design.md §7.2
      docs/superpowers/plans/2026-05-07-options-dialog-phase2.md C.1
```

**Ask the user:** "Suggestion Types page ready. Approve commit?"

---

## Task C.2: Add `QualificationPage`

Same pattern. Settings:

| Control | Setting | Notes |
|---|---|---|
| Schema mode dropdown | `Qualification.SchemaMode` | Items: "Always", "Non-default schemas only", "Never". Map to enum by index. |
| Bracket mode dropdown | `Qualification.BracketMode` | Items: "Always", "When required", "Never". |
| Qualify columns toggle | `Qualification.QualifyColumnsWithTableOrAlias` | |

Tree placement: `AddTreeGroup("Inserted Code", ..., ("Qualification & Brackets", "Qualification"))`. Note that the Inserted Code group was empty after Phase 1 — this Task introduces it.

Commit message:

```
Add Inserted Code › Qualification & Brackets page (Phase 2 C.2)

Surfaces QualificationSettings: SchemaMode, BracketMode,
QualifyColumnsWithTableOrAlias. Introduces the previously-empty
Inserted Code group in the tree.

CompletionEngine already reads SchemaMode (A.2). BracketMode is read
opportunistically — full bracket policy is documented as deferred in
A.2's commit; current engine behavior matches BracketMode.WhenRequired
defaults.

Refs: docs/superpowers/plans/2026-05-07-options-dialog-phase2.md C.2
```

---

## Task C.3: Add `InsertStatementsPage`

Settings:

| Control | Setting |
|---|---|
| Insert column names toggle | `InsertOptions.IncludeColumns` |
| Insert default values as comments toggle | `InsertOptions.IncludeDefaultsAsComments` |
| Insert parameter info for procs toggle | `InsertOptions.IncludeProcParamInfo` |

Tree leaf: `("INSERT statements", "InsertOptions")` under Inserted Code.

Commit message: `Add Inserted Code › INSERT statements page (Phase 2 C.3)` — same pattern.

---

## Task C.4: Add `JoinCompletionPage`

Settings:

| Control | Setting |
|---|---|
| Use matching column names toggle | `JoinOptions.MatchByColumnName` |

Tree leaf: `("JOIN completion", "JoinOptions")` under Inserted Code.

Note: `JoinAssist` and `AutoAlias` (existing fields on `IntelliSenseSettings`) are surfaced on the IntelliSense (Behavior) page already. Do NOT duplicate them here — this page is just the new MatchByColumnName toggle. Display can include a non-clickable info row referencing where the related toggles live, e.g.:

```csharp
ctx.Rows.AddInfoRow(panel, "Related",
    "JOIN suggestions and aliases are configured under " +
    "Suggestions › Behavior (Use FK-assisted JOIN, Auto-alias).");
```

Commit message: `Add Inserted Code › JOIN completion page (Phase 2 C.4)`.

---

## Task C.5: Add `LabsPage`

Settings:

| Control | Setting | Description |
|---|---|---|
| Ghost-text AI completion toggle | `Labs.GhostTextCompletion` | Flag. Engine already gates this on the flag. |
| Parallel schema cache toggle | `Labs.ParallelSchemaCache` | Flag. SchemaCacheManager will read this in a future ticket. |
| Shared snippet sync toggle | `Labs.SharedSnippetSync` | Flag. Snippet sync feature future-pending. |

Add a banner row at the top of the page:

```csharp
ctx.Rows.AddInfoRow(panel, "⚠ Labs notice",
    "Features under Labs are experimental and may change or be removed " +
    "without notice. Use only in non-production environments.");
```

Tree placement: extend the existing Miscellaneous group:

```csharp
AddTreeGroup("Miscellaneous", expanded: false,
    ("Main", "General"),
    ("Labs", "Labs"));   // NEW
```

Commit message: `Add Miscellaneous › Labs page (Phase 2 C.5)`.

---

## Task C.6: Regression test for `OnResetThisPageClick` coverage

**Files:**
- Modify: `tests/AkmlSql.Shell.Shared.Tests/WindowChromeTests.cs`

The final reviewer of Phase 1 flagged that `OnResetThisPageClick` switch coverage is brittle when new pages are added. Block C adds 5 new keys (`SuggestionTypes`, `Qualification`, `InsertOptions`, `JoinOptions`, `Labs`). A test that fails when a new page is missing a `case` is the right safety net.

- [ ] **Step C.6.1: Add the test**

In `WindowChromeTests.cs`:

```csharp
[StaFact]
public void OnResetThisPageClick_HasCaseForEveryPage()
{
    var settings = new AppSettings();
    var dialog = new SettingsWindow(settings);
    var window = dialog.TestBuildWindowForRenderTest();

    var treeView = FindTreeView(window);
    Assert.NotNull(treeView);

    var leafItems = new List<TreeViewItem>();
    CollectLeafTreeViewItems(treeView!, leafItems);
    Assert.True(leafItems.Count >= 19, $"Expected ≥19 leaves after Phase 2, found {leafItems.Count}");

    // Mutate the settings on each page (set non-default values), simulate
    // selection, click the Restore link, confirm the settings on that page
    // returned to defaults. If a switch case is missing, the page won't reset
    // and the test will fail.

    foreach (var leaf in leafItems)
    {
        var key = leaf.Tag as string;
        Assert.NotNull(key);

        // Snapshot the relevant section of settings, mutate it, click reset, compare.
        // The exact mutation depends on the page key — simplest: deep-clone settings,
        // then for each leaf, run the reset path and confirm the per-page section
        // returned to AppSettings defaults.

        // Pseudocode:
        // 1. Clone the AppSettings BEFORE this test mutated anything.
        // 2. Mutate one leaf-relevant field to a non-default value.
        // 3. Set leaf.IsSelected = true; pump dispatcher.
        // 4. Invoke the OnResetThisPageClick handler programmatically (simulate
        //    a click on the "Restore Defaults" hyperlink TextBlock found in the
        //    page header).
        // 5. Read the field back from the dialog's settings; assert it's at default.

        // The mutation map is per-page (each page touches different parts of
        // AppSettings). For the first cut, just check that the case exists by
        // reflecting on OnResetThisPageClick's body — see alternative approach below.
    }
}
```

The full mutation-and-verify per page is brittle. A simpler, more pragmatic test: **reflect on the `OnResetThisPageClick` switch and assert it has a case for every page key.**

```csharp
[Fact]
public void OnResetThisPageClick_HasCaseForEveryRegisteredPageKey()
{
    var settings = new AppSettings();
    var dialog = new SettingsWindow(settings);

    // Read all registered page keys via reflection on _pageBuilders.
    var field = typeof(SettingsWindow).GetField("_pageBuilders",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(field);
    var pageBuilders = (IDictionary)field!.GetValue(dialog)!;

    // Read the OnResetThisPageClick method body via reflection on the IL — too brittle.
    // Alternative: simulate a click for each key and check it doesn't throw or no-op.
    var resetMethod = typeof(SettingsWindow).GetMethod("OnResetThisPageClick",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    Assert.NotNull(resetMethod);

    foreach (DictionaryEntry entry in pageBuilders)
    {
        var key = (string)entry.Key;
        // Set the tree's selected item to a TreeViewItem whose Tag = key.
        // ... pump ...
        // Invoke OnResetThisPageClick. Confirm no exception.
        // Or, in B.2's refactor, the reset is simply _pageControlsByKey[key].Reset(defaults);
        // — that's much cleaner. After Block B, OnResetThisPageClick is a one-liner:
        //   _pageControlsByKey[key].Reset(defaults);
        // and the missing-case bug becomes "missing _pageControlsByKey entry", which
        // throws KeyNotFoundException loudly. So this test becomes:
        //   Assert.True(_pageControlsByKey.ContainsKey(key)) for each key.
    }
}
```

**Pragmatic version (assuming Block B refactored OnResetThisPageClick to dispatch through `_pageControlsByKey`):**

```csharp
[Fact]
public void Every_PageBuilder_HasMatching_PageControls_Entry()
{
    var settings = new AppSettings();
    var dialog = new SettingsWindow(settings);
    _ = dialog.TestBuildWindowForRenderTest();  // realize visual tree → populates _pageControlsByKey

    var buildersField = typeof(SettingsWindow).GetField("_pageBuilders",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    var controlsField = typeof(SettingsWindow).GetField("_pageControlsByKey",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    var builders = (IDictionary)buildersField!.GetValue(dialog)!;
    var controls = (IDictionary)controlsField!.GetValue(dialog)!;

    foreach (var key in builders.Keys)
    {
        Assert.True(controls.Contains(key),
            $"Page builder '{key}' has no matching _pageControlsByKey entry.");
    }
}
```

This catches the "added a builder but forgot to wire it" bug, which IS the underlying issue the spec flagged.

- [ ] **Step C.6.2: Run the test**

```bash
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj --filter "FullyQualifiedName~Every_PageBuilder"
```

Expected: PASS.

- [ ] **Step C.6.3: Self-verify**

Temporarily delete one entry from `_pageControlsByKey` registration in `SettingsWindow.cs` (e.g., comment out the line that adds `"Labs"` to the controls dict — but leave the builder registered). Run the test; it MUST fail naming "Labs". Restore the line; test passes.

- [ ] **Step C.6.4: Prepare commit**

```bash
git add tests/AkmlSql.Shell.Shared.Tests/WindowChromeTests.cs
```

Message:

```
Add regression test for page builder ↔ controls registration (Phase 2 C.6)

Final reviewer of Phase 1 flagged that OnResetThisPageClick was easy to
forget when adding new pages. Block B refactored Reset to dispatch
through _pageControlsByKey, so the new failure mode is "page builder
registered but _pageControlsByKey entry missing".

Every_PageBuilder_HasMatching_PageControls_Entry asserts every key in
_pageBuilders also has an entry in _pageControlsByKey. Self-verified by
removing one registration — the test fails naming the missing key.

Refs: docs/superpowers/plans/2026-05-07-options-dialog-phase2.md C.6
```

**Ask the user:** "Coverage test ready. Approve commit?"

---

# Acceptance — Phase 2

- [ ] **Step Z.1: Run all tests**

```bash
dotnet test
```

Expected: all green across all 5 test projects (Core.Tests, Engine.Tests, Formatting.Tests, E2E.Tests, Shell.Shared.Tests).

- [ ] **Step Z.2: Build all shell extensions to confirm no breakage**

```bash
"/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
"/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe" "src/AkmlSql.Ssms21/AkmlSql.Ssms21.csproj" -t:Build -p:Configuration=Release -v:minimal
"/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe" "src/AkmlSql.VS2022/AkmlSql.VS2022.csproj" -t:Build -p:Configuration=Release -v:minimal
```

(Don't bother with VS2019/VS2026/SSMS20 unless the user specifically requests them — they have different VS SDK targets and Phase 2 has nothing to test that's unique to them.)

Expected: 0 errors per project.

- [ ] **Step Z.3: Confirm `SettingsWindow.cs` size target**

```bash
wc -l src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs
```

Target: 600-800 LoC. If higher, B.17 missed something.

- [ ] **Step Z.4: Confirm new pages exist**

```bash
ls src/AkmlSql.Shell.Shared/Dialogs/Pages/
```

Expected: 19+ files. The 15 migrated pages + 5 new pages + foundation files (IPageBuilder, PageContext, RowFactory, PageTheme).

- [ ] **Step Z.5: User does manual SSMS verification**

This step is the user's responsibility. Open Tools → AKML SQL → Options:
1. Navigate every page (incl. 5 new pages: Suggestion Types, Qualification & Brackets, INSERT statements, JOIN completion, Labs).
2. Toggle a setting on each page; click OK; reopen; confirm setting persisted.
3. On each new page, click "Restore Defaults"; confirm the page resets only its own settings.
4. Click bottom "Restore All Defaults"; confirm everything resets.
5. Click "Export…"; save a file. Toggle some settings. Click "Import…" with the saved file. Confirm settings restored.

Phase 2 is not "done" until the user confirms manual verification.

---

## Self-Review Notes

**Spec coverage check (§7 Phase 2 of the spec):**

- §7.1 Page-file split refactor — Block B (Tasks B.1 through B.17)
- §7.2 New AppSettings sub-objects — Task A.1
- §7.3 Engine wiring — Tasks A.2, A.3, A.4
- §7.4 Bottom button bar — **already implemented** in current code (discovered during Phase 1 recon); spec line item is satisfied without new work
- §7.5 Phase 2 tests — Tasks A.1.2 (round-trip), A.2.2/A.3.3/A.4.2 (engine policy), C.6 (Every_PageBuilder coverage). The plan's `PageBuilderTests.AllPagesBuildWithoutThrowing` is implicitly covered by `PageHeader_HasRestoreLink_ForEveryPage` (each page must build to be reachable for the assertion).

**Ambiguity check:**

- B.2 says "wire into SettingsWindow" — concrete steps given (Steps B.2.3.1-6). Not ambiguous.
- C.5's Labs banner copy is reasonable but not authoritative — implementer can rephrase.
- The split between AppSettings POCO additions (A.1) and engine wiring (A.2-A.4) means an interim state where the POCOs exist but don't affect engine behavior. That's intentional — it lets A.1 land independently as a small, low-risk commit.

**Type/method consistency:**

- `PageContext`, `IPageBuilder`, `IPageControls`, `RowFactory`, `PageTheme` — defined in B.1, used consistently in B.2-C.6.
- `_pageBuilders` and `_pageControlsByKey` field names — consistent across B.2-C.6.
- `OnResetThisPageClick` refactor target (dispatch through `_pageControlsByKey`) — flagged in B.2.3.6 and assumed in C.6.

**Risks not in spec but worth watching during execution:**

- Block B is mechanical but tedious. The implementer (or subagent loop) may rush and break a page. Each page commit is independently reviewable; if a regression is reported, `git bisect` should isolate quickly.
- The IntelliSense page (B.14) is the largest at ~16 controls — budget extra time. If it's hard to fit cleanly, consider splitting into IntelliSenseBehaviorPage + a sub-section. (Spec doesn't require this; only do if pragmatic.)
- The Labs page (C.5) has flags that may not yet be wired anywhere. That's OK — they default off; future feature work reads them. The test for A.1's round-trip covers serialization. No engine integration test is added in C.5.
- Task A.2's `BracketMode` deferred — the spec acknowledges this; Phase 3 may revisit.

**Phase 3 hand-off:**

After Phase 2 ships, Phase 3 (Style Editor + Redgate import + env colors) targets a separate spec section (§8). It assumes Block B's IPageBuilder pattern is in place — the Format › Styles page rewrite will use `RowFactory` and the `IPageBuilder` shape. The 1-day Redgate-style spike outlined in spec §8.6 should run before Phase 3 implementation begins.
