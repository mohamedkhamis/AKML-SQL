using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AkmlSql.Site.Admin;

/// <summary>
/// POST endpoints for the admin portal (GETs are Razor pages). The login POST takes an
/// <see cref="IFormCollection"/> parameter so the antiforgery middleware enforces the token the
/// login page renders — form-action is additionally pinned to 'self' by the site CSP.
/// </summary>
public static class AdminEndpoints
{
    /// <summary>Registers <c>POST /admin/login</c> and <c>POST /admin/logout</c>.</summary>
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/admin/login", HandleLogin);
        // Cast to Delegate: HandleLogout matches the RequestDelegate shape, which would discard
        // the IResult (analyzer ASP0016) instead of writing the redirect to the response.
        endpoints.MapPost("/admin/logout", (Delegate)HandleLogout);
    }

    private static async Task<IResult> HandleLogin(
        HttpContext http,
        IFormCollection form,
        IOptions<AdminOptions> options,
        AdminLoginThrottle throttle)
    {
        var ip = AkmlSql.Site.Analytics.HttpRequestFacts.ClientIp(http) ?? "";

        // Throttled IPs wait before their attempt is even evaluated.
        var delay = throttle.GetDelay(ip);
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, http.RequestAborted);
        }

        var password = form["password"].ToString();
        if (options.Value.IsConfigured && AdminAuth.Verify(password, options.Value.PasswordHash))
        {
            throttle.Reset(ip);
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin")], AdminAuth.Scheme);
            await http.SignInAsync(AdminAuth.Scheme, new ClaimsPrincipal(identity));
            return Results.Redirect("/admin");
        }

        throttle.RecordFailure(ip);
        return Results.Redirect("/admin/login?error=1");
    }

    private static async Task<IResult> HandleLogout(HttpContext http)
    {
        await http.SignOutAsync(AdminAuth.Scheme);
        return Results.Redirect("/admin/login");
    }
}
