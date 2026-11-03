using Xunit;

namespace AkmlSql.Web.E2E.Tests;

public sealed class InstalledEngineSummaryTests
{
    [Theory]
    [InlineData("URL: https://PC:47291/  (every computer, this one included)", "https://localhost:47291/")]
    [InlineData("URL: http://localhost:59417/", "http://localhost:59417/")]
    [InlineData("URL:\t https://PC/\t (every computer)", "https://localhost:443/")]
    [InlineData("URL: no-url  (not installed)", null)]
    public void The_first_URL_token_is_used_and_its_scheme_is_preserved(string summary, string? expected)
        => Assert.Equal(expected, InstalledEngine.ParseSiteUrl(summary.Split('\n')));

    [Fact]
    public void Existing_IIS_summaries_prefer_the_separate_local_HTTP_site()
        => Assert.Equal("http://localhost:8090/", InstalledEngine.ParseSiteUrl(new[]
        {
            "URL: https://PC:443/", "(on this computer also http://localhost:8090/)",
        }));
}
