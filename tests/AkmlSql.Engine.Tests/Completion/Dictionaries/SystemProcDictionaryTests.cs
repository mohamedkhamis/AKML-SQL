using Xunit;
using AkmlSql.Engine.Completion.Dictionaries;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Engine.Tests.Completion.Dictionaries;

public class SystemProcDictionaryTests
{
    // ── Count ─────────────────────────────────────────────────────────────

    [Fact]
    public void Count_IsPositive()
    {
        Assert.True(SystemProcDictionary.Count > 0);
    }

    [Fact]
    public void Count_MatchesGetCompletionItemsCount()
    {
        var items = SystemProcDictionary.GetCompletionItems().ToList();
        Assert.Equal(SystemProcDictionary.Count, items.Count);
    }

    // ── GetCompletionItems ────────────────────────────────────────────────

    [Fact]
    public void GetCompletionItems_ReturnsNonEmpty()
    {
        var items = SystemProcDictionary.GetCompletionItems().ToList();
        Assert.NotEmpty(items);
    }

    [Fact]
    public void GetCompletionItems_AllHaveDisplayText()
    {
        var items = SystemProcDictionary.GetCompletionItems().ToList();
        Assert.All(items, item => Assert.NotEmpty(item.DisplayText));
    }

    [Fact]
    public void GetCompletionItems_DisplayTextEqualsInsertText()
    {
        var items = SystemProcDictionary.GetCompletionItems().ToList();
        Assert.All(items, item => Assert.Equal(item.DisplayText, item.InsertText));
    }

    [Fact]
    public void GetCompletionItems_AllHaveProcedureObjectType()
    {
        var items = SystemProcDictionary.GetCompletionItems().ToList();
        Assert.All(items, item => Assert.Equal((int)CompletionObjectType.Procedure, item.ObjectType));
    }

    [Fact]
    public void GetCompletionItems_AllHaveSourceObject_MasterDbo()
    {
        var items = SystemProcDictionary.GetCompletionItems().ToList();
        Assert.All(items, item => Assert.Equal("master.dbo", item.SourceObject));
    }

    [Fact]
    public void GetCompletionItems_AllHaveSortPriority400()
    {
        var items = SystemProcDictionary.GetCompletionItems().ToList();
        Assert.All(items, item => Assert.Equal(400, item.SortPriority));
    }

    [Fact]
    public void GetCompletionItems_AllHaveSecondaryText()
    {
        var items = SystemProcDictionary.GetCompletionItems().ToList();
        Assert.All(items, item => Assert.NotEmpty(item.SecondaryText));
    }

    [Fact]
    public void GetCompletionItems_ContainsSpHelp()
    {
        var items = SystemProcDictionary.GetCompletionItems().ToList();
        Assert.Contains(items, i => i.DisplayText == "sp_help");
    }

    [Fact]
    public void GetCompletionItems_ContainsXpCmdshell()
    {
        var items = SystemProcDictionary.GetCompletionItems().ToList();
        Assert.Contains(items, i => i.DisplayText == "xp_cmdshell");
    }

    [Fact]
    public void GetCompletionItems_ContainsSpExecuteSql()
    {
        var items = SystemProcDictionary.GetCompletionItems().ToList();
        Assert.Contains(items, i => i.DisplayText == "sp_executesql");
    }

    [Fact]
    public void GetCompletionItems_ContainsSpSendDbmail()
    {
        var items = SystemProcDictionary.GetCompletionItems().ToList();
        Assert.Contains(items, i => i.DisplayText == "sp_send_dbmail");
    }

    [Fact]
    public void GetCompletionItems_AllNamesStartWithSpOrXp()
    {
        var items = SystemProcDictionary.GetCompletionItems().ToList();
        Assert.All(items, item =>
            Assert.True(
                item.DisplayText.StartsWith("sp_", StringComparison.OrdinalIgnoreCase) ||
                item.DisplayText.StartsWith("xp_", StringComparison.OrdinalIgnoreCase),
                $"'{item.DisplayText}' does not start with sp_ or xp_"));
    }
}
