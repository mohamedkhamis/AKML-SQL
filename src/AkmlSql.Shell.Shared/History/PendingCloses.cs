#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Spec 040 (HIS-02/HIS-14) — the tab closes <see cref="ExecutionCapture"/> has seen start but
    /// not yet finish. SSMS raises <c>DocumentClosing</c> first when a close starts — before the
    /// "save changes?" prompt, which the user can Cancel — and then again, twice, as the tab goes.
    /// Acting on the first event marked a cancelled close's query closed and recorded a draft for a
    /// tab still open. So a close waits here, keyed by document (several tabs closing at once
    /// interleave their events), with its text refreshed by each repeat, until the document has
    /// gone; then it is acted on once. A run or a save of the document cancels it.
    /// </summary>
    internal sealed class PendingCloses
    {
        /// <summary>A close still open after this long was cancelled with nothing to show for it; it is dropped.</summary>
        internal static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(10);

        internal sealed class Close
        {
            public Close(string name, DateTime startedUtc)
            {
                Name = name;
                StartedUtc = startedUtc;
            }

            /// <summary>The document's FullName (History's source).</summary>
            public string Name { get; }

            public DateTime StartedUtc { get; }

            /// <summary>The text at the latest close event — the repeats come just before the tab goes.</summary>
            public string? Content { get; set; }

            /// <summary>The query's History session when the close started.</summary>
            public string? SessionKey { get; set; }

            public string? TabTitle { get; set; }

            public (string? Server, string? Database) Connection { get; set; }

            /// <summary>Acted on (during shutdown, at once); kept so its repeats are still recognised.</summary>
            public bool Done { get; set; }
        }

        private readonly Dictionary<string, Close> _pending = new Dictionary<string, Close>(StringComparer.OrdinalIgnoreCase);

        internal bool Any => _pending.Values.Any(c => !c.Done);

        /// <summary>
        /// The close of <paramref name="name"/> at <paramref name="nowUtc"/>: a new pending close
        /// (true), or SSMS repeating one already pending (false) — the existing one is returned
        /// either way, for the caller to refresh its text.
        /// </summary>
        internal bool Begin(string name, DateTime nowUtc, out Close close)
        {
            if (_pending.TryGetValue(name, out var existing))
            {
                close = existing;
                return false;
            }
            close = new Close(name, nowUtc);
            _pending[name] = close;
            return true;
        }

        /// <summary>The document is still in use (it ran, it was saved): its close was cancelled.</summary>
        internal bool Cancel(string? name) => name != null && _pending.TryGetValue(name, out var close) && !close.Done && _pending.Remove(name);

        /// <summary>
        /// The pending closes whose document has gone (<paramref name="isOpen"/> false), removed;
        /// those still open after <see cref="GiveUpAfter"/> are dropped. Done ones are forgotten
        /// once their document has gone.
        /// </summary>
        internal List<Close> TakeFinished(Func<string, bool> isOpen, DateTime nowUtc)
        {
            var finished = new List<Close>();
            foreach (var close in _pending.Values.ToList())
            {
                if (!isOpen(close.Name))
                {
                    _pending.Remove(close.Name);
                    if (!close.Done) finished.Add(close);
                }
                else if (nowUtc - close.StartedUtc >= GiveUpAfter)
                {
                    _pending.Remove(close.Name);
                }
            }
            return finished;
        }
    }
}
