using Xunit;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Parser;

namespace AkmlSql.Engine.Tests.Completion.Providers;

public class KeywordProviderTests
{
    private readonly KeywordProvider _provider = new();

    private static CursorContext MakeContext(
        string partialText = "",
        bool inComment = false,
        bool inString = false,
        bool precedingDot = false,
        ClauseType clause = ClauseType.Unknown) => new()
    {
        PartialText = partialText,
        InComment = inComment,
        InString = inString,
        PrecedingDot = precedingDot,
        ClauseType = clause
    };

    // ── CanHandle ─────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_NormalContext_ReturnsTrue()
    {
        var ctx = MakeContext();
        Assert.True(_provider.CanHandle(ctx, null));
    }

    [Fact]
    public void CanHandle_InComment_ReturnsFalse()
    {
        var ctx = MakeContext(inComment: true);
        Assert.False(_provider.CanHandle(ctx, null));
    }

    [Fact]
    public void CanHandle_InString_ReturnsFalse()
    {
        var ctx = MakeContext(inString: true);
        Assert.False(_provider.CanHandle(ctx, null));
    }

    [Fact]
    public void CanHandle_PrecedingDot_ReturnsFalse()
    {
        var ctx = MakeContext(precedingDot: true);
        Assert.False(_provider.CanHandle(ctx, null));
    }

    // ── GetCompletions ────────────────────────────────────────────────────

    [Fact]
    public void GetCompletions_NoFilter_ReturnsKeywords()
    {
        var ctx = MakeContext();
        var items = _provider.GetCompletions(ctx, null).ToList();

        Assert.NotEmpty(items);
    }

    [Fact]
    public void GetCompletions_AllHaveDisplayText()
    {
        var ctx = MakeContext();
        var items = _provider.GetCompletions(ctx, null).ToList();

        Assert.All(items, item => Assert.NotEmpty(item.DisplayText));
    }

    [Fact]
    public void GetCompletions_AllAreKeywordObjectType()
    {
        var ctx = MakeContext();
        var items = _provider.GetCompletions(ctx, null).ToList();

        Assert.All(items, item => Assert.Equal((int)CompletionObjectType.Keyword, item.ObjectType));
    }

    [Fact]
    public void GetCompletions_DisplayTextEqualsInsertText()
    {
        var ctx = MakeContext();
        var items = _provider.GetCompletions(ctx, null).ToList();

        Assert.All(items, item => Assert.Equal(item.DisplayText, item.InsertText));
    }

    [Fact]
    public void GetCompletions_SelectContext_ContainsFromKeyword()
    {
        var ctx = MakeContext(clause: ClauseType.Select);
        var items = _provider.GetCompletions(ctx, null).ToList();

        Assert.Contains(items, i => i.DisplayText.Equals("FROM", StringComparison.OrdinalIgnoreCase));
    }

    // ── Casing ────────────────────────────────────────────────────────────

    [Fact]
    public void GetCompletions_UpperCasing_AllUppercase()
    {
        var provider = new KeywordProvider(KeywordCasing.Upper);
        var ctx = MakeContext();
        var items = provider.GetCompletions(ctx, null).ToList();

        Assert.All(items, item => Assert.Equal(item.DisplayText.ToUpperInvariant(), item.DisplayText));
    }

    [Fact]
    public void GetCompletions_LowerCasing_AllLowercase()
    {
        var provider = new KeywordProvider(KeywordCasing.Lower);
        var ctx = MakeContext();
        var items = provider.GetCompletions(ctx, null).ToList();

        Assert.All(items, item => Assert.Equal(item.DisplayText.ToLowerInvariant(), item.DisplayText));
    }

    // ── No duplicates ─────────────────────────────────────────────────────

    [Fact]
    public void GetCompletions_NoDuplicateDisplayTexts()
    {
        var ctx = MakeContext();
        var items = _provider.GetCompletions(ctx, null).ToList();
        var displayTexts = items.Select(i => i.DisplayText.ToUpperInvariant()).ToList();
        var unique = displayTexts.Distinct().ToList();

        Assert.Equal(unique.Count, displayTexts.Count);
    }
}
