# Contract: saved connections and browser storage (WEB-002, WEB-003, WEB-004, WEB-010)

## 1. Save in the Connect to database dialog (SQL Server)

`ISqlConnectionService` gains:

```csharp
/// Tests the SQL Server login and, when it is accepted, asks the engine to keep the typed password
/// (TestSqlConnectionRequest.RememberPassword = true). Never stores anything in the browser.
Task<ConnectResult> TestAndRememberAsync(ConnectionTarget target, CancellationToken ct);
```

`ConnectionManagerModal.SaveAsync` (SQL Server branch) works as follows:

| Form state | Engine call | Row saved with | Message (`conn-message`) |
| --- | --- | --- | --- |
| Windows authentication | none | `RememberPassword=false` | "Saved." |
| SQL login, Remember off | none; if the row had Remember on, then `Forget` | `RememberPassword=false` | "Saved." (plus "The engine machine no longer keeps the password for {login}." when Forget removed one) |
| SQL login, Remember on, **no password typed** | none | `RememberPassword` = whether the engine already holds one (`RefreshRememberedAsync`) | "Saved." or "Saved. Type the password once and Save again to keep it on the engine machine." |
| SQL login, Remember on, password typed, engine paired with `EngineRemembersSqlServerPasswords` | `TestAndRememberAsync` | `RememberPassword = result.Ok && result.Remembered` | success: "Saved. The engine machine keeps the password for {login}."; ok but not kept: "Saved, but the engine could not keep the password."; failure: "Saved without the password: the login test failed — {error}." |
| SQL login, Remember on, no engine / an engine without the capability (offline) | none | the row's **previous** `RememberPassword` (false for a new row) | "Saved." when no password was typed; otherwise "Saved. No engine that keeps passwords is connected, so the password was not checked or kept." |

Implementation note (2026-11-21): the offline row first said `RememberPassword=false`. That would erase a saved row's
choice during any temporary engine outage, so Save keeps the previous flag when the engine cannot be asked
(`ConnectionSaveRememberTests`).

**Rules:**

- **Always saved.** The row is saved in every case: Save never discards the user's server, login and name.
- **The password field** is cleared after Save, as it is never kept in the browser.
- **The Remember help line.** The existing `conn-sql-remember-help` line under the checkbox states the rule: "(checked
  when you save; never in the browser; any client paired with this engine can use it)". It is one line, because the
  form must still fit 1280×720, and there is no second hint element.
- **Busy state.** Save shows the busy state during the test. A second click is ignored, as it is for Test.
- **PostgreSQL:** unchanged in this feature (research R4).

## 2. Reconnect prefill (`ApplyPrefill`)

When no saved row matches the tab's binding, `_name` gets `"{server} / {database}"` (PostgreSQL:
`"{host} / {database}"`). A note `conn-not-saved-note` reads "Not a saved connection yet. Save keeps it." The field
stays editable.

## 3. `SavedSqlConnection.EffectivePort`

The property is `[JsonIgnore]`. A test round-trips a SQL Server row and asserts that the JSON has no
`effectivePort`, and that a stored JSON with `effectivePort: 5432` still loads.

## 4. Browser storage persistence

- **JS:** `akml-indexeddb.js` exports `persist()` and `persisted()`. Each returns `true`/`false`, or `null` when
  `navigator.storage?.persist` or `persisted` is missing.
- **Service:** `IBrowserStorageState` (singleton) wraps them. `RequestPersistenceAsync` runs at most once per app
  run and returns the cached answer afterwards.
- **Callers:** the Add paths of `ISavedSqlConnectionStore` and `IConnectionStore` call `RequestPersistenceAsync()`
  after a successful add, fire-and-forget. A failure is logged at Trace and never fails the add.
- **Diagnostics row** `diagnostics-storage`, catalog entry "Browser storage", keywords "persistent storage evicted
  cleared quota":

  | `persisted()` | Text (`diag-storage`) |
  | --- | --- |
  | true | "Kept until you clear it (persistent)." |
  | false | "Best effort: the browser may clear it when the disk is low. Saved connections and pairings are kept per address." + **Ask again** (`diag-storage-ask`) |
  | null | "This browser does not report it." |

## 5. Origin banner (`Shared/OriginBanner.razor`, rendered by `Editor.razor`; `data-testid="origin-banner"`)

**Shown when all hold:**

1. `ISavedSqlConnectionStore.ListAsync()` and `IConnectionStore.ListAsync()` are both empty.
2. The page host is `127.0.0.1` or `[::1]`, a loopback alias other than `localhost`.
3. `IPageEngine.GetAsync()` returned `Engine`.
4. `localStorage['akml.originBanner.dismissed']` is not `"1"`.

**Text:** "Saved connections and pairings belong to the address you opened. If you used {scheme}://localhost:{port}/
before, open it there, or import your settings here." There are links to that URL and to the Settings page's Import
settings action, and a Dismiss button (`origin-banner-dismiss`), which sets the key.

The banner never shows on `localhost` or on a non-loopback host, because the right address cannot be known there.
