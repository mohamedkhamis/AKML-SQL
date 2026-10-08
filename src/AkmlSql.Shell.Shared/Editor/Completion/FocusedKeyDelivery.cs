#nullable enable
using System;
using System.Windows;
using System.Windows.Input;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>
    /// SSMS turns some keys pressed outside the editor — Enter in the Command Palette's search box,
    /// for one — into editor commands and sends them down the editor's command chain, so the window
    /// the user typed in never sees them. <see cref="CompletionController"/> keeps them out of the
    /// document and hands them back to the focused element with this, as the key events WPF would
    /// have raised: PreviewKeyDown, then KeyDown unless that was handled.
    /// </summary>
    internal static class FocusedKeyDelivery
    {
        /// <summary>
        /// Raises <paramref name="key"/> on <paramref name="target"/>; true when a handler handled it.
        /// False, and nothing raised, when the element is not in a window.
        /// </summary>
        internal static bool Deliver(UIElement target, Key key)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var source = PresentationSource.FromVisual(target);
            if (source == null) return false;

            var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            target.RaiseEvent(preview);
            if (preview.Handled) return true;

            var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            };
            target.RaiseEvent(down);
            return down.Handled;
        }
    }
}
