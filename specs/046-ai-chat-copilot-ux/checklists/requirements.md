# Specification Quality Checklist: AI Chat — Copilot-style experience in SSMS and the web edition

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-11-04
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Items marked incomplete require spec updates before `/speckit.clarify` or `/speckit.plan`
- Three clarifications were put to the owner on 2026-11-04 and answered the same day; the answers are written into the spec: FR-027 (whole answers in SSMS now, streaming as a follow-up spec), FR-043 (one conversation, New chat replaces it, no history list), FR-034 (the missing message was a confirmation after switching the model: an inline note naming the new agent with provider and model). No markers remain.
- The closing "Notes for planning (investigation …)" section names the local log file and two of the product's own log lines as evidence; that is diagnostic context for the owner, kept out of the requirements themselves. The functional requirements and success criteria contain no code, file or framework names.

## Requirement coverage after implementation (T066, 2026-11-04)

Every functional requirement maps to code and at least one automated test; the SSMS scenarios the
owner runs (tasks T021, T029, T030, T036, T045, T051) are the remaining acceptance evidence.

| FR | Where | Automated evidence | Owner check |
|----|-------|--------------------|-------------|
| FR-001 – FR-004 | `ChatBindingResolver` | `ChatBindingResolverTests`, `AiChatSessionBindingTests` | S1-1 … S1-7 |
| FR-005 | `AiChatPanel.LogRefusal` | `AiChatSessionBindingTests` (log sink) | S1-6, log check |
| FR-006 | `Decide` returns only supplied ids | `ChatBindingResolverTests.Only_supplied_session_ids_ever_appear` | — |
| FR-007 | `NoteBindingChange` | `AiChatSessionBindingTests` (three notice cases) | S1-4 |
| FR-008 | `AiEditorSqlInserter` path | `AiChatInsertSqlTests` | S2-2 |
| FR-010 – FR-013 | `UpdateSendState`, placeholders, tooltips | `AiChatComposerStateTests`, web `AiChatPanelComposerTests`, E2E `Send_names_ctrl_enter_on_hover` | S3-1 … S3-3 |
| FR-014 | working-state text | `AiChatComposerStateTests.The_working_state_names_the_agent_being_asked` | — |
| FR-020 – FR-021 | `ChatMarkdown`, `ChatMessageRenderer`, `ChatMessageView` | `ChatMarkdownTests`, `AiChatRenderingTests`, `ChatMessageViewTests`, E2E code-panel test | S2-1, S2-2 |
| FR-022 | `AiAgentLabel`, reply keys 7–8, `ChatTurn.AgentLabel` | `AiAgentLabelTests`, `AiChatResponseAgentFieldsTests`, picker and web agent tests | S4-2 |
| FR-023 | `AiChatWelcomeCard`, web starters | `AiChatWelcomeCardTests`, web composer tests | S5-3 |
| FR-024 | `ShouldFollow`, `isAtBottom` | composer-state theory, `akml-ui.scroll.test.mjs` | S2-3 |
| FR-025 | theme tokens only, automation names | review grep (no hex), name/tab-stop tests | S2-4 (T029) |
| FR-026 | raw copy and export | copy tests (both editions) | W6 |
| FR-027 | whole answers in SSMS (unchanged) | — | — |
| FR-030 – FR-033 | picker labels, composer bar, `AiAgentSelector` | `AiChatAgentPickerTests`, `AiAgentSelectorTests` | S4-1, S4-3, W1, W2 |
| FR-034 | `NoteAgentSwitch`, web notice line | picker switch-notice tests, `AiChatPanelAgentTests` | S4-2 |
| FR-040 – FR-043 | `NewConversation`, `NewChatAsync` | composer-state New chat tests, web New chat tests, E2E `NewChat_while_streaming…` | S5-1, S5-2 |

No gap found. Two refinements were recorded in the contracts during implementation: the rebind
notice compares with the last bound database and appears only once a conversation exists
(chat-binding.md Part 5), and the agent label drops the provider when it only repeats the name
("Ollama · llama3" for the offline fallback).
