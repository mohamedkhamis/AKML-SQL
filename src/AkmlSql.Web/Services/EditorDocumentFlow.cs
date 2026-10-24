using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace AkmlSql.Web.Services;

/// <summary>The editor's text, as the document flow reads and replaces it (the editor page implements it).</summary>
public interface IEditorText
{
    Task<string> GetTextAsync();

    /// <summary>Replace the whole text. The editor reports the change like a keystroke.</summary>
    Task SetTextAsync(string text);
}

/// <summary>Saves a text file through the browser's download (akml-download.js <c>downloadText</c>).</summary>
public interface IFileDownloader
{
    Task DownloadTextAsync(string fileName, string mimeType, string text, bool withBom);
}

internal sealed class JsFileDownloader : IFileDownloader, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public JsFileDownloader(IJSRuntime js) => _js = js ?? throw new ArgumentNullException(nameof(js));

    public async Task DownloadTextAsync(string fileName, string mimeType, string text, bool withBom)
    {
        _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/akml-download.js").ConfigureAwait(false);
        await _module.InvokeVoidAsync("downloadText", fileName, mimeType, text, withBom).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module == null) return;
        try { await _module.DisposeAsync().ConfigureAwait(false); }
        catch (JSDisconnectedException) { /* tab closing */ }
    }
}

/// <summary>
/// Spec 043 (research R1, R8): a document that belongs to a query tab. <see cref="Names"/> is the tab set's default-name
/// counter (a number is never handed out twice across tabs); <see cref="Persist"/> saves the whole tab record, which
/// <see cref="QueryTabs"/> builds — the flow never writes a record of its own when scoped.
/// </summary>
public sealed record DocumentScope(IDefaultNameCounter Names, Func<Task> Persist);

/// <summary>
/// Spec 041 (research R64, FR-015, FR-072, FR-073) — the editor's document: its name, whether it is modified, and
/// New query, Open, Save and Save as as SSMS does them.
/// <list type="bullet">
/// <item>Modified = the text differs from the baseline (the text at New, Open or Save). A programmatic set-text
/// moves the baseline first, so the change the editor then reports never marks the document modified.</item>
/// <item>New and Open confirm pending grid edits, then a modified document; each starts a document with a NEW
/// history session key. Default names count up (<c>SQLQuery{n}</c>) and are never reused.</item>
/// <item>Save asks for a name only while the document has a default one; Save as always asks and keeps the session
/// key. The file is <c>{name}.sql</c>, UTF-8 with a BOM.</item>
/// </list>
/// The page attaches its editor (<see cref="Attach"/>) and saves the session with <see cref="Name"/>,
/// <see cref="IsModified"/> and <see cref="NextDefaultNumber"/> on every text change.
/// </summary>
public sealed class EditorDocumentFlow
{
    /// <summary>The largest file Open reads: 10 MB of characters at up to 4 bytes each, plus a BOM.</summary>
    public const long MaxFileBytes = 4L * DocumentSizeLimit.MaxDocumentSizeChars + 4;

    private readonly IEditorSessionStore _store;
    private readonly IDialogService _dialogs;
    private readonly INotificationService _notifications;
    private readonly IFileDownloader _downloader;
    private IEditorText? _text;
    private Func<Task<bool>> _confirmPendingEdits = () => Task.FromResult(true);
    // The text the document was last opened, started or saved with; null when only the persisted flag is known.
    private string? _baseline;
    // While a New / Open replaces the text, the change the editor reports is part of it: one Changed at the end.
    private bool _quiet;
    // Spec 043: the query tab this document belongs to; null for the single document of before tabs.
    private readonly DocumentScope? _scope;
    private int _next = 1;

    public EditorDocumentFlow(IEditorSessionStore store, IDialogService dialogs, INotificationService notifications, IFileDownloader downloader,
        DocumentScope? scope = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _scope = scope;
    }

    public string Name { get; private set; } = EditorDocument.DefaultName(1);

    public bool IsModified { get; private set; }

    /// <summary>The number the next default name takes (spec 043: the tab set's counter for a tab's document).</summary>
    public int NextDefaultNumber
    {
        get => _scope?.Names.Next ?? _next;
        private set => _next = value;
    }

    /// <summary>Take the next default-name number: the tab set's counter for a tab's document, else this document's own.</summary>
    private int TakeNumber() => _scope?.Names.Take() ?? _next++;

    /// <summary>The history session key of the current document.</summary>
    public string? SessionKey { get; private set; }

    /// <summary>Raised when the name or the modified state changes.</summary>
    public event Action? Changed;

    /// <summary>The page's editor, and its "discard pending grid edits?" confirmation (FR-015).</summary>
    public void Attach(IEditorText text, Func<Task<bool>> confirmPendingEdits)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        _confirmPendingEdits = confirmPendingEdits ?? (() => Task.FromResult(true));
    }

    /// <summary>The document the page restored (null: a fresh editor, SQLQuery1). Never writes.</summary>
    public Task LoadAsync(EditorSessionRecord? restored)
    {
        if (_scope == null) NextDefaultNumber = Math.Max(1, restored?.NextDefaultNumber ?? 1);
        if (string.IsNullOrWhiteSpace(restored?.DocumentName))
        {
            // A fresh editor, or a record from before documents had names: it takes the next default name.
            Name = EditorDocument.DefaultName(TakeNumber());
        }
        else
        {
            Name = restored!.DocumentName!;
        }
        IsModified = restored?.IsModified ?? false;
        _baseline = IsModified ? null : restored?.DocumentText ?? string.Empty;
        SessionKey = restored?.SessionKey;
        if (_scope != null) SessionKey ??= Guid.NewGuid().ToString("N");   // every tab groups its own runs in History
        return Task.CompletedTask;
    }

    /// <summary>
    /// Spec 043 (R11, FR-011): an empty, unmodified tab takes a query or a file in place of opening another tab. It becomes a
    /// new document — <paramref name="name"/> (or the name it has), a new history session key, not modified. No question is
    /// asked: there is nothing to lose.
    /// </summary>
    public async Task ReplaceUnmodifiedAsync(string text, string? name)
    {
        await StartAsync(string.IsNullOrWhiteSpace(name) ? Name : name!, text ?? string.Empty).ConfigureAwait(false);
    }

    /// <summary>The page reports every text change (typing or a programmatic set).</summary>
    public void OnTextChanged(string text)
    {
        bool modified = _baseline == null || !string.Equals(text, _baseline, StringComparison.Ordinal);
        if (modified == IsModified) return;
        IsModified = modified;
        if (!_quiet) Changed?.Invoke();
    }

    /// <summary>Replace the text without marking the document modified (History's "Open in editor", a style
    /// change's re-format).</summary>
    public async Task SetTextAsync(string text)
    {
        _baseline = text ?? string.Empty;
        if (_text != null) await _text.SetTextAsync(_baseline).ConfigureAwait(false);
        OnTextChanged(_baseline);
    }

    /// <summary>The session key may be minted by the page before the flow knows it (first run).</summary>
    public void AdoptSessionKey(string key) => SessionKey ??= key;

    // ── New, Open ────────────────────────────────────────────────────────────────────────────────────────

    public Task<bool> NewAsync() => NewWithTextAsync(string.Empty, "Start a new query");

    /// <summary>
    /// New, already holding a script: the explorer's Select Top 1000 Rows and Script as. The script is the baseline, so the new
    /// document is not modified until it is edited. Asks the same "Discard the changes to {Name}?" first; false when the user kept theirs.
    /// </summary>
    public async Task<bool> NewWithTextAsync(string text, string action)
    {
        if (!await CanReplaceAsync(action).ConfigureAwait(false)) return false;
        var name = EditorDocument.DefaultName(TakeNumber());
        await StartAsync(name, text ?? string.Empty).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> OpenAsync(byte[] bytes, string fileName)
    {
        if (!await CanReplaceAsync("Open a file").ConfigureAwait(false)) return false;
        var decoded = EditorDocument.DecodeFile(bytes ?? Array.Empty<byte>());
        if (decoded.Error != null)
        {
            _notifications.Notify($"{fileName} was not opened. {decoded.Error}", NotificationKind.Error);
            return false;
        }
        var name = EditorDocument.NameFromFile(fileName, NextDefaultNumber);
        if (name == EditorDocument.DefaultName(NextDefaultNumber)) TakeNumber();   // a nameless file took the number
        await StartAsync(name, decoded.Text!).ConfigureAwait(false);
        _notifications.Notify($"Opened {fileName}.", NotificationKind.Success);
        return true;
    }

    /// <summary>
    /// History's Open and Re-execute: the query becomes a new document (the next default name, a new history session
    /// key), as Open does — after the same "Discard the changes to {Name}?" a modified document gets. Runs from the
    /// History page, where the editor is not on screen: the state comes from the saved session (the editor saves it
    /// on every change), and the editor shows the new document when it next opens. False when the user kept theirs.
    /// </summary>
    public async Task<bool> OpenQueryAsync(string text, string action)
    {
        if (_scope != null)
        {
            // Spec 043 (T031): a tab's document — the question a modified document gets, then a new document (the next default
            // name, a new history session key) written with the tab's own record.
            if (!await CanReplaceAsync(action).ConfigureAwait(false)) return false;
            await StartAsync(EditorDocument.DefaultName(TakeNumber()), text ?? string.Empty).ConfigureAwait(false);
            return true;
        }
        var record = await _store.RestoreAsync().ConfigureAwait(false);
        if (record?.IsModified == true)
        {
            var name = string.IsNullOrWhiteSpace(record.DocumentName) ? "the current query" : record.DocumentName;
            bool discard = await _dialogs.ConfirmAsync(new DialogRequest
            {
                Title = action,
                Message = $"Discard the changes to {name}?",
                Details = new[] { "Save it first (Ctrl+S) to keep them." },
                ConfirmLabel = "Discard changes",
                Destructive = true,
            }).ConfigureAwait(false);
            if (!discard) return false;
        }

        int number = Math.Max(1, record?.NextDefaultNumber ?? NextDefaultNumber);
        var documentName = EditorDocument.DefaultName(number);
        SessionKey = await EditorSessionKeys.StartDocumentAsync(_store, documentName, text ?? string.Empty, number + 1).ConfigureAwait(false);
        Name = documentName;
        NextDefaultNumber = number + 1;
        _baseline = text ?? string.Empty;
        IsModified = false;
        Changed?.Invoke();
        return true;
    }

    /// <summary>The refusal for a file too big to read at all (checked from its size before reading).</summary>
    public void RefuseTooLarge(string fileName) =>
        _notifications.Notify($"{fileName} was not opened. The file is larger than the 10 MB limit for a document.", NotificationKind.Error);

    private async Task<bool> CanReplaceAsync(string action)
    {
        if (!await _confirmPendingEdits().ConfigureAwait(false)) return false;
        if (!IsModified) return true;
        return await _dialogs.ConfirmAsync(new DialogRequest
        {
            Title = action,
            Message = $"Discard the changes to {Name}?",
            Details = new[] { "Save it first (Ctrl+S) to keep them." },
            ConfirmLabel = "Discard changes",
            Destructive = true,
        }).ConfigureAwait(false);
    }

    private async Task StartAsync(string name, string text)
    {
        // Spec 043: a tab's document is saved with its whole tab record (the scope); before tabs the single record was.
        SessionKey = _scope != null
            ? Guid.NewGuid().ToString("N")
            : await EditorSessionKeys.StartDocumentAsync(_store, name, text, NextDefaultNumber).ConfigureAwait(false);
        Name = name;
        _baseline = text;
        _quiet = true;
        try
        {
            if (_text != null) await _text.SetTextAsync(text).ConfigureAwait(false);
        }
        finally
        {
            _quiet = false;
        }
        IsModified = false;
        Changed?.Invoke();
        if (_scope != null) await _scope.Persist().ConfigureAwait(false);
    }

    // ── Save, Save as ────────────────────────────────────────────────────────────────────────────────────

    public async Task<bool> SaveAsync()
    {
        if (EditorDocument.IsDefaultName(Name))
            return await SaveUnderNewNameAsync("Save query").ConfigureAwait(false);
        await WriteFileAsync().ConfigureAwait(false);
        return true;
    }

    public Task<bool> SaveAsAsync() => SaveUnderNewNameAsync("Save query as");

    private async Task<bool> SaveUnderNewNameAsync(string title)
    {
        var answer = await _dialogs.PromptAsync(new PromptRequest
        {
            Title = title,
            Message = "The query is saved to your downloads as a .sql file.",
            Label = "File name",
            InitialValue = Name,
            ConfirmLabel = "Save",
            Validate = v => string.IsNullOrWhiteSpace(v) ? "Enter a name." : null,
        }).ConfigureAwait(false);
        if (answer == null) return false;
        Name = EditorDocument.SanitizeFileName(answer, NextDefaultNumber);
        await WriteFileAsync().ConfigureAwait(false);
        return true;
    }

    private async Task WriteFileAsync()
    {
        var text = _text != null ? await _text.GetTextAsync().ConfigureAwait(false) : _baseline ?? string.Empty;
        var fileName = Name + ".sql";
        await _downloader.DownloadTextAsync(fileName, "application/sql", text, withBom: true).ConfigureAwait(false);
        _baseline = text;
        IsModified = false;
        await PersistAsync(text).ConfigureAwait(false);
        Changed?.Invoke();
        _notifications.Notify($"Saved {fileName} to your downloads.", NotificationKind.Success);
    }

    /// <summary>Write the name and state into the session record, keeping its key (a rename is not a new document).</summary>
    private async Task PersistAsync(string text)
    {
        if (_scope != null)
        {
            await _scope.Persist().ConfigureAwait(false);   // spec 043: the tab record carries the name and the state
            return;
        }
        var record = await _store.RestoreAsync().ConfigureAwait(false) ?? new EditorSessionRecord();
        record.DocumentText = text;
        record.DocumentName = Name;
        record.IsModified = IsModified;
        record.NextDefaultNumber = NextDefaultNumber;
        record.SessionKey ??= SessionKey;
        SessionKey = record.SessionKey;
        await _store.SaveAsync(record).ConfigureAwait(false);
    }
}
