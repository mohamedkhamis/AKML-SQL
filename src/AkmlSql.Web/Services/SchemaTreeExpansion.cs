using System;
using System.Collections.Generic;

namespace AkmlSql.Web.Services;

/// <summary>
/// The schema explorer's open folders, per server and database, for the life of the browser tab. The explorer is created
/// again on every reconnect and every visit to the editor page; without this it came back collapsed each time.
/// </summary>
public sealed class SchemaTreeExpansion
{
    private readonly Dictionary<string, HashSet<string>> _byTarget = new(StringComparer.Ordinal);

    /// <summary>The live set of open paths for a target (the explorer adds and removes in place). A new target starts with
    /// its database node open.</summary>
    public HashSet<string> For(string server, string database)
    {
        var key = server + "\u001f" + database;
        if (!_byTarget.TryGetValue(key, out var open))
        {
            open = new HashSet<string>(StringComparer.Ordinal) { database };
            _byTarget[key] = open;
        }
        return open;
    }
}
