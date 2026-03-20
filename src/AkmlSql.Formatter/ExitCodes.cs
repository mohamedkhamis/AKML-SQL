namespace AkmlSql.Formatter;

/// <summary>
/// CLI exit codes.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int Violations = 1;
    public const int ParseError = 2;
    public const int FileNotFound = 3;
    public const int InvalidProfile = 4;
    public const int InternalError = 5;
}
