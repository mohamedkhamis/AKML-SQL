using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Workspace;

/// <summary>
/// Spec 041 (T112, research R35, FR-044–FR-046) — a remembered layout fitted to the window, and the side panels a
/// narrow window folds away. Limits from contracts/ui.md §1: editor 920 × 320, Schema 180 / 260, AI 300 / 380,
/// results ≥ 120, edge strip 24, splitter 6.
/// </summary>
public sealed class WorkspaceLayoutFitTests
{
    private static readonly WorkspaceLayoutLimits Limits = WorkspaceLayoutLimits.Default;

    [Fact]
    public void Side_panels_are_kept_between_their_minimum_and_what_leaves_the_editor_its_minimum()
    {
        var tooSmall = WorkspaceLayout.Fit(WorkspaceLayout.Default with { SchemaWidth = 40, AiWidth = 10 }, 1600, 900, Limits);
        Assert.Equal(180, tooSmall.SchemaWidth);
        Assert.Equal(300, tooSmall.AiWidth);

        var tooBig = WorkspaceLayout.Fit(WorkspaceLayout.Default with { SchemaWidth = 2000 }, 1600, 900, Limits);
        Assert.Equal(1600 - 920, tooBig.SchemaWidth);
    }

    [Fact]
    public void The_results_height_defaults_to_38_percent_and_stays_between_120_and_the_workspace_less_320()
    {
        Assert.Equal(342, WorkspaceLayout.Fit(WorkspaceLayout.Default, 1600, 900, Limits).ResultsHeight);
        Assert.Equal(120, WorkspaceLayout.Fit(WorkspaceLayout.Default with { ResultsHeight = 10 }, 1600, 900, Limits).ResultsHeight);
        Assert.Equal(900 - 320, WorkspaceLayout.Fit(WorkspaceLayout.Default with { ResultsHeight = 5000 }, 1600, 900, Limits).ResultsHeight);
        Assert.Equal(120, WorkspaceLayout.Fit(WorkspaceLayout.Default with { ResultsHeight = 300 }, 1600, 380, Limits).ResultsHeight);
    }

    [Fact]
    public void A_layout_with_every_region_hidden_gets_the_default_regions_back()
    {
        var nothing = WorkspaceLayout.Default with { NavVisible = false, SchemaVisible = false, AiVisible = false, ResultsVisible = false };

        var corrected = WorkspaceLayout.Corrected(nothing);

        Assert.True(corrected.NavVisible);
        Assert.True(corrected.SchemaVisible);
        Assert.True(corrected.ResultsVisible);
        Assert.Equal(WorkspaceLayout.Default.AiVisible, corrected.AiVisible);
    }

    [Fact]
    public void Fitting_never_shows_a_region_the_user_hid_even_when_every_region_is_hidden()
    {
        // The correction is for a remembered layout on load (FR-045). During a session the user may hide every
        // region — the editor remains — and Ctrl+R must still hide the results pane (found in the browser).
        var nothing = WorkspaceLayout.Default with { NavVisible = false, SchemaVisible = false, AiVisible = false, ResultsVisible = false };

        var fitted = WorkspaceLayout.Fit(nothing, 1600, 900, Limits);

        Assert.False(fitted.NavVisible);
        Assert.False(fitted.SchemaVisible);
        Assert.False(fitted.ResultsVisible);
    }

    [Fact]
    public void Some_hidden_regions_are_left_as_the_user_chose()
    {
        var some = WorkspaceLayout.Default with { SchemaVisible = false, ResultsVisible = false };

        var fitted = WorkspaceLayout.Fit(WorkspaceLayout.Corrected(some), 1600, 900, Limits);

        Assert.False(fitted.SchemaVisible);
        Assert.False(fitted.ResultsVisible);
    }

    [Fact]
    public void A_hidden_results_pane_is_not_maximised()
    {
        var fitted = WorkspaceLayout.Fit(WorkspaceLayout.Default with { ResultsVisible = false, ResultsMaximised = true }, 1600, 900, Limits);
        Assert.False(fitted.ResultsMaximised);
    }

    [Fact]
    public void A_wide_window_folds_nothing()
    {
        var both = WorkspaceLayout.Default with { AiVisible = true };   // 260 + 6 + 920 + 6 + 380 = 1572
        Assert.Equal(WorkspaceFold.None, WorkspaceLayout.Fold(both, 1600, Limits));
    }

    [Fact]
    public void A_narrower_window_folds_the_AI_panel_first_then_the_Schema_panel()
    {
        var both = WorkspaceLayout.Default with { AiVisible = true };

        Assert.Equal(new WorkspaceFold(SchemaFolded: false, AiFolded: true), WorkspaceLayout.Fold(both, 1300, Limits));
        Assert.Equal(new WorkspaceFold(SchemaFolded: true, AiFolded: true), WorkspaceLayout.Fold(both, 1100, Limits));
    }

    [Fact]
    public void At_1100_px_the_default_Schema_panel_folds_too()
    {
        // 260 + 6 + 920 + 24 (the hidden AI panel's strip) = 1210 > 1100.
        Assert.Equal(new WorkspaceFold(SchemaFolded: true, AiFolded: false), WorkspaceLayout.Fold(WorkspaceLayout.Default, 1100, Limits));
        Assert.Equal(WorkspaceFold.None, WorkspaceLayout.Fold(WorkspaceLayout.Default, 1210, Limits));
    }

    [Fact]
    public void Widening_brings_back_panels_the_user_had_not_hidden_but_never_one_they_hid()
    {
        var aiHidden = WorkspaceLayout.Default with { AiVisible = false };

        Assert.True(WorkspaceLayout.Fold(aiHidden, 1000, Limits).SchemaFolded);
        var wide = WorkspaceLayout.Fold(aiHidden, 1900, Limits);
        Assert.False(wide.SchemaFolded);
        Assert.False(wide.AiFolded);   // hidden, not folded: the layout says so
    }

    [Fact]
    public void The_panel_the_user_just_opened_folds_last()
    {
        // At 1440 px both default panels need 260 + 6 + 380 + 6 + 920 = 1572 px: one has to fold. Opening the AI
        // panel by hand folds the Schema panel instead, so the click shows what was asked for.
        var both = WorkspaceLayout.Default with { AiVisible = true };

        Assert.Equal(new WorkspaceFold(SchemaFolded: false, AiFolded: true), WorkspaceLayout.Fold(both, 1440, Limits));
        Assert.Equal(new WorkspaceFold(SchemaFolded: true, AiFolded: false), WorkspaceLayout.Fold(both, 1440, Limits, keep: WorkspacePanel.Ai));
        Assert.Equal(new WorkspaceFold(SchemaFolded: false, AiFolded: true), WorkspaceLayout.Fold(both, 1440, Limits, keep: WorkspacePanel.Schema));
    }

    [Fact]
    public void A_window_too_narrow_for_the_kept_panel_alone_still_folds_it()
    {
        // 24 + 380 + 6 + 920 = 1330 > 1100: the editor keeps its minimum (FR-046).
        var both = WorkspaceLayout.Default with { AiVisible = true };

        Assert.Equal(new WorkspaceFold(SchemaFolded: true, AiFolded: true), WorkspaceLayout.Fold(both, 1100, Limits, keep: WorkspacePanel.Ai));
    }

    [Fact]
    public void Folding_never_changes_the_layout()
    {
        var layout = WorkspaceLayout.Default with { AiVisible = true };

        WorkspaceLayout.Fold(layout, 900, Limits);

        Assert.True(layout.AiVisible);
        Assert.True(layout.SchemaVisible);
    }
}
