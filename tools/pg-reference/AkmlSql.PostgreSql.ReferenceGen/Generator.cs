using System.Text;
using System.Text.RegularExpressions;
using AkmlSql.PostgreSql.Reference;

namespace AkmlSql.PostgreSql.ReferenceGen;

/// <summary>Where a PostgreSQL source tree's files are read from; a directory in production, a dictionary in tests.</summary>
public interface ISourceTree
{
    /// <summary>The text of a file (path relative to the tree root, forward slashes), or null when it does not exist.</summary>
    string? ReadText(string relativePath);

    /// <summary>The relative paths of the files in a directory that match <paramref name="searchPattern"/>.</summary>
    IEnumerable<string> ListFiles(string relativeDirectory, string searchPattern);
}

public sealed class DirectorySourceTree : ISourceTree
{
    private readonly string _root;

    public DirectorySourceTree(string root) => _root = root;

    public string? ReadText(string relativePath)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public IEnumerable<string> ListFiles(string relativeDirectory, string searchPattern)
    {
        var directory = Path.Combine(_root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory)) return Enumerable.Empty<string>();
        return Directory.EnumerateFiles(directory, searchPattern)
            .Select(f => relativeDirectory.TrimEnd('/') + "/" + Path.GetFileName(f))
            .OrderBy(f => f, StringComparer.Ordinal);
    }
}

public sealed record CatalogFunction(string Name, char Kind, IReadOnlyList<ReferenceArg> Args, string Returns, bool SetOf, string Description);

public sealed record CatalogOperator(string Name, string? Left, string? Right, string Result, string Description);

public sealed record CatalogType(string Name, char TypeType, char Category, string Description);

/// <summary>Everything the generator reads from one release of the PostgreSQL source.</summary>
public sealed class TagSnapshot
{
    public IReadOnlyList<KeywordRecord> Keywords { get; init; } = Array.Empty<KeywordRecord>();
    public IReadOnlyList<CatalogFunction> Functions { get; init; } = Array.Empty<CatalogFunction>();
    public IReadOnlyList<CatalogOperator> Operators { get; init; } = Array.Empty<CatalogOperator>();
    public IReadOnlyList<CatalogType> Types { get; init; } = Array.Empty<CatalogType>();
    public IReadOnlyList<(RefPage Page, string File)> Commands { get; init; } = Array.Empty<(RefPage, string)>();
    public IReadOnlyList<DocFunction> DocFunctions { get; init; } = Array.Empty<DocFunction>();
    public IReadOnlySet<string> DocumentedFunctionNames { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> DocumentedOperatorNames { get; init; } = new HashSet<string>();
    public IReadOnlyList<DocType> DocTypes { get; init; } = Array.Empty<DocType>();
    public IReadOnlyList<SqlStateRecord> SqlStates { get; init; } = Array.Empty<SqlStateRecord>();

    /// <summary>Every section id in <c>syntax.sgml</c>, mapped to the <c>sect1</c> (the manual page) that holds it.</summary>
    public IReadOnlyDictionary<string, string> SyntaxSectionPages { get; init; } = new Dictionary<string, string>();

    public static TagSnapshot Read(ISourceTree tree)
    {
        var commands = new List<(RefPage, string)>();
        foreach (var file in tree.ListFiles("doc/src/sgml/ref", "*.sgml"))
        {
            var page = RefPageReader.Read(tree.ReadText(file) ?? string.Empty);
            if (page != null && page.Id.StartsWith("sql-", StringComparison.Ordinal)) commands.Add((page, file));
        }

        var funcSgml = tree.ReadText("doc/src/sgml/func.sgml") ?? string.Empty;
        var (documentedFunctions, documentedOperators) = FuncSectionIndex.DocumentedNames(funcSgml);

        return new TagSnapshot
        {
            DocumentedFunctionNames = documentedFunctions,
            DocumentedOperatorNames = documentedOperators,
            Keywords = KwlistReader.Read(tree.ReadText("src/include/parser/kwlist.h") ?? string.Empty),
            Functions = ReadFunctions(tree.ReadText("src/include/catalog/pg_proc.dat") ?? string.Empty),
            Operators = ReadOperators(tree.ReadText("src/include/catalog/pg_operator.dat") ?? string.Empty),
            Types = ReadTypes(tree.ReadText("src/include/catalog/pg_type.dat") ?? string.Empty),
            Commands = commands,
            DocFunctions = FuncSectionIndex.Read(funcSgml),
            DocTypes = DatatypeSectionIndex.Read(tree.ReadText("doc/src/sgml/datatype.sgml") ?? string.Empty),
            SqlStates = ErrcodesReader.Read(tree.ReadText("src/backend/utils/errcodes.txt") ?? string.Empty),
            SyntaxSectionPages = SectionPageIndex.Read(tree.ReadText("doc/src/sgml/syntax.sgml") ?? string.Empty),
        };
    }

    // ── pg_proc.dat ─────────────────────────────────────────────────────────────────────────────

    public static IReadOnlyList<CatalogFunction> ReadFunctions(string text)
    {
        var result = new List<CatalogFunction>();
        foreach (var r in DatFileReader.Read(text))
        {
            if (!r.TryGetValue("proname", out var name) || name.Length == 0) continue;
            var kind = r.TryGetValue("prokind", out var k) && k.Length > 0 ? k[0] : 'f';
            var names = BraceList(r.GetValueOrDefault("proargnames"));
            var modes = BraceList(r.GetValueOrDefault("proargmodes"));
            // Without proallargtypes every argument is an input and proargtypes lists them separated by spaces.
            var types = r.TryGetValue("proallargtypes", out var all) && all.Length > 0
                ? BraceList(all)
                : (r.GetValueOrDefault("proargtypes") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

            var args = new List<ReferenceArg>();
            for (var i = 0; i < types.Count; i++)
            {
                var mode = i < modes.Count && modes[i].Length > 0 ? modes[i][0] : 'i';
                args.Add(new ReferenceArg
                {
                    Name = i < names.Count && names[i].Length > 0 ? names[i] : null,
                    Type = types[i],
                    Mode = mode switch { 'o' => "out", 'b' => "inout", 'v' => "variadic", 't' => "table", _ => "in" },
                    HasDefault = false,
                });
            }
            result.Add(new CatalogFunction(name, kind, args, r.GetValueOrDefault("prorettype") ?? string.Empty,
                r.GetValueOrDefault("proretset") == "t", r.GetValueOrDefault("descr") ?? string.Empty));
        }
        return result;
    }

    // '{a,b,c}' -> a, b, c
    private static List<string> BraceList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();
        var inner = value.Trim().Trim('{', '}');
        return inner.Length == 0 ? new List<string>() : inner.Split(',').Select(s => s.Trim().Trim('"')).ToList();
    }

    public static IReadOnlyList<CatalogOperator> ReadOperators(string text)
    {
        var result = new List<CatalogOperator>();
        foreach (var r in DatFileReader.Read(text))
        {
            if (!r.TryGetValue("oprname", out var name) || name.Length == 0) continue;
            string? Side(string key) => r.TryGetValue(key, out var v) && v.Length > 0 && v != "0" && v != "-" ? v : null;
            result.Add(new CatalogOperator(name, Side("oprleft"), Side("oprright"), r.GetValueOrDefault("oprresult") ?? string.Empty,
                r.GetValueOrDefault("descr") ?? string.Empty));
        }
        return result;
    }

    public static IReadOnlyList<CatalogType> ReadTypes(string text)
    {
        var result = new List<CatalogType>();
        foreach (var r in DatFileReader.Read(text))
        {
            if (!r.TryGetValue("typname", out var name) || name.Length == 0) continue;
            var typtype = r.TryGetValue("typtype", out var t) && t.Length > 0 ? t[0] : 'b';
            var category = r.TryGetValue("typcategory", out var c) && c.Length > 0 ? c[0] : 'U';
            result.Add(new CatalogType(name, typtype, category, r.GetValueOrDefault("descr") ?? string.Empty));
        }
        return result;
    }
}

/// <summary>What <see cref="Generator.Build"/> produces, before anything is written.</summary>
public sealed class BuildResult
{
    public ReferenceFile File { get; init; } = new();
    public List<Omission> Omissions { get; init; } = new();
    public Coverage Coverage { get; init; } = new();
}

/// <summary>
/// Spec 042 (T091, contracts/reference-schema.md) - builds the PostgreSQL reference from the sources of each supported release.
/// The newest release named is the documented one: its pages, catalogs and tables make the entries. The older releases are only
/// asked whether they have the same thing, which is what <c>minVersion</c> records.
/// </summary>
public static partial class Generator
{
    public const string Version = "1.0.0";

    /// <summary>Catalog functions that exist for the machinery, not for people to call; the manual does not document them.</summary>
    private static readonly string[] ImplementationPrefixes =
    {
        "I/O", "internal", "implementation of", "aggregate transition function", "aggregate final function",
        "aggregate combine function", "aggregate serial function", "aggregate deserial function",
    };

    [GeneratedRegex(@"REL_?(\d+)")]
    private static partial Regex TagMajor();

    /// <summary>14 for <c>REL_14_STABLE</c>; 0 when the tag has no number.</summary>
    public static int MajorOf(string tag) => TagMajor().Match(tag) is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    /// <summary>The manual link for a page, or for a section nested on it (see <see cref="PgManual"/>).</summary>
    public static string Url(int docMajor, string page, string? section = null) =>
        PgManual.Url(docMajor.ToString(System.Globalization.CultureInfo.InvariantCulture), page, section);

    /// <summary>
    /// The catalogs name types by their internal names (<c>int4</c>, <c>float8</c>, <c>timestamptz</c>, <c>_text</c> for an array);
    /// the manual and psql use the SQL names, which is what a person reads in a signature.
    /// </summary>
    public static string DisplayType(string internalName)
    {
        if (internalName.Length > 1 && internalName[0] == '_') return DisplayType(internalName.Substring(1)) + "[]";
        return internalName switch
        {
            "int2" => "smallint",
            "int4" => "integer",
            "int8" => "bigint",
            "float4" => "real",
            "float8" => "double precision",
            "bool" => "boolean",
            "bpchar" => "character",
            "varchar" => "character varying",
            "timestamptz" => "timestamp with time zone",
            "timetz" => "time with time zone",
            "varbit" => "bit varying",
            _ => internalName,
        };
    }

    public static BuildResult Build(IReadOnlyDictionary<int, TagSnapshot> snapshots, int docMajor, string[] tags)
    {
        var doc = snapshots[docMajor];
        var keysByMajor = snapshots.ToDictionary(s => s.Key, s => (ISet<string>)Keys(s.Value));
        var entries = new List<ReferenceEntry>();
        var omissions = new List<Omission>();
        var coverage = new Coverage { DocVersion = docMajor.ToString() };

        BuildCommands(doc, docMajor, keysByMajor, entries, coverage);
        BuildKeywords(doc, docMajor, keysByMajor, entries, coverage);
        BuildTypes(doc, docMajor, keysByMajor, entries, coverage);
        BuildFunctions(doc, docMajor, keysByMajor, entries, omissions, coverage);
        BuildOperators(doc, docMajor, keysByMajor, entries, omissions, coverage);
        BuildSqlStates(doc, docMajor, keysByMajor, entries, coverage);
        BuildLexical(doc, docMajor, snapshots.Keys.Min(), entries);

        var order = EntryKinds.Order.ToList();
        var sorted = entries
            .OrderBy(e => order.IndexOf(e.Kind))
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();

        return new BuildResult
        {
            File = new ReferenceFile
            {
                Version = docMajor.ToString(),
                GeneratedFrom = new GeneratedFrom { Tags = tags, Generator = Version },
                Entries = sorted,
            },
            Omissions = omissions,
            Coverage = coverage,
        };
    }

    // ── What each release has ───────────────────────────────────────────────────────────────────

    private static HashSet<string> Keys(TagSnapshot s)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var k in s.Keywords) keys.Add(VersionDiffer.Key("kw", k.Name));
        foreach (var (page, _) in s.Commands) foreach (var n in page.Names) keys.Add(VersionDiffer.Key("cmd", n));
        foreach (var f in s.Functions) keys.Add(VersionDiffer.Key("fn", f.Name));
        foreach (var d in s.DocFunctions) keys.Add(VersionDiffer.Key(d.IsOperator ? "op" : "fn", d.Name));
        foreach (var o in s.Operators) keys.Add(VersionDiffer.Key("op", o.Name));
        foreach (var t in s.Types) keys.Add(VersionDiffer.Key("type", t.Name));
        foreach (var t in s.DocTypes)
        {
            keys.Add(VersionDiffer.Key("type", t.Name));
            foreach (var a in t.Aliases) keys.Add(VersionDiffer.Key("type", a));
        }
        foreach (var e in s.SqlStates) keys.Add(VersionDiffer.Key("state", e.Code));
        return keys;
    }

    // ── Commands ────────────────────────────────────────────────────────────────────────────────

    private static void BuildCommands(TagSnapshot doc, int docMajor, Dictionary<int, ISet<string>> keys, List<ReferenceEntry> entries, Coverage coverage)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (page, file) in doc.Commands)
        {
            foreach (var name in page.Names)
            {
                if (!seen.Add(name)) continue;
                entries.Add(new ReferenceEntry
                {
                    Kind = EntryKinds.Command,
                    Name = name,
                    Category = "sql-commands",
                    Summary = page.Purpose.Length > 0 ? page.Purpose : name,
                    Synopsis = page.Synopsis.Length > 0 ? page.Synopsis : null,
                    MinVersion = VersionDiffer.MinVersion(keys, new[] { VersionDiffer.Key("cmd", name) }, docMajor),
                    Source = new ReferenceSource { File = file, SectionId = page.Id },
                    Url = Url(docMajor, page.Id),
                });
            }
        }
        coverage.Commands = new CoverageCount { Documented = seen.Count, Included = seen.Count };
    }

    // ── Keywords ────────────────────────────────────────────────────────────────────────────────

    private static string KeywordSummary(string category) => category switch
    {
        "reserved" => "Reserved key word: it cannot be used as an unquoted table, column, function or type name.",
        "column-name" => "Key word that can be used as a table or column name but not as a function or type name.",
        "type-function-name" => "Key word that can be used as a function or type name but not as a table or column name.",
        _ => "Non-reserved key word: it has a special meaning only in some contexts and can be used as a name.",
    };

    private static void BuildKeywords(TagSnapshot doc, int docMajor, Dictionary<int, ISet<string>> keys, List<ReferenceEntry> entries, Coverage coverage)
    {
        foreach (var k in doc.Keywords)
        {
            entries.Add(new ReferenceEntry
            {
                Kind = EntryKinds.Keyword,
                Name = k.Name.ToUpperInvariant(),
                Category = k.Category,
                Summary = KeywordSummary(k.Category),
                MinVersion = VersionDiffer.MinVersion(keys, new[] { VersionDiffer.Key("kw", k.Name) }, docMajor),
                Source = new ReferenceSource { File = "src/include/parser/kwlist.h", SectionId = "sql-keywords-appendix" },
                Url = Url(docMajor, "sql-keywords-appendix"),
            });
        }
        coverage.Keywords = new CoverageCount { Documented = doc.Keywords.Count, Included = doc.Keywords.Count };
    }

    // ── Types ───────────────────────────────────────────────────────────────────────────────────

    private static string TypeCategory(char c) => c switch
    {
        'A' => "array", 'B' => "boolean", 'C' => "composite", 'D' => "date/time", 'E' => "enum", 'G' => "geometric",
        'I' => "network address", 'N' => "numeric", 'P' => "pseudo", 'R' => "range", 'S' => "string", 'T' => "timespan",
        'U' => "user-defined", 'V' => "bit string", 'X' => "unknown", _ => "other",
    };

    private static void BuildTypes(TagSnapshot doc, int docMajor, Dictionary<int, ISet<string>> keys, List<ReferenceEntry> entries, Coverage coverage)
    {
        var catalog = new Dictionary<string, CatalogType>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in doc.Types) catalog.TryAdd(t.Name, t);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in doc.DocTypes)
        {
            if (!seen.Add(t.Name)) continue;

            var match = catalog.GetValueOrDefault(t.Name) ?? t.Aliases.Select(a => catalog.GetValueOrDefault(a)).FirstOrDefault(c => c != null);
            var aliases = t.Aliases.Where(a => !a.Equals(t.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (match != null && !match.Name.Equals(t.Name, StringComparison.OrdinalIgnoreCase)
                && !aliases.Contains(match.Name, StringComparer.OrdinalIgnoreCase))
                aliases.Add(match.Name);

            var summary = t.Description.Length > 0 ? t.Description : match?.Description ?? string.Empty;
            entries.Add(new ReferenceEntry
            {
                Kind = EntryKinds.Type,
                Name = t.Name,
                Aliases = aliases,
                Category = t.IsPseudo ? "pseudo" : match != null ? TypeCategory(match.Category) : t.SectionId.Replace("datatype-", string.Empty),
                Summary = summary.Length > 0 ? summary : t.Name,
                MinVersion = VersionDiffer.MinVersion(keys, new[] { t.Name }.Concat(t.Aliases).Select(n => VersionDiffer.Key("type", n)), docMajor),
                Source = new ReferenceSource { File = "doc/src/sgml/datatype.sgml", SectionId = t.SectionId },
                Url = Url(docMajor, t.SectionId),
            });
        }
        coverage.Types = new CoverageCount { Documented = seen.Count, Included = seen.Count };
    }

    // ── Functions, aggregates, window functions and procedures ──────────────────────────────────

    private static bool IsImplementation(string description) =>
        ImplementationPrefixes.Any(p => description.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static void BuildFunctions(TagSnapshot doc, int docMajor, Dictionary<int, ISet<string>> keys, List<ReferenceEntry> entries,
        List<Omission> omissions, Coverage coverage)
    {
        var documented = doc.DocFunctions.Where(d => !d.IsOperator)
            .GroupBy(d => d.Name.ToLowerInvariant(), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        var catalog = doc.Functions.GroupBy(f => f.Name.ToLowerInvariant(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var group in documented)
        {
            var name = group.Key;
            var docs = group.ToList();
            var first = docs[0];
            catalog.TryGetValue(name, out var overloads);
            var real = overloads?.Where(o => !IsImplementation(o.Description)).ToList();
            if (real is { Count: 0 }) real = overloads;

            string kind;
            List<ReferenceSignature> signatures;
            string file;
            if (real is { Count: > 0 })
            {
                kind = real.Any(o => o.Kind == 'a') ? EntryKinds.Aggregate
                    : real.Any(o => o.Kind == 'w') ? EntryKinds.Window
                    : real.Any(o => o.Kind == 'p') ? EntryKinds.Procedure
                    : EntryKinds.Function;
                signatures = real.Select(o => new ReferenceSignature
                {
                    Args = o.Args.Select(a => new ReferenceArg { Name = a.Name, Type = DisplayType(a.Type), Mode = a.Mode, HasDefault = a.HasDefault }).ToList(),
                    Returns = DisplayType(o.Returns),
                    IsSetReturning = o.SetOf,
                }).ToList();
                file = "src/include/catalog/pg_proc.dat";
            }
            else
            {
                // SQL syntax that looks like a function (COALESCE, EXTRACT, ...) has no catalog row: the manual's own signature stands.
                kind = EntryKinds.Function;
                signatures = docs.Select(d => new ReferenceSignature { Args = DocArguments(d.Arguments), Returns = d.Returns.Length > 0 ? d.Returns : null })
                    .GroupBy(s => string.Join(",", s.Args.Select(a => a.Type)) + "->" + s.Returns).Select(g => g.First()).ToList();
                file = "doc/src/sgml/func.sgml";
            }

            // A table row's description is the manual's own words. One guessed from the prose after a synopsis is used only when it is
            // about this function; otherwise the catalog's one-line description is the better summary.
            var mentions = new Regex(@"\b" + Regex.Escape(name) + @"\b", RegexOptions.IgnoreCase);
            var summary = docs.Where(d => d.Description.Length > 0 && (!d.FromProse || mentions.IsMatch(d.Description))).Select(d => d.Description).FirstOrDefault()
                ?? real?.Select(o => o.Description).FirstOrDefault(d => d.Length > 0 && !IsImplementation(d))
                ?? docs.Select(d => d.Description).FirstOrDefault(d => d.Length > 0)
                ?? "Built-in function described in the PostgreSQL manual.";

            entries.Add(new ReferenceEntry
            {
                Kind = kind,
                Name = name,
                Category = first.SectionId,
                Summary = summary,
                Signatures = signatures,
                MinVersion = VersionDiffer.MinVersion(keys, new[] { VersionDiffer.Key("fn", name) }, docMajor),
                Source = new ReferenceSource { File = file, SectionId = first.SectionId },
                Url = Url(docMajor, first.SectionId),
            });
        }
        var functionNames = new HashSet<string>(entries.Where(e => e.Kind is EntryKinds.Function or EntryKinds.Aggregate or EntryKinds.Window or EntryKinds.Procedure)
            .Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
        coverage.Functions = new CoverageCount
        {
            Documented = doc.DocumentedFunctionNames.Count,
            Included = doc.DocumentedFunctionNames.Count(functionNames.Contains),
        };

        // The catalog also holds thousands of functions the manual does not document. They are not in the reference; say how many.
        var documentedNames = new HashSet<string>(documented.Select(g => g.Key), StringComparer.Ordinal);
        var undocumented = catalog.Keys.Count(n => !documentedNames.Contains(n));
        if (undocumented > 0)
            omissions.Add(new Omission("function", $"({undocumented} distinct names)",
                "in the catalog but not in the manual's function reference: internal, type-support and access-method functions"));
    }

    // Splits "a, b [, c ...]" at top-level commas into the manual's own argument descriptions. Brackets are the manual's way to say
    // optional, an ellipsis that the last argument may repeat. These signatures are read for syntax-like functions (COALESCE,
    // EXTRACT, JSON_EXISTS) that have no catalog row, so the text is a placeholder or phrase, not always a type name.
    private static List<ReferenceArg> DocArguments(string arguments)
    {
        var result = new List<ReferenceArg>();
        var parentheses = 0;
        var brackets = 0;
        var optional = false;
        var current = new StringBuilder();
        foreach (var c in arguments)
        {
            if (c == '[') { brackets++; continue; }
            if (c == ']') { brackets = Math.Max(0, brackets - 1); continue; }
            if (c == '(') parentheses++;
            else if (c == ')') parentheses--;
            if (c == ',' && parentheses == 0) { Flush(); continue; }
            if (current.Length == 0)
            {
                if (char.IsWhiteSpace(c)) continue;
                optional = brackets > 0;
            }
            current.Append(c);
        }
        Flush();
        return result;

        void Flush()
        {
            var text = Regex.Replace(current.ToString(), @"\s+", " ").Trim();
            current.Clear();
            var repeats = text.EndsWith("...", StringComparison.Ordinal);
            if (repeats) text = text.Substring(0, text.Length - 3).Trim();
            if (text.Length > 0) result.Add(new ReferenceArg { Type = text, HasDefault = optional, Mode = repeats ? "variadic" : "in" });
            else if (repeats && result.Count > 0) result[^1].Mode = "variadic";
        }
    }

    // ── Operators ───────────────────────────────────────────────────────────────────────────────

    private static void BuildOperators(TagSnapshot doc, int docMajor, Dictionary<int, ISet<string>> keys, List<ReferenceEntry> entries,
        List<Omission> omissions, Coverage coverage)
    {
        var documented = doc.DocFunctions.Where(d => d.IsOperator)
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var catalog = doc.Operators.GroupBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var group in documented)
        {
            var name = group.Key;
            var docs = group.ToList();
            var first = docs[0];
            catalog.TryGetValue(name, out var overloads);

            List<ReferenceSignature> signatures;
            string file;
            if (overloads is { Count: > 0 })
            {
                signatures = overloads.Select(o => new ReferenceSignature
                {
                    Args = new[] { o.Left, o.Right }.Where(t => t != null).Select(t => new ReferenceArg { Type = DisplayType(t!) }).ToList(),
                    Returns = DisplayType(o.Result),
                }).ToList();
                file = "src/include/catalog/pg_operator.dat";
            }
            else
            {
                // AND, OR, NOT, IS and the like are keywords with operator syntax: read the sides off the manual's signature.
                signatures = docs.Select(d => OperatorFromDoc(d, name)).ToList();
                file = "doc/src/sgml/func.sgml";
            }

            var summary = docs.Select(d => d.Description).FirstOrDefault(d => d.Length > 0)
                ?? overloads?.Select(o => o.Description).FirstOrDefault(d => d.Length > 0)
                ?? "Operator described in the PostgreSQL manual.";

            entries.Add(new ReferenceEntry
            {
                Kind = EntryKinds.Operator,
                Name = name,
                Category = first.SectionId,
                Summary = summary,
                Signatures = signatures,
                MinVersion = VersionDiffer.MinVersion(keys, new[] { VersionDiffer.Key("op", name) }, docMajor),
                Source = new ReferenceSource { File = file, SectionId = first.SectionId },
                Url = Url(docMajor, first.SectionId),
            });
        }
        var operatorNames = new HashSet<string>(entries.Where(e => e.Kind == EntryKinds.Operator).Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
        coverage.Operators = new CoverageCount
        {
            Documented = doc.DocumentedOperatorNames.Count,
            Included = doc.DocumentedOperatorNames.Count(operatorNames.Contains),
        };

        var documentedNames = new HashSet<string>(documented.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
        var undocumented = catalog.Keys.Count(n => !documentedNames.Contains(n));
        if (undocumented > 0)
            omissions.Add(new Omission("operator", $"({undocumented} distinct names)",
                "in the catalog but not in the manual's operator tables: internal and extension-support operators"));
    }

    private static ReferenceSignature OperatorFromDoc(DocFunction doc, string name)
    {
        var at = doc.Signature.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        var args = new List<ReferenceArg>();
        if (at >= 0)
        {
            var left = doc.Signature.Substring(0, at).Trim();
            var right = doc.Signature.Substring(at + name.Length).Trim();
            if (left.Length > 0) args.Add(new ReferenceArg { Type = left });
            if (right.Length > 0) args.Add(new ReferenceArg { Type = right });
        }
        return new ReferenceSignature { Args = args, Returns = doc.Returns.Length > 0 ? doc.Returns : null };
    }

    // ── SQLSTATE codes ──────────────────────────────────────────────────────────────────────────

    private static void BuildSqlStates(TagSnapshot doc, int docMajor, Dictionary<int, ISet<string>> keys, List<ReferenceEntry> entries, Coverage coverage)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in doc.SqlStates)
        {
            if (!seen.Add(s.Code)) continue;
            var words = s.Condition.Replace('_', ' ');
            entries.Add(new ReferenceEntry
            {
                Kind = EntryKinds.SqlState,
                Name = s.Code,
                Aliases = new List<string> { s.Condition },
                Category = s.ClassName.Length > 0 ? $"Class {s.ClassCode} - {s.ClassName}" : "sqlstate",
                Summary = char.ToUpperInvariant(words[0]) + words.Substring(1),
                MinVersion = VersionDiffer.MinVersion(keys, new[] { VersionDiffer.Key("state", s.Code) }, docMajor),
                Source = new ReferenceSource { File = "src/backend/utils/errcodes.txt", SectionId = "errcodes-appendix" },
                Url = Url(docMajor, "errcodes-appendix"),
            });
        }
        coverage.SqlState = new CoverageCount { Documented = doc.SqlStates.Select(s => s.Code).Distinct().Count(), Included = seen.Count };
    }

    // ── Lexical rules ───────────────────────────────────────────────────────────────────────────

    private static void BuildLexical(TagSnapshot doc, int docMajor, int oldestMajor, List<ReferenceEntry> entries)
    {
        foreach (var (name, aliases, summary, sectionId) in LexicalEntries.All)
        {
            // The page comes from syntax.sgml, not from a list kept by hand: a nested section has no page of its own, and a
            // link built from its id alone is a 404. A section the manual does not have stops the run.
            if (!doc.SyntaxSectionPages.TryGetValue(sectionId, out var page))
                throw new InvalidDataException("Lexical entry '" + name + "': section '" + sectionId + "' is not in doc/src/sgml/syntax.sgml.");

            entries.Add(new ReferenceEntry
            {
                Kind = EntryKinds.Lexical,
                Name = name,
                Aliases = aliases.ToList(),
                Category = "lexical",
                Summary = summary,
                MinVersion = oldestMajor,
                Source = new ReferenceSource { File = "doc/src/sgml/syntax.sgml", SectionId = sectionId },
                Url = Url(docMajor, page, sectionId),
            });
        }
    }
}
