# Format Parity Corpus

Test corpus + golden outputs driving the regression / parity test suite for AKML SQL's formatter.

## Two roles, one driver

The test driver in [tests/AkmlSql.Formatting.Tests/Parity/FormatParityTests.cs](../AkmlSql.Formatting.Tests/Parity/FormatParityTests.cs) compares the formatter's output to the files in `golden/`. Today the goldens are **AKML's own captured output** — so the suite acts as a drift-guard catching any change that quietly alters formatter behaviour. When someone with a Redgate SQL Prompt installation generates Redgate-formatted goldens for the same corpus, the same driver becomes the **SC-007 parity measurement** (≥ 95 % match) without code change.

## Match definition (SC-007 / Q1 clarification)

For each `(corpus.sql × style.akmlstyle|.sqlpromptstylev2)` pair, the driver compares the formatter's output to the golden as follows:

1. Strip trailing whitespace per line
2. Normalise line endings to `\n` (LF)
3. Drop UTF-8 BOM if present
4. **Then require byte-exact equality**

A pair passes iff the two normalised byte streams are equal. The `Normalise` helper is idempotent so the golden is itself the normalised form.

## Layout

```text
tests/format-parity/
├── README.md         # This file
├── corpus/           # Representative .sql input files (13 today, expandable to ~200)
│   └── *.sql
└── golden/           # Captured output, one file per (corpus, style) pair
    └── <input-stem>__<style>.sql
```

## Capture vs compare modes

The driver mirrors the `PerformanceBaselineTests.Capture_or_compare_M0_baseline` pattern:

| Mode | Trigger | Behaviour |
|---|---|---|
| **Capture** | Golden file missing OR `AKML_UPDATE_PARITY_GOLDEN=1` env var | Writes the golden, asserts output is non-empty |
| **Compare** | Default | Byte-exact equality assertion |

To **regenerate all goldens** (e.g. after an intentional formatter change):

```bash
AKML_UPDATE_PARITY_GOLDEN=1 dotnet test \
    tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj \
    -c Release --filter "FullyQualifiedName~FormatParityTests"
```

Inspect the resulting diff in `golden/`. If the change is intentional, commit. If unexpected, you found a regression.

## Built-in styles exercised

The driver runs each input through every built-in `.akmlstyle` profile under [src/AkmlSql.Formatting/Profiles/BuiltIn/](../../src/AkmlSql.Formatting/Profiles/BuiltIn/):

- `default` — AKML's default settings
- `compact` — single-line preference (SELECT inline with first column)
- `indented` — SQL Prompt-style "Indented"
- `aligned-left-bracket` — leading-bracket alignment (AKML's best-effort match to SQL Prompt's variant)
- `leading-commas` — commas before each item rather than trailing
- `minimalist` — minimal whitespace / minimal line breaks

13 corpus files × 6 styles = **78 (input, style) pairs** exercised on every test run.

---

## Swap-in path: Redgate goldens

The drift-guard is useful by itself, but the spec's SC-007 goal is parity with Redgate SQL Prompt's formatter specifically. To upgrade this suite into the SC-007 parity measurement:

### 1. Acquire SQL Prompt CLI

SQL Prompt ships a command-line formatter as part of the SQL Toolbelt Essentials bundle. A 14-day free trial is sufficient.

- Install location: `C:\Program Files (x86)\Red Gate\SQL Prompt 10\SqlPrompt.Format.CommandLine.exe` (or the path for your installed version)
- Add to `PATH` for convenience.

### 2. Wire-format note: `.sqlpromptstyle` vs `.json`

The CLI accepts **`.json`** style files via `--style`, not the editor's native `.sqlpromptstylev2` XML. To convert an editor-saved style, open SQL Prompt's Edit Style dialog → Save As → JSON. The schemas overlap but aren't identical — verify the `.json` produces the same in-editor preview as the `.sqlpromptstylev2` you started from before treating the resulting goldens as authoritative.

### 3. Generate goldens

For each built-in style listed above (or each Redgate built-in if you prefer Redgate's variants):

```powershell
# Per style:
foreach ($input in Get-ChildItem tests\format-parity\corpus\*.sql) {
    Copy-Item $input.FullName "tests\format-parity\golden\$($input.BaseName)__<style>.sql"
}

SqlPrompt.Format.CommandLine.exe `
    --i-agree-to-the-eula `
    --style <path-to>.json `
    --path tests\format-parity\golden\
```

Two caveats:

- The CLI formats **in place**, so copy inputs into `golden/` first under the right naming convention (`<input-stem>__<style>.sql`).
- Use `--create-backups` if you want a safety net; delete the `.bak` files before checking in.

### 4. Update the driver normalisation if needed

The `Normalise` helper in `FormatParityTests.cs` was tuned for AKML's output. If Redgate's CLI emits content that needs additional normalisation (e.g. Redgate may emit `--` end-of-line comments slightly differently), update `Normalise` and document why in its `<summary>` block. **Be careful not to normalise away the difference you're measuring** — the whole point of SC-007 is to compare visible formatting.

### 5. Commit goldens, run tests in compare mode

```bash
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj \
    -c Release --filter "FullyQualifiedName~FormatParityTests"
```

Each failure is a divergence from Redgate. SC-007 says ≥ 95 % must pass; today's driver fails on the first mismatch (strict mode). To get the SC-007 ratio metric instead of a hard fail, replace the per-test `Assert.Equal` with a collector pattern (track passes / fails across all `[Theory]` data points, then assert the ratio in a `[Fact]` finalizer). The infrastructure to do this without changing the corpus / golden layout is in place.

### 6. Expand the corpus

The current 13-file corpus is a starter set covering the formatter's interesting paths. The spec's target is 200 files. Expand by adding `.sql` files to `corpus/` — the driver auto-discovers them via `Directory.EnumerateFiles`. Re-run with `AKML_UPDATE_PARITY_GOLDEN=1` to capture goldens for the new pairs.

---

## Status

- **Drift-guard**: live. 78 / 78 pairs pass in compare mode.
- **SC-007 parity measurement**: ready to wire up — needs goldens generated by Redgate's CLI per the swap-in path above.
