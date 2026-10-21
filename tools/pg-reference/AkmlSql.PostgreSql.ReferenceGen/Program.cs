namespace AkmlSql.PostgreSql.ReferenceGen;

/// <summary>
/// Spec 042 (research R17, contracts/reference-schema.md). <c>gen --source &lt;parent&gt; --tags REL_14_STABLE,... --doc-version 18 --out &lt;repo root&gt;</c>
/// reads a PostgreSQL source checkout per tag and writes the reference the product ships.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }
        if (args[0] != "gen")
        {
            Console.Error.WriteLine($"Unknown command '{args[0]}'.\n{Usage}");
            return 2;
        }

        try
        {
            var options = Options.Parse(args.Skip(1).ToArray());
            var snapshots = new Dictionary<int, TagSnapshot>();
            foreach (var tag in options.Tags)
            {
                var major = Generator.MajorOf(tag);
                if (major == 0) throw new ArgumentException($"Cannot find a release number in the tag '{tag}'.");
                var directory = ResolveTree(options.Source, tag, major)
                    ?? throw new DirectoryNotFoundException($"No source tree for {tag} under {options.Source}: looked for '{tag}', 'pg_{tag}' and 'pg{major}'.");
                Console.WriteLine($"{tag}: reading {directory}");
                snapshots[major] = TagSnapshot.Read(new DirectorySourceTree(directory));
            }
            if (!snapshots.ContainsKey(options.DocVersion))
                throw new ArgumentException($"--doc-version {options.DocVersion} is not among the tags.");

            var result = Generator.Build(snapshots, options.DocVersion, options.Tags);
            foreach (var path in ReferenceWriter.WriteAll(options.Out, result)) Console.WriteLine($"wrote {path}");
            Console.WriteLine($"{result.File.Entries.Count} entries; {result.Omissions.Count} omissions.");
            return 0;
        }
        catch (Exception e) when (e is ArgumentException or IOException)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
    }

    private static string? ResolveTree(string source, string tag, int major)
    {
        foreach (var name in new[] { tag, "pg_" + tag, "pg" + major })
        {
            var path = Path.Combine(source, name);
            if (File.Exists(Path.Combine(path, "src", "include", "parser", "kwlist.h"))) return path;
        }
        return null;
    }

    private const string Usage =
        "usage: pg-reference gen --source <parent directory> --tags REL_14_STABLE,REL_15_STABLE,... --doc-version 18 --out <repository root>\n" +
        "  --source       a directory holding one checkout per tag (named <tag>, pg_<tag> or pg<major>)\n" +
        "  --tags         the supported releases, oldest first\n" +
        "  --doc-version  the release whose manual the entries are read from and linked to\n" +
        "  --out          the repository root the four outputs are written under";

    private sealed record Options(string Source, string[] Tags, int DocVersion, string Out)
    {
        public static Options Parse(string[] args)
        {
            string? source = null, tags = null, doc = null, output = null;
            for (var i = 0; i < args.Length; i++)
            {
                var option = args[i];
                string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{option} needs a value.");
                switch (option)
                {
                    case "--source": source = Value(); break;
                    case "--tags": tags = Value(); break;
                    case "--doc-version": doc = Value(); break;
                    case "--out": output = Value(); break;
                    default: throw new ArgumentException($"Unknown option '{option}'.");
                }
            }
            if (source == null || tags == null || doc == null || output == null)
                throw new ArgumentException("--source, --tags, --doc-version and --out are all required.");
            if (!int.TryParse(doc, out var docVersion)) throw new ArgumentException("--doc-version must be a number such as 18.");
            var tagList = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new Options(Path.GetFullPath(source), tagList, docVersion, Path.GetFullPath(output));
        }
    }
}
