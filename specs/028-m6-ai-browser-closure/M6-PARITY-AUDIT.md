# M6 Parity & Privacy Audit — AI in the Browser

**Spec 028 (M6) tasks T043 (parity) + T041 (privacy capture) + T044 (DoD reconciliation).**

This is the checked-in record comparing the browser AI surface against the WPF surface, the
privacy network-capture evidence, and the M6 Definition-of-Done reconciliation. Screenshot and
network-capture evidence require an **interactive workstation running both surfaces** (the same
constraint as the spec 024/025/027 audits) and is captured during the verification pass.

## Method

- Web edition (`AkmlSql.Web`) and the WPF surface side-by-side at the same OS theme + DPI.
- For each surface: paired screenshots; deltas recorded with a disposition (closed / accepted-with-reason).
- Privacy: DevTools Network panel open; one capture per privacy mode per feature.

## Feature-parity surfaces

| Surface | Web edition | WPF surface | Delta | Disposition |
|---|---|---|---|---|
| Action panel (Explain/Fix/Optimize/NL→SQL/Index Analysis) | `AiPanel.razor` (5 actions, streamed, per-action privacy badge) | AI menu + diff preview | _screenshot pending_ | — |
| Chat | `AiChatPanel.razor` (streamed, persisted, export) | dockable chat panel | _screenshot pending_ | — |
| Settings | `SettingsAi.razor` (providers, privacy modes, ghost-text, CORS notices) | Options → AI | _screenshot pending_ | — |
| Privacy-mode indicator | `AiPrivacyModeBadge` next to each control | mode shown per feature | _screenshot pending_ | — |
| Ghost Text | CM6 grey-text decorator (Tab accept / Esc dismiss) | WPF `GhostTextAdornment` | _screenshot pending_ | — |

> Target: ≤ 3 deltas open after the interactive pass (SC-009). Known by-design deltas: web AI is
> browser-direct (no engine log); OpenAI/Azure are not offered browser-direct (CORS).

## Privacy network-capture audit (FR-036 / SC-003 / T041)

Per privacy mode, the outbound provider request is captured and the disclosed schema asserted.
The **service-boundary equivalent is already automated** in `PrivacyModeTests` (full = columns;
names-only = names without types/FKs/descriptions; no-schema = empty). The network capture
confirms the same on the wire and that **no request targets an AKML-owned host**.

| Mode | Expected outbound schema | Wire capture |
|---|---|---|
| Full schema | tables + columns (+ types/FKs/descriptions) | _capture pending_ |
| Schema names only | table + column names; no types/FKs/descriptions | _capture pending_ |
| No schema | none (SQL/prompt only) | _capture pending_ |
| (any) | request origin = provider only; **no AKML host** | _capture pending_ |

Automated coverage today: `tests/AkmlSql.Web.Tests/Ai/PrivacyModeTests.cs` (per-mode disclosure),
`AnthropicWireTests` + `StreamingParserTests` (wire shapes), `AllowListTests` (origin allow-list).

## Definition-of-Done reconciliation (PRD §12 / FR-039 / T044)

| PRD §12 DoD item | Status | Evidence |
|---|---|---|
| `AkmlSql.AI` library exists; engine + browser consume it | ✅ Shipped | spec 021 T121–T124 (net10.0) |
| All 7 features work in the browser | ✅ (Ghost Text visual = interactive) | Explain/Fix/Optimize/NL→SQL (US1), Index Analysis (US4/T026–T027), Chat (US2/US6), Ghost Text (US5/T028–T033) |
| All 5 providers work | ⚠ **Revised** | Claude (US3/T020), Gemini, Ollama, LM Studio work browser-direct; **OpenAI/Azure CORS-blocked → documented-out** (FR-013 / Reconciliation 3) |
| All 4 privacy modes work | ✅ | US1 / FR-001 / `PrivacyModeTests` |
| Key storage uses Web Crypto; passphrase-protected | ⚠ **Revised** | Web Crypto non-extractable key (shipped), **no passphrase** (FR-002 / Reconciliation 1) |
| Ghost Text: debounce + cache + cancellation | ✅ (visual = interactive) | US5 / `GhostTextControllerTests` |
| Privacy mode audit: captures confirm no leakage | 🔶 Automated at service boundary; **wire capture pending** | `PrivacyModeTests`; this doc (T041) |
| Local provider (Ollama) documented with CORS config | ✅ | `doc/WEB/ai-local-provider-cors.md` (T024) |
| Privacy commitment doc written | ✅ | `doc/WEB/ai-privacy-commitment.md` (T042) |
| Feature parity audit screenshots | 🔶 **Interactive pending** | this doc (T043) |
| Branch merged to master via PR | ⏳ user action | branch `028-m6-ai-browser-closure` |
| Web edition feature-complete for the local-edition track | 🔶 on close of the interactive items | — |

**Legend:** ✅ done · ⚠ done-but-revised-with-reason · 🔶 implemented, interactive evidence pending · ⏳ user action.
