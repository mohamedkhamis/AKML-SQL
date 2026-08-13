using System;
using System.IO;
using System.Threading.Tasks;
using AkmlSql.Core.Models.History;
using AkmlSql.Engine.History;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// F3 fix (final review wave): <c>HistoryFilter.NameFilter</c> means "match against the SESSION
/// name" in <see cref="HistoryDatabase.SearchAsync"/> but, before this fix, meant "match against the
/// raw <c>h.tab_title</c> column" in the export path — one wire field with two meanings. Since a row
/// belonging to a session can have a NULL <c>tab_title</c> (an unsaved scratch tab — see F1) or a
/// stale pre-rename <c>tab_title</c> (see F2), the export path must resolve the name the same way
/// SearchAsync does.
/// </summary>
public class HistoryExportNameFilterTests : IAsyncLifetime
{
    private string _path = string.Empty;
    private HistoryDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), $"akml-exportnf-{Guid.NewGuid():N}.db");
        _db = new HistoryDatabase(_path);
        await _db.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        TryDelete(_path);
        TryDelete(_path + "-wal");
        TryDelete(_path + "-shm");
        return Task.CompletedTask;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Export_NameFilter_matches_the_session_name_not_the_null_tab_title()
    {
        // tab_title is NULL — the shape an unsaved scratch tab records under (F1). The row's only
        // name is its auto-assigned session name, "query-01".
        await _db.InsertEntryAsync("SELECT 1", false, "localhost", "Northwind", null, 5, 1,
            (int)ExecutionStatus.Success, null, source: null, tabTitle: null, sessionKey: "tab-A");

        var outputPath = Path.Combine(Path.GetTempPath(), $"akml-export-{Guid.NewGuid():N}.json");
        try
        {
            await _db.ExportAsync(
                new HistoryFilter { NameFilter = "query-01" }, ExportFormat.Json, outputPath);

            var json = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("SELECT 1", json);
        }
        finally { TryDelete(outputPath); }
    }

    [Fact]
    public async Task Export_NameFilter_does_not_match_an_unrelated_session_name()
    {
        await _db.InsertEntryAsync("SELECT 1", false, "localhost", "Northwind", null, 5, 1,
            (int)ExecutionStatus.Success, null, source: null, tabTitle: null, sessionKey: "tab-A");

        var outputPath = Path.Combine(Path.GetTempPath(), $"akml-export-{Guid.NewGuid():N}.json");
        try
        {
            await _db.ExportAsync(
                new HistoryFilter { NameFilter = "query-99" }, ExportFormat.Json, outputPath);

            var json = await File.ReadAllTextAsync(outputPath);
            Assert.Equal("[]", json.Trim());
        }
        finally { TryDelete(outputPath); }
    }
}
