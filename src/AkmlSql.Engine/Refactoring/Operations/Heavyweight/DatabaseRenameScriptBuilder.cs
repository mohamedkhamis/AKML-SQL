using System.Text;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Spec 030 / T060 / T061 / FR-018 / R8 — the PURE, unit-testable core of database-wide
/// Smart Rename. Given an already-resolved rename target and the already-fetched dependent
/// module definitions, it produces the complete reviewable T-SQL script the user approves
/// before it is applied.
/// <para>
/// This type performs NO database access. All live work (resolving the parent table,
/// classifying object-vs-column, querying <c>sys.sql_expression_dependencies</c> for the
/// referencing modules, and reading each dependent's body from <c>sys.sql_modules</c>) lives
/// in <see cref="DatabaseRenameDependencyReader"/>, which calls this builder with synthetic-or-live
/// rows. Parsing each dependent body in-memory (via <see cref="TsqlParserService"/> +
/// <see cref="ReferenceCollector"/>) is pure CPU, so it stays here — this is what lets T060 feed
/// synthetic <see cref="DependentDefinition"/> rows and assert the emitted script with no SQL Server,
/// mirroring <c>FindInvalidObjectsHandler.MapInvalidObjects</c>.
/// </para>
/// <para>
/// Script shape (FR-018 / US5.1 — applies to BOTH object and column renames):
/// <list type="number">
/// <item><description>A comment header naming the rename.</description></item>
/// <item><description><c>SET XACT_ABORT ON; BEGIN TRANSACTION;</c></description></item>
/// <item><description>The <c>sp_rename</c> — OBJECT form (<c>'schema.obj','new'</c>) or COLUMN form
/// (<c>'schema.table.oldcol','new','COLUMN'</c>).</description></item>
/// <item><description>ONE <c>ALTER</c> per dependent module (proc/view/function/trigger) whose body
/// referenced the old identifier, with the old identifier rewritten to the new name and the leading
/// <c>CREATE</c> turned into <c>ALTER</c>. sp_rename does NOT rewrite dependent module text, so without
/// these the dependents bind to a name that no longer exists until each is ALTERed.</description></item>
/// <item><description><c>COMMIT TRANSACTION;</c> and a footer.</description></item>
/// </list>
/// Zero dependents → the script still renames the object (sp_rename only).
/// </para>
/// </summary>
internal static class DatabaseRenameScriptBuilder
{
    /// <summary>The object or column being renamed (resolved by the reader; fed synthetically by T060).</summary>
    internal readonly record struct RenameTarget(
        string Schema,
        string Name,
        string NewName,
        bool IsColumn,
        string? ParentTable);

    /// <summary>
    /// One referencing module (proc/view/function/trigger) that mentions the old identifier, with its
    /// live <c>sys.sql_modules</c> definition text. The pure builder rewrites the old identifier inside
    /// <see cref="Definition"/> and turns the leading <c>CREATE</c> into <c>ALTER</c>.
    /// </summary>
    internal readonly record struct DependentDefinition(
        string Schema,
        string Name,
        string TypeDesc,
        string Definition);

    private const string Separator = "-- ============================================================";

    /// <summary>
    /// Builds the reviewable rename script. Pure: no DB access, deterministic for given inputs.
    /// </summary>
    /// <param name="target">The resolved rename target (object or column).</param>
    /// <param name="dependents">
    /// The referencing modules whose bodies must be ALTERed. May be empty (object renames with no
    /// dependents still emit a valid sp_rename script).
    /// </param>
    public static string BuildRenameScript(
        RenameTarget target,
        IReadOnlyList<DependentDefinition> dependents)
    {
        var sb = new StringBuilder(4096);

        AppendHeader(sb, target, dependents.Count);

        sb.AppendLine("SET XACT_ABORT ON;");
        sb.AppendLine("BEGIN TRANSACTION;");
        sb.AppendLine("GO");
        sb.AppendLine();

        // ── Step 1: rename the object / column ──────────────────────────────
        sb.Append(BuildSpRename(target));
        sb.AppendLine("GO");
        sb.AppendLine();

        // ── Step 2: re-ALTER every dependent module that referenced the old name ──
        // sp_rename does NOT rewrite the text of dependent modules, so they bind to a name that
        // no longer exists until each is ALTERed with the old identifier replaced by the new one.
        foreach (var dep in dependents)
        {
            var altered = RewriteDependent(dep, target);
            if (altered == null) continue; // body unparseable or no occurrence — skip (reported as warning by caller)

            sb.AppendLine($"-- Update referencing object: {QuoteName(dep.Schema)}.{QuoteName(dep.Name)} ({FriendlyType(dep.TypeDesc)})");
            sb.AppendLine(altered);
            sb.AppendLine("GO");
            sb.AppendLine();
        }

        sb.AppendLine("COMMIT TRANSACTION;");
        sb.AppendLine("GO");
        sb.AppendLine();

        sb.AppendLine(Separator);
        sb.AppendLine($"-- Smart Rename complete: renamed 1 {(target.IsColumn ? "column" : "object")}, " +
                      $"updated {dependents.Count} referencing object(s).");
        sb.AppendLine(Separator);

        return sb.ToString();
    }

    private static void AppendHeader(StringBuilder sb, RenameTarget target, int dependentCount)
    {
        var what = target.IsColumn
            ? $"column {target.Schema}.{target.ParentTable}.{target.Name}"
            : $"object {target.Schema}.{target.Name}";

        sb.AppendLine(Separator);
        sb.AppendLine("-- AKML SQL — Database-wide Smart Rename Script");
        sb.AppendLine($"-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"-- Rename {what} → \"{EscapeComment(target.NewName)}\"");
        sb.AppendLine($"-- Referencing objects to update: {dependentCount}");
        sb.AppendLine(Separator);
        sb.AppendLine("-- WARNING: Review this script carefully before executing it against the database.");
        sb.AppendLine("-- It was generated automatically; verify the rewritten dependent definitions.");
        if (dependentCount > 0)
        {
            // The dependent-body rewrite matches the renamed identifier by NAME wherever it appears as an
            // identifier node. For a COLUMN rename a dependent joining another table that exposes an
            // identically-named column would have BOTH rewritten; for an OBJECT rename a same-named
            // column/alias/variable in a dependent could likewise be rewritten. The user must verify each
            // ALTER. (Acceptable for a reviewable-script tool that is never auto-executed.)
            var kind = target.IsColumn ? "column" : "object";
            sb.AppendLine($"-- NOTE: the {kind} name is rewritten by NAME inside each dependent. If a dependent");
            sb.AppendLine($"--       also uses an identically-named identifier (a column/alias from another");
            sb.AppendLine("--       table, a variable, etc.), verify that ALTER before running it.");
        }
        sb.AppendLine(Separator);
        sb.AppendLine();
    }

    /// <summary>
    /// Emits the <c>EXEC sys.sp_rename</c> call. OBJECT form for an object rename
    /// (<c>'schema.obj','new'</c>); COLUMN form for a column rename
    /// (<c>'schema.table.oldcol','new','COLUMN'</c>). The new name passed to sp_rename is the bare
    /// (unqualified) target name — sp_rename rejects a qualified or bracketed new name.
    /// </summary>
    private static string BuildSpRename(RenameTarget target)
    {
        // First sp_rename argument: a string literal naming the existing object/column. Identifiers are
        // bracket-quoted (and ']' doubled) so names with spaces/dots survive; the surrounding string
        // literal has its single quotes doubled.
        string oldQualified = target.IsColumn
            ? $"{QuoteName(target.Schema)}.{QuoteName(target.ParentTable ?? string.Empty)}.{QuoteName(target.Name)}"
            : $"{QuoteName(target.Schema)}.{QuoteName(target.Name)}";

        // The new name must be UNquoted and UNqualified for sp_rename.
        string newBare = StripBrackets(target.NewName);

        var sb = new StringBuilder();
        if (target.IsColumn)
        {
            sb.AppendLine($"EXEC sys.sp_rename @objname = N'{Literal(oldQualified)}', @newname = N'{Literal(newBare)}', @objtype = 'COLUMN';");
        }
        else
        {
            sb.AppendLine($"EXEC sys.sp_rename @objname = N'{Literal(oldQualified)}', @newname = N'{Literal(newBare)}';");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Rewrites every occurrence of the old identifier inside a dependent's body to the new name and
    /// turns the leading <c>CREATE</c> into <c>ALTER</c>. Returns null when the body cannot be parsed or
    /// the old identifier is not referenced as an identifier (string/comment text is never matched —
    /// the AST visitor only sees real identifier nodes). Pure: parses in-memory only.
    /// </summary>
    private static string? RewriteDependent(DependentDefinition dep, RenameTarget target)
    {
        if (string.IsNullOrWhiteSpace(dep.Definition)) return null;

        var parser = new TsqlParserService();
        var script = parser.Parse(dep.Definition, out _);
        if (script == null) return null;

        var oldName = StripBrackets(target.Name);
        var newName = StripBrackets(target.NewName);

        var collector = new ReferenceCollector(oldName, string.Empty, dep.Definition);
        script.Accept(collector);

        // De-duplicate by offset span BEFORE applying: the TSqlFragmentVisitor descends parent→child,
        // so a single object/table/proc identifier is reported twice at the SAME (StartOffset,EndOffset)
        // (e.g. NamedTableReference + its SchemaObjectName). Applying the same span's Remove+Insert
        // twice corrupts the text (the second edit lands on already-rewritten characters) — observed as
        // "EXEC dbo.GetCustomeGetCustomerOrders". One edit per distinct span, right-to-left.
        var matches = collector.Matches
            .GroupBy(m => (m.StartOffset, m.EndOffset))
            .Select(g => g.First())
            .OrderByDescending(m => m.StartOffset)
            .ToList();

        if (matches.Count == 0) return null;

        var sb = new StringBuilder(dep.Definition);
        foreach (var m in matches)
        {
            // Preserve bracket quoting if the matched text was bracketed.
            string replacement = m.MatchedText.StartsWith("[", StringComparison.Ordinal)
                ? $"[{newName}]"
                : newName;

            int len = m.EndOffset - m.StartOffset;
            if (m.StartOffset < 0 || len < 0 || m.StartOffset + len > sb.Length) continue;
            sb.Remove(m.StartOffset, len);
            sb.Insert(m.StartOffset, replacement);
        }

        return ConvertLeadingCreateToAlter(sb.ToString());
    }

    /// <summary>
    /// Replaces the first leading <c>CREATE</c> keyword (skipping leading whitespace/comments) with
    /// <c>ALTER</c> so the rewritten module re-binds the existing object instead of failing on a
    /// duplicate. Case-insensitive; leaves the rest of the body untouched. If no leading CREATE is
    /// found the body is returned unchanged (the caller still wraps it in the transaction).
    /// </summary>
    private static string ConvertLeadingCreateToAlter(string definition)
    {
        int i = 0;
        int n = definition.Length;

        while (i < n)
        {
            char c = definition[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            // Skip a line comment.
            if (c == '-' && i + 1 < n && definition[i + 1] == '-')
            {
                while (i < n && definition[i] != '\n') i++;
                continue;
            }
            // Skip a block comment.
            if (c == '/' && i + 1 < n && definition[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(definition[i] == '*' && definition[i + 1] == '/')) i++;
                i += 2;
                continue;
            }
            break;
        }

        const string create = "CREATE";
        if (i + create.Length <= n &&
            definition.Substring(i, create.Length).Equals(create, StringComparison.OrdinalIgnoreCase))
        {
            return definition.Substring(0, i) + "ALTER" + definition.Substring(i + create.Length);
        }

        return definition;
    }

    // ── identifier / literal quoting helpers ────────────────────────────────

    /// <summary>Bracket-quotes an identifier, doubling any embedded <c>]</c> (T-SQL escaping).</summary>
    private static string QuoteName(string name)
    {
        var bare = StripBrackets(name);
        return "[" + bare.Replace("]", "]]") + "]";
    }

    /// <summary>Removes a single surrounding pair of brackets if present.</summary>
    private static string StripBrackets(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var t = name.Trim();
        if (t.Length >= 2 && t[0] == '[' && t[t.Length - 1] == ']')
            return t.Substring(1, t.Length - 2).Replace("]]", "]");
        return t;
    }

    /// <summary>Doubles single quotes for safe embedding inside a T-SQL string literal.</summary>
    private static string Literal(string value) => value.Replace("'", "''");

    private static string EscapeComment(string text)
        => string.IsNullOrEmpty(text) ? string.Empty : text.Replace("\r", " ").Replace("\n", " ");

    private static string FriendlyType(string typeDesc)
        => (typeDesc ?? string.Empty).Replace("_", " ").ToUpperInvariant() switch
        {
            "SQL STORED PROCEDURE" => "Stored Procedure",
            "VIEW" => "View",
            "SQL SCALAR FUNCTION" => "Scalar Function",
            "SQL TABLE VALUED FUNCTION" => "Table-Valued Function",
            "SQL INLINE TABLE VALUED FUNCTION" => "Inline Function",
            "SQL TRIGGER" or "SQL DML TRIGGER" => "Trigger",
            _ => string.IsNullOrEmpty(typeDesc) ? "Module" : typeDesc
        };
}
