namespace AkmlSql.Web.Shared;

/// <summary>
/// Spec 043 (research R6) — a text change reported by the editor with the query tab it was made in: the tab CodeMirror
/// showed when it reported the change, which can differ from the active tab by the time the report is handled.
/// </summary>
/// <param name="TabId">The query tab; null when the page has not named one yet.</param>
/// <param name="Text">The tab's whole text after the change.</param>
public sealed record EditorTabText(string? TabId, string Text);
