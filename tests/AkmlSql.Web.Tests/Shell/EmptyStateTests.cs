using System.Text.RegularExpressions;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Grid;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 041 (T205, research R66, FR-088) — a pane with nothing to show says so with the shared <c>.akml-empty</c> state
/// and tells the user what to do next; the start-up and crash screens are styled by tokens, not inline styles.
/// </summary>
public sealed class EmptyStateTests : BunitContext
{
    public EmptyStateTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IQueryExecutionService>(new RecordingQueryExecution());
        Services.AddSingleton<IWorkspaceStatus>(new WorkspaceStatus(() => DateTimeOffset.UtcNow));
        Services.AddSingleton<IDialogService>(new FakeDialogService());
        Services.AddSingleton<INotificationService>(new FakeNotificationService());
    }

    private static void IsEmptyState(AngleSharp.Dom.IElement e, string nextStep)
    {
        Assert.Contains("akml-empty", e.ClassList);
        Assert.Contains(nextStep, e.TextContent);
    }

    [Fact]
    public void Before_a_run_Results_and_Messages_say_how_to_fill_them()
    {
        var cut = Render<ResultsPaneComponent>(p => p.Add(x => x.Result, (ExecuteQueryResult?)null));

        IsEmptyState(cut.Find("[data-testid=results-empty]"), "F5");
        IsEmptyState(cut.Find("[data-testid=messages-empty]"), "F5");
    }

    [Theory]
    [InlineData(false, "Run Analyse (Ctrl+K, Ctrl+L)")]
    [InlineData(true, "No problems found. Run Analyse (Ctrl+K, Ctrl+L)")]
    public void Problems_says_whether_the_document_was_analysed_and_how_to_analyse_it(bool analysed, string text)
    {
        var cut = Render<ProblemsListComponent>(p => p
            .Add(x => x.Findings, analysed ? new CodeAnalysisResponse { Issues = Array.Empty<CodeIssueInfo>() } : null));

        IsEmptyState(cut.Find("[data-testid=problems-empty]"), text);
        if (!analysed) Assert.DoesNotContain("No problems found", cut.Markup);   // never a claim about an unanalysed document
    }

    [Fact]
    public void Problems_hidden_by_the_filters_says_so()
    {
        var cut = Render<ProblemsListComponent>(p => p
            .Add(x => x.Findings, new CodeAnalysisResponse { Issues = new[] { new CodeIssueInfo { RuleId = "ST001", Severity = 1, Line = 1, Column = 1, Message = "m" } } })
            .Add(x => x.DefaultFilter, new ProblemsFilter { ShowInfo = false }));

        IsEmptyState(cut.Find("[data-testid=problems-empty]"), "filters");
    }

    // ── the start-up and crash screens (wwwroot/index.html + css/app.css) ──────────────────────────────

    private static string Web(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { IconSpriteCoverageTests.RepoRoot(), "src", "AkmlSql.Web", "wwwroot" }.Concat(path).ToArray()));

    [Fact]
    public void The_start_up_screen_is_token_styled_with_no_inline_style()
    {
        var html = Web("index.html");
        var css = Web("css", "app.css");

        Assert.DoesNotMatch(new Regex(@"\sstyle\s*=", RegexOptions.IgnoreCase), html);
        Assert.Matches(new Regex(@"<div id=""app"">\s*<div class=""akml-boot"" role=""status"""), html);
        Assert.Contains("class=\"akml-boot-spinner\"", html);
        Assert.Matches(new Regex(@"<div id=""blazor-error-ui"" class=""akml-crash"" role=""alert"">"), html);
        Assert.Contains("class=\"reload\"", html);

        Assert.Contains(".akml-boot {", css);
        Assert.Contains("#blazor-error-ui {", css);
        Assert.Matches(new Regex(@"#blazor-error-ui \{[^}]*display:\s*none", RegexOptions.Singleline), css);
        Assert.Matches(new Regex(@"@keyframes akml-boot-spin"), css);
        Assert.Matches(new Regex(@"prefers-reduced-motion:\s*reduce\)\s*\{[^}]*\.akml-boot-spinner\s*\{\s*animation:\s*none", RegexOptions.Singleline), css);
    }
}
