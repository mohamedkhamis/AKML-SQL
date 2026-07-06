using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion.Dictionaries;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// Provides schema-aware object completions (tables, views, procedures, functions, schemas).
/// Handles plain names, schema-qualified names (schema.object), and context-aware filtering.
/// </summary>
public class ObjectProvider : ICompletionProvider
{
    public string Name => "Object";

    /// <summary>
    /// When false, system stored procedures from <see cref="SystemProcDictionary"/> are
    /// excluded from Exec-context completions.
    /// Set by <see cref="CompletionEngine"/> before each request.
    /// </summary>
    public bool IncludeSystemObjects { get; set; } = true;

    /// <summary>
    /// Controls how object names are qualified in <see cref="InsertText"/>.
    /// Set by <see cref="CompletionEngine"/> before each request.
    /// Default <see cref="SchemaQualifyMode.Always"/> (SQL Prompt parity).
    /// </summary>
    public SchemaQualifyMode SchemaQualifyMode { get; set; } = SchemaQualifyMode.Always;

    /// <summary>
    /// Controls whether inserted identifier names are wrapped in square brackets.
    /// Set by <see cref="CompletionEngine"/> before each request.
    /// Default <see cref="BracketMode.WhenRequired"/>.
    /// </summary>
    public BracketMode BracketMode { get; set; } = BracketMode.WhenRequired;

    /// <summary>
    /// Spec 030 T036 / FR-016 — suggestion connection scope. <see cref="ScopeSchemas"/> limits the
    /// unqualified object + schema-name list to the named schemas (case-insensitive; empty = all).
    /// <see cref="ObjectsInScope"/> is false when the connected database is excluded from a non-empty
    /// database allow-list — its cache-derived object suggestions are then suppressed entirely.
    /// <see cref="IncludeLinkedServers"/>, when true, surfaces the cache's linked servers
    /// (populated from <c>sys.servers</c> in Phase A) as top-level object-reference suggestions.
    /// Set by <see cref="CompletionEngine"/> per request.
    /// </summary>
    public ISet<string> ScopeSchemas { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>False ⇒ suppress this database's cache-derived object/schema suggestions (FR-016).</summary>
    public bool ObjectsInScope { get; set; } = true;

    /// <summary>
    /// True ⇒ emit a suggestion for each linked server in the cache (FR-016). Governed solely by
    /// this flag — independent of <see cref="ObjectsInScope"/>, since a linked server is a separate
    /// server-level axis, not one of the connected database's user objects. When false, or when the
    /// cache holds no linked servers, behavior is identical to omitting this feature entirely.
    /// </summary>
    public bool IncludeLinkedServers { get; set; }

    /// <summary>True when the schema is in scope: an empty allow-list (all) or a case-insensitive match.</summary>
    private bool SchemaInScope(string schemaName) =>
        ScopeSchemas.Count == 0 || ScopeSchemas.Contains(schemaName);

    private static readonly HashSet<ClauseType> ObjectClauseTypes =
    [
        ClauseType.From,
        ClauseType.JoinTable,
        ClauseType.UpdateTable,
        ClauseType.Exec,
        ClauseType.Create,
        ClauseType.Alter,
        ClauseType.Delete,
        ClauseType.InsertColumns,
        ClauseType.UpdateSet,
        ClauseType.JoinOn
    ];

    private static readonly HashSet<DbObjectType> FromJoinObjectTypes =
    [
        DbObjectType.Table,
        DbObjectType.View,
        DbObjectType.TableFunction,
        DbObjectType.InlineFunction,
        DbObjectType.Synonym
    ];

    private static readonly HashSet<DbObjectType> ExecObjectTypes =
    [
        DbObjectType.Procedure
    ];

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        // CTE suggestions don't need a schema cache — they come from the current doc.
        if (!context.PrecedingDot &&
            context.AvailableCtes.Count > 0 &&
            (context.ClauseType is ClauseType.From or ClauseType.JoinTable or ClauseType.JoinOn))
        {
            return true;
        }

        // System procs from SystemProcDictionary don't need a cache — they come from a static
        // list. When IncludeSystemObjects is enabled and we're in an EXEC context, we can handle
        // the request even without a cache.
        if (cache is null && IncludeSystemObjects && context.ClauseType == ClauseType.Exec && !context.PrecedingDot)
        {
            return true;
        }

        if (cache is null)
        {
            return false;
        }

        // Handle dot-qualified: schema.object or database.schema
        if (context.PrecedingDot && !string.IsNullOrEmpty(context.DotPrefix))
        {
            // If DotPrefix is a known alias, let ColumnProvider handle it (#19)
            if (context.AvailableAliases.ContainsKey(context.DotPrefix))
                return false;

            // Check if DotPrefix is a known schema name
            if (cache.Schemas.ContainsKey(context.DotPrefix))
            {
                return true;
            }

            // Could be database.schema scenario — handle for future extensibility
            return true;
        }

        // Handle non-dot contexts where objects are expected
        if (!ObjectClauseTypes.Contains(context.ClauseType))
            return false;

        // SQL Standard sequencing: after a FROM-target identifier we expect a
        // clause keyword (WHERE / GROUP BY / ORDER BY / JOIN / UNION / etc.) or
        // a comma for another table — NOT another bare table name. Suppress
        // ObjectProvider so KeywordProvider's "AfterFrom" list dominates here.
        // Same rule for JoinTable (after `JOIN <target>`) and UpdateTable.
        if (IsAfterTableTargetIdentifier(context))
            return false;

        return true;
    }

    /// <summary>
    /// True when the cursor sits immediately past a table-target identifier in a
    /// FROM/JOIN/UPDATE clause — i.e., the previous non-whitespace token is an
    /// identifier (the table name or its alias) or a closing paren (end of a
    /// derived-table expression). This is the position where the user expects
    /// the NEXT clause keyword, not another table.
    /// </summary>
    private static bool IsAfterTableTargetIdentifier(CursorContext context)
    {
        if (context.ClauseType is not (ClauseType.From or ClauseType.JoinTable or ClauseType.UpdateTable))
            return false;
        var prev = context.PrecedingToken;
        if (prev == null) return false;
        return prev.TokenType is TSqlTokenType.Identifier
            or TSqlTokenType.QuotedIdentifier
            or TSqlTokenType.RightParenthesis;
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        // CTE names come BEFORE schema objects with a bumped priority — a CTE defined
        // in the same statement is almost always the target when the user types a
        // FROM/JOIN clause inside a subsequent CTE or the final SELECT.
        if (!context.PrecedingDot &&
            context.AvailableCtes.Count > 0 &&
            context.ClauseType is ClauseType.From or ClauseType.JoinTable or ClauseType.JoinOn)
        {
            foreach (var cteName in context.AvailableCtes.Keys)
            {
                yield return new CompletionItem
                {
                    DisplayText = cteName,
                    InsertText = cteName,
                    ObjectType = (int)CompletionObjectType.Table,
                    SecondaryText = "CTE",
                    SortPriority = 50 // above dbo tables (100) and non-dbo (200)
                };
            }
        }

        // When there is no schema cache, we can still offer system stored procedures for
        // EXEC context — they come from SystemProcDictionary, not the cache.
        if (cache is null)
        {
            if (IncludeSystemObjects && context.ClauseType == ClauseType.Exec && !context.PrecedingDot)
            {
                foreach (var item in SystemProcDictionary.GetCompletionItems())
                    yield return item;
            }
            yield break;
        }

        if (context.PrecedingDot && !string.IsNullOrEmpty(context.DotPrefix))
        {
            // FR-016 (T036): when the connected database is out of scope, suppress its objects even
            // for an explicit schema/database prefix. (Schema-scope is NOT applied to an explicit
            // prefix — typing "hr." is a deliberate request for that schema's objects.)
            if (!ObjectsInScope)
                yield break;

            // T047: Multi-part name completion
            foreach (var item in GetDotQualifiedCompletions(context, cache))
                yield return item;
            yield break;
        }

        // No dot: return objects from default schema (dbo) + all schema names
        var allowedTypes = GetAllowedObjectTypes(context.ClauseType);

        // ── FK annotation for JOIN/FROM table suggestions ──
        // Build a set of tables that are FK-related to any already-referenced table
        // in the current statement. In JoinTable/From context those tables get a
        // visual marker ("FK → <other>") in their SecondaryText AND a priority boost
        // so they appear at the top of the suggestion list.
        var fkRelated = BuildFkRelatedLookup(context, cache);

        // In JoinTable context JoinProvider owns FK-related suggestions — it emits the
        // full "TABLE ON left.fk = right.pk" insertion text. Skip them here to avoid
        // showing each FK target twice (once as a full join clause, once as a bare name).
        // In From context we still emit everything — the first table has no prior
        // references to FK-join against, so JoinProvider wouldn't run anyway.
        var skipFkTables = context.ClauseType == ClauseType.JoinTable && fkRelated.Count > 0;

        // FR-016 (T036): suppress this database's cache-derived object + schema-name suggestions
        // when the connected database is excluded from the database allow-list. The schema scope
        // (below) then narrows the list to the in-scope schemas. CTEs (above) and system procs
        // (EXEC, below) are not the connected database's user objects and are unaffected.
        if (ObjectsInScope)
        {
            // First, yield objects from default schema (dbo) with higher priority.
            // When SchemaMode is Always, dbo objects also get the "dbo." prefix in InsertText —
            // but only when the statement has no join. Bug #2 (2026-06-14): re-selecting a table in
            // a single-table FROM should insert "dbo.martyrs"; once a join is involved, qualification
            // is noise (aliases carry the disambiguation), so dbo stays bare. A join is present when
            // the cursor is at a JOIN target, or ≥2 tables are already referenced (JOIN or comma-join).
            bool statementHasJoin =
                context.ClauseType == ClauseType.JoinTable ||
                context.AvailableAliases.Count >= 2;
            if (SchemaInScope("dbo"))
            {
                bool qualifyDbo = SchemaQualifyMode == SchemaQualifyMode.Always && !statementHasJoin;
                foreach (var obj in GetFilteredObjects(cache, "dbo", allowedTypes))
                {
                    if (skipFkTables && fkRelated.ContainsKey(obj.FullName))
                        continue;
                    yield return ToCompletionItem(obj, sortPriorityBase: 100, includeSchema: qualifyDbo, fkRelated: fkRelated);
                }
            }

            // Yield objects from non-dbo schemas, schema-qualified.
            // NonDefaultOnly and Always both qualify non-dbo objects; Never skips the prefix.
            bool qualifyNonDbo = SchemaQualifyMode != SchemaQualifyMode.Never;
            foreach (var schema in cache.Schemas.Values)
            {
                if (schema.SchemaName.Equals("dbo", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!SchemaInScope(schema.SchemaName))
                    continue;

                foreach (var obj in GetFilteredObjects(cache, schema.SchemaName, allowedTypes))
                {
                    if (skipFkTables && fkRelated.ContainsKey(obj.FullName))
                        continue;
                    yield return ToCompletionItem(obj, sortPriorityBase: 200, includeSchema: qualifyNonDbo, fkRelated: fkRelated);
                }
            }

            // Yield schema names as completions (in-scope schemas only).
            foreach (var schemaName in cache.GetSchemaNames())
            {
                if (!SchemaInScope(schemaName))
                    continue;

                yield return new CompletionItem
                {
                    DisplayText = schemaName,
                    InsertText = schemaName,
                    ObjectType = (int)CompletionObjectType.Schema,
                    SecondaryText = "Schema",
                    SortPriority = 300
                };
            }
        }

        // FR-016 — linked-server suggestions. Emitted in object-reference clauses (FROM/JOIN/
        // DELETE/UPDATE), where a four-part "server.database.schema.object" name can begin. This is
        // deliberately OUTSIDE the ObjectsInScope gate above: a linked server is a distinct
        // server-level axis, not one of the connected database's user objects, so it is governed
        // only by IncludeLinkedServers. When the flag is off or no linked servers are loaded, this
        // block is a no-op and the result set is byte-for-byte what it was before the feature.
        if (IncludeLinkedServers &&
            cache.LinkedServers.Count > 0 &&
            context.ClauseType is ClauseType.From or ClauseType.JoinTable or ClauseType.JoinOn
                or ClauseType.UpdateTable or ClauseType.Delete)
        {
            foreach (var ls in cache.LinkedServers.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
                yield return ToLinkedServerItem(ls);
        }

        // Yield system stored procedures in EXEC context — gated on IncludeSystemObjects.
        // SystemProcDictionary contains ms-shipped system procs not present in the user schema cache.
        if (IncludeSystemObjects && context.ClauseType == ClauseType.Exec)
        {
            foreach (var item in SystemProcDictionary.GetCompletionItems())
                yield return item;
        }
    }

    /// <summary>
    /// Builds a completion item for a linked server. Typed as <see cref="CompletionObjectType.Database"/>
    /// (the closest existing server-level concept — no host icon-map changes needed). The insert text
    /// is bracketed as a single whole identifier per <see cref="BracketMode"/> — NOT via
    /// <see cref="ApplyBrackets"/>, whose dot-splitting would mangle names like <c>10.0.0.5</c> or
    /// <c>SERVER\INSTANCE</c> into multiple bracketed parts. Sorts below local objects and schema names.
    /// </summary>
    private CompletionItem ToLinkedServerItem(LinkedServerInfo ls)
    {
        var secondaryText = string.IsNullOrWhiteSpace(ls.Product)
            ? "Linked Server"
            : $"Linked Server ({ls.Product})";

        return new CompletionItem
        {
            DisplayText = ls.Name,
            InsertText = BracketWholeName(ls.Name, BracketMode),
            ObjectType = (int)CompletionObjectType.Database,
            SecondaryText = secondaryText,
            SourceObject = ls.Name,
            SortPriority = 400 // below local objects (100/200) and schema names (300)
        };
    }

    /// <summary>
    /// Brackets an identifier treated as a single whole token (no dot-part splitting), applying
    /// QUOTENAME <c>']'</c>-doubling. Used for linked-server names, which are one identifier even when
    /// they embed dots or backslashes. Mirrors the <see cref="BracketMode"/> semantics of
    /// <see cref="ApplyBrackets"/> but never treats a <c>.</c> as a name separator.
    /// </summary>
    private static string BracketWholeName(string name, BracketMode mode)
    {
        if (string.IsNullOrEmpty(name)) return name;

        bool alreadyBracketed =
            name.StartsWith("[", System.StringComparison.Ordinal) &&
            name.EndsWith("]", System.StringComparison.Ordinal) &&
            name.Length >= 2;

        switch (mode)
        {
            case BracketMode.Always:
                return alreadyBracketed ? name : "[" + name.Replace("]", "]]") + "]";
            case BracketMode.Never:
                return alreadyBracketed ? name.Substring(1, name.Length - 2) : name;
            default: // WhenRequired
                if (alreadyBracketed) return name;
                return NeedsBracketing(name) ? "[" + name.Replace("]", "]]") + "]" : name;
        }
    }

    /// <summary>
    /// Builds a map of "schema.table" → "related existing table name" for every table
    /// that has a foreign key relationship (in either direction) with any already-
    /// referenced table in <see cref="CursorContext.AvailableAliases"/>. Returns an
    /// empty dictionary when the clause context is not JOIN/FROM or when there are
    /// no existing table references.
    /// </summary>
    private static Dictionary<string, string> BuildFkRelatedLookup(CursorContext context, DatabaseCache cache)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (context.ClauseType != ClauseType.JoinTable && context.ClauseType != ClauseType.From)
        {
            return result;
        }

        foreach (var (_, fullTableName) in context.AvailableAliases)
        {
            var parts = fullTableName.Split('.');
            var schemaName = parts.Length >= 2 ? parts[0] : "dbo";
            var tableName = parts.Length >= 2 ? parts[1] : parts[0];

            foreach (var fk in cache.GetForeignKeysForTable(schemaName, tableName))
            {
                // Identify the "other" side of the relationship.
                string otherSchema, otherTable;
                bool isParent = fk.ParentSchema.Equals(schemaName, StringComparison.OrdinalIgnoreCase) &&
                                fk.ParentTable.Equals(tableName, StringComparison.OrdinalIgnoreCase);
                if (isParent)
                {
                    otherSchema = fk.ReferencedSchema;
                    otherTable = fk.ReferencedTable;
                }
                else
                {
                    otherSchema = fk.ParentSchema;
                    otherTable = fk.ParentTable;
                }

                var key = $"{otherSchema}.{otherTable}";
                if (!result.ContainsKey(key))
                {
                    result[key] = tableName;
                }
            }
        }

        return result;
    }

    private IEnumerable<CompletionItem> GetDotQualifiedCompletions(CursorContext context, DatabaseCache cache)
    {
        var prefix = context.DotPrefix;

        // A linked-server-qualified prefix (e.g. "PRODLINK." or "PRODLINK.db.") addresses a REMOTE
        // catalog we do not cache, so we cannot resolve its databases/schemas/objects. Suppress
        // rather than fall through to the "unknown prefix -> all LOCAL schema names" branch below,
        // which would actively mislead with this server's local schemas.
        foreach (var ls in cache.LinkedServers)
        {
            if (string.Equals(prefix, ls.Name, StringComparison.OrdinalIgnoreCase) ||
                prefix.StartsWith(ls.Name + ".", StringComparison.OrdinalIgnoreCase))
                yield break;
        }

        // Check if prefix contains a dot (database.schema scenario)
        var dotIndex = prefix.IndexOf('.');
        if (dotIndex >= 0)
        {
            // T047: database.schema.object — for now we only handle the schema part
            // Extract the schema name (part after the dot)
            var schemaName = prefix[(dotIndex + 1)..];
            var allowedTypes = GetAllowedObjectTypes(context.ClauseType);

            foreach (var obj in GetFilteredObjects(cache, schemaName, allowedTypes))
            {
                yield return ToCompletionItem(obj, sortPriorityBase: 100, fkRelated: null);
            }
            yield break;
        }

        // prefix is a schema name: return objects in that schema
        if (cache.Schemas.ContainsKey(prefix))
        {
            var allowedTypes = GetAllowedObjectTypes(context.ClauseType);
            foreach (var obj in GetFilteredObjects(cache, prefix, allowedTypes))
            {
                yield return ToCompletionItem(obj, sortPriorityBase: 100, fkRelated: null);
            }
            yield break;
        }

        // prefix might be a database name: return schema names
        // (Future: resolve actual database, for now return all schemas)
        foreach (var schemaName in cache.GetSchemaNames())
        {
            yield return new CompletionItem
            {
                DisplayText = schemaName,
                InsertText = schemaName,
                ObjectType = (int)CompletionObjectType.Schema,
                SecondaryText = "Schema",
                SortPriority = 100
            };
        }
    }

    /// <summary>
    /// T049: Context-aware filtering — returns the set of allowed object types based on clause.
    /// </summary>
    private static HashSet<DbObjectType>? GetAllowedObjectTypes(ClauseType clauseType)
    {
        return clauseType switch
        {
            ClauseType.Exec => ExecObjectTypes,
            ClauseType.From or ClauseType.JoinTable or ClauseType.JoinOn or ClauseType.Delete or ClauseType.UpdateTable => FromJoinObjectTypes,
            _ => null // null means all types allowed
        };
    }

    private static IEnumerable<DatabaseObject> GetFilteredObjects(
        DatabaseCache cache, string schemaName, HashSet<DbObjectType>? allowedTypes)
    {
        var objects = cache.GetObjectsInSchema(schemaName);

        if (allowedTypes is not null)
        {
            objects = objects.Where(o => allowedTypes.Contains(o.ObjectType));
        }

        // T050: Rank by ApproxRowCount descending (tables/views), then alphabetical
        return objects
            .OrderByDescending(o => o.ApproxRowCount)
            .ThenBy(o => o.ObjectName, StringComparer.OrdinalIgnoreCase);
    }

    private CompletionItem ToCompletionItem(
        DatabaseObject obj,
        int sortPriorityBase,
        bool includeSchema = false,
        Dictionary<string, string>? fkRelated = null)
    {
        var displayText = includeSchema ? obj.FullName : obj.ObjectName;
        var rawInsertText = includeSchema ? obj.FullName : obj.ObjectName;
        var insertText = ApplyBrackets(rawInsertText, BracketMode);

        var completionType = MapObjectType(obj.ObjectType);

        var secondaryText = obj.ObjectType switch
        {
            DbObjectType.Table => obj.ApproxRowCount > 0
                ? $"Table (~{FormatRowCount(obj.ApproxRowCount)} rows)"
                : "Table",
            DbObjectType.View => "View",
            DbObjectType.Procedure => "Procedure",
            DbObjectType.ScalarFunction => "Scalar Function",
            DbObjectType.TableFunction => "Table Function",
            DbObjectType.InlineFunction => "Inline Function",
            DbObjectType.Synonym => "Synonym",
            DbObjectType.Sequence => "Sequence",
            _ => string.Empty
        };

        // T050: Higher row count => lower sort priority number => ranked higher
        var sortPriority = sortPriorityBase;
        if (obj.ApproxRowCount > 0)
        {
            // Subtract a bonus based on log of row count (max bonus ~50)
            var bonus = (int)Math.Min(50, Math.Log10(obj.ApproxRowCount + 1) * 10);
            sortPriority -= bonus;
        }

        // ── FK annotation ──
        // When this table has a foreign-key relationship with a table already in
        // the current query (JOIN/FROM context), surface that in the secondary
        // text and boost the sort priority so it appears near the top.
        if (fkRelated != null && fkRelated.TryGetValue(obj.FullName, out var relatedTo))
        {
            secondaryText = $"{secondaryText}  •  \uD83D\uDD11 FK ↔ {relatedTo}";
            sortPriority -= 500; // strong bump: FK-related tables are almost always the right pick
        }

        return new CompletionItem
        {
            DisplayText = displayText,
            InsertText = insertText,
            ObjectType = (int)completionType,
            SecondaryText = secondaryText,
            SourceObject = obj.FullName,
            SortPriority = sortPriority
        };
    }

    private static CompletionObjectType MapObjectType(DbObjectType dbType)
    {
        return dbType switch
        {
            DbObjectType.Table => CompletionObjectType.Table,
            DbObjectType.View => CompletionObjectType.View,
            DbObjectType.Procedure => CompletionObjectType.Procedure,
            DbObjectType.ScalarFunction => CompletionObjectType.Function,
            DbObjectType.TableFunction => CompletionObjectType.Function,
            DbObjectType.InlineFunction => CompletionObjectType.Function,
            DbObjectType.Synonym => CompletionObjectType.Table, // Synonyms show as tables
            DbObjectType.Sequence => CompletionObjectType.Table,
            _ => CompletionObjectType.Table
        };
    }

    /// <summary>
    /// Applies square-bracket escaping to an identifier according to <paramref name="mode"/>.
    /// <list type="bullet">
    ///   <item><see cref="BracketMode.Always"/>: always returns <c>[identifier]</c>.</item>
    ///   <item><see cref="BracketMode.Never"/>: always returns the bare identifier.</item>
    ///   <item><see cref="BracketMode.WhenRequired"/> (default): brackets only the parts
    ///         that are not valid regular identifiers (spaces, hyphens, leading digit,
    ///         other special chars) or that are T-SQL reserved words — mirrors the
    ///         "safe-by-default" convention for SQL Prompt parity. Bracketing applies
    ///         QUOTENAME <c>']'</c>-doubling.</item>
    /// </list>
    /// When the input already contains brackets (e.g. schema-qualified <c>[dbo].[Table]</c>)
    /// the result is returned as-is for <c>Always</c> (already bracketed) and stripped of
    /// brackets for <c>Never</c>.
    /// </summary>
    public static string ApplyBrackets(string identifier, BracketMode mode)
    {
        if (string.IsNullOrEmpty(identifier)) return identifier;

        switch (mode)
        {
            case BracketMode.Always:
                // If the identifier is already fully bracketed (e.g. "[Name]"), leave it.
                // If it is schema-qualified (e.g. "dbo.Table"), bracket each part.
                return BracketEachPart(identifier);

            case BracketMode.Never:
                // Strip any existing brackets from each part.
                return StripBracketsEachPart(identifier);

            default: // WhenRequired
                // Bracket only the dot-separated parts that actually require quoting
                // (spaces, hyphens, leading digit, other special chars, or reserved words).
                // Parts that are valid regular identifiers are left bare.
                return BracketRequiredParts(identifier);
        }
    }

    /// <summary>Brackets each dot-separated part of a (possibly schema-qualified) identifier.</summary>
    private static string BracketEachPart(string identifier)
    {
        var parts = identifier.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            // Already bracketed — leave as-is.
            if (part.StartsWith("[", System.StringComparison.Ordinal) &&
                part.EndsWith("]", System.StringComparison.Ordinal))
                continue;
            // QUOTENAME semantics: double any embedded ']' so the result is valid T-SQL.
            parts[i] = "[" + part.Replace("]", "]]") + "]";
        }
        return string.Join(".", parts);
    }

    /// <summary>
    /// Brackets only the dot-separated parts that require quoting (used for
    /// <see cref="BracketMode.WhenRequired"/>), applying the same QUOTENAME
    /// <c>']'</c>-doubling rule as <see cref="BracketEachPart"/>.
    /// </summary>
    private static string BracketRequiredParts(string identifier)
    {
        var parts = identifier.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            // Already bracketed — leave as-is.
            if (part.StartsWith("[", System.StringComparison.Ordinal) &&
                part.EndsWith("]", System.StringComparison.Ordinal))
                continue;
            if (NeedsBracketing(part))
                parts[i] = "[" + part.Replace("]", "]]") + "]";
        }
        return string.Join(".", parts);
    }

    /// <summary>
    /// Regular-identifier pattern per T-SQL rules: first char a letter, <c>_</c>,
    /// <c>@</c>, or <c>#</c>; subsequent chars letters, digits, <c>_</c>, <c>@</c>,
    /// <c>#</c>, or <c>$</c>.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex RegularIdentifier =
        new(@"^[A-Za-z_@#][A-Za-z0-9_@#$]*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Returns true when <paramref name="name"/> is NOT a valid regular identifier
    /// (e.g. contains spaces, hyphens, leads with a digit, or other special chars)
    /// OR is a T-SQL reserved word — i.e. it must be bracketed to be valid.
    /// </summary>
    private static bool NeedsBracketing(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!RegularIdentifier.IsMatch(name)) return true;
        return ReservedWords.Contains(name);
    }

    /// <summary>
    /// T-SQL reserved keywords that cannot be used as an unquoted identifier.
    /// (ISO/Transact-SQL reserved word list — not the full keyword set.)
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> ReservedWords =
        new(System.StringComparer.OrdinalIgnoreCase)
        {
            "ADD", "ALL", "ALTER", "AND", "ANY", "AS", "ASC", "AUTHORIZATION",
            "BACKUP", "BEGIN", "BETWEEN", "BREAK", "BROWSE", "BULK", "BY",
            "CASCADE", "CASE", "CHECK", "CHECKPOINT", "CLOSE", "CLUSTERED",
            "COALESCE", "COLLATE", "COLUMN", "COMMIT", "COMPUTE", "CONSTRAINT",
            "CONTAINS", "CONTAINSTABLE", "CONTINUE", "CONVERT", "CREATE", "CROSS",
            "CURRENT", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP",
            "CURRENT_USER", "CURSOR", "DATABASE", "DBCC", "DEALLOCATE", "DECLARE",
            "DEFAULT", "DELETE", "DENY", "DESC", "DISK", "DISTINCT", "DISTRIBUTED",
            "DOUBLE", "DROP", "DUMP", "ELSE", "END", "ERRLVL", "ESCAPE", "EXCEPT",
            "EXEC", "EXECUTE", "EXISTS", "EXIT", "EXTERNAL", "FETCH", "FILE",
            "FILLFACTOR", "FOR", "FOREIGN", "FREETEXT", "FREETEXTTABLE", "FROM",
            "FULL", "FUNCTION", "GOTO", "GRANT", "GROUP", "HAVING", "HOLDLOCK",
            "IDENTITY", "IDENTITY_INSERT", "IDENTITYCOL", "IF", "IN", "INDEX",
            "INNER", "INSERT", "INTERSECT", "INTO", "IS", "JOIN", "KEY", "KILL",
            "LEFT", "LIKE", "LINENO", "LOAD", "MERGE", "NATIONAL", "NOCHECK",
            "NONCLUSTERED", "NOT", "NULL", "NULLIF", "OF", "OFF", "OFFSETS", "ON",
            "OPEN", "OPENDATASOURCE", "OPENQUERY", "OPENROWSET", "OPENXML",
            "OPTION", "OR", "ORDER", "OUTER", "OVER", "PERCENT", "PIVOT", "PLAN",
            "PRECISION", "PRIMARY", "PRINT", "PROC", "PROCEDURE", "PUBLIC",
            "RAISERROR", "READ", "READTEXT", "RECONFIGURE", "REFERENCES",
            "REPLICATION", "RESTORE", "RESTRICT", "RETURN", "REVERT", "REVOKE",
            "RIGHT", "ROLLBACK", "ROWCOUNT", "ROWGUIDCOL", "RULE", "SAVE",
            "SCHEMA", "SECURITYAUDIT", "SELECT", "SEMANTICKEYPHRASETABLE",
            "SEMANTICSIMILARITYDETAILSTABLE", "SEMANTICSIMILARITYTABLE",
            "SESSION_USER", "SET", "SETUSER", "SHUTDOWN", "SOME", "STATISTICS",
            "SYSTEM_USER", "TABLE", "TABLESAMPLE", "TEXTSIZE", "THEN", "TO", "TOP",
            "TRAN", "TRANSACTION", "TRIGGER", "TRUNCATE", "TRY_CONVERT", "TSEQUAL",
            "UNION", "UNIQUE", "UNPIVOT", "UPDATE", "UPDATETEXT", "USE", "USER",
            "VALUES", "VARYING", "VIEW", "WAITFOR", "WHEN", "WHERE", "WHILE",
            "WITH", "WITHIN", "WRITETEXT"
        };

    /// <summary>Strips square brackets from each dot-separated part of an identifier.</summary>
    private static string StripBracketsEachPart(string identifier)
    {
        var parts = identifier.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.StartsWith("[", System.StringComparison.Ordinal) &&
                part.EndsWith("]", System.StringComparison.Ordinal) &&
                part.Length >= 2)
            {
                parts[i] = part.Substring(1, part.Length - 2);
            }
        }
        return string.Join(".", parts);
    }

    private static string FormatRowCount(long count)
    {
        return count switch
        {
            >= 1_000_000_000 => $"{count / 1_000_000_000.0:F1}B",
            >= 1_000_000 => $"{count / 1_000_000.0:F1}M",
            >= 1_000 => $"{count / 1_000.0:F1}K",
            _ => count.ToString()
        };
    }
}
