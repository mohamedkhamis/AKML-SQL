using System.Diagnostics.CodeAnalysis;

namespace AkmlSql.Formatter.Output;

/// <summary>
/// Renders colored console output for format/check/diff results.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static class ConsoleRenderer
{
    public static void WriteInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Error.Write("info: ");
        Console.ResetColor();
        Console.Error.WriteLine(message);
    }

    public static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Error.Write("ok: ");
        Console.ResetColor();
        Console.Error.WriteLine(message);
    }

    public static void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Error.Write("warn: ");
        Console.ResetColor();
        Console.Error.WriteLine(message);
    }

    public static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.Write("error: ");
        Console.ResetColor();
        Console.Error.WriteLine(message);
    }

    public static void WriteFile(string path, bool modified)
    {
        if (modified)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.Write("  modified: ");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Error.Write("  unchanged: ");
        }
        Console.ResetColor();
        Console.Error.WriteLine(path);
    }

    public static void WriteCheckResult(string path, bool formatted)
    {
        if (formatted)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Error.Write("  pass: ");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.Write("  fail: ");
        }
        Console.ResetColor();
        Console.Error.WriteLine(path);
    }

    public static void WriteSummary(int total, int modified, int errors, long elapsedMs)
    {
        Console.Error.WriteLine();
        Console.Error.Write("Processed ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Error.Write(total.ToString());
        Console.ResetColor();
        Console.Error.Write(" file(s) in ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Error.Write($"{elapsedMs}ms");
        Console.ResetColor();
        Console.Error.WriteLine(".");

        if (modified > 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.WriteLine($"  {modified} file(s) modified.");
            Console.ResetColor();
        }

        if (errors > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"  {errors} file(s) had errors.");
            Console.ResetColor();
        }
    }

    public static void WriteCheckSummary(int total, int violations, long elapsedMs)
    {
        Console.Error.WriteLine();
        Console.Error.Write("Checked ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Error.Write(total.ToString());
        Console.ResetColor();
        Console.Error.Write(" file(s) in ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Error.Write($"{elapsedMs}ms");
        Console.ResetColor();
        Console.Error.WriteLine(".");

        if (violations > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"  {violations} file(s) would be reformatted.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Error.WriteLine("  All files are correctly formatted.");
            Console.ResetColor();
        }
    }

    public static void WriteDiffHeader(string path)
    {
        Console.ForegroundColor = ConsoleColor.White;
        Console.Error.WriteLine($"--- {path}");
        Console.Error.WriteLine($"+++ {path} (formatted)");
        Console.ResetColor();
    }

    public static void WriteDiffLine(char prefix, string line)
    {
        switch (prefix)
        {
            case '+':
                Console.ForegroundColor = ConsoleColor.Green;
                break;
            case '-':
                Console.ForegroundColor = ConsoleColor.Red;
                break;
            default:
                Console.ResetColor();
                break;
        }
        Console.WriteLine($"{prefix}{line}");
        Console.ResetColor();
    }
}
