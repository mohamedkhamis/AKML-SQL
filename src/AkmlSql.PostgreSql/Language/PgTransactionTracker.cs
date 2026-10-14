using AkmlSql.Core;

namespace AkmlSql.PostgreSql.Language;

/// <summary>
/// Spec 042 (FR-016, research R9, data-model §9) - the transaction state of a PostgreSQL session, tracked per statement
/// because Npgsql exposes no transaction status. A known approximation: it follows what the script did and what the
/// server answered, which is what the status bar needs. The one self-correcting signal is SQLSTATE 25P02
/// (in_failed_sql_transaction), which can only mean the transaction is aborted.
///
/// <para>
/// A pure function of (state, statement, outcome) so the engine, the tests and any future client agree.
/// </para>
/// </summary>
public static class PgTransactionTracker
{
    public const string InFailedSqlTransaction = "25P02";

    public static TransactionState Next(TransactionState state, string statementText, bool succeeded, string? sqlState)
    {
        if (!succeeded)
        {
            if (sqlState == InFailedSqlTransaction) return TransactionState.Aborted;
            // Any failure inside a transaction aborts it (a failed COMMIT included); outside one, autocommit rolls
            // the statement back and nothing is left open.
            return state == TransactionState.Open ? TransactionState.Aborted : state;
        }

        var words = LeadingWords(statementText, 8);
        if (words.Count == 0) return state;

        switch (words[0])
        {
            case "BEGIN":
                return state == TransactionState.None ? TransactionState.Open : state;

            case "START" when words.Count > 1 && words[1] == "TRANSACTION":
                return state == TransactionState.None ? TransactionState.Open : state;

            case "COMMIT" or "END" or "ROLLBACK" or "ABORT":
                if (words.Count > 1 && words[1] == "PREPARED") return state; // two-phase commit of another session's transaction
                if (words[0] == "ROLLBACK" && RollsBackToSavepoint(words))
                    return state == TransactionState.Aborted ? TransactionState.Open : state;
                return StartsNextTransaction(words) ? TransactionState.Open : TransactionState.None;

            case "PREPARE" when words.Count > 1 && words[1] == "TRANSACTION":
                return TransactionState.None; // the session no longer holds the transaction

            default:
                return state; // SAVEPOINT, RELEASE and everything else leave it as it is
        }
    }

    private static bool RollsBackToSavepoint(IReadOnlyList<string> words)
    {
        // ROLLBACK [WORK | TRANSACTION] TO [SAVEPOINT] name
        for (var i = 1; i < Math.Min(words.Count, 4); i++)
            if (words[i] == "TO") return true;
        return false;
    }

    private static bool StartsNextTransaction(IReadOnlyList<string> words)
    {
        // [COMMIT | ROLLBACK | END] [WORK | TRANSACTION] AND [NO] CHAIN
        for (var i = 1; i < words.Count - 1; i++)
        {
            if (words[i] != "AND") continue;
            return words[i + 1] == "CHAIN";
        }
        return false;
    }

    /// <summary>The first <paramref name="max"/> words of a statement, upper-cased, skipping comments.</summary>
    private static List<string> LeadingWords(string text, int max)
    {
        var words = new List<string>();
        foreach (var token in PgLexicalScanner.Scan(text))
        {
            if (token.Kind is PgTokenKind.Whitespace or PgTokenKind.LineComment or PgTokenKind.BlockComment) continue;
            if (token.Kind != PgTokenKind.Identifier) break;
            words.Add(text.Substring(token.Start, token.End - token.Start).ToUpperInvariant());
            if (words.Count == max) break;
        }
        return words;
    }
}
