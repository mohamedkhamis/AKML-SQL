namespace AkmlSql.PostgreSql.ReferenceGen;

/// <summary>
/// The lexical rules of the language (FR-050), written by hand because they are prose in <c>syntax.sgml</c>, not a table or a
/// catalog. Each carries the section it summarises; the generator finds that section's page in syntax.sgml, so the link goes
/// to the page that explains it, or to the section's anchor on that page.
/// </summary>
public static class LexicalEntries
{
    /// <summary>(form, aliases, summary, syntax.sgml section id).</summary>
    public static readonly IReadOnlyList<(string Name, string[] Aliases, string Summary, string SectionId)> All = new (string, string[], string, string)[]
    {
        ("identifier", new[] { "unquoted identifier" },
            "An unquoted name is folded to lower case; it starts with a letter or underscore and continues with letters, digits, underscores and dollar signs.", "sql-syntax-identifiers"),
        ("\"identifier\"", new[] { "quoted identifier", "delimited identifier" },
            "A name in double quotes keeps its case and may contain any character; a double quote inside is written twice.", "sql-syntax-identifiers"),
        ("'string'", new[] { "string constant" },
            "A string constant is in single quotes; a single quote inside is written twice. Two constants separated by a newline are joined.", "sql-syntax-strings"),
        ("E''", new[] { "escape string constant" },
            "A string constant prefixed with E in which a backslash starts an escape such as \\n, \\t, \\xhh or \\uxxxx.", "sql-syntax-strings-escape"),
        ("U&''", new[] { "unicode escape string constant" },
            "A string constant prefixed with U& in which \\xxxx or \\+xxxxxx writes a Unicode character; UESCAPE changes the escape character.", "sql-syntax-strings-uescape"),
        ("$$", new[] { "dollar-quoted string constant", "$tag$" },
            "A string constant delimited by $$ or $tag$ that contains quotes and backslashes unchanged; function bodies are usually written this way.", "sql-syntax-dollar-quoting"),
        ("B''", new[] { "bit-string constant", "X''" },
            "B'1001' is a bit string of binary digits; X'1FF' is a bit string of hexadecimal digits.", "sql-syntax-bit-strings"),
        ("numeric constant", new[] { "number" },
            "Digits with an optional decimal point and exponent; 0x, 0o and 0b prefixes and underscores between digits are accepted (PostgreSQL 16 and later).", "sql-syntax-constants-numeric"),
        ("type 'string'", new[] { "constant of other types" },
            "A constant of any type is written as the type name followed by a string, for example date '2020-01-31'; it is the same as a cast.", "sql-syntax-constants-generic"),
        ("operator", new[] { "operator name" },
            "A name of up to 63 characters from + - * / < > = ~ ! @ # % ^ & | ` ?, with rules about which may end it.", "sql-syntax-operators"),
        ("special character", new[] { "$ ( ) [ ] , ; : * ." },
            "Characters that are not operators: $ for parameters, parentheses for grouping and calls, brackets for arrays, commas, semicolons, colons and the dot.", "sql-syntax-special-chars"),
        ("--", new[] { "/* */", "comment" },
            "-- starts a comment that runs to the end of the line; /* ... */ is a comment that may span lines and may be nested.", "sql-syntax-comments"),
        ("$n", new[] { "positional parameter" },
            "$1, $2, ... refer to the arguments of a function body or of a prepared statement.", "sql-expressions-parameters-positional"),
        ("::", new[] { "CAST", "type cast" },
            "expression::type converts a value to a type; CAST(expression AS type) is the standard form of the same thing.", "sql-syntax-type-casts"),
        ("ARRAY[]", new[] { "array constructor" },
            "ARRAY[1, 2, 3] builds an array from expressions; ARRAY(subquery) builds one from a query's rows.", "sql-syntax-array-constructors"),
        ("ROW()", new[] { "row constructor" },
            "ROW(a, b) or (a, b) builds a composite value from fields.", "sql-syntax-row-constructors"),
        ("[]", new[] { "subscript" },
            "array[index] picks one element and array[lower:upper] a slice; arrays are 1-based unless declared otherwise.", "sql-expressions-subscripts"),
        ("field selection", new[] { "(composite).field" },
            "(row_expression).field_name selects a field of a composite value; the parentheses are required unless the expression is a table reference.", "field-selection"),
        ("COLLATE", new[] { "collation expression" },
            "expression COLLATE collation overrides the collation used to compare or sort a character value.", "sql-syntax-collate-exprs"),
        ("=>", new[] { "named argument", ":=" },
            "name => value passes a function argument by name; the older := is also accepted.", "sql-syntax-calling-funcs"),
        ("operator precedence", new[] { "precedence" },
            "The order in which operators bind, from the highest (. and ::) to the lowest (OR).", "sql-precedence"),
    };
}
