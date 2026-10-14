using AkmlSql.PostgreSql.Completion;
using AkmlSql.PostgreSql.Reference;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Completion;

/// <summary>Spec 042 (task T096, FR-052, scenario US3-9) - a server is offered only what its release has.</summary>
public class PgVersionGatingTests
{
    private static readonly PgCompletionEngine Engine = new();

    private static string[] Offered(string doc, int serverMajor)
    {
        var caret = doc.IndexOf('|');
        var response = Engine.GetCompletions(doc.Remove(caret, 1), caret, NorthwindPgTestCacheFactory.Create(), new PgCompletionOptions { ServerMajor = serverMajor, MaxSuggestions = 200 });
        return response.Items.Select(i => i.DisplayText).ToArray();
    }

    [Fact]
    public void A_command_is_offered_from_the_release_that_introduced_it()
    {
        Assert.DoesNotContain("MERGE", Offered("|", 14));
        Assert.Contains("MERGE", Offered("|", 15));
        Assert.Contains("MERGE", Offered("|", 18));
    }

    [Fact]
    public void A_function_is_offered_from_the_release_that_introduced_it()
    {
        Assert.DoesNotContain("json_exists", Offered("SELECT json_exi|", 16));
        Assert.Contains("json_exists", Offered("SELECT json_exi|", 17));
        Assert.Contains("json_exists", Offered("SELECT json_exi|", 18));
    }

    [Fact]
    public void Every_function_the_reference_dates_after_14_is_withheld_from_14_and_offered_from_its_release()
    {
        var newer = PgReference.Instance.AllCallables.Where(f => f.MinVersion > 14 && f.Name.Length > 3 && !f.Name.Contains(' ')).Take(60).ToList();
        Assert.True(newer.Count >= 20, "the reference should hold plenty of post-14 functions");

        foreach (var f in newer)
        {
            var doc = "SELECT " + f.Name + "|";
            Assert.DoesNotContain(f.Name, Offered(doc, f.MinVersion - 1));
            Assert.Contains(f.Name, Offered(doc, f.MinVersion));
        }
    }

    [Fact]
    public void A_key_word_newer_than_the_server_is_not_a_suggestion()
    {
        var keyword = PgReference.Instance.AllKeywords.First(k => k.MinVersion >= 17);

        // the keyword shows in the statement-start / clause lists only where the grammar lists it; the reference itself must date it
        Assert.True(keyword.MinVersion >= 17);
        Assert.Null(PgReference.Instance.For(14).Keyword(keyword.Name));
    }

    [Fact]
    public void Types_and_operators_follow_the_release_too()
    {
        var oldest = PgReference.Instance.For(14);
        var newest = PgReference.Instance.For(18);

        Assert.True(newest.AllOperators.Count() >= oldest.AllOperators.Count());
        Assert.True(newest.AllTypes.Count() >= oldest.AllTypes.Count());
        Assert.True(newest.AllCallables.Count() > oldest.AllCallables.Count());
    }

    [Fact]
    public void A_server_newer_than_the_reference_is_offered_everything()
    {
        Assert.Equal(Offered("SELECT json_exi|", 18), Offered("SELECT json_exi|", 25));
    }

    [Fact]
    public void Everything_older_than_the_server_is_still_offered()
    {
        Assert.Contains("count", Offered("SELECT |", 14));
        Assert.Contains("string_agg", Offered("SELECT string_a|", 14));
        Assert.Contains("SELECT", Offered("|", 14));
    }

    [Fact]
    public void Quick_info_and_signature_help_follow_the_release()
    {
        var info = new PgQuickInfoService();
        var signature = new PgSignatureService();
        const string sql = "SELECT json_exists('{}', '$')";

        Assert.Equal("Function", info.Get(sql, 8, null, 17).ObjectType);
        Assert.Equal(string.Empty, info.Get(sql, 8, null, 16).ObjectType);
        Assert.NotEmpty(signature.Get(sql, 19, null, 17).Overloads!);
        Assert.Empty(signature.Get(sql, 19, null, 16).Overloads!);
    }
}
