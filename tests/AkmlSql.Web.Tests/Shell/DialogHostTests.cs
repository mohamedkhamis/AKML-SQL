using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 041 (T016, research R59, FR-086) — the one in-app dialog that replaces every browser
/// pop-up. Cancel is the safe default: focused on open and the Escape target. A destructive
/// confirmation must be clicked deliberately — Enter never confirms it.
/// </summary>
public sealed class DialogHostTests : BunitContext
{
    private readonly DialogService _dialogs = new();

    public DialogHostTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IDialogService>(_dialogs);
    }

    [Fact]
    public void Nothing_renders_while_no_dialog_is_open()
    {
        var cut = Render<DialogHost>();
        Assert.Empty(cut.FindAll("[data-testid=dialog]"));
    }

    [Fact]
    public void Confirm_renders_a_modal_dialog_with_title_and_message()
    {
        var cut = Render<DialogHost>();
        _ = _dialogs.ConfirmAsync(new DialogRequest { Title = "Remove provider", Message = "This removes the key." });

        var dialog = cut.WaitForElement("[data-testid=dialog]");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal("Remove provider", cut.Find("[data-testid=dialog-title]").TextContent);
        Assert.Equal("This removes the key.", cut.Find("[data-testid=dialog-message]").TextContent);
        Assert.Equal(dialog.GetAttribute("aria-labelledby"), cut.Find("[data-testid=dialog-title]").Id);
        Assert.Equal(dialog.GetAttribute("aria-describedby"), cut.Find("[data-testid=dialog-message]").Id);
    }

    [Fact]
    public void Details_are_listed()
    {
        var cut = Render<DialogHost>();
        _ = _dialogs.ConfirmAsync(new DialogRequest
        {
            Title = "Restore all defaults",
            Message = "This resets every section:",
            Details = new[] { "General: theme", "AI assistance: 3 providers and their keys" },
        });

        var items = cut.WaitForElements("[data-testid=dialog-details] li");
        Assert.Equal(2, items.Count);
        Assert.Equal("AI assistance: 3 providers and their keys", items[1].TextContent);
    }

    [Fact]
    public void Cancel_is_focused_when_the_dialog_opens()
    {
        var cut = Render<DialogHost>();
        _ = _dialogs.ConfirmAsync(new DialogRequest { Title = "t", Message = "m" });
        cut.WaitForElement("[data-testid=dialog-cancel]");

        cut.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke());
    }

    [Fact]
    public async Task Confirm_button_resolves_true()
    {
        var cut = Render<DialogHost>();
        var task = _dialogs.ConfirmAsync(new DialogRequest { Title = "t", Message = "m", ConfirmLabel = "Remove" });

        var confirm = cut.WaitForElement("[data-testid=dialog-confirm]");
        Assert.Equal("Remove", confirm.TextContent.Trim());
        confirm.Click();

        Assert.True(await task);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=dialog]")));
    }

    [Fact]
    public async Task Cancel_button_resolves_false()
    {
        var cut = Render<DialogHost>();
        var task = _dialogs.ConfirmAsync(new DialogRequest { Title = "t", Message = "m" });

        cut.WaitForElement("[data-testid=dialog-cancel]").Click();

        Assert.False(await task);
    }

    [Fact]
    public async Task Escape_cancels()
    {
        var cut = Render<DialogHost>();
        var task = _dialogs.ConfirmAsync(new DialogRequest { Title = "t", Message = "m" });

        cut.WaitForElement("[data-testid=dialog]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        Assert.False(await task);
    }

    [Fact]
    public void A_destructive_confirmation_uses_the_danger_style_and_Enter_does_not_confirm_it()
    {
        var cut = Render<DialogHost>();
        Task<bool>? task = null;
        task = _dialogs.ConfirmAsync(new DialogRequest { Title = "t", Message = "m", Destructive = true });

        var confirm = cut.WaitForElement("[data-testid=dialog-confirm]");
        Assert.Contains("akml-btn--danger", confirm.ClassList);

        cut.Find("[data-testid=dialog]").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.False(task!.IsCompleted);
        Assert.NotEmpty(cut.FindAll("[data-testid=dialog]"));
    }

    [Fact]
    public async Task Prompt_returns_the_typed_value()
    {
        var cut = Render<DialogHost>();
        var task = _dialogs.PromptAsync(new PromptRequest { Title = "Save as", Label = "Name", InitialValue = "SQLQuery1" });

        var input = cut.WaitForElement("[data-testid=dialog-input]");
        Assert.Equal("SQLQuery1", input.GetAttribute("value"));
        input.Input("orders-audit");
        cut.Find("[data-testid=dialog-confirm]").Click();

        Assert.Equal("orders-audit", await task);
    }

    [Fact]
    public async Task Prompt_validation_shows_the_message_inline_and_blocks_confirm()
    {
        var cut = Render<DialogHost>();
        var task = _dialogs.PromptAsync(new PromptRequest
        {
            Title = "Save as",
            Label = "Name",
            InitialValue = "x",
            Validate = v => string.IsNullOrWhiteSpace(v) ? "Enter a name." : null,
        });

        cut.WaitForElement("[data-testid=dialog-input]").Input("   ");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Enter a name.", cut.Find("[data-testid=dialog-error]").TextContent);
            Assert.True(cut.Find("[data-testid=dialog-confirm]").HasAttribute("disabled"));
            Assert.Equal("true", cut.Find("[data-testid=dialog-input]").GetAttribute("aria-invalid"));
        });

        cut.Find("[data-testid=dialog-input]").Input("ok");
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=dialog-error]")));
        cut.Find("[data-testid=dialog-confirm]").Click();
        Assert.Equal("ok", await task);
    }

    [Fact]
    public async Task Prompt_cancel_returns_null()
    {
        var cut = Render<DialogHost>();
        var task = _dialogs.PromptAsync(new PromptRequest { Title = "t", Label = "l" });

        cut.WaitForElement("[data-testid=dialog-cancel]").Click();

        Assert.Null(await task);
    }

    [Fact]
    public async Task Enter_in_the_prompt_input_confirms_a_valid_value()
    {
        var cut = Render<DialogHost>();
        var task = _dialogs.PromptAsync(new PromptRequest { Title = "t", Label = "l", InitialValue = "abc" });

        cut.WaitForElement("[data-testid=dialog-input]").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal("abc", await task);
    }

    [Fact]
    public async Task A_second_request_waits_until_the_first_closes()
    {
        var cut = Render<DialogHost>();
        var first = _dialogs.ConfirmAsync(new DialogRequest { Title = "First", Message = "1" });
        var second = _dialogs.ConfirmAsync(new DialogRequest { Title = "Second", Message = "2" });

        Assert.Equal("First", cut.WaitForElement("[data-testid=dialog-title]").TextContent);
        cut.Find("[data-testid=dialog-confirm]").Click();
        Assert.True(await first);

        cut.WaitForAssertion(() => Assert.Equal("Second", cut.Find("[data-testid=dialog-title]").TextContent));
        Assert.False(second.IsCompleted);
        cut.Find("[data-testid=dialog-cancel]").Click();
        Assert.False(await second);
    }

    [Fact]
    public async Task Alert_shows_one_OK_button()
    {
        var cut = Render<DialogHost>();
        var task = _dialogs.AlertAsync("Import", "1 setting skipped.");

        cut.WaitForElement("[data-testid=dialog-confirm]");
        Assert.Empty(cut.FindAll("[data-testid=dialog-cancel]"));
        Assert.Equal("OK", cut.Find("[data-testid=dialog-confirm]").TextContent.Trim());

        cut.Find("[data-testid=dialog-confirm]").Click();
        await task;
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void The_dialog_never_reuses_the_error_banner_test_id()
    {
        var cut = Render<DialogHost>();
        _ = _dialogs.AlertAsync("t", "m");
        cut.WaitForElement("[data-testid=dialog]");

        Assert.Empty(cut.FindAll("[data-testid=error-banner]"));
    }
}
