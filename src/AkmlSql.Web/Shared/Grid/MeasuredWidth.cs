namespace AkmlSql.Web.Shared.Grid;

/// <summary>One column as <c>akml-results-grid.js</c> <c>measureColumns</c> measured it: the header text
/// and the widest candidate value, in CSS pixels (text only — padding is added by
/// <see cref="GridColumnLayout.Fit"/>).</summary>
public sealed record MeasuredWidth(double Header, double Value);
