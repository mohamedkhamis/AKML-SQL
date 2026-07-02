using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Editor.Completion;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Pins the auto-close pairing rules behind SQL Prompt's "Automatically insert the
    /// corresponding closing character" (Inserted code › Special characters). Each rule is
    /// gated by its per-character toggle AND the master <c>AutoCloseCharacters</c> switch,
    /// with guards so auto-close never fires in the middle of a word (e.g. the apostrophe
    /// in <c>-- don't</c>) or directly before an identifier.
    /// </summary>
    public class AutoClosePairsTests
    {
        private static SpecialCharacterSettings AllOn() => new SpecialCharacterSettings
        {
            AutoCloseCharacters = true,
            CloseSingleQuote = true,
            CloseDoubleQuote = true,
            CloseCommentMark = true,
            CloseParenthesis = true,
            CloseSquareBracket = true,
        };

        [Fact]
        public void Parenthesis_Closes_WhenEnabled()
        {
            Assert.Equal(")", AutoClosePairs.TryGetCloser('(', ' ', ' ', AllOn()));
            Assert.Equal(")", AutoClosePairs.TryGetCloser('(', 'T', '\0', AllOn())); // fn call at EOL
        }

        [Fact]
        public void SquareBracket_Closes_WhenEnabled()
        {
            Assert.Equal("]", AutoClosePairs.TryGetCloser('[', ' ', ' ', AllOn()));
        }

        [Fact]
        public void Quotes_Close_WhenEnabled()
        {
            Assert.Equal("'", AutoClosePairs.TryGetCloser('\'', ' ', ' ', AllOn()));
            Assert.Equal("\"", AutoClosePairs.TryGetCloser('"', '(', '\0', AllOn()));
        }

        [Fact]
        public void CommentMark_ClosesOnlyAfterSlash()
        {
            Assert.Equal("*/", AutoClosePairs.TryGetCloser('*', '/', ' ', AllOn()));
            Assert.Null(AutoClosePairs.TryGetCloser('*', ' ', ' ', AllOn())); // bare * (multiply)
        }

        [Fact]
        public void Quote_DoesNotClose_InsideAWord()
        {
            // The apostrophe in "-- don't": prev is a letter → no auto-close.
            Assert.Null(AutoClosePairs.TryGetCloser('\'', 'n', 't', AllOn()));
        }

        [Fact]
        public void Openers_DoNotClose_DirectlyBeforeAnIdentifier()
        {
            // Typing ( before an existing word should not wrap a stray ) into it.
            Assert.Null(AutoClosePairs.TryGetCloser('(', ' ', 'S', AllOn()));
            Assert.Null(AutoClosePairs.TryGetCloser('[', ' ', 'd', AllOn()));
        }

        [Fact]
        public void PerCharacterToggle_Off_Disables_JustThatCharacter()
        {
            var s = AllOn();
            s.CloseParenthesis = false;
            Assert.Null(AutoClosePairs.TryGetCloser('(', ' ', ' ', s));
            Assert.Equal("]", AutoClosePairs.TryGetCloser('[', ' ', ' ', s)); // others unaffected
        }

        [Fact]
        public void MasterSwitch_Off_DisablesEverything()
        {
            var s = AllOn();
            s.AutoCloseCharacters = false;
            Assert.Null(AutoClosePairs.TryGetCloser('(', ' ', ' ', s));
            Assert.Null(AutoClosePairs.TryGetCloser('\'', ' ', ' ', s));
            Assert.Null(AutoClosePairs.TryGetCloser('*', '/', ' ', s));
        }

        [Fact]
        public void NonPairCharacters_ReturnNull()
        {
            Assert.Null(AutoClosePairs.TryGetCloser('a', ' ', ' ', AllOn()));
            Assert.Null(AutoClosePairs.TryGetCloser(')', ' ', ' ', AllOn()));
            Assert.Null(AutoClosePairs.TryGetCloser(';', ' ', ' ', AllOn()));
        }
    }
}
