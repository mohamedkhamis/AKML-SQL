using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Engine.Completion.Dictionaries;

/// <summary>
/// T060: Static dictionary of T-SQL built-in function signatures.
/// Each entry maps a function name to a list of overloads with parameter information.
/// </summary>
public static class BuiltinFunctionDictionary
{
    private static readonly Dictionary<string, SignatureOverload[]?> Functions =
        new(StringComparer.OrdinalIgnoreCase);

    static BuiltinFunctionDictionary()
    {
        // Conversion functions
        Register("CONVERT", [
            Overload("CONVERT(data_type, expression)", "Converts expression to data_type.",
                Param("data_type", "data_type", "Target data type"),
                Param("expression", "expression", "Value to convert")),
            Overload("CONVERT(data_type, expression, style)", "Converts expression to data_type with formatting style.",
                Param("data_type", "data_type", "Target data type"),
                Param("expression", "expression", "Value to convert"),
                Param("style", "int", "Format style code", isOptional: true))
        ]);

        Register("CAST", [
            Overload("CAST(expression AS data_type)", "Converts expression to data_type.",
                Param("expression", "expression", "Value to convert"),
                Param("data_type", "data_type", "Target data type (after AS keyword)"))
        ]);

        Register("TRY_CONVERT", [
            Overload("TRY_CONVERT(data_type, expression)", "Converts expression to data_type, returns NULL on failure.",
                Param("data_type", "data_type", "Target data type"),
                Param("expression", "expression", "Value to convert")),
            Overload("TRY_CONVERT(data_type, expression, style)", "Converts with style, returns NULL on failure.",
                Param("data_type", "data_type", "Target data type"),
                Param("expression", "expression", "Value to convert"),
                Param("style", "int", "Format style code", isOptional: true))
        ]);

        Register("TRY_CAST", [
            Overload("TRY_CAST(expression AS data_type)", "Converts expression to data_type, returns NULL on failure.",
                Param("expression", "expression", "Value to convert"),
                Param("data_type", "data_type", "Target data type (after AS keyword)"))
        ]);

        // Date functions
        Register("DATEADD", [
            Overload("DATEADD(datepart, number, date)", "Adds a specified number interval to a date.",
                Param("datepart", "datepart", "Part of date to add to (year, month, day, etc.)"),
                Param("number", "int", "Number of intervals to add"),
                Param("date", "datetime", "Date to modify"))
        ]);

        Register("DATEDIFF", [
            Overload("DATEDIFF(datepart, startdate, enddate)", "Returns the count of datepart boundaries crossed between two dates.",
                Param("datepart", "datepart", "Part of date to compute difference (year, month, day, etc.)"),
                Param("startdate", "datetime", "Start date"),
                Param("enddate", "datetime", "End date"))
        ]);

        Register("DATEPART", [
            Overload("DATEPART(datepart, date)", "Returns an integer representing the specified datepart of the date.",
                Param("datepart", "datepart", "Part of date to extract (year, month, day, etc.)"),
                Param("date", "datetime", "Date to extract part from"))
        ]);

        Register("FORMAT", [
            Overload("FORMAT(value, format)", "Formats a value with the specified format string.",
                Param("value", "expression", "Value to format"),
                Param("format", "nvarchar", "Format string")),
            Overload("FORMAT(value, format, culture)", "Formats a value with the specified format and culture.",
                Param("value", "expression", "Value to format"),
                Param("format", "nvarchar", "Format string"),
                Param("culture", "nvarchar", "Culture string (e.g., 'en-US')", isOptional: true))
        ]);

        Register("GETDATE", [
            Overload("GETDATE()", "Returns the current database system date and time as datetime.")
        ]);

        Register("GETUTCDATE", [
            Overload("GETUTCDATE()", "Returns the current UTC date and time as datetime.")
        ]);

        // Null handling
        Register("COALESCE", [
            Overload("COALESCE(expression1, expression2, ...)", "Returns the first non-null expression.",
                Param("expression1", "expression", "First expression to evaluate"),
                Param("expression2", "expression", "Second expression to evaluate"),
                Param("...", "expression", "Additional expressions", isOptional: true))
        ]);

        Register("ISNULL", [
            Overload("ISNULL(check_expression, replacement_value)", "Replaces NULL with the specified replacement value.",
                Param("check_expression", "expression", "Expression to check for NULL"),
                Param("replacement_value", "expression", "Value to return if check is NULL"))
        ]);

        Register("NULLIF", [
            Overload("NULLIF(expression1, expression2)", "Returns NULL if the two expressions are equal.",
                Param("expression1", "expression", "First expression"),
                Param("expression2", "expression", "Second expression"))
        ]);

        Register("IIF", [
            Overload("IIF(boolean_expression, true_value, false_value)", "Returns one of two values depending on a boolean condition.",
                Param("boolean_expression", "bit", "Condition to evaluate"),
                Param("true_value", "expression", "Value if condition is true"),
                Param("false_value", "expression", "Value if condition is false"))
        ]);

        Register("CHOOSE", [
            Overload("CHOOSE(index, val1, val2, ...)", "Returns the item at the specified index from a list of values.",
                Param("index", "int", "1-based index into the value list"),
                Param("val1", "expression", "First value"),
                Param("val2", "expression", "Second value"),
                Param("...", "expression", "Additional values", isOptional: true))
        ]);

        // String functions
        Register("SUBSTRING", [
            Overload("SUBSTRING(expression, start, length)", "Returns part of a string.",
                Param("expression", "varchar/nvarchar", "Source string"),
                Param("start", "int", "Starting position (1-based)"),
                Param("length", "int", "Number of characters to return"))
        ]);

        Register("LEFT", [
            Overload("LEFT(expression, count)", "Returns the left part of a string with the specified number of characters.",
                Param("expression", "varchar/nvarchar", "Source string"),
                Param("count", "int", "Number of characters to return"))
        ]);

        Register("RIGHT", [
            Overload("RIGHT(expression, count)", "Returns the right part of a string with the specified number of characters.",
                Param("expression", "varchar/nvarchar", "Source string"),
                Param("count", "int", "Number of characters to return"))
        ]);

        Register("LEN", [
            Overload("LEN(expression)", "Returns the number of characters in a string (excluding trailing spaces).",
                Param("expression", "varchar/nvarchar", "String to measure"))
        ]);

        Register("CHARINDEX", [
            Overload("CHARINDEX(substring, string)", "Returns the starting position of a substring.",
                Param("substring", "varchar/nvarchar", "Substring to search for"),
                Param("string", "varchar/nvarchar", "String to search in")),
            Overload("CHARINDEX(substring, string, start_location)", "Returns the starting position of a substring, starting from a position.",
                Param("substring", "varchar/nvarchar", "Substring to search for"),
                Param("string", "varchar/nvarchar", "String to search in"),
                Param("start_location", "int", "Position to start searching from", isOptional: true))
        ]);

        Register("REPLACE", [
            Overload("REPLACE(string, old_string, new_string)", "Replaces all occurrences of a substring.",
                Param("string", "varchar/nvarchar", "Source string"),
                Param("old_string", "varchar/nvarchar", "Substring to replace"),
                Param("new_string", "varchar/nvarchar", "Replacement substring"))
        ]);

        Register("STUFF", [
            Overload("STUFF(string, start, length, insert_string)", "Deletes and inserts characters in a string.",
                Param("string", "varchar/nvarchar", "Source string"),
                Param("start", "int", "Starting position (1-based)"),
                Param("length", "int", "Number of characters to delete"),
                Param("insert_string", "varchar/nvarchar", "String to insert"))
        ]);

        Register("STRING_AGG", [
            Overload("STRING_AGG(expression, separator)", "Concatenates string values with a separator.",
                Param("expression", "varchar/nvarchar", "Expression to concatenate"),
                Param("separator", "varchar/nvarchar", "Separator string"))
        ]);

        // Math functions
        Register("ROUND", [
            Overload("ROUND(numeric_expression, length)", "Rounds a numeric value to the specified length.",
                Param("numeric_expression", "numeric", "Value to round"),
                Param("length", "int", "Precision to round to")),
            Overload("ROUND(numeric_expression, length, function)", "Rounds or truncates a numeric value.",
                Param("numeric_expression", "numeric", "Value to round"),
                Param("length", "int", "Precision to round to"),
                Param("function", "int", "0=round, non-zero=truncate", isOptional: true))
        ]);

        Register("ABS", [
            Overload("ABS(numeric_expression)", "Returns the absolute value.",
                Param("numeric_expression", "numeric", "Value to get absolute of"))
        ]);

        Register("CEILING", [
            Overload("CEILING(numeric_expression)", "Returns the smallest integer greater than or equal to the expression.",
                Param("numeric_expression", "numeric", "Value to ceiling"))
        ]);

        Register("FLOOR", [
            Overload("FLOOR(numeric_expression)", "Returns the largest integer less than or equal to the expression.",
                Param("numeric_expression", "numeric", "Value to floor"))
        ]);
    }

    /// <summary>
    /// Try to get the signature overloads for a built-in function.
    /// </summary>
    public static bool TryGetSignature(string functionName, out SignatureOverload[]? overloads)
    {
        return Functions.TryGetValue(functionName, out overloads);
    }

    /// <summary>
    /// Check if a function name is a known built-in.
    /// </summary>
    public static bool IsBuiltinFunction(string functionName)
    {
        return Functions.ContainsKey(functionName);
    }

    /// <summary>
    /// Get all registered function names.
    /// </summary>
    public static IEnumerable<string> GetAllFunctionNames()
    {
        return Functions.Keys;
    }

    private static void Register(string name, SignatureOverload[] overloads)
    {
        Functions[name] = overloads;
    }

    private static SignatureOverload Overload(string label, string doc, params ParameterInfo[] parameters)
    {
        return new SignatureOverload
        {
            Label = label,
            Documentation = doc,
            Parameters = parameters
        };
    }

    private static ParameterInfo Param(string name, string type, string doc, bool isOptional = false)
    {
        return new ParameterInfo
        {
            Name = name,
            Type = type,
            Documentation = doc,
            IsOptional = isOptional
        };
    }
}
