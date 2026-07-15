using System.Collections.Generic;
using System.Windows;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Shared logical-tree walking for UI-structure tests — one recursion instead of a private
    /// copy per test class.
    /// </summary>
    internal static class LogicalTree
    {
        /// <summary>Depth-first logical descendants of <paramref name="root"/> of type <typeparamref name="T"/> (root excluded).</summary>
        public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is T match) yield return match;
                if (child is DependencyObject dep)
                {
                    foreach (var nested in Descendants<T>(dep))
                        yield return nested;
                }
            }
        }
    }
}
