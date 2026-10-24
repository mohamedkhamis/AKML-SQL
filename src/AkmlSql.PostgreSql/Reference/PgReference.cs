using System.Text.Json;
using System.Text.RegularExpressions;

namespace AkmlSql.PostgreSql.Reference;

/// <summary>The kinds of entry in the PostgreSQL reference (the <c>kind</c> field of the generated file).</summary>
public enum PgEntryKind
{
    Keyword,
    Command,
    Function,
    Aggregate,
    Window,
    Procedure,
    Operator,
    Type,
    SqlState,
    Lexical,
}

/// <summary>One argument of a function, procedure or operator signature.</summary>
/// <param name="Mode"><c>in</c>, <c>out</c>, <c>inout</c>, <c>variadic</c> or <c>table</c>.</param>
public sealed record PgReferenceArg(string? Name, string Type, string Mode, bool HasDefault);

public sealed record PgReferenceSignature(IReadOnlyList<PgReferenceArg> Args, string? Returns, bool IsSetReturning)
{
    /// <summary><c>(text, integer)</c> or <c>(value text, delimiter text)</c> - the argument list as a person reads it.</summary>
    public string ArgumentText => "(" + string.Join(", ", Args.Select(a =>
        (a.Name is { Length: > 0 } n && a.Mode != "variadic" ? n + " " : string.Empty) + a.Type + (a.Mode == "variadic" ? " ..." : string.Empty))) + ")";
}

/// <summary>One thing the manual describes: a key word, a command, a function, an operator, a type, an error code, a lexical form.</summary>
/// <param name="Synopsis">The syntax diagram of a command; null for everything else.</param>
/// <param name="MinVersion">The oldest supported major release that has it (FR-052).</param>
public sealed record PgReferenceEntry(
    PgEntryKind Kind,
    string Name,
    IReadOnlyList<string> Aliases,
    string Category,
    string Summary,
    string? Synopsis,
    IReadOnlyList<PgReferenceSignature> Signatures,
    int MinVersion,
    string SourceFile,
    string SectionId,
    string Url)
{
    public bool IsCallable => Kind is PgEntryKind.Function or PgEntryKind.Aggregate or PgEntryKind.Window or PgEntryKind.Procedure;
}

/// <summary>
/// Spec 042 (FR-050 to FR-054, task T094) - the PostgreSQL reference generated from the manual and the catalogs (see
/// <c>tools/pg-reference</c>), with lookups by kind. It is loaded from the JSON embedded in this assembly, checked once on load,
/// and <see cref="For"/> gives the view a server of one major release should be offered.
/// </summary>
public sealed class PgReference
{
    private static readonly Lazy<PgReference> Shared = new(LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly Dictionary<int, PgReference> _views = new();

    private readonly Dictionary<string, PgReferenceEntry> _keywords;
    private readonly Dictionary<string, PgReferenceEntry> _commands;
    private readonly Dictionary<string, List<PgReferenceEntry>> _commandsByFirstWord;
    private readonly Dictionary<string, List<PgReferenceEntry>> _callables;
    private readonly Dictionary<string, PgReferenceEntry> _operators;
    private readonly Dictionary<string, PgReferenceEntry> _types;
    private readonly Dictionary<string, PgReferenceEntry> _sqlStates;

    /// <summary>The reference loaded from this assembly; checked on first use.</summary>
    public static PgReference Instance => Shared.Value;

    private PgReference(string version, IReadOnlyList<int> supportedMajors, IReadOnlyList<PgReferenceEntry> entries)
    {
        Version = version;
        SupportedMajors = supportedMajors;
        Entries = entries;

        _keywords = ByName(PgEntryKind.Keyword);
        _commands = ByName(PgEntryKind.Command);
        _operators = ByName(PgEntryKind.Operator);
        _sqlStates = ByName(PgEntryKind.SqlState);

        _types = new Dictionary<string, PgReferenceEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries.Where(e => e.Kind == PgEntryKind.Type))
        {
            _types[e.Name] = e;
            foreach (var alias in e.Aliases) _types.TryAdd(alias, e);
        }

        _callables = new Dictionary<string, List<PgReferenceEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries.Where(e => e.IsCallable))
        {
            if (!_callables.TryGetValue(e.Name, out var list)) _callables[e.Name] = list = new List<PgReferenceEntry>();
            list.Add(e);
        }

        // CREATE TABLE is found as "CREATE TABLE" and under CREATE, so completion can offer the second word after the first.
        _commandsByFirstWord = new Dictionary<string, List<PgReferenceEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in _commands.Values)
        {
            var first = e.Name.Split(' ', 2)[0];
            if (!_commandsByFirstWord.TryGetValue(first, out var list)) _commandsByFirstWord[first] = list = new List<PgReferenceEntry>();
            list.Add(e);
        }
    }

    private Dictionary<string, PgReferenceEntry> ByName(PgEntryKind kind) =>
        Entries.Where(e => e.Kind == kind).ToDictionary(e => e.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>The release whose manual the entries are read from and linked to, for example <c>18</c>.</summary>
    public string Version { get; }

    /// <summary>The major releases the reference was generated from, oldest first.</summary>
    public IReadOnlyList<int> SupportedMajors { get; }

    public IReadOnlyList<PgReferenceEntry> Entries { get; }

    /// <summary>A key word by its name, case-insensitively.</summary>
    public PgReferenceEntry? Keyword(string name) => _keywords.GetValueOrDefault(name);

    /// <summary>A command by its full name: <c>CREATE TABLE</c>, <c>SELECT</c>. White space between the words does not matter.</summary>
    public PgReferenceEntry? Command(string name) => _commands.GetValueOrDefault(Regex.Replace(name.Trim(), @"\s+", " "));

    /// <summary>Every command whose name starts with <paramref name="firstWord"/>: <c>CREATE</c> gives <c>CREATE TABLE</c>, <c>CREATE INDEX</c>, and so on.</summary>
    public IReadOnlyList<PgReferenceEntry> CommandsStartingWith(string firstWord) =>
        _commandsByFirstWord.TryGetValue(firstWord, out var list) ? list : Array.Empty<PgReferenceEntry>();

    public IEnumerable<PgReferenceEntry> AllCommands => _commands.Values;

    public IEnumerable<PgReferenceEntry> AllKeywords => _keywords.Values;

    /// <summary>A function, aggregate, window function or procedure by name; usually one entry, because overloads are one entry.</summary>
    public IReadOnlyList<PgReferenceEntry> Callables(string name) =>
        _callables.TryGetValue(name, out var list) ? list : Array.Empty<PgReferenceEntry>();

    public IEnumerable<PgReferenceEntry> AllCallables => _callables.Values.SelectMany(v => v);

    public PgReferenceEntry? Operator(string name) => _operators.GetValueOrDefault(name);

    public IEnumerable<PgReferenceEntry> AllOperators => _operators.Values;

    /// <summary>A type by its name or one of its aliases: <c>integer</c>, <c>int4</c> and <c>int</c> are the same entry.</summary>
    public PgReferenceEntry? Type(string name) => _types.GetValueOrDefault(Regex.Replace(name.Trim(), @"\s+", " "));

    public IEnumerable<PgReferenceEntry> AllTypes => Entries.Where(e => e.Kind == PgEntryKind.Type);

    /// <summary>An error code such as <c>23505</c>.</summary>
    public PgReferenceEntry? SqlState(string code) => _sqlStates.GetValueOrDefault(code);

    /// <summary>The error code of a condition name such as <c>unique_violation</c>.</summary>
    public PgReferenceEntry? SqlStateByCondition(string condition) =>
        _sqlStates.Values.FirstOrDefault(e => e.Aliases.Contains(condition, StringComparer.OrdinalIgnoreCase));

    public IEnumerable<PgReferenceEntry> Lexical => Entries.Where(e => e.Kind == PgEntryKind.Lexical);

    /// <summary>
    /// The view a server of release <paramref name="serverMajor"/> should be offered (FR-052): only entries it has. A release
    /// newer than the reference knows gets everything. The view is cached.
    /// </summary>
    public PgReference For(int serverMajor)
    {
        lock (_views)
        {
            if (!_views.TryGetValue(serverMajor, out var view))
            {
                var kept = Entries.Where(e => e.MinVersion <= serverMajor).ToList();
                _views[serverMajor] = view = kept.Count == Entries.Count ? this : new PgReference(Version, SupportedMajors, kept);
            }
            return view;
        }
    }

    // ── Loading ─────────────────────────────────────────────────────────────────────────────────

    private static PgReference LoadEmbedded()
    {
        var assembly = typeof(PgReference).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("postgresql-reference.json", StringComparison.Ordinal))
            ?? throw new InvalidDataException("The PostgreSQL reference is not embedded in " + assembly.GetName().Name + ".");
        using var stream = assembly.GetManifestResourceStream(name)!;
        return Load(stream);
    }

    /// <summary>Loads a generated reference and checks it; <see cref="InvalidDataException"/> lists everything wrong with it.</summary>
    public static PgReference Load(Stream json)
    {
        var file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions)
            ?? throw new InvalidDataException("The PostgreSQL reference is empty.");

        var majors = file.GeneratedFrom?.Tags?.Select(MajorOf).Where(m => m > 0).OrderBy(m => m).ToList() ?? new List<int>();
        var entries = (file.Entries ?? new List<EntryDto>()).Select(Map).ToList();

        var problems = Check(file.Version ?? string.Empty, majors, entries);
        if (problems.Count > 0)
            throw new InvalidDataException("The PostgreSQL reference is not valid:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Take(20)));

        return new PgReference(file.Version!, majors, entries);
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static int MajorOf(string tag) => Regex.Match(tag, @"REL_?(\d+)") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    /// <summary>The invariants of <c>contracts/reference-schema.md</c>.</summary>
    /// <summary>
    /// docs/{version}/{section}.html for a section that is a page; docs/{version}/{page}.html#{SECTION} for a nested section, which
    /// the manual publishes only as an upper-case anchor on its parent page.
    /// </summary>
    private static bool FollowsManualUrl(string url, string version, string sectionId)
    {
        var root = $"https://www.postgresql.org/docs/{version}/";
        if (url == root + sectionId + ".html") return true;
        var anchor = ".html#" + sectionId.ToUpperInvariant();
        if (!url.StartsWith(root, StringComparison.Ordinal) || !url.EndsWith(anchor, StringComparison.Ordinal)) return false;
        var page = url[root.Length..^anchor.Length];
        return page.Length > 0 && page.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');
    }

    private static List<string> Check(string version, IReadOnlyList<int> majors, IReadOnlyList<PgReferenceEntry> entries)
    {
        var problems = new List<string>();
        if (version.Length == 0) problems.Add("no version");
        if (majors.Count == 0) problems.Add("no supported releases");
        if (entries.Count == 0) problems.Add("no entries");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            var label = e.Kind + " " + e.Name;
            if (!seen.Add(e.Kind + "\u0001" + e.Name.ToLowerInvariant())) problems.Add(label + ": the name is not unique for its kind");
            if (e.Summary.Trim().Length == 0) problems.Add(label + ": no summary");
            if (e.SectionId.Length == 0) problems.Add(label + ": no section");
            if (!FollowsManualUrl(e.Url, version, e.SectionId)) problems.Add(label + ": the url does not follow docs/" + version + "/<section>.html or <page>.html#<SECTION>");
            if (majors.Count > 0 && (e.MinVersion < majors[0] || e.MinVersion > majors[^1])) problems.Add(label + ": minVersion " + e.MinVersion + " is not a supported release");
        }
        return problems;
    }

    private static PgReferenceEntry Map(EntryDto e) => new(
        Enum.TryParse<PgEntryKind>(e.Kind, ignoreCase: true, out var kind) ? kind : throw new InvalidDataException("Unknown entry kind '" + e.Kind + "'."),
        e.Name ?? string.Empty,
        e.Aliases ?? new List<string>(),
        e.Category ?? string.Empty,
        e.Summary ?? string.Empty,
        e.Synopsis,
        (e.Signatures ?? new List<SignatureDto>()).Select(s => new PgReferenceSignature(
            (s.Args ?? new List<ArgDto>()).Select(a => new PgReferenceArg(a.Name, a.Type ?? string.Empty, a.Mode ?? "in", a.HasDefault)).ToList(),
            s.Returns,
            s.IsSetReturning)).ToList(),
        e.MinVersion,
        e.Source?.File ?? string.Empty,
        e.Source?.SectionId ?? string.Empty,
        e.Url ?? string.Empty);

    // The shape of the generated file; see tools/pg-reference.
    private sealed class FileDto
    {
        public string? Version { get; set; }
        public GeneratedFromDto? GeneratedFrom { get; set; }
        public List<EntryDto>? Entries { get; set; }
    }

    private sealed class GeneratedFromDto
    {
        public List<string>? Tags { get; set; }
    }

    private sealed class EntryDto
    {
        public string? Kind { get; set; }
        public string? Name { get; set; }
        public List<string>? Aliases { get; set; }
        public string? Category { get; set; }
        public string? Summary { get; set; }
        public string? Synopsis { get; set; }
        public List<SignatureDto>? Signatures { get; set; }
        public int MinVersion { get; set; }
        public SourceDto? Source { get; set; }
        public string? Url { get; set; }
    }

    private sealed class SignatureDto
    {
        public List<ArgDto>? Args { get; set; }
        public string? Returns { get; set; }
        public bool IsSetReturning { get; set; }
    }

    private sealed class ArgDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Mode { get; set; }
        public bool HasDefault { get; set; }
    }

    private sealed class SourceDto
    {
        public string? File { get; set; }
        public string? SectionId { get; set; }
    }
}
