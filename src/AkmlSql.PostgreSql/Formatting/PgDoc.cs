using System.Text;

namespace AkmlSql.PostgreSql.Formatting;

/// <summary>
/// Spec 042 (task T148, research R2) - the layout document the PostgreSQL formatter builds before anything is written: text, places
/// where a line may break, groups that stay on one line when they fit, and indentation. The printers decide WHAT goes where from the
/// style; the renderer decides which groups fit. Keeping the two apart is what makes the output a function of the tokens and the
/// style alone, and so idempotent.
/// </summary>
internal abstract record PgDoc
{
    public static readonly PgDoc Empty = new PgText(string.Empty);

    public static PgDoc Text(string text) => text.Length == 0 ? Empty : new PgText(text);

    /// <summary>Text that may span lines (a dollar-quoted body, a block comment). Never re-indented; a group holding one cannot be flat.</summary>
    public static PgDoc Verbatim(string text) => text.Contains('\n') ? new PgVerbatim(text) : Text(text);

    public static PgDoc Concat(params PgDoc[] parts) => new PgConcat(parts);

    public static PgDoc Concat(IEnumerable<PgDoc> parts) => new PgConcat(parts.ToArray());

    /// <summary>A break that is <paramref name="flat"/> (a space, or nothing) when its group fits on the line.</summary>
    public static PgDoc Line(string flat = " ") => new PgLine(flat);

    /// <summary>A break that is always taken, with <paramref name="blankLines"/> empty lines before the next one.</summary>
    public static PgDoc HardLine(int blankLines = 0) => new PgHardLine(blankLines);

    /// <summary>Indents what follows each break inside <paramref name="body"/> by <paramref name="columns"/> more than the enclosing indent.</summary>
    public static PgDoc Nest(int columns, PgDoc body) => columns == 0 ? body : new PgNest(columns, body);

    /// <summary>Indents what follows each break inside <paramref name="body"/> to the column the body starts at.</summary>
    public static PgDoc Align(PgDoc body) => new PgAlign(body);

    /// <summary>Stays on one line when it fits in the width left (and, if given, in <paramref name="maxFlatWidth"/> characters); otherwise every break in it is taken.</summary>
    public static PgDoc Group(PgDoc body, int? maxFlatWidth = null) => new PgGroup(body, maxFlatWidth, false);

    /// <summary>
    /// A group that is broken unless an enclosing group is flat: a query's clauses, a CASE expression, a list that always goes one item
    /// to a line. When an enclosing group fits on one line it is flattened with the rest (a short subquery), and a collapse threshold
    /// (<paramref name="collapseBelow"/>) flattens it on its own when it is short enough.
    /// </summary>
    public static PgDoc Block(PgDoc body, int? collapseBelow = null) => new PgGroup(body, collapseBelow, true);

    /// <summary>One thing when the enclosing group is broken, another when it is flat.</summary>
    public static PgDoc IfBreak(PgDoc broken, PgDoc flat) => new PgIfBreak(broken, flat);

    /// <summary>
    /// Remembers, for the breaks inside <paramref name="body"/>, the indent of the line the body starts on and the column it starts at,
    /// so a closing parenthesis can return to "the start of the line the opening one was on" or to "under the opening one".
    /// </summary>
    public static PgDoc Scope(PgDoc body) => new PgScope(body);

    /// <summary>A break to the line start (or column) the nearest enclosing <see cref="Scope"/> remembered, plus <paramref name="extra"/> columns.</summary>
    public static PgDoc ScopedLine(PgAnchor anchor, int extra = 0, string flat = " ", bool hard = false) => new PgScopedLine(anchor, extra, flat, hard);

    /// <summary>Writes <paramref name="body"/> with every break in it going to the line start (or column) of the nearest enclosing scope, plus <paramref name="extra"/>.</summary>
    public static PgDoc ScopedIndent(PgAnchor anchor, int extra, PgDoc body) => new PgScopedIndent(anchor, extra, body);

    /// <summary>Text that goes at the end of the current line, wherever the line ends (a trailing comment).</summary>
    public static PgDoc LineSuffix(string text) => new PgLineSuffix(text);

    /// <summary>Takes no space and writes nothing, but no group holding it can stay on one line.</summary>
    public static readonly PgDoc BreakParent = new PgBreakParent();

    public static PgDoc Join(PgDoc separator, IEnumerable<PgDoc> parts)
    {
        var list = new List<PgDoc>();
        foreach (var part in parts)
        {
            if (list.Count > 0) list.Add(separator);
            list.Add(part);
        }
        return new PgConcat(list.ToArray());
    }

    /// <summary>The width of the document on one line, or <see cref="int.MaxValue"/> when it cannot be on one line.</summary>
    public static int FlatWidth(PgDoc doc)
    {
        long total = 0;
        if (!Measure(doc, ref total)) return int.MaxValue;
        return total >= int.MaxValue ? int.MaxValue : (int)total;
    }

    private static bool Measure(PgDoc doc, ref long total)
    {
        switch (doc)
        {
            case PgText t: total += t.Value.Length; return true;
            case PgVerbatim: return false;
            case PgLine l: total += l.Flat.Length; return true;
            case PgScopedLine s:
                if (s.Hard) return false;
                total += s.Flat.Length;
                return true;
            case PgHardLine: return false;
            case PgBreakParent: return false;
            case PgLineSuffix: return true;
            case PgConcat c:
                foreach (var part in c.Parts)
                    if (!Measure(part, ref total)) return false;
                return true;
            case PgNest n: return Measure(n.Body, ref total);
            case PgAlign a: return Measure(a.Body, ref total);
            case PgScope sc: return Measure(sc.Body, ref total);
            case PgScopedIndent si: return Measure(si.Body, ref total);
            case PgGroup g: return Measure(g.Body, ref total);
            case PgIfBreak b: return Measure(b.Flat, ref total);
            default: return true;
        }
    }

    /// <summary>True when the document holds a break that cannot be flattened (a hard line, a multi-line verbatim text, a break parent).</summary>
    public static bool HasForcedBreak(PgDoc doc) => doc switch
    {
        PgVerbatim or PgHardLine or PgBreakParent => true,
        PgScopedLine s => s.Hard,
        PgConcat c => c.Parts.Any(HasForcedBreak),
        PgNest n => HasForcedBreak(n.Body),
        PgAlign a => HasForcedBreak(a.Body),
        PgScope sc => HasForcedBreak(sc.Body),
        PgScopedIndent si => HasForcedBreak(si.Body),
        PgGroup g => HasForcedBreak(g.Body),
        PgIfBreak b => HasForcedBreak(b.Flat),
        _ => false,
    };
}

/// <summary>What a <see cref="PgDoc.ScopedLine"/> returns to.</summary>
internal enum PgAnchor
{
    /// <summary>The indentation of the line the scope began on.</summary>
    LineStart,

    /// <summary>The column the scope began at.</summary>
    Column,
}

internal sealed record PgText(string Value) : PgDoc;
internal sealed record PgVerbatim(string Value) : PgDoc;
internal sealed record PgConcat(PgDoc[] Parts) : PgDoc;
internal sealed record PgLine(string Flat) : PgDoc;
internal sealed record PgHardLine(int BlankLines) : PgDoc;
internal sealed record PgNest(int Columns, PgDoc Body) : PgDoc;
internal sealed record PgAlign(PgDoc Body) : PgDoc;
internal sealed record PgGroup(PgDoc Body, int? MaxFlatWidth, bool Forced) : PgDoc;
internal sealed record PgIfBreak(PgDoc Broken, PgDoc Flat) : PgDoc;
internal sealed record PgScope(PgDoc Body) : PgDoc;
internal sealed record PgScopedLine(PgAnchor Anchor, int Extra, string Flat, bool Hard) : PgDoc;
internal sealed record PgScopedIndent(PgAnchor Anchor, int Extra, PgDoc Body) : PgDoc;
internal sealed record PgLineSuffix(string Value) : PgDoc;
internal sealed record PgBreakParent : PgDoc;

/// <summary>How the renderer writes the whitespace at the start of a line.</summary>
/// <param name="UseTabs">True: whole tab stops are tabs and the remainder spaces. False: spaces.</param>
/// <param name="TabSize">Columns per tab stop; also the width a tab character counts as.</param>
/// <param name="MaxWidth">The width lines are kept within; <see cref="int.MaxValue"/> / 4 for "no limit".</param>
internal readonly record struct PgRenderOptions(bool UseTabs, int TabSize, int MaxWidth);

/// <summary>Writes a <see cref="PgDoc"/> as text: the standard "fits on the rest of the line" algorithm over a stack of (indent, mode, document).</summary>
internal static class PgDocRenderer
{
    private readonly record struct Frame(int Indent, bool Flat, PgDoc Doc, int LineIndent, int AnchorColumn);

    public static string Render(PgDoc doc, PgRenderOptions options)
    {
        var sb = new StringBuilder();
        var suffix = new StringBuilder();
        var column = 0;
        var lineIndent = 0;
        var stack = new Stack<Frame>();
        stack.Push(new Frame(0, false, doc, 0, 0));

        while (stack.Count > 0)
        {
            var (indent, flat, current, anchorLine, anchorColumn) = stack.Pop();
            switch (current)
            {
                case PgText t:
                    sb.Append(t.Value);
                    column += t.Value.Length;
                    break;

                case PgVerbatim v:
                    sb.Append(v.Value);
                    column = ColumnAfter(v.Value, column);
                    break;

                case PgConcat c:
                    for (var i = c.Parts.Length - 1; i >= 0; i--) stack.Push(new Frame(indent, flat, c.Parts[i], anchorLine, anchorColumn));
                    break;

                case PgLine l:
                    if (flat)
                    {
                        sb.Append(l.Flat);
                        column += l.Flat.Length;
                    }
                    else
                    {
                        column = NewLine(sb, suffix, indent, 0, options);
                        lineIndent = indent;
                    }
                    break;

                case PgScopedLine s:
                    if (flat && !s.Hard)
                    {
                        sb.Append(s.Flat);
                        column += s.Flat.Length;
                    }
                    else
                    {
                        var target = (s.Anchor == PgAnchor.LineStart ? anchorLine : anchorColumn) + s.Extra;
                        column = NewLine(sb, suffix, target, 0, options);
                        lineIndent = target;
                    }
                    break;

                case PgHardLine h:
                    column = NewLine(sb, suffix, indent, h.BlankLines, options);
                    lineIndent = indent;
                    break;

                case PgLineSuffix ls:
                    suffix.Append(ls.Value);
                    break;

                case PgBreakParent:
                    break;

                case PgNest n:
                    stack.Push(new Frame(indent + n.Columns, flat, n.Body, anchorLine, anchorColumn));
                    break;

                case PgAlign a:
                    stack.Push(new Frame(column, flat, a.Body, anchorLine, anchorColumn));
                    break;

                case PgScope sc:
                    stack.Push(new Frame(indent, flat, sc.Body, lineIndent, column));
                    break;

                case PgIfBreak b:
                    stack.Push(new Frame(indent, flat, flat ? b.Flat : b.Broken, anchorLine, anchorColumn));
                    break;

                case PgScopedIndent si:
                    stack.Push(new Frame(Math.Max(0, (si.Anchor == PgAnchor.LineStart ? anchorLine : anchorColumn) + si.Extra), flat, si.Body, anchorLine, anchorColumn));
                    break;

                case PgGroup g:
                    // Already inside a flat group: stays flat. A group that holds a break nothing can flatten is broken without asking.
                    if (flat) stack.Push(new Frame(indent, true, g.Body, anchorLine, anchorColumn));
                    else if (PgDoc.HasForcedBreak(g.Body)) stack.Push(new Frame(indent, false, g.Body, anchorLine, anchorColumn));
                    else if (g.Forced && g.MaxFlatWidth is not int) stack.Push(new Frame(indent, false, g.Body, anchorLine, anchorColumn));
                    else stack.Push(new Frame(indent, Fits(options.MaxWidth - column, g, stack), g.Body, anchorLine, anchorColumn));
                    break;
            }
        }

        sb.Append(suffix);
        return sb.ToString();
    }

    // The text up to the end of the verbatim block decides the column the next token starts at.
    private static int ColumnAfter(string text, int column)
    {
        var lastBreak = text.LastIndexOf('\n');
        return lastBreak < 0 ? column + text.Length : text.Length - lastBreak - 1;
    }

    private static int NewLine(StringBuilder sb, StringBuilder suffix, int indent, int blankLines, PgRenderOptions options)
    {
        // What was waiting for the end of the line (a trailing comment) goes out first; a line never ends in white space the
        // formatter put there.
        while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        if (suffix.Length > 0)
        {
            sb.Append(suffix);
            suffix.Clear();
        }
        sb.Append('\n');
        for (var i = 0; i < blankLines; i++) sb.Append('\n');
        indent = Math.Max(0, indent);
        if (options.UseTabs && options.TabSize > 0)
        {
            sb.Append('\t', indent / options.TabSize);
            sb.Append(' ', indent % options.TabSize);
        }
        else
        {
            sb.Append(' ', indent);
        }
        return indent;
    }

    /// <summary>
    /// Whether <paramref name="group"/> laid out flat, followed by what is already on the stack (in the modes it will be written in),
    /// stays within <paramref name="remaining"/> columns up to the next break that will be taken.
    /// </summary>
    private static bool Fits(int remaining, PgGroup group, Stack<Frame> rest)
    {
        if (remaining < 0) return false;
        if (group.MaxFlatWidth is int limit && PgDoc.FlatWidth(group.Body) > limit) return false;

        var work = new Stack<Frame>();
        work.Push(new Frame(0, true, group.Body, 0, 0));
        var pending = rest.ToArray();   // top of the stack first
        var next = 0;

        while (remaining >= 0)
        {
            if (work.Count == 0)
            {
                if (next >= pending.Length) return true;
                work.Push(pending[next++]);
                continue;
            }

            var (indent, flat, doc, _, _) = work.Pop();
            switch (doc)
            {
                case PgText t:
                    remaining -= t.Value.Length;
                    break;

                case PgVerbatim v:
                    if (flat) return false;
                    // Written as it is: only its first line sits on this one.
                    var firstBreak = v.Value.IndexOf('\n');
                    remaining -= firstBreak < 0 ? v.Value.Length : firstBreak;
                    return remaining >= 0;

                case PgConcat c:
                    for (var i = c.Parts.Length - 1; i >= 0; i--) work.Push(new Frame(indent, flat, c.Parts[i], 0, 0));
                    break;

                case PgLine l:
                    if (flat) remaining -= l.Flat.Length;
                    else return true;      // a break that will be taken: the line ends here and it fit
                    break;

                case PgScopedLine s:
                    if (flat && !s.Hard) remaining -= s.Flat.Length;
                    else return !flat;     // taken in break mode: the line ends here; a hard one in a flat group cannot be flat
                    break;

                case PgHardLine:
                    return !flat;

                case PgBreakParent:
                    if (flat) return false;
                    break;

                case PgNest n:
                    work.Push(new Frame(indent + n.Columns, flat, n.Body, 0, 0));
                    break;

                case PgAlign a:
                    work.Push(new Frame(indent, flat, a.Body, 0, 0));
                    break;

                case PgScope sc:
                    work.Push(new Frame(indent, flat, sc.Body, 0, 0));
                    break;

                case PgScopedIndent si:
                    work.Push(new Frame(indent, flat, si.Body, 0, 0));
                    break;

                case PgIfBreak b:
                    work.Push(new Frame(indent, flat, flat ? b.Flat : b.Broken, 0, 0));
                    break;

                case PgGroup g:
                    work.Push(new Frame(indent, flat, g.Body, 0, 0));
                    break;
            }
        }
        return false;
    }
}
