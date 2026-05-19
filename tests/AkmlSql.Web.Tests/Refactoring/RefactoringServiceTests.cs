using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Refactoring;

/// <summary>
/// Spec 021 (web edition) -- M5 task T118. RefactoringService:
/// lightweight (FormatSelection) runs locally always; heavyweight gated on
/// refactoring.heavy capability.
/// </summary>
public sealed class RefactoringServiceTests
{
    private static IRefactoringService Build(IEngineBridge? bridge = null)
    {
        bridge ??= new EngineBridge(
            () => new FakeBridgeWebSocket(_ => null!),
            new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));
        return new RefactoringService(bridge, new TestFormatterService());
    }

    [Fact]
    public async Task FormatSelectionAsync_runs_locally_without_a_bridge()
    {
        var service = Build();
        var formatted = await service.FormatSelectionAsync("select 1");
        Assert.False(string.IsNullOrEmpty(formatted));
        Assert.Contains("SELECT", formatted);
    }

    [Fact]
    public void HeavyAvailable_is_false_on_a_closed_bridge()
    {
        var service = Build();
        Assert.False(service.HeavyAvailable);
    }

    [Fact]
    public async Task PreviewAsync_returns_null_when_heavy_is_unavailable()
    {
        var service = Build();
        var preview = await service.PreviewAsync(new RefactorPreviewRequest(), CancellationToken.None);
        Assert.Null(preview);
    }

    [Fact]
    public async Task ApplyAsync_returns_null_when_heavy_is_unavailable()
    {
        var service = Build();
        var apply = await service.ApplyAsync(new RefactorApplyRequest(), CancellationToken.None);
        Assert.Null(apply);
    }
}

/// <summary>
/// Test-only IFormatterService that wraps the real FormatterPipeline. Named
/// distinctly to avoid colliding with the production internal sealed class
/// <c>AkmlSql.Web.Services.FormatterService</c>.
/// </summary>
internal sealed class TestFormatterService : IFormatterService
{
    private readonly AkmlSql.Formatting.Pipeline.FormatterPipeline _pipeline = new();

    public AkmlSql.Formatting.Pipeline.FormatResult Format(
        string sql, AkmlSql.Formatting.Profiles.FormattingProfile? profile = null)
    {
        return _pipeline.Format(sql ?? string.Empty, profile ?? new AkmlSql.Formatting.Profiles.FormattingProfile());
    }
}
