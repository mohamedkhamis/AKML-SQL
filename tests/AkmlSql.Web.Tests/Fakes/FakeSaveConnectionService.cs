using AkmlSql.Web.Services;

namespace AkmlSql.Web.Tests;

/// <summary>
/// Spec 047 (WEB-002, WEB-004) — the connection service the Connect to database dialog's Save path talks to: it records
/// every login test, test-and-remember and forget, and answers each with a scripted result. Everything else keeps the
/// interface's defaults (no engine features beyond remembering SQL Server passwords).
/// </summary>
internal sealed class FakeSaveConnectionService : ISqlConnectionService
{
    public string SessionId => "save-tests";
    public bool IsConnected { get; set; }
    public string? Server { get; set; }
    public string? Database { get; set; }
    public event Action? StateChanged { add { } remove { } }

    public bool EngineRemembersSqlServerPasswords { get; set; } = true;
    public bool EngineSupportsPostgres { get; set; }
    public SqlSessionState? ActiveSession { get; set; }

    /// <summary>Every target passed to <see cref="TestAndRememberAsync"/>, in order.</summary>
    public List<ConnectionTarget> Remembers { get; } = new();

    /// <summary>The answer to the next <see cref="TestAndRememberAsync"/>.</summary>
    public ConnectResult RememberReply { get; set; } = new(true, Remembered: true);

    /// <summary>Holds the test open until the test releases it (for the double-click case).</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>Whether the engine already holds a password for the login (asked when a row is shown).</summary>
    public bool SqlRemembered { get; set; }

    public List<(string Server, string Login)> SqlForgets { get; } = new();

    public async Task<ConnectResult> TestAndRememberAsync(ConnectionTarget target, CancellationToken ct)
    {
        Remembers.Add(target);
        if (Gate != null) await Gate.Task;
        if (RememberReply.Ok && RememberReply.Remembered) SqlRemembered = true;
        return RememberReply;
    }

    public Task<bool> HasRememberedSqlServerPasswordAsync(string server, string login, bool trustServerCertificate, CancellationToken ct) =>
        Task.FromResult(SqlRemembered);

    public Task<bool?> ForgetRememberedSqlServerPasswordAsync(string server, string login, CancellationToken ct)
    {
        SqlForgets.Add((server, login));
        var had = SqlRemembered;
        SqlRemembered = false;
        return Task.FromResult<bool?>(had);
    }

    public Task<ConnectResult> TestAsync(ConnectionTarget target, CancellationToken ct) => Task.FromResult(new ConnectResult(true));

    public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(ConnectionTarget target, CancellationToken ct) =>
        Task.FromResult<(bool, IReadOnlyList<string>, string?)>((true, new[] { "master", "Northwind" }, null));

    public Task<(bool Ok, string? Error)> ConnectAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
        Task.FromResult<(bool, string?)>((true, null));

    public Task<(bool Ok, string? Error)> TestAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
        Task.FromResult<(bool, string?)>((true, null));

    public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(string server, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
        Task.FromResult<(bool, IReadOnlyList<string>, string?)>((true, new[] { "master" }, null));

    public Task DisconnectAsync() => Task.CompletedTask;
    public Task SendDocumentAsync(string documentText, CancellationToken ct) => Task.CompletedTask;
}
