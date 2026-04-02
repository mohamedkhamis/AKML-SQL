using System.Text.RegularExpressions;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Heavyweight Parameterize Values operation — replaces hardcoded literals with parameters.
/// </summary>
public class ParameterizeValuesOperation : HeavyweightOperationBase
{
    public override Task<RefactorPreviewResponse> PreviewAsync(
        RefactorPreviewRequest request,
        RefactoringContext ctx,
        CancellationToken ct)
    {
        var docText  = ctx.DocumentText;
        var visitor  = new LiteralInClauseVisitor();
        ctx.Script.Accept(visitor);

        if (visitor.Literals.Count == 0)
        {
            return Task.FromResult(new RefactorPreviewResponse
            {
                CanApply = true,
                Changes  = [],
                Warnings = [],
                Errors   = []
            });
        }

        // Build parameter map: varName → (sqlType, literalValue, offset, length)
        var usedNames    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var declarations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var changes      = new List<RefactorChangeInfo>();

        foreach (var lit in visitor.Literals)
        {
            var literalValue = docText.Substring(lit.StartOffset, lit.FragmentLength);
            var columnName   = lit.ContextColumnName ?? "Value";
            var varName      = MakeVarName(columnName, usedNames);
            var sqlType      = InferType(lit);

            declarations.TryAdd(varName, $"DECLARE {varName} {sqlType} = {literalValue}");

            changes.Add(new RefactorChangeInfo
            {
                FilePath       = string.Empty,
                StartOffset    = lit.StartOffset,
                EndOffset      = lit.StartOffset + lit.FragmentLength,
                OldText        = literalValue,
                NewText        = varName,
                ChangeCategory = ChangeCategory.Rename
            });
        }

        // Build declaration block to prepend at offset 0
        if (declarations.Count > 0)
        {
            var declBlock = string.Join("\n", declarations.Values) + "\n\n";
            changes.Add(new RefactorChangeInfo
            {
                FilePath       = string.Empty,
                StartOffset    = 0,
                EndOffset      = 0,
                OldText        = string.Empty,
                NewText        = declBlock,
                ChangeCategory = ChangeCategory.Declaration
            });
        }

        var sorted = changes.OrderByDescending(c => c.StartOffset).ToArray();

        return Task.FromResult(new RefactorPreviewResponse
        {
            CanApply = true,
            Changes  = sorted,
            Warnings = [],
            Errors   = []
        });
    }

    public override Task<RefactorApplyResponse> ApplyAsync(
        RefactorApplyRequest request,
        CancellationToken ct)
    {
        var result = ApplyChanges(request.ApprovedChanges, string.Empty);
        return Task.FromResult(new RefactorApplyResponse
        {
            Success             = true,
            AppliedCount        = request.ApprovedChanges.Length,
            UpdatedDocumentText = result
        });
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static string MakeVarName(string columnName, Dictionary<string, string> used)
    {
        // Sanitize column name to a valid identifier
        var clean = Regex.Replace(columnName, @"[^A-Za-z0-9_]", "_");
        if (string.IsNullOrEmpty(clean)) clean = "Value";

        // Pascal-case the first letter
        clean = char.ToUpperInvariant(clean[0]) + clean.Substring(1);

        var candidate = "@" + clean;
        if (!used.ContainsKey(candidate))
        {
            used[candidate] = candidate;
            return candidate;
        }

        // Suffix with counter if collision
        var counter = 2;
        while (used.ContainsKey(candidate + counter))
            counter++;

        var result = candidate + counter;
        used[result] = result;
        return result;
    }

    private static string InferType(LiteralWithContext lit)
    {
        switch (lit.Literal)
        {
            case IntegerLiteral:
                return "int";
            case NumericLiteral:
                return "decimal";
            case RealLiteral:
                return "decimal";
            case StringLiteral sl:
                // Check for date pattern e.g. '2024-01-15' or '2024/01/15'
                if (Regex.IsMatch(sl.Value, @"^\d{4}[-/]\d{2}[-/]\d{2}$"))
                    return "date";
                return "nvarchar(max)";
            default:
                return "nvarchar(max)";
        }
    }

    // ─── Visitor ────────────────────────────────────────────────────────────────

    private sealed class LiteralInClauseVisitor : TSqlFragmentVisitor
    {
        public List<LiteralWithContext> Literals { get; } = new();

        // Track whether we are inside a WHERE / ON / HAVING clause
        private bool _inTargetClause;
        private string? _currentColumnName;

        public override void ExplicitVisit(WhereClause node)
        {
            _inTargetClause = true;
            base.ExplicitVisit(node);
            _inTargetClause = false;
        }

        public override void ExplicitVisit(QualifiedJoin node)
        {
            // Capture ON clause literals
            var saved = _inTargetClause;
            if (node.SearchCondition != null)
            {
                _inTargetClause = true;
                node.SearchCondition.Accept(this);
                _inTargetClause = saved;
            }
            // Visit the two tables but not the search condition again
            node.FirstTableReference?.Accept(this);
            node.SecondTableReference?.Accept(this);
        }

        public override void ExplicitVisit(HavingClause node)
        {
            _inTargetClause = true;
            base.ExplicitVisit(node);
            _inTargetClause = false;
        }

        public override void ExplicitVisit(BooleanComparisonExpression node)
        {
            if (!_inTargetClause)
            {
                base.ExplicitVisit(node);
                return;
            }

            // Infer column name from left side of comparison
            string? colName = null;
            if (node.FirstExpression is ColumnReferenceExpression cre &&
                cre.MultiPartIdentifier?.Identifiers?.Count > 0)
            {
                colName = cre.MultiPartIdentifier.Identifiers.Last().Value;
            }

            var savedCol = _currentColumnName;
            _currentColumnName = colName ?? _currentColumnName;
            base.ExplicitVisit(node);
            _currentColumnName = savedCol;
        }

        public override void Visit(IntegerLiteral node)
        {
            if (_inTargetClause)
                Literals.Add(new LiteralWithContext(node, _currentColumnName));
        }

        public override void Visit(StringLiteral node)
        {
            if (_inTargetClause)
                Literals.Add(new LiteralWithContext(node, _currentColumnName));
        }

        public override void Visit(NumericLiteral node)
        {
            if (_inTargetClause)
                Literals.Add(new LiteralWithContext(node, _currentColumnName));
        }

        public override void Visit(RealLiteral node)
        {
            if (_inTargetClause)
                Literals.Add(new LiteralWithContext(node, _currentColumnName));
        }
    }

    internal sealed class LiteralWithContext(Literal literal, string? contextColumnName)
    {
        public Literal Literal        { get; } = literal;
        public string? ContextColumnName { get; } = contextColumnName;
        public int StartOffset        => Literal.StartOffset;
        public int FragmentLength     => Literal.FragmentLength;
    }
}
