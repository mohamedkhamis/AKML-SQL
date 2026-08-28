using System.Reflection;
using Xunit;

namespace AkmlSql.Site.Tests;

/// <summary>
/// Sanity gate for the test-project wiring (spec 034 T002): proves the AkmlSql.Site
/// project reference resolves and its assembly loads. Real coverage lands with the
/// story phases (docs pipeline, releases manifest, bunit components).
/// </summary>
public sealed class SanityTests
{
    [Fact]
    public void SiteAssemblyLoads()
    {
        var assembly = Assembly.Load("AkmlSql.Site");

        Assert.NotNull(assembly);
        Assert.Equal("AkmlSql.Site", assembly.GetName().Name);
    }
}
