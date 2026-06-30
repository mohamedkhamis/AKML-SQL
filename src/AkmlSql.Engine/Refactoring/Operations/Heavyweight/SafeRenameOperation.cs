using System.Text;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using Microsoft.Data.SqlClient;
using Serilog;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Heavyweight Safe Rename operation — renames all references to an identifier
/// across the current script or project directory.
/// <para>
/// Spec 030 / FR-018 / R8 adds a third scope, <see cref="RefactorScope.Database"/>: database-wide
/// Smart Rename. On that path the operation requires a live connection (mirrors
/// <c>InlineStoredProcOperation</c>), enumerates the referencing modules via
/// <see cref="DatabaseRenameDependencyReader"/>, and returns a reviewable
/// <c>sp_rename</c> + per-dependent <c>ALTER</c> script built by the pure
/// <see cref="DatabaseRenameScriptBuilder"/> in
/// <see cref="RefactorPreviewResponse.GeneratedObjectTexts"/>.
/// </para>
/// </summary>
public class SafeRenameOperation : HeavyweightOperationBase
{
    public override async Task<RefactorPreviewResponse> PreviewAsync(
        RefactorPreviewRequest request,
        RefactoringContext ctx,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OriginalIdentifier))
        {
            return new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["OriginalIdentifier must not be empty"]
            };
        }

        if (string.IsNullOrWhiteSpace(request.NewName))
        {
            return new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["NewName must not be empty"]
            };
        }

        // ── Spec 030 / FR-018 / R8: database-wide Smart Rename ──────────────
        if ((RefactorScope)request.Scope == RefactorScope.Database)
        {
            return await PreviewDatabaseWideAsync(request, ctx, ct);
        }

        var allChanges  = new List<RefactorChangeInfo>();
        var warnings    = new List<string>();

        var documentText = ctx.DocumentText ?? string.Empty;

        if (!string.IsNullOrEmpty(documentText))
        {
            var parser = new TsqlParserService();
            var script = parser.Parse(documentText, out _);

            if (script != null)
            {
                var collector = new ReferenceCollector(request.OriginalIdentifier, string.Empty, documentText);
                script.Accept(collector);
                allChanges.AddRange(BuildChanges(collector.Matches, request.NewName));

                // Collision check: NewName must not already exist in this scope
                var collisionCollector = new ReferenceCollector(request.NewName, string.Empty, documentText);
                script.Accept(collisionCollector);
                if (collisionCollector.Matches.Count > 0)
                {
                    return new RefactorPreviewResponse
                    {
                        CanApply = false,
                        Errors   = [$"Name collision: '{request.NewName}' already exists in this scope"],
                        Changes  = []
                    };
                }

                if (ctx.Settings?.IncludeCommentsInRename == true)
                {
                    var tokens = parser.GetTokenStream(documentText);
                    allChanges.AddRange(SearchTokensForIdentifier(
                        tokens, request.OriginalIdentifier, request.NewName, documentText, string.Empty));
                }
            }
        }

        if ((RefactorScope)request.Scope == RefactorScope.ProjectDirectory)
        {
            var dir = string.IsNullOrEmpty(request.DocumentPath)
                ? string.Empty
                : Path.GetDirectoryName(request.DocumentPath) ?? string.Empty;

            var sqlFiles = string.IsNullOrEmpty(dir)
                ? Array.Empty<string>()
                : Directory.GetFiles(dir, "*.sql", SearchOption.AllDirectories);

            foreach (var filePath in sqlFiles)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var fileText = File.ReadAllText(filePath);
                    var fileParser = new TsqlParserService();
                    var fileScript = fileParser.Parse(fileText, out _);
                    if (fileScript == null) continue;

                    var fileCollector = new ReferenceCollector(request.OriginalIdentifier, filePath, fileText);
                    fileScript.Accept(fileCollector);
                    allChanges.AddRange(BuildChanges(fileCollector.Matches, request.NewName));

                    if (ctx.Settings?.IncludeCommentsInRename == true)
                    {
                        var fileTokens = fileParser.GetTokenStream(fileText);
                        allChanges.AddRange(SearchTokensForIdentifier(
                            fileTokens, request.OriginalIdentifier, request.NewName, fileText, filePath));
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not process '{filePath}': {ex.Message}");
                }
            }
        }

        var sorted = allChanges
            .OrderBy(c => c.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(c => c.StartOffset)
            .ToArray();

        return new RefactorPreviewResponse
        {
            Changes  = sorted,
            Warnings = warnings.ToArray(),
            Errors   = [],
            CanApply = true
        };
    }

    /// <summary>
    /// Spec 030 / FR-018 / R8 — the database-wide Smart Rename preview. Requires a live connection
    /// (refuses with a friendly message when absent, mirroring <c>InlineStoredProcOperation</c>),
    /// enumerates referencing modules via <see cref="DatabaseRenameDependencyReader"/>, and returns a
    /// reviewable <c>sp_rename</c> + per-dependent <c>ALTER</c> script in
    /// <see cref="RefactorPreviewResponse.GeneratedObjectTexts"/>. <see cref="RefactorPreviewResponse.Changes"/>
    /// carries one entry per affected dependent so the preview dialog can list them.
    /// </summary>
    private static async Task<RefactorPreviewResponse> PreviewDatabaseWideAsync(
        RefactorPreviewRequest request,
        RefactoringContext ctx,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.ConnectionString))
        {
            return new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["Database-wide Smart Rename needs a live database connection so it can find every referencing object. Connect the query window to a database and try again."]
            };
        }

        // OriginalIdentifier may be schema-qualified ("schema.name") or bare; the parent table for a
        // column rename is carried in ExtractedUnitName (the shell sets it only for a column rename).
        var (schema, name) = SplitQualified(request.OriginalIdentifier);
        var parentTableHint = string.IsNullOrWhiteSpace(request.ExtractedUnitName) ? null : request.ExtractedUnitName;

        try
        {
            // targetDatabase is null: the session's connection string is already pointed at the
            // database the user is working in, so the reader's ChangeDatabase is an intentional no-op.
            var reader = new DatabaseRenameDependencyReader();
            var plan = await reader.BuildPlanAsync(
                ctx.ConnectionString!,
                targetDatabase: null,
                schema,
                name,
                request.NewName,
                parentTableHint,
                ct);

            if (!plan.Resolved)
            {
                var unresolved = parentTableHint != null
                    ? $"{schema}.{parentTableHint}.{name}"
                    : $"{schema}.{name}";
                return new RefactorPreviewResponse
                {
                    CanApply = false,
                    Errors   = [$"Couldn't resolve '{unresolved}' as an object or column in the connected database. " +
                                "Place the cursor on a real table/view/procedure/function name, or on a column qualified by its table (schema.table.column), not an alias."]
                };
            }

            var script = DatabaseRenameScriptBuilder.BuildRenameScript(plan.Target, plan.Dependents);

            // The preview dialog lists Changes as a checkbox tree and only enables its Generate button
            // when at least one change is present (ApprovedChanges.Length > 0). A valid object rename with
            // ZERO dependents has no dependent rows — so ALWAYS emit a leading change for the rename
            // TARGET ITSELF (the sp_rename). Without it the dialog's Generate button stays disabled and a
            // zero-dependent rename dies silently even though GeneratedObjectTexts has a real script.
            var targetDisplay = plan.Target.IsColumn
                ? $"{plan.Target.Schema}.{plan.Target.ParentTable}.{plan.Target.Name}"
                : $"{plan.Target.Schema}.{plan.Target.Name}";

            var changes = new List<RefactorChangeInfo>
            {
                new()
                {
                    FilePath       = $"Rename: {targetDisplay}",
                    StartOffset    = 0,
                    EndOffset      = 0,
                    OldText        = plan.Target.Name,
                    NewText        = request.NewName,
                    Line           = 0,
                    Column         = 0,
                    ContextSnippet = $"sp_rename {targetDisplay} → {request.NewName}",
                    ChangeCategory = ChangeCategory.Rename
                }
            };

            // One change row per affected dependent, so the preview dialog lists what will be ALTERed.
            changes.AddRange(plan.Dependents.Select(d => new RefactorChangeInfo
            {
                FilePath       = $"{d.Schema}.{d.Name}",
                StartOffset    = 0,
                EndOffset      = 0,
                OldText        = name,
                NewText        = request.NewName,
                Line           = 0,
                Column         = 0,
                ContextSnippet = $"{d.Schema}.{d.Name}",
                ChangeCategory = ChangeCategory.Rename
            }));

            return new RefactorPreviewResponse
            {
                CanApply             = true,
                Changes              = changes.ToArray(),
                Warnings             = [],
                Errors               = [],
                GeneratedObjectTexts = [script]
            };
        }
        catch (SqlException ex) when (DatabaseRenameDependencyReader.IsPermissionDenied(ex))
        {
            Log.Warning(ex, "Database-wide Smart Rename: permission denied");
            return new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["Permission denied. VIEW DEFINITION on the database is required to find the objects that reference this one."]
            };
        }
    }

    /// <summary>
    /// Splits a possibly schema-qualified identifier into (schema, name), defaulting the schema to
    /// <c>dbo</c>. Strips bracket quoting from each part. A bare name yields ("dbo", name).
    /// </summary>
    private static (string Schema, string Name) SplitQualified(string identifier)
    {
        var id = (identifier ?? string.Empty).Trim();
        int dot = LastUnbracketedDot(id);
        string schema = "dbo", name = id;
        if (dot > 0)
        {
            schema = id.Substring(0, dot);
            name   = id.Substring(dot + 1);
        }
        return (Unbracket(schema), Unbracket(name));
    }

    /// <summary>Index of the last '.' that is NOT inside a [bracketed] segment, or -1.</summary>
    private static int LastUnbracketedDot(string s)
    {
        bool inBracket = false;
        int last = -1;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '[') inBracket = true;
            else if (s[i] == ']') inBracket = false;
            else if (s[i] == '.' && !inBracket) last = i;
        }
        return last;
    }

    private static string Unbracket(string s)
    {
        var t = (s ?? string.Empty).Trim();
        if (t.Length >= 2 && t[0] == '[' && t[t.Length - 1] == ']')
            return t.Substring(1, t.Length - 2).Replace("]]", "]");
        return t;
    }

    public override Task<RefactorApplyResponse> ApplyAsync(
        RefactorApplyRequest request,
        CancellationToken ct)
    {
        if (request.ApprovedChanges == null || request.ApprovedChanges.Length == 0)
        {
            return Task.FromResult(new RefactorApplyResponse
            {
                Success      = true,
                AppliedCount = 0
            });
        }

        var failedPaths  = new List<string>();
        var backupPaths  = new List<string>();
        var appliedCount = 0;
        var updatedDocumentText = string.Empty;

        var grouped = request.ApprovedChanges
            .GroupBy(c => c.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            ct.ThrowIfCancellationRequested();

            var filePath = group.Key;
            var changes  = group.OrderByDescending(c => c.StartOffset).ToList();

            if (string.IsNullOrEmpty(filePath))
            {
                // Shell applies changes directly to its buffer; UpdatedDocumentText stays empty.
                appliedCount += changes.Count;
            }
            else
            {
                if (!File.Exists(filePath))
                {
                    failedPaths.Add(filePath);
                    continue;
                }

                try
                {
                    var fileText = File.ReadAllText(filePath);

                    // Stale-file guard: if offsets no longer match, the file changed after preview.
                    var allValid = changes.All(ch =>
                        ch.StartOffset >= 0
                        && ch.EndOffset <= fileText.Length
                        && fileText.Substring(ch.StartOffset, ch.EndOffset - ch.StartOffset) == ch.OldText);

                    if (!allValid)
                    {
                        failedPaths.Add(filePath);
                        continue;
                    }

                    if (request.CreateBackups)
                    {
                        var backupDir = Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, ".refactor-backup");
                        Directory.CreateDirectory(backupDir);
                        var backupPath = Path.Combine(backupDir,
                            Path.GetFileNameWithoutExtension(filePath)
                            + $"_{DateTime.UtcNow:yyyyMMddHHmmss}"
                            + Path.GetExtension(filePath));
                        File.Copy(filePath, backupPath, overwrite: true);
                        backupPaths.Add(backupPath);
                    }

                    var updatedText = ApplyChangesToText(fileText, changes);
                    File.WriteAllText(filePath, updatedText, Encoding.UTF8);
                    appliedCount += changes.Count;
                }
                catch (Exception)
                {
                    failedPaths.Add(filePath);
                }
            }
        }

        return Task.FromResult(new RefactorApplyResponse
        {
            Success             = failedPaths.Count == 0,
            AppliedCount        = appliedCount,
            FailedFilePaths     = failedPaths.ToArray(),
            BackupFilePaths     = backupPaths.ToArray(),
            UpdatedDocumentText = updatedDocumentText
        });
    }

    private static IEnumerable<RefactorChangeInfo> BuildChanges(
        IReadOnlyList<ReferenceMatch> matches,
        string newName)
    {
        foreach (var m in matches)
        {
            // Preserve bracket quoting if the original text was bracketed
            string newText = newName;
            if (m.MatchedText.StartsWith("[", StringComparison.Ordinal) && !newName.StartsWith("[", StringComparison.Ordinal))
                newText = $"[{newName}]";

            yield return new RefactorChangeInfo
            {
                FilePath       = m.FilePath,
                StartOffset    = m.StartOffset,
                EndOffset      = m.EndOffset,
                OldText        = m.MatchedText,
                NewText        = newText,
                Line           = m.Line,
                Column         = m.Column,
                ContextSnippet = m.ContextSnippet,
                ChangeCategory = ChangeCategory.Rename
            };
        }
    }

    private static IEnumerable<RefactorChangeInfo> SearchTokensForIdentifier(
        IList<Microsoft.SqlServer.TransactSql.ScriptDom.TSqlParserToken> tokens,
        string targetName,
        string newName,
        string documentText,
        string filePath)
    {
        foreach (var token in tokens)
        {
            if (token.TokenType != Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.SingleLineComment
                && token.TokenType != Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.MultilineComment)
                continue;

            var text = token.Text ?? string.Empty;
            int idx = 0;
            while (true)
            {
                idx = text.IndexOf(targetName, idx, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;

                var absStart = token.Offset + idx;
                var (line, col) = OffsetToLineCol(documentText, absStart);

                yield return new RefactorChangeInfo
                {
                    FilePath       = filePath,
                    StartOffset    = absStart,
                    EndOffset      = absStart + targetName.Length,
                    OldText        = text.Substring(idx, targetName.Length),
                    NewText        = newName,
                    Line           = line,
                    Column         = col,
                    ContextSnippet = ExtractContext(documentText, absStart),
                    ChangeCategory = ChangeCategory.Rename
                };

                idx += targetName.Length;
            }
        }
    }

}
