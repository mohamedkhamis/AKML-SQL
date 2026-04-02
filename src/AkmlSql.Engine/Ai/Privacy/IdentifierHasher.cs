using System.Security.Cryptography;
using System.Text;
using AkmlSql.Core.Models.Ai;

namespace AkmlSql.Engine.Ai.Privacy;

/// <summary>
/// HMAC-SHA256 based deterministic identifier hashing with per-session keys.
/// <para>
/// Each session generates a fresh 32-byte random key. The same identifier always produces
/// the same hash within a session but a different hash across sessions, preventing
/// cross-session correlation.
/// </para>
/// <para>
/// Hashed identifiers include a category prefix (<c>t_</c> for table, <c>c_</c> for column,
/// <c>s_</c> for schema, <c>p_</c> for procedure) followed by the first 8 bytes of
/// the HMAC as hex (16 characters).
/// </para>
/// </summary>
public sealed class IdentifierHasher : IDisposable
{
    private readonly byte[] _sessionKey;
    private readonly HMACSHA256 _hmac;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _forwardMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _reverseMap = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new <see cref="IdentifierHasher"/> with a cryptographically random session key.
    /// </summary>
    public IdentifierHasher()
    {
        _sessionKey = new byte[32];
        RandomNumberGenerator.Fill(_sessionKey);
        _hmac = new HMACSHA256(_sessionKey);
    }

    /// <summary>
    /// Map from hashed identifier to original identifier.
    /// </summary>
    public IReadOnlyDictionary<string, string> ReverseMap => _reverseMap;

    /// <summary>
    /// Computes a deterministic, category-prefixed hash for the given identifier.
    /// </summary>
    /// <param name="identifier">The identifier to hash (e.g., table name, column name).</param>
    /// <param name="category">
    /// The category prefix: <c>"table"</c>, <c>"column"</c>, <c>"schema"</c>, or <c>"procedure"</c>.
    /// </param>
    /// <returns>
    /// A hashed identifier such as <c>"t_a3f2b1c8e4d9f0a1"</c>.
    /// </returns>
    public string Hash(string identifier, string category)
    {
        var cacheKey = $"{category}:{identifier}";
        if (_forwardMap.TryGetValue(cacheKey, out var cached))
            return cached;

        var prefix = CategoryToPrefix(category);
        var inputBytes = Encoding.UTF8.GetBytes(identifier.ToLowerInvariant());

        byte[] hashBytes;
        lock (_hmac)
        {
            hashBytes = _hmac.ComputeHash(inputBytes);
        }

        // Take the first 8 bytes and hex-encode (16 chars)
        var hex = Convert.ToHexString(hashBytes.AsSpan(0, 8)).ToLowerInvariant();
        var hashed = $"{prefix}{hex}";

        _forwardMap[cacheKey] = hashed;
        _reverseMap[hashed] = identifier;

        return hashed;
    }

    /// <summary>
    /// Hashes all identifiers in a <see cref="SchemaContext"/>, producing a new context with
    /// obfuscated names. The reverse map is updated so hashed identifiers can be restored.
    /// </summary>
    /// <param name="context">The schema context containing real identifier names.</param>
    /// <returns>A new <see cref="SchemaContext"/> with all identifiers hashed.</returns>
    public SchemaContext HashSchema(SchemaContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var hashedContext = new SchemaContext
        {
            DatabaseName = Hash(context.DatabaseName, "database"),
            CompressionLevel = context.CompressionLevel,
            EstimatedTokens = context.EstimatedTokens
        };

        foreach (var obj in context.Objects)
        {
            var hashedObj = new SchemaObjectSummary
            {
                Schema = Hash(obj.Schema, "schema"),
                Name = Hash(obj.Name, GetObjectCategory(obj.Type)),
                Type = obj.Type, // type label is not sensitive
                ApproxRowCount = obj.ApproxRowCount,
                Description = null // descriptions may contain sensitive data, strip in anonymous mode
            };

            if (obj.Columns != null)
            {
                hashedObj.Columns = [];
                foreach (var col in obj.Columns)
                {
                    hashedObj.Columns.Add(new ColumnSummary
                    {
                        Name = Hash(col.Name, "column"),
                        Type = col.Type, // SQL data types are not sensitive
                        IsNullable = col.IsNullable,
                        IsPrimaryKey = col.IsPrimaryKey,
                        ForeignKeyTarget = col.ForeignKeyTarget != null
                            ? HashForeignKeyTarget(col.ForeignKeyTarget)
                            : null,
                        Description = null // strip descriptions in anonymous mode
                    });
                }
            }

            if (obj.PrimaryKey != null)
            {
                hashedObj.PrimaryKey = obj.PrimaryKey
                    .Select(pk => Hash(pk, "column"))
                    .ToList();
            }

            if (obj.Indexes != null)
            {
                hashedObj.Indexes = obj.Indexes
                    .Select(idx => Hash(idx, "index"))
                    .ToList();
            }

            hashedContext.Objects.Add(hashedObj);
        }

        foreach (var fk in context.ForeignKeys)
        {
            hashedContext.ForeignKeys.Add(new FkSummary
            {
                ParentTable = HashQualifiedName(fk.ParentTable),
                ParentColumn = Hash(fk.ParentColumn, "column"),
                ReferencedTable = HashQualifiedName(fk.ReferencedTable),
                ReferencedColumn = Hash(fk.ReferencedColumn, "column")
            });
        }

        return hashedContext;
    }

    /// <summary>
    /// Replaces all known identifiers in <paramref name="sql"/> with their hashed equivalents.
    /// Longer identifiers are replaced first to avoid partial matches.
    /// </summary>
    /// <param name="sql">The SQL text to transform.</param>
    /// <returns>The SQL text with all known identifiers replaced by their hashed equivalents.</returns>
    public string HashSql(string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return sql;

        var result = sql;

        // Build replacement pairs sorted by original identifier length descending
        // to avoid partial matches (e.g., "OrderDetails" before "Order")
        var replacements = _reverseMap
            .Select(kvp => (hashed: kvp.Key, original: kvp.Value))
            .OrderByDescending(r => r.original.Length)
            .ToList();

        foreach (var (hashed, original) in replacements)
        {
            // Case-insensitive replacement of the original identifier with its hash
            result = ReplaceIdentifier(result, original, hashed);
        }

        return result;
    }

    /// <summary>
    /// Replaces hashed identifiers in <paramref name="text"/> with their original values
    /// using the reverse map.
    /// </summary>
    /// <param name="text">Text (typically an AI response) containing hashed identifiers.</param>
    /// <returns>The text with hashed identifiers replaced by their originals.</returns>
    public string Dehash(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = text;

        // Replace longer hashed identifiers first (all are same length, but be safe)
        foreach (var (hashed, original) in _reverseMap.OrderByDescending(kvp => kvp.Key.Length))
        {
            result = result.Replace(hashed, original, StringComparison.Ordinal);
        }

        return result;
    }

    /// <summary>
    /// Hashes a fully qualified name like <c>"dbo.Orders"</c> or <c>"dbo.Orders.CustomerId"</c>.
    /// Each part is hashed with its appropriate category.
    /// </summary>
    private string HashQualifiedName(string qualifiedName)
    {
        var parts = qualifiedName.Split('.');
        return parts.Length switch
        {
            1 => Hash(parts[0], "table"),
            2 => $"{Hash(parts[0], "schema")}.{Hash(parts[1], "table")}",
            3 => $"{Hash(parts[0], "schema")}.{Hash(parts[1], "table")}.{Hash(parts[2], "column")}",
            _ => Hash(qualifiedName, "table")
        };
    }

    /// <summary>
    /// Hashes a foreign key target string like <c>"dbo.Customers.CustomerId"</c>.
    /// </summary>
    private string HashForeignKeyTarget(string target)
    {
        return HashQualifiedName(target);
    }

    /// <summary>
    /// Maps an object type string to the hashing category.
    /// </summary>
    private static string GetObjectCategory(string objectType)
    {
        return objectType.ToLowerInvariant() switch
        {
            "procedure" or "function" => "procedure",
            "view" => "table", // views are referenced like tables in SQL
            _ => "table"
        };
    }

    /// <summary>
    /// Maps a category name to the short prefix used in hashed identifiers.
    /// </summary>
    private static string CategoryToPrefix(string category)
    {
        return category.ToLowerInvariant() switch
        {
            "table" => "t_",
            "column" => "c_",
            "schema" => "s_",
            "procedure" => "p_",
            "index" => "i_",
            "database" => "d_",
            _ => "x_"
        };
    }

    /// <summary>
    /// Case-insensitive word-boundary replacement of an identifier in SQL text.
    /// Uses word-boundary awareness to avoid replacing partial matches inside longer identifiers.
    /// </summary>
    private static string ReplaceIdentifier(string sql, string original, string replacement)
    {
        // Use regex with word boundaries for safe replacement
        var pattern = $@"(?<![_\w\[\]]){System.Text.RegularExpressions.Regex.Escape(original)}(?![_\w\[\]])";
        return System.Text.RegularExpressions.Regex.Replace(
            sql, pattern, replacement,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _hmac.Dispose();
        CryptographicOperations.ZeroMemory(_sessionKey);
    }
}
