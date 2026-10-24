using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 041 (research R59, X7, FR-086) — the one in-app dialog for confirmations, prompts and alerts.
/// Every page, the command palette and every component awaits it instead of the browser's own
/// <c>confirm</c>/<c>prompt</c>/<c>alert</c>, which are never used.
///
/// <para>
/// Requests are queued: one dialog is shown at a time, in request order, by the
/// <c>Shared/DialogHost.razor</c> mounted once in <c>MainLayout</c>. The host-facing members
/// (<see cref="Current"/>, <see cref="Changed"/>, <see cref="Complete"/>) exist only for that host.
/// </para>
/// </summary>
public interface IDialogService
{
    /// <summary>Ask the user to confirm. True when they chose <see cref="DialogRequest.ConfirmLabel"/>;
    /// false on Cancel or Escape.</summary>
    Task<bool> ConfirmAsync(DialogRequest request);

    /// <summary>Ask for a line of text. The typed value, or null when the user cancelled.</summary>
    Task<string?> PromptAsync(PromptRequest request);

    /// <summary>
    /// Spec 043 (FR-007): a confirmation with a third button (<see cref="DialogRequest.AlternateLabel"/>) — "Save changes to
    /// SQLQuery3?" with Save, Don't save and Cancel. Cancel and Escape give <see cref="DialogChoice.Cancel"/>.
    /// </summary>
    async Task<DialogChoice> ChooseAsync(DialogRequest request) =>
        await ConfirmAsync(request).ConfigureAwait(false) ? DialogChoice.Confirm : DialogChoice.Cancel;

    /// <summary>Tell the user something that needs acknowledging. Completes when they close it.</summary>
    Task AlertAsync(string title, string message);

    /// <summary>Host-facing: the dialog to show now, or null.</summary>
    DialogEntry? Current { get; }

    /// <summary>Host-facing: raised when <see cref="Current"/> changes.</summary>
    event Action? Changed;

    /// <summary>Host-facing: close <paramref name="entry"/> with its result (<c>bool</c> for a
    /// confirmation, <c>string?</c> for a prompt, ignored for an alert) and show the next one.</summary>
    void Complete(DialogEntry entry, object? result);
}

/// <summary>What a confirmation dialog says. Defaults to a non-destructive OK / Cancel pair.</summary>
public sealed class DialogRequest
{
    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>Optional list under the message — for example everything a reset will remove.</summary>
    public IReadOnlyList<string>? Details { get; set; }

    public string ConfirmLabel { get; set; } = "OK";

    public string CancelLabel { get; set; } = "Cancel";

    /// <summary>The confirm action removes something. Its button takes the danger style and is never
    /// the Enter target — the user must click it (the WPF FR-005 safety convention).</summary>
    public bool Destructive { get; set; }

    /// <summary>Spec 042: an id for the dialog (<c>db-switch-confirm</c>), rendered as <c>data-dialog-id</c> so a test can tell
    /// one confirmation from another. Null for the dialogs that have none.</summary>
    public string? DialogId { get; set; }

    /// <summary>Spec 043: a third button between Cancel and the confirm button (<see cref="IDialogService.ChooseAsync"/>).</summary>
    public string? AlternateLabel { get; set; }

    /// <summary>Spec 043: the buttons' <c>data-testid</c>s when a dialog needs its own (<c>doc-close-save</c>); null keeps
    /// <c>dialog-confirm</c>, <c>dialog-cancel</c> and <c>dialog-alternate</c>.</summary>
    public string? ConfirmTestId { get; set; }

    public string? CancelTestId { get; set; }

    public string? AlternateTestId { get; set; }
}

/// <summary>Spec 043: the button a <see cref="IDialogService.ChooseAsync"/> dialog was closed with.</summary>
public enum DialogChoice
{
    Cancel,
    Confirm,
    Alternate,
}

/// <summary>What a prompt dialog asks for.</summary>
public sealed class PromptRequest
{
    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>The input's visible label (also its accessible name).</summary>
    public string Label { get; set; } = string.Empty;

    public string InitialValue { get; set; } = string.Empty;

    public string? Placeholder { get; set; }

    public string ConfirmLabel { get; set; } = "OK";

    public string CancelLabel { get; set; } = "Cancel";

    /// <summary>Returns an error message for an invalid value, or null when it is acceptable. While
    /// the value is invalid the message shows under the input and the confirm button is disabled.</summary>
    public Func<string, string?>? Validate { get; set; }

    public int MaxLength { get; set; } = 256;
}

/// <summary>The kind of dialog a <see cref="DialogEntry"/> shows.</summary>
public enum DialogKind
{
    Confirm,
    Prompt,
    Alert,
}

/// <summary>One queued dialog. Created by <see cref="DialogService"/>; read by the host.</summary>
public sealed class DialogEntry
{
    internal DialogEntry(DialogKind kind, DialogRequest? confirm, PromptRequest? prompt)
    {
        Kind = kind;
        Confirm = confirm;
        Prompt = prompt;
    }

    public DialogKind Kind { get; }

    /// <summary>Set for <see cref="DialogKind.Confirm"/> and <see cref="DialogKind.Alert"/>.</summary>
    public DialogRequest? Confirm { get; }

    /// <summary>Set for <see cref="DialogKind.Prompt"/>.</summary>
    public PromptRequest? Prompt { get; }

    internal TaskCompletionSource<object?> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>The app's <see cref="IDialogService"/>: a FIFO queue shown one entry at a time.</summary>
public sealed class DialogService : IDialogService
{
    private readonly Queue<DialogEntry> _queue = new();

    public event Action? Changed;

    public DialogEntry? Current => _queue.Count > 0 ? _queue.Peek() : null;

    public async Task<bool> ConfirmAsync(DialogRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var result = await Enqueue(new DialogEntry(DialogKind.Confirm, request, null)).ConfigureAwait(false);
        return result is true;
    }

    public async Task<string?> PromptAsync(PromptRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var result = await Enqueue(new DialogEntry(DialogKind.Prompt, null, request)).ConfigureAwait(false);
        return result as string;
    }

    public async Task<DialogChoice> ChooseAsync(DialogRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        var result = await Enqueue(new DialogEntry(DialogKind.Confirm, request, null)).ConfigureAwait(false);
        return result switch
        {
            true => DialogChoice.Confirm,
            DialogChoice choice => choice,
            _ => DialogChoice.Cancel,
        };
    }

    public Task AlertAsync(string title, string message) =>
        Enqueue(new DialogEntry(DialogKind.Alert, new DialogRequest { Title = title, Message = message }, null));

    public void Complete(DialogEntry entry, object? result)
    {
        if (entry == null || _queue.Count == 0 || !ReferenceEquals(_queue.Peek(), entry)) return;
        _queue.Dequeue();
        entry.Completion.TrySetResult(result);
        Changed?.Invoke();
    }

    private Task<object?> Enqueue(DialogEntry entry)
    {
        _queue.Enqueue(entry);
        if (_queue.Count == 1) Changed?.Invoke();
        return entry.Completion.Task;
    }
}
