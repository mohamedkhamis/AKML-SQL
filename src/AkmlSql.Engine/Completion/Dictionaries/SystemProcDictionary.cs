using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Engine.Completion.Dictionaries;

/// <summary>
/// T099: Static dictionary of commonly used SQL Server system stored procedures and
/// extended stored procedures for IntelliSense completion.
/// </summary>
public static class SystemProcDictionary
{
    private static readonly SystemProcEntry[] Entries =
    [
        // Core system procedures
        new("sp_help", "Displays object information (tables, views, etc.)"),
        new("sp_helptext", "Displays the definition of a stored procedure, trigger, or view"),
        new("sp_helpdb", "Displays information about a specified database or all databases"),
        new("sp_helpindex", "Reports information about the indexes on a table or view"),
        new("sp_who", "Provides information about current users, sessions, and processes"),
        new("sp_who2", "Extended version of sp_who with additional session details"),
        new("sp_lock", "Reports information about locks"),
        new("sp_columns", "Returns column information for the specified objects"),
        new("sp_tables", "Returns a list of objects that can be queried in the current environment"),
        new("sp_databases", "Lists databases on the server"),
        new("sp_stored_procedures", "Returns a list of stored procedures in the current environment"),

        // Rename / utility
        new("sp_rename", "Renames a user-created object (table, column, index, alias data type)"),
        new("sp_executesql", "Executes a Transact-SQL statement or batch that can be reused or built dynamically"),
        new("sp_spaceused", "Displays the number of rows, disk space reserved, and disk space used"),
        new("sp_depends", "Displays information about database object dependencies"),
        new("sp_helpconstraint", "Returns a list of all constraint types for a table"),

        // Security
        new("sp_addrolemember", "Adds a security principal to a database role"),
        new("sp_droprolemember", "Removes a security principal from a database role"),
        new("sp_helprole", "Returns information about the roles in the current database"),
        new("sp_helpuser", "Reports information about database-level principals"),
        new("sp_helplogins", "Reports information about server-level logins"),

        // Extended procedures
        new("xp_cmdshell", "Executes a Windows command shell command and returns output as text"),
        new("xp_fileexist", "Checks whether a file exists on the server"),
        new("xp_fixeddrives", "Lists fixed drives and their free space"),
        new("xp_loginconfig", "Reports the login security configuration"),
        new("xp_msver", "Returns Microsoft SQL Server version information"),

        // Configuration
        new("sp_configure", "Displays or changes global configuration settings for the current server"),
        new("sp_helpsort", "Displays the sort order and character set for the instance"),

        // Replication / linked servers
        new("sp_addlinkedserver", "Creates a linked server"),
        new("sp_droplinkedserver", "Removes a linked server"),
        new("sp_linkedservers", "Returns the list of linked servers defined"),
        new("sp_helpserver", "Reports information about a particular remote or replication server"),

        // Service Broker
        new("sp_send_dbmail", "Sends an e-mail message to the specified recipients"),

        // Maintenance
        new("sp_updatestats", "Runs UPDATE STATISTICS against all tables in the current database"),
        new("sp_recompile", "Causes stored procedures, triggers, and UDFs to be recompiled the next time they are run"),
    ];

    /// <summary>
    /// Returns all system procedure entries as CompletionItems.
    /// </summary>
    public static IEnumerable<CompletionItem> GetCompletionItems()
    {
        foreach (var entry in Entries)
        {
            yield return new CompletionItem
            {
                DisplayText = entry.Name,
                InsertText = entry.Name,
                ObjectType = (int)CompletionObjectType.Procedure,
                SecondaryText = entry.Description,
                SourceObject = "master.dbo",
                SortPriority = 400 // System procs appear after user objects
            };
        }
    }

    /// <summary>
    /// Gets the count of registered system procedures.
    /// </summary>
    public static int Count => Entries.Length;

    private sealed record SystemProcEntry(string Name, string Description);
}
