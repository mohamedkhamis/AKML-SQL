# Contract: PostgreSQL code analysis rule set (initial release)

Rule ids are `PG` + category code + three digits. Severities: Error, Warning, Info, Hint
(`DiagnosticSeverity`). "Schema" marks `RequiresSchema`. Every rule's `ReferenceUrl` points at the
PostgreSQL 18 manual section named in the last column, and its `Description` states the
PostgreSQL behaviour it rests on (FR-150). 52 rules; the floor is 40 (FR-150, SC-016).

## Performance (PGPE)

| Id | Finding | Default | Schema | Doc section |
| --- | --- | --- | --- | --- |
| PGPE001 | `SELECT *` in a query that is not `EXISTS` / `COUNT(*)` | Warning | | sql-select |
| PGPE002 | `LIKE` / `ILIKE` pattern starting with `%` (no index use) | Warning | | indexes-types / functions-matching |
| PGPE003 | Function applied to a column in a `WHERE` / `JOIN ... ON` predicate (not sargable) | Warning | | indexes-expressional |
| PGPE004 | `NOT IN (subquery)` where the subquery column may be NULL | Warning | Schema | functions-subquery |
| PGPE005 | Column compared with a literal of another type category (implicit cast) | Info | Schema | typeconv-oper |
| PGPE006 | `ORDER BY` inside a subquery or CTE without `LIMIT` / `OFFSET` / `FETCH` | Info | | sql-select |
| PGPE007 | `COUNT(*) > 0` / `= 0` used as an existence test (use `EXISTS`) | Info | | functions-subquery |
| PGPE008 | Scalar correlated subquery in the select list | Hint | | sql-select |
| PGPE009 | `IN` list with more than 100 literals (use `= ANY(array)` / `VALUES`) | Info | | functions-comparisons |
| PGPE010 | Cross join: comma-separated tables with no join predicate, or `CROSS JOIN` | Warning | | queries-table-expressions |

## Best practices (PGBP)

| Id | Finding | Default | Schema | Doc section |
| --- | --- | --- | --- | --- |
| PGBP001 | `INSERT` without an explicit column list | Warning | | sql-insert |
| PGBP002 | `UPDATE` / `DELETE` without `WHERE` | Error | | sql-update / sql-delete |
| PGBP003 | Comma-separated table list (old-style join) | Info | | queries-table-expressions |
| PGBP004 | More than one table in `FROM` without aliases | Info | | queries-table-expressions |
| PGBP005 | Unqualified column that exists in more than one referenced table | Warning | Schema | queries-table-expressions |
| PGBP006 | `SELECT ... INTO table` (use `CREATE TABLE AS`) | Info | | sql-selectinto |
| PGBP007 | `BEGIN` / `START TRANSACTION` without a matching `COMMIT` / `ROLLBACK` in the script | Warning | | sql-begin |
| PGBP008 | `SET search_path` changes the meaning of later unqualified names in the script | Info | | ddl-schemas |
| PGBP009 | `NATURAL JOIN` (breaks when columns are added) | Warning | | queries-table-expressions |
| PGBP010 | CTE defined but never referenced | Info | | queries-with |

## Security (PGSE)

| Id | Finding | Default | Schema | Doc section |
| --- | --- | --- | --- | --- |
| PGSE001 | `EXECUTE` with a string built by concatenation (`\|\|`) instead of `format()` / `USING` (PL/pgSQL bodies) | Warning | | plpgsql-statements-executing-dyn |
| PGSE002 | `GRANT ALL` or `GRANT ... TO PUBLIC` | Warning | | sql-grant |
| PGSE003 | Password literal in `CREATE ROLE` / `ALTER ROLE` / `CREATE USER` | Error | | sql-createrole |
| PGSE004 | `SECURITY DEFINER` function without `SET search_path` | Warning | | sql-createfunction |
| PGSE005 | Filesystem access: `COPY ... FROM/TO PROGRAM`, `pg_read_file`, `pg_ls_dir`, `lo_import`, `lo_export` | Warning | | sql-copy / functions-admin |

## Style (PGST)

| Id | Finding | Default | Schema | Doc section |
| --- | --- | --- | --- | --- |
| PGST001 | Keyword casing inconsistent within the script (mixed upper and lower) | Hint | | sql-syntax-lexical |
| PGST002 | Last statement has no terminating semicolon | Hint | | sql-syntax |
| PGST003 | `!=` used (PostgreSQL converts it to `<>`) | Hint | | functions-comparison |
| PGST004 | Column alias without `AS` | Hint | | queries-select-lists |
| PGST005 | Line longer than 200 characters | Hint | | — (product convention) |

## Design (PGDE)

| Id | Finding | Default | Schema | Doc section |
| --- | --- | --- | --- | --- |
| PGDE001 | `timestamp` without time zone (prefer `timestamptz`) | Warning | | datatype-datetime |
| PGDE002 | `char(n)` column type | Info | | datatype-character |
| PGDE003 | `money` column type | Warning | | datatype-money |
| PGDE004 | `serial` / `bigserial` (prefer identity columns) | Info | | datatype-numeric / sql-createtable |
| PGDE005 | `CREATE TABLE` without a primary key | Warning | | ddl-constraints |
| PGDE006 | Foreign key without an index on the referencing columns | Warning | Schema | ddl-constraints |
| PGDE007 | `json` column type (prefer `jsonb`) | Info | | datatype-json |
| PGDE008 | `timetz` column type | Info | | datatype-datetime |

## Deprecated (PGDEP)

| Id | Finding | Default | Schema | Doc section |
| --- | --- | --- | --- | --- |
| PGDEP001 | `WITH OIDS` / `default_with_oids` (removed in 12) | Error | | sql-createtable |
| PGDEP002 | Named argument with `:=` (use `=>`) | Info | | sql-syntax-calling-funcs |
| PGDEP003 | `abstime` / `reltime` / `tinterval` types (removed in 12) | Error | | datatype |
| PGDEP004 | Table inheritance used for partitioning (use declarative partitioning) | Info | | ddl-partitioning |

## Execution (PGEX)

| Id | Finding | Default | Schema | Doc section |
| --- | --- | --- | --- | --- |
| PGEX001 | Statement that cannot run inside a transaction block between `BEGIN` and `COMMIT` (`CREATE INDEX CONCURRENTLY`, `REINDEX CONCURRENTLY`, `VACUUM`, `CREATE` / `DROP DATABASE`, `CREATE` / `DROP TABLESPACE`, `ALTER SYSTEM`, `CLUSTER` without table, `DISCARD ALL`) | Error | | sql-createindex et al. |
| PGEX002 | `DROP` / `TRUNCATE` outside an explicit transaction | Hint | | sql-drop / sql-truncate |
| PGEX003 | `SET LOCAL` outside a transaction block (no effect) | Warning | | sql-set |
| PGEX004 | psql meta-command (`\d`, `\c`, `\gexec`, ...) in the script | Warning | | app-psql |
| PGEX005 | `COPY ... FROM STDIN` / `TO STDOUT` (not supported in the workspace) | Warning | | sql-copy |

## Naming (PGNM)

| Id | Finding | Default | Schema | Doc section |
| --- | --- | --- | --- | --- |
| PGNM001 | Identifier longer than 63 bytes (silently truncated) | Warning | | sql-syntax-identifiers |
| PGNM002 | Unquoted identifier with upper-case letters (folded to lower case) | Info | | sql-syntax-identifiers |
| PGNM003 | Reserved keyword used as an identifier | Warning | | sql-keywords-appendix |
| PGNM004 | Same name used quoted and unquoted in one script (different identifiers) | Warning | | sql-syntax-identifiers |
| PGNM005 | Identifier that needs quoting everywhere (spaces, punctuation, leading digit) | Info | | sql-syntax-identifiers |

## Mechanics shared with the SQL Server rules

- Enabled / severity overrides: `config.json codeAnalysis.ruleOverrides` and `.casettings`, keyed
  by rule id.
- Suppressions: `-- akml-disable PGxx`, `-- akml-enable`, `-- akml-disable-line`, session
  suppressions, "everywhere" overrides; the legacy `-- noqa` forms.
- Rules window: grouped by kind (`AnalysisRuleInfoDto.Dialect`); SQL Server rules never run on
  PostgreSQL text and vice versa (FR-152).
- Tests: one positive and one negative script per rule in `tests/AkmlSql.PostgreSql.Tests/Rules/`;
  the format corpus must produce 0 crashes and 0 SQL Server findings (SC-016).
