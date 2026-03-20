using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace AkmlSql.Formatter.Output;

/// <summary>
/// Converts DiffPlex output to unified diff format lines.
/// </summary>
public static class UnifiedDiffFormatter
{
    /// <summary>
    /// Produces unified diff output between <paramref name="original"/> and <paramref name="formatted"/>.
    /// Returns the diff lines (including headers). Empty list means no differences.
    /// </summary>
    public static List<DiffLine> ComputeDiff(string original, string formatted, string filePath)
    {
        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(original, formatted);

        var lines = new List<DiffLine>();
        bool hasDifferences = diff.Lines.Any(l => l.Type != ChangeType.Unchanged);

        if (!hasDifferences)
            return lines;

        // Add header
        lines.Add(new DiffLine(' ', $"--- {filePath}"));
        lines.Add(new DiffLine(' ', $"+++ {filePath} (formatted)"));

        // Convert to unified diff with context
        const int contextLines = 3;
        var allLines = diff.Lines;

        int i = 0;
        while (i < allLines.Count)
        {
            // Find next changed line
            if (allLines[i].Type == ChangeType.Unchanged)
            {
                i++;
                continue;
            }

            // Found a change - collect context before
            int hunkStart = Math.Max(0, i - contextLines);

            // Find the end of this hunk (including runs of changes with small gaps)
            int hunkEnd = i;
            while (hunkEnd < allLines.Count)
            {
                if (allLines[hunkEnd].Type != ChangeType.Unchanged)
                {
                    hunkEnd++;
                    continue;
                }

                // Check if there's another change within context distance
                int nextChange = hunkEnd;
                while (nextChange < allLines.Count && nextChange < hunkEnd + contextLines * 2 + 1)
                {
                    if (allLines[nextChange].Type != ChangeType.Unchanged)
                        break;
                    nextChange++;
                }

                if (nextChange < allLines.Count && nextChange < hunkEnd + contextLines * 2 + 1
                    && allLines[nextChange].Type != ChangeType.Unchanged)
                {
                    hunkEnd = nextChange + 1;
                    continue;
                }

                break;
            }

            hunkEnd = Math.Min(allLines.Count, hunkEnd + contextLines);

            // Emit hunk header
            int oldStart = hunkStart + 1;
            int oldCount = 0;
            int newCount = 0;
            for (int j = hunkStart; j < hunkEnd; j++)
            {
                switch (allLines[j].Type)
                {
                    case ChangeType.Unchanged:
                        oldCount++;
                        newCount++;
                        break;
                    case ChangeType.Deleted:
                        oldCount++;
                        break;
                    case ChangeType.Inserted:
                        newCount++;
                        break;
                    case ChangeType.Modified:
                        oldCount++;
                        newCount++;
                        break;
                }
            }

            lines.Add(new DiffLine('@', $"@@ -{oldStart},{oldCount} +{oldStart},{newCount} @@"));

            // Emit hunk lines
            for (int j = hunkStart; j < hunkEnd; j++)
            {
                switch (allLines[j].Type)
                {
                    case ChangeType.Unchanged:
                        lines.Add(new DiffLine(' ', allLines[j].Text ?? string.Empty));
                        break;
                    case ChangeType.Deleted:
                        lines.Add(new DiffLine('-', allLines[j].Text ?? string.Empty));
                        break;
                    case ChangeType.Inserted:
                        lines.Add(new DiffLine('+', allLines[j].Text ?? string.Empty));
                        break;
                    case ChangeType.Modified:
                        lines.Add(new DiffLine('-', allLines[j].Text ?? string.Empty));
                        lines.Add(new DiffLine('+', allLines[j].Text ?? string.Empty));
                        break;
                }
            }

            i = hunkEnd;
        }

        return lines;
    }

    /// <summary>
    /// Writes diff lines to stdout with coloring.
    /// </summary>
    public static void RenderToConsole(IReadOnlyList<DiffLine> lines)
    {
        foreach (var line in lines)
        {
            ConsoleRenderer.WriteDiffLine(line.Prefix, line.Text);
        }
    }

    /// <summary>
    /// Returns the diff as a plain string (no colors).
    /// </summary>
    public static string RenderToString(IReadOnlyList<DiffLine> lines)
    {
        if (lines.Count == 0) return string.Empty;
        return string.Join(Environment.NewLine, lines.Select(l => $"{l.Prefix}{l.Text}"));
    }
}

public readonly record struct DiffLine(char Prefix, string Text);
