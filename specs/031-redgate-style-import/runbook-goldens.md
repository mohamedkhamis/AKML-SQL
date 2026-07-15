# Golden generation runbook — SQL Prompt 11 (manual, ~20 minutes)

You are the ground-truth generator: AKML will be tuned until it matches these outputs byte-for-byte.

## One-time setup

1. Open SSMS 22 with SQL Prompt 11 loaded.
2. SQL Prompt → Options → Styles: confirm the **MohamedKhamis** style is present and set it as the **active** style.
3. SQL Prompt → Options → uncheck anything that rewrites content beyond formatting if enabled (e.g. "Insert semicolons", "Qualify object names"; "Apply casing options" stays ON — casing IS part of the style). Formatting must be the style alone.
4. Connection: any server is fine — do NOT rely on database objects existing; the corpus uses Northwind-style names but formatting does not require them to resolve. `useObjectDefinitionCase` may recase identifiers when connected to a database that HAS these objects — to keep goldens connection-independent, generate them while connected to a database WITHOUT Northwind objects (e.g. an empty scratch DB).

## Per file (repeat for each of the 20 files)

1. File → Open → `tests/format-parity/corpus/sp031-NN-….sql`.
2. Select all (Ctrl+A) → SQL Prompt → **Format SQL** (Ctrl+K, Ctrl+Y).
3. File → Save As → `tests/format-parity/golden/sp031-NN-…__mohamedkhamis.sql`
   - Same file stem + `__mohamedkhamis` suffix, UTF-8, no BOM if the dialog offers a choice.
   - IMPORTANT: Save As, never overwrite the corpus input.

## Deliver

Reply in the working session (or commit if you prefer) with the 20 golden files in
`tests/format-parity/golden/`. The comparison normalizes trailing whitespace, CRLF→LF and BOM,
so minor editor differences are tolerated — content/layout is what is measured.

## Sanity checklist before delivering

- [ ] 20 files, names exactly `sp031-NN-<stem>__mohamedkhamis.sql`
- [ ] Keywords are UPPERCASE, list commas are leading (`, `) — quick visual confirmation the right style was active
- [ ] sp031-18: statements separated by exactly 2 blank lines; `;` preceded by one space
