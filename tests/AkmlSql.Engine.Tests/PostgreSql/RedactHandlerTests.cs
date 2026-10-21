using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.PostgreSql;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T111, FR-081, contracts/ipc.md 223 to 224): the engine removes the literals of PostgreSQL text for the
/// web edition's cloud AI calls. A script it cannot read to the end is refused, never passed through.
/// </summary>
public class RedactHandlerTests
{
    private static Task<RedactResponse> Run(string? text, string dialect = "postgresql") =>
        new RedactHandler().HandleAsync(new RedactRequest { Dialect = dialect, Text = text! }, null!, CancellationToken.None);

    [Fact]
    public void It_answers_message_223_with_224()
    {
        var handler = new RedactHandler();

        Assert.Equal(MessageTypes.RedactRequest, handler.RequestMessageType);
        Assert.Equal(MessageTypes.RedactResponse, handler.ResponseMessageType);
    }

    [Fact]
    public async Task String_numeric_and_dollar_quoted_literals_become_placeholders_and_structure_stays()
    {
        var response = await Run("SELECT name, $$note$$ FROM people WHERE country = 'UK' AND age > 30 ORDER BY name LIMIT 5;");

        Assert.True(response.Ok);
        Assert.Null(response.Error);
        Assert.Equal("SELECT name, $$__STR_0__$$ FROM people WHERE country = '__STR_1__' AND age > __NUM_0__ ORDER BY name LIMIT 5;", response.Text);
        Assert.Equal("'UK'", response.Placeholders["'__STR_1__'"]);
        Assert.Equal("30", response.Placeholders["__NUM_0__"]);
        Assert.Equal("$$note$$", response.Placeholders["$$__STR_0__$$"]);
    }

    [Fact]
    public async Task Identifiers_quoted_names_comments_and_casts_are_kept()
    {
        var response = await Run("-- audit\nSELECT \"Event Name\"::text FROM \"Audit Log\" WHERE log_id = $1;");

        Assert.True(response.Ok);
        Assert.Equal("-- audit\nSELECT \"Event Name\"::text FROM \"Audit Log\" WHERE log_id = $1;", response.Text);
        Assert.Empty(response.Placeholders);
    }

    [Fact]
    public async Task Text_that_cannot_be_read_to_the_end_is_refused_and_not_returned()
    {
        var response = await Run("SELECT 'hunter2 is the password");

        Assert.False(response.Ok);
        Assert.Equal(string.Empty, response.Text);
        Assert.Empty(response.Placeholders);
        Assert.False(string.IsNullOrWhiteSpace(response.Error));
        Assert.DoesNotContain("hunter2", response.Error);
    }

    [Theory]
    [InlineData("sqlserver")]
    [InlineData("mssql")]
    [InlineData("")]
    [InlineData("oracle")]
    public async Task Only_PostgreSQL_text_is_served_and_the_text_is_never_passed_through(string dialect)
    {
        var response = await Run("SELECT 'secret'", dialect);

        Assert.False(response.Ok);
        Assert.Equal(string.Empty, response.Text);
        Assert.DoesNotContain("secret", response.Error);
        Assert.Contains("PostgreSQL", response.Error);
    }

    [Theory]
    [InlineData("postgresql")]
    [InlineData("PostgreSQL")]
    [InlineData("pg")]
    [InlineData(" postgres ")]
    public async Task The_PostgreSQL_dialect_may_be_written_as_its_name_or_a_common_alias(string dialect)
    {
        var response = await Run("SELECT 'x'", dialect);

        Assert.True(response.Ok);
        Assert.Equal("SELECT '__STR_0__'", response.Text);
    }

    [Fact]
    public async Task Empty_and_missing_text_redact_to_nothing()
    {
        foreach (var text in new string?[] { "", null })
        {
            var response = await Run(text);

            Assert.True(response.Ok);
            Assert.Equal(string.Empty, response.Text);
        }
    }

    [Fact]
    public async Task Text_over_the_document_limit_is_refused()
    {
        var response = await Run(new string('x', 10 * 1024 * 1024 + 1));

        Assert.False(response.Ok);
        Assert.Equal(string.Empty, response.Text);
        Assert.Contains("10 MB", response.Error);
    }

    [Fact]
    public async Task A_null_request_is_an_error_of_the_caller_and_not_a_redaction()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => new RedactHandler().HandleAsync(null!, null!, CancellationToken.None));
    }
}
