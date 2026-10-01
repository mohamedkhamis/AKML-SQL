#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ui;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T172/T182, X-02, FR-061, contracts/ui.md §5, research R27) — every AKML window and
    /// form is titled "AKML SQL – ‹Name›" through <see cref="WindowTitles.For"/>, and shows the AKML
    /// icon. A text scan of <c>src/AkmlSql.Shell.Shared</c>:
    /// <list type="bullet">
    /// <item>finds every caption assignment on a window or form — <c>Title =</c> on a WPF
    /// <c>Window</c> (including <c>ThemeAwareWindow</c> and VS <c>DialogWindow</c>) and <c>Text =</c>
    /// on a WinForms <c>Form</c> — whether in the window's own class, in a <c>new Window { … }</c> /
    /// <c>new Form { … }</c> initializer, or on a variable declared as one, and requires the value
    /// to be <c>WindowTitles.For(…)</c> (directly, or through a constant in the same file);</item>
    /// <item>requires every other <c>Title</c> assignment to be on the explicit allow-list below
    /// (file dialogs, message-box captions);</item>
    /// <item><c>Text</c> on anything that isn't a form (labels, buttons, text boxes) is control
    /// text, not a caption, and is not checked; tool-window captions (<c>ToolWindowPane.Caption</c>)
    /// stay unprefixed, because VS shows them in tabs.</item>
    /// </list>
    /// Comments, strings and preprocessor lines are blanked before matching, so text inside them
    /// never counts. In the "AkmlSql ThemeRegistry" collection because one test constructs a
    /// <see cref="ThemeAwareWindow"/>.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public sealed class WindowTitleUsageTests
    {
        // ── Allow-list ──────────────────────────────────────────────────────────────────────────

        /// <summary>Types with a <c>Title</c> that are not AKML windows, and why they keep their own text.</summary>
        private static readonly IReadOnlyDictionary<string, string> NonWindowTitleTypes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SaveFileDialog"] = "file dialog — Windows draws it; its title names the action (\"Export formatting style\")",
            ["OpenFileDialog"] = "file dialog — Windows draws it; its title names the action (\"Import SQL Prompt style\")",
            ["FolderBrowserDialog"] = "folder dialog — Windows draws it",
        };

        /// <summary>
        /// Single <c>Title</c> members that are not window captions, keyed "file|Class.Member".
        /// Every entry must still exist (a stale entry fails the scan).
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> NonWindowTitleSites = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Formatting/FormatFailureNotifier.cs|FormatFailureNotifier.Title"] =
                "message-box caption (R27: message boxes keep Constants.ProductName)",
        };

        /// <summary>contracts/ui.md §5 — tool-window captions, which stay as they are.</summary>
        private static readonly string[] ToolWindowCaptions = { "SQL History", "AI Chat", "Document Outline", "Find References" };

        /// <summary>
        /// contracts/ui.md §5 — every window name. "‹…›" marks a value filled in at run time; the
        /// text before it must start a <c>WindowTitles.For(…)</c> argument.
        /// </summary>
        private static readonly string[] ContractNames =
        {
            "Options",
            "Format styles",
            "New style", "Rename style",
            "Import summary: ‹style›",
            "Code analysis rules",
            "Snippet manager",
            "Surround with",
            "SQL History comparison",
            "Object search",
            "Command palette",
            "SQL authentication", "Saved SQL credentials",
            "Execution warning",
            "Update available", "Downloading update", "Install update",
            "Log viewer",
            "Code analysis results",
            "Find invalid objects",
            "About",
            "Refactoring preview",
            "Edit cell",
            "Smart rename",
            "Split table — ‹table›",
            "Bulk format",
            "Formatting…",
            "Restore queries",
            "Text to SQL",
            "Generated SQL preview",
            "SQL explanation",
            "Fix preview",
            "Multi-database execution — ‹server›",
            "Multi-database results",
            "Column statistics: ‹column›",
            "Row details — row ‹n›",
        };

        // ── Tests ───────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void Every_window_and_form_caption_uses_WindowTitles_For()
        {
            var scan = ShellScan.Load();
            var captions = scan.Sites.Where(s => s.IsWindowCaption(scan)).ToList();

            // The scanner must see every way a window is made, or it proves nothing.
            Assert.True(captions.Count >= 35, $"Only {captions.Count} window captions found — the scan is broken.");
            Assert.Contains(captions, s => s.File == "Dialogs/SettingsWindow.cs" && s.Kind == SiteKind.Initializer);          // new Window { Title = … }
            Assert.Contains(captions, s => s.File == "Refactoring/SplitTableCommand.cs" && s.Kind == SiteKind.Initializer);   // new Form { Text = … }
            Assert.Contains(captions, s => s.File == "Dialogs/AboutDialog.cs" && s.Property == "Text");                       // Form subclass
            Assert.Contains(captions, s => s.File == "Safety/SafetyWarningDialog.cs" && s.Property == "Title");               // Window subclass
            Assert.Contains(captions, s => s.File == "Snippets/SurroundWithCommand.cs" && s.Owner == "SurroundPickerDialog"); // nested DialogWindow
            Assert.True(captions.Count(s => s.File == "Productivity/Grid/TransposeResultsView.cs") >= 2, "TransposeResultsView renames itself per row"); // set outside the constructor too

            var wrong = captions.Where(s => !scan.UsesWindowTitles(s)).Select(s => s.Describe()).ToList();
            Assert.True(wrong.Count == 0,
                "Window/form captions must be WindowTitles.For(\"‹Name›\") (contracts/ui.md §5):\n" + string.Join("\n", wrong));
        }

        [Fact]
        public void Every_other_Title_is_on_the_allow_list()
        {
            var scan = ShellScan.Load();
            var others = scan.Sites.Where(s => s.Property == "Title" && !s.IsWindowCaption(scan)).ToList();

            var unlisted = others
                .Where(s => !(s.Target != null && NonWindowTitleTypes.ContainsKey(s.Target)) && !NonWindowTitleSites.ContainsKey(s.Key))
                .Select(s => s.Describe() + $"   (target: {s.Target ?? "unresolved"})")
                .ToList();
            Assert.True(unlisted.Count == 0,
                "A Title that isn't on a window or form the scan recognises. If it is a window, title it with " +
                "WindowTitles.For; if not, add it to the allow-list with the reason:\n" + string.Join("\n", unlisted));

            var stale = NonWindowTitleSites.Keys.Where(k => others.All(s => s.Key != k)).ToList();
            Assert.True(stale.Count == 0, "Allow-list entries that no longer match anything: " + string.Join(", ", stale));
            Assert.Contains(others, s => s.Target == "SaveFileDialog"); // file dialogs are seen, and allowed
        }

        [Fact]
        public void Tool_window_captions_stay_as_they_are()
        {
            var scan = ShellScan.Load();
            var captions = scan.Sites
                .Where(s => s.Property == "Caption" && s.Kind == SiteKind.Statement && s.Owner != null && scan.IsToolWindow(s.Owner))
                .Select(s => scan.LiteralValue(s))
                .ToList();

            Assert.Equal(ToolWindowCaptions.OrderBy(c => c, StringComparer.Ordinal), captions.OrderBy(c => c, StringComparer.Ordinal));
            Assert.DoesNotContain(captions, c => c != null && c.StartsWith(Core.Constants.ProductName, StringComparison.Ordinal));
        }

        [Fact]
        public void Every_contract_window_name_is_used()
        {
            var scan = ShellScan.Load();
            var arguments = scan.WindowTitlesArguments();

            var missing = ContractNames
                .Where(name =>
                {
                    var marker = name.IndexOf('‹');
                    return marker < 0
                        ? !arguments.Contains(name)
                        : !arguments.Any(a => a.StartsWith(name.Substring(0, marker), StringComparison.Ordinal) && a.Length > marker);
                })
                .ToList();
            Assert.True(missing.Count == 0, "contracts/ui.md §5 names with no WindowTitles.For(…) call: " + string.Join(", ", missing));
        }

        [Fact]
        public void Every_window_and_form_class_shows_the_AKML_icon()
        {
            var scan = ShellScan.Load();
            // ThemeAwareWindow applies the icon for everything derived from it; a VS DialogWindow
            // has a dialog frame (HasDialogFrame defaults to true), which shows no icon.
            var needIcon = scan.Classes
                .Where(c => (scan.IsWpfWindow(c.Name) || scan.IsForm(c.Name))
                            && !(c.Base != null && scan.DerivesFrom(c.Base, "ThemeAwareWindow"))
                            && !scan.DerivesFrom(c.Name, "DialogWindow"))
                .ToList();

            Assert.Contains(needIcon, c => c.Name == "ThemeAwareWindow");
            Assert.Contains(needIcon, c => c.Name == "AboutDialog");
            var missing = needIcon.Where(c => !c.Body.Contains("WindowIcon.")).Select(c => $"{c.File}: {c.Name}").ToList();
            Assert.True(missing.Count == 0, "Windows/forms that don't call WindowIcon.Apply:\n" + string.Join("\n", missing));
        }

        [Fact]
        public void Icon_resource_is_embedded_under_its_fixed_name()
        {
            using var stream = typeof(WindowIcon).Assembly.GetManifestResourceStream(WindowIcon.ResourceName);
            Assert.NotNull(stream);
            Assert.True(stream!.Length > 0);
        }

        [StaFact]
        public void ThemeAwareWindow_gets_the_AKML_icon()
        {
            var window = new ProbeWindow();
            try
            {
                Assert.NotNull(window.Icon);
                Assert.Same(WindowIcon.Source, window.Icon);
            }
            finally { window.Close(); }
        }

        [StaFact]
        public void Apply_keeps_an_icon_a_window_already_has()
        {
            var window = new System.Windows.Window();
            var own = System.Windows.Media.Imaging.BitmapSource.Create(
                1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[4], 4);
            window.Icon = own;
            try
            {
                WindowIcon.Apply(window);
                Assert.Same(own, window.Icon);
            }
            finally { window.Close(); }
        }

        [StaFact]
        public void Form_gets_the_AKML_icon()
        {
            using var form = new System.Windows.Forms.Form();
            WindowIcon.Apply(form);

            Assert.NotNull(WindowIcon.FormIcon);
            Assert.Same(WindowIcon.FormIcon, form.Icon);
        }

        private sealed class ProbeWindow : ThemeAwareWindow
        {
        }

        // ── The scan ────────────────────────────────────────────────────────────────────────────

        internal enum SiteKind
        {
            /// <summary><c>Title = …;</c> in a class body: sets the enclosing class's member.</summary>
            Statement,
            /// <summary><c>new T { Title = … }</c>.</summary>
            Initializer,
            /// <summary><c>x.Title = …;</c>.</summary>
            Qualified,
            /// <summary><c>string Title = …</c> — a field, constant or local named Title.</summary>
            Declaration,
        }

        internal sealed class Site
        {
            public string File = string.Empty;
            public int Line;
            public string Property = string.Empty;
            public SiteKind Kind;
            /// <summary>The class the site is in.</summary>
            public string? Owner;
            /// <summary>The type whose property is set (last name segment), or null when unresolved.</summary>
            public string? Target;
            /// <summary>Index of the value's first character.</summary>
            public int ValueIndex;
            public string Source = string.Empty;
            public string Masked = string.Empty;

            public string Key => $"{File}|{Owner}.{Property}";

            public bool IsWindowCaption(ShellScan scan) =>
                Kind != SiteKind.Declaration && Target != null &&
                ((Property == "Title" && scan.IsWpfWindow(Target)) || (Property == "Text" && scan.IsForm(Target)));

            public string Describe()
            {
                var end = Source.IndexOf('\n', ValueIndex);
                var value = Source.Substring(ValueIndex, (end < 0 ? Source.Length : end) - ValueIndex).Trim();
                return $"  {File}:{Line}  {Property} = {value}";
            }
        }

        internal sealed class ClassInfo
        {
            public string File = string.Empty;
            public string Name = string.Empty;
            public string? Base;
            public int BodyStart;
            public int BodyEnd;
            public string Body = string.Empty;
        }

        internal sealed class ShellScan
        {
            private static readonly Regex ClassDecl = new Regex(@"\b(?:class|struct)\s+(\w+)", RegexOptions.Compiled);
            private static readonly Regex BaseList = new Regex(@"^\s*(?:<[^{]*?>)?\s*(?:\([^{]*?\))?\s*:\s*([\w.]+)", RegexOptions.Compiled);
            private static readonly Regex Assignment = new Regex(
                @"(?<![\w.$@])(?<chain>(?:\w+\s*[!?]?\s*\.\s*)*)(?<prop>Title|Text|Caption)\s*=(?![=>])", RegexOptions.Compiled);
            private static readonly Regex UsesFor = new Regex(
                @"^(?:global::)?(?:AkmlSql\.Core\.Config\.)?WindowTitles\s*\.\s*For\s*\(", RegexOptions.Compiled);
            private static readonly Regex ForArgument = new Regex(
                @"WindowTitles\s*\.\s*For\s*\(\s*(?<prefix>\$?@?)""(?<text>(?:[^""\\\r\n]|\\.)*)""", RegexOptions.Compiled);
            private static readonly HashSet<string> NotTypes = new HashSet<string>(StringComparer.Ordinal)
            {
                "return", "new", "out", "ref", "in", "var", "await", "using", "case", "is", "as", "else", "throw", "yield", "await", "do",
            };

            private static ShellScan? _cached;

            public readonly List<Site> Sites = new List<Site>();
            public readonly List<ClassInfo> Classes = new List<ClassInfo>();
            private readonly Dictionary<string, string> _bases = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly Dictionary<string, (string Source, string Masked)> _files = new Dictionary<string, (string, string)>(StringComparer.Ordinal);

            public static ShellScan Load() => _cached ??= new ShellScan(ShellDirectory());

            private ShellScan(string root)
            {
                foreach (var path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    var rel = path.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                    if (rel.StartsWith("bin/", StringComparison.Ordinal) || rel.StartsWith("obj/", StringComparison.Ordinal)) continue;
                    var source = File.ReadAllText(path);
                    _files[rel] = (source, CSharpMask.Mask(source));
                }

                foreach (var file in _files)
                    FindClasses(file.Key, file.Value.Source, file.Value.Masked);
                foreach (var file in _files)
                    FindSites(file.Key, file.Value.Source, file.Value.Masked);
            }

            private static string ShellDirectory()
            {
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
                    dir = dir.Parent;
                Assert.NotNull(dir);
                return Path.Combine(dir!.FullName, "src", "AkmlSql.Shell.Shared");
            }

            // ── Types ──

            public bool DerivesFrom(string type, string ancestor)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var t = type; t != null && seen.Add(t); t = _bases.TryGetValue(t, out var b) ? b : null)
                    if (t == ancestor) return true;
                return false;
            }

            public bool IsWpfWindow(string type) => DerivesFrom(type, "Window") || DerivesFrom(type, "DialogWindow");
            public bool IsForm(string type) => DerivesFrom(type, "Form");
            public bool IsToolWindow(string type) => DerivesFrom(type, "ToolWindowPane");

            private void FindClasses(string file, string source, string masked)
            {
                foreach (Match m in ClassDecl.Matches(masked))
                {
                    var open = masked.IndexOfAny(new[] { '{', ';' }, m.Index + m.Length);
                    if (open < 0 || masked[open] != '{') continue;
                    var header = masked.Substring(m.Index + m.Length, open - m.Index - m.Length);
                    var baseMatch = BaseList.Match(header);
                    var name = m.Groups[1].Value;
                    string? baseName = baseMatch.Success ? LastSegment(baseMatch.Groups[1].Value) : null;
                    if (baseName != null && !_bases.ContainsKey(name)) _bases[name] = baseName;

                    var close = MatchingBrace(masked, open);
                    Classes.Add(new ClassInfo
                    {
                        File = file, Name = name, Base = baseName, BodyStart = open, BodyEnd = close,
                        Body = masked.Substring(open, close - open),
                    });
                }
            }

            private ClassInfo? EnclosingClass(string file, int index) =>
                Classes.Where(c => c.File == file && c.BodyStart < index && index < c.BodyEnd)
                       .OrderByDescending(c => c.BodyStart)
                       .FirstOrDefault();

            // ── Sites ──

            private void FindSites(string file, string source, string masked)
            {
                foreach (Match m in Assignment.Matches(masked))
                {
                    var chain = Regex.Replace(m.Groups["chain"].Value, @"[\s!?]", string.Empty);
                    if (chain.StartsWith("this.", StringComparison.Ordinal)) chain = chain.Substring(5);
                    var owner = EnclosingClass(file, m.Index);

                    var site = new Site
                    {
                        File = file,
                        Line = LineOf(source, m.Index),
                        Property = m.Groups["prop"].Value,
                        Owner = owner?.Name,
                        ValueIndex = SkipSpace(masked, m.Index + m.Length),
                        Source = source,
                        Masked = masked,
                    };

                    if (chain.Length > 0)
                    {
                        site.Kind = SiteKind.Qualified;
                        site.Target = DeclaredType(masked, chain.TrimEnd('.').Split('.').Last());
                    }
                    else if (IsDeclaration(masked, m.Index))
                    {
                        site.Kind = SiteKind.Declaration;
                        site.Target = owner?.Name;
                    }
                    else
                    {
                        var created = InitializerType(masked, m.Index, out var isInitializer);
                        site.Kind = isInitializer ? SiteKind.Initializer : SiteKind.Statement;
                        site.Target = isInitializer ? created : owner?.Name;
                    }
                    Sites.Add(site);
                }
            }

            /// <summary>
            /// True when the value is <c>WindowTitles.For(…)</c>, or a constant/field in the same
            /// file whose value is.
            /// </summary>
            public bool UsesWindowTitles(Site site)
            {
                var value = site.Source.Substring(site.ValueIndex);
                if (UsesFor.IsMatch(value)) return true;

                var identifier = Regex.Match(site.Masked.Substring(site.ValueIndex), @"^(?:\w+\.)*(\w+)\s*[;,}\r\n)]");
                if (!identifier.Success) return false;
                var declaration = new Regex(@"\bstring\s+" + Regex.Escape(identifier.Groups[1].Value) + @"\s*(?:=>|=)(?!=)");
                var found = declaration.Match(site.Masked);
                return found.Success && UsesFor.IsMatch(site.Source.Substring(SkipSpace(site.Masked, found.Index + found.Length)));
            }

            /// <summary>The text of a string-literal value, or null when the value isn't one.</summary>
            public string? LiteralValue(Site site)
            {
                var m = Regex.Match(site.Source.Substring(site.ValueIndex), @"^""((?:[^""\\\r\n]|\\.)*)""");
                return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
            }

            /// <summary>
            /// The literal text of every <c>WindowTitles.For("…")</c> argument (interpolation holes
            /// kept as written). A file that passes a name through a parameter —
            /// <c>WindowTitles.For(title)</c>, as StyleNameDialog does — contributes all its string
            /// literals, since the names are the helper's callers' arguments.
            /// </summary>
            public HashSet<string> WindowTitlesArguments()
            {
                var result = new HashSet<string>(StringComparer.Ordinal);
                foreach (var file in _files.Values)
                {
                    foreach (Match m in ForArgument.Matches(file.Source))
                    {
                        if (file.Masked[m.Index] != 'W') continue; // in a comment or a string, not code
                        result.Add(Unescape(m.Groups["text"].Value, m.Groups["prefix"].Value.Contains("@")));
                    }

                    if (!Regex.IsMatch(file.Masked, @"WindowTitles\s*\.\s*For\s*\(\s*\w+\s*\)")) continue;
                    // In the masked text a literal is a quote, blanks, a quote — so each match is
                    // exactly one literal; its text is read from the source at the same place.
                    foreach (Match m in MaskedLiteral.Matches(file.Masked))
                        result.Add(Unescape(file.Source.Substring(m.Index + 1, m.Length - 2), verbatim: false));
                }
                return result;
            }

            private static readonly Regex MaskedLiteral = new Regex(@"""[^""\r\n]*""", RegexOptions.Compiled);

            private static string Unescape(string text, bool verbatim) =>
                verbatim
                    ? text
                    : Regex.Replace(text, @"\\u([0-9A-Fa-f]{4})", u => ((char)Convert.ToInt32(u.Groups[1].Value, 16)).ToString())
                           .Replace("\\\"", "\"").Replace("\\\\", "\\");

            private static bool IsDeclaration(string masked, int index)
            {
                var i = index - 1;
                while (i >= 0 && char.IsWhiteSpace(masked[i])) i--;
                if (i < 0) return false;
                var c = masked[i];
                if (c == '?' || c == ']') return true;
                if (c == '>') return i == 0 || masked[i - 1] != '='; // List<string> Title — but not "=> Title"
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
                var end = i + 1;
                while (i >= 0 && (char.IsLetterOrDigit(masked[i]) || masked[i] == '_')) i--;
                var word = masked.Substring(i + 1, end - i - 1);
                return !NotTypes.Contains(word);
            }

            /// <summary>
            /// The type created by the object initializer the assignment is a member of, when the
            /// innermost enclosing brace is <c>new T(…) {</c>.
            /// </summary>
            private static string? InitializerType(string masked, int index, out bool isInitializer)
            {
                isInitializer = false;
                var depth = 0;
                var i = index - 1;
                for (; i >= 0; i--)
                {
                    if (masked[i] == '}') depth++;
                    else if (masked[i] == '{')
                    {
                        if (depth == 0) break;
                        depth--;
                    }
                }
                if (i < 0) return null;

                i = SkipSpaceBack(masked, i - 1);
                if (i >= 1 && masked[i] == '>' && masked[i - 1] == '=') return null; // lambda body
                if (i >= 0 && masked[i] == ')') i = SkipSpaceBack(masked, SkipGroupBack(masked, i, '(', ')') - 1);
                if (i >= 0 && masked[i] == '>') i = SkipSpaceBack(masked, SkipGroupBack(masked, i, '<', '>') - 1);
                var end = i + 1;
                while (i >= 0 && (char.IsLetterOrDigit(masked[i]) || masked[i] == '_' || masked[i] == '.')) i--;
                var type = masked.Substring(i + 1, end - i - 1);
                i = SkipSpaceBack(masked, i);
                if (type == "new")
                {
                    isInitializer = true; // target-typed new() — unresolved
                    return null;
                }
                if (i >= 2 && masked.Substring(i - 2, 3) == "new" && (i - 3 < 0 || !char.IsLetterOrDigit(masked[i - 3])))
                {
                    isInitializer = true;
                    return type.Length == 0 ? null : LastSegment(type);
                }
                return null;
            }

            private static string? DeclaredType(string masked, string variable)
            {
                var v = Regex.Escape(variable);
                var created = Regex.Match(masked, @"\b" + v + @"\s*=\s*new\s+([\w.]+)");
                if (created.Success) return LastSegment(created.Groups[1].Value);
                foreach (Match m in Regex.Matches(masked, @"([\w.]+)(?:<[^<>;{}]*>)?\??(?:\[\])?\s+" + v + @"\s*[=;,)]"))
                {
                    var type = LastSegment(m.Groups[1].Value);
                    if (!NotTypes.Contains(type)) return type;
                }
                return null;
            }

            private static string LastSegment(string name)
            {
                var dot = name.LastIndexOf('.');
                return dot < 0 ? name : name.Substring(dot + 1);
            }

            private static int MatchingBrace(string masked, int open)
            {
                var depth = 0;
                for (var i = open; i < masked.Length; i++)
                {
                    if (masked[i] == '{') depth++;
                    else if (masked[i] == '}' && --depth == 0) return i;
                }
                return masked.Length;
            }

            private static int SkipGroupBack(string masked, int close, char openChar, char closeChar)
            {
                var depth = 0;
                for (var i = close; i >= 0; i--)
                {
                    if (masked[i] == closeChar) depth++;
                    else if (masked[i] == openChar && --depth == 0) return i;
                }
                return 0;
            }

            private static int SkipSpace(string text, int i)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                return i;
            }

            private static int SkipSpaceBack(string text, int i)
            {
                while (i >= 0 && char.IsWhiteSpace(text[i])) i--;
                return i;
            }

            private static int LineOf(string text, int index)
            {
                var line = 1;
                for (var i = 0; i < index; i++)
                    if (text[i] == '\n') line++;
                return line;
            }
        }

        /// <summary>
        /// Blanks the contents of comments, preprocessor lines, and string and character literals
        /// (keeping the quotes, line breaks and every index), so that code-only patterns can be
        /// matched with regular expressions.
        /// </summary>
        internal static class CSharpMask
        {
            public static string Mask(string s)
            {
                var m = s.ToCharArray();
                var i = 0;
                var lineStart = true;
                while (i < s.Length)
                {
                    var c = s[i];
                    if (c == '\n') { lineStart = true; i++; continue; }
                    if (char.IsWhiteSpace(c)) { i++; continue; }
                    if (lineStart && c == '#') { var e = EndOfLine(s, i); Blank(m, i, e); i = e; continue; }
                    lineStart = false;
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') { var e = EndOfLine(s, i); Blank(m, i, e); i = e; continue; }
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                    {
                        var e = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                        e = e < 0 ? s.Length : e + 2;
                        Blank(m, i, e);
                        i = e;
                        continue;
                    }
                    if (TryLiteral(s, i, out var contentStart, out var contentEnd, out var end))
                    {
                        Blank(m, contentStart, contentEnd);
                        i = end;
                        continue;
                    }
                    i++;
                }
                return new string(m);
            }

            private static bool TryLiteral(string s, int i, out int contentStart, out int contentEnd, out int end)
            {
                contentStart = contentEnd = end = i;
                var n = s.Length;
                if (s[i] == '\'')
                {
                    var j = i + 1;
                    while (j < n && s[j] != '\'' && s[j] != '\n') { if (s[j] == '\\') j++; j++; }
                    contentStart = i + 1;
                    contentEnd = Math.Min(j, n);
                    end = Math.Min(j + 1, n);
                    return true;
                }

                var p = i;
                var interpolated = false;
                var verbatim = false;
                while (p < n && (s[p] == '$' || s[p] == '@'))
                {
                    if (s[p] == '$') interpolated = true; else verbatim = true;
                    p++;
                }
                if (p >= n || s[p] != '"') return false;

                var quotes = 0;
                while (p + quotes < n && s[p + quotes] == '"') quotes++;
                if (quotes >= 3)
                {
                    // Raw string literal: ends at the next run of the same number of quotes.
                    var close = s.IndexOf(new string('"', quotes), p + quotes, StringComparison.Ordinal);
                    contentStart = p + quotes;
                    contentEnd = close < 0 ? n : close;
                    end = close < 0 ? n : close + quotes;
                    return true;
                }

                var k = p + 1;
                contentStart = k;
                while (k < n)
                {
                    var ch = s[k];
                    if (verbatim)
                    {
                        if (ch == '"')
                        {
                            if (k + 1 < n && s[k + 1] == '"') { k += 2; continue; }
                            break;
                        }
                    }
                    else
                    {
                        if (ch == '\\') { k += 2; continue; }
                        if (ch == '"' || ch == '\n') break;
                    }
                    if (interpolated && ch == '{')
                    {
                        if (k + 1 < n && s[k + 1] == '{') { k += 2; continue; }
                        k = SkipHole(s, k + 1);
                        continue;
                    }
                    k++;
                }
                contentEnd = Math.Min(k, n);
                end = Math.Min(k + 1, n);
                return true;
            }

            /// <summary>Skips an interpolation hole's code (nested strings included); returns the index after its "}".</summary>
            private static int SkipHole(string s, int k)
            {
                var depth = 0;
                while (k < s.Length)
                {
                    var ch = s[k];
                    if (ch == '}')
                    {
                        if (depth == 0) return k + 1;
                        depth--;
                        k++;
                        continue;
                    }
                    if (ch == '{') { depth++; k++; continue; }
                    if (TryLiteral(s, k, out _, out _, out var end)) { k = end; continue; }
                    k++;
                }
                return s.Length;
            }

            private static int EndOfLine(string s, int i)
            {
                var e = s.IndexOf('\n', i);
                return e < 0 ? s.Length : e;
            }

            private static void Blank(char[] m, int from, int to)
            {
                for (var k = from; k < to && k < m.Length; k++)
                    if (m[k] != '\r' && m[k] != '\n') m[k] = ' ';
            }
        }
    }
}
