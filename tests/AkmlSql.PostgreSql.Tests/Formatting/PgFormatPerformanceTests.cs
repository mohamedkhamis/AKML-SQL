using System.Diagnostics;
using AkmlSql.PostgreSql.Formatting;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.PostgreSql.Tests.Formatting;

/// <summary>
/// Spec 042 (task T180, FR-140): formatting a long PostgreSQL script is quick. The budget of the task is 500 ms for a 2,000-line
/// script with every safety check on (same tree, same tokens and comments, stable on a second pass). A Debug build is several times
/// slower than the Release build the product ships, so the Debug bound is a guard against a slow step creeping in, not the budget.
/// </summary>
/// <summary>A timing test must not share the processor with the other test classes, which xUnit runs in parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PgFormatPerformanceCollection
{
    public const string Name = "PgFormatPerformance";
}

[Collection(PgFormatPerformanceCollection.Name)]
public class PgFormatPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public PgFormatPerformanceTests(ITestOutputHelper output) => _output = output;

#if DEBUG
    private const long LimitMilliseconds = 4000;
#else
    private const long LimitMilliseconds = 500;
#endif

    private static string TwoThousandLines()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "pg-format-corpus", "corpus");
        var style = PgFormatTestStyles.Load("default");
        var parts = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
        {
            var text = File.ReadAllText(file).Replace("\r\n", "\n").TrimEnd();
            if (!text.EndsWith(';') || !PgFormatter.Format(text, style).Success) continue;     // each part must end its last statement to be joined to the next
            parts.Add(text);
        }

        // The accepted scripts, repeated until the script is 2,000 lines long.
        var script = new List<string>();
        var lines = 0;
        while (lines < 2010 && parts.Count > 0)
        {
            foreach (var part in parts)
            {
                if (lines >= 2010) break;
                script.Add(part);
                lines += part.Split('\n').Length + 1;
            }
        }
        return string.Join("\n\n", script);
    }

    [Fact]
    public void A_script_of_two_thousand_lines_is_formatted_with_every_check_inside_the_budget()
    {
        var script = TwoThousandLines();
        var style = PgFormatTestStyles.Load("default");
        var lineCount = script.Split('\n').Length;
        Assert.True(lineCount >= 2000, "the test script should have 2,000 lines: " + lineCount);

        // The budget is for a script formatted in a running engine: the first calls pay for loading and compiling code (the runtime
        // optimises a method only after it has been called many times), so they are not timed. The median of seven timed runs is the result.
        for (var i = 0; i < 8; i++) PgFormatter.Format(script, style);
        var times = new List<long>();
        PgFormatResult result = null!;
        for (var i = 0; i < 7; i++)
        {
            var clock = Stopwatch.StartNew();
            result = PgFormatter.Format(script, style);
            clock.Stop();
            times.Add(clock.ElapsedMilliseconds);
        }
        var median = times.OrderBy(t => t).ElementAt(3);

        _output.WriteLine($"Formatted {lineCount} lines / {script.Length:N0} characters in {median} ms (median of {string.Join(", ", times)}; limit {LimitMilliseconds} ms).");
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Assert.True(median < LimitMilliseconds, $"Formatting {lineCount} lines took {median} ms (runs: {string.Join(", ", times)}).");
    }
}
