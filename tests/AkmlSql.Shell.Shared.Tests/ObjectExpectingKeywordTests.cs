using AkmlSql.Shell.Shared.Editor.Completion;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Pins the shell-side list of keywords after which a space auto-triggers OBJECT completion.
    ///
    /// <para>This list is independent of the engine's <c>ClauseType</c> analysis: the engine
    /// correctly reports <c>ClauseType.Delete</c> and offers tables for <c>DELETE t</c>, but the
    /// shell decides whether to open a session in object mode at all. <c>DELETE</c> was missing
    /// here while every sibling DML/DDL keyword (UPDATE, TRUNCATE, DROP, ALTER, INTO) was present,
    /// so the FROM-less <c>DELETE &lt;table&gt;</c> form — valid T-SQL — offered nothing, while
    /// <c>DELETE FROM &lt;table&gt;</c> worked because FROM carried the trigger.</para>
    ///
    /// <para>The independence cuts both ways, so every row here is a claim about BOTH layers.
    /// A keyword belongs in the list only when the engine actually serves objects at that
    /// position: object mode strips keyword items whenever any object is present, and when no
    /// object is present it shows whatever fallback the engine returned. Adding a keyword the
    /// engine does not understand therefore replaces "no popup" with "wrong popup" — which is
    /// why <c>MERGE</c> sits in the negative theory despite being a real T-SQL object position.</para>
    /// </summary>
    public class ObjectExpectingKeywordTests
    {
        [Theory]
        [InlineData("DELETE")]   // the reported gap: `DELETE martyrs`
        [InlineData("INSERT")]   // same class of gap: INTO is optional in `INSERT t VALUES (…)`
        [InlineData("FROM")]
        [InlineData("JOIN")]
        [InlineData("INTO")]
        [InlineData("UPDATE")]
        [InlineData("TRUNCATE")]
        [InlineData("DROP")]
        [InlineData("ALTER")]
        [InlineData("TABLE")]
        [InlineData("VIEW")]
        [InlineData("EXEC")]
        [InlineData("EXECUTE")]
        public void KeywordsExpectingAnObjectName_triggerObjectCompletion(string keyword)
        {
            Assert.True(CompletionController.IsObjectExpectingKeyword(keyword),
                $"'{keyword}' should auto-trigger object completion after a space");
        }

        [Theory]
        [InlineData("delete")]
        [InlineData("Delete")]
        public void KeywordMatching_isCaseInsensitive(string keyword)
        {
            Assert.True(CompletionController.IsObjectExpectingKeyword(keyword));
        }

        [Theory]
        // Join qualifiers expect the JOIN keyword next, not a table name.
        [InlineData("INNER")]
        [InlineData("LEFT")]
        [InlineData("CROSS")]
        // MERGE *is* an object position in T-SQL (`MERGE target USING …`), but the engine has no
        // MERGE clause type — CursorContextAnalyzer treats the token only as a statement boundary,
        // so `MERGE ` yields the generic statement-start keyword list (ALTER, BACKUP, BEGIN, …)
        // and `MERGE Cus` yields a single stray `RESPECT NULLS`. Triggering object mode there
        // shows that list where a table name belongs, which is worse than showing nothing.
        // Flip this row to the positive theory when the engine learns MERGE.
        [InlineData("MERGE")]
        // Not object positions.
        [InlineData("SELECT")]
        [InlineData("WHERE")]
        [InlineData("")]
        public void NonObjectPositions_doNotTrigger(string keyword)
        {
            Assert.False(CompletionController.IsObjectExpectingKeyword(keyword),
                $"'{keyword}' must not force object-mode completion");
        }
    }
}
