# M6 Parity & Privacy Audit — AI in the Browser

**Spec 028 (M6) tasks T043 (parity) + T041 (privacy capture) + T044 (DoD reconciliation).**

This is the checked-in record comparing the browser AI surface against the WPF surface, the
privacy network-capture evidence, and the M6 Definition-of-Done reconciliation.

> **2026-06-03 interactive pass (web half) completed.** The web edition was run (`dotnet run`)
> and driven with a real browser against a local mock AI provider. Per-mode wire disclosure,
> no-AKML-host, key-never-plaintext, streaming, chat persistence, and ghost text were all
> captured live — see [`SC-009-EVIDENCE/`](./SC-009-EVIDENCE/README.md). The **WPF half** of the
> screenshot comparison still requires an SSMS/VS host (same constraint as the 024/025/027
> audits) and is the only interactive item left open.

> **⚠ Prerequisite bug found + fixed during the pass.** The AI **action panel** and **chat
> panel** (built under 021 T131 / 028) were wired into **no reachable page** — `Editor.razor` had
> no AI affordance and there was no `/ai` or `/chat` route, so Explain/Fix/Optimize/NL→SQL/Index
> Analysis and Chat were unreachable by a user despite the DoD claiming "all 7 features work in
> the browser." 65 green unit tests + bUnit (render-in-isolation) structurally could not catch it;
> running the product did. **Fix:** an editor-adjacent collapsible **AI dock** (`AI ▾` toolbar
> toggle → `[Actions] [Chat]` tabs; actions operate on the live selection, Accept inserts at the
> caret). The AI unit suite stays 65/65 green (the panel API is unchanged; a new optional
> `SelectedSqlProvider` defaults to the existing `SelectedSql` path).

## Method

- Web edition (`AkmlSql.Web`) and the WPF surface side-by-side at the same OS theme + DPI.
- For each surface: paired screenshots; deltas recorded with a disposition (closed / accepted-with-reason).
- Privacy: DevTools Network panel open; one capture per privacy mode per feature.

## Feature-parity surfaces

| Surface | Web edition | WPF surface | Web evidence | Disposition |
|---|---|---|---|---|
| Action panel (Explain/Fix/Optimize/NL→SQL/Index Analysis) | `AiPanel.razor` in the editor AI dock (5 actions, streamed, per-action privacy badge) | AI menu + diff preview | `m6-ai-actions-full-schema.png`, `m6-ai-actions-no-schema.png` | Web ✅ captured · WPF screenshot open |
| Chat | `AiChatPanel.razor` in the dock (streamed, persisted, export) | dockable chat panel | `m6-ai-chat.png` | Web ✅ captured · WPF screenshot open |
| Settings | `SettingsAi.razor` (providers, privacy modes, ghost-text, CORS notices) | Options → AI | (live-driven; provider add + mode switch + ghost toggle all exercised) | Web ✅ exercised · WPF screenshot open |
| Privacy-mode indicator | `AiPrivacyModeBadge` next to each control | mode shown per feature | badges visible in `m6-ai-actions-*` (Full schema / No schema, with tooltips) | Web ✅ captured · WPF screenshot open |
| Ghost Text | CM6 grey-text decorator (Tab accept / Esc dismiss) | WPF `GhostTextAdornment` | `m6-ghost-text.png` (grey widget + Tab-accept verified) | Web ✅ captured · WPF screenshot open |

> Target: ≤ 3 deltas open after the interactive pass (SC-009). **Open deltas: 1** — the WPF-half
> screenshots (no SSMS/VS host in the verification environment). Known by-design deltas: web AI is
> browser-direct (no engine log); OpenAI/Azure are not offered browser-direct (CORS).

## Privacy network-capture audit (FR-036 / SC-003 / T041)

Per privacy mode, the outbound provider request is captured and the disclosed schema asserted.
The **service-boundary equivalent is already automated** in `PrivacyModeTests` (full = columns;
names-only = names without types/FKs/descriptions; no-schema = empty). The network capture
confirms the same on the wire and that **no request targets an AKML-owned host**.

| Mode | Expected outbound schema | Wire capture (2026-06-03) |
|---|---|---|
| Full schema | tables + columns (+ types/FKs/descriptions) | ✅ captured — `dbo.Orders(... nvarchar(100) ... decimal(18,2)) FK->dbo.Customers` + Desc |
| Schema names only | table + column names; no types/FKs/descriptions | ✅ captured — `dbo.Orders (OrderId, CustomerId, Notes, Total)`; no types/FK/desc |
| No schema | none (SQL/prompt only) | ✅ captured — schema block empty; 9-identifier leak-check all absent |
| (any) | request origin = provider only; **no AKML host** | ✅ captured — only `localhost:11434`; no anthropic/openai/azure/AKML host; no `Authorization` header |

Full transcripts + the key-never-plaintext scan: [`SC-009-EVIDENCE/README.md`](./SC-009-EVIDENCE/README.md).
Automated coverage: `tests/AkmlSql.Web.Tests/Ai/PrivacyModeTests.cs` (per-mode disclosure),
`AnthropicWireTests` + `StreamingParserTests` (wire shapes), `AllowListTests` (origin allow-list).

## Definition-of-Done reconciliation (PRD §12 / FR-039 / T044)

| PRD §12 DoD item | Status | Evidence |
|---|---|---|
| `AkmlSql.AI` library exists; engine + browser consume it | ✅ Shipped | spec 021 T121–T124 (net10.0) |
| All 7 features work in the browser | ✅ **now reachable** | Explain/Fix/Optimize/NL→SQL (US1), Index Analysis (US4), Chat (US2/US6), Ghost Text (US5) — all surfaced in the editor **AI dock** (the panels were orphaned before this pass; see top note). Live-verified 2026-06-03. |
| All 5 providers work | ⚠ **Revised** | Claude (US3/T020), Gemini, Ollama, LM Studio work browser-direct; **OpenAI/Azure CORS-blocked → documented-out** (FR-013 / Reconciliation 3) |
| All 4 privacy modes work | ✅ | US1 / FR-001 / `PrivacyModeTests` |
| Key storage uses Web Crypto; passphrase-protected | ⚠ **Revised** | Web Crypto non-extractable key (shipped), **no passphrase** (FR-002 / Reconciliation 1) |
| Ghost Text: debounce + cache + cancellation | ✅ | US5 / `GhostTextControllerTests` + live: grey widget, Tab-accept, **cache-hit 50 % ≥ 30 %** (SC-006), `stream:false max_tokens:150 temp:0.2` (`SC-009-EVIDENCE`) |
| Privacy mode audit: captures confirm no leakage | ✅ **wire-captured** | `PrivacyModeTests` + live per-mode wire capture + no-AKML-host (`SC-009-EVIDENCE`, T041) |
| Local provider (Ollama) documented with CORS config | ✅ | `doc/WEB/ai-local-provider-cors.md` (T024) |
| Privacy commitment doc written | ✅ | `doc/WEB/ai-privacy-commitment.md` (T042) |
| Feature parity audit screenshots | 🔶 Web ✅ / WPF pending | web screenshots in `SC-009-EVIDENCE/` (T043); WPF-half needs an SSMS/VS host |
| Branch merged to master via PR | ⏳ user action | branch `028-m6-ai-browser-closure` |
| Web edition feature-complete for the local-edition track | ✅ (1 cosmetic delta: WPF-half screenshots) | all 7 features reachable + live-verified; privacy proven on the wire |

**Legend:** ✅ done · ⚠ done-but-revised-with-reason · 🔶 implemented, interactive evidence pending · ⏳ user action.
