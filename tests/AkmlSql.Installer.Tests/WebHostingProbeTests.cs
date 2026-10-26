using Xunit;

namespace AkmlSql.Installer.Tests;

public sealed class WebHostingProbeTests
{
    internal static string[] Probe(params string[] expressions)
        => ProbeCode(false, expressions);

    internal static string[] ProbeThinCode(params string[] expressions)
        => ProbeCode(true, expressions);

    private static string[] ProbeCode(bool includeReaders, params string[] expressions)
    {
        const string iscc = @"C:\Program Files\Inno Setup 7\ISCC.exe";
        Assert.True(File.Exists(iscc), "Inno Setup 7 is required for the hosting probe.");
        var work = Path.Combine(Path.GetTempPath(), "akml-hosting-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var output = Path.Combine(work, "result.txt");
            var thinCode = "";
            if (includeReaders)
            {
                var installer = File.ReadAllText(HostingTestTools.Script("web-installer.iss"));
                thinCode = installer[(installer.IndexOf("[Code]", StringComparison.Ordinal) + "[Code]".Length)..];
            }
            File.WriteAllText(Path.Combine(work, "probe.iss"), $$"""
                [Setup]
                AppName=AkmlHostingProbe
                AppVersion=1
                DefaultDirName={tmp}\akmlhostingprobe
                OutputDir=.
                OutputBaseFilename=probe
                Uninstallable=no
                PrivilegesRequired=lowest
                CreateAppDir=no
                #include "{{HostingTestTools.Script("web-hosting.iss")}}"
                {{thinCode}}
                function B(V: Boolean): String;
                begin
                  if V then Result := 'True' else Result := 'False';
                end;
                function InitializeSetup(): Boolean;
                var R: TArrayOfString; Value, Reason: String; State: Integer;
                begin
                  SetArrayLength(R, {{expressions.Length}});
                {{string.Join("\r\n", expressions.Select((e, i) => $"  R[{i}] := {e};"))}}
                  SaveStringsToUTF8File('{{output}}', R, False);
                  Result := False;
                end;
                """);
            var compile = HostingTestTools.Run(iscc, "/Q", Path.Combine(work, "probe.iss"));
            Assert.True(compile.ExitCode == 0, compile.Output);
            var run = HostingTestTools.Run(Path.Combine(work, "probe.exe"), "/VERYSILENT", "/SUPPRESSMSGBOXES");
            Assert.True(File.Exists(output), run.Output);
            return File.ReadAllLines(output);
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Fact]
    public void Hosting_addresses_cover_every_host_and_exposure()
    {
        Assert.Equal(new[] { "http://localhost:59417/", "https://SQLBOX:59417/", "http://localhost/", "http://localhost:8083/", "" },
            Probe("HostingAddress('Service', 'Localhost', 'SQLBOX', 59417, 80)",
                "HostingAddress('Service', 'Lan', 'SQLBOX', 59417, 80)",
                "HostingAddress('IIS', 'Localhost', 'SQLBOX', 59417, 80)",
                "HostingAddress('IIS', 'Lan', 'SQLBOX', 59417, 8083)",
                "HostingAddress('None', 'Lan', 'SQLBOX', 59417, 80)"));
    }

    [Fact]
    public void Readiness_uses_first_failure_and_names_each_missing_part()
    {
        var rows = new[]
        {
            ("False, 10, 1, 0, 0, 0, 4, 4, 'failed'", "IisNotEnabled", "IIS is not enabled"),
            ("True, 6, 1, 1, 1, 1, 2, 3, 'ok'", "IisNotEnabled", "IIS is not enabled"),
            ("True, 10, 0, 1, 1, 1, 2, 3, 'ok'", "IisNotEnabled", "IIS is not enabled"),
            ("True, 10, 1, 0, 0, 0, 4, 4, 'failed'", "IisPartMissing", "Static Content"),
            ("True, 10, 1, 1, 0, 0, 2, 3, 'failed'", "IisPartMissing", "Default Document"),
            ("True, 10, 1, 1, 1, 0, 2, 3, 'failed'", "IisPartMissing", "HTTP Errors"),
            ("True, 10, 1, 1, 1, 1, 4, 3, 'failed'", "IisDisabled", "disabled"),
            ("True, 10, 1, 1, 1, 1, 2, 4, 'ok'", "IisDisabled", "disabled"),
            ("True, 10, 1, 1, 1, 1, 2, 3, 'failed'", "IisToolsUnavailable", "management tools"),
            ("True, 10, 1, 1, 1, 1, 2, 3, 'ok'", "IisReady", ""),
            ("True, 10, 1, 1, 1, 1, 2, 3, 'skipped'", "IisReady", ""),
        };
        Assert.All(Probe(rows.Select(row =>
            $"''; State := IisReadinessFrom({row.Item1}, Reason); R[{{INDEX}}] := B((State = {row.Item2}) and " +
            (row.Item3.Length == 0 ? "(Reason = ''))" : $"(Pos('{row.Item3}', Reason) > 0))")).Select((s, i) => s.Replace("{INDEX}", i.ToString())).ToArray()),
            value => Assert.Equal("True", value));
    }

    [Fact]
    public void Readiness_reasons_include_the_complete_recovery_instructions()
    {
        var reasons = new[]
        {
            ("False, 10, 1, 1, 1, 1, 2, 3, 'ok'", "IIS is not enabled on this computer. To use it, turn on Internet Information Services in Windows Features, then go Back and Next."),
            ("True, 10, 1, 0, 1, 1, 2, 3, 'ok'", "IIS is missing Static Content. Turn it on under Internet Information Services › World Wide Web Services › Common HTTP Features in Windows Features, then go Back and Next."),
            ("True, 10, 1, 1, 0, 1, 2, 3, 'ok'", "IIS is missing Default Document. Turn it on under Internet Information Services › World Wide Web Services › Common HTTP Features in Windows Features, then go Back and Next."),
            ("True, 10, 1, 1, 1, 0, 2, 3, 'ok'", "IIS is missing HTTP Errors. Turn it on under Internet Information Services › World Wide Web Services › Common HTTP Features in Windows Features, then go Back and Next."),
            ("True, 10, 1, 1, 1, 1, 4, 3, 'ok'", "The World Wide Web Publishing Service (or the Windows Process Activation Service it needs) is disabled. Set it to Manual or Automatic in Services, then go Back and Next."),
            ("True, 10, 1, 1, 1, 1, 2, 3, 'failed'", "Setup cannot use IIS's management tools. Turn on IIS Management Tools (under Internet Information Services) in Windows Features, then go Back and Next."),
        };
        Assert.All(Probe(reasons.Select((row, i) =>
            $"''; State := IisReadinessFrom({row.Item1}, Reason); R[{i}] := B(Reason = '" +
            row.Item2.Replace("'", "''").Replace("›", "' + #8250 + '") + "')").ToArray()),
            value => Assert.Equal("True", value));
    }

    [Fact]
    public void Previous_host_and_exposure_keep_deliberate_choices_and_infer_legacy_installs()
    {
        Assert.Equal(new[] { "None", "", "IIS", "Service", "Lan", "Lan", "Localhost", "Localhost" }, Probe(
            "InferPreviousHost('None', True, True, '<site name=" + '"' + "AkmlSqlWeb" + '"' + ">')",
            "InferPreviousHost('', False, False, '')",
            "InferPreviousHost('', True, False, '<site name=" + '"' + "AkmlSqlWeb" + '"' + ">')",
            "InferPreviousHost('', False, True, '')",
            "InferPreviousExposure('', '', 'Lan')",
            "InferPreviousExposure('', '{" + '"' + "bindAddress" + '"' + ":  " + '"' + "::" + '"' + "}', '')",
            "InferPreviousExposure('', '', '')",
            "InferPreviousExposure('Localhost', '', 'Lan')"));
    }

    [Theory]
    [InlineData("Localhost", "127.0.0.1")]
    [InlineData("Lan", "0.0.0.0")]
    public void Config_reader_understands_real_PS51_config_output(string mode, string expected)
    {
        var json = HostingTestTools.DotSource("web-config-bridge.ps1",
            $"Get-BridgeConfigJson -ExistingJson '' -Port 59417 -Mode {mode} -WebRoot ''");
        // The real writer uses Windows PowerShell's ConvertTo-Json spacing. Include its UTF-8 BOM.
        var literal = "#65279 + '" + json.Replace("'", "''").Replace("\r", "' + #13 + '").Replace("\n", "' + #10 + '") + "'";
        var results = Probe($"B(ReadConfigString({literal}, 'bindAddress', Value)); R[0] := R[0] + '|' + Value",
            $"B(ReadConfigString({literal}, 'missing', Value))",
            $"InferPreviousExposure('', {literal}, '')");
        Assert.Equal(new[] { "True|" + expected, "False", mode }, results);
    }
}
