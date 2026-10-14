using System;
using System.Collections.Generic;
using System.Text;

namespace AkmlSql.Core.Text
{
    /// <summary>
    /// Spec 042 (FR-045, FR-061) - PostgreSQL identifier quoting. PostgreSQL folds an unquoted identifier to lower
    /// case, so a name written back into a script needs double quotes exactly when it is not a plain lower-case
    /// identifier (<c>[a-z_][a-z0-9_$]*</c>) or when it is a keyword that cannot stand as a table or column name.
    /// Embedded double quotes are doubled. Never <c>[brackets]</c>.
    ///
    /// <para>
    /// Non-ASCII names are always quoted: PostgreSQL folds only ASCII upper case, so quoting is the one rendering
    /// that is correct whatever the database encoding; it costs nothing but a pair of quotes.
    /// </para>
    /// <para>
    /// <see cref="ReservedWords"/> is the "reserved" and "reserved (can be function or type)" keywords of the
    /// PostgreSQL 18 manual (appendix C). "Non-reserved" and "non-reserved (cannot be function or type)" keywords
    /// are valid unquoted as table and column names, so they are not listed. The reference generator reads
    /// <c>kwlist.h</c>; a test in the PostgreSQL test project compares this set with it.
    /// </para>
    /// Pure C#, netstandard2.0-safe.
    /// </summary>
    public static class PgIdentifierQuoting
    {
        private static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.Ordinal)
        {
            // reserved
            "all", "analyse", "analyze", "and", "any", "array", "as", "asc", "asymmetric", "both", "case", "cast",
            "check", "collate", "column", "constraint", "create", "current_catalog", "current_date", "current_role",
            "current_time", "current_timestamp", "current_user", "default", "deferrable", "desc", "distinct", "do",
            "else", "end", "except", "false", "fetch", "for", "foreign", "from", "grant", "group", "having", "in",
            "initially", "intersect", "into", "lateral", "leading", "limit", "localtime", "localtimestamp", "not",
            "null", "offset", "on", "only", "or", "order", "placing", "primary", "references", "returning", "select",
            "session_user", "some", "symmetric", "system_user", "table", "then", "to", "trailing", "true", "union",
            "unique", "user", "using", "variadic", "when", "where", "window", "with",
            // reserved (can be function or type)
            "authorization", "binary", "collation", "concurrently", "cross", "current_schema", "freeze", "full",
            "ilike", "inner", "is", "isnull", "join", "left", "like", "natural", "notnull", "outer", "overlaps",
            "right", "similar", "tablesample", "verbose",
        };

        /// <summary>The lower-case keywords that must be quoted when used as a table or column name.</summary>
        public static IReadOnlyCollection<string> ReservedWords => Reserved;

        /// <summary>True when <paramref name="name"/> cannot be written unquoted and mean the same identifier.</summary>
        public static bool NeedsQuoting(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            if (!IsPlainLowerCase(name)) return true;
            return Reserved.Contains(name);
        }

        /// <summary>The name as it should be written in a script: bare when safe, otherwise in double quotes.</summary>
        public static string Quote(string name)
        {
            if (!NeedsQuoting(name)) return name;

            var builder = new StringBuilder(name.Length + 2);
            builder.Append('"');
            foreach (var ch in name)
            {
                if (ch == '"') builder.Append('"');
                builder.Append(ch);
            }
            builder.Append('"');
            return builder.ToString();
        }

        /// <summary><c>schema.name</c> with each part quoted as needed; just the name when the schema is empty.</summary>
        public static string QualifiedName(string? schema, string name)
        {
            return string.IsNullOrEmpty(schema) ? Quote(name) : Quote(schema!) + "." + Quote(name);
        }

        private static bool IsPlainLowerCase(string name)
        {
            for (var i = 0; i < name.Length; i++)
            {
                var ch = name[i];
                var letter = ch >= 'a' && ch <= 'z';
                if (i == 0)
                {
                    if (!letter && ch != '_') return false;
                }
                else if (!letter && ch != '_' && ch != '$' && !(ch >= '0' && ch <= '9'))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
