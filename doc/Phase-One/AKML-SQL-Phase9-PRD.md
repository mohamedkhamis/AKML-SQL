# AKML SQL — Phase 9: AI-Powered SQL Assistance

> **Version:** 1.0 | **Date:** March 2026 | **Author:** Mohamed Khamis
> **Status:** Ready for Implementation | **Classification:** Confidential
> **Depends on:** Phase 2 (IntelliSense + schema cache), Phase 5 (code analysis)
> **Branch prefix:** `009-ai-assistance`

---

## 1. Executive Summary

Phase 9 is the culmination of the first nine phases — it integrates AI into the deterministic foundation built by Phases 2–8. Where traditional IntelliSense suggests based on grammar rules and schema metadata, AI assistance understands *intent*. It can generate entire queries from natural language, explain unfamiliar code in plain English, fix broken SQL, optimize slow queries, suggest missing indexes, and act as a context-aware coding assistant.

This is the phase that transforms AKML SQL from a "better IntelliSense" into an "AI-powered development platform." SQL Prompt added AI features (text-to-SQL, explain, fix, optimize) in late 2025. AKML SQL goes further with a **multi-model architecture** (user chooses their AI provider), **complete privacy controls** (no data leaves without explicit consent), **offline mode** (local model support), and **deep schema awareness** (AI knows your actual database structure, not just generic SQL).

### Core Philosophy: AI as Co-Pilot, Not Auto-Pilot

AI features are **opt-in only**. They never alter code without explicit user action. They run alongside the deterministic features from Phases 2–8, not instead of them. The user is always in control: AI suggests, the user decides.

---

## 2. Document Metadata

| Field | Value |
|---|---|
| **Phase** | Phase 9 — AI-Powered SQL Assistance |
| **Depends on** | Phase 2 (schema cache), Phase 3 (formatter), Phase 5 (code analysis) |
| **Target** | All SSMS + VS targets |
| **AI Models** | Claude (Anthropic), GPT-4o (OpenAI), Gemini (Google), Ollama/LM Studio (local) |
| **Privacy** | Opt-in only; no data transmitted without consent; offline mode available |

---

## 3. AI Features — Complete List

### 3.1 Text-to-SQL (Natural Language → SQL)

**Trigger:** AKML SQL → AI Assistant, or `Ctrl+Shift+G`, or type `--ai:` prefix in editor

**Input:** Natural language description of what the user wants to query.

**Process:**
1. User types: `--ai: show me the top 10 customers by total order amount this year`
2. Engine sends the prompt + current database schema (table names, column names, types, FK relationships) to the AI model
3. AI returns generated SQL
4. AKML SQL displays the result in a diff-style preview (not auto-inserted)
5. User can Accept, Edit, or Reject the suggestion

**Schema Context:**
- AI receives a compressed schema snapshot: table names, column names with types, primary keys, foreign keys, and table descriptions (if extended properties exist)
- Schema is filtered to relevant objects based on keywords in the prompt
- Maximum context: 500 objects (tables + columns) per request

**Example:**
```
User: --ai: Get all orders placed in the last 30 days with customer name and total amount,
           sorted by amount descending

AI generates:
SELECT
    c.CompanyName,
    o.OrderID,
    o.OrderDate,
    SUM(od.UnitPrice * od.Quantity * (1 - od.Discount)) AS TotalAmount
FROM dbo.Orders AS o
INNER JOIN dbo.Customers AS c
    ON c.CustomerID = o.CustomerID
INNER JOIN dbo.[Order Details] AS od
    ON od.OrderID = o.OrderID
WHERE o.OrderDate >= DATEADD(DAY, -30, GETDATE())
GROUP BY c.CompanyName, o.OrderID, o.OrderDate
ORDER BY TotalAmount DESC;
```

### 3.2 AI Explain

**Trigger:** Select code → right-click → "AI Explain", or `Ctrl+Shift+E`

**Output:** Plain-English explanation of what the selected SQL does, broken into sections:
- **Purpose:** One-sentence summary
- **Step by step:** Numbered explanation of each clause
- **Key details:** Data types, potential performance concerns, edge cases
- **Suggestions:** Optional improvements or warnings

### 3.3 AI Fix SQL

**Trigger:** When a query fails with an error → notification offers "Fix with AI", or `Shift+Alt+R`

**Process:**
1. AKML SQL captures the error message and the SQL that caused it
2. Sends SQL + error + schema context to AI
3. AI returns corrected SQL with explanation of what was wrong
4. User sees diff view: original vs. fixed, with annotations

**Auto-trigger (optional):** When enabled, AKML SQL automatically offers to fix errors detected during execution. Disabled by default — user must enable in settings.

### 3.4 AI Optimize SQL

**Trigger:** Select code → right-click → "AI Optimize", or `Ctrl+Shift+O`

**Process:**
1. AKML SQL sends the query + schema (including indexes and FK relationships) to AI
2. AI analyzes for performance improvements:
   - SARGability improvements
   - JOIN order optimization
   - Index utilization suggestions
   - Unnecessary DISTINCT/subquery elimination
   - Set-based alternatives to cursor patterns
3. Returns optimized SQL with categorized annotations:
   - **Safe changes** (applied automatically): formatting, redundant code removal
   - **Review changes** (shown as comments): query restructuring, index suggestions

### 3.5 AI Index Suggestions

**Trigger:** AKML SQL → AI → Query Index Analysis, or after viewing execution plan

**Process:**
1. Analyze a query (or execution plan XML) for missing index opportunities
2. AI considers: the query pattern, existing indexes, table statistics, FK relationships
3. Returns CREATE INDEX scripts with:
   - Expected improvement estimate
   - Index size estimate
   - Impact on write performance
   - Covering index recommendations

### 3.6 AI Chat Panel

**Trigger:** AKML SQL → AI Chat, or `Ctrl+Shift+A`

An interactive chat panel docked in SSMS/VS where users can have a conversation with AI about their database:

```
┌──────────────────────────────────────────────────────┐
│  AKML SQL AI Assistant                    [⚙] [X]    │
├──────────────────────────────────────────────────────┤
│                                                      │
│  You: How can I improve the performance of my        │
│       GetOrdersByDate stored procedure?              │
│                                                      │
│  AI: I've analyzed the procedure against your        │
│      schema. Here are 3 recommendations:             │
│                                                      │
│  1. The WHERE clause on OrderDate doesn't use        │
│     a SARGable pattern. Change:                      │
│     `WHERE YEAR(OrderDate) = @Year`                  │
│     to:                                              │
│     `WHERE OrderDate >= @StartDate                   │
│      AND OrderDate < @EndDate`                       │
│                                                      │
│  2. Add a covering index:                            │
│     [Apply Index] [Copy Script]                      │
│                                                      │
│  3. Consider adding SET NOCOUNT ON at the start.     │
│     [Apply Fix]                                      │
│                                                      │
│  ┌──────────────────────────────────────────────┐    │
│  │  Type your question...                   [⏎] │    │
│  └──────────────────────────────────────────────┘    │
└──────────────────────────────────────────────────────┘
```

### 3.7 AI-Powered Code Completion (Inline Ghost Text)

**Trigger:** Automatic as user types (opt-in)

While typing, AI predicts the next line(s) of SQL and shows them as gray ghost text (like GitHub Copilot). Press Tab to accept, Escape to dismiss.

This operates alongside (not replacing) the deterministic Phase 2 IntelliSense. Phase 2 handles single-token completion (keywords, objects, columns); AI handles multi-line prediction (e.g., after typing `FROM dbo.Orders o`, AI predicts the entire JOIN chain).

---

## 4. Multi-Model Architecture

### 4.1 Supported AI Providers

| Provider | Model | Connection | Latency | Privacy |
|---|---|---|---|---|
| **Anthropic** | Claude 4 Sonnet/Opus | API key | ~1–3 sec | Data sent to Anthropic API |
| **OpenAI** | GPT-4o, GPT-4o-mini | API key | ~1–3 sec | Data sent to OpenAI API |
| **Azure OpenAI** | GPT-4o (Azure-hosted) | Azure credentials | ~1–2 sec | Data stays in your Azure tenant |
| **Google** | Gemini 2.5 Pro/Flash | API key | ~1–3 sec | Data sent to Google API |
| **Ollama** | Llama 3, Mistral, CodeLlama | Local (localhost) | ~2–10 sec | **100% offline, no data leaves machine** |
| **LM Studio** | Any GGUF model | Local (localhost) | ~2–10 sec | **100% offline** |
| **Custom endpoint** | Any OpenAI-compatible API | Custom URL + key | Variable | User-controlled |

### 4.2 Provider Configuration

```json
{
  "ai": {
    "enabled": true,
    "provider": "anthropic",
    "model": "claude-sonnet-4-20250514",
    "apiKey": "encrypted:...",
    "maxTokens": 4096,
    "temperature": 0.2,
    "timeout": 30,
    "retries": 2,
    "privacyMode": "schemaOnly",
    "offlineProvider": "ollama",
    "offlineModel": "codellama:13b"
  }
}
```

### 4.3 Privacy Modes

| Mode | Schema Sent | Query Text Sent | Description |
|---|---|---|---|
| `full` | ✔ | ✔ | Best AI quality; sends schema + query text |
| `schemaOnly` | ✔ | ✔ (anonymized) | Table/column names sent, but data values redacted |
| `anonymous` | ✔ (hashed names) | ✔ (hashed names) | Table/column names hashed; AI sees structure but not real names |
| `offline` | N/A | N/A | Local model only; zero data transmission |
| `disabled` | N/A | N/A | All AI features off |

---

## 5. Schema Context Compression

To fit within AI model context limits while providing maximum schema awareness:

| Context Level | Content | Token Estimate |
|---|---|---|
| **Level 1** (always) | Database name, schema names, table/view names with row counts | ~200 tokens |
| **Level 2** (on demand) | Column names and types for referenced tables | ~500–2000 tokens |
| **Level 3** (on demand) | Primary keys, foreign keys, indexes for referenced tables | ~300–1000 tokens |
| **Level 4** (on demand) | Extended properties / descriptions | ~200–500 tokens |

The engine dynamically selects the appropriate level based on the AI request type and available context window.

---

## 6. Configuration

| Setting | Default | Description |
|---|---|---|
| `ai.enabled` | `false` | Master switch (opt-in) |
| `ai.provider` | (none) | AI provider selection |
| `ai.textToSql` | `true` | Enable text-to-SQL generation |
| `ai.explain` | `true` | Enable AI Explain |
| `ai.fix` | `true` | Enable AI Fix |
| `ai.autoFixOnError` | `false` | Auto-offer fix when query fails |
| `ai.optimize` | `true` | Enable AI Optimize |
| `ai.indexSuggestions` | `true` | Enable AI index analysis |
| `ai.inlineCompletion` | `false` | Enable ghost text predictions (opt-in) |
| `ai.chatPanel` | `true` | Enable AI chat panel |
| `ai.privacyMode` | `schemaOnly` | Privacy level for AI requests |
| `ai.shortcutGenerate` | `Ctrl+Shift+G` | Text-to-SQL shortcut |
| `ai.shortcutExplain` | `Ctrl+Shift+E` | Explain shortcut |
| `ai.shortcutFix` | `Shift+Alt+R` | Fix shortcut |
| `ai.shortcutOptimize` | `Ctrl+Shift+O` | Optimize shortcut |
| `ai.shortcutChat` | `Ctrl+Shift+A` | Chat panel shortcut |
| `ai.telemetry` | `false` | Send anonymous AI usage statistics |

---

## 7. Performance Requirements

| Metric | Target |
|---|---|
| Text-to-SQL response (cloud) | < 5 seconds |
| AI Explain response | < 3 seconds |
| AI Fix response | < 5 seconds |
| AI Optimize response | < 8 seconds |
| Inline completion latency | < 500ms for ghost text |
| Schema context preparation | < 200ms |
| Local model response (Ollama) | < 15 seconds |

---

## 8. Testing Requirements

| Area | Test Count |
|---|---|
| Text-to-SQL accuracy | 100+ prompts across various query types |
| AI Explain correctness | 50+ queries with verified explanations |
| AI Fix success rate | 50+ common SQL error patterns |
| AI Optimize quality | 30+ query optimization scenarios |
| Privacy mode verification | 20+ tests verifying no data leaks per mode |
| Multi-model switching | 15+ tests across all providers |
| Offline mode (Ollama) | 20+ tests without internet |
| Schema context accuracy | 30+ tests verifying correct schema sent |
| Error handling | 25+ tests for API failures, timeouts, rate limits |

---

## 9. Competitive Comparison

| Feature | SQL Prompt AI | DataGrip AI | GitHub Copilot | AKML SQL Phase 9 |
|---|---|---|---|---|
| Text-to-SQL | ✔ | ✔ | ✔ | **✔** |
| AI Explain | ✔ | ✔ | No | **✔** |
| AI Fix | ✔ | No | No | **✔** |
| AI Optimize | ✔ | No | No | **✔** |
| AI Index Suggestions | ✔ | No | No | **✔** |
| Inline ghost text | No | ✔ | ✔ | **✔** |
| Interactive chat panel | No | ✔ | ✔ | **✔** |
| Schema-aware context | ✔ | ✔ | No | **✔ (compressed, multi-level)** |
| Multi-model support | No (GPT-4o only) | No (JetBrains AI) | No (OpenAI only) | **✔ (6+ providers)** |
| Local/offline models | No | No | No | **✔ (Ollama, LM Studio)** |
| Privacy modes | Limited | Limited | Limited | **✔ (4 levels + offline)** |
| Azure OpenAI (own tenant) | No | No | ✔ (via Copilot Business) | **✔** |
| Auto-fix on error | ✔ (optional) | No | No | **✔ (optional)** |
| Opt-in only | Yes | No (default on) | No (default on) | **Yes (off by default)** |
| BYOK (bring your own key) | No | No | No | **✔** |
| Diff preview before accept | ✔ | No | No | **✔** |

---

## 10. Timeline & Milestones

| Week | Milestone | Deliverable |
|---|---|---|
| 1–2 | AI infrastructure | Multi-model provider abstraction, API key management, schema context compressor, privacy modes |
| 3–4 | Text-to-SQL & AI Explain | Natural language to SQL generation, code explanation engine, diff preview UI |
| 5–6 | AI Fix & AI Optimize | Error capture + fix pipeline, query optimization engine, annotation system |
| 7–8 | AI Index Suggestions & Chat | Index analysis from execution plans, interactive chat panel UI |
| 9–10 | Inline Completion & Ghost Text | Copilot-style predictive text, debounce logic, acceptance tracking |
| 11–12 | Local models & Offline mode | Ollama/LM Studio integration, model management UI, offline fallback |
| 13–14 | QA, accuracy testing & polish | Prompt engineering refinement, accuracy benchmarks, false positive testing, full test matrix |

**Total estimated duration: 14 weeks** (3.5 months). This is the second-longest phase (after Phase 2) due to the breadth of AI features and the need for extensive accuracy testing.

---

## 11. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| AI hallucinations (incorrect SQL) | User executes wrong query | Always show diff preview; never auto-execute AI-generated SQL; mark AI suggestions clearly |
| API cost for cloud models | High per-token costs for heavy users | Implement token budgets, caching for identical requests, suggest smaller/local models for frequent use |
| Latency for cloud API calls | UX feels slow | Show loading indicators, implement streaming responses, cache schema context, offer local model fallback |
| Privacy concerns | Enterprise users refuse to send schema to cloud | 4 privacy modes including full offline; Azure OpenAI keeps data in user's own tenant |
| Model quality variance | Different providers give different quality results | Provide recommended settings per model; community-shared prompt templates; A/B test different models |
| Local model quality | Ollama models less accurate than cloud models | Set clear expectations in UI; show model capability badge; recommend minimum model sizes |
| Rate limiting by API providers | Features stop working during heavy use | Implement request queuing, exponential backoff, graceful degradation to Phase 2 IntelliSense |

---

## 12. Success Metrics

- **Text-to-SQL accuracy:** > 85% of generated queries are correct (execute without error on the target schema)
- **AI Explain helpfulness:** > 80% of users rate explanations as "helpful" or "very helpful"
- **AI Fix success rate:** > 70% of common SQL errors resolved by AI Fix
- **AI Optimize quality:** > 60% of optimization suggestions measurably improve query performance
- **Adoption:** > 40% of users enable at least one AI feature within 30 days
- **Privacy satisfaction:** 100% of enterprise users report privacy controls meet their requirements
- **Offline mode:** Fully functional AI features with Ollama/LM Studio (no internet required)
- **No auto-execution:** Zero cases where AI-generated SQL is executed without explicit user confirmation

---

*End of Phase 9 PRD — AKML SQL v1.0*
