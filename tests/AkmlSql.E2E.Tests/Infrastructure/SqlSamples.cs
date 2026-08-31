namespace AkmlSql.E2E.Tests.Infrastructure;

/// <summary>
/// Well-known SQL snippets reused across Formatter and Analyzer E2E tests.
/// </summary>
internal static class SqlSamples
{
    // ── Formatter test data ───────────────────────────────────────────────────

    /// <summary>Lowercase SQL — needs keyword uppercasing by the formatter.</summary>
    public const string Dirty =
        "select id, name from dbo.orders where id > 0 order by id";

    /// <summary>Already-uppercase SQL — formatter should not modify this.</summary>
    public const string AlreadyFormatted =
        "SELECT Id, Name FROM dbo.Orders WHERE Id > 0 ORDER BY Id;";

    /// <summary>Multi-statement batch — formatter must handle all statements.</summary>
    public const string MultiStatement =
        "select 1;\nselect 2;\nselect 3;";

    // ── Analyzer — rule-triggering patterns ──────────────────────────────────

    /// <summary>PE003 — DELETE without WHERE clause (error).</summary>
    public const string DeleteNoWhere =
        "DELETE FROM dbo.Orders";

    /// <summary>UPDATE without WHERE clause (PE003 again, error).</summary>
    public const string UpdateNoWhere =
        "UPDATE dbo.Orders SET Status = 'X'";

    /// <summary>SE002 — Hardcoded credential in variable (error).</summary>
    public const string HardcodedPassword =
        "DECLARE @password VARCHAR(50) = 'secret123'";

    /// <summary>BP004 — Equality comparison with NULL (error).</summary>
    public const string EqualsNull =
        "SELECT 1 WHERE Col = NULL";

    /// <summary>DEP001 — Deprecated 'text' data type (warning).</summary>
    public const string DeprecatedTextType =
        "CREATE TABLE dbo.T (Notes text)";

    /// <summary>EX001 — Division by zero (warning).</summary>
    public const string DivisionByZero =
        "SELECT 10 / 0";

    /// <summary>NM002 — sp_ prefix on user procedure (warning).</summary>
    public const string SpPrefix =
        "CREATE PROCEDURE dbo.sp_GetOrders AS RETURN";

    /// <summary>SE001 — Dynamic SQL with string concatenation (warning/error).</summary>
    public const string DynamicSqlConcat =
        "EXEC('SELECT * FROM ' + @tableName)";

    /// <summary>Clean SQL — no violations expected.</summary>
    public const string Clean =
        "SET NOCOUNT ON;\nSELECT Id, Name, Status\nFROM dbo.Orders\nWHERE Id = @id;";

    /// <summary>Inline PE003 suppression (noqa inline comment). Semicolon avoids ST004.</summary>
    public static string DeleteSuppressed =>
        "DELETE FROM dbo.Orders; -- noqa: PE003";

    /// <summary>Block suppression wrapping a DELETE (noqa-begin/end).</summary>
    public static string DeleteBlockSuppressed =>
        "-- noqa-begin\nDELETE FROM dbo.Orders\n-- noqa-end";

    /// <summary>Inline PE003 suppression in the documented akml-disable-line form.</summary>
    public static string DeleteSuppressedAkmlLine =>
        "DELETE FROM dbo.Orders; -- akml-disable-line PE003";

    /// <summary>Block suppression wrapping a DELETE (akml-disable / akml-enable).</summary>
    public static string DeleteBlockSuppressedAkml =>
        "-- akml-disable PE003\nDELETE FROM dbo.Orders;\n-- akml-enable PE003";

    /// <summary>Whole-script PE003 suppression: an akml-disable with no matching enable.</summary>
    public static string DeleteScriptSuppressedAkml =>
        "-- akml-disable PE003\nDELETE FROM dbo.Orders;\nGO\nDELETE FROM dbo.Customers;";

    /// <summary>Deliberately unparseable SQL.</summary>
    public const string Invalid =
        "THIS IS NOT SQL @@@### ???";

    // ── Formatter + Analyzer shared ──────────────────────────────────────────

    /// <summary>Multiple issues in one file (PE003 + BP004).</summary>
    public const string MultiIssue =
        "DELETE FROM dbo.Orders\nSELECT 1 WHERE Col = NULL";
}
