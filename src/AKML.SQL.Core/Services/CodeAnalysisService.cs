using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Service for analyzing SQL code and providing suggestions.
/// Implements Story 9.2: Code Analysis.
/// </summary>
public class CodeAnalysisService : ICodeAnalysisService
{
    private readonly ILogger<CodeAnalysisService> _logger;
    private readonly TSql160Parser _parser;
    private readonly List<IAnalysisRule> _rules;

    public CodeAnalysisService(ILogger<CodeAnalysisService> logger)
    {
        _logger = logger;
        _parser = new TSql160Parser(initialQuotedIdentifiers: true);
        _rules = new List<IAnalysisRule>
        {
            new SelectStarRule(),
            new MissingWhereClauseRule(),
            new ImplicitConversionRule(),
            new TopWithoutOrderByRule(),
            new NoLockHintRule(),
            new UnusedVariableRule(),
            new MissingIndexHintRule(),
            new LargeInClauseRule(),
            new NestedSubqueryRule(),
            new CartesianJoinRule()
        };
    }

    /// <summary>
    /// Analyzes SQL code and returns issues.
    /// </summary>
    public AnalysisResult Analyze(string sql, AnalysisOptions? options = null)
    {
        var result = new AnalysisResult();

        if (string.IsNullOrWhiteSpace(sql))
            return result;

        options ??= new AnalysisOptions();

        try
        {
            using var reader = new StringReader(sql);
            var fragment = _parser.Parse(reader, out var errors);

            // Add parse errors
            if (errors != null)
            {
                foreach (var error in errors)
                {
                    result.Issues.Add(new AnalysisIssue
                    {
                        RuleId = "PARSE001",
                        Message = error.Message,
                        Severity = IssueSeverity.Error,
                        Line = error.Line - 1,
                        Column = error.Column - 1,
                        Category = IssueCategory.Syntax
                    });
                }
            }

            // Run analysis rules
            if (fragment is TSqlScript script)
            {
                var context = new AnalysisContext
                {
                    Script = script,
                    Sql = sql,
                    Options = options
                };

                foreach (var rule in _rules.Where(r => IsRuleEnabled(r, options)))
                {
                    try
                    {
                        var issues = rule.Analyze(context);
                        result.Issues.AddRange(issues);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error running analysis rule {RuleId}", rule.RuleId);
                    }
                }
            }

            result.Issues = result.Issues
                .OrderBy(i => i.Line)
                .ThenBy(i => i.Column)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing SQL");
            result.Issues.Add(new AnalysisIssue
            {
                RuleId = "INTERNAL",
                Message = $"Analysis error: {ex.Message}",
                Severity = IssueSeverity.Error,
                Category = IssueCategory.Internal
            });
        }

        return result;
    }

    /// <summary>
    /// Gets all available rules.
    /// </summary>
    public IReadOnlyList<AnalysisRuleInfo> GetAvailableRules()
    {
        return _rules.Select(r => new AnalysisRuleInfo
        {
            RuleId = r.RuleId,
            Name = r.Name,
            Description = r.Description,
            Category = r.Category,
            DefaultSeverity = r.DefaultSeverity
        }).ToList();
    }

    private bool IsRuleEnabled(IAnalysisRule rule, AnalysisOptions options)
    {
        if (options.DisabledRules?.Contains(rule.RuleId) == true)
            return false;

        if (options.EnabledCategories != null && !options.EnabledCategories.Contains(rule.Category))
            return false;

        return true;
    }
}

/// <summary>
/// Interface for code analysis service.
/// </summary>
public interface ICodeAnalysisService
{
    AnalysisResult Analyze(string sql, AnalysisOptions? options = null);
    IReadOnlyList<AnalysisRuleInfo> GetAvailableRules();
}

/// <summary>
/// Analysis result containing issues found.
/// </summary>
public class AnalysisResult
{
    public List<AnalysisIssue> Issues { get; set; } = new();
    public int ErrorCount => Issues.Count(i => i.Severity == IssueSeverity.Error);
    public int WarningCount => Issues.Count(i => i.Severity == IssueSeverity.Warning);
    public int InfoCount => Issues.Count(i => i.Severity == IssueSeverity.Info);
}

/// <summary>
/// An issue found during analysis.
/// </summary>
public class AnalysisIssue
{
    public string RuleId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public IssueSeverity Severity { get; set; }
    public IssueCategory Category { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public string? Suggestion { get; set; }
}

/// <summary>
/// Analysis options.
/// </summary>
public class AnalysisOptions
{
    public HashSet<string>? DisabledRules { get; set; }
    public HashSet<IssueCategory>? EnabledCategories { get; set; }
    public IssueSeverity MinimumSeverity { get; set; } = IssueSeverity.Info;
}

/// <summary>
/// Issue severity levels.
/// </summary>
public enum IssueSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Issue categories.
/// </summary>
public enum IssueCategory
{
    Syntax,
    Performance,
    Security,
    BestPractice,
    Style,
    Internal
}

/// <summary>
/// Information about an analysis rule.
/// </summary>
public class AnalysisRuleInfo
{
    public string RuleId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IssueCategory Category { get; set; }
    public IssueSeverity DefaultSeverity { get; set; }
}

/// <summary>
/// Context for analysis rules.
/// </summary>
internal class AnalysisContext
{
    public TSqlScript Script { get; set; } = null!;
    public string Sql { get; set; } = string.Empty;
    public AnalysisOptions Options { get; set; } = new();
}

/// <summary>
/// Interface for analysis rules.
/// </summary>
internal interface IAnalysisRule
{
    string RuleId { get; }
    string Name { get; }
    string Description { get; }
    IssueCategory Category { get; }
    IssueSeverity DefaultSeverity { get; }
    IEnumerable<AnalysisIssue> Analyze(AnalysisContext context);
}

#region Analysis Rules

internal class SelectStarRule : IAnalysisRule
{
    public string RuleId => "PERF001";
    public string Name => "SELECT * Usage";
    public string Description => "Avoid using SELECT * in production code";
    public IssueCategory Category => IssueCategory.Performance;
    public IssueSeverity DefaultSeverity => IssueSeverity.Warning;

    public IEnumerable<AnalysisIssue> Analyze(AnalysisContext context)
    {
        var visitor = new SelectStarVisitor();
        context.Script.Accept(visitor);

        return visitor.Stars.Select(s => new AnalysisIssue
        {
            RuleId = RuleId,
            Message = "Avoid using SELECT *. Specify explicit columns instead.",
            Severity = DefaultSeverity,
            Category = Category,
            Line = s.StartLine - 1,
            Column = s.StartColumn - 1,
            StartOffset = s.StartOffset,
            EndOffset = s.StartOffset + s.FragmentLength,
            Suggestion = "Replace * with explicit column names"
        });
    }

    private class SelectStarVisitor : TSqlFragmentVisitor
    {
        public List<SelectStarExpression> Stars { get; } = new();

        public override void Visit(SelectStarExpression node)
        {
            Stars.Add(node);
            base.Visit(node);
        }
    }
}

internal class MissingWhereClauseRule : IAnalysisRule
{
    public string RuleId => "SEC001";
    public string Name => "Missing WHERE Clause";
    public string Description => "UPDATE/DELETE without WHERE clause affects all rows";
    public IssueCategory Category => IssueCategory.Security;
    public IssueSeverity DefaultSeverity => IssueSeverity.Warning;

    public IEnumerable<AnalysisIssue> Analyze(AnalysisContext context)
    {
        var issues = new List<AnalysisIssue>();

        foreach (var batch in context.Script.Batches)
        {
            foreach (var statement in batch.Statements)
            {
                if (statement is UpdateStatement update && update.UpdateSpecification.WhereClause == null)
                {
                    issues.Add(CreateIssue(statement, "UPDATE statement without WHERE clause will affect all rows."));
                }
                else if (statement is DeleteStatement delete && delete.DeleteSpecification.WhereClause == null)
                {
                    issues.Add(CreateIssue(statement, "DELETE statement without WHERE clause will delete all rows."));
                }
            }
        }

        return issues;
    }

    private AnalysisIssue CreateIssue(TSqlStatement statement, string message) => new()
    {
        RuleId = RuleId,
        Message = message,
        Severity = DefaultSeverity,
        Category = Category,
        Line = statement.StartLine - 1,
        Column = statement.StartColumn - 1,
        StartOffset = statement.StartOffset,
        EndOffset = statement.StartOffset + statement.FragmentLength,
        Suggestion = "Add a WHERE clause to limit affected rows"
    };
}

internal class ImplicitConversionRule : IAnalysisRule
{
    public string RuleId => "PERF002";
    public string Name => "Potential Implicit Conversion";
    public string Description => "String literals in comparisons may cause implicit conversions";
    public IssueCategory Category => IssueCategory.Performance;
    public IssueSeverity DefaultSeverity => IssueSeverity.Info;

    public IEnumerable<AnalysisIssue> Analyze(AnalysisContext context)
    {
        // This is a simplified check - full implementation would need schema info
        return Enumerable.Empty<AnalysisIssue>();
    }
}

internal class TopWithoutOrderByRule : IAnalysisRule
{
    public string RuleId => "BP001";
    public string Name => "TOP Without ORDER BY";
    public string Description => "TOP clause without ORDER BY returns arbitrary rows";
    public IssueCategory Category => IssueCategory.BestPractice;
    public IssueSeverity DefaultSeverity => IssueSeverity.Warning;

    public IEnumerable<AnalysisIssue> Analyze(AnalysisContext context)
    {
        var visitor = new TopWithoutOrderByVisitor();
        context.Script.Accept(visitor);
        return visitor.Issues;
    }

    private class TopWithoutOrderByVisitor : TSqlFragmentVisitor
    {
        public List<AnalysisIssue> Issues { get; } = new();

        public override void Visit(QuerySpecification node)
        {
            if (node.TopRowFilter != null && node.OrderByClause == null)
            {
                Issues.Add(new AnalysisIssue
                {
                    RuleId = "BP001",
                    Message = "TOP clause without ORDER BY returns non-deterministic results.",
                    Severity = IssueSeverity.Warning,
                    Category = IssueCategory.BestPractice,
                    Line = node.TopRowFilter.StartLine - 1,
                    Column = node.TopRowFilter.StartColumn - 1,
                    StartOffset = node.TopRowFilter.StartOffset,
                    EndOffset = node.TopRowFilter.StartOffset + node.TopRowFilter.FragmentLength,
                    Suggestion = "Add an ORDER BY clause to ensure consistent results"
                });
            }
            base.Visit(node);
        }
    }
}

internal class NoLockHintRule : IAnalysisRule
{
    public string RuleId => "BP002";
    public string Name => "NOLOCK Hint Usage";
    public string Description => "NOLOCK can cause dirty reads and inconsistent data";
    public IssueCategory Category => IssueCategory.BestPractice;
    public IssueSeverity DefaultSeverity => IssueSeverity.Info;

    public IEnumerable<AnalysisIssue> Analyze(AnalysisContext context)
    {
        var visitor = new NoLockVisitor();
        context.Script.Accept(visitor);
        return visitor.Issues;
    }

    private class NoLockVisitor : TSqlFragmentVisitor
    {
        public List<AnalysisIssue> Issues { get; } = new();

        public override void Visit(TableHint node)
        {
            if (node.HintKind == TableHintKind.NoLock)
            {
                Issues.Add(new AnalysisIssue
                {
                    RuleId = "BP002",
                    Message = "NOLOCK hint can cause dirty reads and inconsistent data.",
                    Severity = IssueSeverity.Info,
                    Category = IssueCategory.BestPractice,
                    Line = node.StartLine - 1,
                    Column = node.StartColumn - 1,
                    StartOffset = node.StartOffset,
                    EndOffset = node.StartOffset + node.FragmentLength,
                    Suggestion = "Consider using READ COMMITTED SNAPSHOT isolation instead"
                });
            }
            base.Visit(node);
        }
    }
}

internal class UnusedVariableRule : IAnalysisRule
{
    public string RuleId => "BP003";
    public string Name => "Unused Variable";
    public string Description => "Variables declared but never used";
    public IssueCategory Category => IssueCategory.BestPractice;
    public IssueSeverity DefaultSeverity => IssueSeverity.Info;

    public IEnumerable<AnalysisIssue> Analyze(AnalysisContext context)
    {
        // Simplified implementation - would need scope tracking for full implementation
        return Enumerable.Empty<AnalysisIssue>();
    }
}

internal class MissingIndexHintRule : IAnalysisRule
{
    public string RuleId => "PERF003";
    public string Name => "Potential Missing Index";
    public string Description => "Large table scans may benefit from indexes";
    public IssueCategory Category => IssueCategory.Performance;
    public IssueSeverity DefaultSeverity => IssueSeverity.Info;

    public IEnumerable<AnalysisIssue> Analyze(AnalysisContext context)
    {
        // Would need execution plan analysis for accurate detection
        return Enumerable.Empty<AnalysisIssue>();
    }
}

internal class LargeInClauseRule : IAnalysisRule
{
    public string RuleId => "PERF004";
    public string Name => "Large IN Clause";
    public string Description => "Large IN clauses may perform poorly";
    public IssueCategory Category => IssueCategory.Performance;
    public IssueSeverity DefaultSeverity => IssueSeverity.Warning;

    public IEnumerable<AnalysisIssue> Analyze(AnalysisContext context)
    {
        var visitor = new LargeInClauseVisitor();
        context.Script.Accept(visitor);
        return visitor.Issues;
    }

    private class LargeInClauseVisitor : TSqlFragmentVisitor
    {
        public List<AnalysisIssue> Issues { get; } = new();
        private const int Threshold = 20;

        public override void Visit(InPredicate node)
        {
            if (node.Values?.Count > Threshold)
            {
                Issues.Add(new AnalysisIssue
                {
                    RuleId = "PERF004",
                    Message = $"Large IN clause with {node.Values.Count} values may perform poorly.",
                    Severity = IssueSeverity.Warning,
                    Category = IssueCategory.Performance,
                    Line = node.StartLine - 1,
                    Column = node.StartColumn - 1,
                    StartOffset = node.StartOffset,
                    EndOffset = node.StartOffset + node.FragmentLength,
                    Suggestion = "Consider using a temporary table or table-valued parameter instead"
                });
            }
            base.Visit(node);
        }
    }
}

internal class NestedSubqueryRule : IAnalysisRule
{
    public string RuleId => "PERF005";
    public string Name => "Deeply Nested Subquery";
    public string Description => "Deeply nested subqueries can be hard to optimize";
    public IssueCategory Category => IssueCategory.Performance;
    public IssueSeverity DefaultSeverity => IssueSeverity.Info;

    public IEnumerable<AnalysisIssue> Analyze(AnalysisContext context)
    {
        var visitor = new NestedSubqueryVisitor();
        context.Script.Accept(visitor);
        return visitor.Issues;
    }

    private class NestedSubqueryVisitor : TSqlFragmentVisitor
    {
        public List<AnalysisIssue> Issues { get; } = new();
        private int _depth = 0;
        private const int MaxDepth = 3;

        public override void Visit(ScalarSubquery node)
        {
            _depth++;
            if (_depth > MaxDepth)
            {
                Issues.Add(new AnalysisIssue
                {
                    RuleId = "PERF005",
                    Message = $"Subquery nested {_depth} levels deep may impact performance.",
                    Severity = IssueSeverity.Info,
                    Category = IssueCategory.Performance,
                    Line = node.StartLine - 1,
                    Column = node.StartColumn - 1,
                    StartOffset = node.StartOffset,
                    EndOffset = node.StartOffset + node.FragmentLength,
                    Suggestion = "Consider refactoring to use CTEs or JOINs"
                });
            }
            base.Visit(node);
            _depth--;
        }
    }
}

internal class CartesianJoinRule : IAnalysisRule
{
    public string RuleId => "PERF006";
    public string Name => "Potential Cartesian Join";
    public string Description => "CROSS JOIN without filter may cause performance issues";
    public IssueCategory Category => IssueCategory.Performance;
    public IssueSeverity DefaultSeverity => IssueSeverity.Warning;

    public IEnumerable<AnalysisIssue> Analyze(AnalysisContext context)
    {
        var visitor = new CartesianJoinVisitor();
        context.Script.Accept(visitor);
        return visitor.Issues;
    }

    private class CartesianJoinVisitor : TSqlFragmentVisitor
    {
        public List<AnalysisIssue> Issues { get; } = new();

        public override void Visit(UnqualifiedJoin node)
        {
            if (node.UnqualifiedJoinType == UnqualifiedJoinType.CrossJoin)
            {
                Issues.Add(new AnalysisIssue
                {
                    RuleId = "PERF006",
                    Message = "CROSS JOIN will produce a Cartesian product of all rows.",
                    Severity = IssueSeverity.Warning,
                    Category = IssueCategory.Performance,
                    Line = node.StartLine - 1,
                    Column = node.StartColumn - 1,
                    StartOffset = node.StartOffset,
                    EndOffset = node.StartOffset + node.FragmentLength,
                    Suggestion = "Ensure CROSS JOIN is intentional or add a WHERE clause to filter results"
                });
            }
            base.Visit(node);
        }
    }
}

#endregion
