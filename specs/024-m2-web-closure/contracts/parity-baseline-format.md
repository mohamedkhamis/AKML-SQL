# Contract: Parity baseline file format

**Owner**: User Story 2 / User Story 3 (FR-006–FR-013)
**Location**: `tests/format-parity/baselines/<profile>/<script-id>.expected.sql` and `tests/format-parity/baselines/default/<script-id>.expected.json`.

Two file types, one shape each. Both carry an IDE-plugin build-version stamp so the parity test refuses to compare against a mismatched build.

---

## Formatter baseline: `*.expected.sql`

Plain UTF-8 text. The IDE-plugin build version is embedded as a leading SQL comment line. Everything after the marker line up to EOF is the formatted SQL, byte-exact.

```sql
-- akml-parity-baseline ide-build=1.26.0525.1538 corpus-item=01-select profile=default
SELECT
    o.OrderId,
    c.CustomerName,
    o.OrderDate
FROM Orders AS o
INNER JOIN Customers AS c
    ON o.CustomerId = c.CustomerId
WHERE o.OrderDate >= '2026-01-01'
ORDER BY o.OrderDate DESC;
```

**Marker line**: exactly `-- akml-parity-baseline ide-build=<version> corpus-item=<id> profile=<profile>` followed by `\n`. Anything else on that line invalidates the baseline.

**Body**: the formatted SQL the IDE plugin produced for the input. Trailing newline at EOF. No BOM. LF line endings on all platforms (the parity test normalises web-edition output to LF before comparing, per spec 020 SC-007 / Q1).

---

## Analyser baseline: `*.expected.json`

UTF-8 JSON. The IDE-plugin build version is the first top-level property. Findings are sorted by `(line, column, ruleId)`.

```json
{
  "akmlParityBaseline": {
    "ideBuild": "1.26.0525.1538",
    "corpusItem": "01-select",
    "profile": "default"
  },
  "findings": [
    {
      "ruleId": "PE001",
      "severity": "Warning",
      "message": "SELECT * — consider an explicit column list",
      "line": 1,
      "column": 8
    },
    {
      "ruleId": "BP012",
      "severity": "Info",
      "message": "Use explicit AS for table alias",
      "line": 4,
      "column": 6
    }
  ]
}
```

**`akmlParityBaseline`**: required object with `ideBuild`, `corpusItem`, `profile`. Any missing field invalidates the baseline.

**`findings`**: required array, can be empty (clean script). Sorted ascending by `line`, then `column`, then `ruleId` (lexical). The parity test normalises the web edition's output to the same sort order before comparing.

**Each finding**: required fields `ruleId` (string, matches `[A-Z]{2,4}\d{3,4}` per the existing analyser convention), `severity` (`Error` | `Warning` | `Info`), `message` (string), `line` (1-based int), `column` (1-based int). Any extra field is rejected to keep baselines stable.

---

## Mismatch detection

The parity test class `ParityCorpusLoader` reads the marker line / JSON header and throws an `xunit.SkipException` (or fails outright, depending on test runner config) if the IDE-plugin build version stamped in the baseline does not equal the value `ParityCorpusLoader.CurrentIdePluginVersion` returns. `CurrentIdePluginVersion` is itself read from a checked-in `tests/format-parity/ide-plugin-version.txt` file that the baseline generator writes whenever it runs. Both the baseline files and the version file are git-tracked; CI catches drift on PR.

---

## Generator output discipline

`ParityBaselineGenerator` (the opt-in `[Trait("Category","ParityBaseline")]` test class) MUST:

- Write every output as UTF-8 (no BOM), LF line endings, trailing newline.
- Emit the marker line / `akmlParityBaseline` block as the very first line / property — not after the SQL / findings.
- Sort the findings array deterministically per the rule above.
- Round-trip safe: running the generator twice with no code change produces byte-identical baseline files (so PR review sees zero noise from a regen).

---

## Validation checklist

- [ ] Every `<script-id>.expected.sql` under `baselines/<profile>/` has the marker line as the first line
- [ ] Every `*.expected.json` under `baselines/default/` has `akmlParityBaseline` and `findings` as the only top-level properties
- [ ] `tests/format-parity/ide-plugin-version.txt` exists and matches every baseline's `ide-build=` / `ideBuild` value
- [ ] Findings arrays are sorted as specified
- [ ] No baseline file has a trailing whitespace per line or BOM
