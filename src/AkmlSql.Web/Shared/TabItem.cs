namespace AkmlSql.Web.Shared;

/// <summary>
/// Spec 041 (research R58) — one tab of a <see cref="TabStrip"/>.
/// </summary>
/// <param name="Id">Stable id; the strip reports it through <c>ActiveIdChanged</c>.</param>
/// <param name="Label">The visible text.</param>
/// <param name="Badge">Optional count or state shown as a pill after the label (e.g. "3").</param>
/// <param name="BadgeKind">Optional badge colour: <c>error</c> or <c>warning</c>; neutral otherwise.</param>
/// <param name="TestId">The tab's <c>data-testid</c>; defaults to <c>tab-{Id}</c>. Set it to keep an id
/// existing tests select (<c>ai-tab-chat</c>, <c>preview-mysql</c>).</param>
/// <param name="Title">Optional tooltip.</param>
/// <param name="BadgeTestId">Optional <c>data-testid</c> for the badge (e.g. <c>results-problems-badge</c>).</param>
public sealed record TabItem(
    string Id,
    string Label,
    string? Badge = null,
    string? BadgeKind = null,
    string? TestId = null,
    string? Title = null,
    string? BadgeTestId = null);
