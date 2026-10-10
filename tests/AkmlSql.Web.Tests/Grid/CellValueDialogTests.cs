using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace AkmlSql.Web.Tests.Grid;

/// <summary>
/// Spec 041 (T051, research R8, FR-002, FR-017) — View value: the whole value with its column, declared type
/// and size, a Copy button; for a value the engine itself cut, an honest "not available" and a shorter
/// projection to run instead.
/// </summary>
public sealed class CellValueDialogTests : IDisposable
{
    private readonly BunitContext _ctx = new();

    public CellValueDialogTests() => _ctx.JSInterop.Mode = JSRuntimeMode.Loose;

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<CellValueDialog> Render(string value, bool cut = false, bool? allowsNull = true, bool binary = false, Action? onClose = null) =>
        _ctx.Render<CellValueDialog>(p => p
            .Add(x => x.ColumnName, "notes")
            .Add(x => x.DeclaredType, "nvarchar(max)")
            .Add(x => x.AllowsNull, allowsNull)
            .Add(x => x.Value, value)
            .Add(x => x.IsEngineCut, cut)
            .Add(x => x.IsBinary, binary)
            .Add(x => x.OnClose, () => onClose?.Invoke()));

    [Fact]
    public void It_shows_the_column_its_type_and_nullability_the_size_and_the_whole_value()
    {
        var value = new string('x', 5_000);

        var cut = Render(value);

        Assert.Equal("dialog", cut.Find("[data-testid=results-view-value]").GetAttribute("role"));
        Assert.Equal("true", cut.Find("[data-testid=results-view-value]").GetAttribute("aria-modal"));
        Assert.Equal("notes", cut.Find("[data-testid=results-view-value-title]").TextContent);
        Assert.Equal("nvarchar(max), NULL", cut.Find("[data-testid=results-view-value-type]").TextContent);
        Assert.Equal("5,000 characters", cut.Find("[data-testid=results-view-value-size]").TextContent);
        var text = cut.Find("[data-testid=results-view-value-text]");
        Assert.True(text.HasAttribute("readonly"));
        Assert.Equal(value, text.TextContent);
    }

    [Fact]
    public void A_NOT_NULL_column_says_so()
    {
        var cut = Render("v", allowsNull: false);
        Assert.Equal("nvarchar(max), NOT NULL", cut.Find("[data-testid=results-view-value-type]").TextContent);
    }

    [Fact]
    public void Copy_puts_the_whole_value_on_the_clipboard()
    {
        var ui = _ctx.JSInterop.SetupModule("./js/akml-ui.js");
        ui.Mode = JSRuntimeMode.Loose;
        ui.Setup<bool>("copyText", _ => true).SetResult(true);
        var cut = Render("line one\nline two");

        cut.Find("[data-testid=results-view-value-copy]").Click();

        Assert.Equal("line one\nline two", (string)ui.Invocations["copyText"].Single().Arguments[0]!);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=results-view-value-copied]")));
    }

    [Fact]
    public void An_engine_cut_value_says_the_content_is_unavailable_and_suggests_a_projection()
    {
        var cut = Render("[text 70000 chars]", cut: true);

        Assert.Equal("70,000 characters", cut.Find("[data-testid=results-view-value-size]").TextContent);
        var note = cut.Find("[data-testid=results-view-value-cut]").TextContent;
        Assert.Contains("not available", note);
        Assert.Contains("LEFT([notes], 8000)", note);
        Assert.Empty(cut.FindAll("[data-testid=results-view-value-text]"));
        Assert.Empty(cut.FindAll("[data-testid=results-view-value-copy]"));
    }

    [Fact]
    public void An_engine_cut_binary_value_suggests_SUBSTRING()
    {
        var cut = Render("[binary 2000000 bytes]", cut: true, binary: true);

        Assert.Equal("2,000,000 bytes", cut.Find("[data-testid=results-view-value-size]").TextContent);
        Assert.Contains("SUBSTRING([notes], 1, 8000)", cut.Find("[data-testid=results-view-value-cut]").TextContent);
    }

    [Fact]
    public void Escape_and_Close_close_it()
    {
        int closed = 0;
        var cut = Render("v", onClose: () => closed++);

        cut.Find("[data-testid=results-view-value]").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal(1, closed);

        var again = Render("v", onClose: () => closed++);
        again.Find("[data-testid=results-view-value-close]").Click();
        Assert.Equal(2, closed);
    }
}
