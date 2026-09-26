using System;
using AkmlSql.Core.Logging;
using Xunit;

namespace AkmlSql.Core.Tests.Logging;

/// <summary>
/// Error reports are on by default and promise "no personal data, machine name or IP". The
/// envelope keeps that promise by construction; these pin that the log TEXT inside it does too,
/// using the shapes AKML SQL's own error messages actually take.
/// </summary>
public sealed class TelemetryScrubberTests
{
    [Theory]
    [InlineData(@"Failed to import SQL Prompt style: C:\Users\mkhamis\AppData\Local\Red Gate\x.sqlpromptstylev2",
                @"Failed to import SQL Prompt style: C:\Users\<user>\AppData\Local\Red Gate\x.sqlpromptstylev2")]
    [InlineData(@"at Foo() in C:\Users\Jane Doe\source\repos\x.cs:line 12",
                @"at Foo() in C:\Users\<user>\source\repos\x.cs:line 12")]
    [InlineData("D:/Users/someone/file.txt", "D:/Users/<user>/file.txt")]
    public void ProfilePaths_LoseTheUserName(string input, string expected)
    {
        Assert.Equal(expected, TelemetryScrubber.Scrub(input));
    }

    [Fact]
    public void TheEnginesConnectionDescription_LosesServerAndDatabase()
    {
        var scrubbed = TelemetryScrubber.Scrub(
            "Connection changed: session=abc db=Sales -- server='PROD-SQL01\\INST' catalog='Payroll' auth='SQL' timeout=15s");

        Assert.Contains("server='<redacted>' catalog='<redacted>'", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("PROD-SQL01", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("Payroll", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("Sales", scrubbed, StringComparison.Ordinal); // db=Sales
        Assert.Contains("auth='SQL'", scrubbed, StringComparison.Ordinal); // not identifying, and useful
    }

    [Fact]
    public void AConnectionString_LosesEveryIdentifyingValue()
    {
        var scrubbed = TelemetryScrubber.Scrub(
            "Bad string: Server=db.corp.example;Database=HR;User ID=svc_hr;Password=Hunter2!;TrustServerCertificate=true");

        Assert.DoesNotContain("db.corp.example", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("HR;", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("svc_hr", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("Hunter2", scrubbed, StringComparison.Ordinal);
        Assert.Contains("TrustServerCertificate=true", scrubbed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Login failed for user 'CORP\\jdoe'.", "Login failed for user '<redacted>'.")]
    [InlineData("Cannot open database 'Payroll' requested by the login.", "Cannot open database '<redacted>' requested by the login.")]
    public void SqlServerQuotedNames_AreRedacted(string input, string expected)
    {
        Assert.Equal(expected, TelemetryScrubber.Scrub(input));
    }

    [Theory]
    [InlineData("connect to 10.0.0.5 failed", "connect to <ip> failed")]
    [InlineData("wss://162.220.54.191:59417/akmlsql", "wss://<ip>:59417/akmlsql")]
    [InlineData("mail jane.doe@example.com now", "mail <email> now")]
    [InlineData(@"open \\fileserver\share\x.sql", @"open \\<host>\share\x.sql")]
    public void AddressesAndEmails_AreRedacted(string input, string expected)
    {
        Assert.Equal(expected, TelemetryScrubber.Scrub(input));
    }

    [Theory]
    [InlineData("AKML SQL 1.26.0923.0723 logger initialized")]
    [InlineData("SqlException 18456, state 8, line 1")]
    [InlineData("System.InvalidOperationException: Sequence contains no elements")]
    [InlineData("at AkmlSql.Engine.Execution.ExecuteQueryHandler.HandleAsync()")]
    public void DiagnosticDetail_Survives(string input)
    {
        Assert.Equal(input, TelemetryScrubber.Scrub(input));
    }

    [Fact]
    public void ThisMachineAndAccountName_AreRedactedAsWholeWords()
    {
        var machine = Environment.MachineName;
        var scrubbed = TelemetryScrubber.Scrub($"host {machine} reported by {machine}.");

        if (machine.Length >= 3)
        {
            Assert.DoesNotContain(machine, scrubbed, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<machine>", scrubbed, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NothingIn_NothingOut(string? input)
    {
        Assert.Equal(input, TelemetryScrubber.Scrub(input));
    }
}
