using AkmlSql.Core;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// Review WEB-10: a late Format reply never replaces newer work. The gate drops a reply that a later Format overtook, or
/// that belongs to another document or dialect; typing in the same document is caught by the editor's compare-and-replace
/// (tests/AkmlSql.Web.Tests/js/akml-editor.replace.test.mjs).
/// </summary>
public sealed class FormatRequestGateTests
{
    [Fact]
    public void The_latest_Format_for_the_same_document_and_dialect_applies()
    {
        var gate = new FormatRequestGate();
        var ticket = gate.Begin("select 1", "doc-1", DatabaseKind.PostgreSql);

        Assert.True(gate.Applies(ticket, "doc-1", DatabaseKind.PostgreSql));
        Assert.Equal("select 1", ticket.Text);
    }

    [Fact]
    public void Of_two_overlapping_Formats_only_the_second_reply_applies_whatever_order_they_arrive_in()
    {
        var gate = new FormatRequestGate();
        var first = gate.Begin("select 1", "doc-1", DatabaseKind.PostgreSql);
        var second = gate.Begin("select 1, 2", "doc-1", DatabaseKind.PostgreSql);

        Assert.False(gate.IsLatest(first));
        Assert.False(gate.Applies(first, "doc-1", DatabaseKind.PostgreSql));
        Assert.True(gate.Applies(second, "doc-1", DatabaseKind.PostgreSql));
    }

    [Fact]
    public void A_reply_for_a_document_that_was_replaced_by_New_or_Open_does_not_apply()
    {
        var gate = new FormatRequestGate();
        var ticket = gate.Begin("select 1", "doc-1", DatabaseKind.SqlServer);

        Assert.False(gate.Applies(ticket, "doc-2", DatabaseKind.SqlServer));
    }

    [Fact]
    public void A_reply_formatted_for_the_other_dialect_does_not_apply()
    {
        var gate = new FormatRequestGate();
        var ticket = gate.Begin("select 1", "doc-1", DatabaseKind.PostgreSql);

        Assert.False(gate.Applies(ticket, "doc-1", DatabaseKind.SqlServer));
    }
}
