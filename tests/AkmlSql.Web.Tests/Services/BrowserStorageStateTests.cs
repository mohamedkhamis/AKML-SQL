using AkmlSql.Web.Services;
using Microsoft.JSInterop;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 047 (WEB-003, research R6, contracts/connections-and-storage.md §4): the web edition asks the browser once to keep
/// its data (navigator.storage.persist), the first time a connection or an engine is saved, and reports the answer.
/// </summary>
public sealed class BrowserStorageStateTests
{
    [Fact]
    public async Task Persistence_is_requested_once_per_run_and_the_answer_is_kept()
    {
        var js = new StorageJs { PersistAnswer = true };
        var state = new BrowserStorageState(js);

        Assert.True(await state.RequestPersistenceAsync());
        Assert.True(await state.RequestPersistenceAsync());
        Assert.Equal(1, js.PersistCalls);
    }

    [Fact]
    public async Task Ask_again_bypasses_the_kept_answer()
    {
        var js = new StorageJs { PersistAnswer = false };
        var state = new BrowserStorageState(js);

        Assert.False(await state.RequestPersistenceAsync());
        js.PersistAnswer = true;
        Assert.True(await state.RequestPersistenceAsync(force: true));
        Assert.Equal(2, js.PersistCalls);
    }

    [Fact]
    public async Task A_browser_without_the_API_answers_null_and_a_failing_call_too()
    {
        Assert.Null(await new BrowserStorageState(new StorageJs { PersistAnswer = null }).RequestPersistenceAsync());
        Assert.Null(await new BrowserStorageState(new StorageJs { Throws = true }).RequestPersistenceAsync());
        Assert.Null(await new BrowserStorageState(new StorageJs { PersistedAnswer = null }).IsPersistedAsync());
    }

    [Fact]
    public async Task Saving_a_connection_or_an_engine_asks_for_persistence()
    {
        var storage = new CountingStorage();
        var adapter = new InMemoryIndexedDbAdapter();
        await new SavedSqlConnectionStore(adapter, storage).AddAsync(new SavedSqlConnection { Name = "Local", Server = "localhost", Database = "master", WindowsAuth = true });
        await new ConnectionStore(adapter, storage).AddAsync(new EngineConnection { Name = "This computer", Host = "localhost", Port = 59417 });

        Assert.Equal(2, storage.Requests);
    }

    [Fact]
    public async Task A_failing_persistence_request_never_fails_the_save()
    {
        var adapter = new InMemoryIndexedDbAdapter();
        var store = new SavedSqlConnectionStore(adapter, new CountingStorage { Throws = true });

        await store.AddAsync(new SavedSqlConnection { Name = "Local", Server = "localhost", Database = "master", WindowsAuth = true });

        Assert.Single(await store.ListAsync());
    }

    private sealed class CountingStorage : IBrowserStorageState
    {
        public int Requests { get; private set; }
        public bool Throws { get; init; }
        public Task<bool?> RequestPersistenceAsync(bool force = false)
        {
            Requests++;
            return Throws ? Task.FromException<bool?>(new JSException("denied")) : Task.FromResult<bool?>(true);
        }
        public Task<bool?> IsPersistedAsync() => Task.FromResult<bool?>(true);
    }

    private sealed class StorageJs : IJSRuntime, IJSObjectReference
    {
        public bool? PersistAnswer { get; set; } = true;
        public bool? PersistedAnswer { get; set; } = true;
        public bool Throws { get; init; }
        public int PersistCalls { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import")
            {
                Assert.Equal("./js/akml-indexeddb.js", Assert.Single(args!));
                return ValueTask.FromResult((TValue)(object)this);
            }
            if (Throws) throw new JSException("storage API failed");
            switch (identifier)
            {
                case "persist":
                    PersistCalls++;
                    return ValueTask.FromResult((TValue)(object?)PersistAnswer!);
                case "persisted":
                    return ValueTask.FromResult((TValue)(object?)PersistedAnswer!);
                default:
                    throw new InvalidOperationException("Unexpected JS call " + identifier);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
