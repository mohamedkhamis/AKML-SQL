using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Diagnostics;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 021 (web edition) -- T077 EngineLogTail. Reads the engine's daily-rolling
/// log file and returns the last N KB to the browser for diagnostics-export
/// inclusion. The handler is filesystem-only -- no SQL connection needed.
/// </summary>
public sealed class EngineLogTailHandlerTests : IDisposable
{
    private readonly string _tempDir;

    public EngineLogTailHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"akml_logtail_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    private EngineLogTailHandler BuildHandler() => new(() => _tempDir);

    [Fact]
    public async Task Returns_empty_text_when_log_directory_is_empty()
    {
        var handler = BuildHandler();
        var response = await handler.HandleAsync(new EngineLogTailRequest(), null!, CancellationToken.None);

        Assert.False(response.Truncated);
        Assert.Contains("No akmlsql-", response.LogText);
    }

    [Fact]
    public async Task Returns_full_text_when_log_is_within_MaxBytes()
    {
        const string content = "INFO line one\nINFO line two\nINFO line three\n";
        File.WriteAllText(Path.Combine(_tempDir, "akmlsql-20260516.log"), content);

        var handler = BuildHandler();
        var response = await handler.HandleAsync(new EngineLogTailRequest { MaxBytes = 1024 }, null!, CancellationToken.None);

        Assert.False(response.Truncated);
        Assert.Equal(content, response.LogText);
        Assert.EndsWith("akmlsql-20260516.log", response.SourcePath);
    }

    [Fact]
    public async Task Truncates_to_MaxBytes_when_log_is_larger()
    {
        // Write a 50 KB log file, ask for 8 KB tail. Each line is 64 chars + \n = 65 bytes
        // => ~123 lines fit in 8 KB. The tail must end with the LAST line.
        var sb = new StringBuilder();
        for (int i = 0; i < 800; i++)
        {
            sb.Append('L').Append(i.ToString("D5")).Append(' ').Append(new string('x', 56)).Append('\n');
        }
        var path = Path.Combine(_tempDir, "akmlsql-20260516.log");
        File.WriteAllText(path, sb.ToString());

        var handler = BuildHandler();
        var response = await handler.HandleAsync(
            new EngineLogTailRequest { MaxBytes = 8 * 1024 }, null!, CancellationToken.None);

        Assert.True(response.Truncated);
        Assert.True(response.LogText.Length <= 8 * 1024);
        // The tail MUST end with the last line written.
        Assert.EndsWith($"L00799 {new string('x', 56)}\n", response.LogText);
        // The tail MUST NOT start mid-line -- the handler drops the partial first line.
        Assert.Matches(@"^L\d{5} ", response.LogText);
    }

    [Fact]
    public async Task Picks_the_most_recent_log_file_when_multiple_exist()
    {
        var older = Path.Combine(_tempDir, "akmlsql-20260101.log");
        var newer = Path.Combine(_tempDir, "akmlsql-20260516.log");
        File.WriteAllText(older, "old content");
        File.WriteAllText(newer, "new content");
        // Force the newer file's last-write timestamp later.
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(-1));

        var handler = BuildHandler();
        var response = await handler.HandleAsync(new EngineLogTailRequest(), null!, CancellationToken.None);

        Assert.Equal("new content", response.LogText);
        Assert.EndsWith("akmlsql-20260516.log", response.SourcePath);
    }

    [Fact]
    public async Task Returns_diagnostic_text_when_directory_is_missing()
    {
        var handler = new EngineLogTailHandler(() => Path.Combine(_tempDir, "no-such-dir"));
        var response = await handler.HandleAsync(new EngineLogTailRequest(), null!, CancellationToken.None);

        Assert.Contains("does not exist", response.LogText);
    }

    [Fact]
    public async Task MaxBytes_is_clamped_to_a_floor_so_a_zero_request_does_not_misbehave()
    {
        File.WriteAllText(Path.Combine(_tempDir, "akmlsql-20260516.log"), "anything");
        var handler = BuildHandler();

        // MaxBytes = 0 should be clamped to the 1 KB floor inside the handler.
        var response = await handler.HandleAsync(
            new EngineLogTailRequest { MaxBytes = 0 }, null!, CancellationToken.None);

        Assert.False(response.Truncated);
        Assert.Equal("anything", response.LogText);
    }
}
