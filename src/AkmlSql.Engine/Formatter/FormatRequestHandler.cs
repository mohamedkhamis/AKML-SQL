using System.Collections.Concurrent;
using System.Text;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Refactoring;
using AkmlSql.Engine.Refactoring.Operations;
using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Selection;
using Serilog;

namespace AkmlSql.Engine.Formatter;

public class FormatRequestHandler(ProfileManager profileManager)
{
    private readonly FormatterPipeline _pipeline = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _bulkSessions = new();

    public FormatResponse HandleFormat(FormatRequest request)
    {
        try
        {
            var profile = LoadProfile(request.ProfileName);
            var result = _pipeline.Format(request.Text, profile);

            return new FormatResponse
            {
                Success = result.Success,
                FormattedText = result.FormattedText,
                WasModified = result.WasModified,
                ValidationPassed = result.ValidationPassed,
                ElapsedMs = result.ElapsedMs,
                Diagnostics = result.Diagnostics.Select(d => new FormatDiagnosticInfo
                {
                    Severity = (int)d.Severity,
                    Message = d.Message,
                    Offset = d.Offset,
                    Line = d.Line
                }).ToArray()
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Format request failed");
            return new FormatResponse
            {
                Success = false,
                FormattedText = request.Text,
                Diagnostics = [new FormatDiagnosticInfo { Severity = 2, Message = ex.Message }]
            };
        }
    }

    public FormatSelectionResponse HandleFormatSelection(FormatSelectionRequest request)
    {
        try
        {
            var profile = LoadProfile(request.ProfileName);
            var selFormatter = new SelectionFormatter();
            var result = selFormatter.FormatSelection(
                request.Text, request.SelectionStart, request.SelectionEnd, profile);

            return new FormatSelectionResponse
            {
                Success = result.Success,
                FormattedText = result.FormattedText,
                OriginalStart = result.OriginalStart,
                OriginalEnd = result.OriginalEnd,
                WasModified = result.WasModified,
                ValidationPassed = result.ValidationPassed,
                ElapsedMs = result.ElapsedMs
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Format selection request failed");
            return new FormatSelectionResponse { Success = false, FormattedText = request.Text };
        }
    }

    public FormatPreviewResponse HandleFormatPreview(FormatPreviewRequest request)
    {
        try
        {
            var profile = ProfileSerializer.Deserialize(request.ProfileJson);
            var result = _pipeline.Format(request.SampleText, profile);

            return new FormatPreviewResponse
            {
                FormattedText = result.FormattedText,
                ElapsedMs = result.ElapsedMs
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Format preview request failed");
            return new FormatPreviewResponse { FormattedText = request.SampleText };
        }
    }

    public FormatActionResponse HandleFormatAction(FormatActionRequest request)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var actionType = (FormatActionType)request.ActionType;

            // Actions 0-8 are handled by the formatter pipeline (existing logic)
            // Actions 9-15 are new lightweight refactoring operations
            ILightweightOperation? op = actionType switch
            {
                FormatActionType.ExpandInsertColumns    => new ExpandInsertColumnsOperation(),
                FormatActionType.ExpandExecParameters   => new ExpandExecParametersOperation(),
                FormatActionType.ExpandUpdateColumns    => new ExpandUpdateColumnsOperation(),
                FormatActionType.ConvertOldStyleJoins   => new ConvertOldStyleJoinsOperation(),
                FormatActionType.AddGroupByColumns      => new AddGroupByColumnsOperation(),
                FormatActionType.EncapsulateBeginEnd    => new EncapsulateBeginEndOperation(),
                FormatActionType.ReplaceDeprecatedSyntax => new ReplaceDeprecatedSyntaxOperation(),
                FormatActionType.ConvertSpExecutesql    => new ConvertSpExecutesqlOperation(),
                FormatActionType.Unformat               => new UnformatOperation(),
                _ => null
            };

            if (op == null)
            {
                return new FormatActionResponse
                {
                    Success = false,
                    FormattedText = request.Text,
                    ErrorMessage = $"Format action type {request.ActionType} is not supported here."
                };
            }

            // Build a minimal RefactoringContext (no schema cache, no session for lightweight ops)
            var parser = new Parser.TsqlParserService();
            var ctx = new RefactoringContext
            {
                DocumentText    = request.Text,
                Script          = parser.Parse(request.Text, out _) ?? new Microsoft.SqlServer.TransactSql.ScriptDom.TSqlScript(),
                Tokens          = parser.GetTokenStream(request.Text),
                SelectionStart  = request.SelectionStart,
                SelectionLength = request.SelectionLength,
                SessionId       = request.SessionId ?? string.Empty
            };

            var (modifiedText, warnings) = op.Apply(ctx);
            sw.Stop();

            return new FormatActionResponse
            {
                Success      = true,
                FormattedText = modifiedText,
                WasModified  = !string.Equals(modifiedText, request.Text, StringComparison.Ordinal),
                ElapsedMs    = sw.ElapsedMilliseconds,
                Warnings     = warnings.Length > 0 ? warnings : null
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HandleFormatAction failed for ActionType={ActionType}", request.ActionType);
            return new FormatActionResponse
            {
                Success       = false,
                FormattedText = request.Text,
                ErrorMessage  = ex.Message
            };
        }
    }

    public ProfileListResponse HandleProfileList()
    {
        try
        {
            var profiles = profileManager.List();
            return new ProfileListResponse
            {
                Profiles = profiles.Select(m => new ProfileInfo
                {
                    Name = m.Name,
                    Description = m.Description,
                    Author = m.Author,
                    IsBuiltIn = m.IsBuiltIn,
                    BasedOn = m.BasedOn,
                    Modified = m.Modified.ToString("o")
                }).ToArray()
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Profile list request failed");
            return new ProfileListResponse { Profiles = [] };
        }
    }

    public ProfileSaveResponse HandleProfileSave(ProfileSaveRequest request)
    {
        try
        {
            var profile = ProfileSerializer.Deserialize(request.ProfileJson);
            profileManager.Save(profile);
            return new ProfileSaveResponse { Success = true };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Profile save failed");
            return new ProfileSaveResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    public ProfileDeleteResponse HandleProfileDelete(ProfileDeleteRequest request)
    {
        try
        {
            profileManager.Delete(request.Name);
            return new ProfileDeleteResponse { Success = true };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Profile delete failed");
            return new ProfileDeleteResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<BulkFormatReportResponse> HandleBulkFormatAsync(BulkFormatRequest request)
    {
        try
        {
            // Validate file paths — all must be absolute and not contain traversal sequences
            foreach (var path in request.FilePaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    throw new ArgumentException("Bulk format file paths must not be empty.");
                if (!Path.IsPathFullyQualified(path))
                    throw new ArgumentException($"Bulk format file path must be absolute: '{path}'");
                // Resolve to canonical form to catch traversal sequences (e.g. foo\..\secret)
                var normalized = path.Replace('/', Path.DirectorySeparatorChar);
                var canonical = Path.GetFullPath(normalized);
                if (!string.Equals(canonical, normalized, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Bulk format file path is not canonical (possible path traversal): '{path}'");
            }

            var profile = LoadProfile(request.ProfileName);
            var bulkFormatter = new BulkFormatter();
            var options = new BulkFormatOptions
            {
                ProfileName = request.ProfileName ?? "Default",
                CreateBackups = request.CreateBackups,
                PreviewOnly = request.DryRun,
                SkipParseErrors = true,
                RespectNoformat = true,
                MaxParallelism = Environment.ProcessorCount
            };

            var cts = new CancellationTokenSource();
            _bulkSessions[request.SessionId] = cts;

            try
            {
                var report = await bulkFormatter.FormatFilesAsync(
                    request.FilePaths, profile, options, progress: null, ct: cts.Token);

                return new BulkFormatReportResponse
                {
                    SessionId = request.SessionId,
                    TotalFiles = report.TotalFiles,
                    SuccessCount = report.FormattedCount,
                    FailedCount = report.ParseErrorCount + report.ErrorCount,
                    SkippedCount = report.SkippedCount + report.AlreadyFormattedCount,
                    ElapsedMs = report.ElapsedMs,
                    Results = report.Details.Select(d => new FileResult
                    {
                        FilePath = d.FilePath,
                        Status = (int)d.Status,
                        LinesChanged = d.LinesChanged,
                        ErrorMessage = d.ErrorMessage
                    }).ToArray()
                };
            }
            finally
            {
                _bulkSessions.TryRemove(request.SessionId, out _);
                cts.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // Parallel.ForEachAsync throws OCE but individual file results are
            // already collected in the ConcurrentBag. Return whatever was completed.
            Log.Information("Bulk format cancelled for session {SessionId}", request.SessionId);
            return new BulkFormatReportResponse
            {
                SessionId = request.SessionId,
                TotalFiles = request.FilePaths.Length,
                SuccessCount = 0,
                FailedCount = 0,
                SkippedCount = request.FilePaths.Length,
                Results = []
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bulk format request failed");
            return new BulkFormatReportResponse
            {
                SessionId = request.SessionId,
                TotalFiles = request.FilePaths.Length,
                Results = []
            };
        }
    }

    public void HandleBulkFormatCancel(BulkFormatCancelRequest request)
    {
        if (_bulkSessions.TryGetValue(request.SessionId, out var cts))
        {
            Log.Information("Cancelling bulk format session {SessionId}", request.SessionId);
            cts.Cancel();
        }
    }

    public ProfileImportResponse HandleProfileImport(ProfileImportRequest request)
    {
        try
        {
            var sourceFormat = request.SourceFormat.ToLowerInvariant();
            var content = Encoding.UTF8.GetString(request.FileContent);

            if (sourceFormat is "sqlprompt" or "sqlpromptstylev2")
            {
                var importResult = SqlPromptImporter.Import(content, request.TargetProfileName);

                // Save the imported profile
                profileManager.Save(importResult.Profile);

                return new ProfileImportResponse
                {
                    Success = true,
                    MappedOptionsCount = importResult.MappedCount,
                    UnmappedOptionsCount = importResult.UnmappedCount,
                    UnmappedOptions = importResult.UnmappedOptions.ToArray()
                };
            }

            if (sourceFormat is "akmlstyle" or "akml")
            {
                // Native import — just deserialize and save
                var profile = ProfileSerializer.Deserialize(content);
                if (!string.IsNullOrWhiteSpace(request.TargetProfileName))
                {
                    profile.Metadata.BasedOn = profile.Metadata.Name;
                    profile.Metadata.Name = request.TargetProfileName;
                }
                profile.Metadata.Id = Guid.NewGuid().ToString();
                profile.Metadata.IsBuiltIn = false;
                profileManager.Save(profile);

                return new ProfileImportResponse
                {
                    Success = true,
                    MappedOptionsCount = -1, // Not applicable for native format
                    UnmappedOptionsCount = 0
                };
            }

            return new ProfileImportResponse
            {
                Success = false,
                ErrorMessage = $"Unsupported import format: '{request.SourceFormat}'. Supported: sqlprompt, akmlstyle"
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Profile import failed");
            return new ProfileImportResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private FormattingProfile LoadProfile(string? profileName)
    {
        if (string.IsNullOrEmpty(profileName))
            return new FormattingProfile(); // Default profile with all defaults

        try
        {
            return profileManager.Load(profileName);
        }
        catch
        {
            Log.Warning("Profile {Name} not found, using default", profileName);
            return new FormattingProfile();
        }
    }
}
