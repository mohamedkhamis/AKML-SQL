using Xunit;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Profiles;

public class ProfileDifferTests
{
    private readonly ProfileDiffer _differ = new();

    // ── ProfileDifference data class ──────────────────────────────────────

    [Fact]
    public void ProfileDifference_Defaults()
    {
        var d = new ProfileDifference();
        Assert.Equal(string.Empty, d.Category);
        Assert.Equal(string.Empty, d.OptionName);
        Assert.Equal(string.Empty, d.OldValue);
        Assert.Equal(string.Empty, d.NewValue);
    }

    [Fact]
    public void ProfileDifference_Mutations()
    {
        var d = new ProfileDifference
        {
            Category = "whitespace",
            OptionName = "TabSize",
            OldValue = "4",
            NewValue = "2"
        };
        Assert.Equal("whitespace", d.Category);
        Assert.Equal("TabSize", d.OptionName);
        Assert.Equal("4", d.OldValue);
        Assert.Equal("2", d.NewValue);
    }

    // ── Compare: identical profiles ───────────────────────────────────────

    [Fact]
    public void Compare_IdenticalProfiles_ReturnsEmpty()
    {
        var left = new FormattingProfile();
        var right = new FormattingProfile();
        var diffs = _differ.Compare(left, right);
        Assert.Empty(diffs);
    }

    // ── Compare: single difference ─────────────────────────────────────────

    [Fact]
    public void Compare_TabSizeDiffers_ReturnsDiff()
    {
        var left = new FormattingProfile();
        var right = new FormattingProfile
        {
            Whitespace =
            {
                TabSize = 2
            }
        };

        var diffs = _differ.Compare(left, right);
        Assert.Contains(diffs, d => d is { Category: "whitespace", OptionName: "TabSize" });
    }

    [Fact]
    public void Compare_CasingDiffers_ReturnsDiff()
    {
        var left = new FormattingProfile();
        var right = new FormattingProfile
        {
            Casing =
            {
                ReservedKeywords = "lowercase"
            }
        };

        var diffs = _differ.Compare(left, right);
        Assert.Contains(diffs, d => d is { Category: "casing", OptionName: "ReservedKeywords" });
    }

    // ── Compare: multiple differences ─────────────────────────────────────

    [Fact]
    public void Compare_MultipleDifferences_ReturnsAll()
    {
        var left = new FormattingProfile();
        var right = new FormattingProfile
        {
            Whitespace =
            {
                TabSize = 2,
                MaxLineWidth = 80
            },
            Casing =
            {
                ReservedKeywords = "lowercase"
            }
        };

        var diffs = _differ.Compare(left, right);
        Assert.True(diffs.Count >= 3);
    }

    // ── Compare: null arguments ────────────────────────────────────────────

    [Fact]
    public void Compare_NullLeft_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _differ.Compare(null!, new FormattingProfile()));
    }

    [Fact]
    public void Compare_NullRight_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => _differ.Compare(new FormattingProfile(), null!));
    }

    // ── Compare: diff values are correct ──────────────────────────────────

    [Fact]
    public void Compare_DiffValues_AreCorrect()
    {
        var left = new FormattingProfile();
        var right = new FormattingProfile
        {
            Whitespace =
            {
                TabSize = 2
            }
        };

        var diffs = _differ.Compare(left, right);
        var tabSizeDiff = diffs.First(d => d.OptionName == "TabSize");
        Assert.Equal("4", tabSizeDiff.OldValue); // default is 4
        Assert.Equal("2", tabSizeDiff.NewValue);
    }

    // ── Compare: all category keys ─────────────────────────────────────────

    [Fact]
    public void Compare_AllCategories_Covered()
    {
        // Make a difference in each category to verify they're all compared
        var left = new FormattingProfile();
        var right = new FormattingProfile
        {
            Whitespace =
            {
                TabSize = 2
            },
            Casing =
            {
                ReservedKeywords = "lowercase"
            },
            List =
            {
                CommaPosition = "leading"
            },
            Parenthesis =
            {
                CollapseThreshold = 30
            },
            Dml =
            {
                CollapseThreshold = 70
            },
            Join =
            {
                IndentJoin = true
            },
            Ddl =
            {
                AlignDataTypes = false
            },
            ControlFlow =
            {
                TryCatchOnNewLine = false
            },
            Case =
            {
                ThenOnNewLine = true
            },
            Cte =
            {
                CommaBeforeCte = true
            },
            Expression =
            {
                BetweenOnOneLine = false
            },
            FormatActions =
            {
                InsertSemicolons = true
            }
        };

        var diffs = _differ.Compare(left, right);
        var categories = diffs.Select(d => d.Category).Distinct().ToHashSet();

        Assert.Contains("whitespace", categories);
        Assert.Contains("casing", categories);
        Assert.Contains("list", categories);
        Assert.Contains("parenthesis", categories);
        Assert.Contains("dml", categories);
        Assert.Contains("join", categories);
        Assert.Contains("ddl", categories);
        Assert.Contains("controlFlow", categories);
        Assert.Contains("case", categories);
        Assert.Contains("cte", categories);
        Assert.Contains("expression", categories);
        Assert.Contains("formatActions", categories);
    }
}
