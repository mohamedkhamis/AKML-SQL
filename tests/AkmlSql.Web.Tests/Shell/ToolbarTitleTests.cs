using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 041 (T196, FR-081) — a toolbar button's tooltip is its command name and keys, read from the one key table,
/// so a key and its tooltip never disagree.
/// </summary>
public sealed class ToolbarTitleTests
{
    [Theory]
    [InlineData("Execute", "editor:execute", "Execute (F5)")]
    [InlineData("Parse", "editor:parse", "Parse (Ctrl+F5)")]
    [InlineData("Format", "editor:format", "Format (Ctrl+K, Ctrl+F)")]
    [InlineData("Analyse", "editor:analyse", "Analyse (Ctrl+K, Ctrl+L)")]
    [InlineData("New query", "editor:new", "New query (Ctrl+Alt+N)")]
    [InlineData("Open", "editor:open", "Open (Ctrl+Alt+O)")]
    [InlineData("Save", "editor:save", "Save (Ctrl+S)")]
    [InlineData("Save as", "editor:saveas", "Save as (Ctrl+Shift+S)")]
    public void A_command_with_keys_shows_them(string name, string commandId, string expected) =>
        Assert.Equal(expected, WorkspaceKeyMap.TitleFor(name, commandId));

    [Fact]
    public void A_command_without_keys_shows_only_its_name() =>
        Assert.Equal("Refactor", WorkspaceKeyMap.TitleFor("Refactor", "editor:refactor-menu"));
}
