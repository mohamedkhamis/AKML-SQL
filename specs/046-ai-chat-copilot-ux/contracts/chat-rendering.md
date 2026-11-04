# Contract: rendered answers, code panels, welcome and follow-scroll

Satisfies **FR-020 – FR-026** (User Story 2) and the web half of **User Story 6**. Research R5–R8, R11.

**Anchors**: `src/AkmlSql.Core/Text/` (new `ChatMarkdown.cs`) · `src/AkmlSql.Shell.Shared/Ai/AiChatPanel.cs`
(`AddAssistantMessage`, `CreateMessageBubble`, `FlashButtonContent`, `OnInsertSqlClick`) ·
`src/AkmlSql.Web/Shared/AiChatPanel.razor` · `src/AkmlSql.Web/Pages/Editor.razor:2672` (`OnAiAcceptResultAsync`) ·
`src/AkmlSql.Web/wwwroot/js/akml-ui.js`

---

## Part 1 — `ChatMarkdown.Parse` (Core, shared)

Input: the answer text. Output: `IReadOnlyList<ChatBlock>` (data-model). Grammar, line by line:

```
fence      := "```" [language] NEWLINE … "```"        # unclosed → runs to end, Unclosed = true
heading    := ("#" | "##" | "###") SPACE inlines
bullet     := ("-" | "*") SPACE inlines                # consecutive → one ListBlock(Ordered=false)
numbered   := DIGITS "." SPACE inlines                 # consecutive → one ListBlock(Ordered=true)
paragraph  := any other non-blank lines until a blank line or another block start
inlines    := ( "**" text "**" | "`" text "`" | text )*   # unclosed markers are literal
```

Test cases that define it (`ChatMarkdownTests`): empty → no blocks; a paragraph; two paragraphs;
heading levels 1–3 (`####` is a paragraph); bullets with `-` and `*`; numbered; mixed list then
paragraph; fenced `sql` block with surrounding prose; two fenced blocks (Index 1 and 2); fence
without language; unclosed fence keeps all text; inline code and bold; unclosed backtick is
literal; CRLF input equals LF input; a fence inside a paragraph line is literal.

## Part 2 — WPF renderer (SSMS)

`ChatMessageRenderer.Build(IReadOnlyList<ChatBlock> blocks, Action<string> copy, Action<string> insert) → StackPanel`

- Prose runs (paragraph, heading, list) are grouped into one read-only `RichTextBox`
  (`IsReadOnly`, `IsDocumentEnabled`, `BorderThickness 0`, transparent, `IsReadOnlyCaretVisible false`)
  with a `FlowDocument` whose `PagePadding` is 0: paragraphs as `Paragraph`; headings as
  `Paragraph` with `FontWeight SemiBold` and sizes 14 / 13 / 12; lists as `List` with
  `MarkerStyle` Disc or Decimal; `Bold` as `Bold`; inline `Code` as `Run` with
  `ThemeTokens.TypographyEditor` family and `SurfaceInputReadOnly` background. Foreground
  `TextPrimary`. Selectable; Ctrl+C copies the selection.
- Each `CodeBlock` is a `Border` (`SurfaceCanvas`, `BorderDefault`, `CornerRadius 4`,
  `Margin 0,4`) containing: a header `DockPanel` (left: language label or `SQL`, `TextSecondary`,
  11 px; right: `⧉ Copy` and, when insertable, `⇩ Insert into query` — `TextLink` border/foreground
  on `SurfacePanel`, `FocusVisualStyles.HighStakes`, accessible names from data-model labels;
  *changed during implementation from `SurfaceElevated`: link text on the elevated surface reads
  4.07:1 (dark) / 4.19:1 (light), below the 4.5:1 target `ThemeContrastScanTests` enforces; the
  web buttons use `--akml-surface-panel` for the same reason*)
  and a read-only `TextBox` in the editor font with `TextWrapping NoWrap`, horizontal scroll, the
  block's `Code`.
- Copy → `TryCopyToClipboard(code)` then `FlashButtonContent(button, "✓ Copied")` / `"⚠ Copy failed"`;
  Insert → `insert(code)` then `"✓ Inserted"` / `"⚠ No query window"`.
- The bubble keeps its per-message `⧉` copy button and the attribution caption; the caption text is
  the agent label (contracts/chat-composer-and-agents.md Part 4).
- `AddAssistantMessage` no longer appends the detached action row; the old `CodeActions` list from
  the reply is ignored for rendering (kept on the wire).
- Brushes only through `SetResourceReference`; fonts hoisted to `static readonly`; no hardcoded hex.

## Part 3 — Web renderer

`ChatMessageView.razor` — parameters `Text` (string), `OnInsert` (`EventCallback<string>`),
`CanInsert` (bool). Renders the blocks as: `<p>`, `<h4>`/`<h5>`/`<h6>` (levels 1–3 inside the
panel), `<ul>`/`<ol>`, `<strong>`, `<code class="akml-chat-inline">`, and for each code block
`<div class="akml-chat-code" data-testid="chat-code-block">` with a header row (language,
`Copy`, `Insert at caret`) and `<pre><code>`. Styles: `--akml-surface-canvas`,
`--akml-border-default`, editor font variables, `--akml-surface-inputreadonly` for inline code.
Copy uses `akml-ui.js copyText` (http fallback kept) and shows `Copied` for 1.5 s on the button;
Insert invokes `OnInsert`, which `AiChatPanel.razor` forwards to its new `[Parameter] OnInsertSql`,
wired in `Editor.razor` to `OnAiAcceptResultAsync` (status line `AI result inserted.` as today).
`CanInsert` is false when the panel is shown without an editor (no callback) — the button is then
absent, not disabled.

The `<pre class="akml-chat-turn-text">` of today is replaced by `ChatMessageView` for assistant
turns; user turns keep plain pre-wrapped text.

## Part 4 — Welcome and starters (FR-023)

SSMS `AiChatWelcomeCard` (shown instead of the greeting bubble when an agent can answer and
`_history` is empty; removed on the first user message; shown again after New chat):

```
┌──────────────────────────────────────────────────────────────┐
│  Ask about {server}.{database}            (or: Not connected to a database)
│  Answers use the schema of the query window's database.      │
│  SQL in answers can be inserted into the query window.       │
│                                                              │
│  [ What are the largest tables in this database? ]           │
│  [ Which tables have no primary key? ]                       │
│  [ Which stored procedures were modified most recently? ]    │
└──────────────────────────────────────────────────────────────┘
```

A starter sets the composer text and calls `SendMessageAsync()`; disabled (with the Send
tooltip's reason) while sending. Web: the same three buttons inside `akml-chat-empty`
(`data-testid="chat-starter"`); the title names nothing database-specific (`Ask anything about your SQL`).

## Part 5 — Follow-scroll (FR-024)

- SSMS: `bool wasAtBottom = _scrollViewer.VerticalOffset >= _scrollViewer.ScrollableHeight - 8;`
  captured **before** adding a child; `ScrollToEnd()` only when true. Notices and the welcome card
  use the same rule. The user's own message always scrolls (sending is an explicit action).
- Web: `akml-ui.js` exports `isAtBottom(el, threshold = 8)` and `scrollToBottom(el)`;
  `AiChatPanel.razor` captures `isAtBottom` before adding turns and calls `scrollToBottom` after
  render when it was true or when the user sent. Node test: `akml-ui.scroll.test.mjs` with a fake
  element (`scrollTop`, `scrollHeight`, `clientHeight`).

## Part 6 — Copy and export keep the raw text (FR-026)

Per-message copy, copy conversation (SSMS) and Export (web) use the stored raw text of each turn,
never the rendered tree. Verified by the existing copy tests plus one case with a code block.
