using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.PostgreSql.Language;
using AkmlSql.PostgreSql.Reference;
using Serilog;

namespace AkmlSql.PostgreSql.Completion;

/// <summary>
/// Spec 042 (task T102, FR-053, FR-063, scenario US3-5) - what hovering a word shows. A schema object shows its kind, schema,
/// columns or parameters and its comment; a command, key word, function, operator or type shows the manual's summary, its syntax or
/// signatures, the release it first appears in when that matters, and a link to the official page.
/// </summary>
public sealed class PgQuickInfoService
{
    private const int MaxColumns = 14;
    private const int MaxSignatures = 6;

    private readonly PgReference _reference;

    public PgQuickInfoService() : this(PgReference.Instance) { }

    public PgQuickInfoService(PgReference reference) => _reference = reference;

    /// <summary>An empty response (no object type, no header) when there is nothing under <paramref name="offset"/> worth describing.</summary>
    public QuickInfoResponse Get(string? text, int offset, DatabaseCache? cache, int serverMajor = 18)
    {
        text ??= string.Empty;
        try
        {
            return Describe(text, Math.Clamp(offset, 0, text.Length), cache, _reference.For(serverMajor)) ?? Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PostgreSQL quick info failed at offset {Offset}", offset);
            return Empty;
        }
    }

    private static QuickInfoResponse Empty => new() { ObjectType = string.Empty, Header = string.Empty };

    // ── Finding what is under the pointer ───────────────────────────────────────────────────────

    private QuickInfoResponse? Describe(string text, int offset, DatabaseCache? cache, PgReference reference)
    {
        var tokens = PgLexicalScanner.Scan(text);
        var index = TokenAt(tokens, offset);
        if (index < 0) return null;
        var token = tokens[index];

        if (token.Kind == PgTokenKind.Operator) return OperatorInfo(text, token, reference);
        if (token.Kind is not (PgTokenKind.Identifier or PgTokenKind.QuotedIdentifier)) return null;

        // the dotted chain this word belongs to, and which part of it was hovered
        var chain = Chain(text, tokens, index, out var hovered);
        if (chain.Count == 0) return null;
        var resolveThrough = chain.Take(hovered + 1).ToList();

        // the statement around the word: which tables and CTEs it can see
        var context = PgCursorContextAnalyzer.Analyze(text, chain[0].Start);
        var view = new PgCatalogView(cache, new PgCompletionOptions { IncludeSystemObjects = true });

        var names = resolveThrough.Select(p => p.Name).ToList();
        var followedByParen = hovered == chain.Count - 1 && NextSignificant(tokens, chain[^1].Index) is { } next && next.Kind == PgTokenKind.Punctuation && text[next.Start] == '(';

        if (names.Count == 1)
        {
            var name = names[0];
            if (followedByParen && FunctionInfo(name, null, view, reference) is { } call) return call;
            return SingleName(text, tokens, chain[0].Index, name, context, view, reference);
        }

        if (names.Count == 2) return TwoParts(names[0], names[1], followedByParen, context, view, reference);
        if (names.Count == 3) return ColumnOf(names[0], names[1], names[2], view);
        return null;
    }

    private sealed record Part(int Index, int Start, PgName Name);

    private static int TokenAt(IReadOnlyList<PgLexToken> tokens, int offset)
    {
        for (var i = 0; i < tokens.Count; i++)
            if (tokens[i].Start <= offset && offset < tokens[i].End) return i;
        // the pointer just after the last character of a word
        for (var i = 0; i < tokens.Count; i++)
            if (tokens[i].End == offset && tokens[i].Kind is PgTokenKind.Identifier or PgTokenKind.QuotedIdentifier) return i;
        return -1;
    }

    private static PgLexToken? NextSignificant(IReadOnlyList<PgLexToken> tokens, int index)
    {
        for (var i = index + 1; i < tokens.Count; i++)
            if (tokens[i].Kind is not (PgTokenKind.Whitespace or PgTokenKind.LineComment or PgTokenKind.BlockComment)) return tokens[i];
        return null;
    }

    private static int PreviousSignificant(IReadOnlyList<PgLexToken> tokens, int index)
    {
        for (var i = index - 1; i >= 0; i--)
            if (tokens[i].Kind is not (PgTokenKind.Whitespace or PgTokenKind.LineComment or PgTokenKind.BlockComment)) return i;
        return -1;
    }

    private static int NextSignificantIndex(IReadOnlyList<PgLexToken> tokens, int index)
    {
        for (var i = index + 1; i < tokens.Count; i++)
            if (tokens[i].Kind is not (PgTokenKind.Whitespace or PgTokenKind.LineComment or PgTokenKind.BlockComment)) return i;
        return -1;
    }

    private static bool IsDot(string text, PgLexToken t) => t.Kind == PgTokenKind.Punctuation && text[t.Start] == '.';

    private static bool IsName(PgLexToken t) => t.Kind is PgTokenKind.Identifier or PgTokenKind.QuotedIdentifier;

    private static PgName NameOf(string text, PgLexToken t)
    {
        var raw = text.Substring(t.Start, t.End - t.Start);
        if (t.Kind != PgTokenKind.QuotedIdentifier) return new PgName(raw, false);
        var inner = t.Terminated && raw.Length >= 2 ? raw.Substring(1, raw.Length - 2) : raw.Substring(1);
        return new PgName(inner.Replace("\"\"", "\""), true);
    }

    // schema . table . column around the hovered word
    private static List<Part> Chain(string text, IReadOnlyList<PgLexToken> tokens, int index, out int hovered)
    {
        var parts = new List<Part> { new(index, tokens[index].Start, NameOf(text, tokens[index])) };

        var i = index;
        while (true)
        {
            var dot = PreviousSignificant(tokens, i);
            if (dot < 0 || !IsDot(text, tokens[dot])) break;
            var name = PreviousSignificant(tokens, dot);
            if (name < 0 || !IsName(tokens[name])) break;
            parts.Insert(0, new Part(name, tokens[name].Start, NameOf(text, tokens[name])));
            i = name;
        }
        hovered = parts.Count - 1;

        i = index;
        while (true)
        {
            var dot = NextSignificantIndex(tokens, i);
            if (dot < 0 || !IsDot(text, tokens[dot])) break;
            var name = NextSignificantIndex(tokens, dot);
            if (name < 0 || !IsName(tokens[name])) break;
            parts.Add(new Part(name, tokens[name].Start, NameOf(text, tokens[name])));
            i = name;
        }
        return parts;
    }

    // ── A single name ───────────────────────────────────────────────────────────────────────────

    private QuickInfoResponse? SingleName(string text, IReadOnlyList<PgLexToken> tokens, int tokenIndex, PgName name, PgCursorContext context, PgCatalogView view, PgReference reference)
    {
        var folded = name.Folded;

        // an alias or a table named in the statement
        var inQuery = context.VisibleTables.FirstOrDefault(t => t.Qualifier == folded);
        if (inQuery != null) return TableRefInfo(inQuery, context, view);

        var cte = context.VisibleCtes.FirstOrDefault(c => c.Name == folded);
        if (cte != null) return CteInfo(cte, context, view);

        var target = context.Target != null && context.Target.Qualifier == folded ? context.Target : null;
        if (target != null) return TableRefInfo(target, context, view);

        // a table of the database that the statement does not mention (CREATE INDEX ON t, TRUNCATE t)
        var relation = view.Resolve(null, name.Quoted ? name.Text : folded);
        if (relation != null && relation.ObjectType is not (DbObjectType.Procedure or DbObjectType.ScalarFunction or DbObjectType.TableFunction or DbObjectType.InlineFunction))
            return ObjectInfo(relation, null, view);

        if (!name.Quoted && view.Schemas().Any(s => string.Equals(s, folded, StringComparison.Ordinal)))
            return SchemaInfo(folded, view);

        // a column of one of the tables the statement reads
        var column = ColumnInQuery(folded, context, view);
        if (column != null) return column;

        if (name.Quoted) return null;

        // in a type position the word is a type, even when it is also a key word (integer, interval)
        if (context.Clause is PgClause.TypePosition or PgClause.DdlColumnType && reference.Type(name.Text) is { } typed) return TypeInfo(typed);

        // a command, spelled as one word or as two (CREATE TABLE)
        var upper = name.Text.ToUpperInvariant();
        var next = NextWord(text, tokens, tokenIndex);
        if (next != null && reference.Command(upper + " " + next) is { } two) return CommandInfo(two);
        if (reference.Command(upper) is { } one && IsCommandPosition(context, upper)) return CommandInfo(one);

        if (reference.Keyword(upper) is { } keyword) return KeywordInfo(keyword);
        if (reference.Command(upper) is { } command) return CommandInfo(command);
        if (reference.Type(name.Text) is { } type) return TypeInfo(type);
        if (reference.Callables(folded).Count > 0) return FunctionInfo(name, null, view, reference);
        return null;
    }

    private static string? NextWord(string text, IReadOnlyList<PgLexToken> tokens, int tokenIndex)
    {
        var next = NextSignificantIndex(tokens, tokenIndex);
        if (next < 0 || tokens[next].Kind != PgTokenKind.Identifier) return null;
        return text.Substring(tokens[next].Start, tokens[next].End - tokens[next].Start).ToUpperInvariant();
    }

    // CREATE, DROP, ... at the start of a statement is the command; elsewhere the same word is just a key word
    private static bool IsCommandPosition(PgCursorContext context, string upper) =>
        context.Verb == upper || context.PrecedingWords.Count == 0;

    // ── Two parts: alias.column, schema.table, schema.function ──────────────────────────────────

    private QuickInfoResponse? TwoParts(PgName first, PgName second, bool callOnSecond, PgCursorContext context, PgCatalogView view, PgReference reference)
    {
        var q = first.Folded;

        var reference_ = context.VisibleTables.FirstOrDefault(t => t.Qualifier == q);
        if (reference_ == null && context.ExcludedAvailable && q == "excluded") reference_ = context.Target;
        if (reference_ != null)
        {
            var column = view.ColumnsOf(reference_, context.VisibleCtes).FirstOrDefault(c => second.Matches(c.Name));
            if (column != null) return ColumnInfo(q, reference_.Name ?? q, reference_.Schema, column, view);
        }

        var cte = context.VisibleCtes.FirstOrDefault(c => c.Name == q);
        if (cte != null)
        {
            var column = view.ColumnsOf(cte, context.VisibleCtes).FirstOrDefault(c => second.Matches(c.Name));
            if (column != null) return ColumnInfo(q, cte.Name ?? q, null, column, view);
        }

        if (callOnSecond)
        {
            if (first.Folded == "pg_catalog") return FunctionInfo(second, null, view, reference);
            return FunctionInfo(second, first, view, reference);
        }

        // schema.table
        var o = view.Resolve(first.Quoted ? first.Text : first.Folded, second.Quoted ? second.Text : second.Folded);
        if (o != null) return ObjectInfo(o, null, view);

        // table.column where the table is not in the statement's FROM
        var table = view.Resolve(null, first.Quoted ? first.Text : first.Folded);
        if (table != null)
        {
            var column = table.Columns.FirstOrDefault(c => second.Matches(c.ColumnName));
            if (column != null) return ColumnInfo(table.ObjectName, table.ObjectName, table.SchemaName, ToInfo(column), view);
        }
        return null;
    }

    private QuickInfoResponse? ColumnOf(PgName schema, PgName table, PgName column, PgCatalogView view)
    {
        var o = view.Resolve(schema.Quoted ? schema.Text : schema.Folded, table.Quoted ? table.Text : table.Folded);
        var c = o?.Columns.FirstOrDefault(x => column.Matches(x.ColumnName));
        return o == null || c == null ? null : ColumnInfo(o.ObjectName, o.ObjectName, o.SchemaName, ToInfo(c), view);
    }

    private static PgColumnInfo ToInfo(Column c) => new(c.ColumnName, c.TypeName, c.IsPrimaryKey, c.IsNullable, c.Description);

    private QuickInfoResponse? ColumnInQuery(string folded, PgCursorContext context, PgCatalogView view)
    {
        var matches = new List<(PgTableRef Ref, PgColumnInfo Column)>();
        foreach (var t in context.VisibleTables)
            foreach (var c in view.ColumnsOf(t, context.VisibleCtes))
                if (string.Equals(c.Name, folded, StringComparison.Ordinal)) matches.Add((t, c));
        if (matches.Count == 0 && context.Target != null)
            foreach (var c in view.ColumnsOf(context.Target, context.VisibleCtes))
                if (string.Equals(c.Name, folded, StringComparison.Ordinal)) matches.Add((context.Target, c));
        if (matches.Count == 0) return null;

        var (reference, column) = matches[0];
        return ColumnInfo(reference.Qualifier, reference.Name ?? reference.Qualifier, reference.Schema, column, view);
    }

    // ── Describing a schema object ──────────────────────────────────────────────────────────────

    private QuickInfoResponse TableRefInfo(PgTableRef r, PgCursorContext context, PgCatalogView view)
    {
        switch (r.Kind)
        {
            case PgTableRefKind.Derived:
            case PgTableRefKind.Values:
                return DerivedInfo(r, context, view);
            case PgTableRefKind.Function:
                return new QuickInfoResponse { ObjectType = "Function", Header = r.Alias.Length > 0 ? r.Alias + " (function " + r.Name + ")" : r.Name ?? string.Empty, Details = new[] { new QuickInfoDetail("Kind", "set-returning function in FROM") } };
        }

        var cte = r.Schema == null ? context.VisibleCtes.FirstOrDefault(c => c.Name == r.Name) : null;
        if (cte != null) return CteInfo(cte, context, view, r.Alias);

        var o = r.Name == null ? null : view.Resolve(r.Schema, r.Name);
        if (o == null)
            return new QuickInfoResponse { ObjectType = "Table", Header = (r.Schema != null ? r.Schema + "." : string.Empty) + r.Name, Details = new[] { new QuickInfoDetail("Kind", "not found in the loaded schema") } };
        return ObjectInfo(o, r.Alias.Length > 0 ? r.Alias : null, view);
    }

    private QuickInfoResponse DerivedInfo(PgTableRef r, PgCursorContext context, PgCatalogView view)
    {
        var columns = view.ColumnsOf(r, context.VisibleCtes);
        return new QuickInfoResponse
        {
            ObjectType = r.Kind == PgTableRefKind.Values ? "Values" : "Subquery",
            Header = r.Alias.Length > 0 ? r.Alias + (r.Kind == PgTableRefKind.Values ? " (VALUES list)" : " (subquery)") : "(subquery)",
            Details = new[] { new QuickInfoDetail("Columns", columns.Count == 0 ? "-" : string.Join(", ", columns.Take(MaxColumns).Select(c => c.Name))) },
        };
    }

    private QuickInfoResponse CteInfo(PgTableRef cte, PgCursorContext context, PgCatalogView view, string? alias = null)
    {
        var columns = view.ColumnsOf(cte, context.VisibleCtes);
        return new QuickInfoResponse
        {
            ObjectType = "CTE",
            Header = alias is { Length: > 0 } ? $"{alias} (alias for CTE {cte.Name})" : cte.Name + " (common table expression)",
            Details = new[] { new QuickInfoDetail("Columns", columns.Count == 0 ? "-" : string.Join(", ", columns.Take(MaxColumns).Select(c => c.Name))) },
        };
    }

    private QuickInfoResponse ObjectInfo(DatabaseObject o, string? alias, PgCatalogView view)
    {
        var kind = o.ObjectType switch
        {
            DbObjectType.View => "View",
            DbObjectType.MaterializedView => "Materialized View",
            DbObjectType.Sequence => "Sequence",
            DbObjectType.Procedure => "Procedure",
            DbObjectType.ScalarFunction or DbObjectType.TableFunction or DbObjectType.InlineFunction => "Function",
            _ => "Table",
        };

        var details = new List<QuickInfoDetail> { new("Schema", o.SchemaName) };

        if (o.ObjectType is DbObjectType.Procedure or DbObjectType.ScalarFunction or DbObjectType.TableFunction or DbObjectType.InlineFunction)
        {
            details.Add(new QuickInfoDetail("Parameters", o.Parameters.Count == 0 ? "none" : string.Join(", ", o.Parameters.Select(ParameterText))));
        }
        else
        {
            details.Add(new QuickInfoDetail("Columns", o.Columns.Count.ToString()));
            if (o.Columns.Count > 0)
            {
                var lines = o.Columns.OrderBy(c => c.ColumnId).Take(MaxColumns).Select(ColumnLine).ToList();
                if (o.Columns.Count > MaxColumns) lines.Add($"... and {o.Columns.Count - MaxColumns} more");
                details.Add(new QuickInfoDetail("Column list", string.Join("\n", lines)));
            }
            var keys = o.Columns.Where(c => c.IsPrimaryKey).Select(c => c.ColumnName).ToList();
            if (keys.Count > 0) details.Add(new QuickInfoDetail("Primary key", string.Join(", ", keys)));
            if (o.ApproxRowCount > 0) details.Add(new QuickInfoDetail("Rows", $"~{o.ApproxRowCount:N0}"));
        }

        return new QuickInfoResponse
        {
            ObjectType = kind,
            Header = alias != null ? $"{alias} (alias for {o.FullName})" : o.FullName,
            Details = details.ToArray(),
            Description = string.IsNullOrWhiteSpace(o.Description) ? null : o.Description,
        };
    }

    private static string ParameterText(Parameter p) =>
        (p.IsOutput ? "OUT " : string.Empty) + (string.IsNullOrEmpty(p.ParameterName) ? p.TypeName : p.ParameterName + " " + p.TypeName);

    private static string ColumnLine(Column c) =>
        c.ColumnName + " " + c.TypeName + (c.IsPrimaryKey ? " (PK)" : c.IsNullable ? string.Empty : " NOT NULL");

    private QuickInfoResponse SchemaInfo(string schema, PgCatalogView view)
    {
        var objects = view.Objects(schema).ToList();
        return new QuickInfoResponse
        {
            ObjectType = "Schema",
            Header = schema,
            Details = new[]
            {
                new QuickInfoDetail("Objects", objects.Count.ToString()),
                new QuickInfoDetail("On the search path", view.InSearchPath(schema) ? "yes" : "no"),
            },
        };
    }

    private QuickInfoResponse ColumnInfo(string qualifier, string table, string? schema, PgColumnInfo column, PgCatalogView view)
    {
        var resolved = view.Resolve(schema, table);
        if (schema == null && resolved != null) schema = resolved.SchemaName;
        var details = new List<QuickInfoDetail>();
        if (column.Type.Length > 0) details.Add(new QuickInfoDetail("Type", column.Type));
        details.Add(new QuickInfoDetail("Nullable", column.IsNullable ? "yes" : "no"));
        if (column.IsPrimaryKey) details.Add(new QuickInfoDetail("Primary key", "yes"));

        var owner = view.Resolve(schema, table);
        if (owner != null)
        {
            foreach (var fk in view.ForeignKeysOf(owner))
            {
                var index = fk.ParentColumns.FindIndex(c => string.Equals(c, column.Name, StringComparison.OrdinalIgnoreCase));
                if (index < 0 || !string.Equals(fk.ParentTable, owner.ObjectName, StringComparison.OrdinalIgnoreCase)) continue;
                var referenced = index < fk.ReferencedColumns.Count ? fk.ReferencedColumns[index] : string.Empty;
                details.Add(new QuickInfoDetail("References", $"{fk.ReferencedFullName}({referenced}) - {fk.FkName}"));
            }
        }

        return new QuickInfoResponse
        {
            ObjectType = "Column",
            Header = $"{qualifier}.{column.Name} ({(schema != null ? schema + "." : string.Empty)}{table})",
            Details = details.ToArray(),
            Description = string.IsNullOrWhiteSpace(column.Description) ? null : column.Description,
        };
    }

    // ── Reference entries ───────────────────────────────────────────────────────────────────────

    private QuickInfoResponse? FunctionInfo(PgName name, PgName? schema, PgCatalogView view, PgReference reference)
    {
        // a user-defined function or procedure of the database
        var overloads = view.VisibleObjects()
            .Where(o => o.ObjectType is DbObjectType.Procedure or DbObjectType.ScalarFunction or DbObjectType.TableFunction or DbObjectType.InlineFunction)
            .Where(o => name.Matches(o.ObjectName) && (schema is { } s ? s.Matches(o.SchemaName) : view.InSearchPath(o.SchemaName) || !view.HasCache))
            .ToList();
        if (overloads.Count > 0)
        {
            var first = overloads[0];
            var details = new List<QuickInfoDetail> { new("Schema", first.SchemaName) };
            foreach (var o in overloads.Take(MaxSignatures))
                details.Add(new QuickInfoDetail("Signature", $"{o.ObjectName}({string.Join(", ", o.Parameters.Select(ParameterText))})"));
            return new QuickInfoResponse
            {
                ObjectType = first.ObjectType == DbObjectType.Procedure ? "Procedure" : "Function",
                Header = first.FullName,
                Details = details.ToArray(),
                Description = string.IsNullOrWhiteSpace(first.Description) ? null : first.Description,
            };
        }

        var entries = reference.Callables(name.Folded);
        if (entries.Count == 0) return null;
        var entry = entries[0];

        var lines = new List<QuickInfoDetail>();
        foreach (var s in entry.Signatures.Take(MaxSignatures))
            lines.Add(new QuickInfoDetail("Signature", entry.Name + s.ArgumentText + (s.Returns is { Length: > 0 } r ? " -> " + (s.IsSetReturning ? "setof " : string.Empty) + r : string.Empty)));
        if (entry.Signatures.Count > MaxSignatures) lines.Add(new QuickInfoDetail("More", $"+{entry.Signatures.Count - MaxSignatures} more forms"));
        AddSince(lines, entry);
        lines.Add(new QuickInfoDetail("Documentation", entry.Url));

        return new QuickInfoResponse
        {
            ObjectType = entry.Kind switch { PgEntryKind.Aggregate => "Aggregate", PgEntryKind.Window => "Window function", PgEntryKind.Procedure => "Procedure", _ => "Function" },
            Header = entry.Name,
            Details = lines.ToArray(),
            Description = entry.Summary,
        };
    }

    private QuickInfoResponse CommandInfo(PgReferenceEntry command)
    {
        var details = new List<QuickInfoDetail>();
        if (!string.IsNullOrWhiteSpace(command.Synopsis)) details.Add(new QuickInfoDetail("Syntax", command.Synopsis!));
        AddSince(details, command);
        details.Add(new QuickInfoDetail("Documentation", command.Url));
        return new QuickInfoResponse { ObjectType = "Command", Header = command.Name, Details = details.ToArray(), Description = command.Summary };
    }

    private QuickInfoResponse KeywordInfo(PgReferenceEntry keyword)
    {
        var details = new List<QuickInfoDetail> { new("Kind", "PostgreSQL key word (" + keyword.Category + ")") };
        AddSince(details, keyword);
        details.Add(new QuickInfoDetail("Documentation", keyword.Url));
        return new QuickInfoResponse { ObjectType = "Keyword", Header = keyword.Name, Details = details.ToArray(), Description = keyword.Summary };
    }

    private QuickInfoResponse TypeInfo(PgReferenceEntry type)
    {
        var details = new List<QuickInfoDetail>();
        if (type.Aliases.Count > 0) details.Add(new QuickInfoDetail("Also written", string.Join(", ", type.Aliases)));
        details.Add(new QuickInfoDetail("Category", type.Category));
        AddSince(details, type);
        details.Add(new QuickInfoDetail("Documentation", type.Url));
        return new QuickInfoResponse { ObjectType = "Type", Header = type.Name, Details = details.ToArray(), Description = type.Summary };
    }

    private QuickInfoResponse? OperatorInfo(string text, PgLexToken token, PgReference reference)
    {
        var op = text.Substring(token.Start, token.End - token.Start);
        var entry = reference.Operator(op);
        if (entry == null)
        {
            // `::` and friends are lexical forms of the language
            var lexical = reference.Lexical.FirstOrDefault(e => e.Name == op || e.Aliases.Contains(op));
            if (lexical == null) return null;
            return new QuickInfoResponse
            {
                ObjectType = "Syntax",
                Header = lexical.Name,
                Details = new[] { new QuickInfoDetail("Documentation", lexical.Url) },
                Description = lexical.Summary,
            };
        }

        var details = entry.Signatures.Take(MaxSignatures)
            .Select(s => new QuickInfoDetail("Form", string.Join(" " + entry.Name + " ", s.Args.Select(a => a.Type)) + (s.Returns is { Length: > 0 } r ? " -> " + r : string.Empty)))
            .ToList();
        AddSince(details, entry);
        details.Add(new QuickInfoDetail("Documentation", entry.Url));
        return new QuickInfoResponse { ObjectType = "Operator", Header = entry.Name, Details = details.ToArray(), Description = entry.Summary };
    }

    // "since PostgreSQL 16" only where it tells the reader something: the entry is newer than the oldest release supported.
    private void AddSince(List<QuickInfoDetail> details, PgReferenceEntry entry)
    {
        var oldest = _reference.SupportedMajors.Count > 0 ? _reference.SupportedMajors[0] : 14;
        if (entry.MinVersion > oldest) details.Add(new QuickInfoDetail("Since", "PostgreSQL " + entry.MinVersion));
    }
}
