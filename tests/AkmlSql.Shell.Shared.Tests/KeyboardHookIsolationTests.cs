#nullable enable
using System;
using System.Collections.Generic;
using AkmlSql.Shell.Shared.Editor.Completion;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// While SSMS 22 had a query open, Visual Studio's shortcuts (the Ctrl+K chords, Ctrl+D) stopped
    /// working until SSMS was closed: AKML's low-level keyboard hook sees the whole desktop and swallowed
    /// those keys in every program. The hook now acts only on keys typed into an AKML SQL editor in this
    /// SSMS, and is not installed at all while another program is active.
    /// </summary>
    public class KeyboardHookIsolationTests
    {
        private const int K = ShortcutRouter.VK_K, Y = ShortcutRouter.VK_Y, D = ShortcutRouter.VK_D;
        private const int C = 0x43, U = 0x55, A = 0x41;

        // ── ShortcutRouter: which keys are ours ─────────────────────────────────────────────────────────

        [Theory]
        [InlineData(K, true, false)]    // Ctrl+K — VS's comment / format / bookmark chords
        [InlineData(D, true, false)]    // Ctrl+D — VS's duplicate line
        [InlineData(D, true, true)]     // Ctrl+Shift+D
        [InlineData(Y, true, false)]
        [InlineData(A, false, false)]
        public void A_key_typed_into_another_program_passes_untouched(int vk, bool ctrl, bool shift)
        {
            var router = new ShortcutRouter();

            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(vk, ctrl, shift, thisProcessInFront: false, sqlEditorFocused: true));
            Assert.False(router.WaitingForChord);
        }

        [Fact]
        public void Visual_Studios_Ctrl_K_chords_reach_it_whole()
        {
            var router = new ShortcutRouter();

            // Ctrl+K, Ctrl+C in Visual Studio (comment selection) — both keys are VS's.
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(K, true, false, false, true));
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(C, true, false, false, true));
            // Ctrl+K, Y in Visual Studio is not an AKML format either.
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(K, true, false, false, true));
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(Y, false, false, false, true));
        }

        [Fact]
        public void In_SSMS_outside_a_SQL_editor_the_keys_keep_their_SSMS_meaning()
        {
            var router = new ShortcutRouter();

            // Object Explorer, a results grid, a dialog: SSMS's own Ctrl+K / Ctrl+D.
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(K, true, false, true, false));
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(D, true, false, true, false));
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(D, true, true, true, false));
        }

        [Fact]
        public void In_a_SQL_editor_Ctrl_K_Y_formats()
        {
            var router = new ShortcutRouter();

            Assert.Equal(ShortcutAction.Swallow, router.OnLowLevelKeyDown(K, true, false, true, true));
            Assert.True(router.WaitingForChord);
            Assert.Equal(ShortcutAction.Format, router.OnLowLevelKeyDown(Y, false, false, true, true));
            Assert.False(router.WaitingForChord);
        }

        [Fact]
        public void In_a_SQL_editor_Ctrl_D_formats_and_Ctrl_Shift_D_refreshes()
        {
            var router = new ShortcutRouter();

            Assert.Equal(ShortcutAction.Format, router.OnLowLevelKeyDown(D, true, false, true, true));
            Assert.Equal(ShortcutAction.Refresh, router.OnLowLevelKeyDown(D, true, true, true, true));
        }

        [Fact]
        public void A_half_typed_chord_is_dropped_when_another_program_comes_to_the_front()
        {
            var router = new ShortcutRouter();
            Assert.Equal(ShortcutAction.Swallow, router.OnLowLevelKeyDown(K, true, false, true, true));

            // Alt+Tab to Visual Studio, type Y: it is VS's letter, not an AKML format.
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(Y, false, false, false, true));
            Assert.False(router.WaitingForChord);
        }

        [Fact]
        public void Any_other_key_ends_the_chord_but_a_modifier_does_not()
        {
            var router = new ShortcutRouter();
            router.OnLowLevelKeyDown(K, true, false, true, true);

            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(0xA2, true, false, true, true));   // left Ctrl
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(0xA0, true, true, true, true));    // left Shift
            Assert.True(router.WaitingForChord);

            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(U, false, false, true, true));
            Assert.False(router.WaitingForChord);
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(Y, false, false, true, true));
        }

        [Theory]
        [InlineData(ShortcutRouter.VK_SPACE, true, true, true)]
        [InlineData(ShortcutRouter.VK_SPACE, true, false, false)]    // another SSMS editor or box
        [InlineData(ShortcutRouter.VK_SPACE, false, true, false)]
        [InlineData(A, true, true, false)]
        public void Ctrl_Space_is_ours_only_in_a_SQL_editor(int vk, bool ctrl, bool editor, bool completes)
        {
            Assert.Equal(completes ? ShortcutAction.Complete : ShortcutAction.Pass, ShortcutRouter.OnThreadKeyDown(vk, ctrl, editor));
        }

        // ── KeyboardHook: the desktop-wide hook exists only while SSMS is the active application ────────

        private sealed class FakeHooks : IWindowsHooks
        {
            private int _next = 100;
            public readonly HashSet<IntPtr> Thread = new HashSet<IntPtr>();
            public readonly HashSet<IntPtr> LowLevel = new HashSet<IntPtr>();
            public int LowLevelInstalls;

            public IntPtr SetThreadHook(KeyboardHook.HookProc proc, uint threadId) { var h = (IntPtr)_next++; Thread.Add(h); return h; }
            public IntPtr SetLowLevelHook(KeyboardHook.HookProc proc) { var h = (IntPtr)_next++; LowLevel.Add(h); LowLevelInstalls++; return h; }
            public void Remove(IntPtr handle) { Thread.Remove(handle); LowLevel.Remove(handle); }
        }

        private sealed class FakeActivation : IAppActivation
        {
            public bool Available = true;
            public Action? Activated, Deactivated;
            public bool Detached;

            public bool Attach(Action activated, Action deactivated)
            {
                if (!Available) return false;
                Activated = activated;
                Deactivated = deactivated;
                return true;
            }

            public void Detach() => Detached = true;
        }

        private sealed class Harness
        {
            public readonly FakeHooks Hooks = new FakeHooks();
            public readonly FakeActivation Activation = new FakeActivation();
            public bool InFront;
            public bool EditorFocused = true;
            public int Formats, Refreshes, Completions;
            public readonly KeyboardHook Hook;

            public Harness(bool inFront, bool activationAvailable = true)
            {
                InFront = inFront;
                Activation.Available = activationAvailable;
                Hook = new KeyboardHook(Hooks, () => InFront, Activation);
                Hook.Install(() => Completions++, () => Formats++, () => Refreshes++, () => EditorFocused);
            }
        }

        [Fact]
        public void Opening_a_query_while_another_program_is_active_installs_no_desktop_hook()
        {
            var h = new Harness(inFront: false);

            Assert.True(h.Hook.IsThreadHookInstalled);
            Assert.False(h.Hook.IsLowLevelHookInstalled);
            Assert.Empty(h.Hooks.LowLevel);
        }

        [Fact]
        public void The_desktop_hook_follows_SSMS_activation()
        {
            var h = new Harness(inFront: true);
            Assert.True(h.Hook.IsLowLevelHookInstalled);

            h.InFront = false;
            h.Activation.Deactivated!();   // Alt+Tab to Visual Studio
            Assert.False(h.Hook.IsLowLevelHookInstalled);
            Assert.Empty(h.Hooks.LowLevel);

            h.InFront = true;
            h.Activation.Activated!();     // back to SSMS
            Assert.True(h.Hook.IsLowLevelHookInstalled);
            Assert.Single(h.Hooks.LowLevel);

            h.Activation.Activated!();     // a second activation does not stack hooks
            Assert.Single(h.Hooks.LowLevel);
        }

        [Fact]
        public void Visual_Studios_keys_are_handed_on_even_if_the_hook_is_still_in_place()
        {
            // No WPF application to follow (or activation missed): the hook stays, the foreground test guards.
            var h = new Harness(inFront: false, activationAvailable: false);
            Assert.True(h.Hook.IsLowLevelHookInstalled);

            Assert.False(h.Hook.HandleLowLevelKeyDown(K, ctrl: true, shift: false));
            Assert.False(h.Hook.HandleLowLevelKeyDown(C, ctrl: true, shift: false));
            Assert.False(h.Hook.HandleLowLevelKeyDown(D, ctrl: true, shift: false));
            Assert.False(h.Hook.HandleLowLevelKeyDown(D, ctrl: true, shift: true));
            Assert.Equal(0, h.Formats);
            Assert.Equal(0, h.Refreshes);
        }

        [Fact]
        public void In_an_SSMS_SQL_editor_the_shortcuts_still_work()
        {
            var h = new Harness(inFront: true);

            Assert.True(h.Hook.HandleLowLevelKeyDown(K, true, false));
            Assert.True(h.Hook.HandleLowLevelKeyDown(Y, false, false));
            Assert.True(h.Hook.HandleLowLevelKeyDown(D, true, false));
            Assert.True(h.Hook.HandleLowLevelKeyDown(D, true, true));
            Assert.True(h.Hook.HandleThreadKeyDown(ShortcutRouter.VK_SPACE, true));

            Assert.Equal(2, h.Formats);
            Assert.Equal(1, h.Refreshes);
            Assert.Equal(1, h.Completions);
        }

        [Fact]
        public void Without_a_focused_SQL_editor_nothing_is_kept()
        {
            var h = new Harness(inFront: true) { EditorFocused = false };

            Assert.False(h.Hook.HandleLowLevelKeyDown(K, true, false));
            Assert.False(h.Hook.HandleLowLevelKeyDown(D, true, false));
            Assert.False(h.Hook.HandleThreadKeyDown(ShortcutRouter.VK_SPACE, true));
            Assert.Equal(0, h.Formats + h.Completions);
        }

        [Fact]
        public void Deactivation_drops_a_half_typed_chord()
        {
            var h = new Harness(inFront: true);
            Assert.True(h.Hook.HandleLowLevelKeyDown(K, true, false));

            h.Activation.Deactivated!();
            h.Activation.Activated!();

            Assert.False(h.Hook.HandleLowLevelKeyDown(Y, false, false));
            Assert.Equal(0, h.Formats);
        }

        [Fact]
        public void Uninstall_removes_both_hooks_and_stops_following_activation()
        {
            var h = new Harness(inFront: true);

            h.Hook.Dispose();

            Assert.Empty(h.Hooks.Thread);
            Assert.Empty(h.Hooks.LowLevel);
            Assert.True(h.Activation.Detached);

            h.Activation.Activated!();     // a late event after the last query closed
            Assert.Empty(h.Hooks.LowLevel);
        }

        [Fact]
        public void Typing_without_Ctrl_never_asks_which_window_is_in_front()
        {
            int asked = 0;
            var hook = new KeyboardHook(new FakeHooks(), () => { asked++; return true; }, new FakeActivation());
            hook.Install(() => { }, () => { }, () => { }, () => true);
            asked = 0;

            for (int vk = A; vk <= 0x5A; vk++)
                Assert.False(hook.HandleLowLevelKeyDown(vk, ctrl: false, shift: false));

            Assert.Equal(0, asked);
        }

        // ── The real Win32 hooks ────────────────────────────────────────────────────────────────────────

        [StaFact]
        public void The_real_desktop_hook_is_set_only_while_active_and_removed_on_deactivation()
        {
            var activation = new FakeActivation();
            using var hook = new KeyboardHook(Win32Hooks.Instance, () => false, activation);
            hook.Install(() => { }, () => { }, () => { }, () => true);

            Assert.True(hook.IsThreadHookInstalled);
            Assert.False(hook.IsLowLevelHookInstalled);

            activation.Activated!();
            Assert.True(hook.IsLowLevelHookInstalled);   // SetWindowsHookEx(WH_KEYBOARD_LL) accepted the module
            activation.Deactivated!();
            Assert.False(hook.IsLowLevelHookInstalled);
        }

        [Fact]
        public void The_test_runner_is_not_the_foreground_process()
        {
            // The real foreground test: the test host has no window, so whatever is in front is another
            // program — exactly the case where every key must pass.
            Assert.False(KeyboardHook.ForegroundIsThisProcess());
        }
    }
}
