#nullable enable
using System;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Spec 040 (HIS-02/HIS-14) — the queries that were open when SSMS last exited or crashed,
    /// as reported by the engine's ReconcileOpen at startup (grouped-row entry ids, newest first).
    /// Restore on start reads them.
    /// </summary>
    internal static class HistoryRestoreState
    {
        private static long[] _restorable = Array.Empty<long>();

        public static long[] RestorableEntryIds
        {
            get => _restorable;
            set => _restorable = value ?? Array.Empty<long>();
        }
    }
}
