# `tests/AkmlSql.Core.Tests/Format/` — placeholder

This folder holds the format-related xunit suites added by spec 020:

| File (planned) | Purpose | Spec task |
|---|---|---|
| `SqlPromptStyleImporterTests.cs` | `.sqlpromptstyle` import — real-world success, unknown-key passthrough, unsupported-key surface, malformed JSON rejection, oversize rejection, path-traversal rejection | T039 |
| `SqlPromptStyleExporterTests.cs` | Export round-trip preserves unknown keys; built-in export matches `.sqlpromptstyle` schema; edits survive round-trip | T040 |
| `SqlPromptKeyMapTests.cs` | Every entry has a default; no duplicate `SqlPromptKey`; every `Implemented` key is reachable on `FormatProfile` via reflection | T041 |
| `BuiltInStyleSeederTests.cs` | Idempotent re-seed; user-modified read-only file is not overwritten | T042 |
| `ActiveProfileScopeTests.cs` | `ConfigManager.Load` reads a single `ActiveProfile`; value is unaffected by host identity (FR-027b regression guard) | T043 |
| `FormatParityTests.cs` | `[Theory]` over (corpus × style); applies the SC-007 normalisation before byte-comparison; suite passes if ≥ 95 % files pass | T073 |

Delete this placeholder once the first real test file lands.
