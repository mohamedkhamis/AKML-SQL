using AkmlSql.Web.Services;
using AkmlSql.Web.Services.Settings;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Spec 041 — every settings store, real, over one recording adapter, and the exportable sections over them:
/// a test can change a store, reset or export a section, and assert exactly which records were touched.
/// </summary>
internal sealed class SettingsStoresHost
{
    private sealed class NoopApplier : IThemeApplier
    {
        public int Forgets { get; private set; }
        public Task ApplyAsync(ThemeMode mode) => Task.CompletedTask;
        public Task ForgetAsync() { Forgets++; return Task.CompletedTask; }
    }

    public RecordingIndexedDbAdapter Db { get; } = new();
    public InMemoryWebCryptoWrapper Crypto { get; } = new();

    public ThemeService Theme { get; }
    public EditorSettingsStore Editor { get; }
    public ProfileStore Profiles { get; }
    public ExecutionSettingsStore Queries { get; }
    public AnalysisSettingsStore Analysis { get; }
    public AiKeyVault Vault { get; }
    public AiPreference Preference { get; }
    public AiFeatureSettingsStore Features { get; }
    public WorkspaceLayoutStore Layout { get; }

    // Stores that hold secrets or data, never preferences: no section may touch them.
    public PairingTokenVault Pairing { get; }
    public ConnectionStore Engines { get; }
    public SavedSqlConnectionStore SavedSql { get; }

    public IReadOnlyList<ISettingsSection> Sections { get; }
    public SettingsPortabilityService Portability { get; }

    public SettingsStoresHost()
    {
        Theme = new ThemeService(Db, new NoopApplier());
        Editor = new EditorSettingsStore(Db);
        Profiles = new ProfileStore(Db);
        Queries = new ExecutionSettingsStore(Db);
        Analysis = new AnalysisSettingsStore(Db);
        Vault = new AiKeyVault(Db, Crypto);
        Preference = new AiPreference(Db);
        Features = new AiFeatureSettingsStore(Db);
        Layout = new WorkspaceLayoutStore(Db);
        Pairing = new PairingTokenVault(Db, Crypto);
        Engines = new ConnectionStore(Db);
        SavedSql = new SavedSqlConnectionStore(Db);

        Sections = new ISettingsSection[]
        {
            new GeneralSettingsSection(Theme),
            new EditorSettingsSection(Editor),
            new FormatSettingsSection(Profiles),
            new QueriesSettingsSection(Queries),
            new CodeAnalysisSettingsSection(Analysis),
            new AiAssistanceSettingsSection(Vault, Preference, Features),
            new LayoutSettingsSection(Layout),
        };
        Portability = new SettingsPortabilityService(Sections);
    }

    public ISettingsSection Section(string id) => Sections.Single(s => s.Id == id);

    /// <summary>Records in the stores no section may touch, as (store, key) → value, for before/after checks.</summary>
    public static readonly string[] ProtectedStores =
    {
        StoreNames.Connections, StoreNames.PairingTokens, StoreNames.KeyMaterial, StoreNames.SavedSqlConnections,
        StoreNames.SchemaEntries, StoreNames.Snippets, StoreNames.ChatHistory, StoreNames.EditorSession, StoreNames.Diagnostics,
    };

    public async Task<Dictionary<(string, string), string>> SnapshotProtectedAsync()
    {
        var snap = new Dictionary<(string, string), string>();
        foreach (var store in ProtectedStores)
            foreach (var (k, v) in await Db.ListAsync(store))
                snap[(store, k)] = v;
        return snap;
    }

    /// <summary>Some of everything: preferences changed from their defaults, three AI providers (two with a key),
    /// and secrets and data in the protected stores.</summary>
    public async Task SeedEverythingAsync()
    {
        await Theme.InitializeAsync();
        await Theme.SetAsync(ThemeMode.Dark);
        await Editor.SetAsync(new EditorSettings { FontSize = 18, WordWrap = true, TabSize = 2 });
        await Profiles.SetActiveIdAsync("builtin.ansi");
        await Queries.SetAsync(new ExecutionSettings { MaxRows = 500, CommandTimeoutSeconds = 90, ShowColumnTypes = true });
        var analysis = new WebAnalysisSettings { Enabled = false, AutoAnalyseOnFormat = false, ProblemsFilter = new ProblemsFilter { ShowInfo = false } };
        analysis.RuleOverrides["PE001"] = "off";
        analysis.RuleOverrides["ST002"] = "error";
        await Analysis.SetAsync(analysis);
        await Vault.SetKeyAsync("openai", new AiProviderConfig { DisplayName = "OpenAI", Model = "gpt-5" }, OpenAiKey);
        await Vault.SetKeyAsync("anthropic", new AiProviderConfig { DisplayName = "Claude", Model = "claude-opus-5-5" }, AnthropicKey);
        await Vault.SetKeyAsync("ollama", new AiProviderConfig { DisplayName = "Ollama", Model = "llama3", Endpoint = "http://localhost:11434" }, string.Empty);
        await Preference.SetActiveAsync("anthropic");
        await Features.SetAsync(new AiFeatureSettings { GlobalDefaultMode = AiPrivacyMode.SchemaNamesOnly, GhostTextEnabled = true });
        await Layout.SetAsync(WorkspaceLayoutFields.SchemaVisible, false);
        await Layout.SetAsync(WorkspaceLayoutFields.AiWidth, 420);

        await Pairing.StoreAsync("engine-1", PairingToken, DateTimeOffset.UtcNow.AddDays(30));
        await Engines.AddAsync(new EngineConnection { Id = "engine-1", Name = "Office engine", Host = "10.0.0.5", IsLocalhost = false, BearerTokenWrappedRef = "engine-1" });
        await SavedSql.AddAsync(new SavedSqlConnection { Id = "sql-1", Name = "Prod", Server = "prod-sql", Database = "Sales", WindowsAuth = false, Login = "report_user" });
        Db.Seed(StoreNames.SchemaEntries, "prod-sql|Sales", "{\"tables\":[]}");
        Db.Seed(StoreNames.Snippets, "ssf", "{\"body\":\"SELECT * FROM\"}");
        Db.Seed(StoreNames.ChatHistory, "current", "{\"turns\":[\"hello\"]}");
        Db.Seed(StoreNames.EditorSession, "current", "{\"DocumentText\":\"SELECT 1\"}");
        Db.ResetLog();
    }

    public const string OpenAiKey = "sk-openai-SECRET-7f3a";
    public const string AnthropicKey = "sk-ant-SECRET-91bc";
    public const string PairingToken = "pairing-SECRET-token-55";
}
