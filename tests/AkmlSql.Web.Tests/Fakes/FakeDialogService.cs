using AkmlSql.Web.Services;

namespace AkmlSql.Web.Tests;

/// <summary>
/// Spec 041 — a scripted <see cref="IDialogService"/> for component tests. Records every request and
/// answers at once: confirmations with <see cref="ConfirmAnswer"/>, prompts with <see cref="PromptAnswer"/>.
/// No host renders anything, so the host-facing members are inert.
/// </summary>
internal sealed class FakeDialogService : IDialogService
{
    public List<DialogRequest> Confirms { get; } = new();
    public List<PromptRequest> Prompts { get; } = new();
    public List<(string Title, string Message)> Alerts { get; } = new();

    /// <summary>The answer to each confirmation; defaults to "yes".</summary>
    public Func<DialogRequest, bool> ConfirmAnswer { get; set; } = _ => true;

    /// <summary>The answer to each prompt; defaults to the initial value (null = cancelled).</summary>
    public Func<PromptRequest, string?> PromptAnswer { get; set; } = r => r.InitialValue;

    public Task<bool> ConfirmAsync(DialogRequest request)
    {
        Confirms.Add(request);
        return Task.FromResult(ConfirmAnswer(request));
    }

    public Task<string?> PromptAsync(PromptRequest request)
    {
        Prompts.Add(request);
        return Task.FromResult(PromptAnswer(request));
    }

    public Task AlertAsync(string title, string message)
    {
        Alerts.Add((title, message));
        return Task.CompletedTask;
    }

    public DialogEntry? Current => null;

#pragma warning disable CS0067 // never raised: no host in component tests
    public event Action? Changed;
#pragma warning restore CS0067

    public void Complete(DialogEntry entry, object? result) { }
}
