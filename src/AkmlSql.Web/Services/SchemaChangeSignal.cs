using System.Linq;
using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Text;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 042 (FR-043): whether a PostgreSQL run changed the schema in a way the explorer should show now. The engine says so
/// (<see cref="ExecuteQueryResult.SchemaChanged"/>, key 14): it has started reloading its catalog, and the browser fetches the
/// result once that settles. An engine from before key 14 reloaded after any schema-changing command tag outside a transaction;
/// the same reading keeps the explorer right with such an engine. A change inside a transaction that is still open is reported by
/// the run that ends the transaction, not before.
/// </summary>
public static class SchemaChangeSignal
{
    public static bool RefetchAfter(ExecuteQueryResult result)
    {
        if (result.SchemaChanged) return true;
        return result.TransactionState == (int)TransactionState.None
            && result.MessageDetails?.Any(m => m.Kind == ExecuteMessageKind.RowCount && PgCommandTags.ChangesSchema(m.Text)) == true;
    }
}
