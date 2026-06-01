using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Refactoring;
using AkmlSql.Engine.Refactoring.Operations;
using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- M5 task T117 + spec 027 (M5 offline closure) T016.
/// Lightweight refactorings (the ten parser-only operations relocated into
/// <c>AkmlSql.IntelliSense</c> by spec 027 Phase 2) run entirely in the browser via
/// <see cref="PreviewLightweightAsync"/> / <see cref="ApplyLightweightAsync"/> — no
/// engine round-trip. Heavyweight refactorings (smart rename, schema-aware) require the
/// engine's <c>refactoring.heavy</c> capability and run via <see cref="IEngineBridge"/>.
/// </summary>
public interface IRefactoringService
{
    /// <summary>True when the engine's heavyweight refactorings are available (bridge open + capability).</summary>
    bool HeavyAvailable { get; }

    /// <summary>Preview a heavyweight refactoring. Returns null when the bridge is unavailable.</summary>
    Task<RefactorPreviewResponse?> PreviewAsync(RefactorPreviewRequest request, CancellationToken ct);

    /// <summary>Apply a previously-previewed heavyweight refactoring. Same gating.</summary>
    Task<RefactorApplyResponse?> ApplyAsync(RefactorApplyRequest request, CancellationToken ct);

    /// <summary>
    /// Light refactoring: format the supplied selection in place. Runs entirely in
    /// the browser via the existing IFormatterService, no engine round-trip.
    /// </summary>
    Task<string> FormatSelectionAsync(string sql, AkmlSql.Formatting.Profiles.FormattingProfile? profile = null);

    /// <summary>
    /// Spec 027 T016: preview one of the ten lightweight refactoring operations against
    /// <paramref name="sql"/>, entirely in the browser (parser + text rewrite, no bridge).
    /// The result's <see cref="LightweightPreview.Changed"/> is false when the operation
    /// is a no-op / not applicable to the input.
    /// </summary>
    Task<LightweightPreview> PreviewLightweightAsync(
        LightweightRefactorKind kind, string sql, int selectionStart = 0, int selectionLength = 0);

    /// <summary>
    /// Spec 027 T016: apply one of the ten lightweight refactoring operations and return
    /// the transformed text. Runs in the browser; output is identical to the engine's for
    /// the same input because both surfaces execute the same operation code.
    /// </summary>
    Task<string> ApplyLightweightAsync(
        LightweightRefactorKind kind, string sql, int selectionStart = 0, int selectionLength = 0);
}

/// <summary>
/// The ten lightweight (parser-only, no-schema) refactoring operations the browser runs
/// offline. Distinct from <c>FormatActionType</c> (a wire enum where these ops occupy a
/// non-contiguous range — e.g. RemoveSemicolons is FormatActionType=2 while the others are
/// 9–17), so this web-internal enum maps cleanly to the ten <c>ILightweightOperation</c>
/// implementations. NOT a wire type — no IPC message carries it.
/// </summary>
public enum LightweightRefactorKind
{
    ExpandInsertColumns,
    ExpandUpdateColumns,
    ConvertOldStyleJoins,
    EncapsulateBeginEnd,
    RemoveSemicolons,
    ReplaceDeprecatedSyntax,
    ExpandExecParameters,
    ConvertSpExecutesql,
    AddGroupByColumns,
    Unformat,
}

/// <summary>Spec 027 T016/E5: the before/after of a lightweight refactoring, shown before commit.</summary>
public sealed record LightweightPreview(string Before, string After, string[] Warnings, bool Changed);

internal sealed class RefactoringService : IRefactoringService
{
    private const string CapabilityHeavy = "refactoring.heavy";
    private readonly IEngineBridge _bridge;
    private readonly IFormatterService _formatter;

    // Reused across preview/apply (and across an all-ops menu preview) so the ScriptDom
    // parser is built once for the service lifetime rather than per call. RefactoringService
    // is a DI singleton and the WASM editor path is single-threaded, so sharing is safe.
    private readonly TsqlParserService _parser = new();

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

    public Task<LightweightPreview> PreviewLightweightAsync(
        LightweightRefactorKind kind, string sql, int selectionStart = 0, int selectionLength = 0)
    {
        var text = sql ?? string.Empty;
        var (modified, warnings) = RunLightweight(kind, text, selectionStart, selectionLength);
        var changed = !string.Equals(modified, text, StringComparison.Ordinal);
        return Task.FromResult(new LightweightPreview(text, modified, warnings, changed));
    }

    public Task<string> ApplyLightweightAsync(
        LightweightRefactorKind kind, string sql, int selectionStart = 0, int selectionLength = 0)
    {
        var (modified, _) = RunLightweight(kind, sql ?? string.Empty, selectionStart, selectionLength);
        return Task.FromResult(modified);
    }

    /// <summary>
    /// Build a <see cref="RefactoringContext"/> in-browser and run the operation. The
    /// browser ALWAYS supplies <c>IntelliSense</c> so the two ops that would otherwise call
    /// <c>ConfigManager.Load()</c> (ExpandInsertColumns / ExpandExecParameters) never touch
    /// disk under WASM (the relocation's WASM-safety invariant — spec 027 Decision 2).
    /// </summary>
    private (string modified, string[] warnings) RunLightweight(
        LightweightRefactorKind kind, string text, int selectionStart, int selectionLength)
    {
        var ctx = new RefactoringContext
        {
            DocumentText = text,
            Script = _parser.Parse(text, out _) ?? new TSqlScript(),
            Tokens = _parser.GetTokenStream(text),
            SelectionStart = selectionStart,
            SelectionLength = selectionLength,
            IntelliSense = new IntelliSenseSettings(),
        };
        var (modified, warnings) = CreateOperation(kind).Apply(ctx);
        return (modified, warnings ?? Array.Empty<string>());
    }

    private static ILightweightOperation CreateOperation(LightweightRefactorKind kind) => kind switch
    {
        LightweightRefactorKind.ExpandInsertColumns => new ExpandInsertColumnsOperation(),
        LightweightRefactorKind.ExpandUpdateColumns => new ExpandUpdateColumnsOperation(),
        LightweightRefactorKind.ConvertOldStyleJoins => new ConvertOldStyleJoinsOperation(),
        LightweightRefactorKind.EncapsulateBeginEnd => new EncapsulateBeginEndOperation(),
        LightweightRefactorKind.RemoveSemicolons => new RemoveSemicolonsOperation(),
        LightweightRefactorKind.ReplaceDeprecatedSyntax => new ReplaceDeprecatedSyntaxOperation(),
        LightweightRefactorKind.ExpandExecParameters => new ExpandExecParametersOperation(),
        LightweightRefactorKind.ConvertSpExecutesql => new ConvertSpExecutesqlOperation(),
        LightweightRefactorKind.AddGroupByColumns => new AddGroupByColumnsOperation(),
        LightweightRefactorKind.Unformat => new UnformatOperation(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown lightweight refactoring kind."),
    };
}
