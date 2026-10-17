namespace AkmlSql.PostgreSql.ReferenceGen;

/// <summary>
/// Spec 042 (FR-052) - when did an entry first appear? Each supported release is read on its own; an entry's
/// <c>minVersion</c> is the oldest supported major whose catalog or documentation has it. An entry only the newest release has
/// is "since" that release; one every release has is simply the oldest supported major.
/// </summary>
public static class VersionDiffer
{
    /// <summary>The key a thing is looked up by: the kind family and the lower-cased name.</summary>
    public static string Key(string family, string name) => family + ":" + name.Trim().ToLowerInvariant();

    /// <param name="keysByMajor">For each supported major, the keys present in that release.</param>
    /// <param name="candidates">The keys the entry can be found under (its name and aliases).</param>
    /// <param name="docMajor">The documented release, the answer when no release has the entry (it was found elsewhere).</param>
    public static int MinVersion(IReadOnlyDictionary<int, ISet<string>> keysByMajor, IEnumerable<string> candidates, int docMajor)
    {
        var keys = candidates.ToList();
        foreach (var major in keysByMajor.Keys.OrderBy(m => m))
        {
            var present = keysByMajor[major];
            if (keys.Any(present.Contains)) return major;
        }
        return docMajor;
    }
}
