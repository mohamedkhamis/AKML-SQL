using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Text;

namespace AkmlSql.Web.Services;

/// <summary>What the schema explorer's context menu asked for.</summary>
public enum SchemaObjectCommand
{
    /// <summary>A new query reading the first 1000 rows, run at once (SSMS's Select Top 1000 Rows).</summary>
    SelectTop,
    ScriptSelect,
    ScriptInsert,
    ScriptUpdate,
    ScriptDelete,
    /// <summary>A procedure's EXEC / CALL, or a function's SELECT.</summary>
    ScriptExecute,
    InsertName,
    CopyName,
}

/// <summary>A context-menu choice on one object of the explorer.</summary>
public sealed record SchemaObjectAction(SchemaObjectCommand Command, DatabaseKind Kind, string SchemaName, SchemaPhaseObject Object);

/// <summary>
/// The scripts the explorer's context menu opens, written for the connection's kind: SQL Server names in brackets and TOP,
/// PostgreSQL names quoted only where they must be and LIMIT. Pure, so the shapes are tested without a browser. The
/// UPDATE and DELETE scripts match no row until their WHERE is filled in.
/// </summary>
public static class SchemaObjectScripts
{
    public const int TopRows = 1000;

    // The engine's DbObjectType codes, as the schema payload carries them.
    private const int Table = 0, View = 1, Procedure = 2, ScalarFunction = 3, TableFunction = 4, InlineFunction = 5, Sequence = 7, MaterializedView = 8;

    public static bool IsTable(int objectType) => objectType == Table;

    public static bool IsReadable(int objectType) => objectType is Table or View or MaterializedView;

    public static bool IsRoutine(int objectType) => objectType is Procedure or ScalarFunction or TableFunction or InlineFunction;

    public static bool IsProcedure(int objectType) => objectType == Procedure;

    public static bool IsSequence(int objectType) => objectType == Sequence;

    /// <summary>The commands the menu offers for an object of this type, in menu order (names and Refresh come after).</summary>
    public static IReadOnlyList<SchemaObjectCommand> CommandsFor(int objectType)
    {
        if (IsTable(objectType))
            return new[] { SchemaObjectCommand.SelectTop, SchemaObjectCommand.ScriptSelect, SchemaObjectCommand.ScriptInsert, SchemaObjectCommand.ScriptUpdate, SchemaObjectCommand.ScriptDelete };
        if (IsReadable(objectType))
            return new[] { SchemaObjectCommand.SelectTop, SchemaObjectCommand.ScriptSelect };
        if (IsRoutine(objectType))
            return new[] { SchemaObjectCommand.ScriptExecute };
        return Array.Empty<SchemaObjectCommand>();
    }

    /// <summary>The menu's words for a command.</summary>
    public static string Label(SchemaObjectCommand command, DatabaseKind kind, int objectType) => command switch
    {
        SchemaObjectCommand.SelectTop => kind == DatabaseKind.PostgreSql ? $"Select first {TopRows} rows" : $"Select Top {TopRows} Rows",
        SchemaObjectCommand.ScriptSelect => "Script as SELECT",
        SchemaObjectCommand.ScriptInsert => "Script as INSERT",
        SchemaObjectCommand.ScriptUpdate => "Script as UPDATE",
        SchemaObjectCommand.ScriptDelete => "Script as DELETE",
        SchemaObjectCommand.ScriptExecute => IsProcedure(objectType)
            ? (kind == DatabaseKind.PostgreSql ? "Script as CALL" : "Script as EXECUTE")
            : "Script as SELECT",
        SchemaObjectCommand.InsertName => "Insert name",
        SchemaObjectCommand.CopyName => "Copy name",
        _ => command.ToString(),
    };

    /// <summary><c>[schema].[name]</c> on SQL Server, <c>schema.name</c> (quoted as needed) on PostgreSQL.</summary>
    public static string Name(DatabaseKind kind, string schema, string name) =>
        kind == DatabaseKind.PostgreSql ? PgIdentifierQuoting.QualifiedName(schema, name) : Bracket(schema) + "." + Bracket(name);

    public static string Column(DatabaseKind kind, string name) =>
        kind == DatabaseKind.PostgreSql ? PgIdentifierQuoting.Quote(name) : Bracket(name);

    private static string Bracket(string name) => "[" + (name ?? string.Empty).Replace("]", "]]") + "]";

    /// <summary>The script for a command, or null when there is none (no columns to write an INSERT with, a name command).</summary>
    public static string? Script(SchemaObjectCommand command, DatabaseKind kind, string schema, SchemaPhaseObject obj)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        var name = Name(kind, schema, obj.ObjectName);
        var columns = obj.Columns ?? Array.Empty<SchemaPhaseColumn>();
        return command switch
        {
            SchemaObjectCommand.SelectTop => Select(kind, name, columns, top: true),
            SchemaObjectCommand.ScriptSelect => Select(kind, name, columns, top: false),
            SchemaObjectCommand.ScriptInsert => columns.Length == 0 ? null : Insert(kind, name, columns),
            SchemaObjectCommand.ScriptUpdate => columns.Length == 0 ? null : Update(kind, name, columns),
            SchemaObjectCommand.ScriptDelete => $"DELETE FROM {name}{Where(kind, columns)}",
            SchemaObjectCommand.ScriptExecute => IsRoutine(obj.ObjectType) ? Execute(kind, name, obj) : null,
            _ => null,
        };
    }

    private static string Select(DatabaseKind kind, string name, SchemaPhaseColumn[] columns, bool top)
    {
        var list = columns.Length == 0 ? "*" : string.Join(",\n       ", columns.Select(c => Column(kind, c.Name)));
        if (kind == DatabaseKind.PostgreSql)
            return $"SELECT {list}\nFROM {name}" + (top ? $"\nLIMIT {TopRows};" : ";");
        return (top ? $"SELECT TOP ({TopRows}) " : "SELECT ") + $"{list}\nFROM {name};";
    }

    private static string Insert(DatabaseKind kind, string name, SchemaPhaseColumn[] columns)
    {
        var sb = new StringBuilder();
        sb.Append("INSERT INTO ").Append(name).Append(" (\n");
        for (var i = 0; i < columns.Length; i++)
            sb.Append("    ").Append(Column(kind, columns[i].Name)).Append(i < columns.Length - 1 ? ",\n" : "\n");
        sb.Append(")\nVALUES (\n");
        for (var i = 0; i < columns.Length; i++)
            sb.Append("    NULL").Append(i < columns.Length - 1 ? "," : " ").Append(" -- ").Append(Describe(columns[i])).Append('\n');
        sb.Append(");");
        return sb.ToString();
    }

    private static string Update(DatabaseKind kind, string name, SchemaPhaseColumn[] columns)
    {
        var set = columns.Where(c => !c.IsPrimaryKey).ToArray();
        if (set.Length == 0) set = columns;
        var sb = new StringBuilder();
        sb.Append("UPDATE ").Append(name).Append('\n');
        for (var i = 0; i < set.Length; i++)
        {
            sb.Append(i == 0 ? "SET    " : "       ").Append(Column(kind, set[i].Name)).Append(" = NULL")
              .Append(i < set.Length - 1 ? "," : " ").Append(" -- ").Append(Describe(set[i])).Append('\n');
        }
        sb.Length--;   // the WHERE starts its own line
        sb.Append(Where(kind, columns));
        return sb.ToString();
    }

    // Matches no row until it is edited: the key columns compared with NULL, or 1 = 0 when the table has no key.
    private static string Where(DatabaseKind kind, SchemaPhaseColumn[] columns)
    {
        var keys = columns.Where(c => c.IsPrimaryKey).ToArray();
        if (keys.Length == 0) return "\nWHERE 1 = 0; -- the rows to change";
        return "\nWHERE  " + string.Join("\n  AND  ", keys.Select(k => Column(kind, k.Name) + " = NULL")) + "; -- the key of the row";
    }

    private static string Execute(DatabaseKind kind, string name, SchemaPhaseObject routine)
    {
        var parameters = (routine.Parameters ?? Array.Empty<SchemaPhaseParameter>())
            .Where(p => !string.IsNullOrEmpty(p.Name) || kind == DatabaseKind.PostgreSql)
            .ToArray();
        if (kind == DatabaseKind.PostgreSql)
        {
            // A function's OUT parameters are its result columns, not arguments; a procedure's are passed (as NULL).
            var passed = IsProcedure(routine.ObjectType) ? parameters : parameters.Where(p => !p.IsOutput).ToArray();
            var args = string.Join(", ", passed.Select(_ => "NULL"));
            return IsProcedure(routine.ObjectType) ? $"CALL {name}({args});" : $"SELECT *\nFROM {name}({args});";
        }
        if (IsProcedure(routine.ObjectType))
        {
            if (parameters.Length == 0) return $"EXEC {name};";
            var lines = parameters.Select((p, i) =>
                "    " + (p.Name.StartsWith("@", StringComparison.Ordinal) ? p.Name : "@" + p.Name) + " = NULL"
                + (p.IsOutput ? " OUTPUT" : string.Empty) + (i < parameters.Length - 1 ? "," : ";") + " -- " + p.TypeName);
            return $"EXEC {name}\n" + string.Join("\n", lines);
        }
        var call = $"{name}({string.Join(", ", parameters.Select(_ => "NULL"))})";
        return routine.ObjectType == ScalarFunction ? $"SELECT {call};" : $"SELECT *\nFROM {call};";
    }

    private static string Describe(SchemaPhaseColumn column) =>
        column.TypeName + (column.IsPrimaryKey ? ", key" : string.Empty) + (column.IsNullable ? string.Empty : ", not null");
}
