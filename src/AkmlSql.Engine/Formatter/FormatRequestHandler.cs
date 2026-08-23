using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Refactoring;
using AkmlSql.Engine.Refactoring.Operations;
using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Formatting.Actions;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.Selection;
using Serilog;

namespace AkmlSql.Engine.Formatter;

public class FormatRequestHandler(ProfileManager profileManager)
{
    /// <summary>One profile-payload size cap shared by save and import (mirrors
    /// SnippetRequestHandler's 1 MB server-side cap) — the two limits must never drift.</summary>
    private const int MaxProfileJsonBytes = 1024 * 1024;

    private readonly FormatterPipeline _pipeline = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _bulkSessions = new();

    public FormatResponse HandleFormat(FormatRequest request)
    {
        try
        {
            var profile = LoadProfile(request.ProfileName, out var fallbackWarning);
            var result = _pipeline.Format(request.Text, profile);

            return new FormatResponse
            {
                Success = result.Success,
                FormattedText = result.FormattedText,
                WasModified = result.WasModified,
                ValidationPassed = result.ValidationPassed,
                ElapsedMs = result.ElapsedMs,
                ProfileFallbackWarning = fallbackWarning,
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

            // Spec 020 T070 — surface stage-6 (SemanticValidator) failure to the editor so it
            // can render the "Preview unavailable — semantically-different SQL" warning bar.
            // Pipeline behaviour: validation failure returns the original SQL unchanged, sets
            // ValidationPassed=false, and adds an Error-severity diagnostic.
            string? validationError = null;
            if (!result.ValidationPassed)
            {
                var diag = Array.Find(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
                validationError = diag?.Message
                    ?? "Preview unavailable — the current settings produce semantically-different SQL.";
            }

            return new FormatPreviewResponse
            {
                FormattedText = result.FormattedText,
                ElapsedMs = result.ElapsedMs,
                ValidationError = validationError,
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Format preview request failed");
            return new FormatPreviewResponse
            {
                FormattedText = request.SampleText,
                ValidationError = $"Preview failed: {ex.Message}",
            };
        }
    }

    // Spec 030: one-arg delegator preserved for call sites without schema/session access
    // (e.g. the web edition's offline path). Schema-aware actions (ExpandWildcards /
    // QualifyObjectNames) gracefully warn "schema cache not available" under this path.
    public FormatActionResponse HandleFormatAction(FormatActionRequest request)
        => HandleFormatAction(request, null, null);

    public FormatActionResponse HandleFormatAction(
        FormatActionRequest request, SchemaCacheManager? schemaCache, SessionManager? sessions)
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var actionType = (FormatActionType)request.ActionType;

            // Spec 030: standalone format actions (types 0-8) → the IFormatAction classes. These were
            // never dispatched (only 9-17 were), so the shell's casing/semicolons/brackets/AS commands
            // returned "not supported here". ExpandWildcards/QualifyObjectNames are deliberately NOT
            // resolved here — they fall through to the schema-aware lightweight switch below.
            var formatAction = ResolveFormatAction(actionType);
            if (formatAction != null)
                return RunFormatAction(formatAction, actionType, request);

            // Actions 9-17 (plus schema-aware ExpandWildcards/QualifyObjectNames) are lightweight
            // refactoring operations.
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
                FormatActionType.ExpandWildcards        => new ExpandWildcardsOperation(),
                FormatActionType.QualifyObjectNames     => new QualifyObjectNamesOperation(),
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

            // Build the RefactoringContext. Populate the schema cache GENERICALLY for ALL lightweight
            // ops — same idiom as RefactoringEngine.BuildContext (GetCache's first param is named
            // serverName but is the SessionId by convention). Schema-independent ops simply ignore it.
            var parser = new Parser.TsqlParserService();
            DatabaseCache? cache = null;
            if (sessions != null && !string.IsNullOrEmpty(request.SessionId))
            {
                var session = sessions.GetSession(request.SessionId);
                if (session != null)
                    cache = schemaCache?.GetCache(request.SessionId, session.DatabaseName);
            }

            var ctx = new RefactoringContext
            {
                DocumentText    = request.Text,
                Script          = parser.Parse(request.Text, out _) ?? new Microsoft.SqlServer.TransactSql.ScriptDom.TSqlScript(),
                Tokens          = parser.GetTokenStream(request.Text),
                SelectionStart  = request.SelectionStart,
                SelectionLength = request.SelectionLength,
                SessionId       = request.SessionId ?? string.Empty,
                SchemaCache     = cache
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

    // Spec 030: maps a standalone format-action type to its IFormatAction implementation.
    // Returns null for the refactoring-operation types (9-17), which the caller dispatches separately.
    private static IFormatAction? ResolveFormatAction(FormatActionType type) => type switch
    {
        FormatActionType.CasingOnly           => new CasingOnlyAction(),
        FormatActionType.InsertSemicolons     => new InsertSemicolonsAction(),
        FormatActionType.RemoveSemicolons     => new RemoveSemicolonsAction(),
        // ExpandWildcards / QualifyObjectNames are intentionally absent — they are schema-aware
        // lightweight operations dispatched through the ILightweightOperation switch (spec 030).
        FormatActionType.AddSquareBrackets    => new ToggleBracketsAction(),
        FormatActionType.RemoveSquareBrackets => new ToggleBracketsAction(),
        FormatActionType.AddAsKeyword         => new ToggleAsKeywordAction(),
        FormatActionType.RemoveAsKeyword      => new ToggleAsKeywordAction(),
        _ => null
    };

    private FormatActionResponse RunFormatAction(IFormatAction action, FormatActionType actionType, FormatActionRequest request)
    {
        var profile = LoadProfile(request.ProfileName);

        // The Toggle* actions choose add-vs-remove from these profile flags. Set them to match the
        // explicit action, then restore so a shared/cached profile instance is not mutated.
        bool savedBrackets = profile.FormatActions.AddSquareBrackets;
        bool savedAs = profile.FormatActions.AddAsKeyword;
        try
        {
            if (actionType == FormatActionType.AddSquareBrackets) profile.FormatActions.AddSquareBrackets = true;
            else if (actionType == FormatActionType.RemoveSquareBrackets) profile.FormatActions.AddSquareBrackets = false;
            if (actionType == FormatActionType.AddAsKeyword) profile.FormatActions.AddAsKeyword = true;
            else if (actionType == FormatActionType.RemoveAsKeyword) profile.FormatActions.AddAsKeyword = false;

            var r = action.Execute(request.Text, profile);
            var messages = r.Diagnostics.Select(d => d.Message).ToArray();
            return new FormatActionResponse
            {
                Success       = r.Success,
                FormattedText = r.FormattedText,
                WasModified   = r.WasModified,
                ElapsedMs     = r.ElapsedMs,
                Warnings      = messages.Length > 0 ? messages : null,
                ErrorMessage  = r.Success ? null : (messages.FirstOrDefault() ?? "Format action failed")
            };
        }
        finally
        {
            profile.FormatActions.AddSquareBrackets = savedBrackets;
            profile.FormatActions.AddAsKeyword = savedAs;
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
            // Spec 033 hardening — Save previously accepted unbounded JSON from the pipe.
            if (request.ProfileJson != null && request.ProfileJson.Length > MaxProfileJsonBytes)
                return new ProfileSaveResponse { Success = false, ErrorMessage = "Profile JSON exceeds the 1 MB limit." };

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

    /// <summary>
    /// Spec 033 — Format Styles editor load-on-select. Returns the stored .akmlstyle file text
    /// VERBATIM via <see cref="ProfileManager.TryReadRaw"/> (re-serializing would bump
    /// <c>metadata.modified</c> and drop unknown nested fields), plus the directory-derived
    /// read-only flag. Never creates or modifies anything.
    /// </summary>
    public ProfileGetResponse HandleProfileGet(ProfileGetRequest request)
    {
        try
        {
            if (!profileManager.TryReadRaw(request.Name, out var json, out var isBuiltIn))
                return new ProfileGetResponse
                {
                    Success = false,
                    ErrorMessage = $"Profile '{request.Name}' was not found."
                };

            return new ProfileGetResponse
            {
                Success = true,
                Name = request.Name,
                ProfileJson = json,
                IsBuiltIn = isBuiltIn
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Profile get failed ({Name})", request.Name);
            return new ProfileGetResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    public ProfileDeleteResponse HandleProfileDelete(ProfileDeleteRequest request)
    {
        try
        {
            // Spec 033 fix — Delete's bool was previously discarded, so deleting a
            // nonexistent profile reported Success=true.
            var deleted = profileManager.Delete(request.Name);
            return deleted
                ? new ProfileDeleteResponse { Success = true }
                : new ProfileDeleteResponse { Success = false, ErrorMessage = $"Profile '{request.Name}' was not found." };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Profile delete failed");
            return new ProfileDeleteResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Spec 033 — atomic engine-side rename of a custom profile (filename + JSON metadata.name
    /// + .source.json sidecar in one transaction via <see cref="ProfileManager.Rename"/>).
    /// Never touches config.json: after renaming the ACTIVE style, the shell caller updates
    /// <c>Formatter.ActiveProfile</c> itself or formatting silently falls back to defaults.
    /// </summary>
    public ProfileRenameResponse HandleProfileRename(ProfileRenameRequest request)
    {
        try
        {
            var finalName = profileManager.Rename(request.OldName, request.NewName);
            return new ProfileRenameResponse { Success = true, NewName = finalName };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Profile rename failed ({Old} -> {New})", request.OldName, request.NewName);
            return new ProfileRenameResponse { Success = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Spec 030 T020 — server-side duplicate of a stored profile (Format Styles editor New/Copy).
    /// <see cref="ProfileManager.Duplicate"/> loads the source's persisted values, clones them with
    /// a fresh identity + <c>BasedOn</c> link, and saves under the new name.
    /// </summary>
    public DuplicateProfileResponse HandleDuplicateProfile(DuplicateProfileRequest request)
    {
        try
        {
            var copy = profileManager.Duplicate(request.SourceName, request.NewName);
            return new DuplicateProfileResponse { Success = true, NewName = copy.Metadata.Name };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Profile duplicate failed ({Source} -> {New})", request.SourceName, request.NewName);
            return new DuplicateProfileResponse { Success = false, ErrorMessage = ex.Message };
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

    /// <summary>
    /// Spec 020 US3 (T049) — returns the canonical Format Settings Schema so the Format Styles
    /// editor can build its tree from one source of truth. The schema is built once at startup
    /// (lazy via <see cref="FormatSettingSchema.Default"/>) and cached for the life of the process.
    /// Short-circuits with <see cref="StyleEditorSchemaResponse.Cached"/> = true when the shell's
    /// <c>ClientSchemaVersion</c> matches.
    /// </summary>
    public StyleEditorSchemaResponse HandleStyleEditorSchema(StyleEditorSchemaRequest request)
    {
        try
        {
            var schema = FormatSettingSchema.Default;

            // Short-circuit: shell's cache is current
            if (request.ClientSchemaVersion.HasValue && request.ClientSchemaVersion.Value == schema.SchemaVersion)
            {
                return new StyleEditorSchemaResponse
                {
                    SchemaVersion = schema.SchemaVersion,
                    SchemaJson = null,
                    Cached = true,
                };
            }

            // Optionally filter unsupported entries
            var payload = schema;
            if (!request.IncludeUnsupported)
            {
                payload = new FormatSettingSchema
                {
                    SchemaVersion = schema.SchemaVersion,
                    Groups = [..schema.Groups],
                    Settings = [..schema.Settings.Where(s => s.Status != "Unsupported")],
                };
            }

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            });

            return new StyleEditorSchemaResponse
            {
                SchemaVersion = schema.SchemaVersion,
                SchemaJson = json,
                Cached = false,
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Style editor schema request failed");
            return new StyleEditorSchemaResponse
            {
                SchemaVersion = 0,
                ErrorMessage = ex.Message,
                Cached = false,
            };
        }
    }

    public ProfileImportResponse HandleProfileImport(ProfileImportRequest request)
    {
        try
        {
            if (request.FileContent is { Length: > MaxProfileJsonBytes })
            {
                return new ProfileImportResponse
                {
                    Success = false,
                    ErrorMessage = "Import content exceeds maximum allowed size (1 MB).",
                };
            }

            var sourceFormat = request.SourceFormat.ToLowerInvariant();
            var content = Encoding.UTF8.GetString(request.FileContent);

            if (sourceFormat is "sqlprompt" or "sqlpromptstylev2")
            {
                // Spec 031 FR-004 — sniff content: modern Redgate styles are JSON; the XML shape
                // is AKML's own spec-020 export. Sniffing is scoped to this branch on purpose —
                // the akmlstyle branch below always receives AKML's own JSON serialization.
                // U+FEFF: Encoding.UTF8.GetString keeps a BOM as a leading char and it is NOT
                // char.IsWhiteSpace, so strip it explicitly (spec edge case: BOM'd files decode correctly).
                // The trimmed copy is also what gets handed to the JSON/XML parsers below — both
                // JsonDocument.Parse(string) and XDocument.Parse(string) throw on a leading U+FEFF
                // *character* (as opposed to raw UTF-8 BOM bytes), so parsing the untrimmed content
                // would fail every BOM'd import. The verbatim ".source.json" write further down still
                // uses the original untrimmed `content` — that copy must stay byte-for-byte faithful.
                var trimmedContent = content.TrimStart((char)0xFEFF, ' ', '\t', '\r', '\n');
                var firstChar = trimmedContent.FirstOrDefault();

                if (firstChar == '{')
                {
                    var jsonResult = RedgateJsonStyleImporter.Import(trimmedContent, fallbackName: request.TargetProfileName);
                    if (!jsonResult.Success)
                    {
                        // FR-005 — visible failure, nothing saved.
                        return new ProfileImportResponse
                        {
                            Success = false,
                            ErrorMessage = $"Style file is not valid SQL Prompt JSON: {jsonResult.ParseError}",
                        };
                    }

                    if (!string.IsNullOrWhiteSpace(request.TargetProfileName))
                        jsonResult.Profile.Metadata.Name = request.TargetProfileName;

                    // FR-008 — built-in names cannot be shadowed by import.
                    if (profileManager.List().Any(p =>
                            p.IsBuiltIn && string.Equals(p.Name, jsonResult.Profile.Metadata.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new ProfileImportResponse
                        {
                            Success = false,
                            ErrorMessage = $"'{jsonResult.Profile.Metadata.Name}' is a built-in style name. Re-import with a different target name.",
                        };
                    }

                    profileManager.Save(jsonResult.Profile);

                    // FR-006 — preserve the verbatim source beside the profile for lossless re-import.
                    // GetCustomArtifactPath pairs SanitizeFileName with ValidatePathWithinBase (the
                    // same two-layer invariant Save/Delete enforce).
                    var sourcePath = profileManager.GetCustomArtifactPath(
                        jsonResult.Profile.Metadata.Name, ".source.json");
                    File.WriteAllText(sourcePath, content);

                    return new ProfileImportResponse
                    {
                        Success = true,
                        ProfileName = jsonResult.Profile.Metadata.Name,
                        MappedOptionsCount = jsonResult.MappedCount,
                        UnmappedOptionsCount = jsonResult.UnsupportedCount + jsonResult.UnknownCount,
                        OptionReports = jsonResult.Options
                            .Select(o => new ProfileImportOptionReport { Path = o.Path, Value = o.Value, Status = o.Status, Reason = o.Reason })
                            .ToArray(),
                    };
                }

                if (firstChar != '<')
                {
                    return new ProfileImportResponse
                    {
                        Success = false,
                        ErrorMessage = "Style file is neither JSON ('{') nor XML ('<').",
                    };
                }

                var importResult = SqlPromptImporter.Import(trimmedContent, request.TargetProfileName);

                // FR-005 — the legacy importer records parse errors in UnmappedOptions without failing; surface them.
                var parseError = importResult.UnmappedOptions.FirstOrDefault(o => o.StartsWith("Parse error:", StringComparison.Ordinal));
                if (parseError != null || (importResult.MappedCount == 0 && importResult.UnmappedCount == 0))
                {
                    return new ProfileImportResponse
                    {
                        Success = false,
                        ErrorMessage = parseError ?? "No options found in the XML style file.",
                    };
                }

                profileManager.Save(importResult.Profile);
                return new ProfileImportResponse
                {
                    Success = true,
                    ProfileName = importResult.Profile.Metadata.Name,
                    MappedOptionsCount = importResult.MappedCount,
                    UnmappedOptionsCount = importResult.UnmappedCount,
                    UnmappedOptions = importResult.UnmappedOptions.ToArray(),
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

    /// <summary>
    /// Spec 020 T031 — Format Styles editor "Export to SQL Prompt" handler. Loads the named
    /// profile and writes it as a <c>.sqlpromptstylev2</c> XML file at the requested absolute
    /// path via <see cref="SqlPromptExporter.ExportToFile"/> (atomic write — temp + rename).
    /// Pairs with <see cref="ProfileImportResponse"/>'s import path as the inverse direction.
    /// </summary>
    public ProfileExportSqlPromptResponse HandleProfileExportSqlPrompt(ProfileExportSqlPromptRequest request)
    {
        try
        {
            // Path validation (same envelope as HandleBulkFormatAsync — CLAUDE.md security policy:
            // absolute path, canonical form check to reject traversal sequences like foo\..\secret).
            if (string.IsNullOrWhiteSpace(request.DestinationPath))
                throw new ArgumentException("DestinationPath must not be empty.");
            if (!Path.IsPathFullyQualified(request.DestinationPath))
                throw new ArgumentException($"DestinationPath must be absolute: '{request.DestinationPath}'");
            var normalized = request.DestinationPath.Replace('/', Path.DirectorySeparatorChar);
            var canonical = Path.GetFullPath(normalized);
            if (!string.Equals(canonical, normalized, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"DestinationPath is not canonical (possible path traversal): '{request.DestinationPath}'");

            // Load via the same ProfileManager the rest of the handler uses — built-in or custom.
            var profile = profileManager.Load(request.Name);

            // Library entrypoint: atomic write (temp + rename) + auto-creates destination dir.
            var result = SqlPromptExporter.ExportToFile(profile, request.DestinationPath);

            return new ProfileExportSqlPromptResponse
            {
                Success = true,
                WrittenCount = result.WrittenCount,
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Profile export to SQL Prompt failed (name={Name}, dest={Dest})",
                request.Name, request.DestinationPath);
            return new ProfileExportSqlPromptResponse
            {
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    private FormattingProfile LoadProfile(string? profileName) => LoadProfile(profileName, out _);

    /// <summary>
    /// Resolves the requested style, falling back to built-in defaults when it cannot be loaded.
    /// <paramref name="fallbackWarning"/> is non-null ONLY in that fallback case, so callers can
    /// tell the user their chosen style did not apply — previously this swallow was silent apart
    /// from a log line, which is how the shipped default style formatting with POCO defaults went
    /// unnoticed. An explicitly-empty name still means "defaults by design" (no warning).
    /// </summary>
    private FormattingProfile LoadProfile(string? profileName, out string? fallbackWarning)
    {
        fallbackWarning = null;

        if (string.IsNullOrEmpty(profileName))
            return new FormattingProfile(); // Default profile with all defaults

        try
        {
            return profileManager.Load(profileName);
        }
        catch (Exception ex)
        {
            fallbackWarning =
                $"Formatting style '{profileName}' could not be loaded, so the built-in defaults were " +
                $"used instead. Check the style still exists in Format Styles. ({ex.GetType().Name})";
            Log.Warning(ex, "Profile {Name} not found, using default", profileName);
            return new FormattingProfile();
        }
    }
}
