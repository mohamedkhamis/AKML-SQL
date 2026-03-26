# Contract: CLI Tool Interface (AkmlSql.Analyzer)

**Branch**: `005-static-code-analysis` | **Date**: 2026-03-22

---

## Executable

`AkmlSql.Analyzer.exe` — standalone self-contained .NET 10 win-x64 executable. Does not require an SSMS/VS installation; shares the Engine analysis logic via project reference to `AkmlSql.Engine`.

---

## Arguments

```
AkmlSql.Analyzer.exe [options]

Input (one required):
  --file <path>              Analyze a single .sql file
  --directory <path>         Analyze all .sql files in a directory
  --recursive                (with --directory) Include subdirectories

Output:
  --report <path>            Write JSON report to file (default: stdout summary only)
  --format text|json         Output format for stdout (default: text)

Filtering:
  --severity error|warning|information|hint
                             Minimum severity to report (default: information)
  --rules PE001,BP004,...    Only run specified rules (comma-separated)
  --exclude-rules PE008,...  Skip specified rules

Configuration:
  --settings <path>          Path to a .casettings JSON file
                             (overrides auto-discovery)

CI/CD:
  --check                    Exit with code 1 if any violations at or above
                             --severity are found; exit 0 if clean

Misc:
  --version                  Print version and exit
  --help                     Print help and exit
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success — no violations at or above `--severity` threshold (or `--check` not specified) |
| `1` | Violations found at or above `--severity` threshold (only when `--check` is specified) |
| `2` | Tool error — bad arguments, file not found, parse failure, etc. |

---

## Text Output Format (--format text)

```
Analyzing: scripts/ (3 files)

  dbo.GetOrders.sql(15,8): warning PE001 - Avoid SELECT * in stored procedures
  dbo.GetOrders.sql(22,1): error   BP004 - Comparison with NULL should use IS NULL
  migrations/v1.sql(8,5):  info   DEP001 - Deprecated data type: use varchar(max) instead of text

Summary: 3 issues (1 error, 1 warning, 1 information) across 3 files
```

---

## JSON Report Format (--report output.json)

```json
{
  "timestamp": "2026-03-22T10:00:00Z",
  "settings": "Team Standard",
  "summary": {
    "filesAnalyzed": 3,
    "totalIssues": 3,
    "errors": 1,
    "warnings": 1,
    "information": 1,
    "hints": 0
  },
  "byCategory": {
    "Performance": 1,
    "BestPractices": 1,
    "Deprecated": 1
  },
  "issues": [
    {
      "file": "dbo.GetOrders.sql",
      "line": 15,
      "column": 8,
      "rule": "PE001",
      "severity": "warning",
      "message": "Avoid SELECT * in stored procedures",
      "fix": "available"
    },
    {
      "file": "dbo.GetOrders.sql",
      "line": 22,
      "column": 1,
      "rule": "BP004",
      "severity": "error",
      "message": "Comparison with NULL should use IS NULL",
      "fix": "available"
    },
    {
      "file": "migrations/v1.sql",
      "line": 8,
      "column": 5,
      "rule": "DEP001",
      "severity": "information",
      "message": "Deprecated data type: use varchar(max) instead of text",
      "fix": "available"
    }
  ]
}
```

---

## CI/CD Usage Examples

```bash
# Fail the pipeline if any errors exist
AkmlSql.Analyzer.exe --directory scripts/ --recursive --check --severity error

# Fail on warnings or errors, write report artifact
AkmlSql.Analyzer.exe --directory scripts/ --recursive --check --severity warning \
  --report analysis-report.json

# Use project settings file
AkmlSql.Analyzer.exe --directory . --settings team.casettings.json --check

# Analyze a single migration file
AkmlSql.Analyzer.exe --file migrations/v42.sql --format text
```

---

## Constraints

- The CLI does not connect to a database; schema-dependent rules (e.g. PE001 column expansion) run in degraded mode (warn that schema is unavailable) but do not crash
- Processing is single-threaded at the file level but rules within each file run in parallel (up to 8 concurrent)
- Memory: must handle a directory of 1,000 SQL files without exceeding 500MB resident memory (files processed sequentially, not loaded all at once)
