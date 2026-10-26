using AkmlSql.Web.Services;
using AkmlSql.Web.Services.Settings;
using AkmlSql.Web.Tests.Bridge;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SettingsPage = AkmlSql.Web.Pages.Settings;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Spec 041 — the Settings page rendered with everything it needs: the real stores of <see cref="SettingsStoresHost"/>
/// over one recording adapter, the real schema cache, diagnostics, connection and saved-connection stores, and
/// fakes for the engine bridge, the SQL connection, the AI client, dialogs and notifications.
/// </summary>
internal sealed class SettingsPageHost : IDisposable
{
    internal sealed class FakeSql : ISqlConnectionService
    {
        public string SessionId => "test-session";
        public bool IsConnected { get; set; }
        public string? Server { get; set; }
        public string? Database { get; set; }
        public event Action? StateChanged;
        public List<(string Server, string Database)> Connects { get; } = new();

        // Spec 042: a fake that sets none of these is a SQL Server connection on an engine without PostgreSQL support.
        public AkmlSql.Core.DatabaseKind Kind { get; set; }
        public bool EngineSupportsPostgres { get; set; }
        public SqlSessionState? ActiveSession { get; set; }

        /// <summary>Spec 043: the tab sessions a Disconnect or close was asked for, in order.</summary>
        public List<SqlSessionState> DisconnectedSessions { get; } = new();

        public Task<DisconnectResult> DisconnectSessionAsync(SqlSessionState session, bool release, CancellationToken ct)
        {
            DisconnectedSessions.Add(session);
            return Task.FromResult(new DisconnectResult(true, false, null));
        }
        public string? ServerVersionText { get; set; }

        /// <summary>Spec 042 (FR-106): every request to forget a remembered PostgreSQL password, in order.</summary>
        public List<(string Host, int? Port, string User)> Forgets { get; } = new();

        /// <summary>Runs inside the forget call, so a test can look at what else existed at that moment.</summary>
        public Func<Task>? OnForget { get; set; }

        /// <summary>The engine's answer to a forget: true (it had one), false (none), null (no answer — the password stays).</summary>
        public bool? ForgetAnswer { get; set; } = true;

        public async Task<bool?> ForgetRememberedPasswordAsync(string host, int? port, string user, CancellationToken ct)
        {
            Forgets.Add((host, port, user));
            if (OnForget != null) await OnForget();
            return ForgetAnswer;
        }

        // ── Spec 043 follow-up (review, round 2): a SQL Server login's password the engine remembers ──
        public bool EngineRemembersSqlServerPasswords { get; set; }

        /// <summary>Every request to forget a remembered SQL Server login's password, in order (answered with <see cref="ForgetAnswer"/>).</summary>
        public List<(string Server, string Login)> SqlForgets { get; } = new();

        public async Task<bool?> ForgetRememberedSqlServerPasswordAsync(string server, string login, CancellationToken ct)
        {
            SqlForgets.Add((server, login));
            if (OnForget != null) await OnForget();
            return ForgetAnswer;
        }

        public Task<(bool Ok, string? Error)> ConnectAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct)
        {
            Connects.Add((server, database));
            IsConnected = true; Server = server; Database = database;
            StateChanged?.Invoke();
            return Task.FromResult<(bool, string?)>((true, null));
        }

        public Task<(bool Ok, string? Error)> TestAsync(string server, string database, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
            Task.FromResult<(bool, string?)>((true, null));

        public Task<(bool Ok, IReadOnlyList<string> Databases, string? Error)> ListDatabasesAsync(string server, bool windowsAuth, string? user, string? password, CancellationToken ct) =>
            Task.FromResult<(bool, IReadOnlyList<string>, string?)>((true, Array.Empty<string>(), null));

        public Task SendDocumentAsync(string documentText, CancellationToken ct) => Task.CompletedTask;

        public Task DisconnectAsync()
        {
            IsConnected = false; Server = null; Database = null;
            StateChanged?.Invoke();
            return Task.CompletedTask;
        }
    }

    public BunitContext Ctx { get; } = new();
    public SettingsStoresHost Stores { get; } = new();
    public FakeDialogService Dialogs { get; } = new();
    public FakeNotificationService Notifications { get; } = new();
    public FakeEngineBridge Bridge { get; } = new();
    public FakeSql Sql { get; } = new();
    public SchemaCacheStore SchemaCache { get; }
    public DiagnosticsRingBuffer Diagnostics { get; }
    public ConnectionManagerController ConnManager { get; } = new();

    public SettingsPageHost()
    {
        Ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var db = Stores.Db;
        SchemaCache = new SchemaCacheStore(db);
        Diagnostics = new DiagnosticsRingBuffer(db);

        var s = Ctx.Services;
        s.AddSingleton<IIndexedDbAdapter>(db);
        s.AddSingleton<IThemeService>(Stores.Theme);
        s.AddSingleton<IEditorSettingsStore>(Stores.Editor);
        s.AddSingleton<IProfileStore>(Stores.Profiles);
        s.AddSingleton<IExecutionSettingsStore>(Stores.Queries);
        s.AddSingleton<IAnalysisSettingsStore>(Stores.Analysis);
        s.AddSingleton<IAiKeyVault>(Stores.Vault);
        s.AddSingleton<IAiPreference>(Stores.Preference);
        s.AddSingleton<IAiFeatureSettings>(Stores.Features);
        s.AddSingleton<IWorkspaceLayoutStore>(Stores.Layout);
        s.AddSingleton<IPairingTokenVault>(Stores.Pairing);
        s.AddSingleton<IConnectionStore>(Stores.Engines);
        s.AddSingleton<ISavedSqlConnectionStore>(Stores.SavedSql);
        s.AddSingleton<IEditorSessionStore>(new EditorSessionStore(db));
        s.AddSingleton<ISchemaCacheStore>(SchemaCache);
        s.AddSingleton<IDiagnosticsRingBuffer>(Diagnostics);
        s.AddSingleton<IEngineBridge>(Bridge);
        s.AddSingleton<ISqlConnectionService>(Sql);
        s.AddSingleton<IConnectionManagerController>(ConnManager);
        s.AddSingleton<IAiClientFactory>(new FakeAiClientFactory());
        s.AddSingleton<IDialogService>(Dialogs);
        s.AddSingleton<INotificationService>(Notifications);
        foreach (var section in Stores.Sections) s.AddSingleton(section);
        s.AddSingleton<ISettingsPortability>(Stores.Portability);
    }

    public NavigationManager Nav => Ctx.Services.GetRequiredService<NavigationManager>();

    /// <summary>Open the page at <paramref name="route"/> ("settings", "settings/ai", "diagnostics" …).</summary>
    public IRenderedComponent<SettingsPage> Open(string route)
    {
        Nav.NavigateTo("/" + route);
        var parts = route.Split('/');
        string? section = parts.Length > 1 && parts[0] == "settings" ? parts[1] : null;
        var cut = Ctx.Render<SettingsPage>(p => p.Add(x => x.Section, section));
        cut.WaitForState(() => cut.FindAll("[data-testid^=settings-section-]").Count > 0);
        return cut;
    }

    public void Dispose() => Ctx.Dispose();
}
