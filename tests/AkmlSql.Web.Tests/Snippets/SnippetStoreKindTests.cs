using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Snippets;

/// <summary>Spec 042 (task T097, FR-066) - built-in snippets by kind, and user snippets kept per kind.</summary>
public sealed class SnippetStoreKindTests
{
    private static ISnippetStore Build() => new SnippetStore(new InMemoryIndexedDbAdapter());

    private static WebSnippet User(string id, string shortcode, DatabaseKind kind) => new()
    {
        Metadata = new WebSnippetMetadata { Id = id, Shortcode = shortcode, Title = shortcode, Kind = (int)kind },
        Body = new[] { "SELECT '" + shortcode + "';" },
    };

    [Fact]
    public async Task The_built_ins_of_each_kind_are_separate()
    {
        var store = Build();

        var sqlServer = await store.ListAsync(DatabaseKind.SqlServer);
        var postgres = await store.ListAsync(DatabaseKind.PostgreSql);

        Assert.NotEmpty(sqlServer);
        Assert.NotEmpty(postgres);
        Assert.All(sqlServer, s => Assert.Equal(0, s.Metadata.Kind));
        Assert.All(postgres, s => Assert.Equal(1, s.Metadata.Kind));
        Assert.Empty(sqlServer.Intersect(postgres));
        Assert.Equal((await store.ListAsync()).Count, sqlServer.Count + postgres.Count);
    }

    [Fact]
    public async Task The_PostgreSQL_pack_mirrors_the_sql_server_shortcodes_where_an_equivalent_exists()
    {
        var postgres = (await Build().ListAsync(DatabaseKind.PostgreSql)).Select(s => s.Metadata.Shortcode).ToArray();

        foreach (var code in new[] { "sel", "ssf", "ins", "upd", "del", "cte", "crt", "cix", "cfn", "cvi", "txn" })
            Assert.Contains(code, postgres);
        Assert.Equal(postgres.Length, postgres.Distinct().Count());
    }

    [Fact]
    public async Task A_shortcode_expands_in_the_dialect_of_the_connection()
    {
        var store = Build();

        var sqlServer = await store.GetByShortcodeAsync("ins", DatabaseKind.SqlServer);
        var postgres = await store.GetByShortcodeAsync("ins", DatabaseKind.PostgreSql);

        Assert.Equal("builtin.ins", sqlServer!.Metadata.Id);
        Assert.Equal("builtin.pg.ins", postgres!.Metadata.Id);
        Assert.DoesNotContain(sqlServer.Body, l => l.Contains("RETURNING"));
        Assert.Contains(postgres.Body, l => l.Contains("RETURNING"));
    }

    [Fact]
    public async Task A_snippet_has_no_t_sql_in_the_PostgreSQL_pack()
    {
        var body = string.Join("\n", (await Build().ListAsync(DatabaseKind.PostgreSql)).SelectMany(s => s.Body));

        Assert.DoesNotContain("NOCOUNT", body);
        Assert.DoesNotContain("IDENTITY(1,1)", body);
        Assert.DoesNotContain("@", body);
        Assert.DoesNotContain("BEGIN TRY", body);
        Assert.Contains("LANGUAGE plpgsql", body);
    }

    [Fact]
    public async Task Without_a_kind_the_sql_server_snippet_of_a_shortcode_wins()
    {
        var found = await Build().GetByShortcodeAsync("sel");

        Assert.Equal("builtin.sel", found!.Metadata.Id);
    }

    [Fact]
    public async Task A_users_snippet_shows_only_in_its_own_kind()
    {
        var store = Build();
        await store.SaveAsync(User("user.pg", "pgone", DatabaseKind.PostgreSql));
        await store.SaveAsync(User("user.sql", "sqlone", DatabaseKind.SqlServer));

        var postgres = (await store.ListAsync(DatabaseKind.PostgreSql)).Select(s => s.Metadata.Shortcode).ToArray();
        var sqlServer = (await store.ListAsync(DatabaseKind.SqlServer)).Select(s => s.Metadata.Shortcode).ToArray();

        Assert.Contains("pgone", postgres);
        Assert.DoesNotContain("sqlone", postgres);
        Assert.Contains("sqlone", sqlServer);
        Assert.DoesNotContain("pgone", sqlServer);
    }

    [Fact]
    public async Task A_snippet_saved_before_the_feature_is_a_sql_server_snippet()
    {
        var store = Build();
        var old = new WebSnippet { Metadata = new WebSnippetMetadata { Id = "user.old", Shortcode = "old", Title = "Old" }, Body = new[] { "SELECT 1;" } };
        await store.SaveAsync(old);

        Assert.Contains(await store.ListAsync(DatabaseKind.SqlServer), s => s.Metadata.Shortcode == "old");
        Assert.DoesNotContain(await store.ListAsync(DatabaseKind.PostgreSql), s => s.Metadata.Shortcode == "old");
    }

    [Fact]
    public async Task The_same_shortcode_can_be_a_users_in_each_kind()
    {
        var store = Build();
        await store.SaveAsync(User("user.a", "mine", DatabaseKind.SqlServer));
        await store.SaveAsync(User("user.b", "mine", DatabaseKind.PostgreSql));

        Assert.Equal("user.a", (await store.GetByShortcodeAsync("mine", DatabaseKind.SqlServer))!.Metadata.Id);
        Assert.Equal("user.b", (await store.GetByShortcodeAsync("mine", DatabaseKind.PostgreSql))!.Metadata.Id);
    }

    [Fact]
    public async Task The_PostgreSQL_built_ins_cannot_be_changed_or_deleted()
    {
        var store = Build();
        var builtIn = (await store.GetByShortcodeAsync("sel", DatabaseKind.PostgreSql))!;

        await Assert.ThrowsAsync<System.InvalidOperationException>(() => store.SaveAsync(builtIn));
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => store.DeleteAsync(builtIn.Metadata.Id!));
    }

    [Fact]
    public async Task The_kind_survives_a_save_and_a_load()
    {
        var store = Build();
        await store.SaveAsync(User("user.pg", "pgone", DatabaseKind.PostgreSql));

        var again = await store.GetByIdAsync("user.pg");

        Assert.Equal(1, again!.Metadata.Kind);
    }
}
