#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace AkmlSql.Core.Text
{
    /// <summary>Spec 046: the kind of one inline run inside a chat answer's prose.</summary>
    public enum ChatInlineKind
    {
        /// <summary>Plain text; may contain <c>\n</c> line breaks.</summary>
        Text,
        /// <summary><c>**bold**</c>.</summary>
        Bold,
        /// <summary><c>`inline code`</c>.</summary>
        Code
    }

    /// <summary>Spec 046: one inline run of a chat answer — its kind and its text without markers.</summary>
    public sealed class ChatInline
    {
        public ChatInline(ChatInlineKind kind, string text)
        {
            Kind = kind;
            Text = text ?? string.Empty;
        }

        public ChatInlineKind Kind { get; }
        public string Text { get; }
    }

    /// <summary>Spec 046 (data-model.md › ChatBlock): one block of a parsed chat answer.</summary>
    public abstract class ChatBlock
    {
        internal ChatBlock() { }
    }

    /// <summary>A paragraph; consecutive lines keep their line breaks.</summary>
    public sealed class ChatParagraph : ChatBlock
    {
        public ChatParagraph(IReadOnlyList<ChatInline> inlines) => Inlines = inlines;
        public IReadOnlyList<ChatInline> Inlines { get; }
    }

    /// <summary>A heading, levels 1–3 (<c>#</c> to <c>###</c>).</summary>
    public sealed class ChatHeading : ChatBlock
    {
        public ChatHeading(int level, IReadOnlyList<ChatInline> inlines)
        {
            Level = level;
            Inlines = inlines;
        }

        public int Level { get; }
        public IReadOnlyList<ChatInline> Inlines { get; }
    }

    /// <summary>A one-level list: bullets (<c>-</c>, <c>*</c>) or numbers (<c>1.</c>).</summary>
    public sealed class ChatListBlock : ChatBlock
    {
        public ChatListBlock(bool ordered, int start, IReadOnlyList<IReadOnlyList<ChatInline>> items)
        {
            Ordered = ordered;
            Start = start;
            Items = items;
        }

        public bool Ordered { get; }

        /// <summary>The first item's number for an ordered list (a list split by a nested bullet list
        /// resumes at its own number); 1 for a bullet list.</summary>
        public int Start { get; }

        public IReadOnlyList<IReadOnlyList<ChatInline>> Items { get; }
    }

    /// <summary>A fenced code block — the unit that gets its own Copy and Insert actions.</summary>
    public sealed class ChatCodeBlock : ChatBlock
    {
        private static readonly HashSet<string> SqlLanguages = new HashSet<string>(StringComparer.Ordinal)
        {
            "", "sql", "tsql", "t-sql", "mssql", "pgsql", "postgresql", "plpgsql"
        };

        public ChatCodeBlock(string language, string code, int index, bool unclosed)
        {
            Language = language ?? string.Empty;
            Code = code ?? string.Empty;
            Index = index;
            Unclosed = unclosed;
        }

        /// <summary>The fence's language, lower-cased (empty when none was given).</summary>
        public string Language { get; }

        /// <summary>The code without the fences; leading and trailing blank lines removed.</summary>
        public string Code { get; }

        /// <summary>1-based position among the answer's code blocks.</summary>
        public int Index { get; }

        /// <summary>True when the answer ended before the closing fence (nothing is dropped).</summary>
        public bool Unclosed { get; }

        /// <summary>True when the block can be inserted into a query editor: no language, or a SQL one.</summary>
        public bool IsSql => SqlLanguages.Contains(Language);
    }

    /// <summary>
    /// Spec 046 (FR-020, contracts/chat-rendering.md Part 1, research R5): the small markdown subset
    /// AI chat answers use, parsed once for both chat renderers (the SSMS WPF panel and the web
    /// component) so the two editions render an answer the same way. Fenced code (an unclosed fence
    /// runs to the end), headings <c>#</c>–<c>###</c>, one-level bullet and numbered lists, paragraphs
    /// that keep their line breaks, and the inlines <c>**bold**</c> and <c>`code`</c>; a horizontal
    /// rule (<c>---</c>) separates blocks and renders nothing. Everything else is literal text — the
    /// parser never drops a character of the answer's content.
    /// </summary>
    public static class ChatMarkdown
    {
        private const string Fence = "```";

        public static IReadOnlyList<ChatBlock> Parse(string? text)
        {
            var blocks = new List<ChatBlock>();
            if (string.IsNullOrWhiteSpace(text))
                return blocks;

            var lines = text!.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var paragraph = new List<string>();
            ListBuilder? list = null;
            var codeIndex = 0;

            void FlushParagraph()
            {
                if (paragraph.Count == 0) return;
                blocks.Add(new ChatParagraph(ParseInlines(string.Join("\n", paragraph))));
                paragraph.Clear();
            }

            void FlushList()
            {
                if (list == null) return;
                blocks.Add(list.Build());
                list = null;
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                if (TryOpenFence(line, out var indent, out var run, out var language))
                {
                    FlushParagraph();
                    FlushList();
                    var code = new List<string>();
                    var closed = false;
                    for (i++; i < lines.Length; i++)
                    {
                        if (IsClosingFence(lines[i], run))
                        {
                            closed = true;
                            break;
                        }
                        code.Add(StripIndent(lines[i], indent));
                    }
                    TrimBlankEnds(code);
                    blocks.Add(new ChatCodeBlock(language, string.Join("\n", code), ++codeIndex, !closed));
                    continue;
                }

                if (trimmed.Length == 0)
                {
                    // A blank line ends a paragraph but not a list: a loose list continues when the
                    // next line is another item or an indented continuation.
                    FlushParagraph();
                    continue;
                }

                if (IsRule(trimmed))
                {
                    FlushParagraph();
                    FlushList();
                    continue;
                }

                if (TryHeading(trimmed, out var level, out var headingText))
                {
                    FlushParagraph();
                    FlushList();
                    blocks.Add(new ChatHeading(level, ParseInlines(headingText)));
                    continue;
                }

                if (TryListItem(trimmed, out var ordered, out var number, out var itemText))
                {
                    FlushParagraph();
                    if (list != null && list.Ordered != ordered) FlushList();
                    list ??= new ListBuilder(ordered, ordered ? number : 1);
                    list.Items.Add(new StringBuilder(itemText));
                    continue;
                }

                if (list != null && (line[0] == ' ' || line[0] == '\t'))
                {
                    // An indented line continues the last item (also after a blank line in a loose list).
                    list.Items[list.Items.Count - 1].Append(' ').Append(trimmed);
                    continue;
                }

                FlushList();
                paragraph.Add(trimmed);
            }

            FlushParagraph();
            FlushList();
            return blocks;
        }

        /// <summary>The number of code blocks in a parse — for "block n of N" labels.</summary>
        public static int CountCodeBlocks(IReadOnlyList<ChatBlock> blocks)
        {
            var count = 0;
            foreach (var block in blocks)
            {
                if (block is ChatCodeBlock) count++;
            }
            return count;
        }

        /// <summary>
        /// Splits prose into text, <c>**bold**</c> and <c>`code`</c> runs. A backtick run closes only on
        /// a run of the same length (so <c>``a`b``</c> holds a backtick); an unclosed marker is literal.
        /// </summary>
        public static IReadOnlyList<ChatInline> ParseInlines(string? text)
        {
            var result = new List<ChatInline>();
            if (string.IsNullOrEmpty(text))
                return result;

            var s = text!;
            var pending = new StringBuilder();

            void FlushText()
            {
                if (pending.Length == 0) return;
                result.Add(new ChatInline(ChatInlineKind.Text, pending.ToString()));
                pending.Clear();
            }

            var i = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (c == '`')
                {
                    var run = RunLength(s, i, '`');
                    var close = FindRun(s, i + run, '`', run);
                    if (close >= 0)
                    {
                        FlushText();
                        var inner = s.Substring(i + run, close - (i + run));
                        if (inner.Length >= 2 && inner[0] == ' ' && inner[inner.Length - 1] == ' ' && inner.Trim().Length > 0)
                            inner = inner.Substring(1, inner.Length - 2);
                        result.Add(new ChatInline(ChatInlineKind.Code, inner));
                        i = close + run;
                        continue;
                    }
                    pending.Append(s, i, run);
                    i += run;
                    continue;
                }

                if (c == '*' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    var close = s.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (close > i + 2)
                    {
                        FlushText();
                        result.Add(new ChatInline(ChatInlineKind.Bold, s.Substring(i + 2, close - (i + 2))));
                        i = close + 2;
                        continue;
                    }
                    pending.Append("**");
                    i += 2;
                    continue;
                }

                pending.Append(c);
                i++;
            }

            FlushText();
            return result;
        }

        // ── line classification ─────────────────────────────────────────────────────────────

        private static bool TryOpenFence(string line, out int indent, out int run, out string language)
        {
            indent = 0;
            run = 0;
            language = string.Empty;
            while (indent < line.Length && line[indent] == ' ') indent++;
            if (string.CompareOrdinal(line, indent, Fence, 0, Fence.Length) != 0)
                return false;

            run = RunLength(line, indent, '`');
            var info = line.Substring(indent + run).Trim();
            if (info.IndexOf('`') >= 0)
                return false;   // "```inline```" on one line is prose, not a fence

            if (info.Length > 0)
            {
                var end = 0;
                while (end < info.Length && !char.IsWhiteSpace(info[end])) end++;
                language = info.Substring(0, end).ToLowerInvariant();
            }
            return true;
        }

        private static bool IsClosingFence(string line, int openRun)
        {
            var trimmed = line.Trim();
            if (trimmed.Length < openRun || trimmed.Length < Fence.Length)
                return false;
            foreach (var c in trimmed)
            {
                if (c != '`') return false;
            }
            return true;
        }

        private static string StripIndent(string line, int indent)
        {
            var strip = 0;
            while (strip < indent && strip < line.Length && line[strip] == ' ') strip++;
            return line.Substring(strip);
        }

        private static void TrimBlankEnds(List<string> lines)
        {
            while (lines.Count > 0 && lines[lines.Count - 1].Trim().Length == 0) lines.RemoveAt(lines.Count - 1);
            while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        }

        private static bool IsRule(string trimmed)
        {
            if (trimmed.Length < 3) return false;
            var first = trimmed[0];
            if (first != '-' && first != '*' && first != '_') return false;
            foreach (var c in trimmed)
            {
                if (c != first) return false;
            }
            return true;
        }

        private static bool TryHeading(string trimmed, out int level, out string text)
        {
            level = 0;
            text = string.Empty;
            while (level < trimmed.Length && trimmed[level] == '#') level++;
            if (level < 1 || level > 3 || level >= trimmed.Length || trimmed[level] != ' ')
                return false;
            text = trimmed.Substring(level + 1).Trim();
            return true;
        }

        private static bool TryListItem(string trimmed, out bool ordered, out int number, out string text)
        {
            ordered = false;
            number = 0;
            text = string.Empty;
            if (trimmed.Length >= 2 && (trimmed[0] == '-' || trimmed[0] == '*') && trimmed[1] == ' ')
            {
                text = trimmed.Substring(2).Trim();
                return text.Length > 0;
            }

            var digits = 0;
            while (digits < trimmed.Length && digits < 9 && char.IsDigit(trimmed[digits])) digits++;
            if (digits == 0 || digits + 1 >= trimmed.Length || trimmed[digits] != '.' || trimmed[digits + 1] != ' ')
                return false;
            text = trimmed.Substring(digits + 2).Trim();
            if (text.Length == 0) return false;
            ordered = true;
            number = int.Parse(trimmed.Substring(0, digits), System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        private static int RunLength(string s, int start, char c)
        {
            var end = start;
            while (end < s.Length && s[end] == c) end++;
            return end - start;
        }

        /// <summary>Index of the next run of exactly <paramref name="length"/> <paramref name="c"/> characters, or -1.</summary>
        private static int FindRun(string s, int from, char c, int length)
        {
            var j = from;
            while (j < s.Length)
            {
                if (s[j] != c)
                {
                    j++;
                    continue;
                }
                var run = RunLength(s, j, c);
                if (run == length) return j;
                j += run;
            }
            return -1;
        }

        private sealed class ListBuilder
        {
            public ListBuilder(bool ordered, int start)
            {
                Ordered = ordered;
                Start = start;
            }

            public bool Ordered { get; }
            public int Start { get; }
            public List<StringBuilder> Items { get; } = new List<StringBuilder>();

            public ChatListBlock Build()
            {
                var items = new List<IReadOnlyList<ChatInline>>(Items.Count);
                foreach (var item in Items) items.Add(ParseInlines(item.ToString()));
                return new ChatListBlock(Ordered, Start, items);
            }
        }
    }
}
