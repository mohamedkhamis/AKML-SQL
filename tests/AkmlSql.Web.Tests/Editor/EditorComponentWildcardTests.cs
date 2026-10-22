using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// Spec 043 follow-up (review) — what Tab on a qualified star asks the engine for, and the span it replaces. A bracketed or
/// double-quoted qualifier ('[o].*', '"o".*', with ']]' / '""' for a quote inside it) is sent as the name it quotes, and the
/// replacement starts at its opening bracket or quote; the walk back used to stop inside it, so the engine was asked for 'o]'
/// (nothing expanded) and a found one would have left the '[' behind. The columns carry the qualifier as it was written.
/// The names the engine sends are written as the dialect needs: SQL Server's are bracketed where they need it, PostgreSQL's
/// come already quoted and are pasted as they are.
/// </summary>
public sealed class EditorComponentWildcardTests : IDisposable
{
    /// <summary>Records every request and answers it with two columns of one table, under the qualifier it was asked for —
    /// or with <see cref="Tables"/> when a test sets them.</summary>
    private sealed class RecordingWildcard : IWildcardExpansionService
    {
        public List<WildcardExpansionRequest> Requests { get; } = new();

        public WildcardTableGroup[]? Tables { get; set; }

        public Task<WildcardExpansionResponse?> GetAsync(WildcardExpansionRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult<WildcardExpansionResponse?>(new WildcardExpansionResponse
            {
                Success = true,
                Tables = Tables ?? new[]
                {
                    new WildcardTableGroup
                    {
                        TableName = "dbo.Orders",
                        Qualifier = request.Qualifier ?? "Orders",
                        Columns = new[]
                        {
                            new WildcardColumn { ColumnName = "OrderID", TypeDisplay = "int" },
                            new WildcardColumn { ColumnName = "CustomerID", TypeDisplay = "nchar(5)" },
                        },
                    },
                },
            });
        }
    }

    private sealed class NoCompletion : ICompletionService
    {
        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct, string? documentText = null) =>
            Task.FromResult(new CompletionResponse());
    }

    private sealed class NoGhostText : IAiGhostTextService
    {
        public Task<string?> CompleteAsync(string precedingText, CancellationToken ct) => Task.FromResult<string?>(null);
        public int SessionRequestCount => 0;
    }

    private sealed class DefaultAiFeatures : IAiFeatureSettings
    {
        public Task<AiFeatureSettings> GetAsync() => Task.FromResult(new AiFeatureSettings());
        public Task SetAsync(AiFeatureSettings settings) => Task.CompletedTask;
        public Task<AiPrivacyMode> ResolveModeAsync(string featureId) => Task.FromResult(default(AiPrivacyMode));
        public Task ResetAsync() => Task.CompletedTask;
        public Task ReloadAsync() => Task.CompletedTask;
    }

    private sealed class DefaultEditorSettings : IEditorSettingsStore
    {
        public Task<EditorSettings> GetAsync() => Task.FromResult(new EditorSettings());
        public Task SetAsync(EditorSettings settings) => Task.CompletedTask;
        public Task ResetAsync() => Task.CompletedTask;
        public Task ReloadAsync() => Task.CompletedTask;
        public event Action? Changed { add { } remove { } }
    }

    private sealed class NoQuickInfo : IQuickInfoService
    {
        public Task<QuickInfoResponse> GetAsync(QuickInfoRequest request, CancellationToken ct) => Task.FromResult(new QuickInfoResponse());
    }

    private sealed class NoSignature : ISignatureHelpService
    {
        public Task<SignatureResponse> GetAsync(SignatureRequest request, CancellationToken ct) => Task.FromResult(new SignatureResponse());
    }

    private sealed class NoDefinition : IGotoDefinitionService
    {
        public Task<GetObjectDefinitionResponse?> GetAsync(GetObjectDefinitionRequest request, CancellationToken ct) =>
            Task.FromResult<GetObjectDefinitionResponse?>(null);
    }

    private readonly BunitContext _ctx = new();
    private readonly RecordingWildcard _wildcard = new();
    private readonly FakeActiveConnection _connection = new();

    public EditorComponentWildcardTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.JSInterop.SetupModule("./js/akml-editor.js");
        // Connected, with no query tab sessions: the editor reads the facade (session "s").
        _ctx.Services.AddSingleton<ISqlConnectionService>(_connection);
        _ctx.Services.AddSingleton<ICompletionService>(new NoCompletion());
        _ctx.Services.AddSingleton<IAiGhostTextService>(new NoGhostText());
        _ctx.Services.AddSingleton<IAiFeatureSettings>(new DefaultAiFeatures());
        _ctx.Services.AddSingleton<IEditorSettingsStore>(new DefaultEditorSettings());
        _ctx.Services.AddSingleton<IQuickInfoService>(new NoQuickInfo());
        _ctx.Services.AddSingleton<ISignatureHelpService>(new NoSignature());
        _ctx.Services.AddSingleton<IGotoDefinitionService>(new NoDefinition());
        _ctx.Services.AddSingleton<IWildcardExpansionService>(_wildcard);
    }

    public void Dispose() => _ctx.Dispose();

    private async Task<EditorComponent.WildcardExpandDto?> ExpandAsync(string doc, int caret)
    {
        var cut = _ctx.Render<EditorComponent>();
        return await cut.InvokeAsync(() => cut.Instance.ExpandWildcardFromJs(caret, doc));
    }

    [Theory]
    [InlineData("SELECT [o].* FROM dbo.Orders AS [o]", 12, "o", 7, 5)]
    [InlineData("SELECT \"o\".* FROM orders o", 12, "o", 7, 5)]
    [InlineData("SELECT o.* FROM dbo.Orders AS o", 10, "o", 7, 3)]
    [InlineData("SELECT [o]]x].* FROM dbo.Orders AS [o]]x]", 15, "o]x", 7, 8)]
    [InlineData("SELECT \"a\"\"b\".* FROM orders AS \"a\"\"b\"", 15, "a\"b", 7, 8)]
    [InlineData("SELECT [Order Lines].* FROM dbo.Orders AS [Order Lines]", 22, "Order Lines", 7, 15)]
    public async Task A_qualified_star_sends_the_name_it_quotes_and_is_replaced_from_the_qualifiers_start(
        string doc, int caret, string qualifier, int spanStart, int spanLength)
    {
        var result = await ExpandAsync(doc, caret);

        var request = Assert.Single(_wildcard.Requests);
        Assert.Equal(qualifier, request.Qualifier);
        Assert.Equal(caret, request.CursorOffset);
        Assert.Equal(doc, request.DocumentText);
        Assert.Equal("s", request.SessionId);

        Assert.NotNull(result);
        Assert.True(result!.Ok);
        Assert.Equal(spanStart, result.SpanStart);
        Assert.Equal(spanLength, result.SpanLength);
        Assert.Equal('*', doc[result.SpanStart + result.SpanLength - 1]);
        // The columns carry the qualifier as it was written, wrapped lines aligned under its start.
        var written = doc.Substring(spanStart, spanLength - 2);
        Assert.Equal(written + ".OrderID,\n" + new string(' ', spanStart) + written + ".CustomerID", result.Text);
    }

    [Fact]
    public async Task The_caret_before_the_star_replaces_the_same_span()
    {
        const string doc = "SELECT [o].* FROM dbo.Orders AS [o]";

        var result = await ExpandAsync(doc, 11);

        Assert.Equal("o", Assert.Single(_wildcard.Requests).Qualifier);
        Assert.NotNull(result);
        Assert.Equal(7, result!.SpanStart);
        Assert.Equal(5, result.SpanLength);
    }

    [Fact]
    public async Task A_bare_star_sends_no_qualifier_and_replaces_the_star_alone()
    {
        const string doc = "SELECT * FROM dbo.Orders";

        var result = await ExpandAsync(doc, 8);

        Assert.Null(Assert.Single(_wildcard.Requests).Qualifier);
        Assert.NotNull(result);
        Assert.Equal(7, result!.SpanStart);
        Assert.Equal(1, result.SpanLength);
        Assert.Equal("OrderID,\n       CustomerID", result.Text);
    }

    [Theory]
    [InlineData("SELECT [].* FROM dbo.Orders", 11)]
    [InlineData("SELECT \"\".* FROM dbo.Orders", 11)]
    [InlineData("SELECT o].* FROM dbo.Orders", 11)]
    [InlineData("SELECT .* FROM dbo.Orders", 9)]
    public async Task A_dot_with_no_name_before_it_is_not_sent_to_the_engine(string doc, int caret)
    {
        var result = await ExpandAsync(doc, caret);

        Assert.Null(result);
        Assert.Empty(_wildcard.Requests);
    }

    [Theory]
    [InlineData("x[o].", "o", 1)]
    [InlineData("[a]]].", "a]", 0)]
    [InlineData("[]]x].", "]x", 0)]
    [InlineData("\"\"\"x\".", "\"x", 0)]
    [InlineData("[a].[b].", "b", 4)]
    [InlineData("\"a\" \"b\".", "b", 4)]
    [InlineData("t1.", "t1", 0)]
    [InlineData("@t.", "@t", 0)]
    public void TryReadQualifier_reads_the_name_written_before_the_dot(string text, string name, int start)
    {
        Assert.True(EditorComponent.TryReadQualifier(text, text.Length - 1, out var read, out var from));
        Assert.Equal(name, read);
        Assert.Equal(start, from);
    }

    // ── Spec 043 follow-up (review): the expansion is written as the dialect needs ──────────────────────────────────────────
    // SQL Server's engine sends its names as they are, so 'Order Details' / 'Order Date' came out as
    // 'Order Details.Order Date', which does not parse. PostgreSQL's expander sends them already written.

    private static WildcardTableGroup Group(string qualifier, params string[] columns) => new()
    {
        TableName = qualifier,
        Qualifier = qualifier,
        Columns = columns.Select(c => new WildcardColumn { ColumnName = c, TypeDisplay = "int" }).ToArray(),
    };

    [Fact]
    public async Task SQL_Server_a_bare_star_over_a_join_brackets_the_qualifiers_and_columns_that_need_it()
    {
        _wildcard.Tables = new[]
        {
            Group("Order Details", "OrderID", "Unit Price"),
            Group("o", "Order", "a]b", "Übersicht", "1st"),
        };
        const string doc = "SELECT * FROM dbo.[Order Details] JOIN dbo.Orders AS o ON o.OrderID = [Order Details].OrderID";

        var result = await ExpandAsync(doc, 8);

        Assert.NotNull(result);
        Assert.Equal(7, result!.SpanStart);
        Assert.Equal(1, result.SpanLength);
        Assert.Equal(string.Join(",\n       ",
            "[Order Details].OrderID", "[Order Details].[Unit Price]", "o.[Order]", "o.[a]]b]", "o.Übersicht", "o.[1st]"), result.Text);
    }

    [Fact]
    public async Task SQL_Server_a_bare_star_over_one_table_brackets_the_columns_that_need_it()
    {
        _wildcard.Tables = new[] { Group("Orders", "OrderID", "Order Date", "select") };
        const string doc = "SELECT * FROM dbo.Orders";

        var result = await ExpandAsync(doc, 8);

        Assert.NotNull(result);
        Assert.Equal("OrderID,\n       [Order Date],\n       [select]", result!.Text);
    }

    [Theory]
    [InlineData("SELECT [od].* FROM dbo.[Order Details] AS od", 13, "[od]")]
    [InlineData("SELECT od.* FROM dbo.[Order Details] AS od", 11, "od")]
    [InlineData("SELECT [Order Details].* FROM dbo.[Order Details]", 24, "[Order Details]")]
    public async Task SQL_Server_a_written_qualifier_is_kept_as_written_and_the_columns_are_bracketed(string doc, int caret, string written)
    {
        // The engine names the group by the name the qualifier stands for, unquoted.
        _wildcard.Tables = new[] { Group(written.Trim('[', ']'), "OrderID", "Unit Price") };

        var result = await ExpandAsync(doc, caret);

        Assert.NotNull(result);
        Assert.Equal(written + ".OrderID,\n" + new string(' ', 7) + written + ".[Unit Price]", result!.Text);
    }

    [Fact]
    public async Task PostgreSQL_the_names_come_written_and_are_pasted_as_they_are()
    {
        _connection.Kind = AkmlSql.Core.DatabaseKind.PostgreSql;
        _wildcard.Tables = new[]
        {
            Group("\"Order Lines\"", "order_id", "\"Order Date\"", "\"a\"\"b\""),
            Group("o", "\"select\"", "total"),
        };
        const string doc = "SELECT * FROM \"Order Lines\" JOIN orders AS o USING (order_id)";

        var result = await ExpandAsync(doc, 8);

        Assert.NotNull(result);
        Assert.Equal(string.Join(",\n       ",
            "\"Order Lines\".order_id", "\"Order Lines\".\"Order Date\"", "\"Order Lines\".\"a\"\"b\"", "o.\"select\"", "o.total"), result!.Text);
    }

    [Fact]
    public async Task PostgreSQL_a_written_qualifier_is_kept_as_written()
    {
        _connection.Kind = AkmlSql.Core.DatabaseKind.PostgreSql;
        _wildcard.Tables = new[] { Group("\"C\"", "customer_id", "\"Company Name\"") };
        const string doc = "SELECT \"C\".* FROM customers AS \"C\"";

        var result = await ExpandAsync(doc, 12);

        Assert.Equal("C", Assert.Single(_wildcard.Requests).Qualifier);
        Assert.NotNull(result);
        Assert.Equal("\"C\".customer_id,\n       \"C\".\"Company Name\"", result!.Text);
    }

    [Theory]
    [InlineData("Orders", "Orders")]
    [InlineData("Order Details", "[Order Details]")]
    [InlineData("Order", "[Order]")]
    [InlineData("a]b", "[a]]b]")]
    [InlineData("1st", "[1st]")]
    [InlineData("Order-Date", "[Order-Date]")]
    [InlineData("Übersicht", "Übersicht")]
    [InlineData("_x$1", "_x$1")]
    [InlineData("#temp", "#temp")]
    public void WrittenName_brackets_a_SQL_Server_name_only_when_it_needs_it(string name, string written)
    {
        Assert.Equal(written, EditorComponent.WrittenName(name, AkmlSql.Core.DatabaseKind.SqlServer));
    }

    [Theory]
    [InlineData("order_id")]
    [InlineData("\"Order Date\"")]
    [InlineData("\"select\"")]
    [InlineData("\"a\"\"b\"")]
    public void WrittenName_leaves_a_PostgreSQL_name_as_the_engine_wrote_it(string name)
    {
        Assert.Equal(name, EditorComponent.WrittenName(name, AkmlSql.Core.DatabaseKind.PostgreSql));
    }
}
