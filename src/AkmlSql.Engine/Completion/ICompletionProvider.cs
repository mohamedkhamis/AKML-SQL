using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Completion;

public interface ICompletionProvider
{
    string Name { get; }
    bool CanHandle(CursorContext context, DatabaseCache? cache);
    IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache);
}
