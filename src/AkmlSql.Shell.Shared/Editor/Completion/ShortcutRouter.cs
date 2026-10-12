namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>What the keyboard hook does with one key-down.</summary>
    internal enum ShortcutAction
    {
        /// <summary>Hand the key on unchanged — the default for anything that is not ours.</summary>
        Pass,
        /// <summary>Keep the key from its window (the Ctrl+K that starts the format chord).</summary>
        Swallow,
        /// <summary>Show AKML completion (Ctrl+Space) and keep the key.</summary>
        Complete,
        /// <summary>Format the document (Ctrl+K,Y or Ctrl+D) and keep the key.</summary>
        Format,
        /// <summary>Refresh the schema cache (Ctrl+Shift+D) and keep the key.</summary>
        Refresh,
    }

    /// <summary>
    /// Decides what <see cref="KeyboardHook"/> does with a key-down, kept free of Win32 so it can be tested.
    ///
    /// The low-level hook sees the keyboard of the whole desktop, not just SSMS. It used to swallow every
    /// Ctrl+K / Ctrl+D / Ctrl+Shift+D on the machine, so while SSMS had a query open, Visual Studio's (and
    /// every other program's) Ctrl+K chords and Ctrl+D did nothing — and AKML formatted the SSMS query
    /// instead. A key is now ours only when SSMS's own window is in front AND an AKML SQL editor has the
    /// focus; anything else passes untouched and drops a half-typed chord.
    /// </summary>
    internal sealed class ShortcutRouter
    {
        internal const int VK_SHIFT = 0x10;
        internal const int VK_CONTROL = 0x11;
        internal const int VK_MENU = 0x12;
        internal const int VK_SPACE = 0x20;
        internal const int VK_D = 0x44;
        internal const int VK_K = 0x4B;
        internal const int VK_Y = 0x59;
        internal const int VK_LWIN = 0x5B;
        internal const int VK_RWIN = 0x5C;
        internal const int VK_LSHIFT = 0xA0;
        internal const int VK_RMENU = 0xA5;

        private bool _waitingForY;

        /// <summary>True between the Ctrl+K of the format chord and the key after it.</summary>
        public bool WaitingForChord => _waitingForY;

        /// <summary>Drops a half-typed chord (SSMS lost the foreground, the hook came off).</summary>
        public void Reset() => _waitingForY = false;

        /// <summary>
        /// A key-down seen by the low-level (desktop-wide) hook.
        /// </summary>
        /// <param name="vk">The virtual-key code.</param>
        /// <param name="ctrl">Ctrl is held.</param>
        /// <param name="shift">Shift is held.</param>
        /// <param name="thisProcessInFront">The foreground window belongs to this SSMS process.</param>
        /// <param name="sqlEditorFocused">An AKML SQL editor has the keyboard focus.</param>
        public ShortcutAction OnLowLevelKeyDown(int vk, bool ctrl, bool shift, bool thisProcessInFront, bool sqlEditorFocused)
        {
            if (!thisProcessInFront || !sqlEditorFocused)
            {
                _waitingForY = false;
                return ShortcutAction.Pass;
            }

            // Ctrl+K starts the chord; keep it so SSMS does not start its own.
            if (ctrl && !shift && vk == VK_K)
            {
                _waitingForY = true;
                return ShortcutAction.Swallow;
            }

            if (_waitingForY && vk == VK_Y)
            {
                _waitingForY = false;
                return ShortcutAction.Format;
            }

            // Ctrl+Shift+D before Ctrl+D, which would otherwise take it.
            if (ctrl && shift && vk == VK_D)
            {
                _waitingForY = false;
                return ShortcutAction.Refresh;
            }

            if (ctrl && !shift && vk == VK_D)
            {
                _waitingForY = false;
                return ShortcutAction.Format;
            }

            // Any other key ends the chord; a modifier pressed between the two keys does not.
            if (_waitingForY && !IsModifier(vk))
                _waitingForY = false;

            return ShortcutAction.Pass;
        }

        /// <summary>
        /// A key-down seen by the thread hook (SSMS's UI thread only). Ctrl+Space is ours only in an AKML
        /// SQL editor; elsewhere in SSMS (another editor, a search box) it keeps its usual meaning.
        /// </summary>
        public static ShortcutAction OnThreadKeyDown(int vk, bool ctrl, bool sqlEditorFocused)
            => ctrl && vk == VK_SPACE && sqlEditorFocused ? ShortcutAction.Complete : ShortcutAction.Pass;

        /// <summary>Shift, Ctrl, Alt (generic and left/right) and the Windows keys.</summary>
        internal static bool IsModifier(int vk)
            => (vk >= VK_SHIFT && vk <= VK_MENU) || (vk >= VK_LSHIFT && vk <= VK_RMENU) || vk == VK_LWIN || vk == VK_RWIN;
    }
}
