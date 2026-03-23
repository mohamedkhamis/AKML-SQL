using System;
using System.Collections.Generic;
using AkmlSql.Engine.Refactoring.Operations.Heavyweight;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring;

/// <summary>
/// A single identifier reference found in a SQL document.
/// </summary>
public class ReferenceMatch
{
    public string FilePath { get; set; } = string.Empty;
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public string MatchedText { get; set; } = string.Empty;
    public string ContextSnippet { get; set; } = string.Empty;
}

/// <summary>
/// TSqlFragmentVisitor that collects all references to a target identifier across
/// ColumnReferenceExpression, NamedTableReference, SchemaObjectName,
/// VariableReference, ProcedureReference, and bare Identifier nodes.
/// </summary>
public class ReferenceCollector : TSqlFragmentVisitor
{
    private readonly string _targetName;
    private readonly string _filePath;
    private readonly string _documentText;
    private readonly List<ReferenceMatch> _matches = new();

    public IReadOnlyList<ReferenceMatch> Matches => _matches;

    public ReferenceCollector(string targetName, string filePath, string documentText)
    {
        _targetName   = targetName;
        _filePath     = filePath;
        _documentText = documentText;
    }

    // ColumnReferenceExpression: e.g., SELECT col  or  table.col
    public override void Visit(ColumnReferenceExpression node)
    {
        if (node.MultiPartIdentifier?.Identifiers == null) return;
        foreach (var id in node.MultiPartIdentifier.Identifiers)
            TryAddMatch(id);
    }

    // NamedTableReference: e.g., FROM dbo.Orders AS o
    public override void Visit(NamedTableReference node)
    {
        if (node.SchemaObject?.Identifiers != null)
        {
            // Match the object name (last identifier) — not the schema prefix
            var ids = node.SchemaObject.Identifiers;
            if (ids.Count > 0) TryAddMatch(ids[ids.Count - 1]);
        }
        // Also check the table alias (e.g., the "o" in "Orders AS o")
        if (node.Alias != null)
            TryAddMatch(node.Alias);
    }

    // SchemaObjectName standalone references
    public override void Visit(SchemaObjectName node)
    {
        if (node.Identifiers == null) return;
        foreach (var id in node.Identifiers)
            TryAddMatch(id);
    }

    // @variable references
    public override void Visit(VariableReference node)
    {
        // VariableReference.Name includes the @ prefix; strip it for comparison
        var name = node.Name?.TrimStart('@') ?? string.Empty;
        if (string.Equals(name, _targetName.TrimStart('@'), StringComparison.OrdinalIgnoreCase))
        {
            _matches.Add(BuildMatch(node.StartOffset, node.FragmentLength, node.Name ?? string.Empty));
        }
    }

    // Stored procedure references
    public override void Visit(ProcedureReference node)
    {
        if (node.Name?.Identifiers == null) return;
        var ids = node.Name.Identifiers;
        if (ids.Count > 0) TryAddMatch(ids[ids.Count - 1]);
    }

    // Column aliases: e.g., SELECT col AS alias
    public override void Visit(SelectScalarExpression node)
    {
        // ColumnName is IdentifierOrValueExpression — extract the Identifier part if present
        if (node.ColumnName?.Identifier != null)
            TryAddMatch(node.ColumnName.Identifier);
    }

    private void TryAddMatch(Identifier id)
    {
        if (id == null) return;
        var value = id.Value ?? string.Empty;
        if (string.Equals(value, _targetName, StringComparison.OrdinalIgnoreCase))
        {
            _matches.Add(BuildMatch(id.StartOffset, id.FragmentLength, value));
        }
    }

    private ReferenceMatch BuildMatch(int startOffset, int length, string matchedText)
    {
        var (line, col) = HeavyweightOperationBase.OffsetToLineCol(_documentText, startOffset);
        return new ReferenceMatch
        {
            FilePath       = _filePath,
            StartOffset    = startOffset,
            EndOffset      = startOffset + length,
            Line           = line,
            Column         = col,
            MatchedText    = matchedText,
            ContextSnippet = HeavyweightOperationBase.ExtractContext(_documentText, startOffset)
        };
    }
}
