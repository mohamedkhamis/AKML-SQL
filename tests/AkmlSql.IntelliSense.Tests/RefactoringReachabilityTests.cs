using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Refactoring;
using AkmlSql.Engine.Refactoring.Operations;
using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.IntelliSense.Tests;

/// <summary>
/// Spec 027 (M5 offline closure) T007. Proves the lightweight refactoring operations +
/// <see cref="RefactoringContext"/> relocated into AkmlSql.IntelliSense are reachable,
/// instantiable, and runnable from a project that ONLY references AkmlSql.IntelliSense
/// (no AkmlSql.Engine dependency). This is the shared-lib boundary the browser path
/// (US2) depends on — if these types could only be reached transitively through the
/// engine, the WASM web edition could not run them offline.
///
/// Mirrors the T102 <see cref="ExtractionSmokeTests"/> pattern. Behavioural parity with
/// the engine is covered by the existing engine refactoring suite (which still passes
/// against the relocated types via the engine's transitive reference) and by the
/// web-side LightweightParityTests (T019).
/// </summary>
public sealed class RefactoringReachabilityTests
{
    [Fact]
    public void RefactoringContext_constructs_without_engine_assembly()
    {
        // RefactoringContext carries AkmlSql.Engine.Schema.DatabaseCache (also relocated)
        // and AkmlSql.Core.Config types — all reachable without AkmlSql.Engine.
        var ctx = new RefactoringContext { DocumentText = "SELECT 1;" };
        Assert.NotNull(ctx);
        Assert.Equal("SELECT 1;", ctx.DocumentText);
        Assert.False(ctx.HasSelection);
    }

    [Fact]
    public void All_ten_lightweight_operations_are_reachable_and_implement_the_interface()
    {
        // Constructing each from a non-engine project proves the type-forwarding worked.
        ILightweightOperation[] ops =
        {
            new ExpandInsertColumnsOperation(),
            new ExpandUpdateColumnsOperation(),
            new ConvertOldStyleJoinsOperation(),
            new EncapsulateBeginEndOperation(),
            new RemoveSemicolonsOperation(),
            new ReplaceDeprecatedSyntaxOperation(),
            new ExpandExecParametersOperation(),
            new ConvertSpExecutesqlOperation(),
            new AddGroupByColumnsOperation(),
            new UnformatOperation(),
        };

        Assert.Equal(10, ops.Length);
        Assert.All(ops, op => Assert.IsAssignableFrom<ILightweightOperation>(op));
    }

    [Fact]
    public void Lightweight_operation_runs_end_to_end_from_shared_library()
    {
        // RemoveSemicolons needs only DocumentText + Tokens (no schema) — the simplest
        // op to exercise the full Apply path: parse → build context → transform.
        var parser = new TsqlParserService();
        const string sql = "SELECT 1;\nSELECT 2;";
        var ctx = new RefactoringContext
        {
            DocumentText = sql,
            Script = parser.Parse(sql, out _)!,
            Tokens = parser.GetTokenStream(sql),
        };

        var (modified, warnings) = new RemoveSemicolonsOperation().Apply(ctx);

        Assert.NotNull(modified);
        Assert.NotNull(warnings);
        // The op removed at least one semicolon terminator (behavioural sanity, not full parity).
        Assert.DoesNotContain(';', modified);
    }
}
