using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Serilog;

namespace AkmlSql.Engine.Refactoring.Operations.Lightweight;

/// <summary>
/// Spec 030 — schema-aware "Expand wildcards" format action. Replaces <c>SELECT *</c> and
/// <c>SELECT t.*</c> with the explicit column list sourced from the schema cache.
/// <list type="bullet">
///   <item>bare <c>*</c>, single table → bare column names</item>
///   <item>bare <c>*</c>, multiple tables OR qualified <c>t.*</c> → alias-prefixed columns</item>
/// </list>
/// Column resolution reuses <see cref="WildcardExpansionHandler"/>, so this op shares the exact
/// alias/CTE resolution and Phase-B (ColumnsLoaded) guarding of the Ctrl+Space expansion. A star
/// that cannot be resolved is skipped with a per-star warning — the whole document is never failed.
/// </summary>
public class ExpandWildcardsOperation : ILightweightOperation
{
    public (string modifiedText, string[] warnings) Apply(RefactoringContext context)
    {
        try
        {
            if (string.IsNullOrEmpty(context.DocumentText))
                return (context.DocumentText, []);

            if (context.SchemaCache == null)
                return (context.DocumentText,
                    ["Schema cache not available — connect to a database to expand wildcards."]);

            var script = context.Script;
            if (script == null)
                return (context.DocumentText, []);

            var collector = new SelectStarCollector();
            script.Accept(collector);

            if (collector.Stars.Count == 0)
                return (context.DocumentText, []);

            // ExpandWildcards needs columns (Phase B / ColumnsLoaded). The handler re-parses the
            // document internally (it ignores context.Script), so build a fresh parser service.
            var parser = new TsqlParserService();
            parser.SetServerVersion(160);
            var handler = new WildcardExpansionHandler(parser);

            // Process descending by StartOffset so each text replacement preserves earlier offsets.
            var stars = collector.Stars
                .OrderByDescending(s => s.StartOffset)
                .ToList();

            var warnings = new List<string>();
            var text = context.DocumentText;

            foreach (var star in stars)
            {
                // Respect an active selection — skip stars outside the selection range.
                if (context.HasSelection)
                {
                    if (star.StartOffset < context.SelectionStart ||
                        star.StartOffset >= context.SelectionStart + context.SelectionLength)
                        continue;
                }

                // node.Qualifier?.Identifiers.Last().Value — null for a bare '*'.
                var qualifier = star.Qualifier?.Identifiers?.LastOrDefault()?.Value;

                var response = handler.Handle(text, star.StartOffset, qualifier, context.SchemaCache);
                if (!response.Success)
                {
                    warnings.Add(response.ErrorMessage ?? $"Could not expand wildcard at offset {star.StartOffset}.");
                    continue;
                }

                if (response.Tables.Length == 0)
                {
                    warnings.Add($"Could not expand wildcard at offset {star.StartOffset}.");
                    continue;
                }

                // bare '*' single table → bare column names.
                // bare '*' multi-table OR qualified 't.*' → alias-prefix each column.
                bool prefix = qualifier != null || response.Tables.Length > 1;

                var columns = new List<string>();
                foreach (var group in response.Tables)
                {
                    foreach (var col in group.Columns)
                    {
                        columns.Add(prefix
                            ? $"{group.Qualifier}.{col.ColumnName}"
                            : col.ColumnName);
                    }
                }

                if (columns.Count == 0)
                {
                    warnings.Add($"Could not expand wildcard at offset {star.StartOffset}.");
                    continue;
                }

                var replacement = string.Join(", ", columns);

                // FragmentLength spans the whole 't.*' (or bare '*').
                text = text.Remove(star.StartOffset, star.FragmentLength)
                           .Insert(star.StartOffset, replacement);
            }

            return (text, [.. warnings]);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ExpandWildcardsOperation.Apply failed");
            return (context.DocumentText, [$"Expand wildcards failed: {ex.Message}"]);
        }
    }

    private sealed class SelectStarCollector : TSqlFragmentVisitor
    {
        public List<SelectStarExpression> Stars { get; } = [];

        public override void Visit(SelectStarExpression node) => Stars.Add(node);
    }
}
