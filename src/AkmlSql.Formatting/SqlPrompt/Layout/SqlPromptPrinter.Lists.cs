using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.SqlPrompt.Layout;

internal sealed partial class SqlPromptPrinter
{
    /// <summary>How one comma-separated list is laid out: the Lists options, plus the per-construct overrides.</summary>
    private sealed class ListLayout
    {
        /// <summary>Column of the keyword that owns the list (SELECT, GROUP BY, the "(" …) — the indent base.</summary>
        public required int KeywordColumn { get; init; }

        /// <summary>Column of the statement the list belongs to (commas "to statement").</summary>
        public int StatementColumn { get; init; }

        /// <summary>always | never | ifSubsequentItems | ifMultiple | ifMultipleItems | ifSubsequentValues | ifLongerThanMaxLineLength</summary>
        public string FirstOnNewLine { get; init; } = "never";

        /// <summary>always | never | ifLongerThanMaxLineLength</summary>
        public string Subsequent { get; init; } = "always";

        /// <summary>Where the first item goes when it stays on the keyword line (null: one space after the keyword).</summary>
        public int? FirstColumn { get; init; }

        public bool AlignToFirst { get; init; } = true;
        public bool Indent { get; init; } = true;
        public bool AlignComments { get; init; }

        /// <summary>Room to leave at the end of the line for what follows the list (") AS Label").</summary>
        public int TrailingWidth { get; init; }

        /// <summary>Called once the layout is decided, with each item's start column (alias / data-type alignment).</summary>
        public Action<Func<int, int>, bool>? OnLayout { get; init; }
    }

    /// <summary>The Lists options for an ordinary clause list (SELECT, SET, DECLARE…).</summary>
    private ListLayout ClauseList(int keywordColumn, int statementColumn, int? firstColumn, string? firstOnNewLine = null, Action<Func<int, int>, bool>? onLayout = null) => new()
    {
        KeywordColumn = keywordColumn,
        StatementColumn = statementColumn,
        FirstColumn = firstColumn,
        FirstOnNewLine = firstOnNewLine ?? S.Lists.PlaceFirstItemOnNewLine,
        Subsequent = S.Lists.PlaceSubsequentItemsOnNewLines,
        AlignToFirst = S.Lists.AlignSubsequentItemsWithFirstItem,
        Indent = S.Lists.IndentListItems,
        AlignComments = S.Lists.AlignComments,
        OnLayout = onLayout,
    };

    /// <summary>
    /// Prints a comma-separated list. The first item's line, the subsequent items' lines and
    /// columns, and where the commas go all come from <paramref name="layout"/>; the items print
    /// themselves through <paramref name="print"/>.
    /// </summary>
    private void List<TItem>(IList<TItem> items, ListLayout layout, Action<TItem, int> print) where TItem : TSqlFragment
    {
        var n = items.Count;
        if (n == 0) return;
        var multi = n > 1;
        var indentColumn = layout.KeywordColumn + (layout.Indent ? Tab : 0);

        var firstNew = layout.FirstOnNewLine switch
        {
            "always" => true,
            "ifSubsequentItems" or "ifMultiple" or "ifMultipleItems" or "ifSubsequentValues" => multi,
            "ifLongerThanMaxLineLength" => !FitsOnLine(items, layout.FirstColumn),
            _ => false,
        };

        W.BeginCommentGroup();

        // Everything on one line, when the style asks for it or allows it.
        if (!W.IsFlat && layout.Subsequent == "ifLongerThanMaxLineLength" && multi)
        {
            if (W.TryOneLine(() => PrintItems(items, layout, print, firstNew, indentColumn, breakItems: false), Max - layout.TrailingWidth))
            {
                W.EndCommentGroup(false);
                return;
            }
        }

        var breakItems = multi && !W.IsFlat && (layout.Subsequent is "always" or "ifLongerThanMaxLineLength");
        PrintItems(items, layout, print, firstNew, indentColumn, breakItems);
        // The last item's end-of-line comment belongs to the list's comment column too.
        if (layout.AlignComments && breakItems) W.HoistTrailingComments(items[^1].LastTokenIndex);
        W.EndCommentGroup(layout.AlignComments && breakItems);
    }

    /// <summary>Set by <see cref="Clauses"/> so the first list printed in the first clause reports where its first item landed.</summary>
    private bool _captureFirstItem;
    private int? _capturedFirstItem;

    private void PrintItems<TItem>(IList<TItem> items, ListLayout layout, Action<TItem, int> print,
        bool firstNew, int indentColumn, bool breakItems) where TItem : TSqlFragment
    {
        // First item: its own line, a planned column, or wherever the caller's whitespace put it.
        if (firstNew) W.NewLine(indentColumn);
        else if (layout.FirstColumn is int fc) W.PadTo(fc);

        var firstColumn = W.Column;
        if (_captureFirstItem)
        {
            _capturedFirstItem = firstColumn;
            _captureFirstItem = false;
        }
        var itemColumn = firstNew ? indentColumn : layout.AlignToFirst ? firstColumn : indentColumn;
        var commasBefore = S.Lists.CommasBeforeItems;
        var afterComma = S.Lists.SpaceAfterComma ? 1 : 0;
        var commaColumn = S.Lists.CommaAlignment switch
        {
            "beforeItem" => Math.Max(0, itemColumn - 1 - afterComma),
            "toStatement" => layout.StatementColumn,
            _ => itemColumn,
        };

        layout.OnLayout?.Invoke(i =>
        {
            if (i == 0) return firstColumn;
            if (!breakItems) return -1; // same line: no shared column
            return commasBefore ? commaColumn + 1 + afterComma : itemColumn;
        }, breakItems);

        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                var comma = Find(items[i - 1].LastTokenIndex + 1, items[i].FirstTokenIndex - 1, TSqlTokenType.Comma);
                if (comma >= 0)
                {
                    W.Until(comma);
                    if (breakItems && commasBefore)
                    {
                        W.HoistTrailingComments(comma);
                        W.NewLine(commaColumn);
                        W.Through(comma);
                        W.SpaceIf(S.Lists.SpaceAfterComma);
                    }
                    else
                    {
                        W.SpaceIf(S.Lists.SpaceBeforeComma);
                        W.Through(comma);
                        if (breakItems) W.NewLine(itemColumn);
                        else W.SpaceIf(S.Lists.SpaceAfterComma);
                    }
                }
                else if (breakItems)
                {
                    W.NewLine(itemColumn);
                }
                else
                {
                    W.Space();
                }
            }
            print(items[i], i);
        }
    }

    /// <summary>True when the list fits on the current line from where its first item would start.</summary>
    private bool FitsOnLine<TItem>(IList<TItem> items, int? firstColumn) where TItem : TSqlFragment
    {
        if (items.Count == 0) return true;
        var width = W.MeasureFlat(() => W.Through(items[^1].LastTokenIndex), items[0].FirstTokenIndex);
        var column = firstColumn ?? W.Column;
        return column + width <= Max;
    }

    // ── clauses ──────────────────────────────────────────────────────────────

    /// <summary>One clause of a DML statement: its keyword tokens and how to print what follows.</summary>
    private sealed record Clause(int KeywordFirst, int KeywordLast, Action<ClauseContext> Body, bool IsList = true);

    /// <summary>Where a clause's keyword and its first item go.</summary>
    private readonly record struct ClauseContext(int KeywordColumn, int KeywordWidth, int ItemColumn, int StatementColumn, bool FirstClause);

    /// <summary>
    /// Prints the clauses of a DML statement (SELECT … FROM … WHERE …) per the DML clause options
    /// (left-aligned, right-aligned "river", or to the first list item; clause indentation) and
    /// "align items across clauses".
    /// </summary>
    private void Clauses(IReadOnlyList<Clause> clauses, int statementColumn)
    {
        if (clauses.Count == 0) return;
        var widths = clauses.Select(c => PhraseWidth(c.KeywordFirst, c.KeywordLast)).ToArray();
        var maxWidth = widths.Max();
        var indent = S.Dml.ClauseIndentation;
        var alignment = S.Dml.ClauseAlignment;

        int KeywordColumn(int i, int? firstItemColumn) => alignment switch
        {
            "rightAligned" => statementColumn + maxWidth - widths[i] + (i > 0 ? indent : 0),
            "toFirstListItem" when i > 0 && firstItemColumn is int f => f + indent,
            _ => statementColumn + (i > 0 ? indent : 0),
        };

        // Across-clause alignment: every clause's first item starts in one column.
        int? across = null;
        if (S.Lists.AlignItemsAcrossClauses && clauses.Count > 1 && alignment != "toFirstListItem")
            across = TabStop(Enumerable.Range(0, clauses.Count).Max(i => KeywordColumn(i, null) + widths[i] + 1));

        int? firstItemColumn = null;
        for (var i = 0; i < clauses.Count; i++)
        {
            var c = clauses[i];
            var keywordColumn = KeywordColumn(i, firstItemColumn);
            if (i == 0)
            {
                // The first keyword is already placed by the caller; right-aligned clauses may push it right.
                if (keywordColumn > W.Column) W.PadTo(keywordColumn);
            }
            else
            {
                W.NewLine(keywordColumn);
            }
            W.Through(c.KeywordLast);

            var itemColumn = across ?? TabStop(keywordColumn + widths[i] + 1);
            if (W.IsFlat) itemColumn = W.Column;
            if (i == 0)
            {
                _captureFirstItem = true;
                _capturedFirstItem = null;
            }
            c.Body(new ClauseContext(keywordColumn, widths[i], itemColumn, statementColumn, i == 0));
            if (i == 0)
            {
                _captureFirstItem = false;
                firstItemColumn = _capturedFirstItem ?? itemColumn;
            }
        }
    }
}
