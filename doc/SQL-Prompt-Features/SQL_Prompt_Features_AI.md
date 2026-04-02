# SQL Prompt — AI Features Reference

> **Purpose:** Design reference for AKML SQL — all AI-powered features from Redgate SQL Prompt AI (formerly "Prompt+"), described with full UI, UX, settings, and design details.
>
> **Scope:** AI features only (see separate Core Features document for all other features)

---

## Table of Contents

1. [Overview & Architecture](#1-overview--architecture)
2. [Prompt AI Window (Main AI Interface)](#2-prompt-ai-window-main-ai-interface)
3. [Natural Language to SQL](#3-natural-language-to-sql)
4. [Explain SQL](#4-explain-sql)
5. [AI Fix (Auto-Repair Broken SQL)](#5-ai-fix-auto-repair-broken-sql)
6. [AI Code Completion (Ghost Text)](#6-ai-code-completion-ghost-text)
7. [Query Index Analysis](#7-query-index-analysis)
8. [Optimize Query](#8-optimize-query)
9. [AI Settings & Configuration](#9-ai-settings--configuration)
10. [AI UI Color & Theme System](#10-ai-ui-color--theme-system)
11. [AI Keyboard Shortcuts](#11-ai-keyboard-shortcuts)

---

## 1. Overview & Architecture

SQL Prompt AI is a suite of opt-in AI-powered features that enhance the traditional SQL Prompt experience. All AI features are available to users with an active subscription license (standalone SQL Prompt or via SQL Toolbelt Essentials).

![SQL Prompt AI Overview](./images/06_ai_window.svg)

### 1.1 Key Design Principles

| Principle | Implementation |
|-----------|---------------|
| **Opt-in** | AI features are disabled by default. User must explicitly enable them |
| **Schema-aware** | AI requests include the connected database schema (tables, columns, types, keys) and SQL Server version for context |
| **Non-intrusive** | AI suggestions don't replace traditional IntelliSense. They supplement it |
| **Toggleable** | Master on/off via `Ctrl+Shift+B`. Individual features can be disabled independently |
| **Privacy-first** | User controls what data is sent. Schema can be excluded if desired |

### 1.2 Feature Architecture

```
┌─────────────────────────────────────────────────────┐
│                  SQL Prompt AI                       │
├──────────────────┬──────────────────────────────────┤
│                  │                                   │
│  CLOUD AI        │   LOCAL AI                        │
│  (Generative)    │   (ML Model)                      │
│                  │                                   │
│  • NL → SQL      │   • Query Index Analysis          │
│  • Explain SQL   │     (uses internal ML model,      │
│  • AI Fix        │      no cloud connection needed)   │
│  • Optimize      │                                   │
│  • AI Completion │                                   │
│                  │                                   │
└──────────────────┴──────────────────────────────────┘
```

### 1.3 Schema Context Sent to AI

When making AI requests, SQL Prompt sends:
- **Database schema:** Table names, column names, data types, primary keys, foreign keys, indexes
- **SQL Server version:** e.g., "SQL Server 2022" — so the AI generates version-compatible code
- **Current query context:** The SQL code in the active editor window
- **NOT sent:** Actual data values in the database

If schema retrieval fails, AI requests proceed without schema awareness (degraded mode), and a warning is displayed.

---

## 2. Prompt AI Window (Main AI Interface)

The primary UI for interacting with AI features. A dockable panel inside SSMS.

![Prompt AI Window](./images/06_ai_window.svg)

### 2.1 Window Layout

```
┌────────────────────────────────────────────────────┐
│  SQL Prompt AI                    [📌 pin] [✕]     │
├────────────────────────────────────────────────────┤
│                                                     │
│  Connected: AdventureWorks (SQL Server 2022)        │
│  ────────────────────────────────────────────────   │
│                                                     │
│  ┌──────────────────────────────────────────────┐   │
│  │                                              │   │
│  │   [AI response area — scrollable]            │   │
│  │                                              │   │
│  │   Generated SQL appears here with            │   │
│  │   syntax highlighting                        │   │
│  │                                              │   │
│  └──────────────────────────────────────────────┘   │
│                                                     │
│  ┌──────────────────────────────────────────────┐   │
│  │  Type your request...                   [▶]  │   │
│  └──────────────────────────────────────────────┘   │
│                                                     │
│  Follow-up suggestion (1 max)                       │
│  [Give feedback]                                    │
│                                                     │
└────────────────────────────────────────────────────┘
```

### 2.2 UI Design Spec

| Element | Design Detail |
|---------|---------------|
| **Window type** | Dockable tool window (can be docked, floating, tabbed, auto-hidden) |
| **Default position** | Right side of SSMS, docked |
| **Open shortcut** | `Alt+Z` |
| **Background** | Matches SSMS theme (light or dark) |
| **Database indicator** | Shows connected database name + SQL Server version as gray suffix |
| **Input area** | Text input at bottom with placeholder text "Type your request..." |
| **Submit** | Press `Enter` or click `▶` button |
| **Response area** | Scrollable text area with syntax highlighting for generated SQL |
| **Copy support** | Select all (`Ctrl+A`) and copy (`Ctrl+C`) from response area |
| **Loading state** | Spinner icon with "working..." text during AI request |
| **Error state** | Error message with `-- ERROR` comment. If schema unavailable, shows warning |
| **Follow-up** | Up to 1 follow-up suggestion shown after each response (limited for accuracy) |
| **Feedback link** | "Give feedback" link at bottom → opens browser to feedback form |
| **Help** | `F1` key → opens documentation in browser |

### 2.3 Onboarding

| State | Display |
|-------|---------|
| **First launch** | "Welcome to SQL Prompt AI!" screen with feature overview and "Get Started" button |
| **Not connected** | Placeholder: "Press ALT-Z to open Prompt AI..." in query editor |
| **Not enabled** | Options page shows which AI features are available and why they may be unavailable |

---

## 3. Natural Language to SQL

Type a natural language description and receive valid T-SQL based on your actual database schema.

![NL to SQL](./images/06_ai_window.svg)

### 3.1 How It Works

1. User opens Prompt AI window (`Alt+Z`)
2. Types a natural language request in the input field
3. SQL Prompt sends the request + database schema + SQL Server version to the AI
4. AI generates T-SQL and returns it in the response area
5. User can copy the SQL to the editor, modify it, or ask a follow-up

### 3.2 Capabilities

| Capability | Detail |
|------------|--------|
| **Simple queries** | "Show me all orders from last month" → generates `SELECT` with date filter |
| **JOINs** | "List customers with their order totals" → generates multi-table JOIN |
| **Subqueries** | "Find products that have never been ordered" → generates correlated subquery or NOT EXISTS |
| **Aggregations** | "Show total sales by category" → generates GROUP BY with SUM |
| **Complex logic** | "Find customers who ordered more than the average order amount" → generates CTE or subquery |
| **DDL** | "Create a table for storing employee reviews" → generates CREATE TABLE |
| **Indexing** | "Create an index to speed up queries on OrderDate" → generates CREATE INDEX |
| **Schema-aware** | Uses actual table/column names from the connected database |
| **Version-aware** | Generates SQL compatible with the connected SQL Server version (avoids newer features on older versions) |

### 3.3 Limitations

| Limitation | Detail |
|------------|--------|
| No data access | AI cannot see or query actual data values |
| Schema size | Works with databases of unlimited size (no object limit) |
| Follow-ups | Limited to 1 follow-up suggestion per response |
| Accuracy | Generated SQL should always be reviewed before execution |

### 3.4 UI Flow

```
User types: "Show all customers who placed orders in 2024 with total amount > $1000"
                              ↓
         ┌──────────────────────────────────────────────┐
         │  SQL Prompt AI                                │
         │                                               │
         │  SELECT c.CustomerName,                       │
         │         SUM(o.TotalAmount) AS TotalSpent       │
         │  FROM dbo.Customers c                          │
         │  INNER JOIN dbo.Orders o                       │
         │      ON c.CustomerID = o.CustomerID            │
         │  WHERE o.OrderDate >= '2024-01-01'             │
         │      AND o.OrderDate < '2025-01-01'            │
         │  GROUP BY c.CustomerName                       │
         │  HAVING SUM(o.TotalAmount) > 1000              │
         │  ORDER BY TotalSpent DESC;                     │
         │                                               │
         │  [Copy to editor] [Insert at cursor]           │
         │                                               │
         │  💡 Follow-up: "Add the customer's email"      │
         └──────────────────────────────────────────────┘
```

---

## 4. Explain SQL

Select any SQL code and get a clear, human-readable explanation of what it does.

<!-- Explain SQL: scrollable text in Prompt AI window -->

### 4.1 How It Works

1. User selects SQL code in the query editor
2. Triggers "Explain SQL" from:
   - Actions List (select code → click lightbulb/actions)
   - SQL Prompt AI menu
   - Right-click context menu
3. The selected SQL + schema context is sent to AI
4. A natural language explanation appears in the Prompt AI window

### 4.2 Output Format

The explanation includes:
- **Overall purpose:** What the query accomplishes
- **Step-by-step breakdown:** Each clause explained
- **Table relationships:** How JOINs connect data
- **Filters applied:** What the WHERE clause does
- **Aggregation logic:** What GROUP BY / HAVING do
- **Potential concerns:** Any obvious issues noted

### 4.3 UI Design

| Element | Detail |
|---------|--------|
| **Output container** | Scrollable text box in the Prompt AI window |
| **Text style** | Plain text paragraphs (not code-formatted) |
| **Scrollbar** | Vertical scrollbar for long explanations |
| **Copy** | `Ctrl+A` → `Ctrl+C` to copy explanation text |
| **Length** | Varies — typically 100–500 words depending on query complexity |

---

## 5. AI Fix (Auto-Repair Broken SQL)

Select SQL code with errors and get an AI-generated fix in one click.

### 5.1 How It Works

1. User has SQL code with syntax errors or logical issues
2. Triggers "Fix SQL" from:
   - Actions List
   - Prompt AI window
   - Optional: automatic popup when errors are detected (if enabled)
3. AI analyzes the errors + schema context
4. Returns corrected SQL in the response area
5. User can apply the fix to the editor

### 5.2 Capabilities

| Capability | Detail |
|------------|--------|
| **Syntax errors** | Fixes missing commas, parentheses, keywords |
| **Multiple errors** | Can handle multiple errors in the same statement |
| **GO batch support** | Respects GO batch separators — won't remove or flag them as errors |
| **Schema-aware** | Knows actual table/column names to fix typos |
| **Version-aware** | Won't "fix" code by adding features unavailable in the connected SQL Server version |

### 5.3 Auto-Fix Popup (Optional)

| Setting | Detail |
|---------|--------|
| **Setting name** | Show fix suggestions popup dialog |
| **Default** | Off |
| **Behavior** | When enabled, a small popup appears near the error location offering to auto-fix |
| **Trigger** | Only triggers on actual SQL syntax errors, not on connection timeouts or non-SQL errors |

### 5.4 UI Flow

```
   BEFORE FIX:
   ┌────────────────────────────────────────────┐
   │  SELECT Name, Price                        │
   │  FORM dbo.Products     ← typo: FORM       │
   │  WEHRE Price > 10      ← typo: WEHRE      │
   │  ORDER Y Name          ← typo: ORDER Y    │
   │                                            │
   │  ❌ 3 syntax errors detected               │
   │     💡 [Fix with AI]                       │
   └────────────────────────────────────────────┘
                     ↓
   AFTER FIX:
   ┌────────────────────────────────────────────┐
   │  SELECT Name, Price                        │
   │  FROM dbo.Products                         │
   │  WHERE Price > 10                          │
   │  ORDER BY Name;                            │
   │                                            │
   │  ✅ 3 errors fixed                         │
   └────────────────────────────────────────────┘
```

---

## 6. AI Code Completion (Ghost Text)

Inline predictive suggestions (similar to GitHub Copilot) that appear as ghost text in the editor.

![AI Ghost Text Completion](./images/07_ai_ghost_text.svg)

### 6.1 How It Works

1. User is typing SQL code
2. After a configurable delay, or on manual trigger, the AI predicts the next line(s)
3. Predicted text appears as dimmed "ghost text" inline in the editor
4. Press `Tab` to accept, or keep typing to dismiss

### 6.2 UI Design

| Element | Detail |
|---------|--------|
| **Ghost text color** | Dimmed/grayed out — significantly lighter than normal editor text |
| **Light theme** | Ghost text: `#C0C0C0` (light gray) on white background |
| **Dark theme** | Ghost text: `#5C6370` (dim gray) on dark background |
| **Position** | Inline, immediately after the cursor position |
| **Multi-line** | Can predict multiple lines at once |
| **Dismiss** | Any non-Tab keypress dismisses the suggestion |
| **Accept** | `Tab` key accepts the full suggestion |
| **No overlap** | AI completion is separate from traditional SQL Prompt suggestions |

### 6.3 Settings

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| Enable AI code completion | `Bool` | **Off** | Master toggle for this feature (experimental/preview) |
| Auto-trigger | `Bool` | **Off** | Show ghost text automatically as you type |
| Auto-trigger delay | `Number (ms)` | **500** | Milliseconds to wait before auto-triggering |
| Manual trigger shortcut | — | `Ctrl+Alt+Up Arrow` | Invoke AI completion on demand |

### 6.4 Ghost Text vs Traditional Suggestions

| Aspect | Traditional SQL Prompt | AI Code Completion |
|--------|----------------------|-------------------|
| **Trigger** | Automatic as-you-type | Manual or delayed auto-trigger |
| **Appearance** | Popup list below/above cursor | Inline ghost text in the editor |
| **Content** | Object names, keywords, snippets | Full lines or blocks of code |
| **Accept key** | Tab/Enter (configurable) | Tab only |
| **Data source** | Schema metadata (local) | AI model (cloud) |
| **Speed** | Instant | 300–2000ms depending on complexity |
| **Accuracy** | Very high (schema-based) | Good (AI-generated, review recommended) |

---

## 7. Query Index Analysis

AI-driven analysis that suggests performance-boosting indexes for your SQL queries. Uses an internal machine learning model — does NOT use cloud-based generative AI.

### 7.1 How It Works

1. User has a SELECT query in the editor
2. Triggers from: SQL Prompt menu → AI → Query Index Analysis
3. The tool analyzes the query structure, referenced tables, WHERE clauses, JOINs, and ORDER BY
4. Returns actionable `CREATE INDEX` statements with rationale

### 7.2 UI Design

| Element | Detail |
|---------|--------|
| **Menu location** | SQL Prompt menu → AI section |
| **Output** | Results appear in the Prompt AI window |
| **Format** | `CREATE INDEX` DDL + explanation of why each index would help |
| **Engine** | Local ML model (not cloud AI) — works without internet |
| **Dependency** | Requires Prompt AI features to be enabled (disabled when AI is off) |

### 7.3 Output Example

```
📊 Query Index Analysis Results
─────────────────────────────────

Recommended index #1:
  CREATE NONCLUSTERED INDEX IX_Orders_OrderDate
  ON dbo.Orders (OrderDate)
  INCLUDE (CustomerID, TotalAmount);

  Reason: The WHERE clause filters on OrderDate, and the
  SELECT list includes CustomerID and TotalAmount. A covering
  index would eliminate key lookups.

Recommended index #2:
  CREATE NONCLUSTERED INDEX IX_Customers_CustomerID
  ON dbo.Customers (CustomerID)
  INCLUDE (CustomerName, Email);

  Reason: The JOIN condition references CustomerID and the
  SELECT list pulls CustomerName. A covering index avoids
  table scans.
```

---

## 8. Optimize Query

Submit a SQL query to the AI for performance optimization suggestions.

### 8.1 How It Works

1. User selects a SQL query in the editor
2. Triggers "Optimize Query" from the Prompt AI window or menu
3. AI analyzes the query + schema + available indexes + SQL Server version
4. Returns an optimized version of the query with explanations

### 8.2 Optimization Types

| Type | Example |
|------|---------|
| **Rewrite subquery as JOIN** | Correlated subquery → INNER JOIN |
| **Replace IN with EXISTS** | `WHERE col IN (SELECT...)` → `WHERE EXISTS (...)` |
| **Remove unnecessary DISTINCT** | When results are already unique |
| **Add schema qualification** | `Orders` → `dbo.Orders` for plan caching |
| **Suggest SET NOCOUNT ON** | For stored procedures |
| **Replace cursors** | Cursor logic → set-based alternatives |
| **Simplify CASE expressions** | Redundant CASE → IIF or COALESCE |

### 8.3 UI Flow

```
   INPUT:
   ┌────────────────────────────────────────────┐
   │  SELECT * FROM Orders                      │
   │  WHERE CustomerID IN (                     │
   │    SELECT CustomerID FROM Customers         │
   │    WHERE Country = 'Germany'                │
   │  )                                         │
   └────────────────────────────────────────────┘
                     ↓ [Optimize]
   OUTPUT:
   ┌────────────────────────────────────────────┐
   │  SELECT o.OrderID,                         │
   │         o.OrderDate,                       │
   │         o.TotalAmount                      │
   │  FROM dbo.Orders o                         │
   │  WHERE EXISTS (                            │
   │      SELECT 1                              │
   │      FROM dbo.Customers c                  │
   │      WHERE c.CustomerID = o.CustomerID     │
   │          AND c.Country = 'Germany'          │
   │  )                                         │
   │  ORDER BY o.OrderDate DESC;                │
   │                                            │
   │  Changes made:                             │
   │  • Replaced SELECT * with explicit columns │
   │  • Added schema qualification (dbo.)       │
   │  • Replaced IN with EXISTS for better      │
   │    performance with large result sets       │
   │  • Added ORDER BY for predictable results  │
   └────────────────────────────────────────────┘
```

---

## 9. AI Settings & Configuration

All AI settings are in **SQL Prompt menu → Options → Prompt AI** (or under the AI section).

### 9.1 Complete Settings Table

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| **Enable SQL Prompt AI features** | `Bool` | **Off** (new installs) | Master toggle for all AI. Shortcut: `Ctrl+Shift+B` |
| **Enable AI code completion** | `Bool` | **Off** | Ghost text inline predictions (experimental/preview) |
| **AI completion auto-trigger** | `Bool` | **Off** | Show ghost text automatically vs manual only |
| **Auto-trigger delay** | `Number (ms)` | **500** | Wait time before auto-triggering completion |
| **Show fix suggestions popup** | `Bool` | **Off** | Auto-popup fix dialog when errors detected |
| **Send schema with requests** | `Bool` | **On** | Include database schema in AI requests for accuracy |
| **Include SQL Server version** | `Bool` | **On** | Send version info (e.g., "SQL Server 2022") for compatibility |

### 9.2 Settings UI Layout

```
┌──────────────────────────────────────────────────────────────┐
│  SQL Prompt Options                                          │
│                                                              │
│  ├─ Main                                                     │
│  │  ├─ Behavior                                              │
│  │  ├─ Database                                              │
│  │  └─ Editors                                               │
│  ├─ Format                                                   │
│  │  └─ Style                                                 │
│  ├─ Tabs                                                     │
│  │  ├─ Color                                                 │
│  │  └─ History                                               │
│  ├─ Code Analysis                                            │
│  ├─ Snippets                                                 │
│  ├─ Query Results                                            │
│  └─ 🤖 Prompt AI    ◀── AI settings page                    │
│     ├─ [✓] Enable SQL Prompt AI features                     │
│     ├─ [  ] Enable AI code completion (Preview)              │
│     ├─ [  ] Show AI completion automatically                 │
│     │       Delay: [500] ms                                  │
│     ├─ [  ] Show fix suggestions popup dialog                │
│     ├─ [✓] Send schema with AI requests                      │
│     └─ [✓] Include SQL Server version                        │
│                                                              │
│  [Import] [Export] [Reset This Page] [Reset All] [OK] [Cancel]│
└──────────────────────────────────────────────────────────────┘
```

### 9.3 Menu Structure

The SQL Prompt menu in SSMS has these AI-related entries:

```
SQL Prompt menu:
  ├─ Enable Suggestions
  ├─ Enable Code Analysis
  ├─ ─────────────────────
  ├─ 🤖 Enable Prompt AI Features     (Ctrl+Shift+B)
  ├─ ─────────────────────
  ├─ AI:
  │   ├─ Open Prompt AI Window        (Alt+Z)
  │   ├─ Query Index Analysis
  │   ├─ Explain SQL
  │   ├─ Fix SQL
  │   └─ Optimize Query
  ├─ ─────────────────────
  ├─ Format SQL
  ├─ Edit Formatting Styles
  └─ Options...
```

---

## 10. AI UI Color & Theme System

### 10.1 Prompt AI Window Colors

| Element | Light Theme | Dark Theme |
|---------|-------------|------------|
| **Window background** | `#FFFFFF` | `#1E1E2E` |
| **Input field background** | `#F5F5F5` | `#252836` |
| **Input field border** | `#CCCCCC` | `#3A3F4E` |
| **Input placeholder text** | `#999999` | `#5C6370` |
| **Response text** | `#333333` | `#D4D4D4` |
| **SQL syntax in response** | Standard syntax highlighting colors (matches editor) | Dark theme syntax colors |
| **Error text** | `#E74C3C` | `#FF5C5C` |
| **Warning text** | `#F39C12` | `#FF9F43` |
| **Success text** | `#2ECC71` | `#3DD68C` |
| **Follow-up suggestion** | `#4F8CFF` link color | `#4F8CFF` link color |
| **Loading spinner** | `#4F8CFF` animated | `#4F8CFF` animated |

### 10.2 AI Code Completion (Ghost Text) Colors

| Theme | Ghost Text Color | Normal Text Color | Contrast |
|-------|-----------------|-------------------|----------|
| Light | `#C0C0C0` (light gray) | `#000000` (black) | Dimmed ~50% |
| Dark | `#5C6370` (dim gray) | `#D4D4D4` (light gray) | Dimmed ~60% |

**Design principle:** Ghost text should be clearly visible but obviously not "real" code. It should be significantly dimmer than normal editor text, with no background color change — just color opacity difference.

### 10.3 AI Status Indicators

| State | Icon/Indicator | Color |
|-------|---------------|-------|
| AI Enabled | Solid icon in menu | Standard UI color |
| AI Disabled | Grayed-out icon | `#808080` |
| AI Processing | Spinning loader | `#4F8CFF` (blue) |
| AI Error | Error icon | `#FF5C5C` (red) |
| AI Warning | Warning triangle | `#FF9F43` (orange) |
| Schema unavailable | Warning banner in Prompt AI window | `#FF9F43` background |

### 10.4 Version Badge in Prompt AI Window

The connected database name and SQL Server version appear as a gray suffix in the Prompt AI window header area.

```
Connected: AdventureWorks  SQL Server 2022
           ^^^^^^^^^^^^^^  ^^^^^^^^^^^^^^^^
           White/standard  Gray suffix (#8892A8)
```

---

## 11. AI Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Alt+Z` | Open / focus the Prompt AI window |
| `Ctrl+Shift+B` | Toggle all AI features on/off |
| `Ctrl+Alt+Up Arrow` | Manually trigger AI code completion (ghost text) |
| `Tab` | Accept AI code completion ghost text |
| `Esc` | Dismiss AI code completion ghost text |
| `F1` (in Prompt AI window) | Open AI documentation in browser |
| `Enter` (in Prompt AI input) | Submit natural language request |
| `Ctrl+A` (in Prompt AI response) | Select all response text |
| `Ctrl+C` (in Prompt AI response) | Copy selected response text |

---

## Summary: AI Feature Comparison Table

| Feature | Input | Output | Engine | Requires Internet |
|---------|-------|--------|--------|:-----------------:|
| **NL → SQL** | Natural language text | T-SQL code | Cloud AI (Generative) | ✅ Yes |
| **Explain SQL** | Selected SQL code | Natural language text | Cloud AI (Generative) | ✅ Yes |
| **AI Fix** | Broken SQL code | Corrected SQL code | Cloud AI (Generative) | ✅ Yes |
| **AI Code Completion** | Current code context | Ghost text predictions | Cloud AI (Generative) | ✅ Yes |
| **Query Index Analysis** | SELECT query | CREATE INDEX DDL | Local ML Model | ❌ No |
| **Optimize Query** | Selected SQL code | Optimized SQL + explanation | Cloud AI (Generative) | ✅ Yes |

---

*Document compiled for AKML SQL gap analysis. Source: Redgate SQL Prompt documentation, University courses, product pages, release notes, and Prompt AI documentation.*
