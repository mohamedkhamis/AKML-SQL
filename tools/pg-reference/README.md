# PostgreSQL reference generator (spec 042)

`AkmlSql.PostgreSql.ReferenceGen` builds the PostgreSQL reference that completion, quick info and signature help
use (FR-050 to FR-054). It reads checked-out PostgreSQL source trees and writes four committed outputs. It is a
developer tool, run by hand when the reference needs to change (a new PostgreSQL release, a fixed omission). It is
in the solution so it builds with it, but it is never part of the installer.

The format of the outputs and the exact source of every entry kind are in
[`specs/042-postgresql-support/contracts/reference-schema.md`](../../specs/042-postgresql-support/contracts/reference-schema.md).

## Outputs

| File | Role |
|---|---|
| `src/AkmlSql.PostgreSql/Reference/postgresql-reference.json` | the reference, embedded in the library |
| `src/AkmlSql.PostgreSql/Reference/omissions.json` | what was left out on purpose, with the reason |
| `src/AkmlSql.PostgreSql/Reference/coverage.json` | counts of the source entries, so coverage can be measured |
| `doc/postgresql-reference.md` | the same reference as a document a person can review |

## Running it

1. Get the sources, shallow, **outside** the repository (one folder per supported release):

   ```text
   git clone --depth 1 --branch REL_18_STABLE https://git.postgresql.org/git/postgresql.git pg18
   git clone --depth 1 --branch REL_17_STABLE https://git.postgresql.org/git/postgresql.git pg17
   git clone --depth 1 --branch REL_16_STABLE https://git.postgresql.org/git/postgresql.git pg16
   git clone --depth 1 --branch REL_15_STABLE https://git.postgresql.org/git/postgresql.git pg15
   git clone --depth 1 --branch REL_14_STABLE https://git.postgresql.org/git/postgresql.git pg14
   ```

   The five releases are the community-supported majors at the time of writing (14 reaches end of life on
   2026-11-12). The documentation version the reference links to is 18.

2. Run the tool from the repository root. `--source` is the folder that holds the five clones, named `pg14` to `pg18`:

   ```text
   dotnet run --project tools/pg-reference/AkmlSql.PostgreSql.ReferenceGen -- gen --source <folder> --tags REL_14_STABLE,REL_15_STABLE,REL_16_STABLE,REL_17_STABLE,REL_18_STABLE --doc-version 18 --out .
   ```

3. Review the diff of `doc/postgresql-reference.md` and `omissions.json`. Every `unresolved` omission must be driven to
   zero or have a written reason. Run `dotnet test tests/AkmlSql.PostgreSql.Tests --filter Category=Reference`.

The run is deterministic: the same sources give the same bytes, so a pull request that changes the reference shows
exactly what changed.
