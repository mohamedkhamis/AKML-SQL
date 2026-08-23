using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Shared;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Refactoring;

/// <summary>
/// Spec 027 (M5 offline closure) T021/T022 (US3). bUnit coverage of the heavyweight input
/// dialog's field-gating + validation, and of the preview panel's heavyweight render mode
/// (change list + blocking-error gating). The actual bridge preview/apply round-trip is the
/// US6 E2E path; these cover the deterministic UI.
/// </summary>
public sealed class RefactorInputDialogTests : BunitContext
{
    [Fact]
    public void Smart_rename_shows_identifier_and_newname_fields_only()
    {
        var cut = Render<RefactorInputDialog>(p => p
            .Add(x => x.Title, "Smart Rename")
            .Add(x => x.NeedsOriginalIdentifier, true)
            .Add(x => x.NeedsNewName, true)
            .Add(x => x.NeedsUnitName, false));

        Assert.NotEmpty(cut.FindAll("[data-testid='rid-original']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='rid-newname']"));
        Assert.Empty(cut.FindAll("[data-testid='rid-unitname']"));
    }

    [Fact]
    public void Extract_proc_shows_only_the_unit_name_field()
    {
        var cut = Render<RefactorInputDialog>(p => p
            .Add(x => x.Title, "Extract Procedure")
            .Add(x => x.NeedsUnitName, true));

        Assert.NotEmpty(cut.FindAll("[data-testid='rid-unitname']"));
        Assert.Empty(cut.FindAll("[data-testid='rid-original']"));
        Assert.Empty(cut.FindAll("[data-testid='rid-newname']"));
    }

    [Fact]
    public async Task Submit_blocks_and_does_not_raise_when_a_required_field_is_empty()
    {
        var raised = false;
        var cut = Render<RefactorInputDialog>(p => p
            .Add(x => x.NeedsOriginalIdentifier, true)
            .Add(x => x.NeedsNewName, true)
            .Add(x => x.OnSubmit, (RefactorInputDialog.RefactorInputs _) => { raised = true; }));

        await cut.Find("[data-testid='rid-preview']").ClickAsync(new());
        Assert.False(raised);   // validation blocks the empty submit
        Assert.Contains("Enter the identifier", cut.Markup);
    }

    [Fact]
    public async Task Submit_raises_with_trimmed_values_when_valid()
    {
        RefactorInputDialog.RefactorInputs? captured = null;
        var cut = Render<RefactorInputDialog>(p => p
            .Add(x => x.NeedsOriginalIdentifier, true)
            .Add(x => x.NeedsNewName, true)
            .Add(x => x.OnSubmit, (RefactorInputDialog.RefactorInputs i) => { captured = i; }));

        cut.Find("[data-testid='rid-original']").Input("  OldName  ");
        cut.Find("[data-testid='rid-newname']").Input("NewName");
        await cut.Find("[data-testid='rid-preview']").ClickAsync(new());

        Assert.NotNull(captured);
        Assert.Equal("OldName", captured!.OriginalIdentifier);   // trimmed
        Assert.Equal("NewName", captured.NewName);
    }

    [Fact]
    public void Preview_panel_renders_heavyweight_change_list_and_enables_apply()
    {
        var heavy = new RefactorPreviewResponse
        {
            CanApply = true,
            Changes = new[]
            {
                new RefactorChangeInfo
                {
                    FilePath = "", StartOffset = 0, EndOffset = 3, OldText = "Old", NewText = "New",
                    Line = 1, Column = 1, ChangeCategory = "rename",
                },
            },
        };

        var cut = Render<RefactorPreviewPanel>(p => p
            .Add(x => x.Title, "Smart Rename")
            .Add(x => x.Heavy, heavy));

        Assert.NotEmpty(cut.FindAll("[data-testid='refactor-changes']"));
        var apply = cut.Find("[data-testid='refactor-apply']");
        Assert.False(apply.HasAttribute("disabled"));
    }

    [Fact]
    public void Preview_panel_shows_errors_and_disables_apply_when_cannot_apply()
    {
        var heavy = new RefactorPreviewResponse
        {
            CanApply = false,
            Changes = System.Array.Empty<RefactorChangeInfo>(),
            Errors = new[] { "Name collision: 'X' already exists in this scope" },
        };

        var cut = Render<RefactorPreviewPanel>(p => p
            .Add(x => x.Title, "Smart Rename")
            .Add(x => x.Heavy, heavy));

        Assert.NotEmpty(cut.FindAll("[data-testid='refactor-errors']"));
        Assert.Contains("Name collision", cut.Markup);
        var apply = cut.Find("[data-testid='refactor-apply']");
        Assert.True(apply.HasAttribute("disabled"));
    }
}
