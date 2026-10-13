using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 041 (research R60, FR-086) — the one notification area for transient status messages
/// ("Imported 12 settings", "Copied 4 rows", "No problems found"). Rendered by
/// <c>Shared/NotificationHost.razor</c> in <c>MainLayout</c>. Execution outcomes go to the status bar
/// and form validation stays inline; this is for everything in between.
/// </summary>
public interface INotificationService
{
    /// <summary>Show a message. Information, success and warnings leave on their own after
    /// <paramref name="duration"/> (4 s by default); errors stay until dismissed. Empty text is ignored.</summary>
    void Notify(string text, NotificationKind kind = NotificationKind.Info, TimeSpan? duration = null);

    /// <summary>The messages on screen, oldest first (at most <see cref="NotificationService.MaxVisible"/>).</summary>
    IReadOnlyList<Notification> Visible { get; }

    /// <summary>Remove one message.</summary>
    void Dismiss(int id);

    /// <summary>Raised when <see cref="Visible"/> changes.</summary>
    event Action? Changed;
}

public enum NotificationKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>One message in the notification area.</summary>
public sealed class Notification
{
    internal Notification(int id, string text, NotificationKind kind)
    {
        Id = id;
        Text = text;
        Kind = kind;
    }

    public int Id { get; }
    public string Text { get; }
    public NotificationKind Kind { get; }
}

/// <summary>The app's <see cref="INotificationService"/>.</summary>
public sealed class NotificationService : INotificationService
{
    /// <summary>More than this and the oldest message goes first.</summary>
    public const int MaxVisible = 3;

    private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    private readonly List<Notification> _visible = new();
    private int _nextId;

    public event Action? Changed;

    public IReadOnlyList<Notification> Visible => _visible.ToArray();

    public void Notify(string text, NotificationKind kind = NotificationKind.Info, TimeSpan? duration = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var note = new Notification(++_nextId, text.Trim(), kind);
        _visible.Add(note);
        while (_visible.Count > MaxVisible) _visible.RemoveAt(0);
        Changed?.Invoke();

        if (kind != NotificationKind.Error)
        {
            _ = DismissLaterAsync(note.Id, duration ?? DefaultDuration);
        }
    }

    public void Dismiss(int id)
    {
        int removed = _visible.RemoveAll(n => n.Id == id);
        if (removed > 0) Changed?.Invoke();
    }

    private async Task DismissLaterAsync(int id, TimeSpan after)
    {
        try { await Task.Delay(after).ConfigureAwait(false); }
        catch (TaskCanceledException) { return; }
        Dismiss(id);
    }
}
