#nullable enable
using System;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Editor.Completion
{
    /// <summary>What an automatic completion trigger should do.</summary>
    internal readonly struct TriggerDecision : IEquatable<TriggerDecision>
    {
        private TriggerDecision(TriggerKind kind, int delayMs)
        {
            Kind = kind;
            DelayMs = delayMs;
        }

        public TriggerKind Kind { get; }

        /// <summary>Milliseconds to wait after the last keystroke; only meaningful for <see cref="TriggerKind.Delayed"/>.</summary>
        public int DelayMs { get; }

        public static TriggerDecision None => new TriggerDecision(TriggerKind.None, 0);
        public static TriggerDecision Immediate => new TriggerDecision(TriggerKind.Immediate, 0);
        public static TriggerDecision Delayed(int delayMs) => new TriggerDecision(TriggerKind.Delayed, delayMs);

        public bool Equals(TriggerDecision other) => Kind == other.Kind && DelayMs == other.DelayMs;
        public override bool Equals(object? obj) => obj is TriggerDecision other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ DelayMs;
        public override string ToString() => Kind == TriggerKind.Delayed ? $"Delayed({DelayMs})" : Kind.ToString();
    }

    internal enum TriggerKind
    {
        None,
        Immediate,
        Delayed,
    }

    /// <summary>
    /// Spec 040 (OPT-01) — the one decision for whether typing opens the suggestions box, and when:
    /// <list type="bullet">
    /// <item>Ctrl+Space is always immediate (the IntelliSense master switch is checked by the caller).</item>
    /// <item>Typing needs IntelliSense on and "Auto-trigger completions while typing" on.</item>
    /// <item>A dot needs "Trigger after dot".</item>
    /// <item>Only identifier characters trigger, plus the caller's keyword contexts (a space after
    ///   FROM, JOIN, GROUP BY …), flagged with <c>contextTrigger</c>.</item>
    /// <item>"Trigger delay (ms)" 0 means immediate; otherwise the box opens that long after the
    ///   last keystroke.</item>
    /// </list>
    /// </summary>
    internal static class CompletionTriggerPolicy
    {
        public static TriggerDecision Decide(char typed, bool ctrlSpace, IntelliSenseSettings s, bool contextTrigger = false)
        {
            if (ctrlSpace) return TriggerDecision.Immediate;
            if (s == null || !s.Enabled || !s.AutoTrigger) return TriggerDecision.None;

            if (!contextTrigger)
            {
                if (typed == '.')
                {
                    if (!s.AfterDot) return TriggerDecision.None;
                }
                else if (!IsIdentifierStart(typed))
                {
                    return TriggerDecision.None;
                }
            }

            return s.TriggerDelayMs <= 0 ? TriggerDecision.Immediate : TriggerDecision.Delayed(s.TriggerDelayMs);
        }

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_' || c == '@' || c == '#';
    }
}
