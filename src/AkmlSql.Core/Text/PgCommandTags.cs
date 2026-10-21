namespace AkmlSql.Core.Text
{
    /// <summary>
    /// Spec 042 (FR-043): what a PostgreSQL command tag says about the schema. A run that created, altered or dropped an object
    /// changes what the Schema explorer should show, so the engine reloads its catalog and the browser refetches it, without a
    /// manual refresh. Shared by the engine (which decides to reload) and the web edition (which decides to refetch), so the two
    /// can never disagree about which tags count.
    /// </summary>
    public static class PgCommandTags
    {
        /// <summary>
        /// True for the tags of statements that change the catalog: <c>CREATE ...</c>, <c>ALTER ...</c>, <c>DROP ...</c> and
        /// <c>COMMENT</c>. Not for <c>REFRESH MATERIALIZED VIEW</c> (the data changes, not the schema) or <c>TRUNCATE TABLE</c>.
        /// </summary>
        public static bool ChangesSchema(string? tag)
        {
            if (string.IsNullOrEmpty(tag)) return false;
            var text = tag!.TrimStart();
            return text.StartsWith("CREATE ", System.StringComparison.Ordinal)
                || text.StartsWith("ALTER ", System.StringComparison.Ordinal)
                || text.StartsWith("DROP ", System.StringComparison.Ordinal)
                || string.Equals(text, "COMMENT", System.StringComparison.Ordinal);
        }
    }
}
