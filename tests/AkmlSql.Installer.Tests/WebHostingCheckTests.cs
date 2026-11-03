using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;

namespace AkmlSql.Installer.Tests;

public sealed class WebHostingCheckTests
{
    [Fact]
    public void Port_holder_uses_URLs_and_images_without_localised_labels()
    {
        var text = "Andere Warteschlange:\n    HTTP://+:59418/\n    C:\\Other\\foreign.exe\n" +
            "Unsere Warteschlange:\n    HTTPS://+:59417/\n    C:\\Program Files\\AKML SQL\\Engine\\AkmlSql.Engine.exe\n";
        var value = HostingTestTools.DotSource("web-hosting-check.ps1",
            $"Get-RegisteredPortHolder -Text '{text.Replace("'", "''")}' -Port 59417");
        Assert.Equal(@"C:\Program Files\AKML SQL\Engine\AkmlSql.Engine.exe", value);
    }

    [Theory]
    [InlineData("<html>app</html>", true, "ExpectEngine", 0)]
    [InlineData("<html>AKML SQL engine: reachable</html>", false, "ExpectEngine", 2)]
    [InlineData("<html>app</html>", false, "ExpectEngine", 2)]
    [InlineData("<script src=\"_framework/blazor.webassembly.js\"></script>", false, "ExpectBundle", 0)]
    [InlineData("<html>IIS welcome</html>", false, "ExpectBundle", 2)]
    public async Task Check_recognises_the_expected_host(string body, bool marker, string expectation, int expectedExit)
    {
        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync();
                    context.Response.ContentType = "text/html; charset=utf-8";
                    if (marker) context.Response.Headers.Add("X-AKML-Host", "engine");
                    var bytes = Encoding.UTF8.GetBytes(body);
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { }
        });
        var reason = Path.Combine(Path.GetTempPath(), "akml-check-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var result = await Task.Run(() => HostingTestTools.Run("powershell.exe", "-NoProfile", "-NonInteractive",
                "-ExecutionPolicy", "Bypass", "-File", HostingTestTools.Script("web-hosting-check.ps1"),
                "-Url", $"http://127.0.0.1:{port}/", "-" + expectation, "-TimeoutSeconds", "2", "-ReasonPath", reason));
            Assert.Equal(expectedExit, result.ExitCode);
            if (expectedExit != 0) Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(reason)));
        }
        finally
        {
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(5));
            File.Delete(reason);
        }
    }

    [Fact]
    public void Closed_port_times_out_and_records_the_reason()
    {
        var reason = Path.Combine(Path.GetTempPath(), "akml-check-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = HostingTestTools.Run("powershell.exe", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
                "-File", HostingTestTools.Script("web-hosting-check.ps1"), "-Url", $"http://127.0.0.1:{FreePort()}/",
                "-ExpectEngine", "-TimeoutSeconds", "3", "-ReasonPath", reason);
            Assert.Equal(1, result.ExitCode);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), result.Output);
            Assert.False(string.IsNullOrWhiteSpace(File.ReadAllText(reason)));
        }
        finally { File.Delete(reason); }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
