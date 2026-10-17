# Contract: PostgreSQL catalog queries (engine, `PgSchemaMetadataService`)

Conventions (mirroring the SQL Server rules in CLAUDE.md): no `ORDER BY` (sorted in memory);
direct joins to `pg_namespace`, never `::regclass` casts per row; one round trip per phase step;
system schemas (`pg_catalog`, `information_schema`, `pg_toast`, `pg_toast_temp_*`, `pg_temp_*`)
excluded unless the step says otherwise; all queries run on the session's persistent connection
under the existing exclusive gate.

## Connect-time facts

```sql
SELECT current_database(), current_user, version(),
       current_setting('server_version_num')::int,
       current_schemas(false),                 -- search path for name resolution
       inet_server_addr(), inet_server_port();
```

## Databases (FR-030)

```sql
SELECT datname
FROM pg_catalog.pg_database
WHERE datallowconn AND NOT datistemplate
  AND has_database_privilege(datname, 'CONNECT');
```

## Phase A: schemas, objects, row estimates (target < 500 ms for 1,000 objects)

```sql
SELECT n.oid AS schema_oid, n.nspname,
       has_schema_privilege(n.oid, 'USAGE') AS usable
FROM pg_catalog.pg_namespace n
WHERE n.nspname NOT LIKE 'pg\_%' AND n.nspname <> 'information_schema';

SELECT c.oid, n.nspname, c.relname, c.relkind, c.reltuples::bigint,
       obj_description(c.oid, 'pg_class') AS descr
FROM pg_catalog.pg_class c
JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r','p','f','v','m','S')
  AND n.nspname NOT LIKE 'pg\_%' AND n.nspname <> 'information_schema';

SELECT p.oid, n.nspname, p.proname, p.prokind, p.proretset,
       obj_description(p.oid, 'pg_proc') AS descr
FROM pg_catalog.pg_proc p
JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname NOT LIKE 'pg\_%' AND n.nspname <> 'information_schema';
```

Mapping: `r`/`p`/`f` → Table, `v` → View, `m` → MaterializedView, `S` → Sequence, `prokind f/a/w`
→ ScalarFunction (TableFunction when `proretset`), `p` → Procedure. `obj_description` is a
per-row function call; if Phase A misses its budget on large catalogs, descriptions move to
Phase B via a join on `pg_description` (`objoid`, `classoid`, `objsubid = 0`).

## Phase B: columns, keys, foreign keys, indexes, parameters, comments

```sql
-- columns (all tables, views, materialized views in one pass)
SELECT a.attrelid, a.attnum, a.attname,
       pg_catalog.format_type(a.atttypid, a.atttypmod) AS type_name,
       a.atttypmod, a.attnotnull, a.attidentity, a.attgenerated,
       pg_catalog.pg_get_expr(d.adbin, d.adrelid) AS default_expr,
       col_description(a.attrelid, a.attnum) AS descr
FROM pg_catalog.pg_attribute a
JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
LEFT JOIN pg_catalog.pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
WHERE a.attnum > 0 AND NOT a.attisdropped
  AND c.relkind IN ('r','p','f','v','m')
  AND n.nspname NOT LIKE 'pg\_%' AND n.nspname <> 'information_schema';

-- primary keys and indexes
SELECT i.indrelid, i.indexrelid, ic.relname AS index_name, i.indisprimary, i.indisunique,
       i.indkey::int2[] AS attnums, i.indoption::int2[] AS options
FROM pg_catalog.pg_index i
JOIN pg_catalog.pg_class ic ON ic.oid = i.indexrelid
JOIN pg_catalog.pg_class tc ON tc.oid = i.indrelid
JOIN pg_catalog.pg_namespace n ON n.oid = tc.relnamespace
WHERE n.nspname NOT LIKE 'pg\_%' AND n.nspname <> 'information_schema';

-- foreign keys
SELECT con.oid, con.conname, con.conrelid, con.confrelid,
       con.conkey::int2[] AS parent_attnums, con.confkey::int2[] AS referenced_attnums
FROM pg_catalog.pg_constraint con
JOIN pg_catalog.pg_class c ON c.oid = con.conrelid
JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
WHERE con.contype = 'f'
  AND n.nspname NOT LIKE 'pg\_%' AND n.nspname <> 'information_schema';

-- function and procedure parameters
SELECT p.oid, p.proargnames, p.proargtypes::oid[] AS in_types, p.proallargtypes, p.proargmodes,
       p.pronargdefaults, pg_catalog.format_type(p.prorettype, NULL) AS return_type
FROM pg_catalog.pg_proc p
JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname NOT LIKE 'pg\_%' AND n.nspname <> 'information_schema';

-- type names for parameter OIDs (one pass, cached per connection)
SELECT t.oid, pg_catalog.format_type(t.oid, NULL) FROM pg_catalog.pg_type t
WHERE t.oid = ANY($1::oid[]);
```

Column attnums from `indkey` / `conkey` / `confkey` resolve against the columns loaded in the
first query (`attrelid`, `attnum`), so no per-row lookups. `MaxLength` / `Precision` / `Scale`
are derived from `atttypmod` for `varchar`/`char` (typmod − 4), `numeric` (packed precision and
scale) and left 0 otherwise.

## Change detection (periodic, cheap)

```sql
SELECT md5(coalesce(string_agg(c.oid::text || ':' || c.relname || ':' || c.relkind::text || ':' || c.relnamespace::text, ',' ORDER BY c.oid), ''))
FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind IN ('r','p','f','v','m','S') AND n.nspname NOT LIKE 'pg\_%' AND n.nspname <> 'information_schema';

SELECT md5(coalesce(string_agg(p.oid::text || ':' || p.proname || ':' || p.pronamespace::text, ',' ORDER BY p.oid), ''))
FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname NOT LIKE 'pg\_%' AND n.nspname <> 'information_schema';
```

The two hashes concatenated form `LastChangeChecksum`. Column-level changes (ALTER TABLE ADD
COLUMN) do not change these hashes; the existing DDL-regex trigger on scripts run from the
workspace covers the in-product case (FR-043), and the Phase B refresh on manual refresh covers
the rest. (`ORDER BY` inside `string_agg` is an aggregate ordering, not a result ordering.)

## Object definitions (FR-068)

```sql
SELECT pg_catalog.pg_get_viewdef($1::oid, true);           -- views, materialized views
SELECT pg_catalog.pg_get_functiondef($1::oid);             -- functions, procedures
```

## Permission probe (FR-046)

`has_schema_privilege(n.oid, 'USAGE')` from Phase A marks `InaccessibleSchemas`; a
`PostgresException` with SQLSTATE `42501` (insufficient_privilege) on a Phase B query marks the
affected schema instead of failing the load.

## Grid editing (R11)

Key verification when `IsKey` is ambiguous:

```sql
SELECT i.indkey::int2[] FROM pg_catalog.pg_index i WHERE i.indrelid = $1::oid AND i.indisprimary;
```

Writes (one transaction per Apply):

```sql
UPDATE "schema"."table" SET "col" = $1::type_name WHERE "pk1" = $2::type_name AND "pk2" = $3::type_name;
INSERT INTO "schema"."table" ("c1", "c2") VALUES ($1::t1, $2::t2) RETURNING "pk1";
DELETE FROM "schema"."table" WHERE "pk1" = $1::type_name;
```

Affected rows must equal 1 for UPDATE and DELETE; otherwise the transaction is rolled back and
the row is reported as not applied (FR-131).
