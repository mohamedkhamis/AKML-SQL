using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AkmlSql.Web.Services;

/// <summary>What the View commands act on: the editor page's workspace.</summary>
public interface IWorkspaceViewHost
{
    /// <summary>The layout as it is on screen: a side panel a narrow window folded away reads as hidden.</summary>
    WorkspaceLayout Layout { get; }

    Task ToggleNavAsync();
    Task ToggleSchemaAsync();
    Task ToggleResultsAsync();
    Task ToggleAiAsync();
    Task ToggleMaximiseResultsAsync();

    /// <summary>F6 / Shift+F6: focus the next (or previous) pane.</summary>
    Task CyclePanesAsync(bool backwards);

    Task ResetLayoutAsync();
}

/// <summary>
/// Spec 041 (research R38, FR-042, contracts/ui.md §3) — the View commands, one table for the View menu, the
/// Command Palette and the keys: each id, its title, the key shown beside it (from
/// <see cref="WorkspaceKeyMap"/>, so a key is shown only where it is claimed) and a check mark that mirrors the
/// layout. <c>view:toggle-ai</c> replaces the old <c>editor:ai</c>.
/// </summary>
public static class WorkspaceViewActions
{
    public const string ToggleNav = "view:toggle-nav";
    public const string ToggleSchema = "view:toggle-schema";
    public const string ToggleResults = "view:toggle-results";
    public const string ToggleAi = "view:toggle-ai";
    public const string MaximiseResults = "view:maximise-results";
    public const string CyclePanes = "view:cycle-panes";
    public const string CyclePanesBack = "view:cycle-panes-back";
    public const string ResetLayout = "view:reset-layout";

    public static IReadOnlyList<CommandAction> Build(IWorkspaceViewHost host)
    {
        if (host == null) throw new ArgumentNullException(nameof(host));
        return new[]
        {
            Toggle(ToggleNav, "Navigation bar", "menu", host.ToggleNavAsync, () => host.Layout.NavVisible),
            Toggle(ToggleSchema, "Schema panel", "folder-tree", host.ToggleSchemaAsync, () => host.Layout.SchemaVisible),
            Toggle(ToggleResults, "Results pane", "panel-bottom", host.ToggleResultsAsync, () => host.Layout.ResultsVisible),
            Toggle(ToggleAi, "AI panel", "bot", host.ToggleAiAsync, () => host.Layout.AiVisible),
            new CommandAction
            {
                Id = MaximiseResults, Title = "Maximise results / Restore results", Group = CommandGroup.View,
                Icon = "maximize-2", Shortcut = WorkspaceKeyMap.DisplayFor(MaximiseResults), Run = host.ToggleMaximiseResultsAsync,
                IsChecked = () => host.Layout.ResultsMaximised,
            },
            new CommandAction
            {
                Id = CyclePanes, Title = "Next pane", Group = CommandGroup.View,
                Icon = "arrow-right", Shortcut = WorkspaceKeyMap.DisplayFor(CyclePanes), Run = () => host.CyclePanesAsync(false),
            },
            new CommandAction
            {
                Id = CyclePanesBack, Title = "Previous pane", Group = CommandGroup.View,
                Icon = "arrow-left", Shortcut = WorkspaceKeyMap.DisplayFor(CyclePanesBack), Run = () => host.CyclePanesAsync(true),
            },
            new CommandAction
            {
                Id = ResetLayout, Title = "Reset layout", Group = CommandGroup.View,
                Icon = "rotate-ccw", Shortcut = WorkspaceKeyMap.DisplayFor(ResetLayout), Run = host.ResetLayoutAsync,
            },
        };
    }

    private static CommandAction Toggle(string id, string title, string icon, Func<Task> run, Func<bool> isChecked) => new()
    {
        Id = id,
        Title = title,
        Group = CommandGroup.View,
        Icon = icon,
        Shortcut = WorkspaceKeyMap.DisplayFor(id),
        Run = run,
        IsChecked = isChecked,
    };
}
