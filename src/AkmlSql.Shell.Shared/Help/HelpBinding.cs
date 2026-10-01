#nullable enable
using System;
using System.Windows;
using System.Windows.Input;
using Serilog;

namespace AkmlSql.Shell.Shared.Help
{
    /// <summary>
    /// Spec 040 (X-03, FR-062, research R26) — wires F1 on a WPF surface to the docs site.
    /// <see cref="Attach"/> adds a <see cref="CommandBinding"/> for
    /// <see cref="ApplicationCommands.Help"/> (WPF's built-in F1 gesture) whose handler opens
    /// the topic through <see cref="F1HelpListener.Open(string)"/>. The topic is read when F1
    /// is pressed, so a window whose topic follows its selection (the Options pages) passes a
    /// delegate rather than a fixed string.
    /// </summary>
    internal static class HelpBinding
    {
        /// <summary>
        /// Makes F1 (or any other source of <see cref="ApplicationCommands.Help"/>) inside
        /// <paramref name="element"/> open the topic <paramref name="topicKey"/> returns — a
        /// docs topic such as <c>topics/sql-history</c> or a registered context key.
        /// </summary>
        public static void Attach(UIElement element, Func<string?> topicKey)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (topicKey == null) throw new ArgumentNullException(nameof(topicKey));

            element.CommandBindings.Add(new CommandBinding(
                ApplicationCommands.Help,
                (_, e) =>
                {
                    e.Handled = true;
                    Open(topicKey);
                },
                (_, e) =>
                {
                    e.CanExecute = true;
                    e.Handled = true;
                }));
        }

        /// <summary>Opens the topic <paramref name="topicKey"/> returns; never throws.</summary>
        internal static bool Open(Func<string?> topicKey)
        {
            try
            {
                var key = topicKey();
                return !string.IsNullOrEmpty(key) && F1HelpListener.Default.Open(key!);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "HelpBinding: opening help failed");
                return false;
            }
        }
    }
}
