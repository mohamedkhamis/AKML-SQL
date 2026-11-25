using System;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Xunit;

namespace AkmlSql.Web.Tests.Refactoring;

/// <summary>
/// Spec 047 (WEB-011, WEB-012, contracts/workspace-ai.md §3) — the refactoring preview is a keyboard dialog: focus moves to
/// Apply (to Cancel when there is nothing to apply), Tab stays inside, Escape cancels and focus goes back; and an operation
/// that changes nothing still shows its warnings ("Could not resolve columns for: dbo.Regio"). Before, Escape did nothing,
/// focus stayed behind the dialog, and a no-change preview hid the reason.
/// </summary>
public sealed class RefactorPreviewKeyboardTests : BunitContext
{
    private readonly BunitJSModuleInterop _ui;

    public RefactorPreviewKeyboardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ui = JSInterop.SetupModule("./js/akml-ui.js");
        _ui.Mode = JSRuntimeMode.Loose;
    }

    [Theory]
    [InlineData(true, "[data-testid=refactor-apply]")]
    [InlineData(false, "[data-testid=refactor-cancel]")]
    public void Focus_moves_to_Apply_or_to_Cancel_when_there_is_nothing_to_apply(bool changed, string expected)
    {
        Render<RefactorPreviewPanel>(p => p.Add(x => x.Preview, new LightweightPreview("a", changed ? "b" : "a", Array.Empty<string>(), changed)));

        _ui.VerifyInvoke("stashFocus");
        Assert.Equal(expected, _ui.VerifyInvoke("focusFirst").Arguments[1]);
        _ui.VerifyInvoke("trapTab");
    }

    [Fact]
    public void Escape_cancels_and_gives_focus_back()
    {
        var cancelled = false;
        var cut = Render<RefactorPreviewPanel>(p => p
            .Add(x => x.Preview, new LightweightPreview("a", "b", Array.Empty<string>(), true))
            .Add(x => x.OnCancel, () => cancelled = true));

        cut.Find("[role=dialog]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.True(cancelled);
        _ui.VerifyInvoke("restoreFocus");
    }

    [Fact]
    public void A_preview_that_changes_nothing_still_lists_its_warnings()
    {
        var cut = Render<RefactorPreviewPanel>(p => p.Add(x => x.Preview,
            new LightweightPreview("insert into dbo.Regio values (1)", "insert into dbo.Regio values (1)",
                new[] { "Could not resolve columns for: dbo.Regio" }, false)));

        Assert.NotNull(cut.Find("[data-testid=refactor-nochange]"));
        Assert.Contains("Could not resolve columns for: dbo.Regio", cut.Find("[data-testid=refactor-warnings]").TextContent);
    }
}
