using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Heavyweight Safe Rename operation — renames all references to an identifier
/// across the current script or project directory.
/// </summary>
public class SafeRenameOperation : HeavyweightOperationBase
{
    public override Task<RefactorPreviewResponse> PreviewAsync(
        RefactorPreviewRequest request,
        RefactoringContext ctx,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.OriginalIdentifier))
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["OriginalIdentifier must not be empty"]
            });
        }

        if (string.IsNullOrWhiteSpace(request.NewName))
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = false,
                Errors   = ["NewName must not be empty"]
            });
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
                    return Task.FromResult(new RefactorPreviewResponse
                    {
                        CanApply = false,
                        Errors   = [$"Name collision: '{request.NewName}' already exists in this scope"],
                        Changes  = []
                    });
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

        return Task.FromResult(new RefactorPreviewResponse
        {
            Changes  = sorted,
            Warnings = warnings.ToArray(),
            Errors   = [],
            CanApply = true
        });
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
