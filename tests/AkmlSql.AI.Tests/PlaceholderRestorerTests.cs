using System.Text;
using AkmlSql.Engine.Ai.Privacy;
using Xunit;

namespace AkmlSql.AI.Tests;

/// <summary>
/// Spec 042 (task T112, FR-081). The engine replaces literals with placeholders before a PostgreSQL script goes to a cloud
/// provider; the answer comes back with the placeholders in it and the browser puts the literals back. The streamed answer
/// arrives in arbitrary pieces, so a placeholder split across two pieces must still be found.
/// </summary>
public sealed class PlaceholderRestorerTests
{
    private static readonly Dictionary<string, string> Map = new()
    {
        ["'__STR_0__'"] = "'UK'",
        ["'__STR_1__'"] = "'it''s'",
        ["__NUM_0__"] = "30",
        ["__NUM_10__"] = "99",
        ["$$__STR_2__$$"] = "$$secret$$",
        ["$tag$__STR_3__$tag$"] = "$tag$ other $tag$",
    };

    private const string Answer =
        "SELECT * FROM t WHERE c = '__STR_0__' AND n > __NUM_0__ AND m < __NUM_10__ OR d = '__STR_1__' AND e = $$__STR_2__$$ AND f = $tag$__STR_3__$tag$;";

    private const string Restored =
        "SELECT * FROM t WHERE c = 'UK' AND n > 30 AND m < 99 OR d = 'it''s' AND e = $$secret$$ AND f = $tag$ other $tag$;";

    [Fact]
    public void Every_kind_of_placeholder_is_replaced_by_the_literal_it_stood_for()
    {
        Assert.Equal(Restored, new PlaceholderRestorer(Map).Restore(Answer));
    }

    [Fact]
    public void A_number_placeholder_is_not_confused_with_a_longer_one()
    {
        var map = new Dictionary<string, string> { ["__NUM_1__"] = "one", ["__NUM_10__"] = "ten" };

        Assert.Equal("one ten one", new PlaceholderRestorer(map).Restore("__NUM_1__ __NUM_10__ __NUM_1__"));
    }

    [Fact]
    public void Text_that_is_not_a_known_placeholder_is_left_alone()
    {
        var restorer = new PlaceholderRestorer(Map);

        Assert.Equal("'__STR_9__' and __NUM_7__ and plain", restorer.Restore("'__STR_9__' and __NUM_7__ and plain"));
        Assert.Equal(string.Empty, restorer.Restore(string.Empty));
    }

    [Fact]
    public void A_restored_literal_that_looks_like_a_placeholder_is_not_restored_again()
    {
        var map = new Dictionary<string, string>
        {
            ["'__STR_0__'"] = "'__STR_1__'",
            ["'__STR_1__'"] = "'real'",
        };

        Assert.Equal("'__STR_1__' 'real'", new PlaceholderRestorer(map).Restore("'__STR_0__' '__STR_1__'"));
    }

    [Fact]
    public void With_nothing_to_restore_the_text_is_returned_as_it_is()
    {
        var restorer = new PlaceholderRestorer(new Dictionary<string, string>());

        Assert.True(restorer.IsEmpty);
        Assert.Equal("anything '__STR_0__'", restorer.Restore("anything '__STR_0__'"));
    }

    // ---- streaming --------------------------------------------------------------------------------------------

    private static string Stream(PlaceholderRestorer restorer, IEnumerable<string> chunks)
    {
        var stream = restorer.BeginStream();
        var output = new StringBuilder();
        foreach (var chunk in chunks) output.Append(stream.Push(chunk));
        output.Append(stream.Flush());
        return output.ToString();
    }

    [Fact]
    public void A_stream_cut_at_any_single_position_gives_the_same_text_as_restoring_it_whole()
    {
        var restorer = new PlaceholderRestorer(Map);

        for (var cut = 0; cut <= Answer.Length; cut++)
            Assert.Equal(Restored, Stream(restorer, new[] { Answer[..cut], Answer[cut..] }));
    }

    [Fact]
    public void A_stream_cut_into_single_characters_gives_the_same_text()
    {
        Assert.Equal(Restored, Stream(new PlaceholderRestorer(Map), Answer.Select(c => c.ToString())));
    }

    [Fact]
    public void A_stream_cut_into_random_pieces_gives_the_same_text()
    {
        var random = new Random(7);
        var restorer = new PlaceholderRestorer(Map);

        for (var n = 0; n < 300; n++)
        {
            var pieces = new List<string>();
            for (var at = 0; at < Answer.Length;)
            {
                var len = random.Next(1, 14);
                pieces.Add(Answer.Substring(at, Math.Min(len, Answer.Length - at)));
                at += len;
            }
            Assert.Equal(Restored, Stream(restorer, pieces));
        }
    }

    [Fact]
    public void Text_before_a_possible_placeholder_is_passed_on_at_once_and_only_the_tail_is_held()
    {
        var stream = new PlaceholderRestorer(Map).BeginStream();

        var first = stream.Push("Use WHERE c = '__ST");

        Assert.Equal("Use WHERE c = ", first);
        Assert.Equal("'UK' now", stream.Push("R_0__' now") + stream.Flush());
    }

    [Fact]
    public void Text_without_anything_that_could_be_a_placeholder_is_passed_on_at_once()
    {
        var stream = new PlaceholderRestorer(Map).BeginStream();

        Assert.Equal("plain words, no quotes", stream.Push("plain words, no quotes"));
        Assert.Equal(string.Empty, stream.Flush());
    }

    [Fact]
    public void A_held_tail_that_never_becomes_a_placeholder_is_released_when_the_stream_ends()
    {
        var stream = new PlaceholderRestorer(Map).BeginStream();

        Assert.Equal("done ", stream.Push("done '__ST"));
        Assert.Equal("'__ST", stream.Flush());
    }

    [Fact]
    public void A_held_tail_that_turns_out_not_to_be_a_placeholder_is_released_with_the_next_piece()
    {
        var stream = new PlaceholderRestorer(Map).BeginStream();

        var output = stream.Push("it's '") + stream.Push("quoted' text") + stream.Flush();

        Assert.Equal("it's 'quoted' text", output);
    }

    [Fact]
    public void An_empty_map_streams_straight_through()
    {
        var stream = new PlaceholderRestorer(new Dictionary<string, string>()).BeginStream();

        Assert.Equal("'__STR_0__'", stream.Push("'__STR_0__'"));
        Assert.Equal(string.Empty, stream.Flush());
    }
}
