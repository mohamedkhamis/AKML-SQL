#nullable enable

using System.Text;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Heavyweight Split Table operation — generates a DDL script that:
///   1. Creates a new table with the source table's PK column(s) plus selected columns
///   2. Adds a foreign key constraint on the new table referencing the source
///   3. Migrates data from the source table into the new table
///   4. Drops the moved columns from the source table
///
/// This is a "generate script" operation: <see cref="PreviewAsync"/> produces the full
/// DDL as <see cref="RefactorChangeInfo"/> items for display in the preview dialog.
/// The user is expected to review and execute the generated script manually.
/// </summary>
public class SplitTableOperation : HeavyweightOperationBase
{
    public override Task<RefactorPreviewResponse> PreviewAsync(
        RefactorPreviewRequest request,
        RefactoringContext ctx,
        CancellationToken ct)
    {
        // ── Validate inputs ─────────────────────────────────────────────

        if (ctx.SchemaCache is null)
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["Schema cache is unavailable. Connect to a database before using Split Table."]
            });
        }

        if (string.IsNullOrWhiteSpace(request.OriginalIdentifier))
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["OriginalIdentifier (source table) must not be empty."]
            });
        }

        if (string.IsNullOrWhiteSpace(request.NewName))
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["NewName (target table) must not be empty."]
            });
        }

        // Parse schema.table from OriginalIdentifier (default schema = dbo)
        var (sourceSchema, sourceTable) = ParseSchemaAndName(request.OriginalIdentifier);

        var sourceObj = ctx.SchemaCache.FindObject(sourceSchema, sourceTable);
        if (sourceObj is null)
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = [$"Table [{sourceSchema}].[{sourceTable}] not found in schema cache."]
            });
        }

        if (sourceObj.ObjectType != DbObjectType.Table)
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = [$"[{sourceSchema}].[{sourceTable}] is a {sourceObj.ObjectType}, not a table."]
            });
        }

        if (!sourceObj.ColumnsLoaded || sourceObj.Columns.Count == 0)
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = [$"Column metadata for [{sourceSchema}].[{sourceTable}] has not been loaded yet. Wait for schema loading to complete."]
            });
        }

        // Column names to move — passed as comma-separated values via AdditionalFilePaths
        var columnNamesToMove = ParseColumnNames(request.AdditionalFilePaths);
        if (columnNamesToMove.Length == 0)
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["No columns specified to move. Pass column names as comma-separated values in AdditionalFilePaths."]
            });
        }

        // Resolve columns from the source table
        var pkColumns  = sourceObj.Columns.Where(c => c.IsPrimaryKey).ToList();
        var allColumns = sourceObj.Columns;

        // Validate that selected column names actually exist
        var warnings        = new List<string>();
        var columnsToMove   = new List<Column>();
        var pkColumnNames   = new HashSet<string>(pkColumns.Select(c => c.ColumnName), StringComparer.OrdinalIgnoreCase);

        foreach (var name in columnNamesToMove)
        {
            var col = allColumns.FirstOrDefault(c => string.Equals(c.ColumnName, name, StringComparison.OrdinalIgnoreCase));
            if (col is null)
            {
                warnings.Add($"Column '{name}' not found in [{sourceSchema}].[{sourceTable}] — skipped.");
                continue;
            }

            if (pkColumnNames.Contains(name))
            {
                warnings.Add($"Column '{name}' is part of the primary key and cannot be moved — skipped.");
                continue;
            }

            if (col.IsIdentity)
            {
                warnings.Add($"Column '{name}' is an IDENTITY column and cannot be moved — skipped.");
                continue;
            }

            if (col.IsComputed)
            {
                warnings.Add($"Column '{name}' is a computed column and cannot be moved — skipped.");
                continue;
            }

            columnsToMove.Add(col);
        }

        if (columnsToMove.Count == 0)
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["No valid columns remain after validation. PK, IDENTITY, and computed columns cannot be moved."],
                Warnings = warnings.ToArray()
            });
        }

        if (pkColumns.Count == 0)
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = [$"Source table [{sourceSchema}].[{sourceTable}] has no primary key. A primary key is required for Split Table."],
                Warnings = warnings.ToArray()
            });
        }

        // ── Parse target schema.table from NewName ──────────────────────

        var (targetSchema, targetTable) = ParseSchemaAndName(request.NewName);

        // ── Build the DDL script ────────────────────────────────────────

        var fkName       = $"FK_{targetTable}_{sourceTable}";
        var changes      = new List<RefactorChangeInfo>();
        var scriptBlocks = new List<string>();

        // Step 1: CREATE TABLE for the new table
        var createSb = new StringBuilder();
        createSb.AppendLine($"CREATE TABLE [{targetSchema}].[{targetTable}]");
        createSb.AppendLine("(");

        // PK columns first (copied from source)
        for (int i = 0; i < pkColumns.Count; i++)
        {
            var pk = pkColumns[i];
            createSb.Append($"    [{pk.ColumnName}] {pk.TypeDisplay}");
            createSb.Append(" NOT NULL");
            createSb.AppendLine(",");
        }

        // Then the moved columns
        for (int i = 0; i < columnsToMove.Count; i++)
        {
            var col = columnsToMove[i];
            createSb.Append($"    [{col.ColumnName}] {col.TypeDisplay}");
            createSb.Append(col.IsNullable ? " NULL" : " NOT NULL");
            if (!string.IsNullOrEmpty(col.DefaultValue))
            {
                createSb.Append($" DEFAULT {col.DefaultValue}");
            }
            createSb.AppendLine(",");
        }

        // PK constraint on the new table (same PK columns as source)
        var pkColList = string.Join(", ", pkColumns.Select(c => $"[{c.ColumnName}]"));
        createSb.AppendLine($"    CONSTRAINT [PK_{targetTable}] PRIMARY KEY CLUSTERED ({pkColList})");
        createSb.AppendLine(");");

        var createText = createSb.ToString();
        scriptBlocks.Add(createText);

        changes.Add(new RefactorChangeInfo
        {
            FilePath       = string.Empty,
            StartOffset    = 0,
            EndOffset      = 0,
            OldText        = string.Empty,
            NewText        = createText,
            Line           = 1,
            Column         = 1,
            ContextSnippet = $"Create new table [{targetSchema}].[{targetTable}] with PK + {columnsToMove.Count} column(s) from [{sourceSchema}].[{sourceTable}]",
            ChangeCategory = ChangeCategory.Structure
        });

        // Step 2: ALTER TABLE to add FK constraint
        var fkColList = string.Join(", ", pkColumns.Select(c => $"[{c.ColumnName}]"));
        var alterFkSb = new StringBuilder();
        alterFkSb.AppendLine();
        alterFkSb.AppendLine($"ALTER TABLE [{targetSchema}].[{targetTable}]");
        alterFkSb.AppendLine($"    ADD CONSTRAINT [{fkName}]");
        alterFkSb.AppendLine($"    FOREIGN KEY ({fkColList})");
        alterFkSb.AppendLine($"    REFERENCES [{sourceSchema}].[{sourceTable}] ({pkColList});");

        var alterFkText = alterFkSb.ToString();
        scriptBlocks.Add(alterFkText);

        changes.Add(new RefactorChangeInfo
        {
            FilePath       = string.Empty,
            StartOffset    = 0,
            EndOffset      = 0,
            OldText        = string.Empty,
            NewText        = alterFkText,
            Line           = 1,
            Column         = 1,
            ContextSnippet = $"Add foreign key [{fkName}] on [{targetSchema}].[{targetTable}] referencing [{sourceSchema}].[{sourceTable}]",
            ChangeCategory = ChangeCategory.Structure
        });

        // Step 3: INSERT INTO to migrate data
        var insertColList  = string.Join(", ", pkColumns.Select(c => $"[{c.ColumnName}]")
            .Concat(columnsToMove.Select(c => $"[{c.ColumnName}]")));
        var selectColList  = string.Join(", ", pkColumns.Select(c => $"[{c.ColumnName}]")
            .Concat(columnsToMove.Select(c => $"[{c.ColumnName}]")));

        var insertSb = new StringBuilder();
        insertSb.AppendLine();
        insertSb.AppendLine($"INSERT INTO [{targetSchema}].[{targetTable}] ({insertColList})");
        insertSb.AppendLine($"SELECT {selectColList}");
        insertSb.AppendLine($"FROM [{sourceSchema}].[{sourceTable}];");

        var insertText = insertSb.ToString();
        scriptBlocks.Add(insertText);

        changes.Add(new RefactorChangeInfo
        {
            FilePath       = string.Empty,
            StartOffset    = 0,
            EndOffset      = 0,
            OldText        = string.Empty,
            NewText        = insertText,
            Line           = 1,
            Column         = 1,
            ContextSnippet = $"Migrate data: copy PK + {columnsToMove.Count} column(s) from [{sourceSchema}].[{sourceTable}] into [{targetSchema}].[{targetTable}]",
            ChangeCategory = ChangeCategory.Structure
        });

        // Step 4: ALTER TABLE to drop moved columns from source
        var dropSb = new StringBuilder();
        dropSb.AppendLine();
        foreach (var col in columnsToMove)
        {
            dropSb.AppendLine($"ALTER TABLE [{sourceSchema}].[{sourceTable}] DROP COLUMN [{col.ColumnName}];");
        }

        var dropText = dropSb.ToString();
        scriptBlocks.Add(dropText);

        changes.Add(new RefactorChangeInfo
        {
            FilePath       = string.Empty,
            StartOffset    = 0,
            EndOffset      = 0,
            OldText        = string.Empty,
            NewText        = dropText,
            Line           = 1,
            Column         = 1,
            ContextSnippet = $"Drop {columnsToMove.Count} moved column(s) from source table [{sourceSchema}].[{sourceTable}]",
            ChangeCategory = ChangeCategory.Structure
        });

        // Add a warning about dependent objects
        warnings.Add("Review dependent views, stored procedures, and functions before executing this script.");
        if (sourceObj.ApproxRowCount > 100_000)
        {
            warnings.Add($"Source table has ~{sourceObj.ApproxRowCount:N0} rows. The INSERT may take significant time — consider batching for large tables.");
        }

        return Task.FromResult(new RefactorPreviewResponse
        {
            CanApply             = true,
            Changes              = changes.ToArray(),
            Warnings             = warnings.ToArray(),
            Errors               = [],
            GeneratedObjectTexts = scriptBlocks.ToArray()
        });
    }

    public override Task<RefactorApplyResponse> ApplyAsync(
        RefactorApplyRequest request,
        CancellationToken ct)
    {
        // Split Table is a "generate script" operation — the preview dialog presents
        // the full DDL script for the user to review and execute manually.
        // ApplyAsync concatenates the approved changes into a single script that
        // replaces the current document text.
        var sb = new StringBuilder();
        foreach (var change in request.ApprovedChanges)
        {
            sb.Append(change.NewText);
        }

        return Task.FromResult(new RefactorApplyResponse
        {
            Success             = true,
            AppliedCount        = request.ApprovedChanges.Length,
            UpdatedDocumentText = sb.ToString()
        });
    }

    // ── Private helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Splits a potentially schema-qualified identifier ("dbo.Customers" or just "Customers")
    /// into (schema, name). Strips surrounding brackets if present. Defaults schema to "dbo".
    /// </summary>
    private static (string schema, string name) ParseSchemaAndName(string identifier)
    {
        var stripped = identifier.Trim();

        // Bracket-aware split: don't split on dots inside [...] brackets.
        // Handles [dbo].[My.Table], [schema].[name], and unbracketed schema.name.
        var parts = SplitBracketAware(stripped);

        if (parts.Count >= 3)
        {
            // Three-part name (server.schema.table or catalog.schema.table) — use last two
            return (StripBrackets(parts[parts.Count - 2]), StripBrackets(parts[parts.Count - 1]));
        }

        if (parts.Count == 2)
            return (StripBrackets(parts[0]), StripBrackets(parts[1]));

        return ("dbo", StripBrackets(parts[0]));
    }

    /// <summary>
    /// Splits a T-SQL identifier on dots, respecting bracket-delimited sections.
    /// "[dbo].[My.Table]" splits to ["[dbo]", "[My.Table]"], not ["[dbo]", "[My", "Table]"].
    /// </summary>
    private static List<string> SplitBracketAware(string identifier)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inBracket = false;

        foreach (var ch in identifier)
        {
            if (ch == '[' && !inBracket) { inBracket = true; current.Append(ch); }
            else if (ch == ']' && inBracket) { inBracket = false; current.Append(ch); }
            else if (ch == '.' && !inBracket) { result.Add(current.ToString()); current.Clear(); }
            else { current.Append(ch); }
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    private static string StripBrackets(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '[' && s[^1] == ']')
            return s[1..^1];
        return s;
    }

    /// <summary>
    /// Extracts column names from <see cref="RefactorPreviewRequest.AdditionalFilePaths"/>.
    /// The first element is expected to contain comma-separated column names.
    /// Additional elements are also treated as individual column names.
    /// </summary>
    private static string[] ParseColumnNames(string[] additionalFilePaths)
    {
        if (additionalFilePaths is null || additionalFilePaths.Length == 0)
            return [];

        var names = new List<string>();
        foreach (var entry in additionalFilePaths)
        {
            if (string.IsNullOrWhiteSpace(entry))
                continue;

            // Each entry may be comma-separated
            foreach (var part in entry.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0)
                    names.Add(StripBrackets(trimmed));
            }
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
