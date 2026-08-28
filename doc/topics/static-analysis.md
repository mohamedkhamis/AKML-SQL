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

## Suppress a rule inline

Disable a rule for a block of code:

```sql
-- akml-disable PE001
SELECT * FROM dbo.Orders
-- akml-enable PE001
```

Or for a single line:

```sql
SELECT * FROM dbo.Orders  -- akml-disable-line PE001
```

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
