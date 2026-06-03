using System.Diagnostics;

namespace AkmlSql.Web.E2E.Tests.Harness;

/// <summary>
/// Spec 028 (M6) — launches the Blazor WASM web edition via <c>dotnet run</c> and waits for it to
/// serve, so a Playwright test can drive the real app. The scaffolds for US2/US4/US5 referred to
/// this as the "spec-024 DotnetRunFixture"; this is the concrete implementation.
///
/// <para>The app has no launchSettings, so Kestrel binds the default <c>http://localhost:5000</c>.</para>
/// </summary>
public sealed class WebAppFixture : IAsyncDisposable
{
    private Process? _proc;

    public string Url { get; private set; } = "http://localhost:5000/";

    public static async Task<WebAppFixture> StartAsync(CancellationToken ct = default)
    {
        var fixture = new WebAppFixture();
        var repoRoot = FindRepoRoot();
        var csproj = Path.Combine(repoRoot, "src", "AkmlSql.Web", "AkmlSql.Web.csproj");

        var psi = new ProcessStartInfo("dotnet", $"run --project \"{csproj}\" --no-build -c Debug")
        {
            WorkingDirectory = repoRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        fixture._proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start dotnet run.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var res = await http.GetAsync(fixture.Url, ct).ConfigureAwait(false);
                if (res.IsSuccessStatusCode) return fixture;
            }
            catch (Exception) { /* not up yet */ }
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        await fixture.DisposeAsync().ConfigureAwait(false);
        throw new TimeoutException($"Web app did not start at {fixture.Url} within 90s.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root (AKML-SQL.slnx) above the test bin directory.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_proc is { HasExited: false })
        {
            try { _proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            try { await _proc.WaitForExitAsync().ConfigureAwait(false); } catch { /* ignore */ }
        }
        _proc?.Dispose();
    }
}
