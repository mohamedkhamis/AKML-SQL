using AkmlSql.Core.Models.Analysis;
using AkmlSql.PostgreSql.Language;
using PgSqlParser;

namespace AkmlSql.PostgreSql.Analysis.Rules.Security;

// Spec 042 (task T165, FR-150, FR-153): the security rules PGSE001 to PGSE005.

/// <summary>PGSE001 - dynamic SQL in a PL/pgSQL body built by joining strings.</summary>
public sealed class Pgse001DynamicSqlConcatenation : PgRuleBase
{
    public Pgse001DynamicSqlConcatenation() : base("PGSE001", "Security", "Dynamic SQL built by concatenation", DiagnosticSeverity.Warning,
        "EXECUTE runs a string as SQL. Building that string with || puts values and names into the command text, which is how SQL injection happens. "
        + "format() with %I (identifier) and %L (literal), and EXECUTE ... USING for values, quote every part correctly. Only function and DO bodies are examined.",
        "plpgsql-statements#plpgsql-statements-executing-dyn") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            if (!IsPlpgsqlBody(s)) continue;

            foreach (var token in s.Tokens.Sig)
            {
                if (token.Kind != PgTokenKind.DollarString || !token.Token.Terminated) continue;

                var text = token.Text;
                var tagLength = text.IndexOf('$', 1) + 1;
                if (tagLength < 2 || text.Length < tagLength * 2) continue;

                var body = text.Substring(tagLength, text.Length - 2 * tagLength);
                foreach (var (start, end) in ConcatenatedExecutes(body))
                    report.At(token.Token.Start + tagLength + start, token.Token.Start + tagLength + end,
                        "This EXECUTE builds its command with ||; use format() with %I and %L, or EXECUTE ... USING.");
            }
        }
    }

    private static bool IsPlpgsqlBody(PgScriptStatement s)
    {
        if (s.Stmt?.DoStmt is { } doStmt)
        {
            var language = PgTreeFacts.Option(doStmt.Args, "language");
            return language == null || string.Equals(language.Arg?.String?.Sval, "plpgsql", StringComparison.OrdinalIgnoreCase);
        }
        if (s.Stmt?.CreateFunctionStmt is { } function)
            return string.Equals(PgTreeFacts.Option(function.Options, "language")?.Arg?.String?.Sval, "plpgsql", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    // The EXECUTE words of a PL/pgSQL body whose command expression contains || and something that is not a plain literal.
    private static IEnumerable<(int Start, int End)> ConcatenatedExecutes(string body)
    {
        var tokens = PgLexicalScanner.Scan(body)
            .Where(t => t.Kind is not (PgTokenKind.Whitespace or PgTokenKind.LineComment or PgTokenKind.BlockComment))
            .ToList();

        string Text(PgLexToken t) => body.Substring(t.Start, t.End - t.Start);

        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != PgTokenKind.Identifier || !string.Equals(Text(tokens[i]), "EXECUTE", StringComparison.OrdinalIgnoreCase)) continue;
            if (i > 0 && !StartsStatement(tokens[i - 1], Text(tokens[i - 1]))) continue;

            var depth = 0;
            var concatenates = false;
            var hasNonLiteral = false;
            for (var j = i + 1; j < tokens.Count; j++)
            {
                var t = tokens[j];
                var text = Text(t);
                if (t.Kind == PgTokenKind.Semicolon) break;
                if (t.Kind == PgTokenKind.Punctuation && text == "(") depth++;
                else if (t.Kind == PgTokenKind.Punctuation && text == ")") depth--;
                else if (depth == 0 && t.Kind == PgTokenKind.Identifier && (text.Equals("USING", StringComparison.OrdinalIgnoreCase) || text.Equals("INTO", StringComparison.OrdinalIgnoreCase))) break;
                else if (t.Kind == PgTokenKind.Operator && text == "||") concatenates = true;
                else if (t.Kind is PgTokenKind.Identifier or PgTokenKind.QuotedIdentifier or PgTokenKind.Parameter) hasNonLiteral = true;
            }

            if (concatenates && hasNonLiteral) yield return (tokens[i].Start, tokens[i].End);
        }
    }

    // EXECUTE begins a statement after a semicolon, a block keyword or a label.
    private static bool StartsStatement(PgLexToken previous, string text) =>
        previous.Kind == PgTokenKind.Semicolon
        || (previous.Kind == PgTokenKind.Operator && text == ">>")
        || (previous.Kind == PgTokenKind.Identifier && text.ToUpperInvariant() is "BEGIN" or "THEN" or "ELSE" or "LOOP");
}

/// <summary>PGSE002 - GRANT ALL, or a grant to everyone.</summary>
public sealed class Pgse002BroadGrant : PgRuleBase
{
    public Pgse002BroadGrant() : base("PGSE002", "Security", "Broad GRANT", DiagnosticSeverity.Warning,
        "GRANT ALL gives every privilege the object type has, including ones added in later releases, and a grant to PUBLIC gives it to every role that exists or will exist. "
        + "Grant the specific privileges to specific roles (the principle of least privilege).",
        "sql-grant") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var grant in s.All<GrantStmt>())
            {
                if (!grant.IsGrant) continue;

                var all = grant.Privileges.Count == 0;
                var toPublic = grant.Grantees.Any(g => g.RoleSpec is { Roletype: RoleSpecType.RolespecPublic });
                if (all && toPublic) report.Statement(s, "GRANT ALL to PUBLIC gives every privilege to every role.");
                else if (all) report.Statement(s, "GRANT ALL gives every privilege the object has; grant only what is needed.");
                else if (toPublic) report.Statement(s, "This grant to PUBLIC applies to every role, present and future.");
            }
        }
    }
}

/// <summary>PGSE003 - a password written into the script.</summary>
public sealed class Pgse003PasswordLiteral : PgRuleBase
{
    public Pgse003PasswordLiteral() : base("PGSE003", "Security", "Password literal in a role statement", DiagnosticSeverity.Error,
        "A password written in CREATE ROLE or ALTER ROLE is stored in the script, in the server log when log_statement is on, and in pg_stat_activity while it runs. "
        + "Set it from psql with \\password, which sends it already hashed, or supply it from a secret store.",
        "sql-createrole") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            var options = s.Stmt?.CreateRoleStmt?.Options ?? s.Stmt?.AlterRoleStmt?.Options;
            if (options == null) continue;

            foreach (var option in options)
            {
                var element = option.DefElem;
                if (element == null || !string.Equals(element.Defname, "password", StringComparison.OrdinalIgnoreCase) || element.Arg?.String == null) continue;
                report.Node(s, element, "A password is written in the script; set it with \\password or from a secret store.");
            }
        }
    }
}

/// <summary>PGSE004 - SECURITY DEFINER functions need a fixed search_path.</summary>
public sealed class Pgse004DefinerWithoutSearchPath : PgRuleBase
{
    public Pgse004DefinerWithoutSearchPath() : base("PGSE004", "Security", "SECURITY DEFINER without a fixed search_path", DiagnosticSeverity.Warning,
        "A SECURITY DEFINER function runs with its owner's privileges. If the function does not set its own search_path, the caller's decides which objects its unqualified "
        + "names resolve to, and a caller who can create objects in an earlier schema can make the function run their code with the owner's rights.",
        "sql-createfunction") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            var function = s.Stmt?.CreateFunctionStmt;
            if (function == null) continue;

            var security = PgTreeFacts.Option(function.Options, "security");
            if (security?.Arg?.Boolean is not { Boolval: true }) continue;

            var fixedPath = function.Options.Any(o => o.DefElem is { Defname: "set" } e
                && string.Equals(e.Arg?.VariableSetStmt?.Name, "search_path", StringComparison.OrdinalIgnoreCase));
            if (!fixedPath)
                report.Node(s, security, "This SECURITY DEFINER function does not set search_path; add SET search_path = ... to the definition.");
        }
    }
}

/// <summary>PGSE005 - the server reading or writing files, or running programs.</summary>
public sealed class Pgse005ServerFileAccess : PgRuleBase
{
    private static readonly HashSet<string> FileFunctions = new(StringComparer.Ordinal)
    {
        "pg_read_file", "pg_read_binary_file", "pg_ls_dir", "pg_stat_file", "pg_ls_logdir", "pg_ls_waldir", "pg_ls_tmpdir", "lo_import", "lo_export",
    };

    public Pgse005ServerFileAccess() : base("PGSE005", "Security", "Server file or program access", DiagnosticSeverity.Warning,
        "COPY with a file name or PROGRAM, and functions such as pg_read_file, pg_ls_dir, lo_import and lo_export, act on the server's file system with the server's "
        + "account, not the client's. They need elevated privileges, and a value that reaches them from outside is a path to files and commands on the server.",
        "sql-copy") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var copy in s.All<CopyStmt>())
            {
                if (copy.IsProgram) report.Statement(s, "COPY ... PROGRAM runs a command on the database server.");
                else if (!string.IsNullOrEmpty(copy.Filename)) report.Statement(s, "COPY reads or writes a file on the database server, not on this computer.");
            }

            foreach (var call in s.All<FuncCall>())
            {
                var name = PgTreeFacts.FunctionName(call);
                if (FileFunctions.Contains(name))
                    report.Node(s, call, $"{name}() accesses the database server's file system.");
            }
        }
    }
}
