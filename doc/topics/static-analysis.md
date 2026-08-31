# Static Code Analysis

AKML SQL checks your T-SQL for problems as you type and when you save. It ships with more than 130 rules, grouped into 8 categories.

## What it checks

- **Performance** — patterns that make queries slow: `SELECT *`, missing `WHERE` on `DELETE`/`UPDATE`, leading-wildcard `LIKE`, non-SARGable predicates, missing `SET NOCOUNT ON`, and more.
- **Best Practices** — fragile or incorrect T-SQL: `= NULL` comparisons, `EXEC(string)` instead of `sp_executesql`, unused variables, missing `TRY/CATCH`.
- **Security** — real vulnerabilities: SQL injection via string concatenation, hard-coded passwords, `xp_cmdshell`, grants to `PUBLIC`, weak hash algorithms.
- **Style** — readability and consistency issues in how the code is written.
- **Design** — structural problems in procedures and schema usage.
- **Deprecated** — syntax and features SQL Server has deprecated.
- **Execution** — statements that behave dangerously or unexpectedly at run time.
- **Naming** — naming-convention violations for objects, aliases, and variables.

Each rule has an ID like `PE003` (category letters plus a number). The full list with descriptions and severities is in the [Analysis rules reference](../analysis-rules.md).

## Where results appear

- Squiggles in the editor under the offending code — hover for the rule message.
- The Visual Studio / SSMS **Error List** window, with rule ID, message, and location.

Many rules offer a lightbulb auto-fix. Click the squiggle and choose the fix; the change is undoable with Ctrl+Z.

## Turn a rule off

Click the warning glyph in the margin, or the lightbulb on the squiggle, and pick how far the
change should reach. The four scopes go from narrowest to widest:

| Menu item | Reaches | Lasts | Where it is recorded |
|---|---|---|---|
| **Suppress PE001 on this line** | that one line | until you delete the comment | a comment in your script |
| **Disable PE001 in this script** | the whole file | until you delete the comment | a comment in your script |
| **Disable PE001 for this session** | every file | until you close SSMS / Visual Studio | nowhere — it is held in memory |
| **Disable PE001 everywhere** | every file | permanently | `config.json` |

The first two write a comment, so they travel with the file — commit them and your team sees the
same result. The session scope writes nothing at all, which makes it the right choice for "not
right now" rather than "not ever". The last one is reversible from
**Tools → AKML SQL → Manage Code Analysis Rules**.

### Writing the comments by hand

The first two scopes are just comments, so you can type them yourself.

Suppress on one line:

```sql
SELECT * FROM dbo.Orders  -- akml-disable-line PE001
```

Suppress over a block:

```sql
-- akml-disable PE001
SELECT * FROM dbo.Orders
-- akml-enable PE001
```

Leave out the `-- akml-enable` and the suppression runs to the end of the file — that is how
"disable in this script" works. Put it on the first line to cover everything:

```sql
-- akml-disable PE001
```

A few details worth knowing:

- Name several rules at once with commas: `-- akml-disable PE001, BP004`.
- Name no rule at all and every rule is suppressed: `-- akml-disable-line` silences the whole line.
- A bare `-- akml-enable` closes everything currently open.
- Anything after the rule ids is treated as a note, so you can say why:
  `-- akml-disable PE001 reporting query, columns are intentional`.
- The directives are case-insensitive and work in `/* … */` comments too.
- The older `-- noqa: PE001`, `-- noqa`, and `-- noqa-begin` / `-- noqa-end` forms still work.

### Undoing a session suppression

Open **Tools → AKML SQL → Manage Code Analysis Rules**. Rules disabled for the session are
highlighted and listed along the bottom of the dialog, with a **Restore** button that puts them all
back when you Save. They also come back on their own the next time you start the IDE.

## Per-project settings

Create a `.casettings` file anywhere in your project folder tree. AKML SQL searches upward from the current file's directory and applies the nearest file, so the whole team gets the same rules when the file is committed to source control.

```jsonc
{
  "rules": {
    "PE001": { "severity": "Warning", "enabled": true },
    "SE001": { "severity": "Error",   "enabled": true },
    "ST001": { "enabled": false }
  },
  "globalSuppressions": [
    { "ruleId": "NM002", "reason": "Legacy naming convention" }
  ]
}
```

Severity values are `None`, `Info`, `Warning`, and `Error`.

## Tune analysis

Open **Tools** -> **Options** -> **AKML SQL** -> **Code Analysis** to turn analysis on or off, choose when it runs (as you type / on save), and control Error List integration. See the [Configuration reference](../configuration.md) for all keys.

Related: [Refactoring](refactoring.md), [Formatting](formatting.md).
