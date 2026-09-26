using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AkmlSql.Formatting.SqlPrompt;

/// <summary>
/// A SQL Prompt formatting style in SQL Prompt's own JSON shape — the same document SQL Prompt
/// 10.5+ reads and writes (<c>{"metadata":{…},"whitespace":{…},"lists":{…},…}</c>).
/// <para>
/// This is what the style editors edit and what a style stores. Like SQL Prompt's own files it
/// is kept minimal: an option at its default is left out of the JSON. Keys this build does not
/// know (options from a newer SQL Prompt) are kept as they are, so nothing is lost on a round trip.
/// </para>
/// </summary>
public sealed class SqlPromptStyleDocument
{
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions WriteIndented = new() { WriteIndented = true };

    public JsonObject Root { get; }

    private SqlPromptStyleDocument(JsonObject root) => Root = root;

    /// <summary>A new style with every option at SQL Prompt's default.</summary>
    public static SqlPromptStyleDocument CreateDefault(string name, string? id = null)
    {
        var doc = new SqlPromptStyleDocument(new JsonObject());
        doc.Id = id ?? Guid.NewGuid().ToString();
        doc.Name = name;
        return doc;
    }

    /// <summary>Parses a SQL Prompt style. Throws <see cref="JsonException"/> when the text is not a JSON object.</summary>
    public static SqlPromptStyleDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        // SQL Prompt writes UTF-8 with a BOM; a BOM that survived decoding is not JSON.
        var node = JsonNode.Parse(json.TrimStart('\uFEFF'), documentOptions: ReadOptions);
        if (node is not JsonObject root)
            throw new JsonException("A SQL Prompt style must be a JSON object.");
        return new SqlPromptStyleDocument(root);
    }

    public static bool TryParse(string json, out SqlPromptStyleDocument? document, out string? error)
    {
        try
        {
            document = Parse(json);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            document = null;
            error = ex.Message;
            return false;
        }
    }

    public static SqlPromptStyleDocument FromNode(JsonObject root) =>
        new((JsonObject)root.DeepClone());

    public SqlPromptStyleDocument Clone() => new((JsonObject)Root.DeepClone());

    // ── metadata ─────────────────────────────────────────────────────────────

    public string Name
    {
        get => (Root["metadata"] as JsonObject)?["name"]?.GetValue<string>() ?? string.Empty;
        set => Metadata()["name"] = value;
    }

    public string Id
    {
        get => (Root["metadata"] as JsonObject)?["id"]?.GetValue<string>() ?? string.Empty;
        set => Metadata()["id"] = value;
    }

    private JsonObject Metadata()
    {
        if (Root["metadata"] is JsonObject m) return m;
        m = new JsonObject();
        // metadata first, the way SQL Prompt writes it
        var rest = Root.ToList();
        Root.Clear();
        Root["metadata"] = m;
        foreach (var (k, v) in rest) Root[k] = v;
        return m;
    }

    // ── options ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The option's value as its JSON literal text (<c>true</c>, <c>4</c>, <c>spaces</c>) — the
    /// stored value when it is present and valid, otherwise SQL Prompt's default.
    /// </summary>
    public string Get(string path)
    {
        var option = SqlPromptOptionCatalog.Find(path)
            ?? throw new ArgumentException($"'{path}' is not a SQL Prompt formatting option.", nameof(path));
        var stored = Normalize(option, Lookup(path));
        if (stored is not null) return stored;
        // SQL Prompt 11 writes a collapse threshold without its switch when the collapse is on
        // (spec 031 FR-003): a threshold with no switch means the collapse is enabled.
        if (CollapseThresholdFor(option.Path) is { } threshold && Lookup(threshold) is not null) return "true";
        return option.Default;
    }

    /// <summary>
    /// The collapse switch → threshold pairs ("collapse short statements" / "…shorter than").
    /// A threshold written without its switch turns the collapse on; see <see cref="Get"/>.
    /// </summary>
    private static readonly Dictionary<string, string> CollapseThresholds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dml.collapseShortStatements"] = "dml.collapseStatementsShorterThan",
        ["dml.collapseShortSubqueries"] = "dml.collapseSubqueriesShorterThan",
        ["ddl.collapseShortStatements"] = "ddl.collapseStatementsShorterThan",
        ["controlFlow.collapseShortStatements"] = "controlFlow.collapseStatementsShorterThan",
        ["parentheses.collapseShortParenthesisContents"] = "parentheses.collapseParenthesesShorterThan",
        ["caseExpressions.collapseShortCaseExpressions"] = "caseExpressions.collapseCaseExpressionsShorterThan",
    };

    private static string? CollapseThresholdFor(string switchPath) => CollapseThresholds.GetValueOrDefault(switchPath);

    public bool GetBool(string path) => Get(path) == "true";

    public int GetInt(string path) => int.Parse(Get(path), CultureInfo.InvariantCulture);

    /// <summary>True when the option is written in the document (even if it equals the default).</summary>
    public bool IsSet(string path) => Lookup(path) is not null;

    /// <summary>
    /// Sets an option from its JSON literal text. Choices are matched case-insensitively and stored
    /// in Redgate's spelling; integers are clamped to the option's range. A value equal to the
    /// default removes the key, as SQL Prompt does.
    /// </summary>
    /// <exception cref="ArgumentException">Unknown option, or a value the option cannot take.</exception>
    public void Set(string path, string value)
    {
        var option = SqlPromptOptionCatalog.Find(path)
            ?? throw new ArgumentException($"'{path}' is not a SQL Prompt formatting option.", nameof(path));
        var normalized = Normalize(option, JsonValue.Create(value))
            ?? throw new ArgumentException($"'{value}' is not a valid value for {option.Path}.", nameof(value));

        // A switched-off collapse whose threshold is written must say "false" out loud, or the
        // threshold alone would read as switched on (see Get).
        var keepExplicit = normalized == "false" && CollapseThresholdFor(option.Path) is { } threshold && Lookup(threshold) is not null;
        if (normalized == option.Default && !keepExplicit)
        {
            Remove(option.Path);
            return;
        }
        Write(option, normalized);
    }

    /// <summary>
    /// Every option written out with its effective value — nothing left for a reader to infer
    /// (defaults, the collapse-threshold rule). What editors that cannot load this library merge
    /// their edits into. Metadata and keys this build does not know are kept.
    /// </summary>
    public string ToExplicitJson(bool indented = true)
    {
        var copy = Clone();
        foreach (var option in SqlPromptOptionCatalog.Options) copy.Write(option, Get(option.Path));
        return copy.ToJson(indented);
    }

    /// <summary>Drops options written at their default: SQL Prompt's own minimal form. Formats the same.</summary>
    public void Minimize()
    {
        var effective = SqlPromptOptionCatalog.Options.Select(o => (o.Path, Value: Get(o.Path))).ToList();
        foreach (var (path, value) in effective) Set(path, value);
    }

    private void Write(SqlPromptOption option, string normalized)
    {
        JsonNode node = option.Kind switch
        {
            SqlPromptOptionKind.Boolean => JsonValue.Create(normalized == "true"),
            SqlPromptOptionKind.Integer => JsonValue.Create(int.Parse(normalized, CultureInfo.InvariantCulture)),
            _ => JsonValue.Create(normalized),
        };
        var parts = option.Path.Split('.');
        var parent = Root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parent[parts[i]] is not JsonObject child)
            {
                child = new JsonObject();
                parent[parts[i]] = child;
            }
            parent = child;
        }
        // A differently-cased copy of the key would otherwise shadow the new value on read.
        foreach (var stale in parent.Select(kv => kv.Key)
                     .Where(k => k != parts[^1] && string.Equals(k, parts[^1], StringComparison.OrdinalIgnoreCase)).ToList())
            parent.Remove(stale);
        parent[parts[^1]] = node;
    }

    /// <summary>Returns an option to SQL Prompt's default.</summary>
    public void Reset(string path)
    {
        var option = SqlPromptOptionCatalog.Find(path)
            ?? throw new ArgumentException($"'{path}' is not a SQL Prompt formatting option.", nameof(path));
        Set(option.Path, option.Default);
    }

    /// <summary>Options whose value differs from SQL Prompt's default.</summary>
    public IEnumerable<SqlPromptOption> ChangedOptions() =>
        SqlPromptOptionCatalog.Options.Where(o => Get(o.Path) != o.Default);

    /// <summary>Keys in the document that are not SQL Prompt options this build knows (kept on save).</summary>
    public IReadOnlyList<string> UnknownKeys()
    {
        var unknown = new List<string>();
        void Walk(JsonObject obj, string prefix)
        {
            foreach (var (key, value) in obj)
            {
                var path = prefix.Length == 0 ? key : prefix + "." + key;
                if (path == "metadata") continue;
                if (value is JsonObject child && SqlPromptOptionCatalog.Find(path) is null) Walk(child, path);
                else if (SqlPromptOptionCatalog.Find(path) is null) unknown.Add(path);
            }
        }
        Walk(Root, string.Empty);
        return unknown;
    }

    public string ToJson(bool indented = true) =>
        indented ? Root.ToJsonString(WriteIndented) : Root.ToJsonString();

    /// <summary>True when both documents describe the same formatting (metadata ignored).</summary>
    public bool FormatsLike(SqlPromptStyleDocument other) =>
        SqlPromptOptionCatalog.Options.All(o => Get(o.Path) == other.Get(o.Path));

    // ── helpers ──────────────────────────────────────────────────────────────

    private JsonNode? Lookup(string path)
    {
        JsonNode? node = Root;
        foreach (var part in path.Split('.'))
        {
            if (node is not JsonObject obj) return null;
            // Case-insensitive, like SQL Prompt's reader; exact match first.
            node = obj[part] ?? obj.FirstOrDefault(kv => string.Equals(kv.Key, part, StringComparison.OrdinalIgnoreCase)).Value;
        }
        return node;
    }

    private void Remove(string path)
    {
        var parts = path.Split('.');
        var chain = new List<JsonObject> { Root };
        JsonObject current = Root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (current[parts[i]] is not JsonObject child) return;
            chain.Add(child);
            current = child;
        }
        current.Remove(parts[^1]);

        // Drop sections the removal left empty, innermost first.
        for (var i = chain.Count - 1; i > 0; i--)
        {
            if (chain[i].Count != 0) break;
            chain[i - 1].Remove(parts[i - 1]);
        }
    }

    /// <summary>The value in canonical form, or null when it is missing or not a value the option can take.</summary>
    internal static string? Normalize(SqlPromptOption option, JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        switch (option.Kind)
        {
            case SqlPromptOptionKind.Boolean:
                if (value.TryGetValue<bool>(out var b)) return b ? "true" : "false";
                if (value.TryGetValue<string>(out var bs) && bool.TryParse(bs, out b)) return b ? "true" : "false";
                return null;

            case SqlPromptOptionKind.Integer:
                int n;
                if (value.TryGetValue<int>(out n)) { }
                else if (value.TryGetValue<double>(out var d) && d == Math.Floor(d) && Math.Abs(d) < int.MaxValue) n = (int)d;
                else if (value.TryGetValue<string>(out var s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) { }
                else return null;
                if (option.Min is { } min && n < min) n = min;
                if (option.Max is { } max && n > max) n = max;
                return n.ToString(CultureInfo.InvariantCulture);

            default:
                if (!value.TryGetValue<string>(out var text)) return null;
                // Redgate shipped one build that wrote "intentedFromWhen"; read it as the real value.
                if (option.Path == "caseExpressions.thenAlignment" && string.Equals(text, "intentedFromWhen", StringComparison.OrdinalIgnoreCase))
                    text = "indentedFromWhen";
                return option.Choices.FirstOrDefault(c => string.Equals(c.Value, text, StringComparison.OrdinalIgnoreCase))?.Value;
        }
    }
}
