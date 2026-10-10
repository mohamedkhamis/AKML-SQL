using AkmlSql.Web.Services;

namespace AkmlSql.Web.Tests;

/// <summary>Spec 041 — records notifications for component tests; nothing is shown or timed.</summary>
internal sealed class FakeNotificationService : INotificationService
{
    public List<(string Text, NotificationKind Kind)> Sent { get; } = new();

    public void Notify(string text, NotificationKind kind = NotificationKind.Info, TimeSpan? duration = null)
    {
        if (!string.IsNullOrWhiteSpace(text)) Sent.Add((text, kind));
    }

    public IReadOnlyList<Notification> Visible => Array.Empty<Notification>();

    public void Dismiss(int id) { }

#pragma warning disable CS0067 // never raised: nothing is shown in component tests
    public event Action? Changed;
#pragma warning restore CS0067
}
