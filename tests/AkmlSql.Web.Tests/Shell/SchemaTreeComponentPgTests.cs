using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Bridge;
using Bunit;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 042 (task T076, FR-040 to FR-046): the Schema explorer for a PostgreSQL database - a Materialized Views and a Sequences
/// group, system schemas out of sight until the toggle is pressed, a marker on a schema the role cannot use, and object names
/// written (inserted) the way PostgreSQL writes them.
/// </summary>
public sealed class SchemaTreeComponentPgTests : BunitContext
{
    private const string Server = "pg://db1:5432";
    private const string Db = "northwind";

    // The values of the engine's DbObjectType.
    private const int Table = 0, View = 1, Procedure = 2, Scalar = 3, TableFunction = 4, Sequence = 7, MaterializedView = 8;

    public SchemaTreeComponentPgTests()
    {
        var adapter = new InMemoryIndexedDbAdapter();
        var bridge = new FakeEngineBridge();
        Services.AddSingleton<ISchemaCacheStore>(new SchemaCacheStore(adapter));
        Services.AddSingleton<IEngineBridge>(bridge);
        Services.AddSingleton<ISchemaSync>(new FakeSchemaSync());
    }

    private static SchemaPhaseObject Obj(string schema, string name, int type, params SchemaPhaseColumn[] columns) => new()
    {
        SchemaName = schema, ObjectName = name, ObjectType = type, Columns = columns,
    };

    private async Task<IRenderedComponent<SchemaTreeComponent>> RenderAsync(SchemaPhasePayload payload, DatabaseKind kind = DatabaseKind.PostgreSql)
    {
        payload.DatabaseName = Db;
        payload.Phase = 2;
        await Services.GetRequiredService<ISchemaCacheStore>().SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server,
            DatabaseName = Db,
            PhaseA = MessagePackSerializer.Serialize(payload),
            PhaseB = MessagePackSerializer.Serialize(payload),
            FetchedAt = DateTimeOffset.UtcNow,
        });
        var cut = Render<SchemaTreeComponent>(p => p
            .Add(c => c.ServerCanonicalIdentity, Server)
            .Add(c => c.DatabaseName, Db)
            .Add(c => c.Kind, kind));
        cut.Instance.ExpandForTest(Db);
        cut.Render();
        return cut;
    }

    private static SchemaPhasePayload Sample() => new()
    {
        Schemas = new[]
        {
            new SchemaPhaseSchema
            {
                Name = "sales",
                Objects = new[]
                {
                    Obj("sales", "customers", Table, new SchemaPhaseColumn { Name = "id", TypeName = "integer", IsPrimaryKey = true }),
                    Obj("sales", "big_orders", View),
                    Obj("sales", "order_totals", MaterializedView),
                    Obj("sales", "invoice_seq", Sequence),
                    Obj("sales", "archive", Procedure),
                    Obj("sales", "add_numbers", Scalar),
                    Obj("sales", "orders_of", TableFunction),
                },
            },
            new SchemaPhaseSchema { Name = "pg_catalog", Objects = new[] { Obj("pg_catalog", "pg_class", Table) } },
            new SchemaPhaseSchema { Name = "information_schema", Objects = new[] { Obj("information_schema", "tables", View) } },
            new SchemaPhaseSchema { Name = "secret", Objects = Array.Empty<SchemaPhaseObject>() },
        },
        HiddenSchemas = new[] { "pg_catalog", "information_schema" },
        InaccessibleSchemas = new[] { "secret" },
        SearchPath = new[] { "public" },
    };

    private static string[] Labels(IRenderedComponent<SchemaTreeComponent> cut, string cssClass) =>
        cut.FindAll("." + cssClass + " .akml-schema-tree-label").Select(e => e.TextContent.Trim()).ToArray();

    // ── The groups ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_PostgreSQL_schema_has_materialized_view_and_sequence_groups_and_calls_procedures_procedures()
    {
        var cut = await RenderAsync(Sample());
        cut.Instance.ExpandForTest(Db + "/sales");
        cut.Render();

        Assert.Equal(
            new[] { "Tables (1)", "Views (1)", "Materialized Views (1)", "Sequences (1)", "Procedures (1)", "Functions (2)" },
            Labels(cut, "akml-schema-tree-kind"));
    }

    [Fact]
    public async Task A_SQL_Server_tree_keeps_its_four_groups()
    {
        var cut = await RenderAsync(Sample(), DatabaseKind.SqlServer);
        cut.Instance.ExpandForTest(Db + "/sales");
        cut.Render();

        Assert.Equal(new[] { "Tables (1)", "Views (1)", "Stored Procedures (1)", "Functions (2)" }, Labels(cut, "akml-schema-tree-kind"));
    }

    [Fact]
    public async Task Objects_of_the_new_groups_show_under_them()
    {
        var cut = await RenderAsync(Sample());
        var instance = cut.Instance;
        instance.ExpandForTest(Db + "/sales");
        instance.ExpandForTest(Db + "/sales/Materialized Views");
        instance.ExpandForTest(Db + "/sales/Sequences");
        cut.Render();

        var qualifiers = cut.FindAll("[data-testid='schema-tree-object']").Select(e => e.GetAttribute("data-qualifier")).ToArray();
        Assert.Equal(new[] { "sales.order_totals", "sales.invoice_seq" }, qualifiers);
    }

    // ── System schemas (FR-040) ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task System_schemas_are_hidden_until_the_toggle_is_pressed()
    {
        var cut = await RenderAsync(Sample());

        Assert.Equal(new[] { "Schema \"sales\"", "Schema \"secret\"" }, Labels(cut, "akml-schema-tree-schema"));
        var toggle = cut.Find("[data-testid='schema-system-toggle']");
        Assert.Equal("false", toggle.GetAttribute("aria-pressed"));

        toggle.Click();

        Assert.Equal(
            new[] { "Schema \"sales\"", "Schema \"pg_catalog\"", "Schema \"information_schema\"", "Schema \"secret\"" },
            Labels(cut, "akml-schema-tree-schema"));
        Assert.Equal("true", cut.Find("[data-testid='schema-system-toggle']").GetAttribute("aria-pressed"));

        cut.Find("[data-testid='schema-system-toggle']").Click();
        Assert.Equal(2, Labels(cut, "akml-schema-tree-schema").Length);
    }

    [Fact]
    public async Task The_system_toggle_is_a_labelled_check_box_whose_on_state_shows_a_check()
    {
        var cut = await RenderAsync(Sample());

        var toggle = cut.Find("[data-testid='schema-system-toggle']");
        // Not the fixed 28 px icon button that squeezed the word, and the label is visible text.
        Assert.DoesNotContain("akml-btn--icon", toggle.ClassList);
        Assert.Contains("System", toggle.TextContent);
        Assert.Equal("Show system objects", toggle.GetAttribute("title"));
        Assert.DoesNotContain("is-on", toggle.ClassList);
        Assert.Empty(cut.Find("[data-testid='schema-system-box']").QuerySelectorAll("svg"));

        toggle.Click();

        toggle = cut.Find("[data-testid='schema-system-toggle']");
        Assert.Contains("is-on", toggle.ClassList);
        Assert.Equal("Hide system objects", toggle.GetAttribute("title"));
        Assert.Single(cut.Find("[data-testid='schema-system-box']").QuerySelectorAll("svg"));

        toggle.Click();

        Assert.DoesNotContain("is-on", cut.Find("[data-testid='schema-system-toggle']").ClassList);
        Assert.Empty(cut.Find("[data-testid='schema-system-box']").QuerySelectorAll("svg"));
    }

    [Fact]
    public async Task Without_system_schemas_in_the_payload_there_is_no_toggle()
    {
        var cut = await RenderAsync(new SchemaPhasePayload
        {
            Schemas = new[] { new SchemaPhaseSchema { Name = "public", Objects = new[] { Obj("public", "t", Table) } } },
        });

        Assert.Empty(cut.FindAll("[data-testid='schema-system-toggle']"));
    }

    // ── A schema the role cannot use (FR-046) ───────────────────────────────────────────────────

    [Fact]
    public async Task An_inaccessible_schema_is_listed_with_a_marker_and_says_why_it_is_empty()
    {
        var cut = await RenderAsync(Sample());

        var marker = Assert.Single(cut.FindAll("[data-testid='schema-inaccessible']"));
        Assert.Equal("no access", marker.TextContent.Trim());
        Assert.Contains("USAGE", marker.GetAttribute("title"));

        cut.Instance.ExpandForTest(Db + "/secret");
        cut.Render();
        Assert.Contains("no USAGE privilege", cut.Find("[data-testid='schema-inaccessible-note']").TextContent);
        Assert.Empty(cut.FindAll(".akml-schema-tree-kind"));
    }

    [Fact]
    public async Task Filtering_skips_the_inaccessible_schema()
    {
        var cut = await RenderAsync(Sample());

        cut.Find("[data-testid='schema-filter']").Input("customers");
        cut.WaitForAssertion(() => Assert.Equal(new[] { "Schema \"sales\"" }, Labels(cut, "akml-schema-tree-schema")));
    }

    // ── Names (FR-064) ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Names_are_written_as_PostgreSQL_writes_them_quoted_only_where_they_must_be()
    {
        var payload = new SchemaPhasePayload
        {
            Schemas = new[]
            {
                new SchemaPhaseSchema
                {
                    Name = "Sales Dept",
                    Objects = new[] { Obj("Sales Dept", "Order Lines", Table), Obj("Sales Dept", "user", Table), Obj("Sales Dept", "plain", Table) },
                },
            },
        };
        var cut = await RenderAsync(payload);
        cut.Instance.ExpandForTest(Db + "/Sales Dept");
        cut.Instance.ExpandForTest(Db + "/Sales Dept/Tables");
        cut.Render();

        var qualifiers = cut.FindAll("[data-testid='schema-tree-object']").Select(e => e.GetAttribute("data-qualifier")).ToArray();
        Assert.Equal(new[] { "\"Sales Dept\".\"Order Lines\"", "\"Sales Dept\".\"user\"", "\"Sales Dept\".plain" }, qualifiers);
    }

    [Fact]
    public async Task Clicking_an_object_inserts_its_PostgreSQL_name()
    {
        var cut = await RenderAsync(Sample());
        string? inserted = null;
        cut.Render(p => p.Add(c => c.OnObjectClicked, name => inserted = name).Add(c => c.Kind, DatabaseKind.PostgreSql));
        cut.Instance.ExpandForTest(Db + "/sales");
        cut.Instance.ExpandForTest(Db + "/sales/Views");
        cut.Render();

        cut.Find("[data-testid='schema-tree-object']").Click();

        Assert.Equal("sales.big_orders", inserted);
    }

    [Fact]
    public async Task A_SQL_Server_name_is_still_bracketed()
    {
        var cut = await RenderAsync(Sample(), DatabaseKind.SqlServer);
        cut.Instance.ExpandForTest(Db + "/sales");
        cut.Instance.ExpandForTest(Db + "/sales/Tables");
        cut.Render();

        Assert.Equal("[sales].[customers]", cut.Find("[data-testid='schema-tree-object']").GetAttribute("data-qualifier"));
    }
}
