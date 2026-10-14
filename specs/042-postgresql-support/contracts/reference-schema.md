# Contract: the PostgreSQL reference and its generator

## Files

| Path | Role |
| --- | --- |
| `tools/pg-reference/AkmlSql.PostgreSql.ReferenceGen/` | console tool (net10.0, not in the product build) |
| `src/AkmlSql.PostgreSql/Reference/postgresql-reference.json` | generated, embedded resource, loaded by `PgReference` |
| `src/AkmlSql.PostgreSql/Reference/omissions.json` | generated + hand-maintained reasons |
| `doc/postgresql-reference.md` | generated, human-readable, reviewed in PRs |
| `tools/pg-reference/README.md` | how to run; which PostgreSQL tags; how to review a diff |

## Inputs (a PostgreSQL source checkout per supported tag)

```text
pg-reference gen --source <path-to-postgres-git> --tags REL_14_STABLE,REL_15_STABLE,REL_16_STABLE,REL_17_STABLE,REL_18_STABLE --doc-version 18 --out <repo-root>
```

| Entry kind | Read from | Fields taken |
| --- | --- | --- |
| keyword | `src/include/parser/kwlist.h` (`PG_KEYWORD("name", TOKEN, CATEGORY, BARE_LABEL)`) | name, category: `RESERVED_KEYWORD` → reserved, `UNRESERVED_KEYWORD` → unreserved, `COL_NAME_KEYWORD` → column-name, `TYPE_FUNC_NAME_KEYWORD` → type-function-name |
| command | `doc/src/sgml/ref/*.sgml` | `<refname>` (name), `<refpurpose>` (summary), first `<synopsis>` (synopsis, entities expanded, whitespace normalised), page id (`<refentry id="sql-select">`) |
| function / aggregate / window | `src/include/catalog/pg_proc.dat` | `proname`, `prokind` (f / a / w; `p` → command-like "procedure" entries), `proargtypes` / `proallargtypes` / `proargmodes` / `proargnames`, `prorettype`, `proretset`, `provariadic`, `descr`; OIDs resolved through `pg_type.dat` (`typname`); section id by matching `<function>name(</function>` in `doc/src/sgml/func/*.sgml` tables; unmatched → `functions` |
| operator | `src/include/catalog/pg_operator.dat` | `oprname`, `oprleft`, `oprright`, `oprresult`, `descr`; section by the same table match or `functions-comparison` |
| type | `src/include/catalog/pg_type.dat` (`typtype` b, e, r, m, d with `typcategory`), aliases from the grammar's type names and `doc/src/sgml/datatype.sgml` tables (`<entry><type>integer</type></entry>`, aliases column) | name, aliases, category, summary (`descr`), section id |
| sqlstate | `src/backend/utils/errcodes.txt` | code, condition name, class |
| lexical | hand-curated list in the generator (`::`, `$n`, `$$`, `E''`, `U&''`, `--`, `/* */`, quoted identifiers, case folding) | name, summary, section id from `syntax.sgml` |

Exclusions written to `omissions.json`: functions whose `descr` starts with `internal` or
`implementation of` / `I/O`, aggregate transition functions, deprecated names listed in
`doc/src/sgml/release-*.sgml` removal notes, and anything the generator cannot resolve (listed
with reason `unresolved`, which the review must drive to zero or justify).

## Output schema (`postgresql-reference.json`)

```json
{
  "version": "18",
  "generatedFrom": { "tags": ["REL_14_STABLE", "REL_15_STABLE", "REL_16_STABLE", "REL_17_STABLE", "REL_18_STABLE"], "generator": "1.0.0" },
  "entries": [
    {
      "kind": "function",
      "name": "string_agg",
      "aliases": [],
      "category": "functions-aggregate",
      "summary": "concatenate aggregate input into a string",
      "synopsis": null,
      "signatures": [
        { "args": [ { "name": null, "type": "text", "mode": "in", "hasDefault": false },
                    { "name": null, "type": "text", "mode": "in", "hasDefault": false } ],
          "returns": "text", "isSetReturning": false },
        { "args": [ { "name": null, "type": "bytea", "mode": "in", "hasDefault": false },
                    { "name": null, "type": "bytea", "mode": "in", "hasDefault": false } ],
          "returns": "bytea", "isSetReturning": false }
      ],
      "minVersion": 14,
      "source": { "file": "src/include/catalog/pg_proc.dat", "sectionId": "functions-aggregate" },
      "url": "https://www.postgresql.org/docs/18/functions-aggregate.html"
    }
  ]
}
```

Invariants (asserted by `ReferenceGenTests` and by a load-time self-check):

- Names are unique per `kind` (functions with overloads merge into one entry with several
  signatures).
- Every entry has a non-empty `summary`, a `source.sectionId` and a `url` that follows the
  `docs/<version>/<sectionId>.html` pattern.
- `minVersion` is one of the supported majors.
- No entry name appears in the T-SQL `KeywordDictionary` / `BuiltinFunctionDictionary` with a
  T-SQL-only meaning that is absent from PostgreSQL (the cross-dialect check in SC-004 reads both
  sides).

## Human-readable output (`doc/postgresql-reference.md`)

One section per kind; tables of name, summary, first version, link. The file is the review
artefact for FR-051: a PR that changes the reference shows the diff here.

## Consumers

- `PgReference.Load()` (library) builds: keywords by upper-cased name with category; commands by
  first keyword(s) (two-word commands such as `CREATE TABLE` indexed by both words); functions /
  aggregates / window functions / operators / types by lower-cased name; SQLSTATE by code.
- Completion offers only entries with `minVersion ≤ server major` (FR-052).
- Quick info renders `summary`, `synopsis` or signatures, "since PostgreSQL N" when
  `minVersion > 14`, and `url` (FR-053).
