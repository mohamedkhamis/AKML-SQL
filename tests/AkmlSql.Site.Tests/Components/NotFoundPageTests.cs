using AkmlSql.Site.Components.Pages;
using Bunit;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AkmlSql.Site.Tests.Components;

/// <summary>The not-found page answers 404 whether it is reached by re-execution or requested directly.</summary>
public sealed class NotFoundPageTests
{
    [Fact]
    public void Direct_request_answers_404_not_a_soft_200()
    {
        using var ctx = new BunitContext();
        var http = new DefaultHttpContext();

        var cut = ctx.Render<NotFound>(ps => ps.AddCascadingValue(http));

        Assert.Equal(StatusCodes.Status404NotFound, http.Response.StatusCode);
        Assert.Equal("Page not found", cut.Find("h1").TextContent);
    }
}
