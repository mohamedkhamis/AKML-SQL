# Contract: Parity baseline file format

**Owner**: User Story 2 / User Story 3 (FR-006–FR-013)
**Location**: `tests/format-parity/baselines/<profile>/<script-id>.expected.sql` and `tests/format-parity/baselines/default/<script-id>.expected.json`.

Two file types, one shape each. Both carry a **baseline-revision** stamp so the parity test refuses to compare against a baseline produced by a different pipeline revision. The revision catches drift between the on-disk baseline and the current desktop pipeline (which is what runs in WASM via the web edition). It is NOT a cross-product IDE-plugin version — the IDE plugin's formatter / analyser output never enters this loop.

---

## Formatter baseline: `*.expected.sql`

Plain UTF-8 text. The baseline revision is embedded as a leading SQL comment line. Everything after the marker line up to EOF is the formatted SQL, byte-exact.

```sql
-- akml-parity-baseline revision=1.26.0526.0000 corpus-item=01-select profile=default
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

**Marker line**: exactly `-- akml-parity-baseline revision=<rev> corpus-item=<id> profile=<profile>` followed by `\n`. Anything else on that line invalidates the baseline.

**Body**: the formatted SQL the IDE plugin produced for the input. Trailing newline at EOF. No BOM. LF line endings on all platforms (the parity test normalises web-edition output to LF before comparing, per spec 020 SC-007 / Q1).

---

## Analyser baseline: `*.expected.json`

UTF-8 JSON. The baseline revision is the first top-level property. Findings are sorted by `(line, column, ruleId)`.

```json
{
  "akmlParityBaseline": {
    "revision": "1.26.0526.0000",
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

**`akmlParityBaseline`**: required object with `revision`, `corpusItem`, `profile`. Any missing field invalidates the baseline.

**`findings`**: required array, can be empty (clean script). Sorted ascending by `line`, then `column`, then `ruleId` (lexical). The parity test normalises the web edition's output to the same sort order before comparing.

**Each finding**: required fields `ruleId` (string, matches `[A-Z]{2,4}\d{3,4}` per the existing analyser convention), `severity` (`Error` | `Warning` | `Info`), `message` (string), `line` (1-based int), `column` (1-based int). Any extra field is rejected to keep baselines stable.

---

## Mismatch detection

The parity test class `ParityCorpusLoader` reads the marker line / JSON header and throws if the baseline revision stamped in the baseline does not equal `ParityCorpusLoader.CurrentBaselineRevision`. The current revision is read from a checked-in `tests/format-parity/baseline-revision.txt` file. Both the baseline files and the revision file are git-tracked; PR review catches drift before merge.

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
- [ ] `tests/format-parity/baseline-revision.txt` exists and matches every baseline's `revision=` / `revision` value
- [ ] Findings arrays are sorted as specified
- [ ] No baseline file has a trailing whitespace per line or BOM
