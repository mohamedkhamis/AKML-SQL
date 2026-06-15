using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Refactoring;
using AkmlSql.Engine.Refactoring.Operations;
using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using AkmlSql.Web.Services;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Xunit;

namespace AkmlSql.Web.Tests.Refactoring;

/// <summary>
/// Spec 027 (M5 offline closure) T019 / FR-009 / SC-003. Proves the browser's lightweight
/// refactoring path produces output identical to the engine's for the same input — the
/// "offline parity" guarantee. Because spec 027 Phase 2 relocated the ten operations into
/// the shared <c>AkmlSql.IntelliSense</c> library, both surfaces execute the SAME
/// <c>ILightweightOperation.Apply</c> code; this test verifies the browser SERVICE wires
/// each <see cref="LightweightRefactorKind"/> to the correct operation and builds an
/// equivalent <c>RefactoringContext</c> (a mis-wire would diverge from the independent
/// reference below), plus a couple of concrete behavioural goldens and edge cases.
/// </summary>
public sealed class LightweightParityTests
{
    // Reuses the TestFormatterService already defined in RefactoringServiceTests.cs
    // (same AkmlSql.Web.Tests.Refactoring namespace). The lightweight path never calls the
    // formatter, but RefactoringService's ctor requires one.
    private static IRefactoringService Build()
        => new RefactoringService(new DisconnectedBridge(), new TestFormatterService());

    // A representative multi-statement script: an old-style comma join + semicolons, so
    // several ops transform it and the rest no-op — equality must hold either way.
    private const string Sql =
        "SELECT a.X, b.Y FROM T1 a, T2 b WHERE a.Id = b.Id;\nSELECT 1;";

    [Theory]
    [InlineData(LightweightRefactorKind.ExpandInsertColumns)]
    [InlineData(LightweightRefactorKind.ExpandUpdateColumns)]
    [InlineData(LightweightRefactorKind.ConvertOldStyleJoins)]
    [InlineData(LightweightRefactorKind.EncapsulateBeginEnd)]
    [InlineData(LightweightRefactorKind.RemoveSemicolons)]
    [InlineData(LightweightRefactorKind.ReplaceDeprecatedSyntax)]
    [InlineData(LightweightRefactorKind.ExpandExecParameters)]
    [InlineData(LightweightRefactorKind.ConvertSpExecutesql)]
    [InlineData(LightweightRefactorKind.AddGroupByColumns)]
    [InlineData(LightweightRefactorKind.Unformat)]
    public async Task BrowserApply_matches_direct_engine_operation(LightweightRefactorKind kind)
    {
        // Browser path (service dispatches kind -> op, builds context, runs Apply).
        var browser = await Build().ApplyLightweightAsync(kind, Sql);

        // Independent reference: construct the expected op + an equivalent context here in
        // the test. A wiring bug in the service's kind->op mapping diverges from this.
        var (referenceText, _) = ReferenceOp(kind).Apply(BuildContext(Sql));

        Assert.Equal(referenceText, browser);
    }

    [Fact]
    public async Task RemoveSemicolons_golden_strips_all_terminators()
    {
        var result = await Build().ApplyLightweightAsync(LightweightRefactorKind.RemoveSemicolons, Sql);
        Assert.DoesNotContain(';', result);
    }

    [Fact]
    public async Task ConvertOldStyleJoins_golden_emits_inner_join()
    {
        var result = await Build().ApplyLightweightAsync(LightweightRefactorKind.ConvertOldStyleJoins, Sql);
        Assert.Contains("INNER JOIN", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preview_reports_changed_false_for_a_noop_input()
    {
        // RemoveSemicolons on SQL with no semicolons is a no-op -> Changed == false,
        // Before == After (the defined "not applicable" state, FR-011).
        var preview = await Build().PreviewLightweightAsync(
            LightweightRefactorKind.RemoveSemicolons, "SELECT 1");
        Assert.False(preview.Changed);
        Assert.Equal(preview.Before, preview.After);
    }

    [Fact]
    public async Task Unparseable_sql_leaves_document_unchanged()
    {
        // Mirrors the engine: a parse failure yields the original text, not a throw (edge case).
        const string garbage = "SELEKT ?? FRM (((";
        var result = await Build().ApplyLightweightAsync(LightweightRefactorKind.ConvertOldStyleJoins, garbage);
        Assert.Equal(garbage, result);
    }

    // --- reference helpers (independent of the service's CreateOperation) ---

    private static ILightweightOperation ReferenceOp(LightweightRefactorKind kind) => kind switch
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
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static RefactoringContext BuildContext(string sql)
    {
        var parser = new TsqlParserService();
        return new RefactoringContext
        {
            DocumentText = sql,
            Script = parser.Parse(sql, out _) ?? new TSqlScript(),
            Tokens = parser.GetTokenStream(sql),
            IntelliSense = new IntelliSenseSettings(),
        };
    }

    // --- test double: the lightweight path never touches the bridge, but the ctor needs one ---

    private sealed class DisconnectedBridge : IEngineBridge
    {
        public BridgeState State => BridgeState.Disconnected;
        public event Action<BridgeState>? StateChanged { add { } remove { } }
        public event Action<DateTimeOffset?>? RetryScheduled { add { } remove { } }
        public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected { add { } remove { } }
        public string[] EngineCapabilities => Array.Empty<string>();
        public string? EngineVersion => null;
        public Task<HandshakeResponse> ConnectAsync(EngineConnection c, string? b, string? p, CancellationToken ct) => Task.FromResult(new HandshakeResponse());
        public Task<TResponse> SendAsync<TRequest, TResponse>(int t, TRequest r, CancellationToken ct) where TRequest : class where TResponse : class => throw new InvalidOperationException();
        public Task SendNotificationAsync<TPayload>(int t, TPayload p, CancellationToken ct) where TPayload : class => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }
}
