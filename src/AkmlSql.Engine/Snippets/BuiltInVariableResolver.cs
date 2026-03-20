namespace AkmlSql.Engine.Snippets;

public class BuiltInVariableResolver
{
    public string Resolve(string text, BuiltInVariableContext context)
    {
        var result = text;
        result = result.Replace("$DATE$", DateTime.Now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$DATETIME$", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$TIME$", DateTime.Now.ToString("HH:mm:ss"), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$USER$", Environment.UserName, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$MACHINE$", Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$DATABASE$", context.DatabaseName, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$SERVER$", context.ServerName, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$SCHEMA$", string.IsNullOrEmpty(context.SchemaName) ? "dbo" : context.SchemaName, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$GUID$", Guid.NewGuid().ToString(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$YEAR$", DateTime.Now.Year.ToString(), StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$FILENAME$", context.FileName, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$CLIPBOARD$", context.ClipboardText, StringComparison.OrdinalIgnoreCase);
        result = result.Replace("$SELECTEDTEXT$", context.SelectedText, StringComparison.OrdinalIgnoreCase);
        // $CURSOR$ is handled separately by PlaceholderParser (removed and tracked as position)
        return result;
    }
}
