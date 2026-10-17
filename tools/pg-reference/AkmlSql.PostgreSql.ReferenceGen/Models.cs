using System.Text.Json;
using System.Text.Json.Serialization;

namespace AkmlSql.PostgreSql.ReferenceGen;

/// <summary>The kinds of entry in the reference (the <c>kind</c> field of the output).</summary>
public static class EntryKinds
{
    public const string Keyword = "keyword";
    public const string Command = "command";
    public const string Function = "function";
    public const string Aggregate = "aggregate";
    public const string Window = "window";
    public const string Procedure = "procedure";
    public const string Operator = "operator";
    public const string Type = "type";
    public const string SqlState = "sqlstate";
    public const string Lexical = "lexical";

    /// <summary>The order the kinds are written in, in the JSON and the human-readable document.</summary>
    public static readonly string[] Order = { Command, Keyword, Type, Function, Aggregate, Window, Procedure, Operator, SqlState, Lexical };
}

public sealed class ReferenceFile
{
    public string Version { get; set; } = string.Empty;
    public GeneratedFrom GeneratedFrom { get; set; } = new();
    public List<ReferenceEntry> Entries { get; set; } = new();
}

public sealed class GeneratedFrom
{
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string Generator { get; set; } = ReferenceGen.Generator.Version;
}

public sealed class ReferenceEntry
{
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public string Category { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? Synopsis { get; set; }
    public List<ReferenceSignature>? Signatures { get; set; }
    public int MinVersion { get; set; }
    public ReferenceSource Source { get; set; } = new();
    public string Url { get; set; } = string.Empty;
}

public sealed class ReferenceSignature
{
    public List<ReferenceArg> Args { get; set; } = new();
    public string? Returns { get; set; }
    public bool IsSetReturning { get; set; }
}

public sealed class ReferenceArg
{
    public string? Name { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Mode { get; set; } = "in";
    public bool HasDefault { get; set; }
}

public sealed class ReferenceSource
{
    public string File { get; set; } = string.Empty;
    public string SectionId { get; set; } = string.Empty;
}

/// <summary>Something in the documentation the reference deliberately does not carry, and why.</summary>
public sealed record Omission(string Kind, string Name, string Reason);

/// <summary>How much of what the documentation lists the reference carries, per kind.</summary>
public sealed class Coverage
{
    public string DocVersion { get; set; } = string.Empty;
    public CoverageCount Commands { get; set; } = new();
    public CoverageCount Keywords { get; set; } = new();
    public CoverageCount Types { get; set; } = new();
    public CoverageCount Functions { get; set; } = new();
    public CoverageCount Operators { get; set; } = new();
    public CoverageCount SqlState { get; set; } = new();
}

public sealed class CoverageCount
{
    /// <summary>What the source documents (the denominator).</summary>
    public int Documented { get; set; }

    /// <summary>How many of those the reference carries.</summary>
    public int Included { get; set; }
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
