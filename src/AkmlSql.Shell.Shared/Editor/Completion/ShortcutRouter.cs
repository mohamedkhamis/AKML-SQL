using System.Collections.Generic;

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
        /// <summary>
        /// The key after a kept Ctrl+K is not Y: keep it and type Ctrl+K and it again
        /// (<see cref="ShortcutRouter.ReplayChord"/>), so SSMS gets its own chord (Ctrl+K,C comments).
        /// </summary>
        Replay,
    }

    /// <summary>One synthetic key event the hook sends.</summary>
    internal readonly struct KeyStroke
    {
        public KeyStroke(int vk, bool up, int scan = 0, bool extended = false)
        {
            Vk = vk;
            Up = up;
            Scan = scan;
            Extended = extended;
        }

        public int Vk { get; }
        public bool Up { get; }
        /// <summary>The hardware scan code; 0 = look it up from <see cref="Vk"/>.</summary>
        public int Scan { get; }
        public bool Extended { get; }

        public override string ToString() => $"{Vk:X2} {(Up ? "up" : "down")}";
    }

    /// <summary>
    /// Decides what <see cref="KeyboardHook"/> does with a key-down, kept free of Win32 so it can be tested.
    ///
    /// The low-level hook sees the keyboard of the whole desktop, not just SSMS. It used to swallow every
    /// Ctrl+K / Ctrl+D / Ctrl+Shift+D on the machine, so while SSMS had a query open, Visual Studio's (and
    /// every other program's) Ctrl+K chords and Ctrl+D did nothing — and AKML formatted the SSMS query
    /// instead. A key is now ours only when SSMS's own window is in front AND an AKML SQL editor has the
    /// focus; anything else passes untouched and drops a half-typed chord.
    ///
    /// In the SQL editor Ctrl+K is still kept (SSMS's accelerators would otherwise take the chord before
    /// AKML's Ctrl+K,Y binding), but only until the next key: if that is not Y, Ctrl+K and the key are typed
    /// again for SSMS, so its own chords — Ctrl+K,C comment, Ctrl+K,U uncomment, Ctrl+K,Ctrl+K bookmark —
    /// work as before AKML.
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
        internal const int VK_RSHIFT = 0xA1;
        internal const int VK_LCONTROL = 0xA2;
        internal const int VK_LMENU = 0xA4;
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
        /// <param name="alt">Alt is held (Ctrl+Alt is AltGr on many layouts: typing, never a shortcut).</param>
        /// <param name="thisProcessInFront">The foreground window belongs to this SSMS process.</param>
        /// <param name="sqlEditorFocused">An AKML SQL editor has the keyboard focus.</param>
        public ShortcutAction OnLowLevelKeyDown(int vk, bool ctrl, bool shift, bool alt, bool thisProcessInFront, bool sqlEditorFocused)
        {
            if (!thisProcessInFront || !sqlEditorFocused)
            {
                _waitingForY = false;
                return ShortcutAction.Pass;
            }

            // A modifier pressed between the two keys of a chord, or before a shortcut.
            if (IsModifier(vk))
                return ShortcutAction.Pass;

            if (_waitingForY)
            {
                _waitingForY = false;

                if (vk == VK_Y)
                    return ShortcutAction.Format;   // Ctrl+K,Y and Ctrl+K,Ctrl+Y

                // Ctrl+D / Ctrl+Shift+D keep their meaning after a Ctrl+K.
                if (ctrl && !alt && vk == VK_D)
                    return shift ? ShortcutAction.Refresh : ShortcutAction.Format;

                // Any other chord is SSMS's own: hand it Ctrl+K and this key.
                return ShortcutAction.Replay;
            }

            if (!ctrl || alt)
                return ShortcutAction.Pass;

            // Ctrl+K starts the chord; keep it so SSMS does not start its own.
            if (!shift && vk == VK_K)
            {
                _waitingForY = true;
                return ShortcutAction.Swallow;
            }

            if (vk == VK_D)
                return shift ? ShortcutAction.Refresh : ShortcutAction.Format;

            return ShortcutAction.Pass;
        }

        /// <summary>
        /// A key-down seen by the thread hook (SSMS's UI thread only). Ctrl+Space is ours only in an AKML
        /// SQL editor; elsewhere in SSMS (another editor, a search box) it keeps its usual meaning.
        /// </summary>
        public static ShortcutAction OnThreadKeyDown(int vk, bool ctrl, bool sqlEditorFocused)
            => ctrl && vk == VK_SPACE && sqlEditorFocused ? ShortcutAction.Complete : ShortcutAction.Pass;

        /// <summary>
        /// The keys that hand SSMS the chord the hook interrupted: a plain Ctrl+K, then the second key with
        /// the modifiers the user is holding for it. Every modifier ends where the keyboard really is —
        /// one the user holds is never left released, one they do not hold is never left down.
        /// </summary>
        /// <param name="vk">The second key of the chord.</param>
        /// <param name="scan">Its hardware scan code.</param>
        /// <param name="extended">It is an extended key (arrows, Insert, Delete, Home, End, ...).</param>
        /// <param name="ctrlHeld">Ctrl is held for the second key (Ctrl+K,Ctrl+C) or not (Ctrl+K,C).</param>
        /// <param name="heldShiftAndAlt">The side-specific Shift and Alt keys held for the second key.</param>
        internal static KeyStroke[] ReplayChord(int vk, int scan, bool extended, bool ctrlHeld, IReadOnlyList<int> heldShiftAndAlt)
        {
            var keys = new List<KeyStroke>();

            // Ctrl+K exactly: Ctrl down, Shift and Alt up.
            if (!ctrlHeld)
                keys.Add(new KeyStroke(VK_LCONTROL, up: false));
            foreach (int m in heldShiftAndAlt)
                keys.Add(new KeyStroke(m, up: true, extended: m == VK_RMENU));
            keys.Add(new KeyStroke(VK_K, up: false));
            keys.Add(new KeyStroke(VK_K, up: true));

            // Back to the modifiers the user holds, then the second key as it was typed.
            for (int i = heldShiftAndAlt.Count - 1; i >= 0; i--)
                keys.Add(new KeyStroke(heldShiftAndAlt[i], up: false, extended: heldShiftAndAlt[i] == VK_RMENU));
            if (!ctrlHeld)
                keys.Add(new KeyStroke(VK_LCONTROL, up: true));
            keys.Add(new KeyStroke(vk, up: false, scan, extended));
            keys.Add(new KeyStroke(vk, up: true, scan, extended));

            return keys.ToArray();
        }

        /// <summary>Shift, Ctrl, Alt (generic and left/right) and the Windows keys.</summary>
        internal static bool IsModifier(int vk)
            => (vk >= VK_SHIFT && vk <= VK_MENU) || (vk >= VK_LSHIFT && vk <= VK_RMENU) || vk == VK_LWIN || vk == VK_RWIN;
    }
}
