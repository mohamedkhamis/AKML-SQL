using System.Diagnostics;
using AkmlSql.Engine.Transports;
using Xunit;

namespace AkmlSql.Engine.Tests.Transports;

public sealed class StaticWebResponderCspTests
{
    [Fact]
    public void Policy_equals_only_the_two_AST_extracted_IIS_assignments()
    {
        var path = Path.Combine(TestPaths.RepoRoot, "src", "AkmlSql.Installer", "web-iis-setup.ps1").Replace("'", "''");
        var command = "$ErrorActionPreference='Stop'; $tokens=$null; $errors=$null; " +
            $"$ast=[System.Management.Automation.Language.Parser]::ParseFile('{path}',[ref]$tokens,[ref]$errors); " +
            "if($errors.Count) {throw $errors[0]}; " +
            "$assignments=@($ast.FindAll({param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] " +
            "-and $n.Left -is [System.Management.Automation.Language.VariableExpressionAst] " +
            "-and $n.Left.VariablePath.UserPath -in @('bridgeConnectSrc','csp')},$true)); " +
            "if($assignments.Count -ne 2) {throw 'Expected exactly two CSP assignments'}; " +
            "$assignments | ForEach-Object {Invoke-Expression $_.Extent.Text}; Write-Output $csp";
        var info = new ProcessStartInfo("powershell.exe")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", command }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, errors);
        Assert.Equal(StaticWebResponder.ContentSecurityPolicy, output.Trim());
    }

    [Fact]
    public void Every_composed_Debug_and_existing_Release_bundle_extension_has_a_contract_mapping()
    {
        var allowed = new[] { ".html", ".js", ".css", ".wasm", ".json", ".map", ".svg", ".txt", ".sql", ".pdb" };
        var web = Path.Combine(TestPaths.RepoRoot, "src", "AkmlSql.Web");
        var roots = new[] { Path.Combine(web, "wwwroot"), Path.Combine(web, "bin", "Debug", "net10.0", "wwwroot", "_framework"),
            Path.Combine(web, "bin", "Release", "net10.0", "publish", "wwwroot") };
        Assert.True(Directory.Exists(roots[1]), "Build AkmlSql.Web Debug before the extension coverage check.");
        foreach (var root in roots.Where(Directory.Exists))
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension is ".br" or ".gz" or ".config") continue;
            Assert.True(allowed.Contains(extension), "Unmapped bundle extension: " + file);
        }
    }
}
