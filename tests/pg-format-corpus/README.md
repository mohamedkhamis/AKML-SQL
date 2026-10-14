# PostgreSQL format corpus (spec 042)

Real-world PostgreSQL scripts and their formatted goldens, the PostgreSQL counterpart of
[`tests/format-parity`](../format-parity/README.md).

- **Runner**: `tests/AkmlSql.PostgreSql.Tests/Formatting/PgFormatCorpusTests.cs`
  (`[Trait("Category", "FormatCorpus")]`).
- **Inputs**: `corpus/NN-name.sql`, at least 100 scripts: examples from the PostgreSQL manual,
  Northwind-style queries, DDL, functions with dollar-quoted bodies, regression cases.
- **Goldens**: `golden/NN-name__<style>.sql`, one per built-in style (`default`, `compact`,
  `collapsed`, `indented`, `aligned-left-bracket`, and the Khamis styles).
- **Regenerating**: set `AKML_UPDATE_PG_GOLDENS=1` and run the corpus test. Review the diff by eye
  against the style definitions before keeping it; a golden is a drift guard, not an authority.

## Invariants asserted for every script and style

1. The formatted text parses and its parse tree equals the original's with all `location`,
   `stmt_location` and `stmt_len` fields cleared (the meaning did not change).
2. Formatting the formatted text changes nothing (idempotent).
3. Every quoted identifier and every dollar-quoted body appears verbatim in the output.
4. The output equals the golden byte for byte.

A script with a syntax error is returned unchanged with a message, and is covered separately in
`PgFormatterSafetyTests`.
