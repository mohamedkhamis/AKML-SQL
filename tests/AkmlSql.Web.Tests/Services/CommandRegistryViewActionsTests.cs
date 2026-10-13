using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Services;

/// <summary>
/// Spec 041 (T115, research R38, FR-042) — the View commands are one table for the View menu, the palette and
/// the keys: the ids and titles of contracts/ui.md §3, the key from WorkspaceKeyMap beside each, a check mark
/// that follows the layout.
/// </summary>
public sealed class CommandRegistryViewActionsTests
{
    private sealed class Host : IWorkspaceViewHost
    {
        public WorkspaceLayout Layout { get; set; } = WorkspaceLayout.Default;
        public List<string> Calls { get; } = new();
        public Task ToggleNavAsync() { Calls.Add("nav"); return Task.CompletedTask; }
        public Task ToggleSchemaAsync() { Calls.Add("schema"); return Task.CompletedTask; }
        public Task ToggleResultsAsync() { Calls.Add("results"); return Task.CompletedTask; }
        public Task ToggleAiAsync() { Calls.Add("ai"); return Task.CompletedTask; }
        public Task ToggleMaximiseResultsAsync() { Calls.Add("max"); return Task.CompletedTask; }
        public Task CyclePanesAsync(bool backwards) { Calls.Add(backwards ? "back" : "next"); return Task.CompletedTask; }
        public Task ResetLayoutAsync() { Calls.Add("reset"); return Task.CompletedTask; }
    }

    [Fact]
    public void The_view_commands_have_the_contract_ids_titles_and_keys()
    {
        var actions = WorkspaceViewActions.Build(new Host());

        Assert.Equal(new[]
        {
            ("view:toggle-nav", "Navigation bar", (string?)null),
            ("view:toggle-schema", "Schema panel", "F8"),
            ("view:toggle-results", "Results pane", "Ctrl+R"),
            ("view:toggle-ai", "AI panel", null),
            ("view:maximise-results", "Maximise results / Restore results", null),
            ("view:cycle-panes", "Next pane", "F6"),
            ("view:cycle-panes-back", "Previous pane", "Shift+F6"),
            ("view:reset-layout", "Reset layout", null),
        }, actions.Select(a => (a.Id, a.Title, a.Shortcut)));
        Assert.All(actions, a => Assert.Equal(CommandGroup.View, a.Group));
        Assert.DoesNotContain(actions, a => a.Id == "editor:ai");
    }

    [Fact]
    public void The_check_marks_follow_the_layout()
    {
        var host = new Host { Layout = WorkspaceLayout.Default with { SchemaVisible = false, AiVisible = true, ResultsMaximised = true } };
        var actions = WorkspaceViewActions.Build(host).ToDictionary(a => a.Id);

        Assert.True(actions["view:toggle-nav"].IsOn);
        Assert.False(actions["view:toggle-schema"].IsOn);
        Assert.True(actions["view:toggle-results"].IsOn);
        Assert.True(actions["view:toggle-ai"].IsOn);
        Assert.True(actions["view:maximise-results"].IsOn);

        host.Layout = host.Layout with { SchemaVisible = true };
        Assert.True(actions["view:toggle-schema"].IsOn);
    }

    [Fact]
    public async Task Each_command_runs_its_action()
    {
        var host = new Host();
        foreach (var action in WorkspaceViewActions.Build(host)) await action.Run();

        Assert.Equal(new[] { "nav", "schema", "results", "ai", "max", "next", "back", "reset" }, host.Calls);
    }

    [Fact]
    public void Every_view_key_resolves_to_a_view_command()
    {
        var ids = WorkspaceViewActions.Build(new Host()).Select(a => a.Id).ToHashSet();

        var viewKeys = WorkspaceKeyMap.Bindings.Where(b => b.CommandId.StartsWith("view:", StringComparison.Ordinal));
        Assert.All(viewKeys, b => Assert.Contains(b.CommandId, ids));
    }
}
