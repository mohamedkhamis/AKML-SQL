using AkmlSql.Core;

namespace AkmlSql.Web.Services;

/// <summary>The web surfaces that exist only for SQL Server in this release (spec 042, FR-092).</summary>
public enum WorkspaceSurface
{
    /// <summary>The Refactor menu: the lightweight operations and the schema-aware ones. Both are written for T-SQL.</summary>
    Refactor,

    /// <summary>Find invalid objects. The desktop edition has it; the web edition has no entry for it yet, so this is a guard for the day it does.</summary>
    FindInvalidObjects,
}

/// <summary>
/// Spec 042 (FR-092, SC-010). Which surfaces a connection kind may use. A surface with no PostgreSQL implementation is shown
/// disabled with <see cref="DialectMessages.NotAvailable"/>, its keyboard and palette routes answer with the same words, and the
/// T-SQL implementation never runs on PostgreSQL text. The engine refuses the same surfaces for a PostgreSQL session, so a client
/// that skips this still cannot reach the T-SQL code (<c>DialectGuard</c>).
/// </summary>
public static class UnsupportedSurfaces
{
    /// <summary>The value of the <c>data-unavailable</c> attribute on a disabled control; tests and styling read it.</summary>
    public const string Marker = "postgresql";

    public static bool IsAvailable(WorkspaceSurface surface, DatabaseKind kind) => kind != DatabaseKind.PostgreSql;

    /// <summary>Why the surface is off, or null while it is on.</summary>
    public static string? Reason(WorkspaceSurface surface, DatabaseKind kind) =>
        IsAvailable(surface, kind) ? null : DialectMessages.NotAvailable;

    /// <summary>The gated surface a command id stands for, or null for any other command.</summary>
    public static WorkspaceSurface? SurfaceOf(string commandId) => commandId switch
    {
        "editor:refactor" => WorkspaceSurface.Refactor,
        _ => null,
    };
}

/// <summary>
/// What a page asks about the surfaces it offers, for the connection that is active now. One instance per page; it holds no state
/// of its own, so it follows a switch of connection by asking the gate each time.
/// </summary>
public sealed class SurfaceGate
{
    private readonly IDialectGate _gate;
    private readonly INotificationService _notifications;

    public SurfaceGate(IDialectGate gate, INotificationService notifications)
    {
        _gate = gate ?? throw new System.ArgumentNullException(nameof(gate));
        _notifications = notifications ?? throw new System.ArgumentNullException(nameof(notifications));
    }

    public bool IsEnabled(WorkspaceSurface surface) => UnsupportedSurfaces.IsAvailable(surface, _gate.Kind);

    /// <summary>The control's tooltip: the reason when the surface is off, otherwise the control's own title.</summary>
    public string? Tooltip(WorkspaceSurface surface, string? ownTitle) => UnsupportedSurfaces.Reason(surface, _gate.Kind) ?? ownTitle;

    /// <summary>The value for <c>data-unavailable</c> when the surface is off, otherwise null (the attribute is not written).</summary>
    public string? Unavailable(WorkspaceSurface surface) => IsEnabled(surface) ? null : UnsupportedSurfaces.Marker;

    /// <summary>
    /// A command that did not run because it is not available: when the reason is the connection's kind, say so. Returns true when
    /// it did. Any other command, and any command on SQL Server, is not this gate's business.
    /// </summary>
    public bool ExplainRefusal(string commandId)
    {
        if (UnsupportedSurfaces.SurfaceOf(commandId) is not { } surface) return false;
        var reason = UnsupportedSurfaces.Reason(surface, _gate.Kind);
        if (reason == null) return false;

        _notifications.Notify(reason, NotificationKind.Info);
        return true;
    }
}
