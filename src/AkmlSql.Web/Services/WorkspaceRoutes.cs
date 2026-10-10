using System;

namespace AkmlSql.Web.Services;

/// <summary>Spec 041 (R33, R42) — which route is the editor workspace (the root, or <c>/editor</c>).</summary>
public static class WorkspaceRoutes
{
    /// <param name="baseRelativePath">The route relative to the app's base (<c>NavigationManager.ToBaseRelativePath</c>).</param>
    public static bool IsEditorRoute(string? baseRelativePath)
    {
        var path = baseRelativePath ?? string.Empty;
        int cut = path.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) path = path.Substring(0, cut);
        path = path.Trim('/');
        return path.Length == 0 || path.Equals("editor", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The main area's classes: no padding around the editor workspace, which fills the window.</summary>
    public static string MainClass(string? baseRelativePath) =>
        IsEditorRoute(baseRelativePath) ? "akml-main akml-main--workspace" : "akml-main";
}
