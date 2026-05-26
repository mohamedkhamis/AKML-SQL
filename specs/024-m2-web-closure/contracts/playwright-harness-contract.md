# Contract: Playwright E2E harness for User Story 1

**Owner**: User Story 4 (FR-014–FR-017)
**Location**: `tests/AkmlSql.Web.E2E.Tests/UserStory1Tests.cs` (test class) and `tests/AkmlSql.Web.E2E.Tests/Harness/` (fixture infrastructure).

The harness is xUnit + Microsoft.Playwright. The contract here is the lifecycle (build → run → drive → tear down), the four scenario shapes, and the headline-flow timing assertion.

---

## Lifecycle (single shared fixture)

```text
Test session start
    │
    ▼
DotnetRunFixture.InitializeAsync()
    │   ├── Run `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Release -nologo`
    │   ├── Abort the test session if the build returned non-zero (no stale-build runs)
    │   ├── Start `dotnet run --project src/AkmlSql.Web -c Release --no-build` (captures stdout)
    │   └── Poll http://localhost:<port>/ until 200 OK (60-second timeout)
    │
    ▼
Each [Fact] in UserStory1Tests
    │   ├── Construct a fresh Playwright `IBrowserContext` (cookies/storage cleared)
    │   ├── Navigate to http://localhost:<port>/
    │   ├── Drive the four-scenario actions (below)
    │   └── Assert per scenario
    │
    ▼
DotnetRunFixture.DisposeAsync()
    │   ├── Kill the `dotnet run` process (SIGINT / process.Kill())
    │   └── Dispose the Playwright instance
    ▼
Test session end
```

The fixture is registered via `xunit.runner.json` + `ICollectionFixture<DotnetRunFixture>` so every test in the collection shares one running web app.

---

## Build-before-browse guard

`DotnetRunFixture.InitializeAsync()` MUST execute the explicit `dotnet build` step before launching `dotnet run --no-build`. The build's exit code is asserted; a non-zero exit aborts the entire test session (throws from `InitializeAsync()`, which xUnit surfaces as fixture-level failure). The contract rejects any harness implementation that:

- Skips the build step
- Allows `dotnet run` without `--no-build` (incremental builds can produce stale binaries under some MSBuild conditions)
- Continues the test run when the build returned non-zero

---

## Readiness probe

Poll `http://localhost:<port>/` after launching `dotnet run`:

- Initial back-off: 250 ms
- Max attempts: 240 (60 seconds total)
- Success: any `2xx`/`3xx` response
- Failure: 4xx, 5xx, or the timeout — fixture aborts

The port number is read from the `dotnet run` stdout (it logs `Now listening on: http://localhost:5XXX`); the fixture parses that line.

---

## The four scenarios

Each maps 1:1 to a spec/021/spec.md User Story 1 acceptance scenario. Test method names follow `<Scenario>_<Action>_<Expected>`.

### Scenario 1 — Paste + format + analyse, headline flow

```csharp
[Fact]
public async Task PasteAndFormat_100LineProc_FormatsAndAnalyses_Under5Seconds()
{
    var sql = await File.ReadAllTextAsync("Fixtures/100-line-stored-proc.sql");

    var timer = HeadlineFlowTimer.Start();
    await Page.Locator("[data-testid='sql-editor']").FillAsync(sql);
    await Page.Keyboard.PressAsync("Control+K");
    await Page.Keyboard.PressAsync("Control+F"); // Format
    await Page.WaitForSelectorAsync("[data-testid='format-complete']");
    await Page.Locator("[data-testid='analyse-button']").ClickAsync();
    await Page.WaitForSelectorAsync("[data-testid='analyse-complete']");
    timer.Stop();

    Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5),
        $"Headline flow took {timer.Elapsed.TotalSeconds:F2}s; SC-004 ceiling is 5s.");
    Assert.Empty(Page.GetByTestId("error-banner").AllAsync().Result);
}
```

### Scenario 2 — Click-to-jump from problems list

```csharp
[Fact]
public async Task ProblemsList_ClickItem_MovesEditorCaretToFindingLine()
{
    // Pre-load SQL known to produce ≥ 1 finding, format + analyse
    // ...
    var firstFinding = Page.Locator("[data-testid='problem-item']").First;
    var expectedLine = int.Parse(await firstFinding.GetAttributeAsync("data-line"));

    await firstFinding.ClickAsync();
    await Page.WaitForFunctionAsync(
        $"() => window.akmlEditor.getCursorLine() === {expectedLine}");
}
```

### Scenario 3 — OS theme switch mid-session

```csharp
[Fact]
public async Task ThemeSwitch_MidSession_SurfaceRepaintsWithoutBreakage()
{
    await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Light });
    await Page.WaitForSelectorAsync("body.theme-light");

    await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
    await Page.WaitForSelectorAsync("body.theme-dark");

    Assert.Empty(Page.GetByTestId("error-banner").AllAsync().Result);
}
```

### Scenario 4 — Profile picker change re-formats

```csharp
[Fact]
public async Task ProfilePicker_SwitchProfile_ReformatProducesNewOutput()
{
    // Format with default profile, capture output
    var defaultOutput = await Page.Locator("[data-testid='sql-editor']").InputValueAsync();

    await Page.Locator("[data-testid='profile-picker']").SelectOptionAsync("builtin.compact");
    await Page.Keyboard.PressAsync("Control+K");
    await Page.Keyboard.PressAsync("Control+F");
    await Page.WaitForSelectorAsync("[data-testid='format-complete']");

    var compactOutput = await Page.Locator("[data-testid='sql-editor']").InputValueAsync();
    Assert.NotEqual(defaultOutput, compactOutput);
}
```

---

## `data-testid` contract

The four scenarios depend on these stable selectors. The spec-021 `Editor.razor` and friends MUST expose them; any rename without updating the test contract is a breaking change.

| `data-testid` | Surface |
|---------------|---------|
| `sql-editor` | The CodeMirror-backed `EditorComponent` textarea (or its accessible role) |
| `format-complete` | Element that appears in the DOM after `FormatAsync` resolves |
| `analyse-button` | The toolbar Analyse button |
| `analyse-complete` | Element that appears after `AnalyseAsync` resolves |
| `problem-item` | A row in `ProblemsListComponent`; carries `data-line` and `data-column` attributes |
| `error-banner` | The catch-all error surface; the four scenarios assert this is empty |
| `profile-picker` | The `<select>` in `ProfilePickerComponent` |

If any of these `data-testid`s is missing in the M2 code, the harness adds them as part of US4 (small, scoped edits — exception to the "no service / no component" constraint, justified in spec 024 Constitution Check as the Playwright contract's prerequisite).

---

## `HeadlineFlowTimer` contract

```csharp
public sealed class HeadlineFlowTimer
{
    private readonly Stopwatch _sw;
    private HeadlineFlowTimer() => _sw = Stopwatch.StartNew();
    public static HeadlineFlowTimer Start() => new();
    public TimeSpan Elapsed => _sw.Elapsed;
    public void Stop() => _sw.Stop();
}
```

The timer wraps `Stopwatch`. It exists to make the timing assertion explicit and greppable; reviewers reading the test see `timer.Elapsed < 5s` and immediately know the spec-024 SC-004 ceiling is being enforced.

---

## Validation checklist

- [ ] `DotnetRunFixture` runs `dotnet build` before `dotnet run` and asserts the build exit code
- [ ] `dotnet run` is launched with `--no-build`
- [ ] Readiness probe waits up to 60 s for a `2xx`/`3xx` from `http://localhost:<port>/`
- [ ] The four `[Fact]` methods exist with the names above (or close variants)
- [ ] Each `[Fact]` asserts `error-banner` is empty
- [ ] Scenario 1 asserts `timer.Elapsed < 5s`
- [ ] All `data-testid` references in the tests resolve in the M2 DOM
- [ ] Fixture disposal stops the `dotnet run` process and the Playwright instance
