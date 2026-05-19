using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M5 task T117. Lightweight refactorings (FormatSelection,
/// rename-in-selection) run locally via <c>AkmlSql.Formatting</c>. Heavyweight
/// refactorings (smart rename, schema-aware) require the engine's
/// <c>refactoring.heavy</c> capability and run via <see cref="IEngineBridge"/>.
/// </summary>
public interface IRefactoringService
{
    /// <summary>True when the engine's heavyweight refactorings are available.</summary>
    bool HeavyAvailable { get; }

    /// <summary>Preview a refactoring. Returns null when the kind is heavy and the bridge is unavailable.</summary>
    Task<RefactorPreviewResponse?> PreviewAsync(RefactorPreviewRequest request, CancellationToken ct);

    /// <summary>Apply a previously-previewed refactoring. Same gating.</summary>
    Task<RefactorApplyResponse?> ApplyAsync(RefactorApplyRequest request, CancellationToken ct);

    /// <summary>
    /// Light refactoring: format the supplied selection in place. Runs entirely in
    /// the browser via the existing IFormatterService, no engine round-trip.
    /// </summary>
    Task<string> FormatSelectionAsync(string sql, AkmlSql.Formatting.Profiles.FormattingProfile? profile = null);
}

internal sealed class RefactoringService : IRefactoringService
{
    private const string CapabilityHeavy = "refactoring.heavy";
    private readonly IEngineBridge _bridge;
    private readonly IFormatterService _formatter;

    public RefactoringService(IEngineBridge bridge, IFormatterService formatter)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    }

    public bool HeavyAvailable =>
        _bridge.State == BridgeState.Open &&
        Array.IndexOf(_bridge.EngineCapabilities, CapabilityHeavy) >= 0;

    public async Task<RefactorPreviewResponse?> PreviewAsync(RefactorPreviewRequest request, CancellationToken ct)
    {
        if (!HeavyAvailable) return null;
        try
        {
            return await _bridge.SendAsync<RefactorPreviewRequest, RefactorPreviewResponse>(
                MessageTypes.RequestRefactorPreview, request, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { return null; }
    }

    public async Task<RefactorApplyResponse?> ApplyAsync(RefactorApplyRequest request, CancellationToken ct)
    {
        if (!HeavyAvailable) return null;
        try
        {
            return await _bridge.SendAsync<RefactorApplyRequest, RefactorApplyResponse>(
                MessageTypes.RequestRefactorApply, request, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException) { return null; }
    }

    public Task<string> FormatSelectionAsync(string sql, AkmlSql.Formatting.Profiles.FormattingProfile? profile = null)
    {
        var result = _formatter.Format(sql ?? string.Empty, profile);
        return Task.FromResult(result.FormattedText ?? sql ?? string.Empty);
    }
}
