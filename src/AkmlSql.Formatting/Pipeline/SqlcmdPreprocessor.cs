using System.Text;
using System.Text.RegularExpressions;

namespace AkmlSql.Formatting.Pipeline;

/// <summary>
/// Pre-processes SQLCMD directives before SQL formatting and restores them afterward.
/// Replaces line directives (:setvar, :connect, :r, :on error, :!, :exit, :ed, :serverlist,
/// :xml, :list, :listvar, :reset, :error, :out, :perftrace, :quit, :help)
/// with sentinel comments, and replaces inline $(VarName) references with placeholder identifiers.
/// Only processes text outside noformat regions.
/// </summary>
public class SqlcmdPreprocessor
{
    private const string DirectiveSentinelPrefix = "/*__AKML_SQLCMD_DIRECTIVE_";
    private const string DirectiveSentinelSuffix = "__*/";
    private const string VarPlaceholderPrefix = "__AKML_SQLCMD_VAR_";
    private const string VarPlaceholderSuffix = "__";

    // Matches SQLCMD line directives at the start of a line (case-insensitive)
    private static readonly Regex DirectivePattern = new(
        @"^[ \t]*:(setvar|connect|r|on\s+error|!|exit|ed|serverlist|xml|list|listvar|reset|error|out|perftrace|quit|help)\b[^\r\n]*",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    // Matches $(VarName) inline variable references
    private static readonly Regex InlineVarPattern = new(
        @"\$\(([A-Za-z_][A-Za-z0-9_]*)\)",
        RegexOptions.Compiled);

    private readonly List<(string Sentinel, string Original)> _directiveMap = [];
    private readonly List<(string Placeholder, string Original)> _varMap = [];
    private int _directiveCounter;
    #pragma warning disable CS0414 // Field is assigned but never used
    private int _varCounter;
    #pragma warning restore CS0414

    /// <summary>
    /// Replaces SQLCMD directives and inline variables with safe placeholders.
    /// </summary>
    public string Preprocess(string text, List<NoformatRegion>? noformatRegions = null)
    {
        _directiveMap.Clear();
        _varMap.Clear();
        _directiveCounter = 0;
        _varCounter = 0;

        if (string.IsNullOrEmpty(text))
            return text;

        // Step 1: Replace line directives with sentinel comments
        var result = DirectivePattern.Replace(text, m =>
        {
            if (noformatRegions != null && NoformatScanner.IsInNoformatRegion(noformatRegions, m.Index))
                return m.Value;

            var sentinel = $"{DirectiveSentinelPrefix}{_directiveCounter}{DirectiveSentinelSuffix}";
            _directiveMap.Add((sentinel, m.Value));
            _directiveCounter++;
            return sentinel;
        });

        // Step 2: Replace inline $(VarName) with placeholder identifiers
        result = InlineVarPattern.Replace(result, m =>
        {
            if (noformatRegions != null && NoformatScanner.IsInNoformatRegion(noformatRegions, m.Index))
                return m.Value;

            // Check if we already have a placeholder for this variable name
            var varName = m.Groups[1].Value;
            var placeholder = $"{VarPlaceholderPrefix}{varName}{VarPlaceholderSuffix}";

            // Track unique replacements
            if (!_varMap.Any(v => v.Placeholder == placeholder))
                _varMap.Add((placeholder, m.Value));

            return placeholder;
        });

        return result;
    }

    /// <summary>
    /// Restores original SQLCMD directives and variable references from their placeholders.
    /// </summary>
    public string Restore(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var result = text;

        // Restore directives (order matters — replace sentinels with originals)
        foreach (var (sentinel, original) in _directiveMap)
        {
            result = result.Replace(sentinel, original);
        }

        // Restore inline variables
        foreach (var (placeholder, original) in _varMap)
        {
            result = result.Replace(placeholder, original);
        }

        return result;
    }
}
