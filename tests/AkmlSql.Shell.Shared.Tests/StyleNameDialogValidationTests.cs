#nullable enable
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T088, STY-07) — the style name prompt explains what is wrong with a name and keeps
    /// OK disabled until it is valid: not empty, at most 80 characters, no characters a file name
    /// cannot hold, and not the name of another style (case-insensitive, trimmed). Rename accepts
    /// the style's own name, including a change of case only.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public sealed class StyleNameDialogValidationTests
    {
        private static readonly string[] Existing = { "Default", "Khamis Style", "Mine" };

        private static StyleNameDialog Dialog(string? currentName = null) =>
            StyleNameDialog.ForTests(string.Empty, Existing, currentName);

        [StaTheory]
        [InlineData("", "Enter a style name.")]
        [InlineData("   ", "Enter a style name.")]
        [InlineData("a/b", "cannot be used in a file name")]
        [InlineData("a:b", "cannot be used in a file name")]
        [InlineData("..", "cannot be used in a file name")]
        [InlineData("default", "already exists")]
        [InlineData("  Khamis Style ", "already exists")]
        public void An_invalid_name_shows_why_and_disables_OK(string name, string expected)
        {
            var dialog = Dialog();
            try
            {
                dialog.NameText = name;

                Assert.False(dialog.CanAccept);
                Assert.Contains(expected, dialog.ValidationMessage);
            }
            finally { dialog.Close(); }
        }

        [StaFact]
        public void A_name_longer_than_80_characters_is_refused()
        {
            var dialog = Dialog();
            try
            {
                dialog.NameText = new string('x', 81);
                Assert.False(dialog.CanAccept);
                Assert.Contains("80", dialog.ValidationMessage);

                dialog.NameText = new string('x', 80);
                Assert.True(dialog.CanAccept);
            }
            finally { dialog.Close(); }
        }

        [StaTheory]
        [InlineData("Mine")]
        [InlineData("MINE")]
        [InlineData(" mine ")]
        public void Rename_accepts_the_styles_own_name_in_any_case(string name)
        {
            var dialog = Dialog(currentName: "Mine");
            try
            {
                dialog.NameText = name;
                Assert.True(dialog.CanAccept);
                Assert.Null(dialog.ValidationMessage);
            }
            finally { dialog.Close(); }
        }

        [StaFact]
        public void Rename_still_refuses_another_styles_name()
        {
            var dialog = Dialog(currentName: "Mine");
            try
            {
                dialog.NameText = "Default";
                Assert.False(dialog.CanAccept);
            }
            finally { dialog.Close(); }
        }

        [StaFact]
        public void A_valid_name_enables_OK()
        {
            var dialog = Dialog();
            try
            {
                dialog.NameText = "Northwind reports";
                Assert.True(dialog.CanAccept);
                Assert.Null(dialog.ValidationMessage);
            }
            finally { dialog.Close(); }
        }
    }
}
