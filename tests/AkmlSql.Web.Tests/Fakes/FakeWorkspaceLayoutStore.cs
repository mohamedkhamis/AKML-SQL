using AkmlSql.Web.Services;

namespace AkmlSql.Web.Tests;

/// <summary>Spec 041 — an in-memory <see cref="IWorkspaceLayoutStore"/> for component tests; records each change.</summary>
internal sealed class FakeWorkspaceLayoutStore : IWorkspaceLayoutStore
{
    private readonly WorkspaceLayoutStore _inner = new(new InMemoryIndexedDbAdapter());

    public FakeWorkspaceLayoutStore(WorkspaceLayout? initial = null)
    {
        _inner.Changed += () => Changed?.Invoke();
        Initial = initial ?? WorkspaceLayout.Default;
    }

    private WorkspaceLayout Initial { get; }
    private bool _loaded;

    public List<(string Field, object? Value)> Sets { get; } = new();
    public int Resets { get; private set; }

    public WorkspaceLayout Current => _loaded ? _inner.Current : Initial;

    public event Action? Changed;

    public async Task<WorkspaceLayout> LoadAsync()
    {
        if (!_loaded)
        {
            _loaded = true;
            await Seed(Initial);
        }
        Changed?.Invoke();
        return _inner.Current;
    }

    public Task SetAsync<T>(string field, T value)
    {
        Sets.Add((field, value));
        _loaded = true;
        return _inner.SetAsync(field, value);
    }

    public Task ResetAsync()
    {
        Resets++;
        _loaded = true;
        return _inner.ResetAsync();
    }

    private async Task Seed(WorkspaceLayout layout)
    {
        if (layout == WorkspaceLayout.Default) return;
        await _inner.SetAsync(WorkspaceLayoutFields.NavVisible, layout.NavVisible);
        await _inner.SetAsync(WorkspaceLayoutFields.SchemaVisible, layout.SchemaVisible);
        await _inner.SetAsync(WorkspaceLayoutFields.SchemaWidth, layout.SchemaWidth);
        await _inner.SetAsync(WorkspaceLayoutFields.AiVisible, layout.AiVisible);
        await _inner.SetAsync(WorkspaceLayoutFields.AiWidth, layout.AiWidth);
        await _inner.SetAsync(WorkspaceLayoutFields.AiTab, layout.AiTab);
        await _inner.SetAsync(WorkspaceLayoutFields.ResultsVisible, layout.ResultsVisible);
        if (layout.ResultsHeight is int h) await _inner.SetAsync(WorkspaceLayoutFields.ResultsHeight, h);
        await _inner.SetAsync(WorkspaceLayoutFields.ResultsMaximised, layout.ResultsMaximised);
        await _inner.SetAsync(WorkspaceLayoutFields.ResultsTab, layout.ResultsTab);
    }
}
