namespace AkmlSql.Site.Feedback;

/// <summary>
/// The admin inbox's actions. All under <c>/admin</c>, so AdminBranchMiddleware guards them with
/// the same cookie as the rest of the portal, and all bind <see cref="IFormCollection"/> so the
/// antiforgery middleware validates the token. The PUBLIC submission is not here: the /feedback
/// page handles its own form post.
/// </summary>
public static class FeedbackEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/admin/feedback/{id:long}/handled", (long id, IFormCollection form, FeedbackStore store) =>
            MarkHandled(id, form, store, DateTimeOffset.UtcNow));

        endpoints.MapPost("/admin/feedback/{id:long}/reopen", (long id, IFormCollection form, FeedbackStore store) =>
            Reopen(id, form, store));

        endpoints.MapPost("/admin/feedback/{id:long}/delete", (long id, IFormCollection form, FeedbackStore store) =>
            Delete(id, form, store));

        endpoints.MapPost("/admin/feedback/test-email", (
            IFormCollection form, FeedbackNotifier notifier, CancellationToken cancellationToken) =>
            SendTestAsync(form, notifier, cancellationToken));
    }

    public static IResult MarkHandled(long id, IFormCollection form, FeedbackStore store, DateTimeOffset now) =>
        Back(form, store.SetHandled(id, handled: true, now) ? "handled" : "missing");

    public static IResult Reopen(long id, IFormCollection form, FeedbackStore store) =>
        Back(form, store.SetHandled(id, handled: false, DateTimeOffset.UtcNow) ? "reopened" : "missing");

    public static IResult Delete(long id, IFormCollection form, FeedbackStore store) =>
        Back(form, store.Delete(id) ? "deleted" : "missing");

    public static async Task<IResult> SendTestAsync(
        IFormCollection form, FeedbackNotifier notifier, CancellationToken cancellationToken) =>
        Back(form, await notifier.SendTestAsync(cancellationToken).ConfigureAwait(false) ? "test-sent" : "test-failed");

    /// <summary>
    /// Back to the tab the action came from. <c>show</c> is posted by the page but is still user
    /// input, so only the known values survive -- it never becomes part of a redirect as-is.
    /// </summary>
    private static IResult Back(IFormCollection form, string result)
    {
        var show = form["show"].ToString() is "handled" or "all" ? $"show={form["show"]}&" : "";
        return Results.Redirect($"/admin/feedback?{show}result={result}");
    }
}
