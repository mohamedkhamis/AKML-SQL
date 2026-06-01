using System.Threading.Tasks;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Refactoring;

/// <summary>
/// Spec 027 (M5 offline closure) T018 (US2). bUnit coverage of the lightweight refactoring
/// preview panel's render states — the deterministic, JS-free part of the US2 surface. (The
/// op output itself is proven by LightweightParityTests; the editor apply round-trip is
/// interactive.)
/// </summary>
public sealed class RefactorPreviewPanelTests : TestContext
{
    [Fact]
    public void Shows_before_and_after_when_changed()
    {
        var preview = new LightweightPreview(
            Before: "SELECT 1;",
            After: "SELECT 1",
            Warnings: System.Array.Empty<string>(),
            Changed: true);

        var cut = RenderComponent<RefactorPreviewPanel>(p => p
            .Add(x => x.Title, "Remove semicolons")
            .Add(x => x.Preview, preview));

        var after = cut.Find("[data-testid='refactor-after']");
        Assert.Equal("SELECT 1", after.TextContent);
        Assert.Contains("Remove semicolons", cut.Markup);

        // Apply is enabled when there is a change.
        var apply = cut.Find("[data-testid='refactor-apply']");
        Assert.False(apply.HasAttribute("disabled"));
    }

    [Fact]
    public void Shows_no_change_state_and_disables_apply_when_not_changed()
    {
        var preview = new LightweightPreview(
            Before: "SELECT 1",
            After: "SELECT 1",
            Warnings: System.Array.Empty<string>(),
            Changed: false);

        var cut = RenderComponent<RefactorPreviewPanel>(p => p
            .Add(x => x.Title, "Convert old-style joins")
            .Add(x => x.Preview, preview));

        Assert.NotEmpty(cut.FindAll("[data-testid='refactor-nochange']"));
        var apply = cut.Find("[data-testid='refactor-apply']");
        Assert.True(apply.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Apply_button_raises_OnApply_when_changed()
    {
        var raised = false;
        var preview = new LightweightPreview("a", "b", System.Array.Empty<string>(), Changed: true);

        var cut = RenderComponent<RefactorPreviewPanel>(p => p
            .Add(x => x.Preview, preview)
            .Add(x => x.OnApply, () => { raised = true; }));

        await cut.Find("[data-testid='refactor-apply']").ClickAsync(new());
        Assert.True(raised);
    }

    [Fact]
    public async Task Cancel_raises_OnCancel()
    {
        var cancelled = false;
        var preview = new LightweightPreview("a", "b", System.Array.Empty<string>(), Changed: true);

        var cut = RenderComponent<RefactorPreviewPanel>(p => p
            .Add(x => x.Preview, preview)
            .Add(x => x.OnCancel, () => { cancelled = true; }));

        // The footer Cancel button is the last .akml-tool-button (Apply is the other).
        var buttons = cut.FindAll(".akml-rpv-footer .akml-tool-button");
        await buttons[0].ClickAsync(new());   // Cancel
        Assert.True(cancelled);
    }
}
