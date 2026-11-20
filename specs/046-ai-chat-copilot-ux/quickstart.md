# Quickstart — spec 046 AI Chat (Copilot-style, SSMS + web)

The acceptance gate for "done". Automated checks first, then the SSMS scenarios the owner runs by
hand (the Messages-pane case cannot be automated).

## Build

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet
"$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal      # one pass, green
powershell -ExecutionPolicy Bypass -File scripts/generate-theme-css.ps1 -CheckOnly   # unchanged tokens, stays green
dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug                      # required before any E2E run
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Debug
```

## Tests

```bash
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj --filter "FullyQualifiedName~ChatMarkdown"
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj --filter "FullyQualifiedName~AiChat|FullyQualifiedName~ChatBinding"
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName~AiChatResponse|FullyQualifiedName~AiSessionBinding"
dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter "FullyQualifiedName~AiChat|FullyQualifiedName~ChatMessageView|FullyQualifiedName~AiAgentSelector"
node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj --filter "FullyQualifiedName~AiChat" -- xUnit.ParallelizeTestCollections=false
```

Then the full suites of the touched projects, and the untouched gates as proof of no regression:

```bash
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj
dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj --filter "Category!=InstallerSmoke"
```

What each automated test proves:

| Test | Contract rule |
|------|---------------|
| `ChatMarkdownTests` | chat-rendering Part 1 — every grammar case, unclosed fence keeps text |
| `ChatBindingResolverTests` | chat-binding Part 1 — all six rows of the decision table, sticky survival and drop |
| `AiChatSessionBindingTests` (extended) | header and send read one binding; three refusal texts; the Information log line through a Serilog test sink; no fabricated id |
| `AiChatRenderingTests` (STA) | prose in a read-only RichTextBox, code panel per block with Copy and insertable Insert, labels "n of N", raw-text copy |
| `AiChatComposerStateTests` (extended) | Send tooltip for the four states; placeholder text; New chat enablement and effect; composer focused after New chat |
| `AiChatAgentPickerTests` (extended) | entries read `Name · Provider · model`; the FR-034 notice once per change, none when re-picking |
| `AiChatResponseAgentFieldsTests` | keys 7–8 round-trip through MessagePack; null from an older payload; handler fills them for a fallback agent |
| `AiChatPanelComposerTests` (web, extended) | tooltip states, placeholder, New chat replaces Clear, stream cancelled |
| `ChatMessageViewTests` (bUnit) | blocks rendered, Copy and Insert at caret per block, `CanInsert=false` hides Insert |
| `AiAgentSelectorTests` (bUnit) | usable agents only, label format, `Manage agents…` navigates, change calls `SetActiveAsync` |
| `akml-ui.scroll.test.mjs` | `isAtBottom` threshold, `scrollToBottom` |
| `AiChatComposerKeysTests` (E2E, extended) | Send `title` = `Send (Ctrl+Enter)`; a mock answer with a fenced block renders `chat-code-block` with Insert at caret landing in the editor |

## SSMS scenarios (manual, owner)

Install the built extension, open SSMS 22, open the AI Chat, and connect a query window to a test
database. Record results in `baseline.md` (pass/fail, SSMS build, date).

| # | Steps | Expected |
|---|-------|----------|
| S1-1 | Run any query; click inside the **Messages** pane; click into the chat; send "which tables are largest?" | Answered about the header's database; no refusal |
| S1-2 | Execute a query and send a chat message immediately | Answered |
| S1-3 | Click inside the **Results** grid; send | Answered |
| S1-4 | Open a second query window on another database; activate it; send | Header names the second database within 2 s; one note `Now answering about …`; answer is about it |
| S1-5 | Click into Object Explorer; send | Still answered about the last query window; header unchanged |
| S1-6 | Disconnect the bound window (Query ▸ Connection ▸ Disconnect); send | Refusal `SQLQueryN.sql is not connected …`; one `AiChatPanel: refused to send — NotConnected …` line in `%AppData%\AKML SQL\logs\akmlsql-*.log` |
| S1-7 | Close all query windows; send | Refusal `No query window is open …`; log line `NoDocument` |
| S2-1 | Ask for "two queries: the largest tables and the newest procedures, explain each" | Two code panels, each with Copy and Insert into query; explanation rendered (no `#`, `-`, backticks visible); attribution `Name · Provider · model` |
| S2-2 | Insert block 2 | Exactly block 2 at the caret of the bound window; button flashes `✓ Inserted` |
| S2-3 | Scroll up in a long conversation, send from the composer | View follows your own message; an answer arriving while scrolled up does not move the view |
| S2-4 | Switch theme light → dark → high contrast | All chat elements follow; tab through every control with a screen reader on — each announced by name |
| S3-1 | Type, press Ctrl+Enter | Sent (if not: record it — research R14 fallback) |
| S3-2 | Hover Send with text / empty / while sending | `Send (Ctrl+Enter)` / `Type a question first` / `Wait for the answer to finish` |
| S3-3 | Hover New chat, the selector, Copy conversation, a block's Copy and Insert | Each shows its tooltip |
| S4-1 | Configure two agents; open the selector | Entries `Name · Provider · model`; current one selected |
| S4-2 | Pick the other agent; send | Note `Now answering with …`; answer attributed to it |
| S4-3 | Close and reopen the chat window | Same agent selected |
| S5-1 | After two turns, click New chat | Conversation empty, welcome card with three starters, composer focused, header and agent unchanged |
| S5-2 | Send "and the second one?" | The assistant does not refer to the cleared turns |
| S5-3 | Click a starter question | Sent as typed |

## Web scenarios (Playwright where listed, otherwise manual in Chromium)

| # | Steps | Expected |
|---|-------|----------|
| W1 | Settings › AI: two agents; open chat | Selector in the composer with both labels; current = active |
| W2 | Change the selector; send | Line `Now answering with …`; the AI assistance tab's actions also use that agent |
| W3 | Hover Send; press Ctrl+Enter | `Send (Ctrl+Enter)`; sent (E2E) |
| W4 | Mock answer with a fenced block | Code panel with Copy and Insert at caret; Insert lands in the editor (E2E) |
| W5 | New chat while streaming | Stream stops, conversation empty, composer focused; reload shows the empty conversation |
| W6 | Export after a code-block answer | Markdown contains the raw fences |

## Log check (FR-005)

```bash
grep -n "AiChatPanel: refused to send" "$APPDATA/AKML SQL/logs/akmlsql-$(date +%Y%m%d).log"
```

One line per refusal, naming the state, the document file name and the three booleans; no SQL,
path, server or login in the line.

## Documentation check

`doc/ipc-api.md` shows keys 7 and 8 under `AiChatResult (175)`; `doc/use-cases.md` describes the
binding rule, Ctrl+Enter, the selector and New chat; `doc/progress.md` has the spec 046 section;
`CLAUDE.md` has one bullet under latest work.
