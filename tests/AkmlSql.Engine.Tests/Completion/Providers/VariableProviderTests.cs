using Xunit;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Parser;

namespace AkmlSql.Engine.Tests.Completion.Providers;

public class VariableProviderTests
{
    private readonly VariableProvider _provider = new();

    private static CursorContext MakeContext(
        string partialText = "",
        Dictionary<string, string>? variables = null) => new()
    {
        PartialText = partialText,
        AvailableVariables = variables ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };

    // ── CanHandle ─────────────────────────────────────────────────────────

    [Fact]
    public void CanHandle_AtSymbolPartial_ReturnsTrue()
    {
        var ctx = MakeContext("@my");
        Assert.True(_provider.CanHandle(ctx, null));
    }

    [Fact]
    public void CanHandle_DoubleAt_ReturnsTrue()
    {
        var ctx = MakeContext("@@");
        Assert.True(_provider.CanHandle(ctx, null));
    }

    [Fact]
    public void CanHandle_NoAtSymbol_ReturnsFalse()
    {
        var ctx = MakeContext("col");
        Assert.False(_provider.CanHandle(ctx, null));
    }

    [Fact]
    public void CanHandle_EmptyPartial_ReturnsFalse()
    {
        var ctx = MakeContext("");
        Assert.False(_provider.CanHandle(ctx, null));
    }

    // ── GetCompletions ────────────────────────────────────────────────────

    [Fact]
    public void GetCompletions_DeclaredVariable_InResults()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["@userId"] = "int"
        };
        var ctx = MakeContext("@user", vars);

        var items = _provider.GetCompletions(ctx, null).ToList();

        Assert.Contains(items, i => i.DisplayText == "@userId");
    }

    [Fact]
    public void GetCompletions_NoVariables_ReturnsEmpty()
    {
        var ctx = MakeContext("@x");

        var items = _provider.GetCompletions(ctx, null).ToList();

        Assert.Empty(items);
    }

    [Fact]
    public void GetCompletions_MultipleVariables_AllReturned()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["@a"] = "int",
            ["@b"] = "varchar(50)",
            ["@c"] = "datetime"
        };
        var ctx = MakeContext("@", vars);

        var items = _provider.GetCompletions(ctx, null).ToList();

        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void GetCompletions_TypeShownAsSecondaryText()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["@count"] = "int"
        };
        var ctx = MakeContext("@count", vars);

        var items = _provider.GetCompletions(ctx, null).ToList();
        var item = items.First(i => i.DisplayText == "@count");

        Assert.Equal("int", item.SecondaryText);
    }

    [Fact]
    public void GetCompletions_AllAreVariableObjectType()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["@x"] = "int"
        };
        var ctx = MakeContext("@x", vars);

        var items = _provider.GetCompletions(ctx, null).ToList();

        Assert.All(items, item => Assert.Equal((int)CompletionObjectType.Variable, item.ObjectType));
    }

    [Fact]
    public void GetCompletions_SortPriorityLow()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["@x"] = "int"
        };
        var ctx = MakeContext("@x", vars);

        var items = _provider.GetCompletions(ctx, null).ToList();

        Assert.All(items, item => Assert.True(item.SortPriority < 100,
            "Variables should have low sort priority for high placement"));
    }
}
