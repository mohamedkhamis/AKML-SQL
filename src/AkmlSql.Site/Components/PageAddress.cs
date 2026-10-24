using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Diagnostics;

namespace AkmlSql.Site.Components;

/// <summary>
/// The address of the page being rendered, as the visitor asked for it.
/// <para>
/// In-page links need it because the site has <c>&lt;base href="/"&gt;</c>: a bare <c>#id</c> resolves against the home page, and
/// Blazor's enhanced navigation then loads Home instead of scrolling. On a "page not found" response the request has been
/// re-executed as <c>/not-found</c>, so the request path (and NavigationManager) name that internal route, not the URL in the
/// address bar; the re-execute feature still holds the original.
/// </para>
/// </summary>
public static class PageAddress
{
    /// <summary>Path and query of the visitor's request (never a fragment).</summary>
    public static string PathAndQuery(NavigationManager navigation, HttpContext? http)
    {
        if (http?.Features.Get<IStatusCodeReExecuteFeature>() is { } original)
        {
            return original.OriginalPathBase + original.OriginalPath + original.OriginalQueryString;
        }

        var relative = navigation.ToBaseRelativePath(navigation.Uri);
        var hash = relative.IndexOf('#');
        return "/" + (hash < 0 ? relative : relative[..hash]);
    }

    /// <summary>A link to the element <paramref name="id"/> on this page.</summary>
    public static string Fragment(NavigationManager navigation, HttpContext? http, string id) =>
        PathAndQuery(navigation, http) + "#" + id;

    /// <summary>Path and query of the visitor's request, from the request alone (for components without a NavigationManager).</summary>
    public static string PathAndQuery(HttpContext http)
    {
        if (http.Features.Get<IStatusCodeReExecuteFeature>() is { } original)
        {
            return original.OriginalPathBase + original.OriginalPath + original.OriginalQueryString;
        }

        var path = http.Request.Path.Value;
        return string.IsNullOrEmpty(path) ? "/" : path + http.Request.QueryString.Value;
    }
}
