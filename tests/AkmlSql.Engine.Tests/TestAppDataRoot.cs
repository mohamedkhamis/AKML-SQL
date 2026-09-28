using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace AkmlSql.Engine.Tests;

/// <summary>
/// Redirects AKML SQL's app data (<c>AKML_APP_DATA_ROOT</c>) to a temp folder for the whole
/// engine test run, before any test executes. Tests that build the full engine
/// (<c>EngineComposition.Build</c>, <c>EngineHost</c>) otherwise read the user's real
/// <c>config.json</c> and open the real history database: a 2026-09-28 run migrated this
/// machine's database to schema v3 and could have trimmed it by retention. A run that already
/// sets the variable keeps its own root.
/// </summary>
internal static class TestAppDataRoot
{
    [ModuleInitializer]
    internal static void Redirect()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AKML_APP_DATA_ROOT")))
            return;

        var root = Path.Combine(Path.GetTempPath(), "akmlsql-engine-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("AKML_APP_DATA_ROOT", root);
    }
}
