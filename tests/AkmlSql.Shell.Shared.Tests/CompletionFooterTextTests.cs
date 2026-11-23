using AkmlSql.Shell.Shared.Editor.Completion;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests;

/// <summary>
/// Installed run 2026-11-23: the completion list's footer toggle read "owner: on", the old SQL Server word for a schema, and did
/// not say what was on. It shows or hides the schema name in front of each suggestion, and now says so.
/// </summary>
public sealed class CompletionFooterTextTests
{
    [Theory]
    [InlineData(true, "schema names: on")]
    [InlineData(false, "schema names: off")]
    public void The_schema_toggle_names_what_it_shows(bool shown, string expected)
    {
        Assert.Equal(expected, AkmlCompletionPopup.OwnerToggleText(shown));
    }
}
