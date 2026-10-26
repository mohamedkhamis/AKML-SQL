using System.Collections.Generic;
using System.Linq;

namespace AkmlSql.Web.Services;

/// <summary>
/// One key the editor page claims. <see cref="Code"/> is a <c>KeyboardEvent.code</c> ("F5", "KeyR"), so
/// it matches whatever the keyboard layout and Shift state make of <c>e.key</c>.
/// </summary>
/// <param name="ChordPrefix">For a chord's second stroke, the first stroke's signature
/// (<c>Ctrl+KeyK</c>); null for a direct key.</param>
/// <param name="Display">The text menus and tooltips show, e.g. "Ctrl+K, Ctrl+F".</param>
public sealed record KeyBinding(string Code, bool Ctrl, bool Shift, bool Alt, string CommandId, string? ChordPrefix, string Display);

/// <summary>
/// Spec 041 (research R37, FR-028, FR-049; contracts/ui.md §2) — the keys the editor page claims, in one
/// table: sent to <c>akml-workspace.js</c> (which calls preventDefault synchronously, before the browser
/// reloads or CodeMirror opens Find) and shown next to the commands in the View menu, the palette and
/// toolbar tooltips. <c>ShortcutCollisionTests</c> checks it against CodeMirror's keys and the keys the
/// browser reserves. Ctrl+P stays with the command palette's own module.
/// </summary>
public static class WorkspaceKeyMap
{
    /// <summary>The first stroke of the Ctrl+K chords.</summary>
    public const string ChordK = "Ctrl+KeyK";

    public static IReadOnlyList<KeyBinding> Bindings { get; } = new[]
    {
        Key("F5", ctrl: false, shift: false, alt: false, "editor:execute"),
        Key("F5", ctrl: true, shift: false, alt: false, "editor:parse"),
        Key("KeyR", ctrl: true, shift: false, alt: false, "view:toggle-results"),
        Key("F8", ctrl: false, shift: false, alt: false, "view:toggle-schema"),
        Key("F6", ctrl: false, shift: false, alt: false, "view:cycle-panes"),
        Key("F6", ctrl: false, shift: true, alt: false, "view:cycle-panes-back"),
        Chord(ChordK, "KeyF", "editor:format"),
        Chord(ChordK, "KeyL", "editor:analyse"),
        Chord(ChordK, "KeyS", "editor:surround"),
        Key("KeyS", ctrl: true, shift: false, alt: false, "editor:save"),
        Key("KeyS", ctrl: true, shift: true, alt: false, "editor:saveas"),
        // Edge reserves Ctrl+Shift+S for Screenshot before the page receives KeyS (T209).
        Chord(ChordK, "KeyA", "editor:saveas"),
        // Ctrl+N and Ctrl+O are browser-reserved (FR-049(b)): SSMS's keys gain Alt.
        Key("KeyN", ctrl: true, shift: false, alt: true, "editor:new"),
        Key("KeyO", ctrl: true, shift: false, alt: true, "editor:open"),
        // Spec 043 (R9): the query tabs. Ctrl+Tab, Ctrl+PageDown/PageUp, Ctrl+W and Ctrl+F4 are the browser's own tab keys;
        // Visual Studio's next and previous document are Ctrl+Alt+PageDown / PageUp, and close pairs with New's Ctrl+Alt.
        Key("PageDown", ctrl: true, shift: false, alt: true, "editor:next-tab"),
        Key("PageUp", ctrl: true, shift: false, alt: true, "editor:previous-tab"),
        Key("KeyW", ctrl: true, shift: false, alt: true, "editor:close-tab"),
    };

    /// <summary>The key text for a command, or null when it has no key.</summary>
    public static string? DisplayFor(string commandId)
    {
        var keys = Bindings.Where(b => b.CommandId == commandId).Select(b => b.Display).ToArray();
        return keys.Length == 0 ? null : string.Join(" or ", keys);
    }

    /// <summary>Spec 041 (FR-081) — a toolbar tooltip: "Execute (F5)", or the bare name for a command with no key.</summary>
    public static string TitleFor(string name, string commandId) =>
        DisplayFor(commandId) is { } keys ? $"{name} ({keys})" : name;

    /// <summary>The bindings whose command is registered, so an unimplemented command never swallows
    /// a key the browser would otherwise act on.</summary>
    public static IReadOnlyList<KeyBinding> For(IEnumerable<string> registeredCommandIds)
    {
        var ids = new HashSet<string>(registeredCommandIds);
        return Bindings.Where(b => ids.Contains(b.CommandId)).ToArray();
    }

    /// <summary>A key's signature as akml-workspace.js builds it: modifiers in Ctrl, Shift, Alt order,
    /// then the code (<c>Ctrl+Shift+KeyS</c>).</summary>
    public static string Signature(KeyBinding b) => Signature(b.Code, b.Ctrl, b.Shift, b.Alt);

    public static string Signature(string code, bool ctrl, bool shift, bool alt) =>
        (ctrl ? "Ctrl+" : string.Empty) + (shift ? "Shift+" : string.Empty) + (alt ? "Alt+" : string.Empty) + code;

    /// <summary>"Ctrl+Shift+S" for (KeyS, ctrl, shift).</summary>
    public static string Describe(string code, bool ctrl, bool shift, bool alt) =>
        (ctrl ? "Ctrl+" : string.Empty) + (shift ? "Shift+" : string.Empty) + (alt ? "Alt+" : string.Empty) + KeyName(code);

    /// <summary>"Ctrl+K" for the signature <c>Ctrl+KeyK</c>.</summary>
    public static string DescribeSignature(string signature)
    {
        var parts = signature.Split('+');
        var code = parts[parts.Length - 1];
        return Describe(code, parts.Contains("Ctrl"), parts.Contains("Shift"), parts.Contains("Alt"));
    }

    private static string KeyName(string code) => code switch
    {
        _ when code.StartsWith("Key", System.StringComparison.Ordinal) && code.Length == 4 => code.Substring(3),
        _ when code.StartsWith("Digit", System.StringComparison.Ordinal) && code.Length == 6 => code.Substring(5),
        "Slash" => "/",
        "Enter" => "Enter",
        _ => code,
    };

    private static KeyBinding Key(string code, bool ctrl, bool shift, bool alt, string commandId) =>
        new(code, ctrl, shift, alt, commandId, null, Describe(code, ctrl, shift, alt));

    private static KeyBinding Chord(string prefix, string code, string commandId) =>
        new(code, true, false, false, commandId, prefix, DescribeSignature(prefix) + ", " + Describe(code, true, false, false));
}
