#nullable enable
using System;
using System.Collections.Generic;
using AkmlSql.Core.Models.Tabs;
using Serilog;

namespace AkmlSql.Shell.Shared.Tabs
{
    /// <summary>
    /// Thread-safe LIFO stack of recently closed editor tabs. When the stack reaches
    /// its configured maximum capacity, the oldest (bottom) entry is evicted.
    /// <para>
    /// Used by <see cref="TabCloseMonitor"/> to capture closed documents and by
    /// <see cref="Commands.RestoreClosedTabCommand"/> to restore them.
    /// </para>
    /// </summary>
    internal sealed class ClosedTabStack
    {
        /// <summary>Singleton instance shared across all shell components.</summary>
        public static ClosedTabStack Instance { get; } = new ClosedTabStack();

        private readonly object _lock = new object();
        private readonly LinkedList<ClosedTabEntry> _entries = new LinkedList<ClosedTabEntry>();
        private int _maxCapacity;

        /// <summary>
        /// Creates a <see cref="ClosedTabStack"/> with the specified maximum capacity.
        /// </summary>
        /// <param name="maxCapacity">
        /// Maximum number of closed tab entries to retain.
        /// Defaults to 20 (matching <c>TabSettings.MaxClosedTabs</c>).
        /// </param>
        public ClosedTabStack(int maxCapacity = 20)
        {
            _maxCapacity = maxCapacity > 0 ? maxCapacity : 20;
        }

        /// <summary>
        /// Updates the maximum capacity. If the current count exceeds the new capacity,
        /// the oldest entries are evicted immediately.
        /// </summary>
        public void SetMaxCapacity(int maxCapacity)
        {
            if (maxCapacity < 1) maxCapacity = 1;

            lock (_lock)
            {
                _maxCapacity = maxCapacity;
                while (_entries.Count > _maxCapacity)
                {
                    _entries.RemoveLast(); // Evict oldest (bottom of stack)
                }
            }
        }

        /// <summary>
        /// Pushes a closed tab entry onto the top of the stack.
        /// If the stack is at capacity, the oldest entry is evicted.
        /// </summary>
        public void Push(ClosedTabEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            lock (_lock)
            {
                // Evict the oldest entry if we're at capacity
                while (_entries.Count >= _maxCapacity)
                {
                    _entries.RemoveLast();
                }

                _entries.AddFirst(entry); // Push to top (most recent)
            }

            Log.Debug("ClosedTabStack: pushed entry '{Title}', count={Count}",
                entry.TabTitle ?? "(untitled)", Count);
        }

        /// <summary>
        /// Removes and returns the most recently closed tab entry, or <c>null</c> if the stack is empty.
        /// </summary>
        public ClosedTabEntry? Pop()
        {
            lock (_lock)
            {
                if (_entries.Count == 0)
                    return null;

                var entry = _entries.First!.Value;
                _entries.RemoveFirst();

                Log.Debug("ClosedTabStack: popped entry '{Title}', remaining={Count}",
                    entry.TabTitle ?? "(untitled)", _entries.Count);

                return entry;
            }
        }

        /// <summary>
        /// Returns the most recently closed tab entry without removing it, or <c>null</c> if empty.
        /// </summary>
        public ClosedTabEntry? Peek()
        {
            lock (_lock)
            {
                return _entries.Count > 0 ? _entries.First!.Value : null;
            }
        }

        /// <summary>
        /// Returns all entries as a read-only list ordered from most recent to oldest.
        /// Intended for building a "recently closed" menu or display.
        /// </summary>
        public IReadOnlyList<ClosedTabEntry> GetAll()
        {
            lock (_lock)
            {
                var list = new List<ClosedTabEntry>(_entries.Count);
                foreach (var entry in _entries)
                {
                    list.Add(entry);
                }
                return list.AsReadOnly();
            }
        }

        /// <summary>Removes all entries from the stack.</summary>
        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
            }

            Log.Debug("ClosedTabStack: cleared");
        }

        /// <summary>Gets the current number of entries in the stack.</summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _entries.Count;
                }
            }
        }
    }
}
