# Baseline: 037-multi-ai-agents

**Recorded**: 2026-09-07, on branch `037-multi-ai-agents` at commit `9642dd6` (clean tree, pre-implementation).

## Build (T001)

Full MSBuild, `Release`, one pass after `Restore`: **green, exit 0**.
Toolchain: `C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe`.

## Test pass counts (T002) — `dotnet test -c Release --no-build`

| Suite | Passed | Failed | Skipped | Total |
|---|---|---|---|---|
| AkmlSql.Core.Tests | 689 | **2 (pre-existing)** | 1 | 692 |
| AkmlSql.AI.Tests | 55 | 0 | 0 | 55 |
| AkmlSql.Engine.Tests | 1751 | 0 | 0 | 1751 |
| AkmlSql.Shell.Shared.Tests | 217 | 0 | 0 | 217 |

### Pre-existing failures (environmental, NOT this feature's regressions)

`AkmlSql.Core.Tests.Theme.VisualReferenceCoverageTests` — both tests fail because SQL Prompt
reference assets are missing on this machine:

- `AllReferenceMarkdownFilesExistAndAreNonEmpty` — "4 expected SQL Prompt reference markdown file(s) missing"
- `AllSvgMockupsExist`

Any *new* red in the suites above is this feature's regression. These two (and the one skipped
`HardcodedHexScannerTests.NoHardcodedChromeHex`) are the standing baseline state.

## Ratchets (must stay green — T104)

- Format-parity goldens: **977**
- Completion corpus: **1,342 / ~97.5%**
