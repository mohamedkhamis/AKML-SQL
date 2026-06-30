# Design — SQL-auth credential support for IntelliSense (spec 029)

| | |
|---|---|
| **Author** | Mohamed Khamis (with Claude Code) |
| **Date** | 2026-06-04 |
| **Status** | Approved design — awaiting implementation plan |
| **Scope** | Targets **SSMS 22**; VS 2026 shares the code (`.projitems`) but is **not live-verified** in this spec (§10). |
| **Origin** | Bug "any remote server cannot load schema, just local only" → root cause: SQL-auth windows are not engine-usable |

> **Note on the appendix and "planned vs present" wording.** This is a design for *planned* work. Every "add X" / "change Y" below is a future edit, not current state. Appendix A maps each change to its **current** code anchor and the **planned** edit; it does not assert the edit already exists.

## 1. Problem

When an SSMS/VS query window is connected with **SQL Server authentication** (a login such as `sa`), the out-of-process .NET 10 engine cannot reuse the password — SSMS does not expose it across the process boundary (R-017, spec 014). Today `SsmsConnectionDetector.ClassifyAuth` collapses SQL auth into `AuthMode.Unsupported`, `ConnectionWiringHelper` skips the `ConnectionChanged` notification, the engine never registers the session, and the schema cache stays empty. Observed symptom: **local Windows-auth connections get IntelliSense; every remote SQL-auth connection silently gets none.**

Evidence (user's `akmlsql-20260604.log`):
```
ParseCaption: caption-user='sa' has no domain/UPN markers → inferring SQL auth (Unsupported)
  for 'SQLQuery1.sql - 192.168.5.123.NatGas_G2_Testing (sa (53))'
[WRN] AKML SQL IntelliSense disabled for 192.168.5.123.NatGas_G2_Testing: … "Unsupported" …
[WRN] SchemaRefreshRequest: session='…' not found — nothing to refresh
```

## 2. Goal & non-goals

**Goal:** Let the user supply the SQL password **once** per `(server, login)`. AKML validates it against the server, stores it DPAPI-encrypted, and the engine then connects with a SQL-auth connection string so schema + IntelliSense load exactly as they do for Windows auth.

**Non-goals (out of scope):**
- AAD-interactive auth modes (Password, Interactive, ServicePrincipal, DeviceCode) — these stay `Unsupported`; the engine genuinely cannot reuse them without a second prompt.
- A full Options "credential manager" page (list/edit/clear-all). Deferred — §10.
- Storing Windows/AAD credentials (those already work via the inherited user token).
- VS 2026 live verification (§10). If VS 2026's DTE `AuthenticationType` or caption format differs, `SsmsConnectionDetector` may need VS-specific branching — a follow-up, not this spec.

## 3. Approved decisions

| Decision | Choice |
|---|---|
| **Prompt UX** | **Click-to-enter, non-intrusive.** No surprise modal. A clickable affordance in the editor's schema-progress margin; the user clicks to open the password dialog. |
| **Validation** | **Validate before store.** The dialog runs a `TestSqlConnection` IPC round-trip; it persists the password **only on success** and shows the exact SQL error inline on failure. Nothing bad is ever stored. |
| **Re-use & staleness** | A stored credential is trusted on re-use (no re-validation per open). If the server later rejects it (password changed), the engine reports `AuthError` and the margin shows "credentials rejected — click to re-enter" (§5.7). Never a silent failure. |
| **Storage** | DPAPI (`CurrentUser` + app entropy), single JSON file `%AppData%\AKML SQL\sql-credentials.json`, keyed per `(server, login)`. |
| **Opt-out** | `intelliSense.enableSqlAuthCredentials`, default **true**. When false → today's behavior (skip + log, no prompt, no storage). |
| **Management** | A "Clear saved password" button inside the dialog (when a credential exists). Full Options page deferred. |

## 4. Architecture overview

**Shell-owned credential store.** The shell already builds the engine connection string and sends it via `ConnectionChanged`; the SQL password is resolved and injected **shell-side**, so the engine's contract is unchanged for the actual connection. The engine gains one new, stateless capability — a **validate** round-trip — and one new optional status field (`AuthError`).

**Why the "needs credentials" state is shell-local.** A SQL-auth window has **no registered engine session** until we send `ConnectionChanged`. So the engine cannot originate the "needs credentials" signal. It lives in a per-buffer `SqlAuthState` written by the wiring into `TextBuffer.Properties` and read directly by the margin.

**The two non-silent failure paths.**
- *No credential stored yet* → shell-driven: `SqlAuthState.NeedsCredentials = true`; margin shows the click-to-enter affordance.
- *Stored credential rejected by the server* → engine-driven: we sent `ConnectionChanged`, the engine has a session, its schema load hits login-failure (18456/4060/18452/916), it sets `SchemaStatusResponse.AuthError`; the margin (which already polls status) shows "credentials rejected — click to re-enter."

```
Open SQL-auth window
  → SsmsConnectionDetector.ParseCaption → AuthMode.SqlPassword, Login="sa", ConnectionString=null, IsEngineUsable=false
  → ConnectionWiringHelper: enableSqlAuthCredentials? → SqlCredentialStore.TryGet(server, login)
       ├─ found   → BuildSqlAuthConnectionString → SendConnectionChangedAsync → engine loads schema
       │             (engine rejects stale pwd → SchemaStatusResponse.AuthError → margin "click to re-enter")
       └─ missing → SqlAuthState{Server,Login,NeedsCredentials=true} in TextBuffer.Properties; skip send
  → SchemaProgressMargin reads SqlAuthState → renders "SQL auth — click to enable IntelliSense"
       (while NeedsCredentials, each 1s poll does a store-only re-check → window B auto-resolves once window A stores the pwd)
  → user clicks → SqlCredentialDialog (server+login read-only, masked password)
       → Save → "Testing connection…" (buttons disabled) → TestSqlConnection IPC → engine opens short-timeout SqlConnection
            ├─ ok   → SqlCredentialStore.Save (DPAPI) → DialogResult=true → margin re-detect → ConnectionChanged → schema loads
            └─ fail → inline error ("Login failed for user 'sa'"); NOTHING stored; dialog stays open for retry
```

## 5. Components

### 5.1 `SqlCredentialStore` (new) — `src/AkmlSql.Core/Config/SqlCredentialStore.cs`
Lives in `AkmlSql.Core` (namespace `AkmlSql.Core.Config`), reusing the DPAPI dependency already referenced there. The net472 shell loads it transitively (see §6 for the runtime-binding note + the explicit-`PackageReference` recommendation).

- **Encryption** mirrors `AkmlSql.Engine/Ai/Security/CredentialManager.cs`: `dpapi:` prefix + Base64, `ProtectedData.Protect/Unprotect` with `DataProtectionScope.CurrentUser` and SHA-256 app entropy `"AkmlSql-SqlCred-v1"` (distinct from the API-key entropy). Zero plaintext byte arrays after use (`CryptographicOperations.ZeroMemory`) where the API permits.
- **Format:** `List<SqlCredentialEntry { string Server; string Login; string EncryptedPassword; }>` → JSON (System.Text.Json, camelCase) at `%AppData%\AKML SQL\sql-credentials.json` (`Constants.AppDataPath`). **First run / missing file:** treated as empty; every `TryGet` returns false. The directory + file are created on the first `Save` (`Directory.CreateDirectory` then the atomic write, exactly like `ConfigManager.cs:92-115`). An empty store is never written.
- **Atomic + race-safe:** copy `ConfigManager`'s write idiom (temp `path + ".tmp"`, then `File.Replace` on netstandard2.0 / `File.Move(overwrite:true)` on net10). A `static readonly object _gate` guards every read-modify-write to close the TOCTOU window (single interactive user → negligible contention).
- **API:** `bool TryGet(server, login, out password)`, `Save(server, login, password)`, `Remove(server, login)`. `(server, login)` matching is `StringComparer.OrdinalIgnoreCase`.
- **Corrupt-entry self-heal:** `TryGet` wraps `ProtectedData.Unprotect` in `try/catch (CryptographicException)`. On failure it logs a Warning, **removes that one entry** (atomic rewrite), and returns false — one bad entry (e.g. a roamed profile) never blocks the others.
- The **shell** is the only caller. The engine never reads this store.

### 5.2 `SsmsConnectionDetector` — `src/AkmlSql.Shell.Shared/Editor/SsmsConnectionDetector.cs`
- **Add `AuthMode.SqlPassword`** (enum at lines 31-41; insert after `AzureAdIntegrated`, before `Unsupported`).
- **`ClassifyAuth`:** numeric case `1` (SSMS `SqlPassword`, currently → `Unsupported` at line 510) → `AuthMode.SqlPassword`. AAD cases `2`/`4`/`5`/`6` stay `Unsupported`.
- **Bare-username heuristic** (lines 304-318): the infer line (314, currently `AuthMode.Unsupported`) → `AuthMode.SqlPassword`.
- **Add `ConnectionResult.Login`** (`string`) — the class (lines 564-583) currently has no login field. Populate it from `captionUserName` at the instantiation site (lines 360-367).
- **No-password-yet path:** `SqlPassword` is intentionally **not** added to `BuildEngineConnectionString`'s switch — it falls through `default → return null`, so at parse time `ConnectionString=null, IsEngineUsable=false` while `AuthMode=SqlPassword` is preserved.
- **New `internal static string BuildSqlAuthConnectionString(string server, string database, string login, string password)`** (above `BuildEngineConnectionString`, line 376). **Build it with `System.Data.SqlClient.SqlConnectionStringBuilder`** — the shell already references `System.Data.SqlClient` (`Execution/MultiDatabaseExecutor.cs:6,85`), and the builder escapes `;`, `'`, `"`, `=` in the password automatically (no manual quoting, no injection risk):
  ```csharp
  var b = new System.Data.SqlClient.SqlConnectionStringBuilder {
      DataSource = server, InitialCatalog = database, UserID = login, Password = password,
      IntegratedSecurity = false, // + TrustServerCertificate=true, Encrypt=true, ConnectTimeout=5, ApplicationName
  };
  return b.ConnectionString;
  ```
  The keyword set is parse-compatible with the engine's `Microsoft.Data.SqlClient`. **`Encrypt=true`** (security review): a SQL-auth connection carries a reusable password, so the wire (login + data) is encrypted to defeat passive interception; `TrustServerCertificate=true` is kept because internal SQL Servers present self-signed certs that wouldn't chain-validate (this mirrors SSMS 22's own default). Called by the wiring once the password is known.
- **Warning logic** (lines 344-350): the "IntelliSense disabled … reconnect with Windows auth" warning now fires only for `Unsupported`. `SqlPassword` routes to the affordance instead.

### 5.3 `ConnectionWiringHelper` — `src/AkmlSql.Shell.Shared/Editor/ConnectionWiringHelper.cs`
At both skip-send branches (lines 44-51 and 74-82), before the existing `!IsEngineUsable` skip, handle `conn.AuthMode == SqlPassword` when `settings.IntelliSense.EnableSqlAuthCredentials`:
- `SqlCredentialStore.TryGet(conn.Server, conn.Login)` →
  - **found:** fill `conn.ConnectionString = SsmsConnectionDetector.BuildSqlAuthConnectionString(server, database, login, pwd)` and `conn.IsEngineUsable = true`, then let the connection flow through the **existing** `SendConnectionChangedAsync` path unchanged. Write `SqlAuthState { Server, Database, Login, NeedsCredentials = false }`. (Trusts the validation done when the password was first stored; a since-changed password is caught by the engine `AuthError` path, §5.7.)
  - **missing:** write `SqlAuthState { Server, Database, Login, NeedsCredentials = true }`; leave `IsEngineUsable = false` so the existing skip-send path runs (no warning); the margin shows the affordance.
- `EnableSqlAuthCredentials == false` → today's skip + log.

**Credential resolution order (follow-up — inherit from SSMS, zero prompt).** Both the wiring (`MaybeApplyStoredSqlCredential`) and the margin re-check (`TryResolveStoredSqlCredential`) resolve the password via `ResolveSqlAuthPassword(server, login)`, in order: **(1) inherit** it from SSMS's active query-window connection — `ScriptFactory.Instance.CurrentlyActiveWndConnectionInfo.UIConnectionInfo.Password`, read entirely by **reflection** (`SsmsConnectionDetector.TryGetActiveSqlAuthPassword`; no compile-time SSMS-assembly dependency, so a silent no-op on VS 2026 / older SSMS), used **only** when the active connection's server + login match the window being wired (so window A's password never reaches window B), and persisted to the store for resilience; then **(2)** the DPAPI store; then **(3)** the prompt. Tier 1 means the **common case never prompts** — SSMS already holds the SQL password (the same way SQL Prompt inherits the connection), so AKML reuses it. The prompt remains only for cases SSMS doesn't expose (a window connected by another means, older SSMS, or `EnableSqlAuthCredentials=false`).

**`SqlAuthState` is written for *every* SQL-auth window** (resolved or not) — its presence is the signal that lets the margin treat an engine `AuthError` (§5.7) as "SQL credentials rejected" rather than a Windows-auth permission denial (which keeps today's behavior).

**`SqlAuthState`** (small new per-buffer class, shell `Editor/`): `{ string Server; string Database; string Login; bool NeedsCredentials; }`. It **never holds the plaintext password** — the password lives only in the store (encrypted) and transiently inside the connection string during the send. `Database` is captured at detect time so the cheap re-check (below) can build the connection string without re-parsing the caption.

**New `public static bool TryResolveStoredSqlCredential(string sessionId, IWpfTextView textView)`** — the lightweight path the margin calls while waiting (§5.4). It reads `SqlAuthState` from the buffer (no caption parse, no DTE/COM walk), does `SqlCredentialStore.TryGet(state.Server, state.Login)`; on a hit it builds the SQL string (`BuildSqlAuthConnectionString`), sends `ConnectionChanged`, clears `NeedsCredentials`, and returns true; on a miss returns false. This is a **file read only** — safe to call once per poll.

**NeedsCredentials lifecycle (single source of truth):**
- **Set** → `true` only here, when `AuthMode==SqlPassword` and `TryGet` finds nothing.
- **Cleared** → in exactly two places: (a) here, when a re-detect's `TryGet` succeeds and we send `ConnectionChanged`; (b) the margin's dialog callback, after a successful `Save` (which then re-detects, hitting (a)). No other code touches it.

**Connection change / re-detect:** when the SSMS caption changes (user switches server), the margin's next poll calls `DetectAndSendConnection`, which re-parses and **overwrites** `SqlAuthState` for the new `(server, login)` (sets or clears `NeedsCredentials` accordingly). Re-detect always overwrites — no stale state survives a connection change.

### 5.4 `SchemaProgressMargin` — `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs`
- **Service provider:** resolve via `Microsoft.VisualStudio.Shell.ServiceProvider.GlobalProvider` — the established pattern (`TextViewCreationListener.cs:54`). Safe: the shell runs single-threaded on the UI thread.
- **State:** add `MarginState.NeedsCredentials` (the enum currently has `Hidden/Loading/Ready`). On each poll tick (`OnPollTick`, line 268) **and** on creation, read `TextBuffer.Properties["AkmlSqlAuthState"]`:
  - If `NeedsCredentials == true` → render the affordance (accent-colored, clickable: "SQL auth — click to enable IntelliSense"), **with priority over** the engine `SchemaStatusResponse` (which reports session-not-found anyway). **While in this state, call `ConnectionWiringHelper.TryResolveStoredSqlCredential(sessionId, _textView)` each tick** — a **store file read only** (no caption parse, no DTE/COM walk; §5.3) — so a credential entered in *any* window propagates to all windows on the same `(server, login)` within ~1s without a second click. (Do **not** re-run the full `DetectAndSendConnection` per tick — that walks DTE `ProjectItem.Properties` over COM on the UI thread and must stay event-driven, not a 1 s loop.)
  - Else, apply the engine status as today; if `SchemaStatusResponse.AuthError == true` (§5.7) → render "credentials rejected — click to re-enter."
  - Rendering is idempotent; the click-to-enter UX never auto-opens anything, so the 1s poll causes no prompt spam.
- **Click → `public void BeginEnterCredentials()`** (parallel to `BeginRefresh`, lines 471-487): query `SqlCredentialStore.TryGet(server, login, out _)` for `hasExistingCredential`, show `SqlCredentialDialog(server, login, hasExistingCredential)`; on a successful save, clear `NeedsCredentials` and call `DetectAndSendConnection(sp, sessionId, _textView)`.
- **Lifetime:** `SqlAuthState` lives in `TextBuffer.Properties` and is collected with the buffer (standard lifecycle) — no explicit cleanup needed in `Dispose()`.

### 5.5 `SqlCredentialDialog` (new WPF modal) — `src/AkmlSql.Shell.Shared/Editor/SqlCredentialDialog.cs`
Programmatic WPF (no XAML), matching house style (`SafetyWarningDialog.cs`, `ThemeAwareWindow.cs`). Constructor: `SqlCredentialDialog(string server, string login, bool hasExistingCredential)`.
- DTE-HWND owner via `TryAttachOwnerToHost` (`SafetyWarningDialog.cs:71-85`) or the `ThemeAwareWindow` base (`Ui/Theme/ThemeAwareWindow.cs:30-49`) — implementer's choice (§9); `WindowStartupLocation = CenterOwner`.
- Theme brushes from `ThemeRegistry.Instance` (`AttachTo(this)`); `Freeze()` for new brushes; `Typography.UiFont`/`MonoFont`; `Spacing.*` constants.
- Read-only server + login labels; a **`System.Windows.Controls.PasswordBox`** (the first in the codebase; instantiated programmatically like any control; `Loaded`-focused so the user can type immediately); an inline error/status `TextBlock`; **[Save] / [Cancel]**. Cancel is `IsCancel=true` (Esc). **Save is the default button (Enter submits)** — safe here, unlike the destructive `SafetyWarningDialog`, because validation always precedes any storage (nothing is persisted on a bad password).
- **"Clear saved password"** button shown only when `hasExistingCredential == true` → `SqlCredentialStore.Remove` + close.
- **Save flow with feedback:** on [Save], **disable [Save] + [Clear], set the PasswordBox read-only, show "Testing connection…"**; build the candidate string (`BuildSqlAuthConnectionString`), send `TestSqlConnection`, await. On `ok` → `SqlCredentialStore.Save` + `DialogResult=true`. On failure → show `errorMessage` inline, re-enable controls, keep the dialog open. Nothing is persisted on failure (honors "validate before store").
- **Plaintext lifetime / honesty:** `PasswordBox.Password` is an immutable CLR `string` and cannot be reliably zeroed; the plaintext also exists inside the `TestSqlConnectionRequest`/connection string during the round-trip. See §6 mitigations. The dialog never hands plaintext to the margin — it persists encrypted via the store; the margin's re-detect later decrypts transiently to build the connection string (the same path every auth mode uses for the send).

### 5.6 `TestSqlConnection` IPC (new) — engine validation round-trip
- **Message types** in `src/AkmlSql.Core/Ipc/RpcMessage.cs` (`MessageTypes`, line 43): allocate a request/response pair in the reserved Shell→Engine `90–99` / Engine→Shell `190–199` ranges. **Exact free integers chosen at implementation by reading the enum** (§9).
- **Message classes** in `src/AkmlSql.Core/Ipc/Messages/`, `[MessagePackObject]` + `[Key(n)]` (match the existing convention — verify nullable-`string?` usage against a sibling message at implementation):
  - `TestSqlConnectionRequest { string ConnectionString; }` — the **exact** string the engine will later receive (carries the password; travels only over the ACL'd pipe, §6).
  - `TestSqlConnectionResponse { bool Ok; string? ErrorMessage; }`.
- **Handler** `TestSqlConnectionHandler : IRpcRequestHandler<TestSqlConnectionRequest, TestSqlConnectionResponse>` in `src/AkmlSql.Engine/Handlers/Control/`, modeled on `PingHandler` (`ControlHandlers.cs:37-55`). Opens `await using var conn = new SqlConnection(req.ConnectionString); await conn.OpenAsync(ct);` (`SchemaMetadataService.cs:168-169`). Catch `SqlException sqlEx when (sqlEx.Number is 18456 or 4060 or 18452 or 916)` → `Ok=false, ErrorMessage = sqlEx.Message` (the server's text, e.g. "Login failed for user 'sa'."). Broader `SqlException`/timeout → `Ok=false` with a concise message. Success → `Ok=true`. `ErrorMessage` travels only over the ACL'd pipe and is shown inline (never persisted), so echoing the login name is acceptable. **All logging uses `ConnectionDiagnostics.Describe(req.ConnectionString)`** — never the raw string.
- **Register** in `EngineHandlerRegistry.RegisterAllHandlers()` (~line 250) via `router.Register(new TestSqlConnectionHandler())`.

### 5.7 `SchemaStatusResponse.AuthError` (new field) — stale-credential signal
- Add `bool AuthError` (default false) to `SchemaStatusResponse` (`src/AkmlSql.Core/Ipc/Messages/SchemaStatusRequest.cs:10-45`).
- The engine sets it `true` for a session whose schema load hit a login-failure (the existing catch at `SchemaMetadataService.cs:231` already detects 18456/4060/18452/916 and sets `cache.PermissionDenied`); `SchemaStatusHandler` surfaces `AuthError = cache.PermissionDenied` in the status response the margin already polls.
- **Terminal-cache recovery:** a `PermissionDenied` cache is terminal — `ConnectionChangedHandler` will not re-run Phase A for the same session+db even with a corrected password. So `ConnectionChangedHandler` is enhanced to **reset** the cache (`Schemas.Clear()`, `ForeignKeys.Clear()`, `Phase = NotLoaded`, `PermissionDenied = false`) when the **incoming connection string differs** from the session's previous one (captured before `UpdateSession`). A corrected credential → a different string → a fresh Phase A attempt. (This also benefits Windows-auth reconnects after a permission change.)
- The margin (§5.4) renders it as "credentials rejected — click to re-enter," and `BeginEnterCredentials()` opens the dialog so the user supplies the new password. This converts the only remaining silent-failure (server password changed after storing) into the same click-to-enter flow.

> **Do not collapse `TestSqlConnection` and `AuthError` — they are not redundant.** The dialog-time `TestSqlConnection` (§5.6) is what honors the user's explicit decision *"validate before store → nothing bad gets persisted."* The engine `AuthError` only catches the *after-the-fact* case where a previously-good stored password is later rejected by the server. Removing either one silently drops a requirement: dropping `TestSqlConnection` persists unvalidated passwords; dropping `AuthError` reintroduces a silent no-schema failure for stale credentials.

### 5.8 Config — `src/AkmlSql.Core/Config/AppSettings.cs`
Add `public bool EnableSqlAuthCredentials { get; set; } = true;` to `IntelliSenseSettings` (after line 320). Serializes as `enableSqlAuthCredentials` (camelCase, `ConfigManager.cs:15-22`). Options-dialog toggle (`IntelliSensePage.cs:136-171`) deferred (§10).

## 6. Security

- **At rest:** DPAPI-encrypted (`CurrentUser` + app entropy); the JSON never contains plaintext; atomic, lock-guarded writes.
- **In transit (shell ↔ engine):** the plaintext password crosses the named pipe inside the connection string — the **existing** model (`ConnectionChanged` already carries the full connection string; pipe ACL'd to owner SID, Network SID denied: `akmlsql-engine-{SID}-{PID}`). The `TestSqlConnection` request rides the same trust boundary.
- **In transit (engine ↔ SQL Server):** `BuildSqlAuthConnectionString` sets **`Encrypt=true`** so the password-bearing connection (login + data) is TLS-encrypted on the network — defeating passive interception of the `sa` password / schema. `TrustServerCertificate=true` accepts the server's (typically self-signed) certificate, mirroring SSMS 22's default; the residual active-MITM exposure is a documented trade-off, and full cert validation is a future opt-in (not a safe default for internal self-signed-cert servers).
- **No log leaks — ACCEPTANCE CRITERION (plan gate), not a checklist line.** No path, **at any log level (the running config defaults to `Debug`)**, may emit a connection string or password. The IPC layer must log message **type/size, never payload contents**. Current state verified across the password-bearing paths: `RpcRouter.RouteAsync` deserializes the payload but has **no log statements**; `NamedPipeTransport` logs only `message.MessageType` + pipe lifecycle; `SessionManager.UpdateSession` logs `session/server/db`; `ConnectionChangedHandler` logs `{Db}` only; `SchemaMetadataService` catch logs `sqlEx.Number/State/Class` + db. The new `TestSqlConnectionHandler` must log exclusively via `ConnectionDiagnostics.Describe(req.ConnectionString)`. The plan adds a grep-based gate: *"no `Log.*` call in the engine or shell takes a connection-string argument; the router/transport log sizes/types, not contents."* (Optional hardening if the gate ever fails: send discrete `server/login/password/database` fields in `TestSqlConnectionRequest` so a payload dump can never contain a full string — noted, not adopted, since the audit is currently clean.)
- **Memory honesty:** `PasswordBox.Password` is an immutable CLR string and cannot be reliably zeroed; plaintext also exists transiently in the `TestSqlConnectionRequest`/connection string. Mitigations: short-lived (5 s connect timeout), DPAPI-only at rest, ACL'd pipe, and `enableSqlAuthCredentials=false` for orgs with strict memory-isolation rules. We zero raw byte buffers where the API allows.
- **DPAPI runtime binding:** `System.Security.Cryptography.ProtectedData` v10 is referenced under `AkmlSql.Core`'s netstandard2.0 condition (`AkmlSql.Core.csproj:18-22`); the net472 shell loads it transitively (netstandard2.0 is binding-compatible with net472). **Recommendation:** add an explicit `<PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.*" />` to `AkmlSql.Ssms22.csproj` (and `AkmlSql.VS2026.csproj`) to make the dependency explicit and avoid an assembly-not-found surprise in the VSIX output. Verify the asset is deployed into the extension folder at build time.

## 7. Error handling

| Situation | Behavior |
|---|---|
| Wrong password / login failed (18456) at dialog Save | Inline error in dialog; **nothing stored**; dialog stays open for retry. |
| Cannot open database (4060) / no permission (916) at Save | Inline error with the database name; nothing stored. |
| Server unreachable / timeout at Save | Inline error (5 s connect timeout); nothing stored. |
| **Stored credential rejected on re-use** (server pwd changed) | Engine sets `SchemaStatusResponse.AuthError`; margin shows "credentials rejected — click to re-enter"; the dialog re-validates the new password (no silent failure). |
| DPAPI decrypt failure on read | `TryGet` catches `CryptographicException`, logs Warning, removes that entry, returns false → re-prompt. One bad entry never blocks others. |
| Dialog cancelled | No send; margin keeps the affordance. |
| `enableSqlAuthCredentials = false` | No affordance; the `Unsupported`-style skip+log path. **Existing stored credentials are not auto-deleted** (remain encrypted); re-enabling the flag makes them available again. To delete: the dialog's "Clear saved password" or remove `sql-credentials.json`. |

## 8. Testing

**Automated (xunit — `tests/AkmlSql.Shell.Shared.Tests` + Core tests):**
- `SqlCredentialStore`: encrypt → persist → read round-trip; stored value carries `dpapi:` and is not plaintext; missing/corrupt file → empty/no-throw; one corrupt entry is dropped while others still read; case-insensitive `(server, login)` match; `Remove` deletes; concurrent save/read under the lock does not corrupt the file.
- `ClassifyAuth` / `ParseCaption` (extend `SsmsConnectionDetectorTests.cs`): SSMS numeric `1` and the bare-username heuristic → `AuthMode.SqlPassword`; AAD modes (`2`/`4`/`5`/`6`) stay `Unsupported`; `ConnectionResult.Login` populated from the caption.
- `BuildSqlAuthConnectionString`: correct `Data Source`/`Initial Catalog`/`User ID`/`Password` + suffix; a password containing `;`, `'`, `"`, `=` round-trips intact through `SqlConnectionStringBuilder` (no injection, no truncation).

**IPC integration** (serialization, handler registration, dispatch) of `TestSqlConnection` + the `AuthError` field is exercised by the manual live test below; a thin engine-handler unit test asserts the login-failure → `Ok=false` mapping.

**Manual live verification:**
1. *Originating bug:* on the running SSMS 22, open a window on `192.168.5.123` / `NatGas_G2_Testing` as `sa` → margin shows the affordance → click → enter the password → validation succeeds → schema/IntelliSense load. Log shows `Sent ConnectionChanged … auth=SqlPassword`, no "session not found".
2. *Wrong password:* inline error, nothing stored.
3. *Persistence:* restart SSMS → reconnect → no re-prompt (stored credential reused).
4. *Multi-window:* open two windows on the same `(server, login)`; enter the password in one → the other resolves within ~1s without a click.
5. *Stale credential:* change the server password after storing → reconnect → margin shows "credentials rejected — click to re-enter."

## 9. Open implementation details (resolve in the plan)

1. **Exact `MessageTypes` integers** for `TestSqlConnection` / `TestSqlConnectionResult` (read the enum; pick free values in the 90s/190s ranges).
2. **`SqlCredentialDialog` base class** — `ThemeAwareWindow` vs `Window` + manual `TryAttachOwnerToHost`. Either matches house style; pick one.
3. **MessagePack nullable-`string?`** convention — confirm against a sibling message class in `src/AkmlSql.Core/Ipc/Messages/` before declaring `TestSqlConnectionResponse.ErrorMessage`.

**Pre-implementation de-risk (do first, ~5 min):** confirm a connection string built by `System.Data.SqlClient.SqlConnectionStringBuilder` (shell) actually opens under the engine's `Microsoft.Data.SqlClient` (a one-off connect against `192.168.5.123` as `sa`). This is load-bearing for the whole feature and far cheaper to confirm before writing the dialog/store than at the final live test.

*(Resolved during review — no longer open: password quoting → use `System.Data.SqlClient.SqlConnectionStringBuilder`, §5.2; `IServiceProvider` → `ServiceProvider.GlobalProvider`, §5.4; NeedsCredentials lifecycle, multi-window, and stale-credential signal → §5.3/§5.4/§5.7.)*

## 10. Deferred / follow-ups

- Options-dialog toggle for `enableSqlAuthCredentials` (`IntelliSensePage` Build/Load/Save) — the config flag works without it.
- Full "Manage SQL credentials" Options page (list + clear-all). The in-dialog "Clear saved password" covers v1.
- VS 2026 live verification (shared `.projitems`; verify caption/`AuthenticationType` parity, add VS-specific branching only if they differ).

## Appendix A — Integration map (current anchor → planned change)

Every row is a **planned** edit; the anchor is the **current** code location.

| Concern | Current anchor | Planned change |
|---|---|---|
| DPAPI available in Core (netstandard2.0) | `AkmlSql.Core/AkmlSql.Core.csproj:18-22` | + explicit shell `PackageReference` (§6) |
| DPAPI reference pattern to mirror | `AkmlSql.Engine/Ai/Security/CredentialManager.cs` (whole file) | new `SqlCredentialStore` mirrors it |
| Atomic-write idiom | `AkmlSql.Core/Config/ConfigManager.cs:92-115`; dir `Constants.cs:48-58` | copy into `SqlCredentialStore` |
| `MessageTypes` enum | `AkmlSql.Core/Ipc/RpcMessage.cs:43` (req 1–31 & 90–99; resp 101–131 & 190–199) | add `TestSqlConnection`/`…Result` |
| Handler template | `AkmlSql.Engine/Handlers/Control/ControlHandlers.cs:37-55` (PingHandler) | new `TestSqlConnectionHandler` |
| Handler registration | `AkmlSql.Engine/EngineHandlerRegistry.cs:~250` | register the new handler |
| SqlClient open idiom (engine) | `AkmlSql.Engine/Schema/SchemaMetadataService.cs:168-169` | reuse in the handler |
| Login-failure catch (18456/4060/18452/916) | `SchemaMetadataService.cs:231-243` (sets `cache.PermissionDenied`) | reuse in handler; surface as `AuthError` |
| `SchemaStatusHandler` builds the response | `Handlers/Schema/SchemaHandlers.cs:38-83` (`cache` available) | set `response.AuthError = cache.PermissionDenied` |
| `ConnectionChangedHandler` cache lookup | `Handlers/Control/ConnectionChangedHandler.cs:36-48` (`GetOrCreateCache`; terminal `PermissionDenied`) | reset cache when connection string changed |
| Password-safe logging | `AkmlSql.Engine/Schema/ConnectionDiagnostics.cs:33` (`Describe`) | use in handler |
| **`SqlConnectionStringBuilder` in shell** | `Shell.Shared/Execution/MultiDatabaseExecutor.cs:6,85` (`using System.Data.SqlClient`) | use in `BuildSqlAuthConnectionString` |
| Wiring skip-send branches | `Shell.Shared/Editor/ConnectionWiringHelper.cs:44-51, 74-82` | add `SqlPassword` resolve/prompt |
| `SendConnectionChangedAsync` | `ConnectionWiringHelper.cs:163-195`; `ConnectionInfo.cs:1-23` | reuse |
| `DetectAndSendConnection` | `ConnectionWiringHelper.cs:17-18` | reuse; called by margin re-detect |
| `IServiceProvider` source | `Shell.Shared/Editor/TextViewCreationListener.cs:54` (`ServiceProvider.GlobalProvider`) | margin reuses |
| `AuthMode` enum (4 values) | `SsmsConnectionDetector.cs:31-41` | add `SqlPassword` |
| `ClassifyAuth` numeric mapping (case 1 → `Unsupported`) | `SsmsConnectionDetector.cs:498-524` (line 510) | case 1 → `SqlPassword` |
| Bare-username heuristic (→ `Unsupported`) | `SsmsConnectionDetector.cs:304-318` (line 314) | → `SqlPassword` |
| `BuildEngineConnectionString` + suffix | `SsmsConnectionDetector.cs:376-405` (suffix 380-381) | + `BuildSqlAuthConnectionString` |
| `ConnectionResult` (5 fields, **no Login**) | `SsmsConnectionDetector.cs:564-583` (instantiation 360-367) | add `Login`, populate it |
| Detector test file | `tests/AkmlSql.Shell.Shared.Tests/SsmsConnectionDetectorTests.cs` | extend |
| Margin state enum (`Hidden/Loading/Ready`) + poll/apply | `SchemaProgress/SchemaProgressMargin.cs:34, 171, 268-388, 455`; `BeginRefresh` 471-487 | add `NeedsCredentials`; read `SqlAuthState`; `BeginEnterCredentials` |
| `SchemaStatusResponse` shape | `AkmlSql.Core/Ipc/Messages/SchemaStatusRequest.cs:10-45` | add `AuthError` |
| Per-buffer session key | `TextBuffer.Properties["AkmlSqlSessionId"]` — `TextViewCreationListener.cs:36-37`, `SchemaProgressMargin.cs:455` | add `["AkmlSqlAuthState"]` |
| `IntelliSenseSettings` POCO | `AkmlSql.Core/Config/AppSettings.cs:254-321` | add `EnableSqlAuthCredentials` (after 320) |
| Config camelCase policy | `ConfigManager.cs:15-22` | (no change) |
| Options IntelliSense binding (deferred) | `Shell.Shared/Dialogs/Pages/IntelliSensePage.cs:136-171` | deferred toggle |
| WPF owner idiom | `Safety/SafetyWarningDialog.cs:71-85`; `Ui/Theme/ThemeAwareWindow.cs:30-49` | `SqlCredentialDialog` |
| Theme/Freeze/Typography/Cancel-focus | `SafetyWarningDialog.cs:90-96, 193-194, 360-386, 425-429`; `Ui/Theme/Typography.cs:13-14` | `SqlCredentialDialog` |
| No existing PasswordBox | confirmed absent | this dialog adds the first |
