namespace AkmlSql.Web.Shared;

/// <summary>
/// Spec 041 (research R7) — one entry of a <see cref="ContextMenu"/>: a command, a separator, or a
/// parent that opens a submenu (<see cref="Children"/>).
/// </summary>
public sealed class MenuItem
{
    public string Label { get; init; } = string.Empty;

    /// <summary>The key shown at the right, e.g. "Ctrl+Shift+C".</summary>
    public string? Shortcut { get; init; }

    /// <summary>Shown, focusable, but not runnable; <see cref="DisabledReason"/> says why.</summary>
    public bool Disabled { get; init; }

    public string? DisabledReason { get; init; }

    /// <summary>A submenu (e.g. Copy as ▸). A parent item has no <see cref="OnSelect"/>.</summary>
    public IReadOnlyList<MenuItem>? Children { get; init; }

    public Func<Task>? OnSelect { get; init; }

    public bool IsSeparator { get; init; }

    /// <summary>Null for a plain command; true/false for a checkable one (a visible panel).</summary>
    public bool? IsChecked { get; init; }

    /// <summary>The item's <c>data-testid</c>.</summary>
    public string? TestId { get; init; }

    public bool HasChildren => Children is { Count: > 0 };

    public static MenuItem Separator() => new() { IsSeparator = true };
}
