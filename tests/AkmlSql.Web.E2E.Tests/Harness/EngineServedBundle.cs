using System.Text.Json;

namespace AkmlSql.Web.E2E.Tests.Harness;

/// <summary>A byte-for-byte Debug web bundle, separate from the sandbox engine's private state.</summary>
internal sealed class EngineServedBundle : IDisposable
{
    public string Root { get; }

    public EngineServedBundle()
    {
        var source = Path.Combine(SandboxEngine.RepoRoot(), "src", "AkmlSql.Web");
        var build = Path.Combine(source, "bin", "Debug", "net10.0");
        var dll = Path.Combine(build, "AkmlSql.Web.dll");
        var newestSource = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(p => Path.GetExtension(p) is ".cs" or ".razor")
            .Where(p => !Path.GetRelativePath(source, p).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"))
            .Max(File.GetLastWriteTimeUtc);
        if (!File.Exists(dll) || File.GetLastWriteTimeUtc(dll) < newestSource
            || !Directory.Exists(Path.Combine(build, "wwwroot", "_framework")))
            throw new InvalidOperationException("Stale Debug web bundle: run `dotnet build src/AkmlSql.Web -c Debug`.");

        Root = Directory.CreateTempSubdirectory("akml-e2e-bundle-").FullName;
        try
        {
            CopyTree(Path.Combine(source, "wwwroot"), Root);
            CopyTree(Path.Combine(build, "wwwroot", "_framework"), Path.Combine(Root, "_framework"));
            // Newer SDKs leave Debug boot modules in obj and expose them only through
            // static-asset endpoints. Materialize those framework URLs without editing bytes.
            using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(build, "AkmlSql.Web.staticwebassets.runtime.json")));
            var contentRoots = assets.RootElement.GetProperty("ContentRoots").EnumerateArray().Select(r => r.GetString()!).ToArray();
            foreach (var asset in assets.RootElement.GetProperty("Root").GetProperty("Children").GetProperty("_framework").GetProperty("Children").EnumerateObject())
            {
                var route = "_framework/" + asset.Name;
                if (Path.GetExtension(route) != ".js" || !asset.Value.TryGetProperty("Asset", out var file)
                    || file.ValueKind == JsonValueKind.Null) continue;
                var target = Path.GetFullPath(Path.Combine(Root, route.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Framework asset path escaped the bundle.");
                if (!File.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(Path.Combine(contentRoots[file.GetProperty("ContentRootIndex").GetInt32()], file.GetProperty("SubPath").GetString()!), target);
                }
            }
            // This static sandbox has no dotnet-watch provider. Answer the SDK's optional
            // availability check explicitly, without patching its boot scripts or metadata.
            if (Directory.EnumerateFiles(Path.Combine(Root, "_framework"), "*DotNetWatch*.js").Any())
            {
                var tools = Directory.CreateDirectory(Path.Combine(Root, "_framework", "dotnet-browser-tools"));
                File.WriteAllText(Path.Combine(tools.FullName, "hot-reload-settings.json"), "{\"hotReload\":false}");
            }
        }
        catch { Dispose(); throw; }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
