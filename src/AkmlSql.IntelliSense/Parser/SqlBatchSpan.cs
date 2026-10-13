namespace AkmlSql.Engine.Parser;

/// <summary>
/// Spec 041 (research R16) — one batch of a script split on <c>GO</c> by
/// <see cref="TsqlParserService.SplitBatches"/>.
/// </summary>
/// <param name="Index">Position among the batches that will run (whitespace-only batches are skipped).</param>
/// <param name="StartOffset">Character offset of the batch in the script.</param>
/// <param name="EndOffset">Offset just past the batch: its <c>GO</c> token, or the end of the script.</param>
/// <param name="StartLine">1-based script line of <paramref name="StartOffset"/> — the server's line 1 for
/// this batch, so a server line <c>L</c> is script line <c>StartLine + L - 1</c>.</param>
/// <param name="RepeatCount">How many times the batch runs (<c>GO 3</c>); 1 without a count.</param>
/// <param name="Text">The batch, verbatim.</param>
public sealed record SqlBatchSpan(int Index, int StartOffset, int EndOffset, int StartLine, int RepeatCount, string Text);

/// <summary>A script split into batches. A non-null <see cref="SeparatorError"/> means a <c>GO</c> line held
/// something other than a repeat count and a comment: the whole script is refused, as SSMS refuses it.</summary>
public sealed record SqlBatchSplit(IReadOnlyList<SqlBatchSpan> Batches, string? SeparatorError);
