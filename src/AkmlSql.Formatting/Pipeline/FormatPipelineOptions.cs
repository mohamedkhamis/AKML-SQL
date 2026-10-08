namespace AkmlSql.Formatting.Pipeline;

/// <summary>What Stage 8 does with statement terminators.</summary>
public enum SemicolonAction
{
    Leave = 0,
    Insert = 1,
    Remove = 2,
}

/// <summary>What Stage 8 does with square brackets around identifiers.</summary>
public enum BracketAction
{
    Leave = 0,
    Add = 1,
    Remove = 2,
}

/// <summary>
/// Spec 040 (STY-11, research R29) — per-call choices for <see cref="FormatterPipeline"/>, set from
/// the interactive Format SQL actions. Everything defaults to today's behaviour: layout and casing
/// run, and a null Stage 8 choice falls back to the style's own <c>formatActions</c> — so the CLI,
/// bulk format and the format-parity goldens, which pass no options, are unaffected.
/// </summary>
public sealed class FormatPipelineOptions
{
    /// <summary>Today's behaviour.</summary>
    public static FormatPipelineOptions Default { get; } = new();

    /// <summary>False skips the layout stage: the original line breaks and spacing are kept.</summary>
    public bool ApplyLayout { get; init; } = true;

    /// <summary>False skips the casing stage: keywords, functions and types keep their case.</summary>
    public bool ApplyCasing { get; init; } = true;

    /// <summary>Stage 8 semicolons; null = the style's <c>insertSemicolons</c> / <c>removeSemicolons</c>.</summary>
    public SemicolonAction? Semicolons { get; init; }

    /// <summary>Stage 8 brackets; null = the style's <c>addSquareBrackets</c> (false meaning leave).</summary>
    public BracketAction? SquareBrackets { get; init; }
}
