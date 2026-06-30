using System;
using System.Collections.Generic;
using System.Linq;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 030 (web edition) — the registry behind the ⌘P command palette
/// (<c>Shared/CommandPalette.razor</c>). A singleton: the palette and the static-action
/// seeding read from it, while context-bearing pages (e.g. <c>Editor.razor</c>) register
/// their actions on mount and dispose the returned token on unmount.
///
/// <para>
/// Recents are kept in-memory only (most-recent-first, capped). Persisting them would need a
/// dedicated IndexedDB object store declared in <c>akml-indexeddb.js</c>; the palette never
/// requires recents to survive a reload, so we avoid that schema-migration risk.
/// </para>
/// </summary>
public interface ICommandRegistry
{
    /// <summary>
    /// Register a batch of actions. Returns an <see cref="IDisposable"/> that, when disposed,
    /// removes exactly these actions again — pages MUST dispose it on unmount so the palette can
    /// never run a closure over disposed page state.
    /// </summary>
    IDisposable Register(IEnumerable<CommandAction> actions);

    /// <summary>A point-in-time snapshot of every registered action.</summary>
    IReadOnlyList<CommandAction> Snapshot();

    /// <summary>The most-recently-run action ids, most-recent first (capped).</summary>
    IReadOnlyList<string> RecentIds { get; }

    /// <summary>Push <paramref name="id"/> to the front of the recents list (de-duplicated).</summary>
    void RecordUsed(string id);

    /// <summary>Raised whenever the set of registered actions changes (register / unregister).</summary>
    event Action? Changed;
}

/// <summary>
/// One command-palette entry. <see cref="Run"/> is a closure — for context actions it captures
/// page state (the live editor, the active profile…), which is exactly why the owning page must
/// unregister it on dispose.
/// </summary>
public sealed class CommandAction
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required CommandGroup Group { get; init; }

    /// <summary>A short glyph shown at the row's left edge (emoji / unicode — no icon font dependency).</summary>
    public string Icon { get; init; } = "•";

    /// <summary>Optional right-aligned shortcut hint, e.g. "Ctrl+K Ctrl+F".</summary>
    public string? Shortcut { get; init; }

    /// <summary>Optional muted meta text shown after the title (e.g. a route or object type).</summary>
    public string? Meta { get; init; }

    /// <summary>Invoked when the user runs this entry.</summary>
    public required Func<System.Threading.Tasks.Task> Run { get; init; }
}

/// <summary>The ranked result buckets, in display order.</summary>
public enum CommandGroup
{
    Recent = 0,
    Action = 1,
    File = 2,
    Object = 3,
}

internal sealed class CommandRegistry : ICommandRegistry
{
    private const int MaxRecents = 8;

    private readonly object _gate = new();
    // Each registration batch is its own list so disposing a token removes exactly that batch.
    private readonly List<List<CommandAction>> _batches = new();
    private readonly List<string> _recents = new();

    public event Action? Changed;

    public IDisposable Register(IEnumerable<CommandAction> actions)
    {
        if (actions == null) throw new ArgumentNullException(nameof(actions));
        var batch = actions.ToList();
        lock (_gate)
        {
            _batches.Add(batch);
        }
        Changed?.Invoke();
        return new Registration(this, batch);
    }

    public IReadOnlyList<CommandAction> Snapshot()
    {
        lock (_gate)
        {
            return _batches.SelectMany(b => b).ToList();
        }
    }

    public IReadOnlyList<string> RecentIds
    {
        get { lock (_gate) { return _recents.ToList(); } }
    }

    public void RecordUsed(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        lock (_gate)
        {
            _recents.Remove(id);
            _recents.Insert(0, id);
            if (_recents.Count > MaxRecents) _recents.RemoveRange(MaxRecents, _recents.Count - MaxRecents);
        }
        Changed?.Invoke();
    }

    private void Remove(List<CommandAction> batch)
    {
        bool removed;
        lock (_gate)
        {
            removed = _batches.Remove(batch);
        }
        if (removed) Changed?.Invoke();
    }

    private sealed class Registration : IDisposable
    {
        private CommandRegistry? _owner;
        private readonly List<CommandAction> _batch;

        public Registration(CommandRegistry owner, List<CommandAction> batch)
        {
            _owner = owner;
            _batch = batch;
        }

        public void Dispose()
        {
            // Idempotent — a page's Dispose may run more than once on some teardown paths.
            var owner = System.Threading.Interlocked.Exchange(ref _owner, null);
            owner?.Remove(_batch);
        }
    }
}
