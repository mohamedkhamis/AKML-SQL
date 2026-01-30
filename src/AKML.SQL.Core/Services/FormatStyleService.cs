using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Service for managing SQL formatting styles and presets.
/// Implements Story 6.1: Formatting Style Management.
/// </summary>
public class FormatStyleService : IFormatStyleService
{
    private readonly ILogger<FormatStyleService> _logger;
    private readonly Dictionary<string, FormatStyle> _customStyles;
    private static readonly Dictionary<string, FormatStyle> _builtInStyles;
    private static readonly JsonSerializerOptions _jsonOptions;

    static FormatStyleService()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        _builtInStyles = new Dictionary<string, FormatStyle>(StringComparer.OrdinalIgnoreCase)
        {
            ["Compact"] = CreateCompactStyle(),
            ["Standard"] = CreateStandardStyle(),
            ["Expanded"] = CreateExpandedStyle()
        };
    }

    public FormatStyleService(ILogger<FormatStyleService> logger)
    {
        _logger = logger;
        _customStyles = new Dictionary<string, FormatStyle>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets all available format styles (built-in and custom).
    /// </summary>
    public IReadOnlyList<FormatStyle> GetAllStyles()
    {
        var styles = new List<FormatStyle>(_builtInStyles.Values);
        styles.AddRange(_customStyles.Values);
        return styles;
    }

    /// <summary>
    /// Gets a format style by name.
    /// </summary>
    public FormatStyle? GetStyle(string name)
    {
        if (_builtInStyles.TryGetValue(name, out var builtIn))
            return builtIn;

        if (_customStyles.TryGetValue(name, out var custom))
            return custom;

        return null;
    }

    /// <summary>
    /// Gets the default format style (Standard).
    /// </summary>
    public FormatStyle GetDefaultStyle() => _builtInStyles["Standard"];

    /// <summary>
    /// Adds or updates a custom format style.
    /// </summary>
    public void SaveCustomStyle(FormatStyle style)
    {
        if (string.IsNullOrWhiteSpace(style.Name))
            throw new ArgumentException("Style name cannot be empty", nameof(style));

        if (_builtInStyles.ContainsKey(style.Name))
            throw new InvalidOperationException($"Cannot overwrite built-in style: {style.Name}");

        style.IsBuiltIn = false;
        _customStyles[style.Name] = style;
        _logger.LogInformation("Saved custom format style: {StyleName}", style.Name);
    }

    /// <summary>
    /// Deletes a custom format style.
    /// </summary>
    public bool DeleteCustomStyle(string name)
    {
        if (_builtInStyles.ContainsKey(name))
            throw new InvalidOperationException($"Cannot delete built-in style: {name}");

        var removed = _customStyles.Remove(name);
        if (removed)
            _logger.LogInformation("Deleted custom format style: {StyleName}", name);

        return removed;
    }

    /// <summary>
    /// Exports a format style to JSON.
    /// </summary>
    public string ExportStyle(string name)
    {
        var style = GetStyle(name);
        if (style == null)
            throw new ArgumentException($"Style not found: {name}", nameof(name));

        return JsonSerializer.Serialize(style, _jsonOptions);
    }

    /// <summary>
    /// Imports a format style from JSON.
    /// </summary>
    public FormatStyle ImportStyle(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON cannot be empty", nameof(json));

        var style = JsonSerializer.Deserialize<FormatStyle>(json, _jsonOptions);
        if (style == null)
            throw new InvalidOperationException("Failed to deserialize format style");

        style.IsBuiltIn = false;
        return style;
    }

    /// <summary>
    /// Imports and saves a format style from JSON.
    /// </summary>
    public FormatStyle ImportAndSaveStyle(string json, string? newName = null)
    {
        var style = ImportStyle(json);

        if (!string.IsNullOrWhiteSpace(newName))
            style.Name = newName;

        SaveCustomStyle(style);
        return style;
    }

    /// <summary>
    /// Converts a FormatStyle to SqlScriptGeneratorOptions.
    /// </summary>
    public SqlScriptGeneratorOptions ToGeneratorOptions(FormatStyle style)
    {
        return new SqlScriptGeneratorOptions
        {
            AlignClauseBodies = style.AlignClauseBodies,
            AlignColumnDefinitionFields = style.AlignColumnDefinitions,
            AlignSetClauseItem = style.AlignSetClauses,
            AsKeywordOnOwnLine = style.AsKeywordOnOwnLine,
            IncludeSemicolons = style.IncludeSemicolons,
            IndentationSize = style.IndentSize,
            IndentSetClause = style.IndentSetClause,
            IndentViewBody = style.IndentViewBody,
            KeywordCasing = ConvertKeywordCasing(style.KeywordCasing),
            MultilineInsertSourcesList = style.MultilineInsertSourcesList,
            MultilineInsertTargetsList = style.MultilineInsertTargetsList,
            MultilineSelectElementsList = style.MultilineSelectElementsList,
            MultilineSetClauseItems = style.MultilineSetClauseItems,
            MultilineViewColumnsList = style.MultilineViewColumnsList,
            MultilineWherePredicatesList = style.MultilineWherePredicatesList,
            NewLineBeforeCloseParenthesisInMultilineList = style.NewLineBeforeCloseParenthesis,
            NewLineBeforeFromClause = style.NewLineBeforeFromClause,
            NewLineBeforeGroupByClause = style.NewLineBeforeGroupByClause,
            NewLineBeforeHavingClause = style.NewLineBeforeHavingClause,
            NewLineBeforeJoinClause = style.NewLineBeforeJoinClause,
            NewLineBeforeOffsetClause = style.NewLineBeforeOffsetClause,
            NewLineBeforeOpenParenthesisInMultilineList = style.NewLineBeforeOpenParenthesis,
            NewLineBeforeOrderByClause = style.NewLineBeforeOrderByClause,
            NewLineBeforeOutputClause = style.NewLineBeforeOutputClause,
            NewLineBeforeWhereClause = style.NewLineBeforeWhereClause,
            SqlVersion = SqlVersion.Sql160
        };
    }

    /// <summary>
    /// Creates a FormatStyle from SqlScriptGeneratorOptions.
    /// </summary>
    public FormatStyle FromGeneratorOptions(SqlScriptGeneratorOptions options, string name)
    {
        return new FormatStyle
        {
            Name = name,
            IsBuiltIn = false,
            AlignClauseBodies = options.AlignClauseBodies,
            AlignColumnDefinitions = options.AlignColumnDefinitionFields,
            AlignSetClauses = options.AlignSetClauseItem,
            AsKeywordOnOwnLine = options.AsKeywordOnOwnLine,
            IncludeSemicolons = options.IncludeSemicolons,
            IndentSize = options.IndentationSize,
            IndentSetClause = options.IndentSetClause,
            IndentViewBody = options.IndentViewBody,
            KeywordCasing = ConvertFromScriptDomCasing(options.KeywordCasing),
            MultilineInsertSourcesList = options.MultilineInsertSourcesList,
            MultilineInsertTargetsList = options.MultilineInsertTargetsList,
            MultilineSelectElementsList = options.MultilineSelectElementsList,
            MultilineSetClauseItems = options.MultilineSetClauseItems,
            MultilineViewColumnsList = options.MultilineViewColumnsList,
            MultilineWherePredicatesList = options.MultilineWherePredicatesList,
            NewLineBeforeCloseParenthesis = options.NewLineBeforeCloseParenthesisInMultilineList,
            NewLineBeforeFromClause = options.NewLineBeforeFromClause,
            NewLineBeforeGroupByClause = options.NewLineBeforeGroupByClause,
            NewLineBeforeHavingClause = options.NewLineBeforeHavingClause,
            NewLineBeforeJoinClause = options.NewLineBeforeJoinClause,
            NewLineBeforeOffsetClause = options.NewLineBeforeOffsetClause,
            NewLineBeforeOpenParenthesis = options.NewLineBeforeOpenParenthesisInMultilineList,
            NewLineBeforeOrderByClause = options.NewLineBeforeOrderByClause,
            NewLineBeforeOutputClause = options.NewLineBeforeOutputClause,
            NewLineBeforeWhereClause = options.NewLineBeforeWhereClause
        };
    }

    private static KeywordCasing ConvertKeywordCasing(FormatKeywordCasing casing)
    {
        return casing switch
        {
            FormatKeywordCasing.Uppercase => KeywordCasing.Uppercase,
            FormatKeywordCasing.Lowercase => KeywordCasing.Lowercase,
            FormatKeywordCasing.PascalCase => KeywordCasing.PascalCase,
            _ => KeywordCasing.Uppercase
        };
    }

    private static FormatKeywordCasing ConvertFromScriptDomCasing(KeywordCasing casing)
    {
        return casing switch
        {
            KeywordCasing.Uppercase => FormatKeywordCasing.Uppercase,
            KeywordCasing.Lowercase => FormatKeywordCasing.Lowercase,
            KeywordCasing.PascalCase => FormatKeywordCasing.PascalCase,
            _ => FormatKeywordCasing.Uppercase
        };
    }

    #region Built-in Style Definitions

    private static FormatStyle CreateCompactStyle()
    {
        return new FormatStyle
        {
            Name = "Compact",
            Description = "Minimal whitespace, single-line where possible",
            IsBuiltIn = true,
            IndentSize = 2,
            UseTabs = false,
            KeywordCasing = FormatKeywordCasing.Uppercase,
            AlignClauseBodies = false,
            AlignColumnDefinitions = false,
            AlignSetClauses = false,
            AsKeywordOnOwnLine = false,
            IncludeSemicolons = false,
            IndentSetClause = false,
            IndentViewBody = false,
            MultilineInsertSourcesList = false,
            MultilineInsertTargetsList = false,
            MultilineSelectElementsList = false,
            MultilineSetClauseItems = false,
            MultilineViewColumnsList = false,
            MultilineWherePredicatesList = false,
            NewLineBeforeCloseParenthesis = false,
            NewLineBeforeFromClause = false,
            NewLineBeforeGroupByClause = false,
            NewLineBeforeHavingClause = false,
            NewLineBeforeJoinClause = false,
            NewLineBeforeOffsetClause = false,
            NewLineBeforeOpenParenthesis = false,
            NewLineBeforeOrderByClause = false,
            NewLineBeforeOutputClause = false,
            NewLineBeforeWhereClause = false
        };
    }

    private static FormatStyle CreateStandardStyle()
    {
        return new FormatStyle
        {
            Name = "Standard",
            Description = "Balanced formatting with clause alignment",
            IsBuiltIn = true,
            IndentSize = 4,
            UseTabs = false,
            KeywordCasing = FormatKeywordCasing.Uppercase,
            AlignClauseBodies = true,
            AlignColumnDefinitions = true,
            AlignSetClauses = true,
            AsKeywordOnOwnLine = false,
            IncludeSemicolons = true,
            IndentSetClause = true,
            IndentViewBody = true,
            MultilineInsertSourcesList = true,
            MultilineInsertTargetsList = true,
            MultilineSelectElementsList = true,
            MultilineSetClauseItems = true,
            MultilineViewColumnsList = true,
            MultilineWherePredicatesList = true,
            NewLineBeforeCloseParenthesis = true,
            NewLineBeforeFromClause = true,
            NewLineBeforeGroupByClause = true,
            NewLineBeforeHavingClause = true,
            NewLineBeforeJoinClause = true,
            NewLineBeforeOffsetClause = true,
            NewLineBeforeOpenParenthesis = false,
            NewLineBeforeOrderByClause = true,
            NewLineBeforeOutputClause = true,
            NewLineBeforeWhereClause = true
        };
    }

    private static FormatStyle CreateExpandedStyle()
    {
        return new FormatStyle
        {
            Name = "Expanded",
            Description = "Maximum readability with extensive whitespace",
            IsBuiltIn = true,
            IndentSize = 4,
            UseTabs = false,
            KeywordCasing = FormatKeywordCasing.Uppercase,
            AlignClauseBodies = true,
            AlignColumnDefinitions = true,
            AlignSetClauses = true,
            AsKeywordOnOwnLine = true,
            IncludeSemicolons = true,
            IndentSetClause = true,
            IndentViewBody = true,
            MultilineInsertSourcesList = true,
            MultilineInsertTargetsList = true,
            MultilineSelectElementsList = true,
            MultilineSetClauseItems = true,
            MultilineViewColumnsList = true,
            MultilineWherePredicatesList = true,
            NewLineBeforeCloseParenthesis = true,
            NewLineBeforeFromClause = true,
            NewLineBeforeGroupByClause = true,
            NewLineBeforeHavingClause = true,
            NewLineBeforeJoinClause = true,
            NewLineBeforeOffsetClause = true,
            NewLineBeforeOpenParenthesis = true,
            NewLineBeforeOrderByClause = true,
            NewLineBeforeOutputClause = true,
            NewLineBeforeWhereClause = true
        };
    }

    #endregion
}

/// <summary>
/// Interface for format style service.
/// </summary>
public interface IFormatStyleService
{
    IReadOnlyList<FormatStyle> GetAllStyles();
    FormatStyle? GetStyle(string name);
    FormatStyle GetDefaultStyle();
    void SaveCustomStyle(FormatStyle style);
    bool DeleteCustomStyle(string name);
    string ExportStyle(string name);
    FormatStyle ImportStyle(string json);
    FormatStyle ImportAndSaveStyle(string json, string? newName = null);
    SqlScriptGeneratorOptions ToGeneratorOptions(FormatStyle style);
    FormatStyle FromGeneratorOptions(SqlScriptGeneratorOptions options, string name);
}

/// <summary>
/// Represents a SQL formatting style configuration.
/// </summary>
public class FormatStyle
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsBuiltIn { get; set; }

    // Basic formatting
    public int IndentSize { get; set; } = 4;
    public bool UseTabs { get; set; } = false;
    public FormatKeywordCasing KeywordCasing { get; set; } = FormatKeywordCasing.Uppercase;

    // Alignment options
    public bool AlignClauseBodies { get; set; } = true;
    public bool AlignColumnDefinitions { get; set; } = true;
    public bool AlignSetClauses { get; set; } = true;

    // Keyword placement
    public bool AsKeywordOnOwnLine { get; set; } = false;
    public bool IncludeSemicolons { get; set; } = true;
    public bool IndentSetClause { get; set; } = true;
    public bool IndentViewBody { get; set; } = true;

    // Multiline options
    public bool MultilineInsertSourcesList { get; set; } = true;
    public bool MultilineInsertTargetsList { get; set; } = true;
    public bool MultilineSelectElementsList { get; set; } = true;
    public bool MultilineSetClauseItems { get; set; } = true;
    public bool MultilineViewColumnsList { get; set; } = true;
    public bool MultilineWherePredicatesList { get; set; } = true;

    // New line options
    public bool NewLineBeforeCloseParenthesis { get; set; } = true;
    public bool NewLineBeforeFromClause { get; set; } = true;
    public bool NewLineBeforeGroupByClause { get; set; } = true;
    public bool NewLineBeforeHavingClause { get; set; } = true;
    public bool NewLineBeforeJoinClause { get; set; } = true;
    public bool NewLineBeforeOffsetClause { get; set; } = true;
    public bool NewLineBeforeOpenParenthesis { get; set; } = false;
    public bool NewLineBeforeOrderByClause { get; set; } = true;
    public bool NewLineBeforeOutputClause { get; set; } = true;
    public bool NewLineBeforeWhereClause { get; set; } = true;

    /// <summary>
    /// Creates a clone of this style.
    /// </summary>
    public FormatStyle Clone()
    {
        return new FormatStyle
        {
            Name = Name,
            Description = Description,
            IsBuiltIn = false,
            IndentSize = IndentSize,
            UseTabs = UseTabs,
            KeywordCasing = KeywordCasing,
            AlignClauseBodies = AlignClauseBodies,
            AlignColumnDefinitions = AlignColumnDefinitions,
            AlignSetClauses = AlignSetClauses,
            AsKeywordOnOwnLine = AsKeywordOnOwnLine,
            IncludeSemicolons = IncludeSemicolons,
            IndentSetClause = IndentSetClause,
            IndentViewBody = IndentViewBody,
            MultilineInsertSourcesList = MultilineInsertSourcesList,
            MultilineInsertTargetsList = MultilineInsertTargetsList,
            MultilineSelectElementsList = MultilineSelectElementsList,
            MultilineSetClauseItems = MultilineSetClauseItems,
            MultilineViewColumnsList = MultilineViewColumnsList,
            MultilineWherePredicatesList = MultilineWherePredicatesList,
            NewLineBeforeCloseParenthesis = NewLineBeforeCloseParenthesis,
            NewLineBeforeFromClause = NewLineBeforeFromClause,
            NewLineBeforeGroupByClause = NewLineBeforeGroupByClause,
            NewLineBeforeHavingClause = NewLineBeforeHavingClause,
            NewLineBeforeJoinClause = NewLineBeforeJoinClause,
            NewLineBeforeOffsetClause = NewLineBeforeOffsetClause,
            NewLineBeforeOpenParenthesis = NewLineBeforeOpenParenthesis,
            NewLineBeforeOrderByClause = NewLineBeforeOrderByClause,
            NewLineBeforeOutputClause = NewLineBeforeOutputClause,
            NewLineBeforeWhereClause = NewLineBeforeWhereClause
        };
    }
}

/// <summary>
/// Keyword casing options for formatting.
/// </summary>
public enum FormatKeywordCasing
{
    Uppercase,
    Lowercase,
    PascalCase
}
