using System;
using System.Collections.Generic;
using System.Linq;

namespace AKML.SQL.Core.DataStructures
{
    /// <summary>
    /// Trie (prefix tree) data structure for fast prefix-based lookups.
    /// Implements Story 4.2: Database Schema Harvesting & Caching.
    /// Supports O(m) lookup where m is the prefix length.
    /// </summary>
    /// <typeparam name="T">The type of value stored at each node.</typeparam>
    public class Trie<T> where T : class
    {
        private readonly TrieNode _root;
        private readonly bool _caseSensitive;
        private int _count;

        /// <summary>
        /// Initializes a new instance of the Trie class.
        /// </summary>
        /// <param name="caseSensitive">Whether lookups should be case-sensitive.</param>
        public Trie(bool caseSensitive = false)
        {
            _root = new TrieNode();
            _caseSensitive = caseSensitive;
            _count = 0;
        }

        /// <summary>
        /// Gets the number of items in the trie.
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// Adds a key-value pair to the trie.
        /// </summary>
        /// <param name="key">The key (e.g., table name).</param>
        /// <param name="value">The value to store.</param>
        public void Add(string key, T value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentNullException(nameof(key));

            var normalizedKey = NormalizeKey(key);
            var node = _root;

            foreach (var c in normalizedKey)
            {
                if (!node.Children.TryGetValue(c, out var child))
                {
                    child = new TrieNode();
                    node.Children[c] = child;
                }
                node = child;
            }

            if (!node.IsEndOfWord)
            {
                node.IsEndOfWord = true;
                _count++;
            }

            node.Value = value;
            node.OriginalKey = key; // Store original key for case preservation
        }

        /// <summary>
        /// Removes a key from the trie.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <returns>True if the key was found and removed.</returns>
        public bool Remove(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            var normalizedKey = NormalizeKey(key);
            var found = false;
            RemoveRecursive(_root, normalizedKey, 0, ref found);
            return found;
        }

        private bool RemoveRecursive(TrieNode node, string key, int depth, ref bool found)
        {
            if (depth == key.Length)
            {
                if (!node.IsEndOfWord)
                    return false;

                node.IsEndOfWord = false;
                node.Value = null;
                node.OriginalKey = null;
                _count--;
                found = true;
                return node.Children.Count == 0;
            }

            var c = key[depth];
            if (!node.Children.TryGetValue(c, out var child))
                return false;

            var shouldDeleteChild = RemoveRecursive(child, key, depth + 1, ref found);

            if (shouldDeleteChild)
            {
                node.Children.Remove(c);
                return node.Children.Count == 0 && !node.IsEndOfWord;
            }

            return false;
        }

        /// <summary>
        /// Checks if the trie contains a key.
        /// </summary>
        /// <param name="key">The key to find.</param>
        /// <returns>True if the key exists.</returns>
        public bool Contains(string key)
        {
            var node = FindNode(key);
            return node != null && node.IsEndOfWord;
        }

        /// <summary>
        /// Gets the value for a key.
        /// </summary>
        /// <param name="key">The key to find.</param>
        /// <param name="value">The value if found.</param>
        /// <returns>True if the key was found.</returns>
        public bool TryGetValue(string key, out T? value)
        {
            var node = FindNode(key);
            if (node != null && node.IsEndOfWord)
            {
                value = node.Value;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Gets all values matching a prefix.
        /// </summary>
        /// <param name="prefix">The prefix to search for.</param>
        /// <param name="maxResults">Maximum number of results to return.</param>
        /// <returns>Collection of matching values with their keys.</returns>
        public IEnumerable<TrieResult<T>> GetByPrefix(string prefix, int maxResults = int.MaxValue)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return GetAllValues(maxResults);
            }

            var normalizedPrefix = NormalizeKey(prefix);
            var node = _root;

            // Navigate to the prefix node
            foreach (var c in normalizedPrefix)
            {
                if (!node.Children.TryGetValue(c, out var child))
                {
                    return Enumerable.Empty<TrieResult<T>>();
                }
                node = child;
            }

            // Collect all values under this prefix
            var results = new List<TrieResult<T>>();
            CollectValues(node, prefix, results, maxResults);
            return results;
        }

        /// <summary>
        /// Gets all values in the trie.
        /// </summary>
        /// <param name="maxResults">Maximum number of results to return.</param>
        /// <returns>Collection of all values with their keys.</returns>
        public IEnumerable<TrieResult<T>> GetAllValues(int maxResults = int.MaxValue)
        {
            var results = new List<TrieResult<T>>();
            CollectValues(_root, string.Empty, results, maxResults);
            return results;
        }

        /// <summary>
        /// Performs a fuzzy search on the trie.
        /// </summary>
        /// <param name="query">The search query.</param>
        /// <param name="maxResults">Maximum number of results.</param>
        /// <param name="minScore">Minimum score threshold (0-100).</param>
        /// <returns>Scored search results ordered by relevance.</returns>
        public IEnumerable<TrieSearchResult<T>> FuzzySearch(string query, int maxResults = 50, int minScore = 20)
        {
            if (string.IsNullOrEmpty(query))
            {
                return GetAllValues(maxResults)
                    .Select(r => new TrieSearchResult<T>(r.Key, r.Value, 50));
            }

            var results = new List<TrieSearchResult<T>>();
            var normalizedQuery = NormalizeKey(query);

            FuzzySearchRecursive(_root, string.Empty, normalizedQuery, results, minScore);

            return results
                .OrderByDescending(r => r.Score)
                .ThenBy(r => r.Key.Length)
                .ThenBy(r => r.Key)
                .Take(maxResults);
        }

        private void FuzzySearchRecursive(TrieNode node, string currentKey, string query,
            List<TrieSearchResult<T>> results, int minScore)
        {
            if (node.IsEndOfWord && node.Value != null)
            {
                var score = CalculateFuzzyScore(node.OriginalKey ?? currentKey, query);
                if (score >= minScore)
                {
                    results.Add(new TrieSearchResult<T>(
                        node.OriginalKey ?? currentKey,
                        node.Value,
                        score));
                }
            }

            foreach (var kvp in node.Children)
            {
                FuzzySearchRecursive(kvp.Value, currentKey + kvp.Key, query, results, minScore);
            }
        }

        private int CalculateFuzzyScore(string text, string query)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
                return 0;

            var textLower = text.ToLowerInvariant();
            var queryLower = query.ToLowerInvariant();

            // Exact match
            if (textLower == queryLower)
                return 100;

            // Starts with (prefix match)
            if (textLower.StartsWith(queryLower))
                return 90 - Math.Min(20, text.Length - query.Length);

            // Contains as substring
            if (textLower.Contains(queryLower))
            {
                var index = textLower.IndexOf(queryLower);
                return 70 - Math.Min(30, index);
            }

            // Word boundary match (e.g., "CO" matches "CustomerOrder" at C and O)
            if (MatchesWordBoundaries(text, query))
            {
                return 60;
            }

            // Subsequence match (e.g., "cuord" matches "CustomerOrder")
            if (IsSubsequence(textLower, queryLower))
            {
                var matchRatio = (double)query.Length / text.Length;
                return 30 + (int)(matchRatio * 20);
            }

            return 0;
        }

        private bool MatchesWordBoundaries(string text, string query)
        {
            var queryIndex = 0;
            var queryLower = query.ToLowerInvariant();

            for (var i = 0; i < text.Length && queryIndex < query.Length; i++)
            {
                // Check if this is a word boundary (start, after underscore, or uppercase after lowercase)
                var isWordBoundary = i == 0 ||
                                     text[i - 1] == '_' ||
                                     (char.IsUpper(text[i]) && i > 0 && char.IsLower(text[i - 1]));

                if (isWordBoundary && char.ToLowerInvariant(text[i]) == queryLower[queryIndex])
                {
                    queryIndex++;
                }
            }

            return queryIndex == query.Length;
        }

        private bool IsSubsequence(string text, string query)
        {
            var queryIndex = 0;
            foreach (var c in text)
            {
                if (queryIndex < query.Length && c == query[queryIndex])
                {
                    queryIndex++;
                }
            }
            return queryIndex == query.Length;
        }

        private void CollectValues(TrieNode node, string currentKey, List<TrieResult<T>> results, int maxResults)
        {
            if (results.Count >= maxResults)
                return;

            if (node.IsEndOfWord && node.Value != null)
            {
                results.Add(new TrieResult<T>(node.OriginalKey ?? currentKey, node.Value));
            }

            foreach (var kvp in node.Children.OrderBy(x => x.Key))
            {
                if (results.Count >= maxResults)
                    break;

                CollectValues(kvp.Value, currentKey + kvp.Key, results, maxResults);
            }
        }

        private TrieNode? FindNode(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            var normalizedKey = NormalizeKey(key);
            var node = _root;

            foreach (var c in normalizedKey)
            {
                if (!node.Children.TryGetValue(c, out var child))
                    return null;
                node = child;
            }

            return node;
        }

        private string NormalizeKey(string key)
        {
            return _caseSensitive ? key : key.ToLowerInvariant();
        }

        /// <summary>
        /// Clears all items from the trie.
        /// </summary>
        public void Clear()
        {
            _root.Children.Clear();
            _count = 0;
        }

        /// <summary>
        /// Internal node class for the trie.
        /// </summary>
        private class TrieNode
        {
            public Dictionary<char, TrieNode> Children { get; } = new Dictionary<char, TrieNode>();
            public bool IsEndOfWord { get; set; }
            public T? Value { get; set; }
            public string? OriginalKey { get; set; }
        }
    }

    /// <summary>
    /// Result from a trie lookup.
    /// </summary>
    /// <typeparam name="T">The type of value.</typeparam>
    public class TrieResult<T>
    {
        public TrieResult(string key, T value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; }
        public T Value { get; }
    }

    /// <summary>
    /// Result from a fuzzy search with score.
    /// </summary>
    /// <typeparam name="T">The type of value.</typeparam>
    public class TrieSearchResult<T>
    {
        public TrieSearchResult(string key, T value, int score)
        {
            Key = key;
            Value = value;
            Score = score;
        }

        public string Key { get; }
        public T Value { get; }
        public int Score { get; }
    }
}
