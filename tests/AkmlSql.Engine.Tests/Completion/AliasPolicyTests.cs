using System;
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Engine.Completion.Providers;
using Xunit;

namespace AkmlSql.Engine.Tests.Completion;

/// <summary>
/// Spec 030 T035 / FR-015 — automatic alias generation honors the include-AS option, a
/// user-defined object→alias map, and prefixes-to-ignore. Tests target the policy method
/// <see cref="AliasProvider.BuildAliasItems"/> directly (no editor context needed).
/// </summary>
public class AliasPolicyTests
{
    private static readonly HashSet<string> None = new(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Default_IncludesAs_AndGeneratesFromPascalCase()
    {
        var provider = new AliasProvider();   // IncludeAs defaults true
        var items = provider.BuildAliasItems("OrderDetails", None);

        Assert.NotEmpty(items);
        Assert.Contains(items, i => i.Display == "od");           // PascalCase initials
        Assert.All(items, i => Assert.StartsWith("AS ", i.Insert)); // AS keyword included
        Assert.Contains(items, i => i.Insert == "AS od");
    }

    [Fact]
    public void IncludeAsOff_OmitsAsKeyword()
    {
        var provider = new AliasProvider { IncludeAs = false };
        var items = provider.BuildAliasItems("OrderDetails", None);

        Assert.Contains(items, i => i.Display == "od" && i.Insert == "od");
        Assert.DoesNotContain(items, i => i.Insert.StartsWith("AS ", StringComparison.Ordinal));
    }

    [Fact]
    public void ObjectAliasMap_OffersMappedAliasFirst()
    {
        var provider = new AliasProvider
        {
            ObjectAliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Orders"] = "ord" },
        };
        var items = provider.BuildAliasItems("Orders", None);

        Assert.Equal("ord", items[0].Display);     // custom mapping wins, listed first
        Assert.Equal("AS ord", items[0].Insert);
    }

    [Fact]
    public void PrefixesToIgnore_StrippedBeforeGenerating()
    {
        var provider = new AliasProvider { PrefixesToIgnore = new[] { "tbl_" } };
        var items = provider.BuildAliasItems("tbl_Orders", None);

        // Alias generated from "Orders", not "tbl_Orders" (so "o", not "t").
        Assert.Contains(items, i => i.Display == "o");
        Assert.DoesNotContain(items, i => i.Display == "t");
    }

    [Fact]
    public void ExistingAlias_IsSkipped()
    {
        var provider = new AliasProvider { IncludeAs = false };
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "od" };
        var items = provider.BuildAliasItems("OrderDetails", taken);

        Assert.DoesNotContain(items, i => i.Display == "od");   // conflict skipped
        Assert.Contains(items, i => i.Display == "o");          // fallback still offered
    }
}
