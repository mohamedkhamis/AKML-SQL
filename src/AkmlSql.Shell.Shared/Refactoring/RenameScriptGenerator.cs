#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Shell.Shared.Refactoring
{
    /// <summary>
    /// Converts <see cref="RefactorChangeInfo"/> arrays from the engine's rename preview
    /// into a complete SQL script that users can review, modify, and execute manually.
    /// Per spec clarification: Safe Rename does NOT execute directly — it generates a script.
    /// </summary>
    internal static class RenameScriptGenerator
    {
        private const string Separator = "-- ============================================================";
        private const string SectionRule = "-- ── ";
        private const string SectionPad = " ────────────────────────────────────";

        /// <summary>
        /// Generates a SQL rename script from the given change info array.
        /// Groups changes by file/object and wraps in a transaction.
        /// </summary>
        public static string Generate(
            RefactorChangeInfo[] changes,
            string originalName,
            string newName,
            string[] warnings)
        {
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            if (changes.Length == 0) return string.Empty;

            var sb = new StringBuilder(4096);

            // ── Header ──────────────────────────────────────────────────
            AppendHeader(sb, originalName, newName, warnings);

            // ── Transaction open ────────────────────────────────────────
            sb.AppendLine("SET XACT_ABORT ON;");
            sb.AppendLine("BEGIN TRANSACTION;");
            sb.AppendLine("GO");
            sb.AppendLine();

            // ── Group changes by FilePath ───────────────────────────────
            var groups = changes
                .GroupBy(c => c.FilePath ?? string.Empty)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int objectCount = groups.Count;

            foreach (var group in groups)
            {
                string label = GetGroupLabel(group.Key);
                sb.Append(SectionRule).Append(label).AppendLine(SectionPad);

                foreach (var change in group.OrderBy(c => c.Line).ThenBy(c => c.Column))
                {
                    AppendChangeComment(sb, change);
                }

                sb.AppendLine();
            }

            // ── Transaction close ───────────────────────────────────────
            sb.AppendLine("COMMIT TRANSACTION;");
            sb.AppendLine("GO");
            sb.AppendLine();

            // ── Footer ─────────────────────────────────────────────────
            sb.AppendLine(Separator);
            sb.AppendLine($"-- Rename complete. {changes.Length} change(s) across {objectCount} object(s).");
            sb.AppendLine(Separator);

            return sb.ToString();
        }

        private static void AppendHeader(
            StringBuilder sb,
            string originalName,
            string newName,
            string[] warnings)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            sb.AppendLine(Separator);
            sb.AppendLine("-- AKML SQL \u2014 Safe Rename Script");
            sb.AppendLine($"-- Generated: {timestamp}");
            sb.AppendLine($"-- Rename: \"{EscapeComment(originalName)}\" \u2192 \"{EscapeComment(newName)}\"");
            sb.AppendLine(Separator);
            sb.AppendLine("-- WARNING: Review this script carefully before executing.");
            sb.AppendLine("-- This script was generated automatically and should be verified.");

            if (warnings != null && warnings.Length > 0)
            {
                sb.AppendLine("--");
                foreach (string warning in warnings)
                {
                    sb.AppendLine($"-- WARNING: {EscapeComment(warning)}");
                }
            }

            sb.AppendLine(Separator);
            sb.AppendLine();
        }

        /// <summary>
        /// Derives a display label for a change group.
        /// Empty FilePath means the current editor document;
        /// otherwise extracts the object name from the file path.
        /// </summary>
        private static string GetGroupLabel(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return "Current Document";

            // FilePath may be a database object identifier like "dbo.GetOrders (Stored Procedure)"
            // or an actual file path. Use the value as-is if it contains a dot-qualified name;
            // otherwise fall back to the file name.
            if (filePath.Contains("(") || filePath.Contains("."))
                return filePath;

            try
            {
                return Path.GetFileName(filePath);
            }
            catch
            {
                return filePath;
            }
        }

        private static void AppendChangeComment(StringBuilder sb, RefactorChangeInfo change)
        {
            sb.Append($"-- Line {change.Line}, Col {change.Column}: ");
            sb.Append(EscapeComment(change.OldText));
            sb.Append(" \u2192 ");
            sb.AppendLine(EscapeComment(change.NewText));

            if (!string.IsNullOrEmpty(change.ContextSnippet))
            {
                string snippet = change.ContextSnippet.Trim();
                // Truncate long snippets for readability
                if (snippet.Length > 120)
                    snippet = snippet.Substring(0, 117) + "...";

                sb.AppendLine($"-- Context: ...{EscapeComment(snippet)}...");
            }

            sb.AppendLine();
        }

        /// <summary>
        /// Ensures text embedded in SQL comments does not contain newlines
        /// (which would break out of the comment) or other problematic sequences.
        /// </summary>
        private static string EscapeComment(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text
                .Replace("\r\n", " ")
                .Replace("\r", " ")
                .Replace("\n", " ");
        }
    }
}
