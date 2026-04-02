#nullable enable
using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Models.Productivity;

namespace AkmlSql.Shell.Shared.Productivity.CommandPalette
{
    /// <summary>
    /// T034: Character-subsequence fuzzy matcher for the Command Palette.
    /// Scores based on consecutive matches, word-boundary bonuses, and prefix match bonus.
    /// Returns a sorted list of (CommandEntry, score) tuples.
    /// </summary>
    internal static class FuzzyMatcher
    {
        /// <summary>
        /// Matches a search query against a list of command entries using
        /// character-subsequence matching with scoring.
        /// </summary>
        /// <param name="query">The user's search text.</param>
        /// <param name="commands">All available commands.</param>
        /// <returns>Matching commands sorted by descending score, with their match scores.</returns>
        public static List<(CommandEntry Entry, double Score)> Match(
            string query, IReadOnlyList<CommandEntry> commands)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                // Return all commands with a base score of 0 when no search text
                return commands
                    .Select(c => (c, 0.0))
                    .ToList();
            }

            var results = new List<(CommandEntry Entry, double Score)>();
            var queryLower = query.ToLowerInvariant();

            foreach (var command in commands)
            {
                var score = ComputeScore(queryLower, command);
                if (score > 0)
                {
                    results.Add((command, score));
                }
            }

            return results
                .OrderByDescending(r => r.Score)
                .ToList();
        }

        /// <summary>
        /// Computes a match score for a single command against the search query.
        /// Returns 0 if the query does not match as a subsequence.
        /// </summary>
        private static double ComputeScore(string queryLower, CommandEntry command)
        {
            var nameLower = command.Name.ToLowerInvariant();
            var categoryLower = command.Category.ToLowerInvariant();
            var candidateLower = nameLower;

            // Try matching against the name first, then category + name
            var nameScore = ComputeSubsequenceScore(queryLower, candidateLower, command.Name);
            if (nameScore > 0) return nameScore;

            // Try matching against category:name combined
            var combined = categoryLower + " " + nameLower;
            var combinedOriginal = command.Category + " " + command.Name;
            var combinedScore = ComputeSubsequenceScore(queryLower, combined, combinedOriginal);
            if (combinedScore > 0) return combinedScore * 0.8; // Slight penalty for category match

            // Try matching against the command ID
            var idLower = command.Id.ToLowerInvariant();
            var idScore = ComputeSubsequenceScore(queryLower, idLower, command.Id);
            if (idScore > 0) return idScore * 0.5; // Penalty for ID match

            return 0;
        }

        /// <summary>
        /// Core scoring: checks if query is a subsequence of candidate and computes score.
        /// </summary>
        private static double ComputeSubsequenceScore(string queryLower, string candidateLower, string originalCandidate)
        {
            if (queryLower.Length == 0) return 0;
            if (queryLower.Length > candidateLower.Length) return 0;

            int queryIdx = 0;
            double score = 0;
            int consecutiveCount = 0;
            int lastMatchIdx = -2; // -2 so first match is not "consecutive"
            bool prefixMatched = false;

            for (int i = 0; i < candidateLower.Length && queryIdx < queryLower.Length; i++)
            {
                if (candidateLower[i] == queryLower[queryIdx])
                {
                    // Base match score
                    score += 1.0;

                    // Consecutive match bonus
                    if (i == lastMatchIdx + 1)
                    {
                        consecutiveCount++;
                        score += consecutiveCount * 2.0;
                    }
                    else
                    {
                        consecutiveCount = 0;
                    }

                    // Prefix match bonus (query starts matching from the beginning)
                    if (queryIdx == 0 && i == 0)
                    {
                        prefixMatched = true;
                        score += 10.0;
                    }

                    // Word boundary bonus (match at start of a word)
                    if (i > 0 && IsWordBoundary(candidateLower, i))
                    {
                        score += 5.0;
                    }

                    // CamelCase / PascalCase boundary bonus
                    if (i > 0 && i < originalCandidate.Length &&
                        char.IsUpper(originalCandidate[i]) && char.IsLower(originalCandidate[i - 1]))
                    {
                        score += 3.0;
                    }

                    lastMatchIdx = i;
                    queryIdx++;
                }
            }

            // Did we match all characters in the query?
            if (queryIdx < queryLower.Length)
                return 0; // Not a subsequence match

            // Exact match bonus
            if (candidateLower == queryLower)
                score += 50.0;

            // Starts-with bonus (weaker than exact match but stronger than subsequence)
            if (!prefixMatched && candidateLower.StartsWith(queryLower))
                score += 20.0;

            // Length ratio bonus (prefer shorter candidates that match)
            score += (double)queryLower.Length / candidateLower.Length * 5.0;

            return score;
        }

        /// <summary>
        /// Determines if position i is at a word boundary (preceded by space, underscore, dot, or dash).
        /// </summary>
        private static bool IsWordBoundary(string text, int i)
        {
            if (i <= 0 || i >= text.Length) return false;
            var prev = text[i - 1];
            return prev == ' ' || prev == '_' || prev == '.' || prev == '-' || prev == '/';
        }
    }
}
