using System.Text.Json;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Web.Tests.Parity;

/// <summary>
/// Spec 024 T005 — walks <c>tests/format-parity/corpus/*.sql</c>, reads the matching
/// <c>baselines/&lt;profile&gt;/&lt;id&gt;.expected.sql</c> + <c>baselines/default/&lt;id&gt;.expected.json</c>
/// produced by <see cref="ParityBaselineGenerator"/>, and validates the IDE-plugin
/// build version stamped in each baseline against <c>tests/format-parity/ide-plugin-version.txt</c>.
///
/// The web edition ships TWO built-in profiles (<c>builtin.default</c> + <c>builtin.ansi</c>);
/// the loader's <see cref="ProfileIds"/> reflects that. FR-007's "≥ 3 profiles" is therefore
/// implemented as ≥ 2 (the actual profile zoo) — recorded as a deviation in
/// <c>specs/024-m2-web-closure/tasks.md</c>.
/// </summary>
public static class ParityCorpusLoader
{
    /// <summary>Profile ids the parity tests cover. Matches IProfileStore's built-ins.</summary>
    public static readonly string[] ProfileIds = { "default", "ansi" };

    /// <summary>
    /// Constructs the FormattingProfile the web edition would resolve for the given id.
    /// Mirrors IProfileStore.BuildBuiltInProfiles() so the parity test, the baseline
    /// generator, and the runtime resolve the same profile object.
    /// </summary>
    public static FormattingProfile GetProfile(string profileId)
    {
        switch (profileId)
        {
            case "default":
            {
                var p = new FormattingProfile();
                p.Metadata.Name = "AKML Default";
                return p;
            }
            case "ansi":
            {
                var p = new FormattingProfile();
                p.Metadata.Name = "ANSI-compact";
                p.Casing.ReservedKeywords = "uppercase";
                return p;
            }
            default:
                throw new ArgumentException($"Unknown profile id '{profileId}'. Expected one of: {string.Join(", ", ProfileIds)}", nameof(profileId));
        }
    }

    /// <summary>Normalise text to LF endings + trailing newline per parity-baseline-format.md.</summary>
    public static string NormaliseLineEndings(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var lf = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return lf.EndsWith('\n') ? lf : lf + "\n";
    }

    /// <summary>The IDE-plugin build version every baseline file must match.</summary>
    public static string CurrentIdePluginVersion => _lazyVersion.Value;

    private static readonly Lazy<string> _lazyVersion = new(() =>
    {
        var versionFile = Path.Combine(RepoRoot(), "tests", "format-parity", "ide-plugin-version.txt");
        if (!File.Exists(versionFile))
        {
            throw new FileNotFoundException(
                $"IDE-plugin version file missing: {versionFile}. " +
                "Run the ParityBaselineGenerator with AKML_REGEN_PARITY_BASELINE=1 to (re)produce baselines + this version stamp.");
        }
        return File.ReadAllText(versionFile).Trim();
    });

    /// <summary>Enumerates every corpus item × profile pair the parity tests should cover.</summary>
    public static IEnumerable<(string CorpusId, string ProfileId)> EnumerateFormatterPairs()
    {
        foreach (var (id, _) in EnumerateCorpus())
        {
            foreach (var profile in ProfileIds)
            {
                yield return (id, profile);
            }
        }
    }

    /// <summary>Enumerates every corpus item for the analyser parity test (analyser is profile-independent).</summary>
    public static IEnumerable<string> EnumerateAnalyserItems()
    {
        foreach (var (id, _) in EnumerateCorpus())
        {
            yield return id;
        }
    }

    /// <summary>Yields <c>(corpusId, absoluteSqlPath)</c> for every <c>tests/format-parity/corpus/*.sql</c>.</summary>
    public static IEnumerable<(string Id, string SqlPath)> EnumerateCorpus()
    {
        var corpusDir = CorpusDirectory();
        foreach (var sqlPath in Directory.EnumerateFiles(corpusDir, "*.sql").OrderBy(p => p, StringComparer.Ordinal))
        {
            yield return (Path.GetFileNameWithoutExtension(sqlPath), sqlPath);
        }
    }

    /// <summary>Reads the corpus .sql input for the given id.</summary>
    public static string LoadInputSql(string corpusId)
    {
        var path = Path.Combine(CorpusDirectory(), corpusId + ".sql");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Corpus input missing: {path}");
        }
        return File.ReadAllText(path);
    }

    /// <summary>
    /// Loads a formatter baseline: strips the marker line per
    /// <c>contracts/parity-baseline-format.md</c>, validates the IDE-build stamp, and returns the formatted body.
    /// Throws if the file is missing or the stamp does not match.
    /// </summary>
    public static string LoadFormatterBaseline(string corpusId, string profileId)
    {
        var path = Path.Combine(BaselinesDirectory(), profileId, corpusId + ".expected.sql");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Formatter baseline missing: {path}. " +
                "Run AKML_REGEN_PARITY_BASELINE=1 dotnet test --filter \"Category=ParityBaseline\" to produce it.");
        }

        var raw = File.ReadAllText(path);
        var firstNewline = raw.IndexOf('\n');
        if (firstNewline < 0)
        {
            throw new InvalidDataException($"Baseline {path} has no marker line");
        }
        var markerLine = raw[..firstNewline].TrimEnd('\r');

        var expectedMarker = $"-- akml-parity-baseline ide-build={CurrentIdePluginVersion} corpus-item={corpusId} profile={profileId}";
        if (markerLine != expectedMarker)
        {
            throw new InvalidDataException(
                $"Baseline marker mismatch in {path}.\n" +
                $"  Expected: {expectedMarker}\n" +
                $"  Actual:   {markerLine}\n" +
                "Regenerate baselines: AKML_REGEN_PARITY_BASELINE=1 dotnet test --filter \"Category=ParityBaseline\"");
        }

        return raw[(firstNewline + 1)..];
    }

    /// <summary>
    /// Loads an analyser baseline: parses the <c>akmlParityBaseline</c> + <c>findings</c> envelope per
    /// <c>contracts/parity-baseline-format.md</c>, validates the IDE-build stamp, and returns the sorted findings.
    /// </summary>
    public static ParityFinding[] LoadAnalyserBaseline(string corpusId)
    {
        var path = Path.Combine(BaselinesDirectory(), "default", corpusId + ".expected.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Analyser baseline missing: {path}. " +
                "Run AKML_REGEN_PARITY_BASELINE=1 dotnet test --filter \"Category=ParityBaseline\" to produce it.");
        }

        var doc = JsonSerializer.Deserialize<ParityBaselineEnvelope>(
            File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException($"Baseline {path} did not deserialise");

        if (doc.AkmlParityBaseline is null || doc.AkmlParityBaseline.IdeBuild != CurrentIdePluginVersion)
        {
            throw new InvalidDataException(
                $"Baseline IDE-build mismatch in {path}.\n" +
                $"  Expected: {CurrentIdePluginVersion}\n" +
                $"  Actual:   {doc.AkmlParityBaseline?.IdeBuild ?? "(missing)"}\n" +
                "Regenerate baselines: AKML_REGEN_PARITY_BASELINE=1 dotnet test --filter \"Category=ParityBaseline\"");
        }

        return doc.Findings ?? Array.Empty<ParityFinding>();
    }

    internal static string CorpusDirectory() => Path.Combine(RepoRoot(), "tests", "format-parity", "corpus");

    internal static string BaselinesDirectory() => Path.Combine(RepoRoot(), "tests", "format-parity", "baselines");

    /// <summary>Walks up from the test output directory to the repo root marked by AKML-SQL.slnx.</summary>
    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new InvalidOperationException("Repo root (AKML-SQL.slnx) not found above test output dir");
        }
        return dir.FullName;
    }

    public sealed record ParityFinding(string RuleId, string Severity, string Message, int Line, int Column);

    private sealed class ParityBaselineEnvelope
    {
        public ParityBaselineHeader? AkmlParityBaseline { get; set; }
        public ParityFinding[]? Findings { get; set; }
    }

    private sealed class ParityBaselineHeader
    {
        public string IdeBuild { get; set; } = string.Empty;
        public string CorpusItem { get; set; } = string.Empty;
        public string Profile { get; set; } = string.Empty;
    }
}
