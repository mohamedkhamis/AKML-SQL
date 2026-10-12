using System.Text;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// Spec 041 (T166, research R64, FR-015, FR-072) — New query, Open, Save and Save as, as SSMS does them: a modified
/// document is confirmed before it is replaced (and pending grid edits before that), default names count up and
/// are never reused, Save asks for a name only while the document has a default one, the file is
/// <c>{name}.sql</c> in UTF-8 with a BOM, and a programmatic set-text never marks the document modified.
/// </summary>
public sealed class EditorDocumentFlowTests
{
    private sealed class FakeText : IEditorText
    {
        public string Text { get; set; } = string.Empty;
        public Action<string>? OnSet { get; set; }
        public Task<string> GetTextAsync() => Task.FromResult(Text);
        public Task SetTextAsync(string text)
        {
            Text = text;
            OnSet?.Invoke(text);   // the editor's change listener fires on a programmatic set too
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDownloader : IFileDownloader
    {
        public List<(string Name, string Mime, string Text, bool Bom)> Files { get; } = new();
        public Task DownloadTextAsync(string fileName, string mimeType, string text, bool withBom)
        {
            Files.Add((fileName, mimeType, text, withBom));
            return Task.CompletedTask;
        }
    }

    private readonly FakeEditorSessionStore _store = new();
    private readonly FakeDialogService _dialogs = new();
    private readonly FakeNotificationService _notes = new();
    private readonly FakeDownloader _downloads = new();
    private readonly FakeText _text = new();
    private readonly EditorDocumentFlow _flow;
    private bool _pendingEditsOk = true;
    private int _pendingEditChecks;

    public EditorDocumentFlowTests()
    {
        _flow = new EditorDocumentFlow(_store, _dialogs, _notes, _downloads);
        _flow.Attach(_text, () => { _pendingEditChecks++; return Task.FromResult(_pendingEditsOk); });
        _text.OnSet = t => _flow.OnTextChanged(t);
    }

    private async Task TypeAsync(string text)
    {
        _text.Text = text;
        _flow.OnTextChanged(text);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_fresh_editor_is_SQLQuery1_and_not_modified()
    {
        await _flow.LoadAsync(null);

        Assert.Equal("SQLQuery1", _flow.Name);
        Assert.False(_flow.IsModified);
        Assert.Equal(2, _flow.NextDefaultNumber);
    }

    [Fact]
    public async Task A_restored_document_keeps_its_name_and_modified_state()
    {
        await _flow.LoadAsync(new EditorSessionRecord { DocumentName = "orders-audit", IsModified = true, NextDefaultNumber = 4, DocumentText = "SELECT 1;", SessionKey = "k" });

        Assert.Equal("orders-audit", _flow.Name);
        Assert.True(_flow.IsModified);
        Assert.Equal(4, _flow.NextDefaultNumber);
        Assert.Equal("k", _flow.SessionKey);
    }

    [Fact]
    public async Task Modified_is_text_different_from_the_baseline()
    {
        await _flow.LoadAsync(new EditorSessionRecord { DocumentName = "SQLQuery1", DocumentText = "SELECT 1;", NextDefaultNumber = 2 });

        await TypeAsync("SELECT 12;");
        Assert.True(_flow.IsModified);
        await TypeAsync("SELECT 1;");
        Assert.False(_flow.IsModified);
    }

    [Fact]
    public async Task New_query_on_a_modified_document_confirms_and_Cancel_changes_nothing()
    {
        await _flow.LoadAsync(null);
        await TypeAsync("SELECT 1;");
        _dialogs.ConfirmAnswer = _ => false;

        Assert.False(await _flow.NewAsync());

        Assert.Contains("SQLQuery1", Assert.Single(_dialogs.Confirms).Message);
        Assert.Equal("SQLQuery1", _flow.Name);
        Assert.Equal("SELECT 1;", _text.Text);
        Assert.True(_flow.IsModified);
    }

    [Fact]
    public async Task New_query_starts_the_next_default_name_and_never_reuses_one()
    {
        await _flow.LoadAsync(null);
        var firstKey = _flow.SessionKey;
        await TypeAsync("SELECT 1;");

        Assert.True(await _flow.NewAsync());
        Assert.Equal("SQLQuery2", _flow.Name);
        Assert.Equal(string.Empty, _text.Text);
        Assert.False(_flow.IsModified);
        Assert.NotEqual(firstKey, _flow.SessionKey);

        Assert.True(await _flow.NewAsync());   // not modified: no confirmation
        Assert.Equal("SQLQuery3", _flow.Name);
        Assert.Single(_dialogs.Confirms);
        Assert.Equal(4, (await _store.RestoreAsync())!.NextDefaultNumber);
    }

    [Fact]
    public async Task Pending_grid_edits_are_confirmed_first_and_a_No_stops_New_and_Open()
    {
        await _flow.LoadAsync(null);
        _pendingEditsOk = false;

        Assert.False(await _flow.NewAsync());
        Assert.False(await _flow.OpenAsync(Encoding.UTF8.GetBytes("SELECT 2;"), "two.sql"));

        Assert.Equal(2, _pendingEditChecks);
        Assert.Equal("SQLQuery1", _flow.Name);
    }

    [Fact]
    public async Task Open_names_the_document_after_the_file_and_is_not_modified()
    {
        await _flow.LoadAsync(null);
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("SELECT N'é';")).ToArray();

        Assert.True(await _flow.OpenAsync(utf16, "Monthly Close.sql"));

        Assert.Equal("Monthly Close", _flow.Name);
        Assert.Equal("SELECT N'é';", _text.Text);
        Assert.False(_flow.IsModified);
        Assert.Equal("Monthly Close", (await _store.RestoreAsync())!.DocumentName);
    }

    [Fact]
    public async Task Open_on_a_modified_document_confirms_first()
    {
        await _flow.LoadAsync(null);
        await TypeAsync("unsaved");
        _dialogs.ConfirmAnswer = _ => false;

        Assert.False(await _flow.OpenAsync(Encoding.UTF8.GetBytes("SELECT 2;"), "two.sql"));

        Assert.Single(_dialogs.Confirms);
        Assert.Equal("unsaved", _text.Text);
    }

    [Fact]
    public async Task Open_refuses_a_file_over_the_limit_with_a_message()
    {
        await _flow.LoadAsync(null);
        var big = Encoding.ASCII.GetBytes(new string('x', DocumentSizeLimit.MaxDocumentSizeChars + 1));

        Assert.False(await _flow.OpenAsync(big, "huge.sql"));

        Assert.Contains(_notes.Sent, n => n.Text.Contains("10 MB") && n.Kind == NotificationKind.Error);
        Assert.Equal("SQLQuery1", _flow.Name);
    }

    [Fact]
    public async Task Save_asks_for_a_name_only_while_the_name_is_a_default()
    {
        await _flow.LoadAsync(null);
        await TypeAsync("SELECT 1;");
        _dialogs.PromptAnswer = _ => "orders-audit";

        Assert.True(await _flow.SaveAsync());
        Assert.Single(_dialogs.Prompts);
        Assert.Equal("orders-audit", _flow.Name);
        Assert.False(_flow.IsModified);

        await TypeAsync("SELECT 2;");
        Assert.True(await _flow.SaveAsync());
        Assert.Single(_dialogs.Prompts);   // named now: no second prompt
        Assert.Equal(2, _downloads.Files.Count);
    }

    [Fact]
    public async Task Cancelling_the_name_prompt_saves_nothing()
    {
        await _flow.LoadAsync(null);
        await TypeAsync("SELECT 1;");
        _dialogs.PromptAnswer = _ => null;

        Assert.False(await _flow.SaveAsync());

        Assert.Empty(_downloads.Files);
        Assert.True(_flow.IsModified);
    }

    [Fact]
    public async Task The_saved_file_is_name_dot_sql_in_UTF8_with_a_BOM()
    {
        await _flow.LoadAsync(new EditorSessionRecord { DocumentName = "orders-audit", DocumentText = "SELECT 1;", NextDefaultNumber = 2 });
        await TypeAsync("SELECT 1; -- é");

        await _flow.SaveAsync();

        var file = Assert.Single(_downloads.Files);
        Assert.Equal("orders-audit.sql", file.Name);
        Assert.True(file.Bom);
        Assert.Equal("SELECT 1; -- é", file.Text);
    }

    [Fact]
    public async Task Save_as_always_asks_and_renames_under_the_same_session_key()
    {
        await _flow.LoadAsync(new EditorSessionRecord { DocumentName = "orders-audit", DocumentText = "SELECT 1;", NextDefaultNumber = 2, SessionKey = "k" });
        _dialogs.PromptAnswer = r => r.InitialValue == "orders-audit" ? "orders audit v2.sql" : null;

        Assert.True(await _flow.SaveAsAsync());

        Assert.Single(_dialogs.Prompts);
        Assert.Equal("orders audit v2", _flow.Name);
        Assert.Equal("k", _flow.SessionKey);
        Assert.Equal("orders audit v2.sql", Assert.Single(_downloads.Files).Name);
        var record = await _store.RestoreAsync();
        Assert.Equal("orders audit v2", record!.DocumentName);
        Assert.Equal("k", record.SessionKey);
    }

    [Fact]
    public async Task A_programmatic_set_text_does_not_mark_the_document_modified()
    {
        await _flow.LoadAsync(null);

        await _flow.SetTextAsync("SELECT * FROM sys.objects;");   // e.g. History's "Open in editor"

        Assert.False(_flow.IsModified);
        await TypeAsync("SELECT * FROM sys.objects; -- edited");
        Assert.True(_flow.IsModified);
    }

    [Fact]
    public async Task Changed_is_raised_when_the_name_or_the_modified_state_changes()
    {
        await _flow.LoadAsync(null);
        int changed = 0;
        _flow.Changed += () => changed++;

        await TypeAsync("a");
        await TypeAsync("ab");   // still modified: no new event
        await _flow.NewAsync();

        Assert.Equal(2, changed);
    }

    // ── PR #255: History's Open and Re-execute ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Opening_a_history_query_over_a_modified_document_asks_and_Cancel_keeps_it()
    {
        await _store.SaveAsync(new EditorSessionRecord { DocumentName = "orders-audit", DocumentText = "edited", IsModified = true, NextDefaultNumber = 4, SessionKey = "k" });
        _dialogs.ConfirmAnswer = _ => false;

        Assert.False(await _flow.OpenQueryAsync("SELECT 42;", "Open in editor"));

        Assert.Contains("orders-audit", Assert.Single(_dialogs.Confirms).Message);
        var kept = await _store.RestoreAsync();
        Assert.Equal("edited", kept!.DocumentText);
        Assert.Equal("orders-audit", kept.DocumentName);
        Assert.True(kept.IsModified);
    }

    [Fact]
    public async Task A_history_query_opens_as_a_new_default_named_document()
    {
        await _store.SaveAsync(new EditorSessionRecord { DocumentName = "orders-audit", DocumentText = "SELECT 1;", IsModified = false, NextDefaultNumber = 4, SessionKey = "k" });

        Assert.True(await _flow.OpenQueryAsync("SELECT 42;", "Re-execute"));

        Assert.Empty(_dialogs.Confirms);   // nothing to lose: no question
        var opened = await _store.RestoreAsync();
        Assert.Equal("SELECT 42;", opened!.DocumentText);
        Assert.Equal("SQLQuery4", opened.DocumentName);   // never saved under the old document's name
        Assert.False(opened.IsModified);
        Assert.Equal(5, opened.NextDefaultNumber);
        Assert.NotEqual("k", opened.SessionKey);
        Assert.Equal("SQLQuery4", _flow.Name);
    }

    [Fact]
    public async Task Confirming_the_discard_opens_the_history_query()
    {
        await _store.SaveAsync(new EditorSessionRecord { DocumentName = "orders-audit", DocumentText = "edited", IsModified = true, NextDefaultNumber = 2 });
        _dialogs.ConfirmAnswer = _ => true;

        Assert.True(await _flow.OpenQueryAsync("SELECT 42;", "Open in editor"));

        Assert.Equal("SELECT 42;", (await _store.RestoreAsync())!.DocumentText);
    }
}
