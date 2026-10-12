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

            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(vk, ctrl, shift, false, thisProcessInFront: false, sqlEditorFocused: true));
            Assert.False(router.WaitingForChord);
        }

        [Fact]
        public void Visual_Studios_Ctrl_K_chords_reach_it_whole()
        {
            var router = new ShortcutRouter();

            // Ctrl+K, Ctrl+C in Visual Studio (comment selection) — both keys are VS's.
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(K, true, false, false, false, true));
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(C, true, false, false, false, true));
            // Ctrl+K, Y in Visual Studio is not an AKML format either.
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(K, true, false, false, false, true));
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(Y, false, false, false, false, true));
        }

        [Fact]
        public void In_SSMS_outside_a_SQL_editor_the_keys_keep_their_SSMS_meaning()
        {
            var router = new ShortcutRouter();

            // Object Explorer, a results grid, a dialog: SSMS's own Ctrl+K / Ctrl+D.
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(K, true, false, false, true, false));
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(D, true, false, false, true, false));
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(D, true, true, false, true, false));
        }

        [Fact]
        public void In_a_SQL_editor_Ctrl_K_Y_formats()
        {
            var router = new ShortcutRouter();

            Assert.Equal(ShortcutAction.Swallow, router.OnLowLevelKeyDown(K, true, false, false, true, true));
            Assert.True(router.WaitingForChord);
            Assert.Equal(ShortcutAction.Format, router.OnLowLevelKeyDown(Y, false, false, false, true, true));
            Assert.False(router.WaitingForChord);
        }

        [Fact]
        public void In_a_SQL_editor_Ctrl_D_formats_and_Ctrl_Shift_D_refreshes()
        {
            var router = new ShortcutRouter();

            Assert.Equal(ShortcutAction.Format, router.OnLowLevelKeyDown(D, true, false, false, true, true));
            Assert.Equal(ShortcutAction.Refresh, router.OnLowLevelKeyDown(D, true, true, false, true, true));
        }

        [Fact]
        public void A_half_typed_chord_is_dropped_when_another_program_comes_to_the_front()
        {
            var router = new ShortcutRouter();
            Assert.Equal(ShortcutAction.Swallow, router.OnLowLevelKeyDown(K, true, false, false, true, true));

            // Alt+Tab to Visual Studio, type Y: it is VS's letter, not an AKML format.
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(Y, false, false, false, false, true));
            Assert.False(router.WaitingForChord);
        }

        [Fact]
        public void Any_other_key_ends_the_chord_but_a_modifier_does_not()
        {
            var router = new ShortcutRouter();
            router.OnLowLevelKeyDown(K, true, false, false, true, true);

            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(0xA2, true, false, false, true, true));   // left Ctrl
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(0xA0, true, true, false, true, true));    // left Shift
            Assert.True(router.WaitingForChord);

            Assert.Equal(ShortcutAction.Replay, router.OnLowLevelKeyDown(U, false, false, false, true, true));
            Assert.False(router.WaitingForChord);
            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(Y, false, false, false, true, true));
        }

        // ── SSMS's own Ctrl+K chords in the SQL editor ──────────────────────────────────────────────────

        [Theory]
        [InlineData(C, true)]     // Ctrl+K, Ctrl+C — comment selection
        [InlineData(C, false)]    // Ctrl+K, C
        [InlineData(U, true)]     // Ctrl+K, Ctrl+U — uncomment
        [InlineData(K, true)]     // Ctrl+K, Ctrl+K — toggle bookmark
        [InlineData(0x1B, false)] // Ctrl+K, Esc — SSMS cancels its chord
        public void In_a_SQL_editor_SSMS_Ctrl_K_chords_are_handed_back(int second, bool ctrl)
        {
            var router = new ShortcutRouter();
            Assert.Equal(ShortcutAction.Swallow, router.OnLowLevelKeyDown(K, true, false, false, true, true));

            Assert.Equal(ShortcutAction.Replay, router.OnLowLevelKeyDown(second, ctrl, false, false, true, true));
            Assert.False(router.WaitingForChord);
        }

        [Fact]
        public void After_Ctrl_K_Ctrl_Y_still_formats_and_Ctrl_D_keeps_its_meaning()
        {
            var router = new ShortcutRouter();

            router.OnLowLevelKeyDown(K, true, false, false, true, true);
            Assert.Equal(ShortcutAction.Format, router.OnLowLevelKeyDown(Y, true, false, false, true, true));
            router.OnLowLevelKeyDown(K, true, false, false, true, true);
            Assert.Equal(ShortcutAction.Format, router.OnLowLevelKeyDown(D, true, false, false, true, true));
            router.OnLowLevelKeyDown(K, true, false, false, true, true);
            Assert.Equal(ShortcutAction.Refresh, router.OnLowLevelKeyDown(D, true, true, false, true, true));
        }

        [Theory]
        [InlineData(K, false)]
        [InlineData(D, false)]
        [InlineData(D, true)]
        public void Ctrl_Alt_is_AltGr_typing_not_a_shortcut(int vk, bool shift)
        {
            var router = new ShortcutRouter();

            Assert.Equal(ShortcutAction.Pass, router.OnLowLevelKeyDown(vk, true, shift, true, true, true));
            Assert.False(router.WaitingForChord);
        }

        private static string Keys(KeyStroke[] keys) => string.Join(" ", Array.ConvertAll(keys, k => k.ToString()));

        [Fact]
        public void Replaying_Ctrl_K_C_presses_Ctrl_for_K_only()
        {
            // The user let go of Ctrl before C: K needs Ctrl, C must not have it.
            var keys = ShortcutRouter.ReplayChord(C, 0x2E, false, ctrlHeld: false, heldShiftAndAlt: new int[0]);

            Assert.Equal("A2 down 4B down 4B up A2 up 43 down 43 up", Keys(keys));
            Assert.Equal(0x2E, keys[4].Scan);
        }

        [Fact]
        public void Replaying_Ctrl_K_Ctrl_C_uses_the_Ctrl_the_user_holds()
        {
            var keys = ShortcutRouter.ReplayChord(C, 0x2E, false, ctrlHeld: true, heldShiftAndAlt: new int[0]);

            Assert.Equal("4B down 4B up 43 down 43 up", Keys(keys));
        }

        [Fact]
        public void A_held_Shift_or_Alt_is_lifted_for_K_and_pressed_again_for_the_second_key()
        {
            var keys = ShortcutRouter.ReplayChord(0x25, 0x4B, true, ctrlHeld: true, heldShiftAndAlt: new[] { ShortcutRouter.VK_RSHIFT, ShortcutRouter.VK_RMENU });

            Assert.Equal("A1 up A5 up 4B down 4B up A5 down A1 down 25 down 25 up", Keys(keys));
            Assert.True(keys[1].Extended);    // right Alt is an extended key
            Assert.True(keys[6].Extended);    // so is the arrow typed after it
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_replay_leaves_every_modifier_as_the_user_holds_it(bool ctrlHeld)
        {
            foreach (var held in new[] { new int[0], new[] { 0xA0 }, new[] { 0xA4 }, new[] { 0xA1, 0xA5 } })
            {
                var state = new Dictionary<int, bool> { [0xA2] = ctrlHeld };
                foreach (int m in held) state[m] = true;
                var before = new Dictionary<int, bool>(state);

                foreach (var key in ShortcutRouter.ReplayChord(C, 0, false, ctrlHeld, held))
                {
                    if (ShortcutRouter.IsModifier(key.Vk))
                        state[key.Vk] = !key.Up;
                    else if (key.Vk == K && !key.Up)
                    {
                        Assert.True(state[0xA2]);                                         // K is typed with Ctrl...
                        Assert.All(held, m => Assert.False(state[m]));                    // ...and nothing else
                    }
                    else if (key.Vk == C && !key.Up)
                        Assert.Equal(before, state);                                      // C as the user typed it
                }

                Assert.Equal(before, state);                                              // nothing stuck, nothing lost
            }
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
            public readonly List<KeyStroke[]> Injected = new List<KeyStroke[]>();

            public IntPtr SetThreadHook(KeyboardHook.HookProc proc, uint threadId) { var h = (IntPtr)_next++; Thread.Add(h); return h; }
            public IntPtr SetLowLevelHook(KeyboardHook.HookProc proc) { var h = (IntPtr)_next++; LowLevel.Add(h); LowLevelInstalls++; return h; }
            public void Remove(IntPtr handle) { Thread.Remove(handle); LowLevel.Remove(handle); }
            public void Inject(KeyStroke[] keys) => Injected.Add(keys);
        }

        private sealed class FakeActivation : IAppActivation
        {
            public bool Available = true;
            public Action? Changed;
            public bool Detached;

            public bool Attach(Action foregroundChanged)
            {
                if (!Available) return false;
                Changed = foregroundChanged;
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
            h.Activation.Changed!();       // Alt+Tab to Visual Studio
            Assert.False(h.Hook.IsLowLevelHookInstalled);
            Assert.Empty(h.Hooks.LowLevel);

            h.InFront = true;
            h.Activation.Changed!();       // back to SSMS
            Assert.True(h.Hook.IsLowLevelHookInstalled);
            Assert.Single(h.Hooks.LowLevel);

            h.Activation.Changed!();       // a second notification does not stack hooks
            Assert.Single(h.Hooks.LowLevel);
        }

        [Fact]
        public void The_hook_follows_who_is_in_front_not_what_the_notification_implies()
        {
            var h = new Harness(inFront: false);

            h.Activation.Changed!();       // a notification while another program is still in front
            Assert.False(h.Hook.IsLowLevelHookInstalled);
        }

        [Fact]
        public void A_switch_to_SSMS_that_Windows_did_not_report_is_caught_by_the_first_key()
        {
            var h = new Harness(inFront: false);
            h.InFront = true;              // brought to the front through attached input queues: no notification

            Assert.False(h.Hook.HandleThreadKeyDown(0x41, ctrl: false));
            Assert.True(h.Hook.IsLowLevelHookInstalled);

            Assert.True(h.Hook.HandleLowLevelKeyDown(K, true, false));
            Assert.True(h.Hook.HandleLowLevelKeyDown(Y, false, false));
            Assert.Equal(1, h.Formats);
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
        public void In_an_SSMS_SQL_editor_Ctrl_K_C_reaches_SSMS_as_Ctrl_K_C()
        {
            var h = new Harness(inFront: true);

            Assert.True(h.Hook.HandleLowLevelKeyDown(K, true, false));
            Assert.True(h.Hook.HandleLowLevelKeyDown(C, true, false, scan: 0x2E));   // kept, then typed again

            Assert.Equal("4B down 4B up 43 down 43 up", Keys(Assert.Single(h.Hooks.Injected)));
            Assert.Equal(0, h.Formats);
        }

        [Fact]
        public void Ctrl_K_Y_formats_without_typing_anything()
        {
            var h = new Harness(inFront: true);

            h.Hook.HandleLowLevelKeyDown(K, true, false);
            h.Hook.HandleLowLevelKeyDown(Y, false, false);

            Assert.Empty(h.Hooks.Injected);
            Assert.Equal(1, h.Formats);
        }

        [Fact]
        public void Nothing_is_ever_typed_into_another_program()
        {
            var h = new Harness(inFront: false, activationAvailable: false);

            h.Hook.HandleLowLevelKeyDown(K, true, false);
            h.Hook.HandleLowLevelKeyDown(C, true, false);

            Assert.Empty(h.Hooks.Injected);
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

            h.InFront = false;
            h.Activation.Changed!();
            h.InFront = true;
            h.Activation.Changed!();

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

            h.Activation.Changed!();       // a late event after the last query closed
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
            bool inFront = false;
            using var hook = new KeyboardHook(Win32Hooks.Instance, () => inFront, activation);
            hook.Install(() => { }, () => { }, () => { }, () => true);

            Assert.True(hook.IsThreadHookInstalled);
            Assert.False(hook.IsLowLevelHookInstalled);

            inFront = true;
            activation.Changed!();
            Assert.True(hook.IsLowLevelHookInstalled);   // SetWindowsHookEx(WH_KEYBOARD_LL) accepted the module
            inFront = false;
            activation.Changed!();
            Assert.False(hook.IsLowLevelHookInstalled);
        }

        [StaFact]
        public void The_real_foreground_notification_can_be_attached_and_detached()
        {
            var watcher = new ForegroundWatcher();

            Assert.True(watcher.Attach(() => { }));
            watcher.Detach();
            watcher.Detach();                            // twice is harmless
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
