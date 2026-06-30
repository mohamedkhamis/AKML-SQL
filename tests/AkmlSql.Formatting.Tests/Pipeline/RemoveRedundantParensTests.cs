using System;
using System.IO;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Pipeline;

/// <summary>
/// Spec 030 T011 — parenthesis.removeRedundant is FORCE-DISABLED. The pass peels exactly one
/// paren layer per format (only the innermost pair of "((a))" wraps a single token), so repeated
/// formats produce different output: ((a)) → (a) → a — verified empirically before disabling.
/// All built-in styles ship removeRedundant:false; these pin that a user-enabled option stays
/// inert until the pass is rebuilt with a fixpoint + semantic guards. Before the disable, the
/// option broke BOTH pipeline paths: with Stage-6 validation on, the peeled output failed the
/// semantic compare (the normalised ASTs keep paren structure) and Format returned the ORIGINAL —
/// enabling the option silently disabled formatting; on the validation-skipping path the peel
/// emitted non-idempotent output.
/// </summary>
public class RemoveRedundantParensTests
{
    private const string Nested = "select  ((a))  from t where ((a > 1));";

    [Fact]
    public void RemoveRedundant_Enabled_DoesNotBreakFormatting()
    {
        var profile = LoadDefaultStyle();
        profile.Parenthesis.RemoveRedundant = true;
        var result = new FormatterPipeline().Format(Nested, profile);

        Assert.True(result.ValidationPassed, result.FormattedText);
        Assert.Contains("SELECT", result.FormattedText);   // formatting actually ran (casing applied)
        Assert.Contains("((a))", result.FormattedText);    // parens untouched
    }

    [Fact]
    public void RemoveRedundant_Enabled_IsInert_OnValidationSkippingPath()
    {
        var profile = LoadDefaultStyle();
        profile.Parenthesis.RemoveRedundant = true;
        profile.Metadata.SkipValidation = true;
        profile.Metadata.EnableIdempotencyCheck = false;
        var result = new FormatterPipeline().Format(Nested, profile);

        Assert.Contains("((a))", result.FormattedText);
    }

    [Fact]
    public void RemoveRedundant_Enabled_IsIdempotent_OnValidationSkippingPath()
    {
        var profile = LoadDefaultStyle();
        profile.Parenthesis.RemoveRedundant = true;
        profile.Metadata.SkipValidation = true;
        profile.Metadata.EnableIdempotencyCheck = false;
        var once = new FormatterPipeline().Format(Nested, profile);
        var twice = new FormatterPipeline().Format(once.FormattedText, profile);
        Assert.Equal(once.FormattedText, twice.FormattedText);
    }

    private static FormattingProfile LoadDefaultStyle()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
            dir = dir.Parent;
        if (dir == null) throw new DirectoryNotFoundException("AKML-SQL.slnx not found");
        var stylePath = Path.Combine(dir.FullName, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn", "default.akmlstyle");
        return ProfileSerializer.Deserialize(File.ReadAllText(stylePath));
    }
}
