using System;
using System.Collections.Generic;

namespace AkmlSql.Web.Services;

/// <summary>What stopped the browser from reaching an engine.</summary>
public enum EngineConnectProblem
{
    /// <summary>This page's own Content-Security-Policy refused the address; nothing left the computer.</summary>
    BlockedByPagePolicy,

    /// <summary>The WebSocket did not open: nothing answered, the port is closed, or the certificate was refused.</summary>
    NotReachable,
}

/// <summary>
/// Turns a failed engine connect into something a person can act on.
///
/// <para>
/// The browser's WebSocket API reports every failure the same way -- one bare error, no reason --
/// whether nothing is listening, a firewall dropped the packets, or the engine's self-signed
/// certificate was refused. So the explanation cannot be exact; it can be ORDERED: the single most
/// likely cause first, with a one-click test (open the address, accept the certificate) that also
/// fixes it. The one failure the page CAN name precisely is its own security policy refusing the
/// address, which akml-bridge.js reports by that name.
/// </para>
/// </summary>
public sealed record EngineConnectDiagnosis(
    EngineConnectProblem Problem,
    string Summary,
    IReadOnlyList<string> Steps,
    string? TrustUrl,
    string RawError)
{
    /// <summary>The phrase akml-bridge.js puts in a policy refusal (blockedByPolicyMessage).</summary>
    internal const string PolicyMarker = "Content-Security-Policy";

    /// <summary>Diagnoses a failed connect to <paramref name="host"/>:<paramref name="port"/>.</summary>
    public static EngineConnectDiagnosis For(string host, int port, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var raw = FirstLine(error.Message);
        var address = $"{Bracket(host)}:{port}";

        if (raw.Contains(PolicyMarker, StringComparison.Ordinal))
        {
            return new EngineConnectDiagnosis(
                EngineConnectProblem.BlockedByPagePolicy,
                $"This copy of AKML SQL Web is only allowed to connect to engines on this computer, so the browser blocked {address} before it left the machine.",
                [
                    "Update AKML SQL Web on this computer to the latest version (the installer allows remote engines from any install).",
                    "Or, after updating, run \"Repair AKML SQL Web hosting\" from the Start menu, then reload this page.",
                    "If you only need a SQL Server on that machine, you don't need a remote engine: use Connect to SQL Server with the server's address and SQL Server authentication.",
                ],
                TrustUrl: null,
                raw);
        }

        if (EngineEndpoint.IsLoopbackHost(host))
        {
            return new EngineConnectDiagnosis(
                EngineConnectProblem.NotReachable,
                $"Nothing answered at {address} on this computer.",
                [
                    "Check that the AkmlSqlWebEngine service is running (services.msc, or: sc start AkmlSqlWebEngine).",
                    $"Check the port. The engine's port is the \"Bridge port\" line in C:\\ProgramData\\AKML SQL Web\\INSTALL-SUMMARY.txt; the default is {EngineEndpoint.DefaultPort}, but the installer can choose another.",
                ],
                TrustUrl: null,
                raw);
        }

        var trustUrl = $"https://{address}{EngineEndpoint.Path}";
        return new EngineConnectDiagnosis(
            EngineConnectProblem.NotReachable,
            $"Couldn't open a secure connection to the engine at {address}.",
            [
                $"Open {trustUrl} in a new tab. If the browser warns about the certificate, choose Advanced, then continue to the site. You should see \"AKML SQL engine: reachable\". Come back here and pair again.",
                "If that page does not load, check the port: it is the \"Bridge port\" line in C:\\ProgramData\\AKML SQL Web\\INSTALL-SUMMARY.txt on the engine computer. The default is " +
                    $"{EngineEndpoint.DefaultPort}, but the installer can choose another.",
                "Check that the engine computer has AKML SQL Web installed in network (LAN) mode with the AkmlSqlWebEngine service running, " +
                    "and that the port is open in its Windows Firewall and in any cloud or network firewall in between.",
                "The PIN is in C:\\ProgramData\\AKML SQL Web\\pairing-pin.txt on the engine computer.",
            ],
            trustUrl,
            raw);
    }

    /// <summary>
    /// The error's first line. A JavaScript error arrives with its stack trace appended
    /// ("at ws.onerror (http://localhost/js/akml-bridge.js:56:28)"), which means nothing to a user.
    /// </summary>
    internal static string FirstLine(string message)
    {
        var text = (message ?? string.Empty).Trim();
        var end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text : text[..end].TrimEnd();
    }

    private static string Bracket(string host) =>
        host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host;
}
