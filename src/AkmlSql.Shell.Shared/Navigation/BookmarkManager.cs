#nullable enable
using System;
using System.Collections.Generic;

namespace AkmlSql.Shell.Shared.Navigation
{
    /// <summary>
    /// Session-scoped bookmark store. Maps text view IDs to sorted line number sets.
    /// Thread-safe for UI-thread access (all VS editor operations are UI-thread).
    /// </summary>
    internal static class BookmarkManager
    {
        private static readonly Dictionary<string, SortedSet<int>> _bookmarks = new();

        public static void Toggle(string textViewId, int lineNumber)
        {
            if (!_bookmarks.TryGetValue(textViewId, out var lines))
            {
                lines = new SortedSet<int>();
                _bookmarks[textViewId] = lines;
            }
            if (!lines.Remove(lineNumber))
                lines.Add(lineNumber);
        }

        public static bool IsBookmarked(string textViewId, int lineNumber)
        {
            return _bookmarks.TryGetValue(textViewId, out var lines) && lines.Contains(lineNumber);
        }

        public static int NextBookmark(string textViewId, int currentLine)
        {
            if (!_bookmarks.TryGetValue(textViewId, out var lines) || lines.Count == 0)
                return -1;
            // Find first bookmark after currentLine
            foreach (var line in lines)
                if (line > currentLine) return line;
            // Wrap around to first bookmark
            return lines.Min;
        }

        public static int PreviousBookmark(string textViewId, int currentLine)
        {
            if (!_bookmarks.TryGetValue(textViewId, out var lines) || lines.Count == 0)
                return -1;
            // Find last bookmark strictly before currentLine (consistent with NextBookmark's > semantics)
            int prev = -1;
            foreach (var line in lines)
            {
                if (line < currentLine) prev = line;
            }
            return prev >= 0 ? prev : lines.Max; // Wrap around
        }

        public static IReadOnlyCollection<int> GetAll(string textViewId)
        {
            return _bookmarks.TryGetValue(textViewId, out var lines)
                ? (IReadOnlyCollection<int>)lines
                : Array.Empty<int>();
        }

        public static void Clear(string textViewId)
        {
            _bookmarks.Remove(textViewId);
        }
    }
}
