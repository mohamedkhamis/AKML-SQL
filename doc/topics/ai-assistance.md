# AI Assistance

AKML SQL can use an AI model of your choice to write, explain, fix, and tune SQL. Every AI suggestion is shown for review first — nothing is applied or executed without your confirmation.

## What it can do

- **Text-to-SQL** — describe the query in plain English and get schema-correct SQL to review and accept.
- **Explain** — a plain-English breakdown of the selected query: purpose, step-by-step logic, and performance notes.
- **Fix** — when a query fails, get a suggested correction as a diff, grounded in the actual schema.
- **Optimize** — suggestions for a slow query, split into safe one-click changes and changes to review.
- **Index Analysis** — index recommendations with ready-to-run `CREATE INDEX` scripts you can copy.
- **Chat** — a multi-turn conversation about your schema and queries, with apply buttons next to suggested code changes.
- **Ghost Text** — inline autocomplete suggestions that appear grayed-out as you type; accept them with a key press.

All of these are available from the AKML SQL menu and the editor right-click menu.

## Supported providers

- OpenAI
- Anthropic (Claude)
- Google Gemini
- Azure OpenAI
- Kimi (Moonshot)
- Ollama (local models)
- LM Studio (local models)

Configure your provider and API key in **Tools** -> **Options** -> **AKML SQL** under the AI section — the **Test connection** button there verifies the provider, model, endpoint and key before you save. Ollama and LM Studio run on your own machine, so no data leaves it. Kimi defaults to the international endpoint (`api.moonshot.ai`); for the mainland-China service, set the endpoint to `https://api.moonshot.cn/v1`.

## Where your API keys live

Keys are wrapped (encrypted) at rest with Windows DPAPI, scoped to your Windows user account. Inside `config.json` they appear only as encrypted `dpapi:` blobs — never plaintext — and they are never written to log files.

## Schema-aware prompting

When AKML SQL talks to the AI, it includes the relevant table and column names from your database schema, so generated SQL references real objects instead of guessed ones.

## Stay in control

- Generated SQL is shown in a preview before it is inserted.
- Fixes and optimizations appear as diffs you accept or reject.
- No AI-generated statement is ever executed automatically.

If the AI suggests an object that does not exist in your schema, AKML SQL flags it instead of applying it silently.

Related: [IntelliSense](intellisense.md), [Static Code Analysis](static-analysis.md).
