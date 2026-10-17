namespace AkmlSql.PostgreSql.Completion;

/// <summary>Where in a statement the caret is; decides which providers run (spec 042 FR-061, FR-064).</summary>
public enum PgClause
{
    /// <summary>Nothing recognisable before the caret: an empty statement.</summary>
    None,

    /// <summary>Where a new statement begins: the commands.</summary>
    StatementStart,

    /// <summary><c>WITH</c> and the name of a common table expression.</summary>
    With,

    SelectList,
    From,
    Join,

    /// <summary><c>JOIN ... ON</c> and <c>MERGE ... ON</c> conditions.</summary>
    JoinOn,

    /// <summary><c>JOIN ... USING (</c>: columns the two sides share.</summary>
    JoinUsing,

    Where,
    GroupBy,
    Having,
    Window,
    OrderBy,

    /// <summary><c>LIMIT</c>, <c>OFFSET</c>, <c>FETCH</c>.</summary>
    Limit,

    /// <summary>After <c>UNION</c>, <c>INTERSECT</c>, <c>EXCEPT</c>.</summary>
    SetOperation,

    InsertTarget,
    InsertColumns,
    Values,
    OnConflict,
    OnConflictTarget,
    OnConflictAction,
    UpdateTarget,
    Set,
    DeleteTarget,

    /// <summary><c>DELETE ... USING</c>.</summary>
    Using,

    Returning,
    MergeTarget,
    MergeSource,
    MergeWhen,
    MergeAction,

    /// <summary>The name of an existing relation: <c>ALTER TABLE</c>, <c>DROP TABLE</c>, <c>TRUNCATE</c>, <c>CREATE INDEX ON</c>, <c>REFERENCES</c>.</summary>
    DdlRelation,

    /// <summary>The name of the thing being created: nothing to suggest.</summary>
    DdlName,

    /// <summary>Inside the parentheses of <c>CREATE TABLE</c>, where a column or constraint starts.</summary>
    DdlColumnName,

    /// <summary>After a column name in <c>CREATE TABLE</c> or <c>ALTER TABLE ... ADD COLUMN</c>.</summary>
    DdlColumnType,

    /// <summary>A parenthesised list of existing columns: an index, a key, a foreign key target.</summary>
    DdlColumnList,

    /// <summary>After <c>::</c>, <c>CAST (x AS</c>, <c>RETURNS</c>.</summary>
    TypePosition,

    /// <summary>Some other part of a statement (options, privileges, ...).</summary>
    Other,
}

public enum PgTableRefKind
{
    Table,
    Cte,
    Derived,
    Function,
    Values,
}

/// <summary>
/// One thing a statement reads from or writes to: a table (or view) with its optional alias, a common table expression, a
/// derived table, a function in <c>FROM</c>, a <c>VALUES</c> list.
/// </summary>
/// <param name="Alias">The alias as written (empty when there is none); folded to lower case unless it was quoted.</param>
/// <param name="Schema">The schema as written, or null when the name was not qualified.</param>
/// <param name="Name">The table, view or CTE name; null for a derived table or <c>VALUES</c> list.</param>
/// <param name="Columns">The column names the reference declares itself (an alias column list, a CTE, a derived table); empty when they come from the schema.</param>
/// <param name="StarSources">For a CTE or derived table whose select list holds a star: the tables the star expands over.</param>
public sealed record PgTableRef(
    string Alias,
    string? Schema,
    string? Name,
    PgTableRefKind Kind,
    IReadOnlyList<string> Columns,
    IReadOnlyList<PgTableRef> StarSources,
    bool NameQuoted = false)
{
    /// <summary>The word that qualifies a column of this reference: the alias, else the table name.</summary>
    public string Qualifier => Alias.Length > 0 ? Alias : Name ?? string.Empty;
}

/// <summary>What the analyzer found at the caret.</summary>
public sealed record PgCursorContext
{
    /// <summary>The caret is inside a string, a comment, a number or a meta-command: nothing is offered.</summary>
    public bool Suppressed { get; init; }

    /// <summary>The word being typed up to the caret, without a leading double quote.</summary>
    public string Prefix { get; init; } = string.Empty;

    /// <summary>Where the word being typed starts (the opening quote included); equal to the caret when there is none.</summary>
    public int PrefixStart { get; init; }

    /// <summary>The word began with a double quote.</summary>
    public bool PrefixQuoted { get; init; }

    /// <summary>The caret offset the context was built for.</summary>
    public int Caret { get; init; }

    /// <summary>The names before the dot at the caret: <c>[o]</c> for <c>o.</c>, <c>[sales, orders]</c> for <c>sales.orders.</c>.</summary>
    public IReadOnlyList<PgName> DotChain { get; init; } = Array.Empty<PgName>();

    public bool AfterDot => DotChain.Count > 0;

    public PgClause Clause { get; init; } = PgClause.None;

    /// <summary>SELECT, INSERT, UPDATE, DELETE, MERGE, CREATE, ... of the statement the caret is in (upper case), or empty.</summary>
    public string Verb { get; init; } = string.Empty;

    /// <summary>
    /// The previous token completes an operand (a name, a constant, a closing parenthesis): what follows is an operator, a
    /// keyword or the next list item, not another operand.
    /// </summary>
    public bool AfterOperand { get; init; }

    /// <summary>The words before the one being typed, upper case, nearest last (at most six), for keyword decisions.</summary>
    public IReadOnlyList<string> PrecedingWords { get; init; } = Array.Empty<string>();

    /// <summary>The caret is inside the parentheses of a function call (<c>date_trunc(</c>): its name, schema and argument index.</summary>
    public string? CallName { get; init; }

    public string? CallSchema { get; init; }

    public int CallArgument { get; init; }

    /// <summary>The caret is after <c>::</c> or in the type of a <c>CAST</c>.</summary>
    public bool InTypePosition => Clause == PgClause.TypePosition;

    /// <summary>Everything the statement reads from that the caret can see, innermost query first.</summary>
    public IReadOnlyList<PgTableRef> VisibleTables { get; init; } = Array.Empty<PgTableRef>();

    /// <summary>How many of <see cref="VisibleTables"/> belong to the innermost query (the rest come from queries around it).</summary>
    public int InnerTableCount { get; init; }

    /// <summary>Common table expressions visible at the caret.</summary>
    public IReadOnlyList<PgTableRef> VisibleCtes { get; init; } = Array.Empty<PgTableRef>();

    /// <summary>The table an INSERT, UPDATE, DELETE or MERGE writes to.</summary>
    public PgTableRef? Target { get; init; }

    /// <summary>For a join condition: the table just joined (the right-hand side), and the ones before it.</summary>
    public PgTableRef? JoinTarget { get; init; }

    public IReadOnlyList<PgTableRef> JoinLeft { get; init; } = Array.Empty<PgTableRef>();

    /// <summary>For an index, key or foreign key column list: the relation the columns belong to.</summary>
    public PgTableRef? ListOwner { get; init; }

    /// <summary>Column names declared by the <c>CREATE TABLE</c> the caret is in, for its constraints.</summary>
    public IReadOnlyList<string> DeclaredColumns { get; init; } = Array.Empty<string>();

    /// <summary>The caret is inside the parentheses of OVER ( ... ) or WINDOW w AS ( ... ).</summary>
    public bool InWindowSpec { get; init; }

    /// <summary>The kind of object a CREATE, ALTER or DROP statement is about (TABLE, INDEX, VIEW, ...), or empty.</summary>
    public string DdlObject { get; init; } = string.Empty;

    /// <summary>The caret is inside the parentheses of CREATE TABLE, at or after a column definition.</summary>
    public bool InTableBody { get; init; }

    /// <summary>The caret is inside a function-body dollar quote and the context describes the body.</summary>
    public bool InDollarBody { get; init; }

    /// <summary><c>ON CONFLICT</c> is the clause and <c>EXCLUDED</c> is available.</summary>
    public bool ExcludedAvailable { get; init; }
}

/// <summary>An identifier as written: its text without quotes, and whether it was double-quoted.</summary>
public readonly record struct PgName(string Text, bool Quoted)
{
    /// <summary>The name as the server will look it up: quoted names keep their case, others fold to lower case.</summary>
    public string Folded => Quoted ? Text : Text.ToLowerInvariant();

    public bool Matches(string catalogName) =>
        Quoted ? string.Equals(Text, catalogName, StringComparison.Ordinal) : string.Equals(Folded, catalogName, StringComparison.Ordinal);

    public override string ToString() => Quoted ? "\"" + Text + "\"" : Text;
}
