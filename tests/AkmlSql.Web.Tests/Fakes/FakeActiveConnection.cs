using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Web.Services;

namespace AkmlSql.Web.Tests;

/// <summary>An <see cref="ISqlConnectionService"/> whose active target a test sets directly; it never talks to an engine.</summary>
internal sealed class FakeActiveConnection : ISqlConnectionService
{
    public string SessionId => "s";
    public bool IsConnected { get; set; } = true;
    public string? Server { get; set; } = "srv";
    public string? Database { get; set; } = "Sales";
    public DatabaseKind Kind { get; set; } = DatabaseKind.SqlServer;
    public string? SchemaIdentity { get; set; }
    public event Action? StateChanged { add { } remove { } }

    public Task<(bool Ok, string? Error)> ConnectAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) => throw new NotSupportedException();
    public Task<(bool Ok, string? Error)> TestAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) => throw new NotSupportedException();
    public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(string server, bool windowsAuth, string? user, string? password, CancellationToken ct) => throw new NotSupportedException();
    public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
    public Task SendDocumentAsync(string documentText, CancellationToken ct) => Task.CompletedTask;
}
