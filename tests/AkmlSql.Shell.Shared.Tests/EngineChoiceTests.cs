using System.Collections.Generic;
using AkmlSql.Shell.Shared.Ipc;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests;

/// <summary>
/// Spec 047 installed-product run of 2026-11-22 (F8): SSMS started an engine of 2026-10-10 left by hand in the extension's
/// own Engine folder, because that folder was always tried first, so the installed 1.26.1122.1400 engine never ran and
/// SSMS kept the October formatter and Find Invalid Objects. The engine of the extension's own version now wins wherever
/// it is; without one the old order stands.
/// </summary>
public sealed class EngineChoiceTests
{
    private const string Left = @"C:\SSMS\Release\Common7\IDE\Extensions\AkmlSql\Engine\AkmlSql.Engine.exe";
    private const string Installed = @"C:\Program Files (x86)\AKML SQL\Engine\AkmlSql.Engine.exe";

    [Fact]
    public void The_engine_of_the_extensions_version_wins_over_one_left_in_the_extension_folder()
    {
        var found = new List<(string Path, string Version)> { (Left, "1.26.1010.2224"), (Installed, "1.26.1122.1400") };

        var chosen = EngineProcessManager.ChooseEngine(found, "1.26.1122.1400");

        Assert.Equal(Installed, chosen?.Path);
    }

    [Fact]
    public void A_developer_engine_of_the_same_version_in_the_extension_folder_is_still_first()
    {
        var found = new List<(string Path, string Version)> { (Left, "1.26.1122.1400"), (Installed, "1.26.1122.1400") };

        Assert.Equal(Left, EngineProcessManager.ChooseEngine(found, "1.26.1122.1400")?.Path);
    }

    [Fact]
    public void Without_an_engine_of_the_extensions_version_the_first_found_is_used()
    {
        var found = new List<(string Path, string Version)> { (Left, "1.26.1010.2224"), (Installed, "1.26.1104.1510") };

        Assert.Equal(Left, EngineProcessManager.ChooseEngine(found, "1.26.1122.1400")?.Path);
        Assert.Equal(Left, EngineProcessManager.ChooseEngine(found, null)?.Path);
    }

    [Fact]
    public void No_engine_found_is_none()
        => Assert.Null(EngineProcessManager.ChooseEngine(new List<(string Path, string Version)>(), "1.26.1122.1400"));
}
