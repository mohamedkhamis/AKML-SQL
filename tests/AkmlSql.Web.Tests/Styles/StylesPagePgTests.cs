using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.Web.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using StylesPage = AkmlSql.Web.Pages.Styles;

namespace AkmlSql.Web.Tests.Styles;

/// <summary>
/// Spec 042 (task T146, FR-145). While a PostgreSQL connection is active the Format Styles page marks the options that have no PostgreSQL
/// meaning ("No effect for PostgreSQL") and previews a PostgreSQL sample, formatted by the engine; with a SQL Server connection, or none,
/// the page is exactly what it was.
/// </summary>
public sealed class StylesPagePgTests : BunitContext
{
    private sealed class Gate : IDialectGate
    {
        public Gate(DatabaseKind kind) => Kind = kind;
        public DatabaseKind Kind { get; }
        public bool IsPostgres => Kind == DatabaseKind.PostgreSql;
    }

    private sealed class SpyEngine : IPostgresFormatEngine
    {
        public readonly List<string> Previewed = new();

        public Task<FormatResult> FormatAsync(string sql, FormattingProfile profile, CancellationToken ct) =>
            Task.FromResult(new FormatResult { Success = true, FormattedText = sql, ValidationPassed = true });

        public Task<FormatResult> PreviewAsync(string sample, FormattingProfile profile, CancellationToken ct)
        {
            lock (Previewed) Previewed.Add(sample);
            return Task.FromResult(new FormatResult { Success = true, FormattedText = "ENGINE PREVIEW", ValidationPassed = true });
        }
    }

    private readonly SpyEngine _engine = new();

    private IRenderedComponent<StylesPage> Open(DatabaseKind kind)
    {
        var store = new ProfileStore(new InMemoryIndexedDbAdapter());
        store.SaveDocumentAsync(null, SqlPromptStyleDocument.CreateDefault("SQL Prompt defaults")).GetAwaiter().GetResult();
        var gate = new Gate(kind);
        Services.AddSingleton<IProfileStore>(store);
        Services.AddSingleton<IDialectGate>(gate);
        Services.AddSingleton<IFormatterService>(new FormatterService(gate, _engine));
        Services.AddSingleton<IDialogService>(new FakeDialogService());
        JSInterop.Mode = JSRuntimeMode.Loose;

        var page = Render<StylesPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=style-row]")));
        page.FindAll("[data-testid=style-row]").First().Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=option-row]")));
        return page;
    }

    private static void OpenPage(IRenderedComponent<StylesPage> page, string sectionId)
    {
        page.Find($"[data-testid=page-{sectionId}]").Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=option-row]")));
    }

    [Fact]
    public void On_a_PostgreSQL_connection_the_options_with_no_meaning_are_marked_and_the_others_are_not()
    {
        var page = Open(DatabaseKind.PostgreSql);
        OpenPage(page, "controlFlow");

        var rows = page.FindAll("[data-testid=option-row]");
        Assert.NotEmpty(rows);
        Assert.All(rows, row =>
        {
            Assert.Equal("postgresql", row.GetAttribute("data-no-effect"));
            Assert.Equal(SqlPromptApplicability.NoEffectLabel, row.QuerySelector("[data-testid=option-no-effect]")!.TextContent.Trim());
        });

        OpenPage(page, "lists");
        var comma = page.FindAll("[data-testid=option-row]").First(r => r.GetAttribute("data-option") == "lists.placeCommasBeforeItems");
        Assert.Null(comma.GetAttribute("data-no-effect"));
        Assert.Empty(comma.QuerySelectorAll("[data-testid=option-no-effect]"));
        var aligned = page.FindAll("[data-testid=option-row]").First(r => r.GetAttribute("data-option") == "lists.alignAliases");
        Assert.Equal("postgresql", aligned.GetAttribute("data-no-effect"));
    }

    [Fact]
    public void The_mark_carries_the_reason_as_its_tooltip()
    {
        var page = Open(DatabaseKind.PostgreSql);
        OpenPage(page, "casing");

        var row = page.FindAll("[data-testid=option-row]").First(r => r.GetAttribute("data-option") == "casing.globalVariables");

        Assert.Equal(SqlPromptApplicability.ReasonNoEffect("casing.globalVariables"), row.QuerySelector("[data-testid=option-no-effect]")!.GetAttribute("title"));
    }

    [Fact]
    public void On_a_SQL_Server_connection_nothing_is_marked()
    {
        var page = Open(DatabaseKind.SqlServer);
        OpenPage(page, "controlFlow");

        Assert.Empty(page.FindAll("[data-testid=option-no-effect]"));
        Assert.All(page.FindAll("[data-testid=option-row]"), row => Assert.Null(row.GetAttribute("data-no-effect")));
    }

    [Fact]
    public void On_a_PostgreSQL_connection_the_preview_is_the_PostgreSQL_sample_formatted_by_the_engine()
    {
        var page = Open(DatabaseKind.PostgreSql);
        OpenPage(page, "joinStatements");

        page.WaitForAssertion(() => Assert.Equal("ENGINE PREVIEW", page.Find("[data-testid=preview]").TextContent.Trim()));
        lock (_engine.Previewed) Assert.Contains(SqlPromptPreviewSamples.ForPostgreSql("joinStatements"), _engine.Previewed);
    }

    [Fact]
    public void On_a_SQL_Server_connection_the_preview_is_formatted_in_the_browser()
    {
        var page = Open(DatabaseKind.SqlServer);
        OpenPage(page, "joinStatements");

        page.WaitForAssertion(() => Assert.Contains("JOIN", page.Find("[data-testid=preview]").TextContent, System.StringComparison.OrdinalIgnoreCase));
        lock (_engine.Previewed) Assert.Empty(_engine.Previewed);
    }
}
