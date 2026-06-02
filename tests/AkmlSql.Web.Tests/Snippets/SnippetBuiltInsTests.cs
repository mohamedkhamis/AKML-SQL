using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Snippets;

/// <summary>
/// Spec 027 (M5 offline closure) T009 / T015 (US1). Verifies the built-in snippet set and the
/// data-layer guarantees the snippet management surface relies on (the Razor page itself is
/// JS-coupled and exercised interactively; these tests cover the deterministic store + format
/// contract).
///
/// The key cross-surface invariant (FR-006 / SC-002): built-in bodies use the ENGINE-NATIVE
/// <c>$Name$</c> / <c>$CURSOR$</c> / <c>$SELECTEDTEXT$</c> placeholder syntax — NOT the VS-Code
/// <c>${1:label}</c> / <c>$selected$</c> dialect — so a web-authored .akmlsnippet expands
/// correctly on the engine/WPF surface too.
/// </summary>
public sealed class SnippetBuiltInsTests
{
    private static ISnippetStore Build() => new SnippetStore(new InMemoryIndexedDbAdapter());

    // Mirrors AkmlSql.Engine.Snippets.PlaceholderParser's regex: a tab-stop/variable is $Name$.
    private static readonly Regex VsCodeDialect = new(@"\$\{|\$selected\$", RegexOptions.IgnoreCase);

    [Fact]
    public async Task BuiltIns_use_engine_native_placeholder_syntax_not_vscode_dialect()
    {
        var store = Build();
        var builtIns = (await store.ListAsync()).Where(s => s.IsBuiltIn).ToList();
        Assert.NotEmpty(builtIns);

        foreach (var s in builtIns)
        {
            var body = string.Join("\n", s.Body);
            Assert.False(VsCodeDialect.IsMatch(body),
                $"Built-in '{s.Metadata.Shortcode}' uses VS-Code placeholder dialect (${{...}} or $selected$): {body}");
        }
    }

    [Fact]
    public async Task BuiltIns_include_the_floor_ssf_and_cte()
    {
        var store = Build();
        var ids = (await store.ListAsync()).Select(s => s.Metadata.Id).ToHashSet();
        Assert.Contains("builtin.ssf", ids);
        Assert.Contains("builtin.cte", ids);
    }

    [Fact]
    public async Task Surround_capable_built_ins_exist_and_carry_the_selection_token()
    {
        var store = Build();
        var surrounders = (await store.ListAsync())
            .Where(s => s.IsBuiltIn && s.Metadata.SurroundsWith)
            .ToList();

        Assert.NotEmpty(surrounders);
        foreach (var s in surrounders)
        {
            var body = string.Join("\n", s.Body);
            Assert.Contains("$SELECTEDTEXT$", body, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Non_surround_built_ins_do_not_carry_the_selection_token()
    {
        var store = Build();
        var plain = (await store.ListAsync())
            .Where(s => s.IsBuiltIn && !s.Metadata.SurroundsWith);

        foreach (var s in plain)
        {
            var body = string.Join("\n", s.Body);
            Assert.DoesNotContain("$SELECTEDTEXT$", body, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Personal_snippet_round_trips_through_export_import_json_byte_identical()
    {
        // The management page exports via JsonSerializer.Serialize and imports via Deserialize;
        // this asserts that round-trip preserves the snippet (FR-006 / SC-002 shape stability).
        var original = new WebSnippet
        {
            Metadata = new WebSnippetMetadata
            {
                Id = "user.demo", Shortcode = "demo", Title = "Demo",
                Description = "d", SurroundsWith = true, Tags = new[] { "x" },
            },
            Variables = new[] { new WebSnippetVariable { Name = "n", Default = "v", Tooltip = "t" } },
            Body = new[] { "BEGIN", "    $SELECTEDTEXT$", "END;" },
        };

        var json = JsonSerializer.Serialize(original);
        var reimported = JsonSerializer.Deserialize<WebSnippet>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(reimported);
        Assert.Equal(original.Metadata.Shortcode, reimported!.Metadata.Shortcode);
        Assert.Equal(original.Metadata.SurroundsWith, reimported.Metadata.SurroundsWith);
        Assert.Equal(original.Body, reimported.Body);
        Assert.Equal(original.Variables[0].Name, reimported.Variables[0].Name);
        Assert.Equal(original.Variables[0].Tooltip, reimported.Variables[0].Tooltip);
        // Re-serialising the re-imported snippet yields identical JSON (round-trip stable).
        Assert.Equal(json, JsonSerializer.Serialize(reimported));
    }

    [Fact]
    public async Task Imported_snippet_with_builtin_id_is_savable_only_after_id_is_cleared()
    {
        // The page strips a builtin.* id on import so an import can never overwrite a built-in.
        var store = Build();
        var hostile = new WebSnippet
        {
            Metadata = new WebSnippetMetadata { Id = "builtin.ssf", Shortcode = "evil", Title = "Evil" },
            Body = new[] { "SELECT 1;" },
        };
        // With the builtin id, the store refuses (IsBuiltIn guard).
        await Assert.ThrowsAsync<System.InvalidOperationException>(() => store.SaveAsync(hostile));

        // After clearing the id (what the import handler does), it saves as a personal snippet.
        hostile.Metadata.Id = null;
        await store.SaveAsync(hostile);
        var saved = await store.GetByShortcodeAsync("evil");
        Assert.NotNull(saved);
        Assert.False(saved!.IsBuiltIn);
    }
}
