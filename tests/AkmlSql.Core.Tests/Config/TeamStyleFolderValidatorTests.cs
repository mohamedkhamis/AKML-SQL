using System.IO;
using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests.Config;

/// <summary>
/// Spec 040 (T178, STY-10, data-model 1.2) — the team style folder must be a full path. The value
/// kept is the canonical <see cref="Path.GetFullPath(string)"/> form (Constitution security: path
/// checks use the canonical form, never a "..": substring test). An unreachable folder is allowed;
/// reachability is reported by the style list, not refused here.
/// </summary>
public class TeamStyleFolderValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_means_the_team_folder_is_off(string? input)
    {
        var (ok, fullPath, error) = TeamStyleFolderValidator.Normalize(input);

        Assert.True(ok);
        Assert.Null(fullPath);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("styles")]
    [InlineData(@"team\styles")]
    [InlineData(@"..\styles")]
    [InlineData(@"C:styles")]        // drive-relative: resolves against the drive's current folder
    [InlineData(@"\styles")]         // root-relative: resolves against the current drive
    public void A_relative_path_is_refused(string input)
    {
        var (ok, fullPath, error) = TeamStyleFolderValidator.Normalize(input);

        Assert.False(ok);
        Assert.Null(fullPath);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData(@"C:\Team\Styles")]
    [InlineData(@"D:\")]
    [InlineData(@"\\server\share\styles")]
    public void A_rooted_local_or_unc_path_is_accepted_in_its_full_path_form(string input)
    {
        var (ok, fullPath, error) = TeamStyleFolderValidator.Normalize(input);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(Path.GetFullPath(input), fullPath);
    }

    [Fact]
    public void Surrounding_spaces_and_quotes_are_ignored()
    {
        var (ok, fullPath, _) = TeamStyleFolderValidator.Normalize("  \"C:\\Team\\Styles\"  ");

        Assert.True(ok);
        Assert.Equal(Path.GetFullPath(@"C:\Team\Styles"), fullPath);
    }

    [Theory]
    [InlineData(@"C:\Team\..\Shared\Styles", @"C:\Shared\Styles")]
    [InlineData(@"C:\Team\.\Styles", @"C:\Team\Styles")]
    [InlineData(@"\\server\share\team\..\styles", @"\\server\share\styles")]
    public void Dot_segments_are_resolved_by_the_canonical_form(string input, string expected)
    {
        var (ok, fullPath, _) = TeamStyleFolderValidator.Normalize(input);

        Assert.True(ok);
        Assert.Equal(expected, fullPath);
        Assert.DoesNotContain("..", fullPath!);
    }

    [Fact]
    public void Illegal_path_characters_are_refused()
    {
        var (ok, fullPath, error) = TeamStyleFolderValidator.Normalize("C:\\Team\\Sty\0les");

        Assert.False(ok);
        Assert.Null(fullPath);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
