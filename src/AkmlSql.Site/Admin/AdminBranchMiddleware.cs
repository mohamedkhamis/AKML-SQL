namespace AkmlSql.Site.Admin;

/// <summary>
/// Authorization gate for the /admin branch, chosen over endpoint metadata because static SSR
/// Razor components carry no [Authorize] surface: everything under /admin except the login page
/// (and its POST) requires the admin cookie; unauthenticated requests are redirected to the
/// login page. Must run after <c>UseAuthentication()</c>.
/// </summary>
public sealed class AdminBranchMiddleware
{
    private readonly RequestDelegate _next;

    public AdminBranchMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresChallenge(context))
        {
            context.Response.Redirect("/admin/login");
            return;
        }

        await _next(context);
    }

    /// <summary>True when the request targets a protected /admin path without an authenticated admin cookie.</summary>
    public static bool RequiresChallenge(HttpContext context)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/admin"))
        {
            return false;
        }

        if (path.StartsWithSegments("/admin/login"))
        {
            return false;
        }

        return context.User.Identity?.IsAuthenticated != true;
    }
}
