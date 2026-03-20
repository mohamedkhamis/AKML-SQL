using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// T074: Provides variable completions when typing text starting with "@".
/// Returns variables from context.AvailableVariables with their declared types.
/// </summary>
public class VariableProvider : ICompletionProvider
{
    public string Name => "Variable";

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        // Activate when the partial text starts with "@"
        return context.PartialText.StartsWith("@");
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        if (context.AvailableVariables.Count == 0)
            yield break;

        foreach (var (variableName, typeName) in context.AvailableVariables)
        {
            yield return new CompletionItem
            {
                DisplayText = variableName,
                InsertText = variableName,
                ObjectType = (int)CompletionObjectType.Variable,
                SecondaryText = typeName,
                SourceObject = string.Empty,
                SortPriority = 10
            };
        }
    }
}
