---
description: "Task list for spec 047 — QA audit remediation"
---

# Tasks: QA Audit Remediation

**Input**: Design documents from `/specs/047-qa-audit-remediation/`. Each task depends on:
- [plan.md](./plan.md);
- [spec.md](./spec.md) (clarified 2026-11-21; plan-time amendments A1, A2);
- [research.md](./research.md) R1–R24;
- [data-model.md](./data-model.md);
- [contracts/](./contracts/);
- [quickstart.md](./quickstart.md).

**Tests**: REQUIRED. FR-038 requires each of the 32 issues to have one or more permanent automated tests that **fail
before the fix and pass after it**. Every story lists its tests first. Run each new test before implementing, see it
fail for the stated reason, and note that failure in `baseline.md`.
- **New types.** When a test needs a type that does not exist yet (a DTO, an interface, a record), first add that
  type with a stub body (`throw new NotImplementedException()` or a default value) so the test compiles and fails on
  behaviour, not on compilation.
- **Shared samples.** The audit's evidence and script folders are git-ignored. Every sample a test needs is
  committed under the test project or `tests/fixtures/`, never read from `reports/`.

**Organization**: one phase per spec user story (US1…US10, the spec's priority order), each independently
implementable and testable.

**Git**: never stage, commit or push (constitution IV, CLAUDE.md). Changes stay uncommitted until the owner asks.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (a different file, and no dependency on an unfinished task).
- **[Story]**: US1…US10, as in spec.md.
- Paths are relative to the repository root `C:\Repos\AKML\AKML-SQL`.

## Build and test rules that apply to every task (CLAUDE.md)

- **Shell projects** (`AkmlSql.Ssms22`, which compiles `AkmlSql.Shell.Shared`) build only with full MSBuild, never
  `dotnet build`.
- **Before any `tests/AkmlSql.Web.E2E.Tests` run**, run `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug`
  and `dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Debug`. Run that suite serially:
  `dotnet test tests/AkmlSql.Web.E2E.Tests -- xUnit.ParallelizeTestCollections=false`.
- **Stop a running `dotnet run` web dev server** before `dotnet test tests/AkmlSql.Web.Tests`, because it locks the
  bin folder.
- **Theme CSS** is generated from `docs/theme-tokens.json`, never hand-edited
  (`scripts/generate-theme-css.ps1 -CheckOnly` must stay green).

---

## Phase 1: Setup (shared test infrastructure)

**Purpose**: committed fixtures, the one new test package, and the place results are recorded.

- [X] T001 Create `specs/047-qa-audit-remediation/baseline.md` with three tables.
  - **Validation:** rows V1–V22, copied from quickstart.md §3, with the columns *Before (date, observed)*,
    *After (date, observed)*, *Result (Pass/Fail/Not Run + reason)* and *Evidence*.
  - **Ratchets:** `FormatParityTests` pass count, `CorpusGateTests` score, the PostgreSQL completion and format
    corpus gates.
  - **Fails-before log:** columns *Test*, *Issue*, *Failure seen before the fix*.
- [X] T002 [P] Create the CLI fixtures in `tests/fixtures/cli/` from contracts/cli-docs-a11y.md §1:
  - `tests/fixtures/cli/multi.sql`, exactly the three lines `SELECT * FROM dbo.Customers WHERE CompanyName LIKE '%a';`,
    `UPDATE dbo.Foo SET a=1;` and `SELECT NOLOCK FROM x;`;
  - `tests/fixtures/cli/sem1.sql`, the single line `select 1 from dbo.Customers` with no semicolon;
  - `tests/fixtures/cli/pe004-off.casettings`, containing `{"rules":{"PE004":{"enabled":false,"severity":"warning"}}}`.
- [X] T003 [P] Add `<PackageReference Include="Deque.AxeCore.Playwright" Version="4.10.*" />` to
  `tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj` (test-only; plan Complexity Tracking), then run
  `dotnet restore tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj`.
- [X] T004 [P] Create the formatter corpus inputs in `tests/format-parity/corpus/` from contracts/formatter.md §1, without goldens yet. The goldens are
  captured in T064 from the fixed formatter.
  - `tests/format-parity/corpus/sp047-01-proc-params.sql`:
    `create procedure dbo.p @a int, @b varchar(10)='x' as begin set nocount on; select @a, @b end` / `go` /
    `create procedure dbo.p2 (@a int, @b nvarchar(max), @c decimal(10, 2) = 1.5) as select 1` / `go` /
    `create procedure dbo.p3 @d datetime = null, @e int output as set @e = 1`.
  - `tests/format-parity/corpus/sp047-02-control-flow.sql`: `create procedure dbo.cf @a int as` / `begin` /
    `if @a > 1 begin select 1 end else begin select 2 end;` / `if @a = 0 select 0 else if @a = 1 begin select 1 end;` /
    `while @a > 0 begin set @a = @a - 1; if @a = 5 begin break end end` /
    `begin try begin select 1/0 end end try begin catch select error_message() end catch` / `end`.
  - `tests/format-parity/corpus/sp047-03-go-count.sql`: `select 1` / `GO` / `select 2` / `GO 3` / `print 'x'` /
    `GO 2 -- twice` / `select 3` / `GO`.
  - `tests/format-parity/corpus/sp047-04-block-comment.sql`: `/* first line` / `   second line indented three` /
    `     third indented five */` / `select 1;` / `select 2 /* inline` / `   continued */ from dbo.Region;`.
  - `tests/format-parity/corpus/sp047-05-spacing.sql`: one line
    `select c.CustomerID from dbo.Customers c inner join dbo.Orders o on o.CustomerID = c.CustomerID left outer join dbo.Shippers s on s.ShipperID = o.ShipVia where o.EmployeeID in (10, 11);`,
    then `set nocount on;`.

---

## Phase 2: Foundational (green baseline)

**Purpose**: no story shares new code, so this phase only fixes the reference point that "fails before / passes
after" and the ratchets are judged against.

**⚠️ CRITICAL**: finish this phase before changing product code.

- [X] T005 Build the branch as it is (`AKML-SQL.slnx`, `scripts/generate-theme-css.ps1`) and record the result in `specs/047-qa-audit-remediation/baseline.md`.
  - Run `"$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet`, then
    `"$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal`, then
    `powershell -ExecutionPolicy Bypass -File scripts/generate-theme-css.ps1 -CheckOnly`.
  - Then the two Debug builds for the E2E suite: `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug` and
    `dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Debug`.
  - Record "green" (or the exact errors) in `baseline.md`.
- [X] T006 Run the regression suites under `tests/` listed in quickstart.md §2 ("must stay at their current levels") and record in
  `baseline.md`:
  - the pass and fail counts per suite;
  - the `FormatParityTests` count and the `CorpusGateTests` score;
  - the known pre-existing failures (`PerformanceBaselineTests` drift, sp031 pending goldens, the load-sensitive
    `AiChatPanelCopyButtonTests` and `NotificationHostTests`), so later runs can tell a regression from an existing
    red.

**Checkpoint**: baseline recorded; the stories can start, in any order, apart from the file overlaps listed under
Dependencies.

---

## Phase 3: User Story 1 — The command-line analyzer reports what it finds (Priority: P1) 🎯 MVP

**Goal**: the installed, trimmed `AkmlSql.Analyzer.exe` reports the same findings as an untrimmed build, and a
release build cannot ship a silent analyzer (CLI-001; research R1).

**Independent Test**: publish the CLI and run it on `tests/fixtures/cli/*` (quickstart V1).

### Tests for User Story 1 ⚠️ (write first, see them fail)

- [X] T007 [US1] Create `tests/AkmlSql.E2E.Tests/Analyzer/PublishedAnalyzerTests.cs` with `[Trait("Category","PublishedCli")]`.
  - **Fixture:** an `IAsyncLifetime` class fixture runs
    `dotnet publish src/AkmlSql.Analyzer/AkmlSql.Analyzer.csproj -c Release -r win-x64 -o <temp dir>` once,
    through `tests/AkmlSql.E2E.Tests/Infrastructure/CliRunner.cs`, and exposes the exe path.
  - **Four facts,** with fixture paths resolved from the repo root as `CliFixture.FindRepoRoot` does:
    - `--file multi.sql` prints `PE002`, `PE003` and `PE004`;
    - `--file multi.sql --check` exits with code 1;
    - `--file sem1.sql --rules ST004` prints exactly one line containing `information` and `ST004`;
    - `--file multi.sql --settings pe004-off.casettings` prints `PE003` and no `PE004`.
  - Run `dotnet test tests/AkmlSql.E2E.Tests --filter "Category=PublishedCli"`. The first, second and third facts
    must fail with "No issues found". Log it in `baseline.md`.

### Implementation for User Story 1

- [X] T008 [US1] In `src/AkmlSql.Analyzer/AkmlSql.Analyzer.csproj`, keep `PublishTrimmed`/`PublishSingleFile` and
  add `<JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>` to the
  `PropertyGroup`. Add an `ItemGroup` with `<TrimmerRootAssembly Include="AkmlSql.Analysis" />` and
  `<TrimmerRootAssembly Include="AkmlSql.Core" />` (R1). Re-run T007; all four facts must pass.
- [X] T009 [US1] In `build.ps1`, right after the `Invoke-Build "Analyzer CLI (publish)"` block (currently around
  lines 242–245), add `Invoke-Build "Analyzer CLI (smoke)"`.
  - It runs `src\AkmlSql.Analyzer\bin\$Configuration\net10.0\win-x64\publish\AkmlSql.Analyzer.exe` four times, with
    the arguments and expectations of contracts/cli-docs-a11y.md §1, from `tests\fixtures\cli`.
  - On any miss it throws "Analyzer CLI smoke failed:", followed by the arguments and the captured output.
  - Keep the helper a small script-level function, so T010 can copy it unchanged.
- [X] T010 [US1] In `doc/Deploy-Build-Release.ps1`, add the same smoke check after the Step 4 publish loop (around
  lines 258–283), using the publish path the script already lists for the Analyzer CLI (around line 471). A failure
  stops the release with a non-zero exit before anything is copied or packaged.
- [X] T011 [US1] Verify the gate in `build.ps1` catches the real defect.
  - Dot-source the smoke function from T009 and point it at the old installed exe,
    `C:\Program Files (x86)\AKML SQL\AkmlSql.Analyzer.exe` (trimmed, 1.26.1104.1510). It must fail.
  - Run `powershell -ExecutionPolicy Bypass -File build.ps1 -SkipTests -SkipShell`. The smoke step must pass.
  - Record both outcomes under V1 in `baseline.md`.

**Checkpoint**: US1 complete. The published CLI reports findings, and both build scripts refuse a silent one.

---

## Phase 4: User Story 2 — First-run engine pairing works on a standard install (Priority: P1)

**Goal**: "Use this computer's engine" pairs with the install's real engine port, failures name the address and
offer Add engine…, and paired browsers are labelled and individually revocable, never pruned (WEB-001, ENG-001;
research R2, R3; contracts/engine-pairing.md).

**Independent Test**: IIS install with bridge port 59417 and an empty browser profile; one click or none gives Live.
Two paired browsers are listed and one is revoked (quickstart V2–V4).

### Contracts first (so the tests compile)

- [X] T012 [US2] Add the IPC numbers and DTOs (data-model §3) in `src/AkmlSql.Core/Ipc/RpcMessage.cs` and a new `src/AkmlSql.Core/Ipc/Messages/PairingMessages.cs`.
  - **Numbers:** in `src/AkmlSql.Core/Ipc/RpcMessage.cs` (class `MessageTypes`), after `SessionDisconnectResult = 226`,
    add a commented block "Spec 047 (pairing.manage.v1)" with `PairingListRequest = 227`,
    `PairingListResponse = 228`, `PairingRevokeRequest = 229` and `PairingRevokeResponse = 230`.
  - **DTOs:** create `src/AkmlSql.Core/Ipc/Messages/PairingMessages.cs` (netstandard2.0-safe, `[MessagePackObject]`,
    keys exactly as data-model §3): `PairingListRequest` (empty), `PairingListResponse { 0 PairingUsed, 1 Entries }`,
    `PairingEntry { 0 Id, 1 Label, 2 MintedAt, 3 LastUsedAt, 4 ExpiresAt, 5 IsCurrent }`,
    `PairingRevokeRequest { 0 Id }` and `PairingRevokeResponse { 0 Ok, 1 Message, 2 ClosedConnections }`.
- [X] T013 [P] [US2] Add stub types so the web tests compile, in `src/AkmlSql.Web/Services/PageEngine.cs`, `src/AkmlSql.Web/Services/IEngineAutoConnect.cs` and a new `src/AkmlSql.Web/Services/IPairingService.cs`.
  - In `src/AkmlSql.Web/Services/PageEngine.cs`: `public enum PageEngineSource { ServedPage, InstallManifest }` and a
    `Source` member on `PageEngineResult`, defaulting to `ServedPage`.
  - In `src/AkmlSql.Web/Services/IEngineAutoConnect.cs`: `public enum LocalPairFailure { NotReachable, PinRequired, Rejected }`
    and `public sealed record LocalPairResult(bool Ok, string TriedAddress, LocalPairFailure? Failure)`.
  - Create `src/AkmlSql.Web/Services/IPairingService.cs`: an interface `IPairingService` with
    `bool Available { get; }`, `Task<PairingListResponse?> ListAsync(CancellationToken ct)` and
    `Task<PairingRevokeResponse?> RevokeAsync(string id, CancellationToken ct)`, plus a stub class.

### Tests for User Story 2 ⚠️ (write first, see them fail)

- [X] T014 [P] [US2] Create `tests/AkmlSql.Installer.Tests/EngineManifestScriptTests.cs`. It uses
  `HostingTestTools.Run(HostingTestTools.Script("web-engine-manifest.ps1"), …)` against a temp folder and asserts:
  - `-WebRoot <tmp> -Port 59417 -Scheme ws` writes `akml-engine.json` equal to
    `{"manifestVersion":1,"bridgePort":59417,"scheme":"ws"}` (parsed, no BOM, no `.tmp` left);
  - `-Scheme wss` is written as `wss`;
  - a missing folder, a junction (reparse point) folder, port `80` and port `70000` exit 1 and write nothing;
  - `-Remove` deletes the file and exits 0, also when the file is absent.
- [X] T015 [P] [US2] Extend `tests/AkmlSql.Web.Tests/js/akml-page-engine.test.mjs` with tests for a new
  `readManifest(url)`:
  - with a stubbed `globalThis.fetch`, a 200 JSON object is returned parsed;
  - a 404, invalid JSON, a non-object JSON and an abort after the 5 s timer each give `null`;
  - the request uses `method: 'GET'` and `cache: 'no-cache'`.
- [X] T016 [P] [US2] Create `tests/AkmlSql.Web.Tests/Services/PageEngineManifestTests.cs`. It uses a fake
  `IJSRuntime` / `IJSObjectReference` returning the module results, in the pattern of
  `tests/AkmlSql.Web.Tests/Services/PageEngineTests.cs`.
  - **`detect`=false and manifest `{manifestVersion:1, bridgePort:59417, scheme:"ws"}`:** `Kind=Engine`,
    `Source=InstallManifest` and `Engine=(page host, 59417, "ws")`.
  - **Static results:** a manifest with port 80, scheme "http", version 2 or `null` gives `Kind=Static`.
  - **`detect`=true:** `Kind=Engine`, `Source=ServedPage`, and `readManifest` is never called.
- [X] T017 [P] [US2] Create `tests/AkmlSql.Web.Tests/Bridge/LocalPairResultTests.cs`. It covers
  `EngineAutoConnect.PairLocalEngineAsync` with the fakes used by `EngineAutoConnectPrecedenceTests.cs`:
  - a manifest page engine gives `TriedAddress = "{host}:{port}"` of the page engine;
  - with no page engine, `TriedAddress = "localhost:47291"`;
  - a bridge that throws gives `Failure=NotReachable`;
  - `PinRequired` / `PinInvalid` give `Failure=PinRequired` and set `PairingPrefill`;
  - another status gives `Rejected`;
  - `Ok` gives `Ok=true` and a null failure.
- [X] T018 [P] [US2] Create `tests/AkmlSql.Web.Tests/Shell/LocalPairFailureTests.cs` (bUnit on `StatusBar.razor`,
  fake `IEngineAutoConnect` returning each `LocalPairResult`).
  - `local-engine-failure` shows the texts of contracts/engine-pairing.md §3 for `NotReachable`, `PinRequired` and
    `Rejected`.
  - `local-engine-add` navigates to `settings/connections` and sets `IPageEngine.PairingPrefill` to the tried
    host and port.
  - Nothing navigates automatically on failure.
  - The button tooltip names the address.
- [X] T019 [P] [US2] Create `tests/AkmlSql.Web.Tests/js/akml-bridge.label.test.mjs` for a new export
  `browserLabel(nav, origin)`.
  - Chrome, Edge, Firefox, Safari and Opera user-agent strings on Windows, macOS, Linux, Android and iOS give the
    family and OS of contracts/engine-pairing.md §4.
  - `userAgentData.brands` takes precedence when present.
  - An unknown UA gives `"Browser on unknown OS · {origin}"`.
  - The output is never longer than 80 characters, and a cut ends in "…".
- [X] T020 [P] [US2] Create `tests/AkmlSql.Engine.Tests/Pairing/PairingManageHandlerTests.cs`. It uses a temp
  `BearerTokenStore`, a fake socket-closer and the `BridgeConnection` ambient set as in `BridgeConnectionTests.cs`.
  - With pairing not required, the list gives `PairingUsed=false` and empty `Entries`.
  - Two minted tokens are listed newest first, with labels and dates, and `IsCurrent` is true only for the
    caller's hash.
  - Revoke of a known id removes it (`List()` no longer contains it) and returns `ClosedConnections` from the
    fake closer.
  - An unknown id gives `Ok=false` and "That pairing no longer exists.".
  - A request with no bridge source (named pipe) gets an error reply.
  - The revoke log line contains labels but no hash.
- [X] T021 [P] [US2] Extend `tests/AkmlSql.Engine.Tests/Handlers/HandshakeHandlerTests.cs`:
  - a successful bearer handshake records `BearerTokenStore.HashToken(bearer)` as the connection's token hash;
  - a PIN pairing records the hash of the minted token;
  - `PinInvalid`, `PinRequired` and `ProtocolMismatch` record none.
- [X] T022 [P] [US2] Create `tests/AkmlSql.Web.Tests/Settings/PairedBrowsersSectionTests.cs` (bUnit,
  `ConnectionsSection.razor`, fake `IPairingService`).
  - `paired-browsers` is shown only when `Available` is true and `PairingUsed` is true.
  - Rows `paired-browser-{id8}` show the label, "This browser" (IsCurrent), "Paired …", "Last used …" and
    "Expired".
  - `paired-browser-revoke-{id8}` opens the confirmation with the two texts of contracts/engine-pairing.md §6.
  - Confirming calls `RevokeAsync(id)` and refreshes the list; cancelling calls nothing.

### Implementation for User Story 2

- [X] T023 [P] [US2] Create `src/AkmlSql.Installer/web-engine-manifest.ps1`
  (`param([string]$WebRoot, [int]$Port, [ValidateSet('ws','wss')][string]$Scheme, [switch]$Remove)`), per
  contracts/engine-pairing.md §1.
  - **Refusals:** a root that is missing or a reparse point (`(Get-Item).Attributes -band ReparsePoint`), checked
    on its canonical path (`[IO.Path]::GetFullPath`), and a port outside 1024–65535.
  - **Writing:** UTF-8 without a BOM (`[IO.File]::WriteAllText(..., (New-Object Text.UTF8Encoding $false))`) to
    `akml-engine.json.tmp`, then `Move-Item -Force`.
  - **`-Remove`:** `Remove-Item -ErrorAction SilentlyContinue`, exit 0.
  - **Errors:** written to stderr, exit 1.
  - Make T014 pass.
- [X] T024 [US2] Wire the helper into `src/AkmlSql.Installer/web-installer.iss`.
  - **Files:** add `Source: "web-engine-manifest.ps1"; DestDir: "{app}\Support"; Flags: ignoreversion; Components: web`
    next to the other `web-*.ps1` lines (lines 13–21).
  - **`Web_PostInstall`**, after the `web-config-bridge.ps1` call, around line 549:
    - for `WebHost` `IIS` or `None`, call
      `Web_Support('web-engine-manifest.ps1', '-WebRoot "' + ExpandConstant('{app}\Web') + '" -Port ' + IntToStr(WebBridgePort) + ' -Scheme ' + <ws when Exposure='Localhost', wss when 'Lan'>, Code)`;
    - for `Service`, call it with `-Remove`;
    - log a non-zero exit with `Log('WARNING: …')` (non-fatal, like the firewall step) and add it to the summary
      `Failure` text.
- [X] T025 [P] [US2] Re-run the helper after a port or exposure change in `src/AkmlSql.Installer/web-hosting-repair.ps1`
  (near the `web-config-bridge.ps1` call around line 100: same `-WebRoot (Join-Path $app 'Web')`, `-Port $enginePort`,
  and the scheme from `$exposure`) and in `src/AkmlSql.Installer/web-iis-repair.ps1` (with its `$root` and port
  values). Both use `-Remove` when the host is Service.
- [X] T026 [US2] Make sure IIS serves `.json` from the site.
  - Inspect the web.config the publish produces (`src/AkmlSql.Web/bin/Release/net10.0/publish/wwwroot/web.config`)
    and what `src/AkmlSql.Installer/web-iis-setup.ps1` configures.
  - Only if no `.json` mime map is effective, add `<mimeMap fileExtension=".json" mimeType="application/json" />`
    with a preceding `<remove fileExtension=".json" />` in the shipped web.config source.
  - Record the finding in `baseline.md`.
- [X] T027 [P] [US2] Add `export async function readManifest(url)` to `src/AkmlSql.Web/wwwroot/js/akml-page-engine.js`:
  fetch `GET`, `cache: 'no-cache'`, `AbortController` 5 s, `null` unless `response.ok` and the parsed value is a
  non-null object, with the timer always cleared. Make T015 pass.
- [X] T028 [US2] Implement the manifest step in `src/AkmlSql.Web/Services/PageEngine.cs` `GetAsync`, per
  contracts/engine-pairing.md §2.
  - After `detect` returns false, call `readManifest(new Uri(_pageBase, "/akml-engine.json").AbsoluteUri)` on the
    same module.
  - Accept it only when `manifestVersion == 1`, `bridgePort` is in 1024–65535 and `scheme` is `ws` or `wss`. Then
    return `new(PageEngineKind.Engine, new PageEngineInfo(_pageBase.Host, port, scheme)) { Source = InstallManifest }`.
  - Keep the existing failure and retry rules for `detect`. Make T016 pass.
- [X] T029 [US2] Change `PairLocalEngineAsync` in `src/AkmlSql.Web/Services/IEngineAutoConnect.cs` (interface and
  implementation, lines ~329–381) to return `LocalPairResult`.
  - `TriedAddress` is `"{host}:{port}"` of the connection actually tried.
  - Map statuses: `PinRequired`/`PinInvalid` give `PinRequired` (prefill set as today); any other non-Ok status
    gives `Rejected`; a thrown exception gives `NotReachable`.
  - Update every caller and fake (grep `PairLocalEngineAsync` in `src/` and `tests/`). Make T017 pass.
- [X] T030 [US2] Update `src/AkmlSql.Web/Shared/StatusBar.razor` (`UseLocalEngineAsync`, `LocalEngineTitle`,
  lines ~345–365) per contracts/engine-pairing.md §3.
  - Remove the automatic `Nav.NavigateTo("settings/connections")`.
  - Render the `local-engine-failure` notice with the per-failure text and buttons `local-engine-add` / Pair… /
    Settings…, plus Dismiss.
  - **Add engine…** sets `IPageEngine.PairingPrefill = new PageEngineInfo(host, port, scheme)` from `TriedAddress`,
    then navigates.
  - The tooltip reads "Pair with the engine on this computer ({address})". Make T018 pass.
- [X] T031 [P] [US2] Add `export function browserLabel(nav, origin)` to `src/AkmlSql.Web/wwwroot/js/akml-bridge.js`
  (pure; contracts/engine-pairing.md §4).
  - In `src/AkmlSql.Web/Services/IEngineBridge.cs`, obtain it once per page through the bridge's existing JS module
    (fallback `"Web edition"` on any JS failure) and send it as `HandshakeRequest.BrowserLabel` (currently the
    literal at line ~292).
  - Make T019 pass.
- [X] T032 [US2] Record the authenticated token per connection in `src/AkmlSql.Engine/Pairing/BridgeConnection.cs` and `src/AkmlSql.Engine/Handlers/Handshake/HandshakeHandler.cs`.
  - **`src/AkmlSql.Engine/Pairing/BridgeConnection.cs`:** add `string? TokenHash` to the private `Holder`, plus
    `internal static void SetTokenHash(string? hash)` and `internal static string? CurrentTokenHash`, following the
    `Adopt` / `Current` pattern.
  - **`src/AkmlSql.Engine/Handlers/Handshake/HandshakeHandler.cs`:** call `SetTokenHash` on the two success paths,
    with `BearerTokenStore.HashToken(minted)` for a PIN mint (lines ~122–139) and
    `BearerTokenStore.HashToken(request.BearerToken)` for a valid bearer (lines ~142–170). Make `HashToken`
    reachable from the handler (it is `internal static` in the same assembly).
  - Make T021 pass.
- [X] T033 [US2] Close the sockets of a revoked pairing in `src/AkmlSql.Engine/Transports/WebSocketTransport.cs`.
  Add `internal int CloseConnectionsWithToken(string tokenHash)`: for every open socket whose connection holder's
  `TokenHash` equals it, run `CloseAsync(WebSocketCloseStatus.PolicyViolation, "pairing revoked", …)` and return the
  count. Reuse the collection the transport already keeps for shutdown (around line 488).
- [X] T034 [US2] Create `src/AkmlSql.Engine/Handlers/Pairing/PairingManageHandler.cs`, two handlers in the style of
  the existing session-disconnect handler, per contracts/engine-pairing.md §5.
  - **List:** map `BearerTokenStore.List()` to `PairingEntry`, newest `MintedAt` first, with
    `IsCurrent = hash == BridgeConnection.CurrentTokenHash`; `PairingUsed` from the engine's
    pairing-required provider.
  - **Revoke:** `RevokeByHash`, then `CloseConnectionsWithToken`, then the Information log
    `Pairing revoked: {Label} (by {CallerLabel})`.
  - Refuse non-bridge requests (`BridgeSourceIp.Current is null`).
  - Register both in `src/AkmlSql.Engine/EngineHandlerRegistry.cs`, and advertise `pairing.manage.v1` from
    `src/AkmlSql.Engine/Capabilities.cs` for bridge clients.
  - Make T020 pass.
- [X] T035 [P] [US2] Implement `src/AkmlSql.Web/Services/IPairingService.cs` (`PairingService`).
  - `Available` = the bridge is open and advertises `pairing.manage.v1`.
  - `ListAsync` / `RevokeAsync` use `IEngineBridge.SendAsync` with message types 227/229 and a 15 s timeout, and
    return null on `InvalidOperationException`.
  - Register `builder.Services.AddSingleton<IPairingService, PairingService>();` in `src/AkmlSql.Web/Program.cs`.
- [X] T036 [US2] Add the "Paired browsers" list to `src/AkmlSql.Web/Shared/Settings/ConnectionsSection.razor`, under
  the active engine row (contracts/engine-pairing.md §6).
  - Load it on render when `IPairingService.Available` is true.
  - Use the test ids `paired-browsers`, `paired-browser-{id8}` and `paired-browser-revoke-{id8}`.
  - Confirm through the existing `IDialogService.ConfirmAsync` (Destructive = true) and refresh after a revoke.
  - Make T022 pass.
- [ ] T037 [US2] Run the story's tests: `dotnet test tests/AkmlSql.Installer.Tests --filter "FullyQualifiedName~EngineManifest"`,
  - **Status (implementation):** unit, integration and JS suites green; browser classes 20 / 6: ConnectDialogTests green, the 6 failures are environmental (EngineAutoConnectTests need a network-exposed install, this one is Localhost; EngineServedWebTests trips on the Debug bundle's hot-reload 404). See baseline.md, Browser regression runs. Left open until a LAN-mode run.
  `dotnet test tests/AkmlSql.Engine.Tests --filter "FullyQualifiedName~Pairing|FullyQualifiedName~HandshakeHandler"`,
  `dotnet test tests/AkmlSql.Web.Tests` and `node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"`. Then run the
  regression E2E classes `EngineAutoConnectTests`, `EngineServedWebTests` and `ConnectDialogTests` (serial, Debug
  builds first). All must be green.

**Checkpoint**: US2 complete. Discovery, the failure notice and paired-browser management are tested. V2–V4 are
validated in T141.

---

## Phase 5: User Story 3 — Saved connections are remembered, and the dialog tells the truth (Priority: P1)

**Goal**: Save with Remember checks the login, then keeps the password on the engine. The reconnect form is never
blank. Browser data is requested to persist, and its address is explained. Rows carry no PostgreSQL port (WEB-002,
WEB-004, WEB-003, WEB-010; research R4–R7; contracts/connections-and-storage.md).

**Independent Test**: quickstart V5–V7.

### Contracts first

- [X] T038 [US3] Add `Task<ConnectResult> TestAndRememberAsync(ConnectionTarget target, CancellationToken ct);` to
  `ISqlConnectionService` in `src/AkmlSql.Web/Services/ISqlConnectionService.cs`, as a stub in `SqlConnectionService`.
  Add it to every test fake that implements the interface (grep `: ISqlConnectionService` under `tests/`).
  Create `src/AkmlSql.Web/Services/BrowserStorageState.cs` with
  `public interface IBrowserStorageState { Task<bool?> RequestPersistenceAsync(); Task<bool?> IsPersistedAsync(); }`
  and a stub class.

### Tests for User Story 3 ⚠️ (write first, see them fail)

- [X] T039 [P] [US3] Create `tests/AkmlSql.Web.Tests/Shell/ConnectionSaveRememberTests.cs` (bUnit on
  `ConnectionManagerModal.razor`, with the fakes used by `ConnectionManagerModalKindTests.cs`).
  - One test per row of the Save table in contracts/connections-and-storage.md §1, asserting the saved row's
    `RememberPassword`, the `conn-message` text, and which service calls were made: `TestAndRememberAsync` only
    when a password is typed with Remember on, and `Forget` when Remember is unticked on a row that had it.
  - The password field is empty after Save.
  - A second Save click during the test is ignored.
  - `conn-sql-remember-hint` holds the hint text.
- [X] T040 [P] [US3] Create `tests/AkmlSql.Web.Tests/Services/TestAndRememberTests.cs`, with a fake bridge capturing
  the request, in the pattern of `SqlConnectionServiceTargetTests.cs`:
  - `TestAndRememberAsync` sends `TestSqlConnectionRequest` with `RememberPassword = true` and the typed password;
  - the result's `Remembered` follows response key 4, and is true for a v1 engine that cannot confirm
    (`EngineConfirmsRememberedPasswords` false);
  - a failed login gives `Ok=false` with the server message;
  - a closed bridge gives the "no engine" failure without sending.
- [X] T041 [P] [US3] Create `tests/AkmlSql.Web.Tests/Shell/ConnectionPrefillNameTests.cs` (bUnit). Opening the modal
  with a prefill binding that matches no saved row:
  - sets the Display name to `"{server} / {database}"` (PostgreSQL: `"{host} / {database}"`);
  - shows `conn-not-saved-note`;
  - leads Save to add a row with exactly that name.
- [X] T042 [P] [US3] Create `tests/AkmlSql.Web.Tests/Bridge/SavedConnectionJsonTests.cs`:
  - `JsonSerializer.Serialize(new SavedSqlConnection { Kind = SqlServer, … })` contains no `effectivePort` member
    (case-insensitive);
  - `{"…","effectivePort":5432}` from an old record deserializes, and the row loads through the store's read path.
- [X] T043 [P] [US3] Create `tests/AkmlSql.Web.Tests/Services/BrowserStorageStateTests.cs` (fake `IJSRuntime`).
  - `RequestPersistenceAsync` invokes the module's `persist` once per instance and caches the answer.
  - A missing API (`null`) is passed through.
  - The saved-connection store's and engine-connection store's Add paths call it after a successful add.
  - A throwing `persist` does not fail the Add.
- [X] T044 [P] [US3] Create `tests/AkmlSql.Web.Tests/Settings/DiagnosticsStorageRowTests.cs` (bUnit,
  `DiagnosticsSection.razor`): `diag-storage` shows the three texts of contracts/connections-and-storage.md §4 for
  `true` / `false` / `null`, and `diag-storage-ask` calls `RequestPersistenceAsync` again (bypassing the cache).
- [X] T045 [P] [US3] Create `tests/AkmlSql.Web.Tests/Editor/OriginBannerTests.cs`.
  - `origin-banner` is shown only when all four conditions of contracts/connections-and-storage.md §5 hold.
  - Each condition is tested false on its own: a saved row, `localhost`, a LAN host, `Static`, the dismissed key.
  - The text uses the current scheme and port.
  - `origin-banner-dismiss` writes `akml.originBanner.dismissed = "1"`, and a throwing `localStorage` still renders
    without error.

### Implementation for User Story 3

- [X] T046 [US3] Implement `TestAndRememberAsync` in `src/AkmlSql.Web/Services/ISqlConnectionService.cs`.
  - It delegates to the private `TestSqlServerAsync(server, database, windowsAuth, user, password, trustServerCertificate, ct, CopySourceOf(target, null), rememberPassword: true)`
    for SQL Server targets, and returns a failed `ConnectResult` ("PostgreSQL is not supported here") for
    PostgreSQL, which is never called by the dialog.
  - Make T040 pass.
- [X] T047 [US3] Rewrite the SQL Server branch of `SaveAsync` in `src/AkmlSql.Web/Shared/ConnectionManagerModal.razor`
  (lines ~1441–1492) to the decision table of contracts/connections-and-storage.md §1.
  - The row is always saved.
  - The test runs only for a SQL login with Remember ticked, a typed password and `SqlConn.EngineRemembersSqlServerPasswords`.
  - `RememberPassword = result.Ok && result.Remembered`.
  - Unticking calls the existing forget path (the one Settings › Connections uses), and its outcome adds the
    "no longer keeps" sentence.
  - Clear `_password` afterwards. Keep `_busy` handling.
  - Add the `conn-sql-remember-hint` element under the Remember checkbox in the markup.
  - Make T039 pass.
- [X] T048 [US3] In `ApplyPrefill` in the same file (lines ~932–975), after `NewConnection(b.Kind)`, set `_name` to
  `$"{b.Server} / {b.Database}"`, or `$"{b.Server} / {b.Database}"` with the PostgreSQL host field when the kind is
  PostgreSQL. Render `conn-not-saved-note` ("Not a saved connection yet. Save keeps it.") while `_selectedId` is null
  and the form came from a prefill. Make T041 pass.
- [X] T049 [P] [US3] Add `[System.Text.Json.Serialization.JsonIgnore]` to `EffectivePort` in
  `src/AkmlSql.Web/Services/ISavedSqlConnectionStore.cs` (line ~101). Make T042 pass.
- [X] T050 [P] [US3] Add `export async function persist()` and `export async function persisted()` to
  `src/AkmlSql.Web/wwwroot/js/akml-indexeddb.js`. Each returns `null` when `navigator.storage?.persist` /
  `persisted` is not a function, otherwise the boolean, with errors turned into `null`. Implement `BrowserStorageState`
  in `src/AkmlSql.Web/Services/BrowserStorageState.cs`: it imports `./js/akml-indexeddb.js`, caches once per instance,
  and offers `RequestPersistenceAsync(bool force = false)`. Register it as a singleton in `src/AkmlSql.Web/Program.cs`.
- [X] T051 [US3] Call `IBrowserStorageState.RequestPersistenceAsync()` fire-and-forget after a successful
  `AddAsync`. Do this in `src/AkmlSql.Web/Services/ISavedSqlConnectionStore.cs` (`SavedSqlConnectionStore`) and in
  `src/AkmlSql.Web/Services/IConnectionStore.cs` (`ConnectionStore`), injected as an optional constructor parameter
  so existing tests keep constructing them. Log failures at Trace through the existing diagnostics sink. Make T043
  pass.
- [X] T052 [US3] Add the `diagnostics-storage` row to `src/AkmlSql.Web/Shared/Settings/DiagnosticsSection.razor`
  (`diag-storage`, `diag-storage-ask`). Add its catalog entry to `src/AkmlSql.Web/Services/Settings/SettingsCatalog.cs`
  next to `diagnostics-engine`: title "Browser storage", description "Whether this browser keeps your saved
  connections and pairings, or may clear them when space runs low.", keywords "persistent storage evicted cleared
  quota". `SettingsCatalogConsistencyTests` must stay green. Make T044 pass.
- [X] T053 [US3] Add the origin banner to `src/AkmlSql.Web/Pages/Editor.razor`, at the top of the workspace, per
  contracts/connections-and-storage.md §5.
  - It evaluates the conditions once in `OnInitializedAsync` (stores, `NavigationManager.BaseUri` host,
    `IPageEngine.GetAsync`, the localStorage key through the existing JS helper, wrapped in try/catch).
  - It links to `{scheme}://localhost:{port}/` and to the Settings page's Import settings action.
  - Styles use existing tokens only.
  - Make T045 pass.
- [X] T054 [US3] Run `dotnet test tests/AkmlSql.Web.Tests` and `node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"`,
  - **Status (implementation):** done: Web.Tests and node green at every later story gate (2305/2305 and 321/321 at US10); ConnectDialogTests, SecretStorageTests and SettingsPageTests green in the US2/US3 browser run; QueryTabsTests 43 passed with TabStripKeyboard, WorkspaceAccessibility, WorkspaceLayout and ShellPolish on 2026-11-22 (6 skipped by design: no PostgreSQL server or SQL login), after its close-button locator followed the US10 structure (ActiveClose).
  then the E2E regression classes `ConnectDialogTests`, `SecretStorageTests`, `QueryTabsTests` and `SettingsPageTests`
  (serial). All must be green.

**Checkpoint**: US3 complete. V5 needs a SQL login on `(local)`: ask the owner before creating one.

---

## Phase 6: User Story 4 — The formatter produces correct, stable layout in every style (Priority: P1)

**Goal**: sized parameter types stay whole, `BEGIN`/`END` align with their owner, the statement after `GO n` starts
at column 0, comment text is kept, the spacing oddities are fixed, and SSMS keeps CRLF (FMT-001…005, SSMS-004;
research R8–R12; contracts/formatter.md).

**Independent Test**: `Sp047FormatterRulesTests` and `LineEndingTests` green; quickstart V8–V9.

### Tests for User Story 4 ⚠️ (write first, see them fail)

- [X] T055 [US4] Create `tests/AkmlSql.Formatting.Tests/Parity/Sp047FormatterRulesTests.cs`. It is a theory over 11
  styles × the five `tests/format-parity/corpus/sp047-*.sql` inputs from T004.
  - **The 11 styles:**
    - the 8 `src/AkmlSql.Formatting/Profiles/BuiltIn/*.akmlstyle`, loaded as `FormatParityTests` / `ParityHarness`
      load them;
    - the web's two POCO built-ins, rebuilt inline: `new FormattingProfile()` named "AKML Default", and a second
      with `Casing.ReservedKeywords = "uppercase"` named "ANSI-compact", mirroring
      `src/AkmlSql.Web/Services/IProfileStore.cs:559-581`;
    - `SqlPromptStyles.ToProfile(SqlPromptStyleDocument.CreateDefault("sp047"))`.
  - **Assertions:** rules 1–6 of contracts/formatter.md §2, as text checks on the formatted output; rule 6 formats a
    second time and compares tokens with a ScriptDom token comparison ignoring case, brackets and semicolons.
  - The test must fail for FMT-001, FMT-002 and FMT-003 in every classic style before T058–T060. Log which
    style/rule pairs fail.
- [X] T056 [P] [US4] Create `tests/AkmlSql.Formatting.Tests/Pipeline/LineEndingTests.cs` per contracts/formatter.md
  §4, through `FormatterPipeline`:
  - CRLF input gives CRLF output with no lone `\n`;
  - LF input gives LF;
  - a mostly-LF mixed input gives LF;
  - an empty or one-line input is unchanged;
  - a failed parse returns the original text byte for byte.
- [X] T057 [P] [US4] Extend `tests/AkmlSql.Engine.Tests/Formatter/FormatRequestHandlerTests.cs` with a test: a
  `FormatRequest` whose SQL uses `\r\n` gives `FormattedText` containing `\r\n` and no lone `\n`.

### Implementation for User Story 4

- [X] T058 [US4] FMT-001 in `src/AkmlSql.Formatting/Rules/ParenthesisRules.cs` `ApplyProcedureParameters`
  (lines ~426–480), per research R8.
  - Find the procedure name after `TSqlTokenType.Procedure`: up to three dot-separated identifiers, skipping
    whitespace and comments.
  - Treat the next `(` as the parameter list only if it immediately follows that name.
  - Otherwise apply the "each parameter on a new line" break to top-level commas between the name and the `AS`
    keyword. Parentheses inside a parameter are never lists.
  - FMT-001 rows of T055 pass.
- [X] T059 [US4] FMT-002 in `src/AkmlSql.Formatting/Rules/ControlFlowRules.cs`, per research R9.
  - **Status (implementation):** done. The owner comes from the parse tree (LayoutNode.OwnerTokenIndex), not a token walk. Grew to the root cause (spec A3): CASE END / BEGIN TRAN are not block keywords, and the DML, list and DDL collapses stop at a statement boundary (Layout/StatementBounds), so no statement is joined to another; Khamis Style's indentBeginEndKeywords is honoured. Corpus sp047-02 extended, sp047-06 added.
  - In `ApplyBeginEndRules`, when a `BEGIN` gets a new line, set its `IndentLevel` to the indent of its owner. The
    owner is the nearest preceding `IF`/`ELSE`/`WHILE` node at statement level; with none, one level deeper than
    the enclosing `BEGIN`.
  - Honour the style's documented control-flow indent option (`ControlFlowOptions`).
  - In `ApplyIndentBetweenBeginEnd`, raise a nested `BEGIN` to at least the enclosing block level + 1.
  - FMT-002 rows pass.
- [X] T060 [US4] FMT-003 in `src/AkmlSql.Formatting/Pipeline/LayoutEngine.cs` (lines ~126–176), per research R10.
  When `afterGo && !goCount` is true for the current token, treat it as a batch start: `tracker.Reset()`,
  `baseIndent = 0`, `indentTracker.Reset()` and `statementIndex++`, as for `isBatchStart`. If the SQL Prompt style
  rows of T055 still fail rule 3, fix the "GO 5" path in `src/AkmlSql.Formatting/SqlPrompt/Layout/SqlPromptPrinter.cs`
  (lines ~86–102) the same way. FMT-003 rows pass.
- [X] T061 [US4] FMT-004: fix comment alignment in `src/AkmlSql.Formatting` (the code behind "Align multi-line comments").
  - **Status (implementation):** done in TextEmitter.FormatBlockComment (uniform shift by the opener's move, LayoutNode.OriginalColumn); spec A4: the first statement after leading comments keeps its line break. The spec 030 comment tests that asserted flattening were rewritten.
  - Find the code behind the Khamis Style option "Align multi-line comments": grep `AlignMultiLine` /
    `multiLineComment` in `src/AkmlSql.Formatting`.
  - Change it to shift every line of a block comment by the same offset instead of trimming inner lines.
  - Rule 4 rows pass for `khamis-style` and the other styles.
- [X] T062 [US4] FMT-005, from the failing rule-5 rows of T055, in `src/AkmlSql.Formatting/Layout/` and `src/AkmlSql.Formatting/Rules/ListRules.cs`.
  - **Status (implementation):** done in ListRules.ApplyAlignItemsAcrossClauses (per statement and parenthesis level, no join keywords) and the IN-list default auto (spec A5; six classic built-ins regenerated or edited, Khamis/Collapsed regenerated); ControlFlowRules.CollapseInList rejoins an AlignedLeftBracket bracket. RightAligner/AlignmentCalculator/CollapseEvaluator needed no change. No style needed a by-design exemption.
  - **Join padding:** pad only to keywords present in the same aligned group (`src/AkmlSql.Formatting/Layout/RightAligner.cs`,
    `src/AkmlSql.Formatting/Layout/AlignmentCalculator.cs`), so `INNER JOIN x` and a lone `SET` are not padded.
  - **Short lists:** a two-item `IN` list that fits stays collapsed (`src/AkmlSql.Formatting/Layout/CollapseEvaluator.cs`
    / `src/AkmlSql.Formatting/Rules/ListRules.cs`).
  - **By design:** if one row is by design for a style (keyword columns in ANSI-compact), exempt that style in the
    test with a comment, and state it in that style's `Metadata.Description`, instead of changing the behaviour.
- [X] T063 [US4] Line endings in `src/AkmlSql.Formatting/Pipeline/FormatterPipeline.cs`, per data-model §12.
  - Compute the input's dominant ending before any normalisation.
  - After validation and the idempotency check, convert the output's `\n` to `\r\n` when the input was CRLF.
  - Never touch the return-original path.
  - Make T056 and T057 pass.
- [X] T064 [US4] Capture and review goldens in `tests/format-parity/golden/`.
  - Run
    `$env:AKML_UPDATE_PARITY_GOLDEN=1; dotnet test tests/AkmlSql.Formatting.Tests --filter "FullyQualifiedName~FormatParityTests"`,
    then unset it.
  - Open every new `tests/format-parity/golden/sp047-*` file and confirm it obeys contracts/formatter.md §2.
  - Run `git diff --ignore-space-at-eol --stat -- tests/format-parity/golden`. For every **existing** golden that
    changed, check that the change is one rules 1–5 require, and list it with its reason in `baseline.md`. Revert any
    other change by fixing the code, not the golden (Principle III).
- [X] T065 [US4] Move the web parity baselines (`tests/format-parity/baselines/`) with the goldens if any changed: run
  `$env:AKML_REGEN_PARITY_BASELINE=1; dotnet test tests/AkmlSql.Web.Tests --filter "Category=ParityBaseline"`
  (`tests/format-parity/baselines/{default,ansi}/`), then unset it and review the diff the same way.
- [X] T066 [US4] Run `dotnet test tests/AkmlSql.Formatting.Tests`, `dotnet test tests/AkmlSql.Engine.Tests --filter "FullyQualifiedName~Formatter"`
  and `dotnet test tests/AkmlSql.Web.Tests --filter "FullyQualifiedName~Formatter|Category=ParityBaseline"`. All must
  be green, and the `FormatParityTests` count must be ≥ the baseline in `baseline.md`.

**Checkpoint**: US4 complete. V8 and V9 are validated in T141.

---

## Phase 7: User Story 5 — Advisory findings show one small marker (Priority: P1)

**Goal**: Information and Hint findings get only a margin or gutter marker by default in SSMS and the web, warnings
and errors keep underlines, `squiggleStyle` and `showAdvisoryHints` do what they document, and an opened file is
analysed once the engine connects (UX-001, SSMS-001, SSMS-002; research R13, R14; contracts/analysis-display.md).

**Independent Test**: quickstart V10–V12; the unit tests below.

### Tests for User Story 5 ⚠️ (write first, see them fail)

- [X] T067 [P] [US5] Create `tests/AkmlSql.Core.Tests/Config/SquiggleStyleMigrationTests.cs`, in the pattern of
  `tests/AkmlSql.Core.Tests/Config/TriggerDelayMigrationTests.cs`.
  - A config JSON without `squiggleStyleVersion` and with `"underline"` loads as `"marker"` with version 1.
  - Without the marker and with `"dotted"`, it keeps `"dotted"` and gets version 1.
  - With the marker and `"underline"`, it stays `"underline"`.
  - A new `AppSettings()` has `"marker"`.
  - A throwing migration leaves the settings as read.
- [X] T068 [P] [US5] Create `tests/AkmlSql.Shell.Shared.Tests/DiagnosticTaggerPresentationTests.cs`. Test a new pure
  `internal static bool DiagnosticTagger.ShouldTag(int severity, string? squiggleStyle)`: `"marker"` gives
  false/false/true/true for severities 0/1/2/3; `"underline"`, `"dotted"`, `"solid"`, `null` and `""` give true for
  all severities.
- [X] T069 [P] [US5] Create `tests/AkmlSql.Shell.Shared.Tests/LightbulbAdvisoryHintsTests.cs`. Test a new pure
  `internal static bool LightbulbProvider.OffersFixActions(bool hasAutomaticFix, bool showAdvisoryHints)`: false only
  when `!hasAutomaticFix && !showAdvisoryHints`.
- [X] T070 [P] [US5] Create `tests/AkmlSql.Shell.Shared.Tests/CodeAnalysisPageAdvisoryRowTests.cs` (`[StaFact]`, page
  builder in the pattern of the existing Options page tests).
  - The Code analysis page has an "Advisory findings" dropdown with the items "Margin marker only" and "Underline
    in the text".
  - Load maps `marker` to index 0 and `underline` to index 1.
  - Save writes the value back.
  - The row is registered for search ("Advisory findings").
- [X] T071 [P] [US5] Create `tests/AkmlSql.Shell.Shared.Tests/AnalysisDeferredUntilEngineTests.cs`.
  - **Status (implementation):** done; the client seam is Func<IRpcClientAccessor> (the shell's existing accessor pattern, production EngineRpcClientAccessor.Instance) instead of Func<PipeRpcClient?>; a minimal tests/AkmlSql.Shell.Shared.Tests/FakeTextBuffer.cs backs the controller.
  - Add test seams to `AnalysisController`: `internal static Func<PipeRpcClient?>` for the client, and an
    `internal static Func<Func<Task>, Task<bool>>` for "when engine ready", defaulting to the real
    `ConnectionWiringHelper.WhenEngineReadyAsync(EngineReady, …)`.
  - A controller built with a disconnected client sends no request and registers exactly one deferral.
  - Completing the deferral sends one `RequestAnalyze` and raises `DiagnosticsUpdated` once, with no buffer edit.
  - Building two passes while disconnected still registers one deferral.
- [X] T072 [P] [US5] Extend `tests/AkmlSql.Web.Tests/js/akml-editor.diagnostics.test.mjs`:
  `toLintDiagnostics(doc, diags, { advisoryMarkersOnly: true })` adds `markClass: 'akml-lint-marker-only'` to
  severities 0 and 1 only. With `false` or no options, no `markClass`.
- [X] T073 [P] [US5] Create `tests/AkmlSql.Web.Tests/Settings/AdvisoryDisplaySettingTests.cs`.
  - `IAnalysisSettingsStore` defaults `AdvisoryDisplay` to `"marker"`, and an old stored record without the member
    reads `"marker"`.
  - The Settings › Code analysis row `analysis-advisory-display` saves `underline`.
  - `CodeAnalysisSettingsSection` export includes it, import applies it, and Restore defaults resets it.
  - `SettingsCatalogConsistencyTests` stays green.

### Implementation for User Story 5

- [X] T074 [US5] Change the SSMS config in `src/AkmlSql.Core/Config/AppSettings.cs` and `src/AkmlSql.Core/Config/ConfigManager.cs`.
  - **`src/AkmlSql.Core/Config/AppSettings.cs`** (`CodeAnalysisSettings`, lines ~757–774): `SquiggleStyle` defaults
    to `"marker"` (XML doc: `marker` | `underline`; other values read as `underline`). Add
    `[JsonPropertyName("squiggleStyleVersion")] public int? SquiggleStyleVersion { get; set; }`. Update the
    `ShowAdvisoryHints` doc to the A1 meaning.
  - **`src/AkmlSql.Core/Config/ConfigManager.cs`:** add `internal static void MigrateSquiggleStyle(AppSettings settings)`
    per contracts/analysis-display.md §2, wrapped in try/catch with `Log.Warning` like `MigrateTabEnvironments`.
    Call it beside every `MigrateTriggerDelay(...)` call (lines ~38, 47, 75, 84).
  - Make T067 pass, and keep `AppSettingsTests` and `ConfigManagerTests` green.
- [X] T075 [US5] Implement marker mode in `src/AkmlSql.Shell.Shared/Analysis/DiagnosticTagger.cs`.
  - Add `ShouldTag`. In `GetTags`, skip issues for which `ShouldTag(issue.Severity, style)` is false.
  - Read `style` from the cached settings: make `AnalysisController.CurrentSettings()` `internal static` and call it
    once per `GetTags`.
  - Make T068 pass.
- [X] T076 [US5] Wire `showAdvisoryHints` in `src/AkmlSql.Shell.Shared/Analysis/LightbulbProvider.cs`.
  - Add `OffersFixActions`.
  - Return no suggested-action set for an issue whose fix list is empty when
    `CurrentSettings().ShowAdvisoryHints` is false. The glyph menu (`WarningGlyphMenu.cs`) is untouched.
  - Make T069 pass.
- [X] T077 [US5] In `src/AkmlSql.Shell.Shared/Dialogs/Pages/CodeAnalysisPage.cs`, add an "Advisory findings"
  `ctx.Rows.AddDropdown(...)` in the "Analysis engine" group, with items "Margin marker only" and "Underline in the
  text" and the help text of contracts/analysis-display.md §2.
  - Register it with `ctx.RegisterSearch`.
  - Extend `CodeAnalysisControls` with the combo, loading and saving `settings.CodeAnalysis.SquiggleStyle`
    (index 0 = `marker`, 1 = `underline`).
  - Make T070 pass.
- [X] T078 [US5] Defer analysis until the engine connects, in `src/AkmlSql.Shell.Shared/Analysis/AnalysisController.cs` and `src/AkmlSql.Shell.Shared/Editor/ConnectionWiringHelper.cs`.
  - **`src/AkmlSql.Shell.Shared/Analysis/AnalysisController.cs` `RunAnalysisAsync`:** when the client is missing or
    disconnected, log Debug "Analysis deferred for session {Session}: engine not connected". If no deferral is
    pending (an `int` flag set with `Interlocked`), register one through the T071 seam whose callback calls
    `ScheduleAnalysis()`. Return.
  - **`src/AkmlSql.Shell.Shared/Editor/ConnectionWiringHelper.cs`:** make `WhenEngineReadyAsync` / `EngineReady`
    `internal`. After a successful `SendConnectionChangedAsync`, look up the tracked view's buffer
    `AnalysisController` property and call `TriggerReanalysis()`.
  - Make T071 pass.
- [X] T079 [US5] Add the web setting in `src/AkmlSql.Web/Services/IAnalysisSettingsStore.cs` and its Settings row.
  - **`src/AkmlSql.Web/Services/IAnalysisSettingsStore.cs`:** add `public string AdvisoryDisplay { get; set; } = "marker";`
    to the settings record, with absent records reading `"marker"`.
  - **`src/AkmlSql.Web/Shared/Settings/CodeAnalysisSection.razor`:** add a `SettingRow Id="analysis-advisory-display"`
    select with the two options.
  - **`src/AkmlSql.Web/Services/Settings/SettingsCatalog.cs`:** add the catalog entry, titled "Advisory findings"
    with keywords "semicolon marker underline squiggle information hint".
  - **`src/AkmlSql.Web/Services/Settings/SettingsSections.cs` (`CodeAnalysisSettingsSection`):** export, import and
    reset it.
  - Make T073 pass.
- [X] T080 [US5] Apply marker mode in the web editor (`src/AkmlSql.Web/wwwroot/js/akml-editor.js`, `src/AkmlSql.Web/Shared/EditorComponent.razor`).
  - **`src/AkmlSql.Web/wwwroot/js/akml-editor.js`:** `toLintDiagnostics(doc, diags, options)` adds the `markClass`
    for hint and info when `options?.advisoryMarkersOnly`. `setDiagnostics` and `withDiagnostics` take and pass the
    option. Add `EditorView.theme({'.cm-lintRange.akml-lint-marker-only': { backgroundImage: 'none', background: 'none', textDecoration: 'none' }})`
    to the extensions created at `create` time, beside `lintGutter()` (around line 1239).
  - **`src/AkmlSql.Web/Shared/EditorComponent.razor`:** pass
    `new { advisoryMarkersOnly = settings.AdvisoryDisplay != "underline" }` at the three `setDiagnostics` call
    sites (~302, ~942, ~962), reading the store once and re-reading it when the Settings page saves (the store's
    existing reload/changed hook).
  - Make T072 pass.
- [X] T081 [US5] Update the docs in `doc/configuration.md` and `doc/analysis-rules.md`.
  - **`doc/configuration.md`:** in the `codeAnalysis` table (line ~281) and the example JSON (line ~138), set
    `squiggleStyle` to `marker` (default) | `underline`, and add `squiggleStyleVersion` (marker, written once) and
    `showAdvisoryHints` (A1 meaning).
  - **`doc/analysis-rules.md`:** make the global settings table (line ~392) match.
- [X] T082 [US5] Run the story's tests (`tests/AkmlSql.Core.Tests`, `tests/AkmlSql.Shell.Shared.Tests`, `tests/AkmlSql.Web.Tests`).
  - **Status (implementation):** done 2026-11-21: Core Config 348/348; Ssms22 MSBuild Release ok; Shell.Shared 1103/1103 (OptionsLiveSettingsTests allow list gained the Advisory findings row, as its doc requires); Web Settings|Editor 451/451; node 318/318.
  - `dotnet test tests/AkmlSql.Core.Tests --filter "FullyQualifiedName~Config"`.
  - Build `src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj` with MSBuild, then
    `dotnet test tests/AkmlSql.Shell.Shared.Tests`.
  - `dotnet test tests/AkmlSql.Web.Tests --filter "FullyQualifiedName~Settings|FullyQualifiedName~Editor"` and the
    node tests.
  - All must be green.

**Checkpoint**: US5 complete. V10–V12 need a live SSMS desktop; they are validated in T141.

---

## Phase 8: User Story 6 — Syntax errors are reported clearly in the web editor (Priority: P1)

**Goal**: unparseable SQL shows an Error in Problems and a red mark after Analyse, and Parse works offline, marks the
error and reports "Parse failed" (WEB-005; research R15; contracts/analysis-display.md §4).

**Independent Test**: quickstart V13; the tests below.

### Tests for User Story 6 ⚠️ (write first, see them fail)

- [X] T083 [P] [US6] Create `tests/AkmlSql.Analysis.Tests/ParseErrorFindingTests.cs` (construct `AnalysisEngine` as
  - **Status (implementation):** done; 5 facts in tests/AkmlSql.Analysis.Tests/ParseErrorFindingTests.cs, including a GO repeat count (GO 3) that must not be a SYNTAX finding: ScriptDom alone rejects GO n (error 46010), SSMS accepts it, so TsqlParserService.IsGoRepeatCount filters it (found when the web parity case sp047-03 failed).
  `src/AkmlSql.Web/Services/IAnalyserService.cs` does).
  - **`reportParseErrors: true`:**
    - `SELEC * FROM dbo.Customers;` gives exactly one issue with `RuleId == "SYNTAX"`, `Severity == 3`, `Line == 1`,
      the column of the parser error, and the parser's message;
    - a script whose first batch is valid and whose second batch (after `GO`) is invalid gives the `SYNTAX` issue
      plus that batch's rule findings;
    - `Enabled = false` settings give no issues.
  - **Default (`false`):** no `SYNTAX` issue, as today.
- [X] T084 [P] [US6] Extend `tests/AkmlSql.Web.Tests/Services/AnalyserServiceTests.cs`: `AnalyseAsync("SELEC * FROM dbo.Customers;")`
  returns a `SYNTAX` Error issue, and a PostgreSQL gate result is unchanged.
- [X] T085 [P] [US6] Create `tests/AkmlSql.Web.Tests/Editor/ProblemsSyntaxEmptyStateTests.cs` (bUnit,
  - **Status (implementation):** done as tests/AkmlSql.Web.Tests/Results/ProblemsSyntaxErrorTests.cs (beside the other Problems-list tests, not Editor/).
  `ProblemsListComponent.razor`): with a `SYNTAX` issue among others, it is listed first and "No problems found." is
  absent; with no issues, the empty state shows.
- [X] T086 [P] [US6] Create `tests/AkmlSql.Web.Tests/Editor/OfflineParseTests.cs` (bUnit on `Pages/Editor.razor` with
  - **Status (implementation):** replaced: a bUnit render of Pages/Editor.razor cannot run Parse (StateHasChanged with no renderer), so the decisions moved into the pure Services/SyntaxCheck.cs (CanParse, OnServer, InBrowser, Outcome, FirstError) covered by tests/AkmlSql.Web.Tests/Editor/SyntaxCheckTests.cs (15 pass); the page wiring is covered end to end by ParseMarkerBrowserTests (T087).
  the existing editor test host and fakes).
  - **No connection:** the Parse command is enabled; after Parse of `SELEC * FROM dbo.Customers;`:
    - the Messages contain "Checked in the browser; the server was not asked." and one
      `Msg 102, Level 15, Line 1: …` line;
    - the tab's `ErrorMark` is line 1, severity 3;
    - the status text is "Parse failed: 1 syntax error.".
  - **Connected with `execute.v2`:** the fake `IQueryExecutionService.ParseAsync` is used, and the first error with a
    line sets `ErrorMark` without a click.
  - **Valid SQL:** "Parse succeeded.".
- [X] T087 [US6] Create `tests/AkmlSql.Web.E2E.Tests/ParseMarkerBrowserTests.cs` (Playwright, no engine started).
  - Set the editor text through `akml-editor.js setText` (CLAUDE.md "Driving the web app").
  - Run Analyse: the Problems pane shows an Error row, and `.cm-lintRange-error` exists.
  - Press Ctrl+F5: the status shows "Parse failed: 1 syntax error.", and the Messages pane shows the
    browser-check line.

### Implementation for User Story 6

- [X] T088 [US6] Add the optional parameter `bool reportParseErrors = false` to `AnalyzeAsync` in
  `src/AkmlSql.Analysis/AnalysisEngine.cs` (around lines 60–160).
  - After `parser.Parse(request.DocumentText, out var errors)`, when the flag is set and analysis is enabled, add one
    `CodeIssueInfo` per `ParseError`: RuleId `SYNTAX`, Severity 3, Message `e.Message`, Line `e.Line`, Column
    `e.Column`, and EndColumn = end of the token at `e.Offset` when available.
  - Add them before rule findings, outside the per-batch cache, whether or not `script` is null; return them when
    `script` is null.
  - Make T083 pass.
- [X] T089 [US6] Pass `reportParseErrors: true` from `src/AkmlSql.Web/Services/IAnalyserService.cs`
  (`_engine.AnalyzeAsync(...)`, line ~114). The engine's SSMS handler and the CLI are unchanged. Make T084 pass.
- [X] T090 [US6] In `src/AkmlSql.Web/Shared/ProblemsListComponent.razor`, order `SYNTAX` issues first and never
  render the "No problems found." empty state while any issue exists (verify the current condition; adjust only if
  needed). Make T085 pass.
- [X] T091 [US6] Implement offline Parse in `src/AkmlSql.Web/Pages/Editor.razor` (`CanParse`, `ParseAsync`, around
  - **Status (implementation):** done; Parse decides through Services/SyntaxCheck.cs; RunSnapshot gained an optional Outcome so a tab switch keeps the Parse words; a run clears them (Begin/Complete/Fail).
  lines 1292–1321; the toolbar title around line 94).
  - `CanParse = !Running && !IsPostgresTab` (no connection needed).
  - When not connected or the engine lacks `execute.v2`, parse in the browser with a `TsqlParserService`. Build an
    `ExecuteQueryResult` whose `MessageDetails` hold the info line and one error per `ParseError`
    (`AkmlSql.Core.Text.SqlMessageText`-compatible fields: Number 102, Class 15, Line) and show it through the
    existing `ShowResult`.
  - Either way, after the result, set `tab.ErrorMark` from the first message with a line (the same
    `ExecuteLineMapper` mapping `OnNavigateToLineAsync` uses) and call `ShowDiagnosticsAsync`.
  - Set the status text through `src/AkmlSql.Web/Services/IWorkspaceStatus.cs`: add the constants
    `ParseFailedOne = "Parse failed: 1 syntax error."`, `ParseFailedMany = "Parse failed: {0} syntax errors."` and
    `ParseSucceeded = "Parse succeeded."`, used for Parse only. Run status keeps `FailedText`.
  - Update the button title per contracts/analysis-display.md §4.
  - Make T086 and T087 pass.
- [X] T092 [US6] Run `dotnet test tests/AkmlSql.Analysis.Tests`, `dotnet test tests/AkmlSql.Engine.Tests --filter "FullyQualifiedName~Analysis"`
  - **Status (implementation):** done 2026-11-21: Analysis.Tests 10/10; Engine Analysis 407/407 (the SSMS path is unchanged); Web full 2252/2252; E2E ParseMarkerBrowserTests + ResultsMessagesTests 12/12, serial, Debug web + engine builds of the working tree.
  (the SSMS path is unchanged), `dotnet test tests/AkmlSql.Web.Tests`, and the E2E classes `ParseMarkerBrowserTests`
  and `ResultsMessagesTests` (serial). All must be green.

**Checkpoint**: US6 complete. V13 is validated in T141.

---

## Phase 9: User Story 7 — Workspace features do what they promise (Priority: P2)

**Goal**: custom snippets complete and expand; the explorer explains incomplete metadata and empty filters; the
Expand refactorings use the cached schema; the Refactor dialog has keys and focus; imports explain themselves; Find
Invalid Objects reports the references it checked (SN-001, WEB-013, WEB-011, WEB-014, WEB-012, UX-002, SSMS-003;
research R16–R20; contracts/workspace-ai.md).

**Independent Test**: quickstart V14–V19; the tests below.

### Contracts first

- [X] T093 [US7] Add the payload flag and stub types in `src/AkmlSql.Core/Ipc/Messages/SchemaPhasePayload.cs`, `src/AkmlSql.IntelliSense/Schema/DatabaseCache.cs`, `src/AkmlSql.Web/Services/ISnippetStore.cs` and a new `src/AkmlSql.Web/Services/SchemaSnapshotCache.cs`.
  - **Status (implementation):** done; SchemaSnapshotCache was not written (spec amendment A7): the existing SchemaPhaseRehydrator already maps what data-model §11 lists. Services/RefactorSchema.cs holds the state (From, LoadAsync), the menu reasons and RefactorMenuItem. ISnippetStore.Changed, SchemaPhasePayload key 8 and DatabaseCache.MetadataLimited as listed.
  - **`src/AkmlSql.Core/Ipc/Messages/SchemaPhasePayload.cs`:** append `[Key(8)] public bool MetadataLimited { get; set; }`
    to `SchemaPhasePayload`, with a doc comment per data-model §8.
  - **`src/AkmlSql.IntelliSense/Schema/DatabaseCache.cs`:** add `public bool MetadataLimited { get; set; }`.
  - **`src/AkmlSql.Web/Services/ISnippetStore.cs`:** add `event Action? Changed;` to `ISnippetStore`, raised later.
  - **Snapshot converter:** create `src/AkmlSql.Web/Services/SchemaSnapshotCache.cs` with
    `public static class SchemaSnapshotCache { public static DatabaseCache ToDatabaseCache(SchemaPhasePayload payload) => throw new NotImplementedException(); }`.

### Tests for User Story 7 ⚠️ (write first, see them fail)

- [X] T094 [P] [US7] Create `tests/AkmlSql.Web.Tests/Completion/CompletionUserSnippetTests.cs`, with a fake bridge
  - **Status (implementation):** done (12 facts). The online trigger also covers a statement start (spec amendment A6): the engine filters its list by the typed word, so its reply for qaz holds no snippet.
  and a fake `ISnippetStore`, in the pattern of `CompletionServiceOfflineTests.cs`.
  - **Online:** a response containing a built-in snippet item gets the user's SQL Server snippets appended (mapping
    of data-model §10); a response with no snippet item gets none.
  - **Offline:** at a statement start, user snippets are included; mid-clause, they are not.
  - **Dialect:** a PostgreSQL gate adds only kind-1 snippets.
  - **Collisions:** a user shortcode equal to a built-in replaces that item.
  - **Cache:** raising `Changed` drops the cached list (a second call reads the store again).
- [X] T095 [P] [US7] Create `tests/AkmlSql.Web.E2E.Tests/SnippetsInEditorTests.cs` (Playwright).
  - **Status (implementation):** done; the test waits for qaz to be the selected option of a settled list before Tab (CodeMirror interactionDelay).
  1. On `/snippets`, create `qaz` with body `SELECT $CURSOR$ FROM dbo.Region;` (SQL Server kind) and save.
  2. Open `/`, focus the editor, type `qaz` and press Tab. `.cm-line` text is `SELECT  FROM dbo.Region;`, with the
     caret after `SELECT `.
  3. Delete `qaz` on `/snippets`; back in a new tab, `qaz` + Tab leaves `qaz` as typed.
- [X] T096 [P] [US7] Create `tests/AkmlSql.Engine.Tests/Schema/PhaseAMetadataLimitedTests.cs`.
  - **Status (implementation):** done; the live fact ran here: NT AUTHORITY\SYSTEM lacks VIEW DEFINITION on Northwind.
  - `SchemaPhaseSerializer.SerializePhaseA` of a cache with `MetadataLimited = true` round-trips to `true`.
  - A payload serialized without key 8 (a hand-built MessagePack map of keys 0–7) deserializes to `false`.
  - **Live (`[SkippableFact]`, skips unless `TryReachLocalSql`-style `(local)`/Northwind is reachable):** open a
    connection to Northwind and call the new probe (T100) after `EXECUTE AS LOGIN = N'NT AUTHORITY\SYSTEM'`. It
    returns limited = true, and after `REVERT` false. EXECUTE AS is session-scoped, so nothing in the database
    changes. If `EXECUTE AS` is refused (the runner lacks IMPERSONATE), the test calls `Skip` with that reason.
- [X] T097 [P] [US7] Create `tests/AkmlSql.Web.Tests/Bridge/SchemaTreeMetadataLimitedTests.cs` (bUnit,
  `SchemaTreeComponent.razor`, in the pattern of `SchemaTreeComponentTests.cs`).
  - **`MetadataLimited = true`:** the headers read "Stored Procedures (may be incomplete)" and "Functions (may be
    incomplete)", and `schema-metadata-limited` names `IEngineBridge.EngineRunsAs` ("NT AUTHORITY\SYSTEM") and the
    database. Null `RunsAs` gives "the engine's account".
  - **`false`:** counts as today.
- [X] T098 [P] [US7] Create `tests/AkmlSql.Web.Tests/Bridge/SchemaTreeFilterEmptyTests.cs` (bUnit): filter `zzzz`
  with no match shows `schema-filter-empty` "No objects match 'zzzz'."; `schema-filter-clear` clears the filter and
  restores the expanded groups.
- [X] T099 [P] [US7] Create two refactoring test files in `tests/AkmlSql.Web.Tests/Refactoring/`, with a committed fixture
  - **Status (implementation):** done as RefactorSchemaTests, RefactorExpandWithSnapshotTests and RefactorMenuTests, with the payload built in code instead of a JSON fixture. The converter half is SchemaPhaseRehydratorTests (IntelliSense.Tests), which already covered it. RefactorPreviewKeyboardTests was added for T109.
  `tests/AkmlSql.Web.Tests/Fixtures/northwind-region-phaseb.json`: a `SchemaPhasePayload` (JSON form) with `dbo.Region`
  (`RegionID int`, `RegionDescription nchar(50)`) and `dbo.CustOrderHist` (parameter `@CustomerID nchar(5)`).
  - `tests/AkmlSql.Web.Tests/Refactoring/SchemaSnapshotCacheTests.cs`: the converter maps schemas, objects, types,
    columns, parameters and foreign keys (`RebuildFkIndex` lookups work), and Phase A input gives objects with no
    columns.
  - `tests/AkmlSql.Web.Tests/Refactoring/RefactorExpandWithSnapshotTests.cs`: Expand INSERT columns on
    `insert into dbo.Region values (9,'x')` with the cache gives `(RegionID, RegionDescription)`. Expand EXEC
    parameters on `exec dbo.CustOrderHist 'ALFKI'` gives `@CustomerID = 'ALFKI'`. The menu reason texts of
    contracts/workspace-ai.md §3 are covered for Phase A only, no snapshot, and heavy items without the capability.
- [X] T100 [P] [US7] Create `tests/AkmlSql.Web.E2E.Tests/RefactorMenuKeyboardTests.cs` (Playwright): open Refactor
  from the toolbar; Escape closes the menu and focus returns to the Refactor button; open it again, Arrow/Tab to
  "Remove semicolons" and press Enter, and the preview opens; Escape closes the preview and focus is on the Refactor
  button.
- [X] T101 [P] [US7] Create two import test files in `tests/AkmlSql.Web.Tests/Styles/` and `tests/AkmlSql.Web.Tests/Snippets/`.
  - `tests/AkmlSql.Web.Tests/Styles/StyleImportMessageTests.cs` (bUnit `/styles` with `FakeStylesEngine`):
    importing a `.json` that is not a style, and a non-JSON file, shows "That file is not a SQL Prompt style
    (.json)." and no exception text (assert that no `Exception`, `at ` or `JsonReaderException` substring appears).
  - `tests/AkmlSql.Web.Tests/Snippets/SnippetImportMessageTests.cs` (bUnit `/snippets`): a valid snippet shows
    "Imported 'x'."; a style JSON and a `.txt` show "No snippet was found in that file. Existing snippets are
    unchanged."; the status element has `aria-live="polite"`.
- [X] T102 [P] [US7] Create the Find Invalid Objects tests in `tests/AkmlSql.Engine.Tests/Refactoring/` and `tests/AkmlSql.Shell.Shared.Tests/`.
  - **Status (implementation):** done; the live Northwind count ran here (63 = the independent COUNT).
  - `tests/AkmlSql.Engine.Tests/Refactoring/FindInvalidObjectsCountTests.cs`: a `[SkippableFact]` live test on
    Northwind, where `TotalScanned` equals an independent
    `SELECT COUNT(*) FROM sys.sql_expression_dependencies d JOIN sys.objects o ON o.object_id = d.referencing_id WHERE o.is_ms_shipped = 0`
    and `Records` is empty. A pure test of the new count/candidate split if T106 extracts a pure helper.
  - `tests/AkmlSql.Shell.Shared.Tests/FindInvalidObjectsDialogTextTests.cs`: a pure
    `FindInvalidObjectsDialog.SummaryText(int records, int totalScanned)` returns the three texts of data-model §9,
    and a pure `ShowsGrid(int records)` is false for 0.

### Implementation for User Story 7

- [X] T103 [US7] SN-001 in `src/AkmlSql.Web/Services/ISnippetStore.cs` and `src/AkmlSql.Web/Services/ICompletionService.cs`.
  - **`src/AkmlSql.Web/Services/ISnippetStore.cs` (`SnippetStore`):** raise `Changed` after `SaveAsync` and
    `DeleteAsync`. The Snippets page import goes through `SaveAsync`.
  - **`src/AkmlSql.Web/Services/ICompletionService.cs`:** add an optional `ISnippetStore? snippets = null`
    constructor parameter. Cache `ListAsync(kind)` results (user snippets only, `!IsBuiltIn`) per kind, cleared on
    `Changed`. Merge them per contracts/workspace-ai.md §1 into the online response (when it contains an
    `ObjectType == Snippet` item) and into `BuildFromCacheAsync` (at a statement start computed from
    `liveDocumentText`).
  - DI in `src/AkmlSql.Web/Program.cs` resolves the parameter automatically. Verify with a quick DI test or by
    running the app.
  - Make T094 and T095 pass.
- [X] T104 [US7] WEB-013, engine side, in `src/AkmlSql.Engine/Schema/SchemaMetadataService.cs` and `src/AkmlSql.Engine/Handlers/Schema/SchemaPhaseSerializer.cs`.
  - **`src/AkmlSql.Engine/Schema/SchemaMetadataService.cs`:** add
    `internal static async Task<bool> ReadMetadataLimitedAsync(SqlConnection conn, CancellationToken ct)`, running
    `SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION')`. A `0` gives true; null or an error gives
    false, logged at Debug. Call it in Phase A on the open connection after the objects reader closes (before
    `LoadLinkedServersAsync`) and set `cache.MetadataLimited`. Log it in the "Phase A complete" line.
  - **`src/AkmlSql.Engine/Handlers/Schema/SchemaPhaseSerializer.cs` `BuildPayload`:** set
    `MetadataLimited = cache.MetadataLimited`.
  - PostgreSQL caches leave it false.
  - Make T096 pass.
- [X] T105 [US7] WEB-013 and WEB-014 in the web, in `src/AkmlSql.Web/Shared/SchemaTreeComponent.razor`.
  - In `src/AkmlSql.Web/Shared/SchemaTreeComponent.razor`, read `_payload.MetadataLimited`. When true, render the
    two group headers with "(may be incomplete)" and no count, and the first child note `schema-metadata-limited`
    with the contracts/workspace-ai.md §2 text (`Bridge.EngineRunsAs`, the payload's `DatabaseName`).
  - When a filter is set and every enumerated group is empty, render `schema-filter-empty` with the
    `schema-filter-clear` button, which clears the filter and restores the pre-filter `_expanded` set.
  - Make T097 and T098 pass.
- [X] T106 [US7] SSMS-003 in `src/AkmlSql.Engine/Handlers/Refactoring/FindInvalidObjectsHandler.cs` and `src/AkmlSql.Shell.Shared/Refactoring/FindInvalidObjectsDialog.cs`.
  - **`src/AkmlSql.Engine/Handlers/Refactoring/FindInvalidObjectsHandler.cs` `ReadDependencyRowsAsync`:** prepend
    the count query of contracts/workspace-ai.md §5 to the same command, read the scalar first, then
    `NextResultAsync` for the candidate rows. Return `(rows, totalChecked)` and set
    `TotalScanned = totalChecked`. Update the Debug log to "checked {Total} references, {Invalid} invalid".
  - **`src/AkmlSql.Shell.Shared/Refactoring/FindInvalidObjectsDialog.cs`:** add `SummaryText` and `ShowsGrid`, use
    them for the label (line ~55), and do not add the grid when `ShowsGrid` is false.
  - Make T102 pass.
- [X] T107 [US7] WEB-011, service side, in `src/AkmlSql.Web/Services/SchemaSnapshotCache.cs` and `src/AkmlSql.Web/Services/IRefactoringService.cs`.
  - Implement `SchemaSnapshotCache.ToDatabaseCache` in `src/AkmlSql.Web/Services/SchemaSnapshotCache.cs` per
    data-model §11, using the `DatabaseCache`, `SchemaEntry`, `DatabaseObject`, `Column`, `Parameter` and
    `ForeignKey` models of `AkmlSql.IntelliSense`, then `RebuildFkIndex()`.
  - In `src/AkmlSql.Web/Services/IRefactoringService.cs`, add an optional `DatabaseCache? schema = null` to
    `PreviewLightweightAsync` / `ApplyLightweightAsync` and set `RefactoringContext.SchemaCache = schema` in
    `RunLightweight`. Update the interface, the implementation and the fakes.
  - Make the converter half of T099 pass.
- [X] T108 [US7] WEB-011 and WEB-012 in the Refactor menu in `src/AkmlSql.Web/Pages/Editor.razor` (lines ~365–407 and
  - **Status (implementation):** done; the menu is Shared/RefactorMenu.razor (buttons, reasons with aria-describedby, Arrow/Home/End, focus stash/trap/restore); Editor.razor builds its items from RefactorSchema.
  the handlers `PreviewLightweightAsync` / `CloseRefactorMenu`).
  - **Items:** render each item as `<button type="button">` inside its `<li>`.
  - **Expand items:** compute the state of the three from the active tab's snapshot through
    `ISchemaCacheStore.GetActiveAsync(SqlConn)` (Phase B → enabled; Phase A only / none → disabled with
    `aria-describedby` reason text). When enabled, pass `SchemaSnapshotCache.ToDatabaseCache(phaseB)` and show the
    operation warnings in the preview.
  - **Heavy items:** the reason is "Not available in the browser yet." when connected without `refactoring.heavy`,
    and "Needs a paired engine." with no engine.
  - **Focus:** on open, call `akml-ui.js` `stashFocus` and `focusFirst`, and `trapTab` on the dialog. On
    `keydown` Escape, close it and `restoreFocus`.
  - Make the menu half of T099, and T100's menu part, pass.
- [X] T109 [US7] WEB-012 preview in `src/AkmlSql.Web/Shared/RefactorPreviewPanel.razor`: the same focus pattern
  (focus Apply on open, trap Tab, Escape = Cancel with focus restored to the Refactor button). The existing
  `RefactorPreviewPanelTests.cs` stays green, and T100 passes completely.
- [X] T110 [US7] UX-002 in `src/AkmlSql.Web/Pages/Styles.razor` and `src/AkmlSql.Web/Pages/Snippets.razor`.
  - **Status (implementation):** done; Styles also refuses JSON that has neither SQL Prompt metadata nor a known option (SqlPromptStyleDocument.LooksLikeStyle); the Snippets status is always in the page.
  - **`src/AkmlSql.Web/Pages/Styles.razor` `ImportAsync`** (lines ~726–790): catch `JsonException`,
    `InvalidDataException` and the importer's own exceptions, show "That file is not a SQL Prompt style (.json).",
    and log `ex.Message` to diagnostics at Info.
  - **`src/AkmlSql.Web/Pages/Snippets.razor` `OnImportAsync`** (lines ~443–479): for a missing shortcode or a
    `JsonException`, set "No snippet was found in that file. Existing snippets are unchanged.". Call
    `StateHasChanged()` after every outcome, and give the status element `aria-live="polite"`.
  - Make T101 pass.
- [X] T111 [US7] Run `dotnet test tests/AkmlSql.Engine.Tests --filter "FullyQualifiedName~Schema|FullyQualifiedName~FindInvalid"`,
  - **Status (implementation):** done 2026-11-21: Engine Schema|FindInvalid 268/268 (20 PostgreSQL live skips); Ssms22 MSBuild ok; Shell.Shared 1111/1111; Web 2302/2302; node 318/318; IntelliSense 25/25 (corpus gate); E2E SnippetsInEditor + RefactorMenuKeyboard + QuickstartAcceptance + ShellPolish 21/21 after the T095 test-timing fix.
  build the SSMS project with MSBuild and run `dotnet test tests/AkmlSql.Shell.Shared.Tests`, then
  `dotnet test tests/AkmlSql.Web.Tests` and `node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"`. Then run the E2E
  classes `SnippetsInEditorTests`, `RefactorMenuKeyboardTests`, `QuickstartAcceptanceTests` and `ShellPolishTests`
  (serial). Also run `dotnet test tests/AkmlSql.IntelliSense.Tests`: the `CorpusGateTests` score must be ≥ the
  baseline.

**Checkpoint**: US7 complete. V15 needs the owner's go-ahead for `GRANT VIEW DEFINITION`.

---

## Phase 10: User Story 8 — AI prompts are coherent in every privacy mode (Priority: P2)

**Goal**: no empty schema section or "provided schema" rule without a schema; web Fix needs an error (AI-001,
AI-002; research R21; contracts/workspace-ai.md §6).

**Independent Test**: `PromptPrivacyModeTests`, `AiPanelFixWithoutErrorTests`; quickstart V20.

### Tests for User Story 8 ⚠️ (write first, see them fail)

- [X] T112 [P] [US8] Create `tests/AkmlSql.AI.Tests/PromptPrivacyModeTests.cs`. For Explain, Fix, Optimize, Text to
  SQL, Index Analysis, Ghost text and Chat (the builders in `src/AkmlSql.AI/Prompts/`) × SQL Server and PostgreSQL:
  - with `schemaText = ""`, the system prompt contains neither "following schema:" nor "provided schema" nor
    "provided in the schema context", and does contain "No database schema is shared";
  - with a non-empty schema, it contains "following schema:" and the schema text, as today.
- [X] T113 [P] [US8] Create `tests/AkmlSql.Web.Tests/Ai/AiPanelFixWithoutErrorTests.cs` (bUnit `AiPanel.razor`, in the
  pattern of `AiPanelTests.cs`):
  - a `LastErrorProvider` returning null gives `ai-fix` disabled with the title "Run the query first: Fix needs the
    error the server returned.", and clicking sends no request;
  - an error gives the button enabled, and the sent prompt contains the error text and no "(no error)".

### Implementation for User Story 8

- [X] T114 [US8] Make the prompts conditional in `src/AkmlSql.AI/Prompts/`.
  - **Status (implementation):** done; PromptTemplates.NoSchemaSentence and WithoutSchema (line-based, so the raw strings' line endings do not matter) serve both dialects; with a schema the prompts are unchanged character for character (PromptDialectTests goldens green).
  - In `src/AkmlSql.AI/Prompts/PromptTemplates.cs` (`BuildSystemPrompt` and `BuildPostgreSqlSystemPrompt`, lines
    ~23–70): when `string.IsNullOrWhiteSpace(schemaText)`, omit the schema sentence, the block and the
    provided-schema rule, and add the no-schema sentence of contracts/workspace-ai.md §6.
  - In `src/AkmlSql.AI/Prompts/IndexAnalysisPrompt.cs` and `src/AkmlSql.AI/Prompts/OptimizePrompt.cs`, select a
    no-schema instruction variant when the schema is blank: drop "(provided in the schema context)" and "Do not
    suggest indexes that already exist in the schema context", and add "Existing indexes are unknown; say which ones
    to check first.".
  - Make T112 pass, and keep `PromptDialectTests` and `SchemaContextAssemblyTests` green.
- [X] T115 [US8] In `src/AkmlSql.Web/Shared/AiPanel.razor`, disable the Fix action (`ai-fix`) while
  - **Status (implementation):** done; HasError is read on every render (the page re-renders the panel when a run finishes), and RunFixAsync refuses without an error too; the button gained data-testid ai-fix.
  `LastErrorProvider` returns null for the current tab (re-evaluated when a run finishes). Set the title per the
  contract, and never build Fix with the no-error placeholder. Make T113 pass.
- [X] T116 [US8] Run `dotnet test tests/AkmlSql.AI.Tests` and `dotnet test tests/AkmlSql.Web.Tests --filter "FullyQualifiedName~Ai"`,
  - **Status (implementation):** done 2026-11-21: AI.Tests 122/122; Web ~Ai 366/366; E2E UserStory5AiTests 3/3.
  then the E2E class `UserStory5AiTests` (serial, with the mock provider as that class sets it up). All must be green.

**Checkpoint**: US8 complete.

---

## Phase 11: User Story 9 — The documentation explains connections, credentials and common symptoms (Priority: P2)

**Goal**: the browser connection, credentials and storage are explained; the five web symptoms have headed entries;
summaries and links are valid; screenshots match (DOC-001…003; research R22; contracts/cli-docs-a11y.md §2–3).
**Depends on** US2 and US3 for the final wording and the screenshots, and on US7 for the WEB-013 entry.

**Independent Test**: `DocsQualityTests`; quickstart V21.

### Tests for User Story 9 ⚠️ (write first, see them fail)

- [X] T117 [US9] Create `tests/AkmlSql.Site.Tests/Docs/DocsQualityTests.cs` with the four checks of
  - **Status (implementation):** done; the links check reads the rendered href values (a relative one the renderer could not route is a site 404), so no renderer hook was needed. HEAD's doc/: 8 of 8 failed, exactly configuration's summary and link, the five headings and the connecting text.
  contracts/cli-docs-a11y.md §3. Load the real `doc/` tree the way `DocsContentServiceTests.cs` does.
  - **Summary length:** every published document has `Summary.Length >= 50`.
  - **Links:** no rendered `href` that is relative and not rewritten to a published route. Detect it through the
    renderer's rewrite outcome; add an `internal` accessor or list on `MarkdownRenderer` only if none exists.
  - **Symptom headings:** the troubleshooting page's headings contain each symptom phrase.
  - **Connecting text:** the connecting page contains "Remember" and "engine machine".
  - It must fail at least on `configuration`'s summary, the `deployment.md#web-edition` link and the missing
    headings.

### Implementation for User Story 9

- [X] T118 [P] [US9] Add the section "Connecting in the browser" to `doc/topics/connecting.md`, with the content list
  - **Status (implementation):** done; the wording is the dialog's own (the Remember label and hint, the three Save outcomes, Forget it); Export and Import settings are said to move preferences only, as they do.
  of contracts/cli-docs-a11y.md §2. Its wording must match the dialog texts implemented in T047 and T048 (copy them
  exactly).
- [X] T119 [P] [US9] Add the section "Web edition" to `doc/topics/troubleshooting.md`, with five `###` entries:
  "Handshake failed", "No engine paired yet", "My saved connections are gone", "It asks for my password again" and
  "Stored procedures are missing from the explorer". Each gives symptom → cause → fix, using the texts from T030,
  T047, T052 and T105, and the INSTALL-SUMMARY.txt "Bridge port" line.
- [X] T120 [P] [US9] Add the IIS note to `doc/topics/browser-setup.md`: page and engine ports, the installer
  publishing `akml-engine.json`, and a link to the troubleshooting section.
- [X] T121 [P] [US9] In `doc/configuration.md`, replace the first paragraph with "Every `config.json` setting of AKML
  SQL and the per-project `.casettings` file, with defaults and examples.", keeping the path code block after it.
  Change `[deployment](deployment.md#web-edition)` (line ~62) to a published page (`/docs/topics/browser-setup`).
- [X] T122 [US9] Re-run T117. Fix every remaining summary or link it reports in other published pages under `doc/`
  - **Status (implementation):** done; after T121 no other published page fails (the only short summary and the only unserved link were configuration's).
  (content only), until it is green.
- [X] T123 [US9] Retake the affected screenshots for `doc/topics/` with the existing docs screenshot flow: see
  - **Status (implementation):** done in part: tests/AkmlSql.Web.E2E.Tests/DocsWebScreenshotTour.cs (opt-in, AKML_CAPTURE_DOC_SCREENSHOTS=1) took doc/images/web/connect-remember.png, status-no-engine.png and status-engine-not-answering.png, now shown in connecting.md and troubleshooting.md. Settings › Connections › Paired browsers is listed only by an engine that keeps pairings (LAN mode, PIN), so that shot waits for a LAN install (as T037).
  `tests/AkmlSql.Site.Tests/DocsScreenshotsTests.cs` and `tests/AkmlSql.Web.E2E.Tests/WorkspaceScreenshotTour.cs`
  for where docs images come from. The shots are the Connect to database dialog with Remember and its hint,
  Settings › Connections with Paired browsers, and the status-bar failure notice. Update the image references in
  the pages above.
- [X] T124 [US9] Run `dotnet test tests/AkmlSql.Site.Tests`; it must be green.
  - **Status (implementation):** done 2026-11-22: Site.Tests 925/925 (917 + the 8 DocsQualityTests).

**Checkpoint**: US9 complete. V21 is validated in T141 against a local site preview, never the deployed site.

---

## Phase 12: User Story 10 — The web editor is accessible (Priority: P3)

**Goal**: the editor has a name, badges meet contrast in three themes, the axe best-practice findings are fixed, and
an automated gate keeps them fixed (WEB-007, WEB-008, WEB-009; research R23; contracts/cli-docs-a11y.md §4).

**Independent Test**: `AxeAccessibilityTests` with 0 violations; quickstart V22.

### Tests for User Story 10 ⚠️ (write first, see them fail)

- [X] T125 [P] [US10] Extend `tests/AkmlSql.Web.Tests/js/akml-editor.attributes.test.mjs`: the content attributes
  include `aria-label` `"SQL query editor — SQLQuery1"` for a tab named SQLQuery1, `aria-multiline="true"` and
  `role="textbox"`, and the label follows a tab switch.
- [X] T126 [P] [US10] Create `tests/AkmlSql.Web.E2E.Tests/AxeAccessibilityTests.cs`, using `Deque.AxeCore.Playwright`
  from T003 and the `WebAppFixture` harness.
  - For `/`, `/snippets`, `/styles`, `/history`, `/settings` and `/settings/connections`, in light, dark and high
    contrast (set as the existing theme tests do), run axe with tags `wcag2a`, `wcag2aa` and `best-practice`.
  - Assert zero violations, printing each violation id and its selectors to `ITestOutputHelper`.
  - `/settings/connections` needs a paired sandbox engine for the Active badge; use `SandboxEngine` as other classes
    do.
- [X] T127 [P] [US10] Extend `tests/AkmlSql.Web.E2E.Tests/TabStripKeyboardTests.cs`: `doc-tab-close` is not a
  descendant of `doc-tab`; Tab reaches the close button; Delete, middle click and Ctrl+Alt+W still close a tab.

### Implementation for User Story 10

- [X] T128 [US10] WEB-007 in `src/AkmlSql.Web/wwwroot/js/akml-editor.js`: add `aria-label`, `aria-multiline` and
  - **Status (implementation):** done; editorNameAttributes in a compartment, set at create and reconfigured by showTab and setTabName (rename); EditorComponent.DocumentName from the active tab.
  `role` through the existing `EditorView.contentAttributes` facet next to the writing-assistant opt-outs. Update the
  label in `switchTab` with the tab's name, passed from `src/AkmlSql.Web/Shared/EditorComponent.razor` (it already
  knows the tab). Make T125 pass. If US5's T080 is not done yet, do this task after it: same file.
- [X] T129 [P] [US10] WEB-008 in `src/AkmlSql.Web/Shared/ConnectionPickerComponent.razor` and `src/AkmlSql.Web/Pages/Styles.razor`.
  - **Status (implementation):** done; the Active badge uses --akml-badge-success; the muted style badge has no opacity, and on a selected or hovered row every badge but Active takes the row's colour. Axe also found the active Settings nav link at 4.24:1 in dark (the page-wide a:hover colour): the nav link keeps its own colour on hover.
  - **Active badge,** `.akml-connections-badge--active` in `src/AkmlSql.Web/Shared/ConnectionPickerComponent.razor`
    (line ~190): use the existing web semantic success badge pair (search `docs/theme-tokens.json` for the `Badge` /
    `StatusText` success tokens). Only if no pair reaches 4.5:1, add one to `docs/theme-tokens.json` and run
    `scripts/generate-theme-css.ps1`, never editing CSS by hand.
  - **Muted badge,** `.akml-styles-badge.muted` in `src/AkmlSql.Web/Pages/Styles.razor` (line ~267): replace
    `opacity: 0.8` with `color: var(--akml-text-secondary)`.
- [X] T130 [US10] WEB-009, tab close button, in `src/AkmlSql.Web/Shared/TabStrip.razor` (`Closable`): render the
  - **Status (implementation):** done; the close button is the tab's sibling in a presentational item that carries the active marker, and only the active tab's close is a tab stop. A tablist may own only tabs (axe aria-required-children flagged the sibling button), so a closable strip's tablist is an element of its own that owns the tabs with aria-owns; the row is a plain container. TabStripTests' closable fact restated; QueryTabsTests' close locator follows (ActiveClose).
  close control as a sibling button after the tab element, inside the same list item, with
  `aria-label="Close {title}"`. Keep the `doc-tab` and `doc-tab-close` test ids and the key handling of
  `akml-ui.js trapTabStripKeys`. Make T127 pass, and keep `QueryTabsTests` green.
- [X] T131 [P] [US10] WEB-009, labels and headings, in `src/AkmlSql.Web/Pages/` and `src/AkmlSql.Web/Shared/Settings/ConnectionsSection.razor`.
  - **Status (implementation):** done; only the editor lacked an h1 (Snippets, Styles, History and Settings have visible ones), so only the editor got a hidden one. The Schema cache table's empty header got Actions too.
  - `src/AkmlSql.Web/Pages/History.razor`: visually hidden `<label for>` for the Server and Database selects.
  - `src/AkmlSql.Web/Pages/Styles.razor`: the preview `<pre>` gets `tabindex="0"` and `aria-label="Preview"`, and
    the heading order is fixed.
  - `src/AkmlSql.Web/Shared/Settings/ConnectionsSection.razor`: the empty 5th `<th>` gets visually hidden "Actions".
  - One visually hidden `<h1>` in each routed page: `Pages/Editor.razor` "SQL editor", `Snippets.razor` "Snippets",
    `Styles.razor` "Format styles", `History.razor` "SQL history" and `Settings.razor` "Settings", using the
    existing `akml-visually-hidden` class.
- [X] T132 [US10] WEB-009, landmarks, in `src/AkmlSql.Web/Shared/MainLayout.razor`: wrap the top chrome in
  - **Status (implementation):** done; header (nav and the TLS banner) and footer (status bar) around the existing main; WorkspaceLayoutTests and ShellPolishTests green.
  `<header>`, the nav in `<nav aria-label="Main">`, the page body in `<main>` and the status bar in `<footer>`. Check
  that `WorkspaceLayoutTests` and `ShellPolishTests` stay green, because the layout grid selectors may change.
- [X] T133 [US10] Run `node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"`, `dotnet test tests/AkmlSql.Web.Tests`,
  - **Status (implementation):** done 2026-11-22: node 321/321; Web 2305/2305; theme check OK; AxeAccessibilityTests 0 violations on 18 route x theme pairs (41 before); TabStripKeyboardTests 2/2; WorkspaceAccessibilityTests 4/4; WorkspaceLayout, ShellPolish and QueryTabs green in the gate run (43 passed, 6 skipped: no PostgreSQL server or SQL login).
  `scripts/generate-theme-css.ps1 -CheckOnly`, and the E2E classes `AxeAccessibilityTests`, `TabStripKeyboardTests`,
  `WorkspaceAccessibilityTests` and `WorkspaceLayoutTests` (serial). Fix until axe reports zero violations on all 18
  route-theme pairs.

**Checkpoint**: all ten stories complete.

---

## Phase 13: Polish & Cross-Cutting Concerns

**Purpose**: documentation currency (constitution, Additional Constraints), the full gates and the acceptance run.

- [X] T134 [P] Document the IPC changes in `doc/ipc-api.md`: messages 227–230 with the capability
  - **Status (implementation):** done; doc/ipc-api.md: the 227-230 rows, the pairing.manage.v1 capability row, the PairingList/PairingRevoke section, and SchemaPhasePayload key 8 with the new TotalScanned meaning (whose XML comment in Core was updated to match).
  `pairing.manage.v1` (contracts/engine-pairing.md §5), `SchemaPhasePayload` key 8 `MetadataLimited`, and the new
  meaning of `FindInvalidObjectsResponse.TotalScanned` (references checked).
- [X] T135 [P] Add a "Spec 047 — QA audit remediation" section to `doc/progress.md`: a per-story task table (US1–US10
  - **Status (implementation):** done; doc/progress.md section with the per-story table, amendments A1-A7 and the deferred list (plus four items found during the work).
  with T-ids), the two plan-time amendments A1 and A2, and the deferred items from quickstart.md §4 (WEB-006 and the
  engine-held connection list; PostgreSQL Save-with-Remember; the CLI JSON `settings` field; the SSMS commands the
  audit could not run).
- [X] T136 [P] Update `CLAUDE.md`. Add a "Latest merged work" bullet for spec 047 (one paragraph, the decisions that
  - **Status (implementation):** done; CLAUDE.md (UTF-8 without BOM, CRLF kept): the spec 047 bullet and four Build Gotchas (analyzer smoke gate, akml-engine.json, squiggleStyle marker/underline, the axe gate and CodeMirror's interactionDelay in browser tests).
  stay). Add Build Gotchas entries:
  - the Analyzer CLI smoke gate in `build.ps1` and `Deploy-Build-Release.ps1`, and why (trimming plus reflection);
  - `akml-engine.json` written by the installer for IIS/None hosting;
  - `squiggleStyle` `marker`/`underline` and its one-time migration;
  - the axe E2E gate and its package.

  Keep the file's encoding as it is (UTF-8 without BOM, CRLF).
- [X] T137 Run the one-pass solution build of `AKML-SQL.slnx` (MSBuild Restore, then Build Release `-m`) and
  - **Status (implementation):** done 2026-11-22: MSBuild Restore + Build Release -m of AKML-SQL.slnx, no errors, every project's output written in the one pass (08:34); theme check OK for light, dark and high contrast.
  `scripts/generate-theme-css.ps1 -CheckOnly`. Both must be green (Principle II).
- [X] T138 Run every suite in quickstart.md §2, including `dotnet test tests/AkmlSql.E2E.Tests --filter "Category=PublishedCli"`,
  - **Status (implementation):** done 2026-11-22: every suite at or above its baseline, the ratchets held (FormatParityTests 312/312, CorpusGate 1311/1343, PostgreSQL corpora green); numbers in baseline.md. Two test races introduced by this feature were found and fixed: the PublishedCli publish against the engine-launch fixture's Release build (own non-parallel collection), and RefactorMenuKeyboardTests pressing Tab before the menu took focus. The browser suite's other 6 failures are the environmental ones known before.
  the Installer tests with `--filter "Category!=InstallerSmoke"`, and the whole `tests/AkmlSql.Web.E2E.Tests` suite
  serially after the Debug builds. Compare with the T006 baseline: no new failure, and the ratchets equal or higher
  (Principle III). Re-run the known load-sensitive classes alone before calling anything a regression. Record the
  results in `baseline.md`.
- [X] T139 Fill in the "Fails-before log" of `baseline.md` completely: each issue's test, with the failure it showed
  - **Status (implementation):** done; every one of the 32 in-scope issues has a fails-before row (tests that did not compile before their code are listed as such).
  before its fix (FR-038), all 32 issues present.
- [X] T140 Build the installer (`build.ps1` full, or ISCC on `src/AkmlSql.Installer/AkmlSqlSetup.iss` after the
  - **Status (implementation):** built 2026-11-22: build.ps1 -SkipTests (all 15 steps, analyzer smoke 4/4 on the new publish) wrote src/AkmlSql.Installer/Output/AKMLSQLSetup.exe, version 1.26.1122.1103. Not installed: the owner approved the install on 2026-11-22, but the session's permission settings blocked running the installer, so the owner runs it (silent: AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /SUPPRESSMSGBOXES /NORESTART /TARGETS=ssms22). The akml-engine.json check (bridge port 59417 in {app}\Web) follows the install. The build also refreshed src/AkmlSql.Site/wwwroot/docs-metadata.json (generatedAt and one updated date), as every release build does.
  - **Done 2026-11-22 (owner):** the owner built 1.26.1122.1400 from the committed branch and installed it (release commit de24958e). Setup took the install over in place in `C:\Program Files (x86)\AKML SQL\` (IIS, 59416/59417). `{app}\Web\akml-engine.json` = `{"manifestVersion":1,"bridgePort":59417,"scheme":"ws"}`; the spec 047 code is in the installed engine, SSMS extension, web bundle and analyzer (baseline.md › Installed-product run).
  publishes). Install it on this machine **only with the owner's go-ahead**, because it replaces the installed SSMS
  extension and web hosting. Then confirm that `C:\Program Files\AKML SQL\Web\akml-engine.json`, or the install's
  `{app}\Web`, holds the bridge port 59417.
- [ ] T141 Run the acceptance scenarios V1–V22 of quickstart.md §3 and record each in `baseline.md`, with evidence
  - **Status (implementation):** recorded 2026-11-22 in baseline.md, V1-V22: each row names its automated evidence (all green). Not Run, with the reason in each row: the installed-product runs (need the T140 install), the SSMS desktop runs V9-V12 and V19, V5 (needs a SQL login) and the V15 GRANT (owner go-ahead), V4 and the Paired browsers shot (a LAN-mode install), V21's site search (an isolated local site preview), and a screen reader for V22.
  - **Installed run 2026-11-22 (1.26.1122.1400):** baseline.md › Installed-product run. Pass: V1, V2, V6, V7, V8 (web, 72/72), V13, V14, V16, V17, V18, V22, and the web cycle. Fail: V10 web (F1: advisory findings still underlined) and V20's Fix half (F2: Fix never enabled on SQL Server). Partial: V15 (F3: the note overlaps the tree; GRANT not run), V21 (one query puts Connecting before Troubleshooting). Differs: V3 (F4: the IIS install never shows its notice). Still open: the SSMS desktop runs (the RDP session was locked; `tests/AkmlSql.UiTests/Check047.cs` runs them), V4 (LAN mode), V5 and the V15 GRANT (owner go-ahead), a screen reader. The open findings F1–F3 need code fixes; this task stays open until they are fixed or accepted.
  under `specs/047-qa-audit-remediation/baseline/` (git-ignored screenshots are not evidence that survives; store
  small text outputs instead).
  - **Owner go-ahead needed:** V5 (create a SQL login) and V15 (`GRANT VIEW DEFINITION … TO [NT AUTHORITY\SYSTEM]`).
  - **SSMS scenarios** (V9–V12, V19) need a live desktop session; mark them Not Run with the reason when the RDP
    session is not connected, never Pass.
- [ ] T142 [P] Optionally update the Status column of the audit index in `reports/AKML-QA-UX-Documentation-Audit.md`
  - **Status (implementation):** not done (optional): the audit index's Status cells carry the audit's own confirmation notes ("Confirmed (axe)", "Observed once"), which replacing them would lose; the per-issue fixes and evidence are in baseline.md.
  (an untracked file) to "Fixed in spec 047 (T…)" per issue, and WEB-006 to "Deferred (own spec)".
- [X] T143 Report to the owner, without committing (constitution IV), using `specs/047-qa-audit-remediation/baseline.md`: the files changed per story, the test results
  - **Status (implementation):** done 2026-11-22: reported to the owner in the session, nothing committed; the owner decides on commit, push or PR, the install (T140), V5 and V15.
  against the baseline, the V1–V22 outcomes, the open items and the deferred list. Ask whether to commit, push or
  open a PR.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)** has no dependencies.
- **Foundational (Phase 2)** depends on Setup, and blocks all stories, because it holds the baseline every
  "fails before" check is judged against.
- **US1–US8 and US10** depend only on Phase 2, and are independent of each other in behaviour.
- **US9** depends on US2 (failure notice and paired-browser wording, screenshots), US3 (dialog wording, Diagnostics
  row, screenshots) and US7 (the WEB-013 note text).
- **Polish** depends on every story chosen for this delivery.

### Shared files: sequence these tasks across stories (same file, no [P] across them)

| File | Tasks, in order |
|---|---|
| `src/AkmlSql.Web/Pages/Editor.razor` | T053 (US3 banner) → T091 (US6 Parse) → T108 (US7 refactor menu) → T131 (US10 h1) |
| `src/AkmlSql.Web/wwwroot/js/akml-editor.js` | T080 (US5 marker class) → T128 (US10 aria) |
| `src/AkmlSql.Web/Shared/EditorComponent.razor` | T080 → T128 |
| `src/AkmlSql.Web/Services/Settings/SettingsCatalog.cs` | T052 (US3) → T079 (US5) |
| `src/AkmlSql.Web/Pages/Styles.razor` | T110 (US7) → T129 → T131 (US10) |
| `src/AkmlSql.Web/Shared/Settings/ConnectionsSection.razor` | T036 (US2) → T131 (US10) |
| `src/AkmlSql.Web/Program.cs` | T035 (US2) → T050 (US3) → T103 (US7) |
| `doc/configuration.md` | T081 (US5) → T121 (US9) |
| `src/AkmlSql.Web/Services/IEngineAutoConnect.cs` | T013 → T029 (US2 only) |

### Within each story

- Contracts and stub types first, then tests (they must fail for the stated reason), then implementation, then the
  story's test run.
- Engine and Core changes come before the web consumers that call them (for example T032–T034 before T035–T036).
- The goldens (T064) are captured only after all of T058–T063.

### Parallel opportunities

- **Setup:** T002, T003 and T004 are parallel.
- **US2:** after T012 and T013, the tests T014–T022 are all parallel (eight different files), and of the
  implementation, T023, T027, T031 and T035 are parallel (installer script, JS, label, web service).
- **US3:** T039–T045 are parallel, and so are T049 and T050.
- **US4:** T056 and T057 are parallel with T055. The fixes T058–T062 touch different files but share the T055
  oracle, so run them one after another for clean failure attribution.
- **US5:** T067–T073 are parallel (Core, Shell, node and Web test files).
- **US6:** T083–T086 are parallel.
- **US7:** T094–T102 are parallel.
- **US8:** T112 and T113 are parallel.
- **US9:** T118–T121 are parallel (four doc files).
- **US10:** T125–T127 are parallel, and so are T129 and T131.
- **Across stories:** US1, US4, US5's SSMS half, US6's shared-library half and US8 touch disjoint projects, and can
  run side by side from the start.

## Parallel Example: User Story 2

```text
# After T012 (IPC DTOs) and T013 (web stub types):
Task: "T014 EngineManifestScriptTests in tests/AkmlSql.Installer.Tests/EngineManifestScriptTests.cs"
Task: "T015 readManifest node tests in tests/AkmlSql.Web.Tests/js/akml-page-engine.test.mjs"
Task: "T016 PageEngineManifestTests in tests/AkmlSql.Web.Tests/Services/PageEngineManifestTests.cs"
Task: "T019 browserLabel node tests in tests/AkmlSql.Web.Tests/js/akml-bridge.label.test.mjs"
Task: "T020 PairingManageHandlerTests in tests/AkmlSql.Engine.Tests/Pairing/PairingManageHandlerTests.cs"
# Then, in parallel:
Task: "T023 src/AkmlSql.Installer/web-engine-manifest.ps1"
Task: "T027 readManifest in src/AkmlSql.Web/wwwroot/js/akml-page-engine.js"
Task: "T031 browserLabel in src/AkmlSql.Web/wwwroot/js/akml-bridge.js"
```

## Parallel Example: User Story 7

```text
Task: "T094 CompletionUserSnippetTests"     Task: "T096 PhaseAMetadataLimitedTests"
Task: "T097 SchemaTreeMetadataLimitedTests" Task: "T099 SchemaSnapshotCache + RefactorExpandWithSnapshot tests"
Task: "T101 Style/Snippet import message tests"  Task: "T102 FindInvalidObjects count + dialog text tests"
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 Setup, then Phase 2 Foundational (baseline).
2. Phase 3, US1: the CLI fix and the release gate, which is small, independent and stops a silent false "clean" in
   CI.
3. **Stop and validate**: T007 green; the gate fails on the old exe and passes on the new one (T011).

### Incremental delivery (spec priority order)

1. US1 → US2 → US3: first-run pairing and the user's credential complaint, the highest daily impact.
2. US4: formatter correctness, the headline feature (watch the golden review in T064).
3. US5 → US6: quiet advisory markers and honest syntax feedback.
4. US7 → US8: the workspace gaps and AI prompts.
5. US9: documentation, after US2, US3 and US7 settle the wording.
6. US10: accessibility and the axe gate.
7. Polish: docs currency, the full gates and acceptance V1–V22.

Each step is reviewable on its own. Stop after any checkpoint, show the owner, and ask before any commit.

---

## Notes

### Execution notes (added at implementation start, 2026-11-21)

1. **T004 runs at the start of Phase 6 (US4), not in Setup.** Every `tests/format-parity/corpus/*.sql` is picked up
   by two tests:
   - `FormatParityTests`, which **captures a missing golden** from whatever the formatter does now;
   - `AkmlSql.Web.Tests` parity tests, which **fail on a missing baseline**.

   Adding the `sp047-*` inputs early would either freeze broken output or break the web suite for US1–US3.
2. **During US4**, run only `--filter FullyQualifiedName~Sp047FormatterRulesTests` (plus `LineEndingTests`) until
   T063 is done. Before T064, delete any `tests/format-parity/golden/sp047-*` file a full run may have captured.
3. **T006 covers the unit suites.** The Playwright suite (`tests/AkmlSql.Web.E2E.Tests`) takes very long, so its
   baseline is taken per story, for the classes that story's last task lists, before the story's first change.
4. **US6 (T083/T088): the parse-error finding must ignore errors on batch-separator lines.** ScriptDom does not parse
   `GO n`, and SSMS scripts use it. T083 gets a case: `select 1` / `GO 3` / `select 2` gives no `SYNTAX`.

### Totals

- 143 tasks. Every one of the 32 in-scope issues has one or more test tasks that must fail first (FR-038); WEB-006 has
  none (out of scope).
- Live SQL, SSMS desktop and installer steps either use the existing opt-in mechanisms (`[SkippableFact]`,
  `TryReachLocalSql`, `InstallerSmoke`, FlaUI UiTests) or are manual rows in `baseline.md`.
- Changes that need the owner's go-ahead before they run: creating a SQL login (V5), `GRANT VIEW DEFINITION` (V15),
  installing the build on this machine (T140), and any git operation (T143).
