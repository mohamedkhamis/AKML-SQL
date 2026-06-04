# SQL-auth credential support — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **GIT RULE (overrides the skill's "frequent commits"):** This repo forbids `git add/commit/push` without the user's explicit "yes" to "Ready to commit?". The `Commit` steps below are checkpoints — at each one, **summarize and ask; do not run git** until the user approves.

**Goal:** Let SQL Server-authentication windows (login like `sa`) get schema/IntelliSense by letting the user enter the password once, stored DPAPI-encrypted per `(server, login)`; the engine then connects with a SQL-auth connection string.

**Architecture:** Shell-owned credential store in `AkmlSql.Core`; the shell injects the password into the connection string it already sends via `ConnectionChanged`. A new `TestSqlConnection` IPC validates the password before storage. The "needs credentials" UI state is shell-local (per-buffer `SqlAuthState` in `TextBuffer.Properties`), rendered by the schema-progress margin as a click-to-enter affordance. A stored credential later rejected by the server is surfaced via a new `SchemaStatusResponse.AuthError` flag.

**Tech Stack:** C# (net472 shell shared `.projitems`, netstandard2.0+net10 `AkmlSql.Core`, .NET 10 `AkmlSql.Engine`), MessagePack IPC, WPF (programmatic), DPAPI (`ProtectedData`), `System.Data.SqlClient` (shell) / `Microsoft.Data.SqlClient` (engine), xunit.

**Spec:** `specs/029-sql-auth-credentials/design.md`. Message types allocated: `TestSqlConnection = 93`, `TestSqlConnectionResult = 193` (free in the reserved 90–99 / 190–199 ranges).

---

## Task 0: Pre-flight de-risk (manual, ~5 min)

**Why first:** the whole feature relies on a string built by `System.Data.SqlClient.SqlConnectionStringBuilder` (shell) opening cleanly under the engine's `Microsoft.Data.SqlClient`. Confirm before building anything else.

- [ ] **Step 1: Confirm the builder ↔ Microsoft.Data.SqlClient round-trip**

Add this temporary fact to `tests/AkmlSql.Engine.Tests` (any test class), fill in the live server/login/password, run once, then delete it:

```csharp
[Fact(Skip = "manual — needs the live SQL server; un-skip locally, run once, then delete")]
public async System.Threading.Tasks.Task SmokeTest_BuilderStringOpensUnderMicrosoftDataSqlClient()
{
    var b = new System.Data.SqlClient.SqlConnectionStringBuilder
    {
        DataSource = "192.168.5.123", InitialCatalog = "NatGas_G2_Testing",
        UserID = "sa", Password = "<REAL_PASSWORD>",
        IntegratedSecurity = false, ApplicationName = "AKML SQL Engine",
        TrustServerCertificate = true, Encrypt = false, ConnectTimeout = 5
    };
    await using var conn = new Microsoft.Data.SqlClient.SqlConnection(b.ConnectionString);
    await conn.OpenAsync();
    Assert.Equal(System.Data.ConnectionState.Open, conn.State);
}
```

Run: temporarily remove `Skip`, then `dotnet test tests/AkmlSql.Engine.Tests --filter SmokeTest_BuilderStringOpensUnderMicrosoftDataSqlClient`
Expected: PASS (connection opens). If it fails on a keyword mismatch, stop and reconcile the connection-string keywords before proceeding. **Delete the test afterward** (it carries a live password).

---

## Task 1: `SqlCredentialStore` (DPAPI credential store) — TDD

**Files:**
- Create: `src/AkmlSql.Core/Config/SqlCredentialStore.cs`
- Test: `tests/AkmlSql.Core.Tests/SqlCredentialStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/AkmlSql.Core.Tests/SqlCredentialStoreTests.cs`:

```csharp
using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests
{
    // NOTE: SqlCredentialStore persists to %AppData%\AKML SQL\sql-credentials.json. These tests
    // use a unique (server, login) per case and clean up with Remove() so they don't collide.
    public class SqlCredentialStoreTests
    {
        [Fact]
        public void SaveThenTryGet_RoundTripsThePassword()
        {
            var server = "unit-test-srv-1"; var login = "sa";
            try
            {
                SqlCredentialStore.Save(server, login, "P@ss;w'd\"x");
                Assert.True(SqlCredentialStore.TryGet(server, login, out var pwd));
                Assert.Equal("P@ss;w'd\"x", pwd);
            }
            finally { SqlCredentialStore.Remove(server, login); }
        }

        [Fact]
        public void TryGet_UnknownKey_ReturnsFalse()
        {
            Assert.False(SqlCredentialStore.TryGet("no-such-srv", "no-such-login", out var pwd));
            Assert.Equal(string.Empty, pwd);
        }

        [Fact]
        public void Match_IsCaseInsensitive_OnServerAndLogin()
        {
            var server = "Unit-Test-Srv-2"; var login = "SA";
            try
            {
                SqlCredentialStore.Save(server, login, "x");
                Assert.True(SqlCredentialStore.TryGet("unit-test-srv-2", "sa", out _));
                Assert.True(SqlCredentialStore.Has("UNIT-TEST-SRV-2", "Sa"));
            }
            finally { SqlCredentialStore.Remove(server, login); }
        }

        [Fact]
        public void Remove_DeletesTheEntry()
        {
            var server = "unit-test-srv-3"; var login = "sa";
            SqlCredentialStore.Save(server, login, "x");
            SqlCredentialStore.Remove(server, login);
            Assert.False(SqlCredentialStore.TryGet(server, login, out _));
        }

        [Fact]
        public void Save_ReplacesExistingEntry_ForSameKey()
        {
            var server = "unit-test-srv-4"; var login = "sa";
            try
            {
                SqlCredentialStore.Save(server, login, "first");
                SqlCredentialStore.Save(server, login, "second");
                Assert.True(SqlCredentialStore.TryGet(server, login, out var pwd));
                Assert.Equal("second", pwd);
            }
            finally { SqlCredentialStore.Remove(server, login); }
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AkmlSql.Core.Tests --filter SqlCredentialStoreTests`
Expected: FAIL to compile — `SqlCredentialStore` does not exist.

- [ ] **Step 3: Implement `SqlCredentialStore`**

Create `src/AkmlSql.Core/Config/SqlCredentialStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace AkmlSql.Core.Config
{
    /// <summary>One stored SQL-auth credential. The password is DPAPI-encrypted
    /// (<c>dpapi:&lt;base64&gt;</c>); plaintext is never persisted.</summary>
    public sealed class SqlCredentialEntry
    {
        public string Server { get; set; } = string.Empty;
        public string Login { get; set; } = string.Empty;
        public string EncryptedPassword { get; set; } = string.Empty;
    }

    /// <summary>
    /// Spec 029. Per-user store of SQL Server-authentication passwords keyed by (server, login),
    /// so the out-of-process engine can connect with SQL auth for IntelliSense. Passwords are
    /// encrypted at rest with Windows DPAPI (CurrentUser scope + app entropy) and saved to
    /// <c>%AppData%\AKML SQL\sql-credentials.json</c> via an atomic temp+rename write
    /// (mirrors <see cref="ConfigManager"/>). All public methods are guarded by a process-wide
    /// lock to make read-modify-write atomic.
    /// </summary>
    public static class SqlCredentialStore
    {
        private const string EncryptedPrefix = "dpapi:";
        private static readonly byte[] AppEntropy = ComputeEntropy();
        private static readonly object _gate = new object();

        // netstandard2.0-safe: SHA256.HashData is .NET5+, so hash via an instance.
        private static byte[] ComputeEntropy()
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes("AkmlSql-SqlCred-v1"));
        }

        // Same serializer options as ConfigManager (the TypeInfoResolver line is required for the
        // .NET 10 trimmed engine where reflection-based serialization is disabled).
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };

        // Same directory as config.json (%AppData%\AKML SQL), derived from ConfigFilePath so we
        // don't depend on a separate Constants member name.
        private static string FilePath
        {
            get
            {
                var dir = Path.GetDirectoryName(Constants.ConfigFilePath);
                if (string.IsNullOrEmpty(dir))
                    dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AKML SQL");
                return Path.Combine(dir, "sql-credentials.json");
            }
        }

        /// <summary>Decrypts the password for (server, login), or returns false if none is stored.
        /// A single entry whose blob fails to decrypt (e.g. roamed profile) is removed and treated as
        /// absent — it never blocks the other entries.</summary>
        public static bool TryGet(string server, string login, out string password)
        {
            password = string.Empty;
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(login)) return false;

            lock (_gate)
            {
                var list = LoadList();
                var entry = list.FirstOrDefault(e =>
                    string.Equals(e.Server, server, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
                if (entry == null) return false;

                try
                {
                    password = Decrypt(entry.EncryptedPassword);
                    return !string.IsNullOrEmpty(password);
                }
                catch (CryptographicException ex)
                {
                    Log.Warning(ex, "SqlCredentialStore: could not decrypt credential for {Server}/{Login}; dropping it", server, login);
                    list.Remove(entry);
                    SaveList(list);
                    password = string.Empty;
                    return false;
                }
            }
        }

        /// <summary>True if a credential is stored for (server, login) (without decrypting).</summary>
        public static bool Has(string server, string login)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(login)) return false;
            lock (_gate)
            {
                return LoadList().Any(e =>
                    string.Equals(e.Server, server, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Encrypts and stores the password for (server, login), replacing any existing entry.</summary>
        public static void Save(string server, string login, string password)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(login)) return;
            lock (_gate)
            {
                var list = LoadList();
                list.RemoveAll(e =>
                    string.Equals(e.Server, server, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
                list.Add(new SqlCredentialEntry
                {
                    Server = server,
                    Login = login,
                    EncryptedPassword = Encrypt(password)
                });
                SaveList(list);
            }
        }

        /// <summary>Removes the stored credential for (server, login), if any.</summary>
        public static void Remove(string server, string login)
        {
            if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(login)) return;
            lock (_gate)
            {
                var list = LoadList();
                int removed = list.RemoveAll(e =>
                    string.Equals(e.Server, server, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Login, login, StringComparison.OrdinalIgnoreCase));
                if (removed > 0) SaveList(list);
            }
        }

        // --- internals (callers hold _gate) ---

        private static List<SqlCredentialEntry> LoadList()
        {
            try
            {
                var path = FilePath;
                if (!File.Exists(path)) return new List<SqlCredentialEntry>();
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<SqlCredentialEntry>>(json, SerializerOptions)
                       ?? new List<SqlCredentialEntry>();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SqlCredentialStore: failed to read store; treating as empty");
                return new List<SqlCredentialEntry>();
            }
        }

        private static void SaveList(List<SqlCredentialEntry> list)
        {
            try
            {
                var path = FilePath;
                var dir = Path.GetDirectoryName(path);
                if (dir != null) Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(list, SerializerOptions);
                var tempPath = path + ".tmp";
                File.WriteAllText(tempPath, json);
#if NETSTANDARD2_0
                if (File.Exists(path)) File.Replace(tempPath, path, null);
                else File.Move(tempPath, path);
#else
                File.Move(tempPath, path, overwrite: true);
#endif
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SqlCredentialStore: failed to save store");
            }
        }

        private static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            try
            {
                var cipher = ProtectedData.Protect(plainBytes, AppEntropy, DataProtectionScope.CurrentUser);
                return EncryptedPrefix + Convert.ToBase64String(cipher);
            }
            finally
            {
                Array.Clear(plainBytes, 0, plainBytes.Length); // portable across netstandard2.0 + net10
            }
        }

        private static string Decrypt(string encrypted)
        {
            if (string.IsNullOrEmpty(encrypted)) return string.Empty;
            if (!encrypted.StartsWith(EncryptedPrefix, StringComparison.Ordinal)) return string.Empty;
            var cipher = Convert.FromBase64String(encrypted.Substring(EncryptedPrefix.Length));
            var plainBytes = ProtectedData.Unprotect(cipher, AppEntropy, DataProtectionScope.CurrentUser);
            try { return Encoding.UTF8.GetString(plainBytes); }
            finally { Array.Clear(plainBytes, 0, plainBytes.Length); }
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AkmlSql.Core.Tests --filter SqlCredentialStoreTests`
Expected: PASS (5/5).

- [ ] **Step 5: Commit checkpoint** — summarize, ask "Ready to commit?", wait for "yes", then:
```bash
git add src/AkmlSql.Core/Config/SqlCredentialStore.cs tests/AkmlSql.Core.Tests/SqlCredentialStoreTests.cs
git commit -m "feat(029): SqlCredentialStore — DPAPI per-(server,login) SQL password store"
```

---

## Task 2: Detector — `AuthMode.SqlPassword`, `Login`, `BuildSqlAuthConnectionString` — TDD

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Editor/SsmsConnectionDetector.cs` (enum 31-41; ClassifyAuth 507-558; ConnectionResult 564-583; instantiation 360-367; warning block ~334-358; add new method near 376)
- Test: `tests/AkmlSql.Shell.Shared.Tests/SsmsConnectionDetectorTests.cs` (extend)

- [ ] **Step 1: Write the failing tests** — append to `SsmsConnectionDetectorTests.cs` (inside the class):

```csharp
        [Theory]
        [InlineData("1", AuthMode.SqlPassword)]                         // numeric SqlPassword
        [InlineData("SqlPassword", AuthMode.SqlPassword)]               // string form
        [InlineData("SQL Server Authentication", AuthMode.SqlPassword)] // SSMS label
        [InlineData("2", AuthMode.Unsupported)]                          // AAD Password stays unsupported
        [InlineData("4", AuthMode.Unsupported)]                          // AAD Interactive stays unsupported
        [InlineData("3", AuthMode.AzureAdIntegrated)]
        [InlineData("0", AuthMode.Windows)]
        public void ClassifyAuth_MapsSqlLoginToSqlPassword(string raw, AuthMode expected)
        {
            Assert.Equal(expected, SsmsConnectionDetector.ClassifyAuth(raw));
        }

        [Fact]
        public void ParseCaption_BareLogin_ClassifiesSqlPassword_CapturesLogin_NotEngineUsableYet()
        {
            var r = SsmsConnectionDetector.ParseCaption("q.sql - 192.168.5.123.NatGas_G2_Testing (sa (53))");
            Assert.NotNull(r);
            Assert.Equal("192.168.5.123", r.Server);
            Assert.Equal("NatGas_G2_Testing", r.Database);
            Assert.Equal("sa", r.Login);
            Assert.Equal(AuthMode.SqlPassword, r.AuthMode);
            Assert.False(r.IsEngineUsable);   // no password at parse time
            Assert.Null(r.ConnectionString);
        }

        [Fact]
        public void BuildSqlAuthConnectionString_EscapesSpecialChars_AndSetsFields()
        {
            var cs = SsmsConnectionDetector.BuildSqlAuthConnectionString("10.0.0.5", "MyDb", "sa", "P@ss;w'd\"x");
            var b = new System.Data.SqlClient.SqlConnectionStringBuilder(cs);
            Assert.Equal("10.0.0.5", b.DataSource);
            Assert.Equal("MyDb", b.InitialCatalog);
            Assert.Equal("sa", b.UserID);
            Assert.Equal("P@ss;w'd\"x", b.Password);   // round-trips intact despite ; ' "
            Assert.False(b.Encrypt);
            Assert.True(b.TrustServerCertificate);
        }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/AkmlSql.Shell.Shared.Tests --filter SsmsConnectionDetector`
Expected: FAIL to compile — `AuthMode.SqlPassword`, `ConnectionResult.Login`, and `BuildSqlAuthConnectionString` don't exist.

- [ ] **Step 3a: Add `SqlPassword` to the `AuthMode` enum**

In `SsmsConnectionDetector.cs`, the enum (lines ~31-41). Add `SqlPassword` before `Unsupported`:

```csharp
        internal enum AuthMode
        {
            Unknown,
            Windows,
            AzureAdIntegrated,
            SqlPassword,
            Unsupported
        }
```

- [ ] **Step 3b: Map SQL logins to `SqlPassword` in `ClassifyAuth`**

Change the numeric case 1 (line ~510):
```csharp
                    1 => AuthMode.Unsupported, // SqlPassword
```
to:
```csharp
                    1 => AuthMode.SqlPassword, // SQL login — engine connects with a stored/entered credential
```

Change the string "SQL" branch (lines ~553-558):
```csharp
            if (lower.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // SQL Server auth — we'd need the password, which SSMS doesn't expose.
                Log.Debug("ClassifyAuth: string raw='{Raw}' matched 'SQL' → Unsupported", raw);
                return AuthMode.Unsupported;
            }
```
to:
```csharp
            if (lower.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // SQL Server auth — engine connects with a stored/entered credential (spec 029).
                Log.Debug("ClassifyAuth: string raw='{Raw}' matched 'SQL' → SqlPassword", raw);
                return AuthMode.SqlPassword;
            }
```

- [ ] **Step 3c: Map the bare-username heuristic to `SqlPassword`**

In `ParseCaption`, the heuristic block (line ~314), change:
```csharp
                    authMode = AuthMode.Unsupported;
```
to:
```csharp
                    authMode = AuthMode.SqlPassword;
```
(Leave the surrounding `if (!hasDomainPrefix && !looksLikeUpn)` and the Debug log unchanged — only the assigned mode changes.)

- [ ] **Step 3d: Add `Login` to `ConnectionResult` and populate it**

In the `ConnectionResult` class (after `Database`):
```csharp
            public string Database { get; set; }

            /// <summary>The login parsed from the SSMS caption "(Login (SPID))", used to key the
            /// SQL credential store. Empty when not available. Spec 029.</summary>
            public string Login { get; set; }
```

In the `ConnectionResult` instantiation (lines ~360-367), add the `Login` line:
```csharp
            return new ConnectionResult
            {
                Server = server,
                Database = database,
                Login = captionUserName,
                ConnectionString = connStr,
                AuthMode = authMode,
                IsEngineUsable = usable
            };
```

- [ ] **Step 3e: Don't show the "reconnect with Windows auth" warning for SQL auth**

In the `else` (not-usable) block (lines ~334-358), wrap the existing `Log.Warning(...)`/dedupe so `SqlPassword` logs a quiet Debug instead. Replace the block body with:

```csharp
            else
            {
                if (authMode == AuthMode.SqlPassword)
                {
                    // SQL auth is handled by the click-to-enter affordance (spec 029) — no scary
                    // "IntelliSense disabled" warning. The wiring/margin take over from here.
                    Log.Debug(
                        "SsmsConnectionDetector: SQL auth for {Server}.{Database} (login='{Login}') — schema loads once a credential is stored/entered",
                        server, database, captionUserName);
                }
                else
                {
                    // One Warning per (server, database, authMode) — retries stay quiet after the first.
                    var dedupeKey = $"{server}|{database}|{authMode}";
                    if (_warnedUnusableAuth.TryAdd(dedupeKey, 0))
                    {
                        Log.Warning(
                            "AKML SQL IntelliSense disabled for {Server}.{Database}: " +
                            "this window is using {AuthMode} (raw='{RawAuth}'), which the out-of-process " +
                            "engine cannot silently reuse. To enable IntelliSense, either (a) reconnect " +
                            "this window using Windows authentication, or (b) grant your Windows user " +
                            "access to {TargetDatabase} in SQL Server. Caption='{Caption}'.",
                            server, database, authMode, rawAuthType ?? "(null)", database, caption);
                    }
                    else
                    {
                        Log.Debug(
                            "SsmsConnectionDetector: repeat unusable-auth detection for {Server}.{Database} ({AuthMode}) — warning already emitted",
                            server, database, authMode);
                    }
                }
            }
```

- [ ] **Step 3f: Add `BuildSqlAuthConnectionString`**

Immediately above `private static string BuildEngineConnectionString(...)` (line ~376), add:

```csharp
        /// <summary>
        /// Builds the SQL-authentication connection string the engine uses once the user's password
        /// is available (spec 029). Uses <see cref="System.Data.SqlClient.SqlConnectionStringBuilder"/>
        /// so the password is escaped correctly (semicolons, quotes, equals) — never hand-concatenated.
        /// The keyword set is parse-compatible with the engine's Microsoft.Data.SqlClient.
        /// </summary>
        internal static string BuildSqlAuthConnectionString(string server, string database, string login, string password)
        {
            var b = new System.Data.SqlClient.SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                UserID = login,
                Password = password,
                IntegratedSecurity = false,
                ApplicationName = "AKML SQL Engine",
                TrustServerCertificate = true,
                Encrypt = false,
                ConnectTimeout = 5
            };
            return b.ConnectionString;
        }
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/AkmlSql.Shell.Shared.Tests --filter SsmsConnectionDetector`
Expected: PASS (the new theory + 2 facts + the existing `ParseCaption_SplitsServerDatabaseAtLastDot` 9 cases).

- [ ] **Step 5: Commit checkpoint** — ask first, then:
```bash
git add src/AkmlSql.Shell.Shared/Editor/SsmsConnectionDetector.cs tests/AkmlSql.Shell.Shared.Tests/SsmsConnectionDetectorTests.cs
git commit -m "feat(029): classify SQL logins as AuthMode.SqlPassword + BuildSqlAuthConnectionString + ConnectionResult.Login"
```

---

## Task 3: IPC message types + `TestSqlConnection` messages

**Files:**
- Modify: `src/AkmlSql.Core/Ipc/RpcMessage.cs` (add two constants)
- Create: `src/AkmlSql.Core/Ipc/Messages/TestSqlConnectionMessages.cs`

- [ ] **Step 1: Add the message-type constants**

In `RpcMessage.cs`, after `public const int EncryptedObjectDecryption = 92;` add:
```csharp

        // Shell → Engine (Spec 029: SQL-auth credential validation)
        public const int TestSqlConnection = 93;
```
After `public const int EncryptedObjectDecryptionResult = 192;` add:
```csharp

        // Engine → Shell (Spec 029)
        public const int TestSqlConnectionResult = 193;
```

- [ ] **Step 2: Create the message classes**

Create `src/AkmlSql.Core/Ipc/Messages/TestSqlConnectionMessages.cs`:
```csharp
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Shell → Engine (spec 029): validate a SQL-auth connection string by opening a short-timeout
    /// test connection. Carries the password inside the connection string; it travels only over the
    /// ACL'd named pipe and is never logged (the handler logs via ConnectionDiagnostics.Describe).
    /// </summary>
    [MessagePackObject]
    public class TestSqlConnectionRequest
    {
        [Key(0)]
        public string ConnectionString { get; set; } = string.Empty;
    }

    /// <summary>Engine → Shell (spec 029): result of <see cref="TestSqlConnectionRequest"/>.</summary>
    [MessagePackObject]
    public class TestSqlConnectionResponse
    {
        [Key(0)]
        public bool Ok { get; set; }

        /// <summary>SQL error text on failure (e.g. "Login failed for user 'sa'."); null on success.</summary>
        [Key(1)]
        public string? ErrorMessage { get; set; }
    }
}
```

- [ ] **Step 3: Build Core to verify it compiles**

Run: `dotnet build src/AkmlSql.Core/AkmlSql.Core.csproj -c Release`
Expected: build succeeds (both target frameworks).

- [ ] **Step 4: Commit checkpoint** — ask first, then:
```bash
git add src/AkmlSql.Core/Ipc/RpcMessage.cs src/AkmlSql.Core/Ipc/Messages/TestSqlConnectionMessages.cs
git commit -m "feat(029): add TestSqlConnection IPC message types (93/193)"
```

---

## Task 4: `SchemaStatusResponse.AuthError` + handler populates it

**Files:**
- Modify: `src/AkmlSql.Core/Ipc/Messages/SchemaStatusRequest.cs` (add field to the response)
- Modify: `src/AkmlSql.Engine/Handlers/Schema/SchemaHandlers.cs` (set the field)

- [ ] **Step 1: Add the `AuthError` field**

In `SchemaStatusRequest.cs`, in `SchemaStatusResponse`, after the `Exists` property (Key 4):
```csharp
        /// <summary>True if the engine's schema load for this session hit a login/permission failure
        /// (4060/18456/18452/916). The shell uses this to surface "credentials rejected" for SQL-auth
        /// sessions. Spec 029.</summary>
        [Key(5)]
        public bool AuthError { get; set; }
```

- [ ] **Step 2: Populate it in `SchemaStatusHandler`**

In `SchemaHandlers.cs`, after `response.Phase = (int)cache.Phase;` add:
```csharp
            response.AuthError = cache.PermissionDenied;
```

- [ ] **Step 3: Build the engine**

Run: `dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release`
Expected: build succeeds.

- [ ] **Step 4: Commit checkpoint** — ask first, then:
```bash
git add src/AkmlSql.Core/Ipc/Messages/SchemaStatusRequest.cs src/AkmlSql.Engine/Handlers/Schema/SchemaHandlers.cs
git commit -m "feat(029): surface schema-cache PermissionDenied as SchemaStatusResponse.AuthError"
```

---

## Task 5: `TestSqlConnectionHandler` (engine) — TDD

**Files:**
- Create: `src/AkmlSql.Engine/Handlers/Control/TestSqlConnectionHandler.cs`
- Modify: `src/AkmlSql.Engine/EngineHandlerRegistry.cs` (register it, near the other Control handlers)
- Test: `tests/AkmlSql.Engine.Tests/TestSqlConnectionHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/AkmlSql.Engine.Tests/TestSqlConnectionHandlerTests.cs`:
```csharp
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Control;
using Xunit;

namespace AkmlSql.Engine.Tests
{
    public class TestSqlConnectionHandlerTests
    {
        [Fact]
        public async Task EmptyConnectionString_ReturnsNotOk()
        {
            var handler = new TestSqlConnectionHandler();
            var resp = await handler.HandleAsync(new TestSqlConnectionRequest { ConnectionString = "" }, null!, CancellationToken.None);
            Assert.False(resp.Ok);
            Assert.False(string.IsNullOrEmpty(resp.ErrorMessage));
        }

        [Fact]
        public async Task UnreachableServer_ReturnsNotOk_WithMessage()
        {
            var handler = new TestSqlConnectionHandler();
            var req = new TestSqlConnectionRequest
            {
                // Bogus host + tiny timeout so the test fails fast.
                ConnectionString = "Data Source=akml-nonexistent-host-xyz,14330;Initial Catalog=x;" +
                                   "User ID=sa;Password=wrong;Connect Timeout=2;TrustServerCertificate=true;Encrypt=false"
            };
            var resp = await handler.HandleAsync(req, null!, CancellationToken.None);
            Assert.False(resp.Ok);
            Assert.False(string.IsNullOrEmpty(resp.ErrorMessage));
        }
    }
}
```
(The handler ignores `RpcContext`, so passing `null!` is safe.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/AkmlSql.Engine.Tests --filter TestSqlConnectionHandlerTests`
Expected: FAIL to compile — `TestSqlConnectionHandler` doesn't exist.

- [ ] **Step 3: Implement the handler**

Create `src/AkmlSql.Engine/Handlers/Control/TestSqlConnectionHandler.cs`:
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Transports;
using Microsoft.Data.SqlClient;
using Serilog;

namespace AkmlSql.Engine.Handlers.Control
{
    /// <summary>
    /// Spec 029. Validates a candidate SQL-auth connection string by opening a short-timeout
    /// connection. The shell calls this before persisting a password, so a bad password is never
    /// stored. Never logs the raw connection string — uses ConnectionDiagnostics.Describe.
    /// </summary>
    public sealed class TestSqlConnectionHandler
        : IRpcRequestHandler<TestSqlConnectionRequest, TestSqlConnectionResponse>
    {
        public int RequestMessageType => MessageTypes.TestSqlConnection;
        public int ResponseMessageType => MessageTypes.TestSqlConnectionResult;

        public async Task<TestSqlConnectionResponse> HandleAsync(
            TestSqlConnectionRequest request, RpcContext ctx, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrEmpty(request.ConnectionString))
                return new TestSqlConnectionResponse { Ok = false, ErrorMessage = "No connection string supplied." };

            try
            {
                await using var conn = new SqlConnection(request.ConnectionString);
                await conn.OpenAsync(ct);
                Log.Information("TestSqlConnection ok — {ConnDesc}",
                    ConnectionDiagnostics.Describe(request.ConnectionString));
                return new TestSqlConnectionResponse { Ok = true };
            }
            catch (SqlException sqlEx)
            {
                Log.Warning("TestSqlConnection failed (err={Num} state={State}) — {ConnDesc}",
                    sqlEx.Number, sqlEx.State, ConnectionDiagnostics.Describe(request.ConnectionString));
                return new TestSqlConnectionResponse { Ok = false, ErrorMessage = sqlEx.Message };
            }
            catch (Exception ex)
            {
                Log.Warning("TestSqlConnection error — {ConnDesc}: {Msg}",
                    ConnectionDiagnostics.Describe(request.ConnectionString), ex.Message);
                return new TestSqlConnectionResponse { Ok = false, ErrorMessage = ex.Message };
            }
        }
    }
}
```

- [ ] **Step 4: Register the handler**

In `src/AkmlSql.Engine/EngineHandlerRegistry.cs`, find where the Control handlers are registered (search for `new PingHandler` / `router.Register`). Add alongside them:
```csharp
            router.Register(new TestSqlConnectionHandler());
```
(If registration uses a different helper than `router.Register`, match the surrounding handlers' exact call — the goal is one registration line for `TestSqlConnectionHandler`.)

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/AkmlSql.Engine.Tests --filter TestSqlConnectionHandlerTests`
Expected: PASS (2/2; the unreachable-server case takes ~2s for the connect timeout).

- [ ] **Step 6: Commit checkpoint** — ask first, then:
```bash
git add src/AkmlSql.Engine/Handlers/Control/TestSqlConnectionHandler.cs src/AkmlSql.Engine/EngineHandlerRegistry.cs tests/AkmlSql.Engine.Tests/TestSqlConnectionHandlerTests.cs
git commit -m "feat(029): TestSqlConnectionHandler — validate SQL credential before storage"
```

---

## Task 6: Stale-credential recovery — reset terminal cache on connection-string change

**Files:**
- Modify: `src/AkmlSql.Engine/Handlers/Control/ConnectionChangedHandler.cs`

**Why:** a `PermissionDenied` cache is terminal — a corrected password re-sending `ConnectionChanged` for the same session+db would otherwise never retry Phase A. Reset the cache when the incoming connection string differs from the session's previous one.

- [ ] **Step 1: Capture the old connection string and reset on change**

In `ConnectionChangedHandler.HandleAsync`, **before** `ctx.Sessions.UpdateSession(request);`, capture the prior string:
```csharp
            var previousConnStr = ctx.Sessions.GetSession(request.SessionId)?.ConnectionString;

            ctx.Sessions.UpdateSession(request);
```
Then, **after** `var schemaCache = ctx.SchemaCache.GetOrCreateCache(request.SessionId, request.DatabaseName);`, add:
```csharp
            // Spec 029: a corrected credential (or any reconnect with different identity) produces a
            // different connection string. A PermissionDenied cache is otherwise terminal and would
            // never retry Phase A — reset it so the new credential gets a fresh attempt.
            if (schemaCache.PermissionDenied &&
                !string.Equals(previousConnStr, request.ConnectionString, StringComparison.Ordinal))
            {
                schemaCache.Schemas.Clear();
                schemaCache.ForeignKeys.Clear();
                schemaCache.Phase = PopulationPhase.NotLoaded;
                schemaCache.PermissionDenied = false;
                Log.Information("ConnectionChanged: connection string changed for session={Session} db={Db} — reset permission-denied cache for a fresh Phase A",
                    request.SessionId, request.DatabaseName);
            }
```
(`PopulationPhase` is already in scope — `ConnectionChangedHandler` references `PopulationPhase.NotLoaded`/`PhaseA` elsewhere. `Log` is `Serilog.Log`, already used in this file.)

- [ ] **Step 2: Build the engine**

Run: `dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release`
Expected: build succeeds.

- [ ] **Step 3: Commit checkpoint** — ask first, then:
```bash
git add src/AkmlSql.Engine/Handlers/Control/ConnectionChangedHandler.cs
git commit -m "feat(029): reset terminal PermissionDenied cache when connection string changes"
```

---

## Task 7: `SqlAuthState` per-buffer marker

**Files:**
- Create: `src/AkmlSql.Shell.Shared/Editor/SqlAuthState.cs`

- [ ] **Step 1: Create the class**

```csharp
namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// Spec 029. Per-buffer marker for a SQL-authentication editor window, stored in
    /// <c>ITextBuffer.Properties["AkmlSqlAuthState"]</c>. Its presence tells the schema-progress
    /// margin this is a SQL-auth session (so a server-side login rejection is shown as
    /// "credentials rejected" rather than a Windows-auth permission denial). It NEVER holds the
    /// plaintext password — the password lives only (encrypted) in <c>SqlCredentialStore</c>.
    /// </summary>
    internal sealed class SqlAuthState
    {
        public string Server { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public string Login { get; set; } = string.Empty;

        /// <summary>True when this SQL-auth window has no engine session yet — no stored credential,
        /// or its credential was rejected. The margin renders the click-to-enter affordance.</summary>
        public bool NeedsCredentials { get; set; }
    }
}
```

- [ ] **Step 2: Commit checkpoint** — ask first, then:
```bash
git add src/AkmlSql.Shell.Shared/Editor/SqlAuthState.cs
git commit -m "feat(029): SqlAuthState per-buffer SQL-auth marker"
```

---

## Task 8: Wiring — resolve stored credential or mark NeedsCredentials

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Editor/ConnectionWiringHelper.cs`

This task has no unit test (it needs DTE + the engine pipe); it's verified live in Task 13. Implement carefully against the existing structure.

- [ ] **Step 1: Add the `using` for the credential store**

At the top of `ConnectionWiringHelper.cs`, after `using AkmlSql.Core.Ipc.Messages;`, add:
```csharp
using AkmlSql.Core.Config;
```

- [ ] **Step 2: Call `MaybeApplyStoredSqlCredential` before BOTH `IsEngineUsable` checks**

In the **retry-loop** branch, the `conn` is obtained inside `Dispatcher.InvokeAsync`. Right after that assignment, add the call. Change:
```csharp
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    conn = SsmsConnectionDetector.TryDetectConnection(serviceProvider, textView);
                                });
                                if (conn != null)
                                {
```
to:
```csharp
                                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    conn = SsmsConnectionDetector.TryDetectConnection(serviceProvider, textView);
                                    MaybeApplyStoredSqlCredential(conn, textView);
                                });
                                if (conn != null)
                                {
```

In the **synchronous** branch, before `if (!connection.IsEngineUsable)` (line ~74), add the call. Change:
```csharp
                if (!connection.IsEngineUsable)
                {
```
to:
```csharp
                MaybeApplyStoredSqlCredential(connection, textView);

                if (!connection.IsEngineUsable)
                {
```

- [ ] **Step 3: Add the helper methods**

Add these to `ConnectionWiringHelper` (e.g. just below `DetectAndSendConnection`):

```csharp
        /// <summary>
        /// Spec 029. For a SQL-auth detection: write the per-buffer <see cref="SqlAuthState"/> marker,
        /// and if a credential is already stored, fill the connection string + mark engine-usable so the
        /// connection flows through the existing send path. When no credential is stored, leave the
        /// connection not-engine-usable (the existing skip path runs) and NeedsCredentials=true so the
        /// margin shows the click-to-enter affordance. No-op for non-SQL auth, or when disabled by config.
        /// </summary>
        private static void MaybeApplyStoredSqlCredential(
            SsmsConnectionDetector.ConnectionResult conn,
            Microsoft.VisualStudio.Text.Editor.IWpfTextView textView)
        {
            try
            {
                if (conn == null || conn.AuthMode != SsmsConnectionDetector.AuthMode.SqlPassword) return;

                var settings = ConfigManager.Load();
                if (!settings.IntelliSense.EnableSqlAuthCredentials) return; // opt-out → behave like Unsupported

                bool has = SqlCredentialStore.TryGet(conn.Server, conn.Login, out var pwd);

                if (textView != null)
                {
                    textView.TextBuffer.Properties["AkmlSqlAuthState"] = new SqlAuthState
                    {
                        Server = conn.Server,
                        Database = conn.Database,
                        Login = conn.Login,
                        NeedsCredentials = !has
                    };
                }

                if (has)
                {
                    conn.ConnectionString = SsmsConnectionDetector.BuildSqlAuthConnectionString(
                        conn.Server, conn.Database, conn.Login, pwd);
                    conn.IsEngineUsable = true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "MaybeApplyStoredSqlCredential failed");
            }
        }

        /// <summary>
        /// Spec 029. Called by the margin while a buffer is in NeedsCredentials (and after a successful
        /// dialog save): if a credential is now stored for the buffer's (server, login), build the SQL
        /// connection string, send ConnectionChanged, clear NeedsCredentials, and return true. Reads the
        /// stored marker — no caption parse, no DTE walk (cheap enough for the 1s poll). Returns false
        /// when there is no marker or no stored credential.
        /// </summary>
        public static bool TryResolveStoredSqlCredential(
            string sessionId, Microsoft.VisualStudio.Text.Editor.IWpfTextView textView)
        {
            try
            {
                if (textView == null) return false;
                if (!textView.TextBuffer.Properties.TryGetProperty<SqlAuthState>("AkmlSqlAuthState", out var state)
                    || state == null)
                    return false;
                if (!SqlCredentialStore.TryGet(state.Server, state.Login, out var pwd))
                    return false;

                var conn = new SsmsConnectionDetector.ConnectionResult
                {
                    Server = state.Server,
                    Database = state.Database,
                    Login = state.Login,
                    ConnectionString = SsmsConnectionDetector.BuildSqlAuthConnectionString(
                        state.Server, state.Database, state.Login, pwd),
                    AuthMode = SsmsConnectionDetector.AuthMode.SqlPassword,
                    IsEngineUsable = true
                };
                state.NeedsCredentials = false;

                var client = EngineLifecycle.Manager?.Client;
                if (client != null && client.IsConnected)
                    Task.Run(() => SendConnectionChangedAsync(client, sessionId, conn));
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "TryResolveStoredSqlCredential failed for session={Session}", sessionId);
                return false;
            }
        }
```

- [ ] **Step 4: Build the shell to verify it compiles** (full MSBuild — see the build command in Task 13). For a quick compile check of the shared sources, the Shell.Shared.Tests build covers it:

Run: `dotnet build tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj -c Release`
Expected: build succeeds (this compiles the `.projitems` including `ConnectionWiringHelper`).

- [ ] **Step 5: Commit checkpoint** — ask first, then:
```bash
git add src/AkmlSql.Shell.Shared/Editor/ConnectionWiringHelper.cs
git commit -m "feat(029): wiring resolves stored SQL credential or marks NeedsCredentials"
```

---

## Task 9: `SqlCredentialDialog` (WPF modal)

**Files:**
- Create: `src/AkmlSql.Shell.Shared/Editor/SqlCredentialDialog.cs`

- [ ] **Step 1: Create the dialog**

```csharp
#nullable enable
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Ui.Theme;
using Orientation = System.Windows.Controls.Orientation;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// Spec 029. Theme-aware modal that collects a SQL Server-auth password, validates it against the
    /// server via the engine (TestSqlConnection IPC), and stores it DPAPI-encrypted on success.
    /// Programmatic WPF (no XAML), matching SafetyWarningDialog's house style. Returns DialogResult=true
    /// when a password was saved (validated) OR an existing one was cleared; false on Cancel.
    /// </summary>
    internal sealed class SqlCredentialDialog : Window
    {
        private static readonly FontFamily SegoeUiFont = new FontFamily("Segoe UI");

        private readonly string _server;
        private readonly string _database;
        private readonly string _login;

        private PasswordBox _passwordBox = null!;
        private TextBlock _statusText = null!;
        private Button _saveBtn = null!;
        private Button? _clearBtn;

        private SolidColorBrush _mutedBrush = null!;
        private SolidColorBrush _fgBrush = null!;
        private readonly SolidColorBrush _errorBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xDC, 0x35, 0x45)));

        public SqlCredentialDialog(string server, string database, string login, bool hasExistingCredential)
        {
            _server = server ?? string.Empty;
            _database = database ?? string.Empty;
            _login = login ?? string.Empty;
            Build(hasExistingCredential);
            TryAttachOwnerToHost();
        }

        private void TryAttachOwnerToHost()
        {
            try
            {
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte?.MainWindow != null)
                    new WindowInteropHelper(this).Owner = (IntPtr)dte.MainWindow.HWnd;
            }
            catch { /* not critical */ }
        }

        private void Build(bool hasExistingCredential)
        {
            var registry = ThemeRegistry.Instance.Resources;
            _fgBrush = (SolidColorBrush)registry[ThemeTokens.TextPrimary];
            _mutedBrush = (SolidColorBrush)registry[ThemeTokens.TextPlaceholder];

            Title = "AKML SQL — SQL authentication";
            Width = 430;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ThemeRegistry.Instance.AttachTo(this);
            this.SetResourceReference(BackgroundProperty, ThemeTokens.SurfaceCanvas);
            this.SetResourceReference(ForegroundProperty, ThemeTokens.TextPrimary);
            FontFamily = SegoeUiFont;
            FontSize = 13;

            var root = new StackPanel { Margin = new Thickness(18) };

            root.Children.Add(new TextBlock
            {
                Text = "Enter the SQL Server password to enable IntelliSense for this connection. " +
                       "It is validated against the server, then stored encrypted (Windows DPAPI) for this user.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = _fgBrush,
                Margin = new Thickness(0, 0, 0, 14)
            });

            root.Children.Add(LabeledValue("Server", _server));
            root.Children.Add(LabeledValue("Database", _database));
            root.Children.Add(LabeledValue("Login", _login));

            root.Children.Add(new TextBlock
            {
                Text = "Password",
                Foreground = _mutedBrush,
                FontSize = 12,
                Margin = new Thickness(0, 10, 0, 4)
            });
            _passwordBox = new PasswordBox { Padding = new Thickness(8, 6, 8, 6), FontSize = 13 };
            _passwordBox.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);
            root.Children.Add(_passwordBox);

            _statusText = new TextBlock
            {
                Text = string.Empty,
                TextWrapping = TextWrapping.Wrap,
                Foreground = _mutedBrush,
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed
            };
            root.Children.Add(_statusText);

            root.Children.Add(BuildFooter(hasExistingCredential));

            Content = root;
            Loaded += (_, _) => _passwordBox.Focus();
        }

        private UIElement LabeledValue(string label, string value)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = label + ":", Width = 72, Foreground = _mutedBrush, FontSize = 12 });
            row.Children.Add(new TextBlock { Text = value, Foreground = _fgBrush, FontSize = 12, FontWeight = FontWeights.SemiBold });
            return row;
        }

        private DockPanel BuildFooter(bool hasExistingCredential)
        {
            var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };

            if (hasExistingCredential)
            {
                _clearBtn = new Button
                {
                    Content = "Clear saved password",
                    Height = 30,
                    Padding = new Thickness(10, 0, 10, 0),
                    FontSize = 12
                };
                DockPanel.SetDock(_clearBtn, Dock.Left);
                _clearBtn.Click += (_, _) =>
                {
                    SqlCredentialStore.Remove(_server, _login);
                    DialogResult = true;
                    Close();
                };
                footer.Children.Add(_clearBtn);
            }

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(btnPanel, Dock.Right);

            var cancelBtn = new Button { Content = "Cancel", Width = 80, Height = 30, Margin = new Thickness(8, 0, 0, 0), IsCancel = true, FontSize = 13 };
            cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };

            // Save is the default button (Enter submits) — safe: validation always precedes storage.
            _saveBtn = new Button { Content = "Save", MinWidth = 90, Height = 30, FontSize = 13, IsDefault = true };
            _saveBtn.Click += OnSaveClick;

            btnPanel.Children.Add(_saveBtn);
            btnPanel.Children.Add(cancelBtn);
            footer.Children.Add(btnPanel);
            return footer;
        }

        private async void OnSaveClick(object sender, RoutedEventArgs e)
        {
            var pwd = _passwordBox.Password;
            if (string.IsNullOrEmpty(pwd))
            {
                ShowStatus("Enter a password.", isError: true);
                return;
            }

            SetBusy(true);
            ShowStatus("Testing connection…", isError: false);
            try
            {
                var connStr = SsmsConnectionDetector.BuildSqlAuthConnectionString(_server, _database, _login, pwd);
                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    ShowStatus("AKML SQL engine is not running yet — try again in a moment.", isError: true);
                    SetBusy(false);
                    return;
                }

                var resp = await client.SendRequestAsync<TestSqlConnectionResponse, TestSqlConnectionRequest>(
                    MessageTypes.TestSqlConnection,
                    new TestSqlConnectionRequest { ConnectionString = connStr },
                    timeoutMs: 8000);

                if (resp != null && resp.Ok)
                {
                    SqlCredentialStore.Save(_server, _login, pwd);
                    DialogResult = true;
                    Close();
                    return;
                }

                ShowStatus(resp?.ErrorMessage ?? "Could not connect with these credentials.", isError: true);
            }
            catch (Exception ex)
            {
                ShowStatus("Validation failed: " + ex.Message, isError: true);
            }
            SetBusy(false);
        }

        private void SetBusy(bool busy)
        {
            _saveBtn.IsEnabled = !busy;
            if (_clearBtn != null) _clearBtn.IsEnabled = !busy;
            _passwordBox.IsEnabled = !busy;
        }

        private void ShowStatus(string text, bool isError)
        {
            _statusText.Text = text;
            _statusText.Foreground = isError ? _errorBrush : _mutedBrush;
            _statusText.Visibility = Visibility.Visible;
        }

        private static SolidColorBrush Freeze(SolidColorBrush b) { if (b.CanFreeze) b.Freeze(); return b; }
    }
}
```

- [ ] **Step 1b: Guard the empty-login case**

`SqlCredentialStore.Save`/`TryGet` early-return on an empty login, so if the caption had no `(login (spid))` parenthetical the dialog would silently no-op on Save and the affordance would reappear with no error. Guard it. (a) At the end of `Build(...)`, after `Content = root;`, add:
```csharp
            if (string.IsNullOrEmpty(_login))
            {
                _saveBtn.IsEnabled = false;
                ShowStatus("Couldn't read the SQL login from the window title — reconnect this window so the login appears in the title, then reopen this prompt.", isError: true);
            }
```
(b) As a belt-and-suspenders, add to the top of `OnSaveClick`, before the password check:
```csharp
            if (string.IsNullOrEmpty(_login))
            {
                ShowStatus("No SQL login available for this window.", isError: true);
                return;
            }
```

- [ ] **Step 2: Confirm the `SendRequestAsync` signature**

The margin (`SchemaProgressMargin.OnPollTick`) calls `client.SendRequestAsync<SchemaStatusResponse, SchemaStatusRequest>(MessageTypes.SchemaStatusRequest, req, timeoutMs: 3000)`. The dialog's call mirrors that generic order `<TResponse, TRequest>`. If the real signature differs, match the margin's exact usage.

- [ ] **Step 3: Build to verify compile**

Run: `dotnet build tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj -c Release`
Expected: build succeeds (compiles the dialog via the `.projitems`).

- [ ] **Step 4: Commit checkpoint** — ask first, then:
```bash
git add src/AkmlSql.Shell.Shared/Editor/SqlCredentialDialog.cs
git commit -m "feat(029): SqlCredentialDialog — validate-before-store SQL password prompt"
```

---

## Task 10: Margin — NeedsCredentials state, affordance, AuthError, click

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs`

- [ ] **Step 1: Add the config `using`**

After `using AkmlSql.Core.Ipc.Messages;` add:
```csharp
using AkmlSql.Core.Config;
```

- [ ] **Step 2: Add the `NeedsCredentials` state**

Change:
```csharp
        private enum MarginState { Hidden, Loading, Ready }
```
to:
```csharp
        private enum MarginState { Hidden, Loading, Ready, NeedsCredentials }
```

- [ ] **Step 3: Wire the click handler in the constructor**

After `ThemeRegistry.Instance.AttachTo(_notificationBorder);` (or anywhere after `_notificationBorder` is created), add:
```csharp
            _notificationBorder.MouseLeftButtonUp += OnNotificationClicked;
```

- [ ] **Step 4: Handle SQL-auth state at the top of the poll tick, and AuthError after the poll**

In `OnPollTick`, after the `if (string.IsNullOrEmpty(_sessionId)) { ... }` block and before fetching the client, insert:
```csharp
                // Spec 029: SQL-auth windows are driven by shell-local SqlAuthState, not the engine
                // (which has no session until we send ConnectionChanged). This takes priority.
                if (TryGetAuthState(out var authState) && authState.NeedsCredentials)
                {
                    if (ConnectionWiringHelper.TryResolveStoredSqlCredential(_sessionId, _textView))
                    {
                        // A credential is now stored (this window, or another window on the same
                        // server/login just saved it) — ConnectionChanged was sent; show Loading.
                        TransitionTo(MarginState.Loading);
                        _loadingStartedAtUtc = DateTime.UtcNow;
                    }
                    else
                    {
                        TransitionTo(MarginState.NeedsCredentials);
                    }
                    return;
                }
```

Then, after `var resp = await client.SendRequestAsync<...>(...);` and the `if (_disposed) return;`, before `Apply(resp);`, insert:
```csharp
                // Spec 029: the engine rejected a stored SQL credential (login/permission failure).
                // Only treat it as "re-enter credentials" for SQL-auth sessions (SqlAuthState present);
                // Windows-auth permission denials keep their existing behavior.
                if (resp != null && resp.AuthError && TryGetAuthState(out var rejected))
                {
                    SqlCredentialStore.Remove(rejected.Server, rejected.Login);
                    rejected.NeedsCredentials = true;
                    TransitionTo(MarginState.NeedsCredentials);
                    return;
                }
```

- [ ] **Step 5: Render the NeedsCredentials state in `TransitionTo`**

Add a case to the `switch (newState)` in `TransitionTo` (and reset the cursor in the other cases):
```csharp
                case MarginState.NeedsCredentials:
                    EnsureAdornmentAdded();
                    _notificationBorder.Visibility = Visibility.Visible;
                    _notificationBorder.Cursor = System.Windows.Input.Cursors.Hand;
                    _spinnerArc.Visibility = Visibility.Collapsed;
                    _loadingLabel.Visibility = Visibility.Collapsed;
                    _readyGlyph.Visibility = Visibility.Collapsed; // no glyph — emoji render unreliably in the adornment
                    _statusText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.EditorSpinnerStroke); // accent = actionable
                    SetText("SQL auth — click to enable IntelliSense");
                    FadeTo(1, null);
                    break;
```
In the `Hidden`, `Loading`, and `Ready` cases, set the cursor back to the default by adding this line to each:
```csharp
                    _notificationBorder.Cursor = System.Windows.Input.Cursors.Arrow;
```
And in the `Loading` and `Ready` cases, reset the status-text color (NeedsCredentials accents it) by adding:
```csharp
                    _statusText.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
```
(No glyph reset is needed — the NeedsCredentials state collapses the glyph instead of changing its text, so `_readyGlyph` keeps its constructor `✓`.)

- [ ] **Step 6: Add the click handler, `BeginEnterCredentials`, and `TryGetAuthState`**

Add to the class (e.g. in the "External triggers" region near `BeginRefresh`):
```csharp
        private void OnNotificationClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_disposed) return;
            if (_state != MarginState.NeedsCredentials) return;
            BeginEnterCredentials();
        }

        /// <summary>Spec 029. Opens the SQL credential dialog; on a successful save (or clear),
        /// re-resolves the connection so schema loads (or the affordance reappears).</summary>
        public void BeginEnterCredentials()
        {
            if (_disposed) return;
            if (!TryGetAuthState(out var state)) return;
            try
            {
                bool hasExisting = SqlCredentialStore.Has(state.Server, state.Login);
                var dlg = new SqlCredentialDialog(state.Server, state.Database, state.Login, hasExisting);
                var result = dlg.ShowDialog();
                if (result == true)
                {
                    if (ConnectionWiringHelper.TryResolveStoredSqlCredential(_sessionId, _textView))
                    {
                        TransitionTo(MarginState.Loading);
                        _loadingStartedAtUtc = DateTime.UtcNow;
                    }
                    else
                    {
                        // "Clear saved password" was used (no credential now) — keep the affordance.
                        state.NeedsCredentials = true;
                        TransitionTo(MarginState.NeedsCredentials);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BeginEnterCredentials failed");
            }
        }

        private bool TryGetAuthState(out SqlAuthState state)
        {
            state = null!;
            try
            {
                if (_textView.TextBuffer.Properties.TryGetProperty<SqlAuthState>("AkmlSqlAuthState", out var s) && s != null)
                {
                    state = s;
                    return true;
                }
            }
            catch { }
            return false;
        }
```

- [ ] **Step 7: Detach the click handler in `Dispose`**

In `Dispose()`, alongside the other `-=` lines, add:
```csharp
            try { _notificationBorder.MouseLeftButtonUp -= OnNotificationClicked; } catch { }
```

- [ ] **Step 8: Build to verify compile**

Run: `dotnet build tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj -c Release`
Expected: build succeeds.

- [ ] **Step 9: Commit checkpoint** — ask first, then:
```bash
git add src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs
git commit -m "feat(029): margin click-to-enter affordance + AuthError re-prompt + multi-window auto-resolve"
```

---

## Task 11: Config opt-out flag

**Files:**
- Modify: `src/AkmlSql.Core/Config/AppSettings.cs` (in `IntelliSenseSettings`, after `SnippetsInCompletion`, line ~320)

- [ ] **Step 1: Add the property**

After:
```csharp
        public bool SnippetsInCompletion { get; set; } = false;
```
add:
```csharp
        /// <summary>Spec 029. When true (default), AKML offers to store a SQL Server-auth password
        /// (DPAPI-encrypted, per server+login) so the out-of-process engine can load schema/IntelliSense
        /// for SQL-auth connections. Set false to disable the prompt and storage entirely.</summary>
        public bool EnableSqlAuthCredentials { get; set; } = true;
```

- [ ] **Step 2: Build Core**

Run: `dotnet build src/AkmlSql.Core/AkmlSql.Core.csproj -c Release`
Expected: build succeeds. (Serializes as `enableSqlAuthCredentials`.)

- [ ] **Step 3: Commit checkpoint** — ask first, then:
```bash
git add src/AkmlSql.Core/Config/AppSettings.cs
git commit -m "feat(029): add intelliSense.enableSqlAuthCredentials opt-out (default true)"
```

---

## Task 12: Explicit DPAPI package reference in the shells (hardening)

**Files:**
- Modify: `src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj`
- Modify: `src/AkmlSql.VS2026/AkmlSql.VS2026.csproj`

**Why:** `System.Security.Cryptography.ProtectedData` reaches the shell transitively via `AkmlSql.Core`. An explicit reference guarantees the assembly is deployed into the VSIX/extension folder (avoids a runtime assembly-not-found). Task 13 verifies the DLL actually lands in the extension folder; if it already does, this task is belt-and-suspenders.

- [ ] **Step 1: Add the PackageReference to each shell csproj**

In the main `<ItemGroup>` that holds `<PackageReference>` entries, add:
```xml
    <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="10.*" />
```
(Match the version `AkmlSql.Core.csproj` uses so there's no version conflict.)

- [ ] **Step 2: Commit checkpoint** — ask first, then:
```bash
git add src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj src/AkmlSql.VS2026/AkmlSql.VS2026.csproj
git commit -m "build(029): explicit ProtectedData PackageReference in shells (ensure DPAPI asset deploys)"
```

---

## Task 13: Build, deploy to SSMS 22, live-verify

This is the integration gate. It needs the user's machine, the live server (`192.168.5.123` / `NatGas_G2_Testing` / `sa`), and SSMS 22.

- [ ] **Step 1: Run all unit tests**
```bash
dotnet test tests/AkmlSql.Core.Tests
dotnet test tests/AkmlSql.Engine.Tests
dotnet test tests/AkmlSql.Shell.Shared.Tests
```
Expected: all green (including the new Task 1/2/5 tests).

- [ ] **Step 2: Publish the engine + build the SSMS shell**
```bash
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build    -p:Configuration=Release -v:minimal
```
Expected: clean build.

- [ ] **Step 3: Verify the DPAPI asset is in the shell output**

Confirm `System.Security.Cryptography.ProtectedData.dll` is present in `src/AkmlSql.Ssms22/bin/Release/net472/`. If absent, Task 12's explicit reference is required (re-check the build).

- [ ] **Step 4: Resolve the exact engine path FIRST, then deploy**

The engine **must** be redeployed — `TestSqlConnection` (type 93), `AuthError`, and the cache-reset all live in the engine. If a stale engine is left in place, `RpcRouter` has no handler for type 93 → returns null → the dialog's `SendRequestAsync` times out at 8s and shows a misleading "Validation failed". So resolve the path deterministically before copying:
- Open `src/AkmlSql.Shell.Shared/Ipc/EngineLifecycle.cs` (and/or `EngineProcessManager`) and read where it launches the engine exe from — that absolute path under the `AkmlSql\` extension folder is the deploy target. Record it.

Then, with SSMS 22 closed (DLLs are locked while running):
- Copy `AkmlSql.Ssms22.dll`, `AkmlSql.Core.dll`, and `System.Security.Cryptography.ProtectedData.dll` from `src/AkmlSql.Ssms22/bin/Release/net472/` to `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\` (back up the originals first).
- Copy the published engine output (`src/AkmlSql.Engine/bin/Release/net10.0/win-x64/publish/`) to **the exact engine path resolved above**.
- Delete the MEF cache: `%LocalAppData%\Microsoft\SSMS\22.0_*\ComponentModelCache`.
- Reopen SSMS 22.

- [ ] **Step 5: Live-verify the 5 scenarios**

1. **Originating bug:** open a query window on `192.168.5.123` / `NatGas_G2_Testing` as `sa`. The schema-progress toast shows **"SQL auth — click to enable IntelliSense"**. Click → dialog → enter the password → validation succeeds → **schema loads** (toast goes to Loading → Ready; IntelliSense works). Log shows `Sent ConnectionChanged: 192.168.5.123.NatGas_G2_Testing auth=SqlPassword`, no "session not found". **Engine-liveness sanity:** the dialog's validation succeeding *is* proof the new engine is live (it round-trips type 93). If validation hangs ~8s and fails, suspect a **stale/mis-placed engine** (Step 4) before debugging anything else.
2. **Wrong password:** click → enter a wrong password → inline error (e.g. "Login failed for user 'sa'."); nothing stored; dialog stays open.
3. **Persistence:** restart SSMS, reconnect → IntelliSense loads with no prompt.
4. **Multi-window:** open two windows on the same server/login (delete the stored credential first so both show the affordance) → enter the password in one → the other resolves within ~1s without a click.
5. **Stale credential:** with a credential stored, change the `sa` password on the server, then open/refresh a window → toast shows **"credentials rejected — click to re-enter"**; entering the new password reloads schema.

- [ ] **Step 6: Restore the `.bak` originals if anything regresses; otherwise keep the deployed build.**

---

## Task 14: No-secret-leak gate

- [ ] **Step 1: Enumerate and eyeball every connection-string / password use**

A single-line `grep "ConnectionString" | grep "Log\."` is **not** sufficient — Serilog calls in this codebase span lines (the message template on one line, `ConnectionDiagnostics.Describe(request.ConnectionString)` on the next), so a split `Log.Debug("…", conn.ConnectionString)` would slip through. Instead, enumerate **every** occurrence and inspect each (there are few):
```bash
grep -rIn -e "\.ConnectionString" -e "Password" src/AkmlSql.Engine src/AkmlSql.Shell.Shared
```
Expected: every hit is one of — `new SqlConnection(...)` / `new SqlConnectionStringBuilder(...)`, a `ConnectionDiagnostics.Describe(...)` argument, an assignment (`conn.ConnectionString = …`, `info.ConnectionString = …`), a `SqlConnectionStringBuilder` property (`b.Password = …`), or the message-class definition. **No hit may be a direct argument to a `Log.*` call.** Specifically re-read the message-template lines in `TestSqlConnectionHandler` and `ConnectionChangedHandler`: they must log only `Describe(...)` / `{Db}` / `sqlEx.Number` — never `request.ConnectionString` / `conn.ConnectionString` / `pwd`.

- [ ] **Step 2: Confirm the IPC layer logs type, not contents** — re-confirm `RpcRouter` has no payload logging and `NamedPipeTransport` logs only `message.MessageType` (no code change expected; this is the standing invariant).

---

## Task 15: Docs + final commit

**Files:**
- Modify: `doc/progress.md` (add a spec-029 entry)

- [ ] **Step 1: Add a progress-log entry** summarizing spec 029 (problem, approach, the 5 verified scenarios, the `enableSqlAuthCredentials` opt-out, deferred Options-page toggle).

- [ ] **Step 2: Final commit checkpoint** — ask first, then commit `doc/progress.md` (and any straggler files):
```bash
git add doc/progress.md
git commit -m "docs(029): progress log — SQL-auth credential support for IntelliSense"
```

---

## Self-review (run before execution)

- **Spec coverage:** §5.1 SqlCredentialStore → Task 1; §5.2 detector → Task 2; §5.6 TestSqlConnection → Tasks 3+5; §5.7 AuthError + terminal-cache reset → Tasks 4+6; §5.3 wiring + SqlAuthState → Tasks 7+8; §5.5 dialog → Task 9; §5.4 margin → Task 10; §5.8 config → Task 11; §6 DPAPI deploy → Task 12; §6 no-leak invariant → Task 14; testing §8 → Tasks 1/2/5 + Task 13 live. All covered.
- **Type consistency:** `SqlAuthState { Server, Database, Login, NeedsCredentials }` used identically in Tasks 7/8/10. `BuildSqlAuthConnectionString(server, database, login, password)` signature identical in Tasks 2/8/9. `TestSqlConnectionRequest.ConnectionString` / `TestSqlConnectionResponse.Ok/ErrorMessage` identical in Tasks 3/5/9. `MessageTypes.TestSqlConnection`=93 used in Tasks 5/9. `SchemaStatusResponse.AuthError` set in Task 4, read in Task 10.
- **No placeholders:** every code step has complete code; the only manual steps (Task 0 smoke test, Task 13 live verify) are inherently interactive and spelled out.
