using Xunit;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Core.Tests.Ipc.Messages
{
    /// <summary>
    /// Covers every IPC message class in AkmlSql.Core.Ipc.Messages — default values,
    /// property setters/getters, and any non-trivial constructors.
    /// </summary>
    public class IpcMessagesTests
    {
        // ── ConnectionInfo ────────────────────────────────────────────────────

        [Fact]
        public void ConnectionInfo_DefaultsAndMutations()
        {
            var m = new ConnectionInfo();
            Assert.Equal(string.Empty, m.SessionId);
            Assert.Equal(string.Empty, m.ConnectionString);
            Assert.Equal(0, m.ServerVersion);
            Assert.Equal(0, m.EngineEdition);
            Assert.Equal(string.Empty, m.DatabaseName);

            m.SessionId = "s1"; m.ConnectionString = "cs"; m.ServerVersion = 16;
            m.EngineEdition = 5; m.DatabaseName = "master";
            Assert.Equal("s1", m.SessionId);
            Assert.Equal("cs", m.ConnectionString);
            Assert.Equal(16, m.ServerVersion);
            Assert.Equal(5, m.EngineEdition);
            Assert.Equal("master", m.DatabaseName);
        }

        // ── DocumentChange + TextChange ───────────────────────────────────────

        [Fact]
        public void DocumentChange_DefaultsAndMutations()
        {
            var m = new DocumentChange();
            Assert.Equal(string.Empty, m.SessionId);
            Assert.Equal(0, m.ChangeType);
            Assert.Null(m.FullText);
            Assert.Null(m.Changes);

            m.SessionId = "s"; m.ChangeType = 1; m.FullText = "SELECT 1";
            m.Changes = [new TextChange { Offset = 0, OldLength = 1, NewText = "x" }];
            Assert.Equal("s", m.SessionId);
            Assert.Equal(1, m.ChangeType);
            Assert.Equal("SELECT 1", m.FullText);
            Assert.Single(m.Changes);
        }

        [Fact]
        public void TextChange_DefaultsAndMutations()
        {
            var t = new TextChange();
            Assert.Equal(0, t.Offset);
            Assert.Equal(0, t.OldLength);
            Assert.Equal(string.Empty, t.NewText);

            t.Offset = 5; t.OldLength = 3; t.NewText = "abc";
            Assert.Equal(5, t.Offset);
            Assert.Equal(3, t.OldLength);
            Assert.Equal("abc", t.NewText);
        }

        // ── CompletionRequest + CompletionResponse + CompletionItem + enum ────

        [Fact]
        public void CompletionRequest_DefaultsAndMutations()
        {
            var r = new CompletionRequest();
            Assert.Equal(string.Empty, r.SessionId);
            Assert.Equal(0, r.CursorOffset);
            Assert.Equal(0, r.TriggerKind);
            Assert.False(r.HasSelection);

            r.SessionId = "s"; r.CursorOffset = 10; r.TriggerKind = 2; r.HasSelection = true;
            Assert.Equal("s", r.SessionId);
            Assert.Equal(10, r.CursorOffset);
            Assert.Equal(2, r.TriggerKind);
            Assert.True(r.HasSelection);
        }

        [Fact]
        public void CompletionResponse_DefaultsAndMutations()
        {
            var r = new CompletionResponse();
            Assert.Empty(r.Items);
            Assert.False(r.IsIncomplete);

            r.Items = [new CompletionItem { DisplayText = "foo" }];
            r.IsIncomplete = true;
            Assert.Single(r.Items);
            Assert.True(r.IsIncomplete);
        }

        [Fact]
        public void CompletionItem_DefaultsAndMutations()
        {
            var i = new CompletionItem();
            Assert.Equal(string.Empty, i.DisplayText);
            Assert.Equal(string.Empty, i.InsertText);
            Assert.Equal(0, i.ObjectType);
            Assert.Equal(string.Empty, i.SecondaryText);
            Assert.Equal(string.Empty, i.SourceObject);
            Assert.Equal(0, i.SortPriority);

            i.DisplayText = "tbl"; i.InsertText = "tbl"; i.ObjectType = (int)CompletionObjectType.Table;
            i.SecondaryText = "dbo"; i.SourceObject = "dbo.tbl"; i.SortPriority = 1;
            Assert.Equal("tbl", i.DisplayText);
            Assert.Equal((int)CompletionObjectType.Table, i.ObjectType);
            Assert.Equal(1, i.SortPriority);
        }

        [Fact]
        public void CompletionObjectType_AllValues()
        {
            Assert.Equal(0,  (int)CompletionObjectType.Table);
            Assert.Equal(1,  (int)CompletionObjectType.View);
            Assert.Equal(2,  (int)CompletionObjectType.Column);
            Assert.Equal(3,  (int)CompletionObjectType.Keyword);
            Assert.Equal(4,  (int)CompletionObjectType.Snippet);
            Assert.Equal(5,  (int)CompletionObjectType.Function);
            Assert.Equal(6,  (int)CompletionObjectType.Procedure);
            Assert.Equal(7,  (int)CompletionObjectType.Schema);
            Assert.Equal(8,  (int)CompletionObjectType.Database);
            Assert.Equal(9,  (int)CompletionObjectType.Variable);
            Assert.Equal(10, (int)CompletionObjectType.Alias);
            Assert.Equal(11, (int)CompletionObjectType.Parameter);
        }

        // ── SignatureRequest + SignatureResponse + SignatureOverload + ParameterInfo ──

        [Fact]
        public void SignatureRequest_DefaultsAndMutations()
        {
            var r = new SignatureRequest();
            Assert.Equal(string.Empty, r.SessionId);
            Assert.Equal(0, r.CursorOffset);

            r.SessionId = "s"; r.CursorOffset = 5;
            Assert.Equal("s", r.SessionId);
            Assert.Equal(5, r.CursorOffset);
        }

        [Fact]
        public void SignatureResponse_DefaultsAndMutations()
        {
            var r = new SignatureResponse();
            Assert.Equal(string.Empty, r.FunctionName);
            Assert.Empty(r.Overloads);
            Assert.Equal(0, r.ActiveOverload);
            Assert.Equal(0, r.ActiveParameter);

            r.FunctionName = "ISNULL"; r.ActiveOverload = 1; r.ActiveParameter = 0;
            r.Overloads = [new SignatureOverload()];
            Assert.Equal("ISNULL", r.FunctionName);
            Assert.Single(r.Overloads);
        }

        [Fact]
        public void SignatureOverload_DefaultsAndMutations()
        {
            var o = new SignatureOverload();
            Assert.Equal(string.Empty, o.Label);
            Assert.Equal(string.Empty, o.Documentation);
            Assert.Empty(o.Parameters);

            o.Label = "ISNULL(check_expression, replacement)";
            o.Documentation = "Replaces NULL with a replacement.";
            o.Parameters = [new ParameterInfo { Name = "check_expression", Type = "expression", IsOptional = false }];
            Assert.Equal("ISNULL(check_expression, replacement)", o.Label);
            Assert.Single(o.Parameters);
        }

        [Fact]
        public void ParameterInfo_DefaultsAndMutations()
        {
            var p = new ParameterInfo();
            Assert.Equal(string.Empty, p.Name);
            Assert.Equal(string.Empty, p.Type);
            Assert.Equal(string.Empty, p.Documentation);
            Assert.False(p.IsOptional);

            p.Name = "n"; p.Type = "int"; p.Documentation = "doc"; p.IsOptional = true;
            Assert.Equal("n", p.Name);
            Assert.Equal("int", p.Type);
            Assert.Equal("doc", p.Documentation);
            Assert.True(p.IsOptional);
        }

        // ── QuickInfoRequest + QuickInfoResponse + QuickInfoDetail ─────────────

        [Fact]
        public void QuickInfoRequest_DefaultsAndMutations()
        {
            var r = new QuickInfoRequest();
            Assert.Equal(string.Empty, r.SessionId);
            Assert.Equal(0, r.CursorOffset);

            r.SessionId = "s"; r.CursorOffset = 7;
            Assert.Equal("s", r.SessionId);
            Assert.Equal(7, r.CursorOffset);
        }

        [Fact]
        public void QuickInfoResponse_DefaultsAndMutations()
        {
            var r = new QuickInfoResponse();
            Assert.Equal(string.Empty, r.ObjectType);
            Assert.Equal(string.Empty, r.Header);
            Assert.Empty(r.Details);
            Assert.Null(r.Description);

            r.ObjectType = "Table"; r.Header = "dbo.Orders"; r.Description = "desc";
            r.Details = [new QuickInfoDetail("Columns", "3")];
            Assert.Equal("Table", r.ObjectType);
            Assert.Equal("dbo.Orders", r.Header);
            Assert.Equal("desc", r.Description);
            Assert.Single(r.Details);
        }

        [Fact]
        public void QuickInfoDetail_DefaultConstructorAndMutations()
        {
            var d = new QuickInfoDetail();
            Assert.Equal(string.Empty, d.Label);
            Assert.Equal(string.Empty, d.Value);

            d.Label = "Type"; d.Value = "nvarchar(50)";
            Assert.Equal("Type", d.Label);
            Assert.Equal("nvarchar(50)", d.Value);
        }

        [Fact]
        public void QuickInfoDetail_ParameterizedConstructor()
        {
            var d = new QuickInfoDetail("Schema", "dbo");
            Assert.Equal("Schema", d.Label);
            Assert.Equal("dbo", d.Value);
        }

        // ── RefreshRequest + RefreshResponse ──────────────────────────────────

        [Fact]
        public void RefreshRequest_DefaultsAndMutations()
        {
            var r = new RefreshRequest();
            Assert.Equal(string.Empty, r.SessionId);
            r.SessionId = "sess"; Assert.Equal("sess", r.SessionId);
        }

        [Fact]
        public void RefreshResponse_DefaultsAndMutations()
        {
            var r = new RefreshResponse();
            Assert.False(r.Success);
            Assert.Equal(0, r.ObjectCount);
            Assert.Null(r.ErrorMessage);

            r.Success = true; r.ObjectCount = 42; r.ErrorMessage = "err";
            Assert.True(r.Success);
            Assert.Equal(42, r.ObjectCount);
            Assert.Equal("err", r.ErrorMessage);
        }

        // ── ErrorInfo + EngineStatusInfo ──────────────────────────────────────

        [Fact]
        public void ErrorInfo_DefaultsAndMutations()
        {
            var e = new ErrorInfo();
            Assert.Equal(0, e.Code);
            Assert.Equal(string.Empty, e.Message);

            e.Code = 500; e.Message = "Internal error";
            Assert.Equal(500, e.Code);
            Assert.Equal("Internal error", e.Message);
        }

        [Fact]
        public void EngineStatusInfo_DefaultsAndMutations()
        {
            var s = new EngineStatusInfo();
            Assert.Equal(0, s.MemoryUsageMb);
            Assert.Equal(0, s.CachedDatabases);
            Assert.Equal(0, s.ActiveSessions);
            Assert.Equal(0L, s.UptimeSeconds);

            s.MemoryUsageMb = 128; s.CachedDatabases = 3; s.ActiveSessions = 2; s.UptimeSeconds = 3600;
            Assert.Equal(128, s.MemoryUsageMb);
            Assert.Equal(3, s.CachedDatabases);
            Assert.Equal(2, s.ActiveSessions);
            Assert.Equal(3600L, s.UptimeSeconds);
        }

        // ── BulkFormat group ─────────────────────────────────────────────────

        [Fact]
        public void BulkFormatRequest_DefaultsAndMutations()
        {
            var r = new BulkFormatRequest();
            Assert.Equal(string.Empty, r.SessionId);
            Assert.Empty(r.FilePaths);
            Assert.Null(r.ProfileName);
            Assert.False(r.DryRun);
            Assert.False(r.CreateBackups);

            r.SessionId = "s"; r.FilePaths = ["a.sql", "b.sql"]; r.ProfileName = "P";
            r.DryRun = true; r.CreateBackups = true;
            Assert.Equal("s", r.SessionId);
            Assert.Equal(2, r.FilePaths.Length);
            Assert.Equal("P", r.ProfileName);
            Assert.True(r.DryRun);
            Assert.True(r.CreateBackups);
        }

        [Fact]
        public void BulkFormatCancelRequest_DefaultsAndMutations()
        {
            var r = new BulkFormatCancelRequest();
            Assert.Equal(string.Empty, r.SessionId);
            r.SessionId = "x"; Assert.Equal("x", r.SessionId);
        }

        [Fact]
        public void BulkFormatProgressInfo_DefaultsAndMutations()
        {
            var p = new BulkFormatProgressInfo();
            Assert.Equal(string.Empty, p.SessionId);
            Assert.Equal(0, p.TotalFiles);
            Assert.Equal(0, p.CompletedFiles);
            Assert.Null(p.CurrentFile);
            Assert.Null(p.LastResult);

            p.SessionId = "s"; p.TotalFiles = 10; p.CompletedFiles = 5;
            p.CurrentFile = "c.sql"; p.LastResult = new FileResult();
            Assert.Equal("s", p.SessionId);
            Assert.Equal(10, p.TotalFiles);
            Assert.Equal(5, p.CompletedFiles);
            Assert.Equal("c.sql", p.CurrentFile);
            Assert.NotNull(p.LastResult);
        }

        [Fact]
        public void BulkFormatReportResponse_DefaultsAndMutations()
        {
            var r = new BulkFormatReportResponse();
            Assert.Equal(string.Empty, r.SessionId);
            Assert.Equal(0, r.TotalFiles);
            Assert.Equal(0, r.SuccessCount);
            Assert.Equal(0, r.FailedCount);
            Assert.Equal(0, r.SkippedCount);
            Assert.Equal(0L, r.ElapsedMs);
            Assert.Empty(r.Results);

            r.SessionId = "s"; r.TotalFiles = 5; r.SuccessCount = 4; r.FailedCount = 1;
            r.SkippedCount = 0; r.ElapsedMs = 1234L; r.Results = [new FileResult()];
            Assert.Equal("s", r.SessionId);
            Assert.Equal(5, r.TotalFiles);
            Assert.Equal(4, r.SuccessCount);
            Assert.Equal(1, r.FailedCount);
            Assert.Equal(1234L, r.ElapsedMs);
            Assert.Single(r.Results);
        }

        // ── FileResult ────────────────────────────────────────────────────────

        [Fact]
        public void FileResult_DefaultsAndMutations()
        {
            var f = new FileResult();
            Assert.Equal(string.Empty, f.FilePath);
            Assert.Equal(0, f.Status);
            Assert.Equal(0, f.LinesChanged);
            Assert.Null(f.ErrorMessage);

            f.FilePath = "a.sql"; f.Status = 1; f.LinesChanged = 5; f.ErrorMessage = "err";
            Assert.Equal("a.sql", f.FilePath);
            Assert.Equal(1, f.Status);
            Assert.Equal(5, f.LinesChanged);
            Assert.Equal("err", f.ErrorMessage);
        }

        // ── Format group ──────────────────────────────────────────────────────

        [Fact]
        public void FormatRequest_DefaultsAndMutations()
        {
            var r = new FormatRequest();
            Assert.Equal(string.Empty, r.SessionId);
            Assert.Equal(string.Empty, r.Text);
            Assert.Null(r.ProfileName);
            Assert.Null(r.ProfileOverrides);
            Assert.Null(r.IncludeActions);

            r.SessionId = "s"; r.Text = "SELECT 1"; r.ProfileName = "P";
            r.ProfileOverrides = [1]; r.IncludeActions = [1, 2];
            Assert.Equal("s", r.SessionId);
            Assert.Equal("SELECT 1", r.Text);
            Assert.Equal("P", r.ProfileName);
            Assert.NotNull(r.ProfileOverrides);
            Assert.Equal(2, r.IncludeActions.Length);
        }

        [Fact]
        public void FormatResponse_DefaultsAndMutations()
        {
            var r = new FormatResponse();
            Assert.False(r.Success);
            Assert.Equal(string.Empty, r.FormattedText);
            Assert.False(r.WasModified);
            Assert.False(r.ValidationPassed);
            Assert.Equal(0L, r.ElapsedMs);
            Assert.Null(r.Diagnostics);

            r.Success = true; r.FormattedText = "SELECT 1"; r.WasModified = true;
            r.ValidationPassed = true; r.ElapsedMs = 50L;
            r.Diagnostics = [new FormatDiagnosticInfo { Severity = 1, Message = "w", Offset = 0, Line = 1 }];
            Assert.True(r.Success);
            Assert.Equal("SELECT 1", r.FormattedText);
            Assert.Equal(50L, r.ElapsedMs);
            Assert.Single(r.Diagnostics);
        }

        [Fact]
        public void FormatDiagnosticInfo_DefaultsAndMutations()
        {
            var d = new FormatDiagnosticInfo();
            Assert.Equal(0, d.Severity);
            Assert.Equal(string.Empty, d.Message);
            Assert.Equal(0, d.Offset);
            Assert.Equal(0, d.Line);

            d.Severity = 2; d.Message = "warn"; d.Offset = 10; d.Line = 3;
            Assert.Equal(2, d.Severity);
            Assert.Equal("warn", d.Message);
            Assert.Equal(10, d.Offset);
            Assert.Equal(3, d.Line);
        }

        [Fact]
        public void FormatSelectionRequest_DefaultsAndMutations()
        {
            var r = new FormatSelectionRequest();
            Assert.Equal(string.Empty, r.SessionId);
            Assert.Equal(string.Empty, r.Text);
            Assert.Equal(0, r.SelectionStart);
            Assert.Equal(0, r.SelectionEnd);
            Assert.Null(r.ProfileName);

            r.SessionId = "s"; r.Text = "t"; r.SelectionStart = 1; r.SelectionEnd = 5; r.ProfileName = "P";
            Assert.Equal("s", r.SessionId);
            Assert.Equal(1, r.SelectionStart);
            Assert.Equal(5, r.SelectionEnd);
            Assert.Equal("P", r.ProfileName);
        }

        [Fact]
        public void FormatSelectionResponse_DefaultsAndMutations()
        {
            var r = new FormatSelectionResponse();
            Assert.False(r.Success);
            Assert.Equal(string.Empty, r.FormattedText);
            Assert.Equal(0, r.OriginalStart);
            Assert.Equal(0, r.OriginalEnd);
            Assert.False(r.WasModified);
            Assert.False(r.ValidationPassed);
            Assert.Equal(0L, r.ElapsedMs);

            r.Success = true; r.FormattedText = "x"; r.OriginalStart = 1; r.OriginalEnd = 4;
            r.WasModified = true; r.ValidationPassed = true; r.ElapsedMs = 10L;
            Assert.True(r.Success);
            Assert.Equal(1, r.OriginalStart);
            Assert.Equal(10L, r.ElapsedMs);
        }

        [Fact]
        public void FormatPreviewRequest_DefaultsAndMutations()
        {
            var r = new FormatPreviewRequest();
            Assert.Equal(string.Empty, r.SessionId);
            Assert.Equal(string.Empty, r.SampleText);
            Assert.Equal(string.Empty, r.ProfileJson);

            r.SessionId = "s"; r.SampleText = "SELECT 1"; r.ProfileJson = "{}";
            Assert.Equal("s", r.SessionId);
            Assert.Equal("SELECT 1", r.SampleText);
            Assert.Equal("{}", r.ProfileJson);
        }

        [Fact]
        public void FormatPreviewResponse_DefaultsAndMutations()
        {
            var r = new FormatPreviewResponse();
            Assert.Equal(string.Empty, r.FormattedText);
            Assert.Equal(0L, r.ElapsedMs);

            r.FormattedText = "SELECT 1"; r.ElapsedMs = 20L;
            Assert.Equal("SELECT 1", r.FormattedText);
            Assert.Equal(20L, r.ElapsedMs);
        }

        // ── FormatAction group ────────────────────────────────────────────────

        [Fact]
        public void FormatActionType_AllValues()
        {
            Assert.Equal(0, (int)FormatActionType.CasingOnly);
            Assert.Equal(1, (int)FormatActionType.InsertSemicolons);
            Assert.Equal(2, (int)FormatActionType.RemoveSemicolons);
            Assert.Equal(3, (int)FormatActionType.ExpandWildcards);
            Assert.Equal(4, (int)FormatActionType.QualifyObjectNames);
            Assert.Equal(5, (int)FormatActionType.AddSquareBrackets);
            Assert.Equal(6, (int)FormatActionType.RemoveSquareBrackets);
            Assert.Equal(7, (int)FormatActionType.AddAsKeyword);
            Assert.Equal(8, (int)FormatActionType.RemoveAsKeyword);
        }

        [Fact]
        public void FormatActionRequest_DefaultsAndMutations()
        {
            var r = new FormatActionRequest();
            Assert.Equal(string.Empty, r.SessionId);
            Assert.Equal(string.Empty, r.Text);
            Assert.Equal(0, r.ActionType);
            Assert.Null(r.ProfileName);

            r.SessionId = "s"; r.Text = "t"; r.ActionType = (int)FormatActionType.CasingOnly; r.ProfileName = "P";
            Assert.Equal("s", r.SessionId);
            Assert.Equal((int)FormatActionType.CasingOnly, r.ActionType);
            Assert.Equal("P", r.ProfileName);
        }

        [Fact]
        public void FormatActionResponse_DefaultsAndMutations()
        {
            var r = new FormatActionResponse();
            Assert.False(r.Success);
            Assert.Equal(string.Empty, r.FormattedText);
            Assert.False(r.WasModified);
            Assert.Equal(0L, r.ElapsedMs);
            Assert.Null(r.ErrorMessage);

            r.Success = true; r.FormattedText = "x"; r.WasModified = true; r.ElapsedMs = 5L; r.ErrorMessage = "e";
            Assert.True(r.Success);
            Assert.Equal("x", r.FormattedText);
            Assert.Equal(5L, r.ElapsedMs);
            Assert.Equal("e", r.ErrorMessage);
        }

        // ── Profile group ─────────────────────────────────────────────────────

        [Fact]
        public void ProfileInfo_DefaultsAndMutations()
        {
            var p = new ProfileInfo();
            Assert.Equal(string.Empty, p.Name);
            Assert.Null(p.Description);
            Assert.Null(p.Author);
            Assert.False(p.IsBuiltIn);
            Assert.False(p.IsActive);
            Assert.Null(p.BasedOn);
            Assert.Null(p.Modified);

            p.Name = "Default"; p.Description = "d"; p.Author = "a"; p.IsBuiltIn = true;
            p.IsActive = true; p.BasedOn = "Base"; p.Modified = "2026-01-01";
            Assert.Equal("Default", p.Name);
            Assert.Equal("d", p.Description);
            Assert.True(p.IsBuiltIn);
            Assert.True(p.IsActive);
            Assert.Equal("2026-01-01", p.Modified);
        }

        [Fact]
        public void ProfileListRequest_CanBeInstantiated()
        {
            var r = new ProfileListRequest();
            Assert.NotNull(r);
        }

        [Fact]
        public void ProfileListResponse_DefaultsAndMutations()
        {
            var r = new ProfileListResponse();
            Assert.Empty(r.Profiles);
            r.Profiles = [new ProfileInfo { Name = "P" }];
            Assert.Single(r.Profiles);
        }

        [Fact]
        public void ProfileSaveRequest_DefaultsAndMutations()
        {
            var r = new ProfileSaveRequest();
            Assert.Equal(string.Empty, r.Name);
            Assert.Equal(string.Empty, r.ProfileJson);
            Assert.Null(r.Description);
            Assert.Null(r.BasedOn);

            r.Name = "N"; r.ProfileJson = "{}"; r.Description = "d"; r.BasedOn = "B";
            Assert.Equal("N", r.Name);
            Assert.Equal("{}", r.ProfileJson);
            Assert.Equal("d", r.Description);
            Assert.Equal("B", r.BasedOn);
        }

        [Fact]
        public void ProfileSaveResponse_DefaultsAndMutations()
        {
            var r = new ProfileSaveResponse();
            Assert.False(r.Success);
            Assert.Null(r.ErrorMessage);

            r.Success = true; r.ErrorMessage = "e";
            Assert.True(r.Success);
            Assert.Equal("e", r.ErrorMessage);
        }

        [Fact]
        public void ProfileDeleteRequest_DefaultsAndMutations()
        {
            var r = new ProfileDeleteRequest();
            Assert.Equal(string.Empty, r.Name);
            r.Name = "X"; Assert.Equal("X", r.Name);
        }

        [Fact]
        public void ProfileDeleteResponse_DefaultsAndMutations()
        {
            var r = new ProfileDeleteResponse();
            Assert.False(r.Success);
            Assert.Null(r.ErrorMessage);

            r.Success = true; r.ErrorMessage = "e";
            Assert.True(r.Success);
            Assert.Equal("e", r.ErrorMessage);
        }

        [Fact]
        public void ProfileImportRequest_DefaultsAndMutations()
        {
            var r = new ProfileImportRequest();
            Assert.Equal(string.Empty, r.SourceFormat);
            Assert.Empty(r.FileContent);
            Assert.Null(r.TargetProfileName);

            r.SourceFormat = "ssms"; r.FileContent = [1, 2]; r.TargetProfileName = "T";
            Assert.Equal("ssms", r.SourceFormat);
            Assert.Equal(2, r.FileContent.Length);
            Assert.Equal("T", r.TargetProfileName);
        }

        [Fact]
        public void ProfileImportResponse_DefaultsAndMutations()
        {
            var r = new ProfileImportResponse();
            Assert.False(r.Success);
            Assert.Equal(0, r.MappedOptionsCount);
            Assert.Equal(0, r.UnmappedOptionsCount);
            Assert.Null(r.UnmappedOptions);
            Assert.Null(r.ErrorMessage);

            r.Success = true; r.MappedOptionsCount = 10; r.UnmappedOptionsCount = 2;
            r.UnmappedOptions = ["x", "y"]; r.ErrorMessage = "e";
            Assert.True(r.Success);
            Assert.Equal(10, r.MappedOptionsCount);
            Assert.Equal(2, r.UnmappedOptions.Length);
            Assert.Equal("e", r.ErrorMessage);
        }

        // ── Snippet group ─────────────────────────────────────────────────────

        [Fact]
        public void SnippetExpandRequest_DefaultsAndMutations()
        {
            var r = new SnippetExpandRequest();
            Assert.Equal(string.Empty, r.SessionId);
            Assert.Equal(string.Empty, r.Shortcode);
            Assert.Equal(0, r.CursorOffset);
            Assert.Equal(string.Empty, r.SelectedText);
            Assert.False(r.FormatOnExpand);
            Assert.Equal(string.Empty, r.ProfileName);
            Assert.Equal(string.Empty, r.ClipboardText);

            r.SessionId = "s"; r.Shortcode = "sel"; r.CursorOffset = 3;
            r.SelectedText = "txt"; r.FormatOnExpand = true; r.ProfileName = "P"; r.ClipboardText = "c";
            Assert.Equal("s", r.SessionId);
            Assert.Equal("sel", r.Shortcode);
            Assert.Equal(3, r.CursorOffset);
            Assert.True(r.FormatOnExpand);
            Assert.Equal("c", r.ClipboardText);
        }

        [Fact]
        public void SnippetExpandResponse_DefaultsAndMutations()
        {
            var r = new SnippetExpandResponse();
            Assert.False(r.Success);
            Assert.Equal(string.Empty, r.ExpandedText);
            Assert.Empty(r.Placeholders);
            Assert.Equal(-1, r.CursorOffset);
            Assert.False(r.WasFormatted);
            Assert.Null(r.ErrorMessage);

            r.Success = true; r.ExpandedText = "SELECT $1"; r.CursorOffset = 7;
            r.WasFormatted = true; r.ErrorMessage = "e";
            r.Placeholders = [new PlaceholderInfo()];
            Assert.True(r.Success);
            Assert.Equal("SELECT $1", r.ExpandedText);
            Assert.Equal(7, r.CursorOffset);
            Assert.True(r.WasFormatted);
            Assert.Single(r.Placeholders);
        }

        [Fact]
        public void PlaceholderInfo_DefaultsAndMutations()
        {
            var p = new PlaceholderInfo();
            Assert.Equal(string.Empty, p.VariableName);
            Assert.Equal(0, p.Offset);
            Assert.Equal(0, p.Length);
            Assert.Equal(string.Empty, p.DefaultText);
            Assert.Null(p.SchemaAwareType);
            Assert.Equal(0, p.GroupIndex);

            p.VariableName = "col"; p.Offset = 7; p.Length = 3; p.DefaultText = "Id";
            p.SchemaAwareType = "column"; p.GroupIndex = 1;
            Assert.Equal("col", p.VariableName);
            Assert.Equal(7, p.Offset);
            Assert.Equal(3, p.Length);
            Assert.Equal("Id", p.DefaultText);
            Assert.Equal("column", p.SchemaAwareType);
            Assert.Equal(1, p.GroupIndex);
        }

        [Fact]
        public void SnippetListRequest_DefaultsAndMutations()
        {
            var r = new SnippetListRequest();
            Assert.Equal(string.Empty, r.Query);
            Assert.Null(r.Context);
            Assert.False(r.HasSelection);
            Assert.Equal(0, r.SourceFilter);
            Assert.Null(r.CategoryFilter);

            r.Query = "sel"; r.Context = "ctx"; r.HasSelection = true; r.SourceFilter = 1; r.CategoryFilter = "DML";
            Assert.Equal("sel", r.Query);
            Assert.Equal("ctx", r.Context);
            Assert.True(r.HasSelection);
            Assert.Equal(1, r.SourceFilter);
            Assert.Equal("DML", r.CategoryFilter);
        }

        [Fact]
        public void SnippetListResponse_DefaultsAndMutations()
        {
            var r = new SnippetListResponse();
            Assert.Empty(r.Snippets);
            r.Snippets = [new SnippetInfo { Id = "1", Shortcode = "sel" }];
            Assert.Single(r.Snippets);
        }

        [Fact]
        public void SnippetInfo_DefaultsAndMutations()
        {
            var s = new SnippetInfo();
            Assert.Equal(string.Empty, s.Id);
            Assert.Equal(string.Empty, s.Shortcode);
            Assert.Equal(string.Empty, s.Name);
            Assert.Equal(string.Empty, s.Description);
            Assert.Equal(string.Empty, s.Category);
            Assert.Equal(0, s.Source);
            Assert.False(s.SurroundsWith);
            Assert.Equal(0, s.UsageCount);
            Assert.Empty(s.Tags);

            s.Id = "1"; s.Shortcode = "sel"; s.Name = "Select"; s.Description = "d";
            s.Category = "DML"; s.Source = 1; s.SurroundsWith = true; s.UsageCount = 5;
            s.Tags = ["sql", "select"];
            Assert.Equal("1", s.Id);
            Assert.Equal("sel", s.Shortcode);
            Assert.Equal(1, s.Source);
            Assert.True(s.SurroundsWith);
            Assert.Equal(5, s.UsageCount);
            Assert.Equal(2, s.Tags.Length);
        }

        [Fact]
        public void SnippetSaveRequest_DefaultsAndMutations()
        {
            var r = new SnippetSaveRequest();
            Assert.Equal(string.Empty, r.SnippetJson);
            Assert.False(r.IsNew);

            r.SnippetJson = "{}"; r.IsNew = true;
            Assert.Equal("{}", r.SnippetJson);
            Assert.True(r.IsNew);
        }

        [Fact]
        public void SnippetSaveResponse_DefaultsAndMutations()
        {
            var r = new SnippetSaveResponse();
            Assert.False(r.Success);
            Assert.Null(r.ErrorMessage);

            r.Success = true; r.ErrorMessage = "e";
            Assert.True(r.Success);
            Assert.Equal("e", r.ErrorMessage);
        }

        [Fact]
        public void SnippetDeleteRequest_DefaultsAndMutations()
        {
            var r = new SnippetDeleteRequest();
            Assert.Equal(string.Empty, r.SnippetId);
            r.SnippetId = "abc"; Assert.Equal("abc", r.SnippetId);
        }

        [Fact]
        public void SnippetDeleteResponse_DefaultsAndMutations()
        {
            var r = new SnippetDeleteResponse();
            Assert.False(r.Success);
            Assert.Null(r.ErrorMessage);

            r.Success = true; r.ErrorMessage = "e";
            Assert.True(r.Success);
            Assert.Equal("e", r.ErrorMessage);
        }

        [Fact]
        public void SnippetImportRequest_DefaultsAndMutations()
        {
            var r = new SnippetImportRequest();
            Assert.Equal(string.Empty, r.FileContent);
            Assert.Equal(0, r.SourceFormat);
            Assert.Null(r.NewSnippetName);

            r.FileContent = "<snippets/>"; r.SourceFormat = 1; r.NewSnippetName = "My";
            Assert.Equal("<snippets/>", r.FileContent);
            Assert.Equal(1, r.SourceFormat);
            Assert.Equal("My", r.NewSnippetName);
        }

        [Fact]
        public void SnippetImportResponse_DefaultsAndMutations()
        {
            var r = new SnippetImportResponse();
            Assert.False(r.Success);
            Assert.Equal(0, r.ImportedCount);
            Assert.Equal(0, r.FailedCount);
            Assert.Empty(r.FailedDetails);
            Assert.Empty(r.SnippetIds);

            r.Success = true; r.ImportedCount = 3; r.FailedCount = 1;
            r.FailedDetails = ["err1"]; r.SnippetIds = ["a", "b", "c"];
            Assert.True(r.Success);
            Assert.Equal(3, r.ImportedCount);
            Assert.Equal(1, r.FailedCount);
            Assert.Single(r.FailedDetails);
            Assert.Equal(3, r.SnippetIds.Length);
        }
    }
}
