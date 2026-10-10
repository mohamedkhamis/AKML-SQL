using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Styles;

/// <summary>
/// Spec 041 (T184, research R29, FR-063, FR-084) — the toolbar's style picker tells the truth: the engine's styles
/// are "Shared with SSMS" only when SSMS started the engine, a name that appears in two groups says which one in the
/// closed control, and a group with nothing in it is not shown.
/// </summary>
public sealed class ProfilePickerDisambiguationTests : BunitContext
{
    public ProfilePickerDisambiguationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IDialogService>(new FakeDialogService());
    }

    private IRenderedComponent<ProfilePickerComponent> RenderPicker(string? hostKind, string engineStyleName)
    {
        var engine = new FakeStylesEngine { EngineHostKind = hostKind };
        engine.SeedCustom(engineStyleName);   // already on the engine (the browser would refuse to save a duplicate)
        var store = new ProfileStore(new InMemoryIndexedDbAdapter(), engine);
        Services.AddSingleton<IProfileStore>(store);
        var picker = Render<ProfilePickerComponent>();
        picker.WaitForAssertion(() => Assert.NotEmpty(picker.FindAll("option")));
        return picker;
    }

    private static string[] Groups(IRenderedComponent<ProfilePickerComponent> picker) =>
        picker.FindAll("optgroup").Select(g => g.GetAttribute("label")!).ToArray();

    [Theory]
    [InlineData("ide", "Shared with SSMS")]
    [InlineData("service", "On this engine")]
    [InlineData(null, "On this engine")]
    public void The_engine_group_is_Shared_with_SSMS_only_for_an_engine_SSMS_started(string? hostKind, string expected)
    {
        var picker = RenderPicker(hostKind, "Team style");

        picker.WaitForAssertion(() => Assert.Contains(expected, Groups(picker)));
        Assert.DoesNotContain(expected == "Shared with SSMS" ? "On this engine" : "Shared with SSMS", Groups(picker));
    }

    [Fact]
    public void A_name_in_two_groups_says_which_group_in_the_closed_control()
    {
        var picker = RenderPicker("service", "Khamis Style");   // the built-in style's name

        picker.WaitForAssertion(() =>
        {
            var texts = picker.FindAll("option").Select(o => o.TextContent.Trim()).ToArray();
            Assert.Contains("Khamis Style (built-in)", texts);
            Assert.Contains("Khamis Style (on this engine)", texts);
        });
    }

    [Fact]
    public void A_unique_name_carries_no_suffix_and_empty_groups_are_hidden()
    {
        var picker = RenderPicker("ide", "Team style");

        picker.WaitForAssertion(() => Assert.Contains("Team style", picker.FindAll("option").Select(o => o.TextContent.Trim())));
        Assert.All(Groups(picker), g => Assert.NotEmpty(picker.FindAll($"optgroup[label='{g}'] option")));
        Assert.DoesNotContain("User", Groups(picker));
        Assert.Empty(picker.FindAll("option[disabled]"));
    }
}
