using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;

// Spec 041 (T004, research R68) — the shared web-test doubles, moved out of
// Bridge/SchemaTreeComponentTests.cs so every suite can use them. They keep their original namespace
// so existing tests compile unchanged. New fakes go in their own file in this folder.
namespace AkmlSql.Web.Tests.Bridge;

internal sealed class FakeEngineBridge : IEngineBridge
{
    public BridgeState State { get; private set; } = BridgeState.Disconnected;
    public string[] EngineCapabilities { get; set; } = Array.Empty<string>();
    public string? EngineVersion { get; set; }
    public string? EngineHostKind { get; set; }
    public string? EngineRunsAs { get; set; }

    public event Action<BridgeState>? StateChanged;
    public event Action<DateTimeOffset?>? RetryScheduled;
    public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected;

    public void SetState(BridgeState newState)
    {
        State = newState;
        StateChanged?.Invoke(newState);
    }

    /// <summary>Every handshake asked for, in order: the address dialled and what it carried.</summary>
    public List<(string Host, int Port, string? BearerToken, string? PairingPin)> Handshakes { get; } = new();

    /// <summary>The engine's answer to a handshake; defaults to Ok.</summary>
    public Func<string? /*bearer*/, string? /*pin*/, HandshakeResponse> Answer { get; set; } =
        (_, _) => new HandshakeResponse { Status = HandshakeStatus.Ok };

    public Task<HandshakeResponse> ConnectAsync(EngineConnection connection, string? bearerToken, string? pairingPin, CancellationToken ct)
    {
        Handshakes.Add((connection.Host, connection.Port, bearerToken, pairingPin));
        return Task.FromResult(Answer(bearerToken, pairingPin));
    }

    /// <summary>The engine's answer to a SQL Server connection test, which every SQL Server connect now starts with.</summary>
    public TestSqlConnectionResponse TestSqlReply { get; set; } = new() { Ok = true };

    public Task<TResponse> SendAsync<TRequest, TResponse>(int requestMessageType, TRequest request, CancellationToken ct)
        where TRequest : class
        where TResponse : class =>
        Task.FromResult(requestMessageType == AkmlSql.Core.Ipc.MessageTypes.TestSqlConnection ? (TResponse)(object)TestSqlReply : default!);

    /// <summary>Spec 041 (T184) — every notification sent, in order.</summary>
    public List<(int Type, object Payload)> Notifications { get; } = new();

    public Task SendNotificationAsync<TPayload>(int messageType, TPayload payload, CancellationToken ct)
        where TPayload : class
    {
        Notifications.Add((messageType, payload));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => default;

    /// <summary>Test helper for the spec-025 follow-on TLS-banner tests — fires the
    /// production-event shape so the banner subscriber wakes up exactly like it would
    /// from a real bridge mismatch detection.</summary>
    public void FireMismatch(TlsFingerprintMismatch m) => FingerprintMismatchDetected?.Invoke(m);

    // Suppress unused-event-warning while keeping the contract.
    private void _suppressUnused() { RetryScheduled?.Invoke(null); }
}

internal sealed class FakeSchemaSync : ISchemaSync
{
    public event Action<ChecksumDriftNotice>? ChecksumDrifted;

    public Task StartAsync(string sessionId, string serverCanonicalIdentity, string databaseName) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public int ActiveReports { get; private set; }
    public void ReportEditorActive() => ActiveReports++;
    public ValueTask DisposeAsync() => default;

    /// <summary>Spec 041 (T184) — refreshes asked for; <see cref="OnRefresh"/> runs inside each (a gate, a probe).</summary>
    public int Refreshes { get; private set; }
    public Func<Task>? OnRefresh { get; set; }

    public bool IsRefreshing { get; private set; }
    public event Action? RefreshingChanged;

    public void SetRefreshing(bool refreshing)
    {
        IsRefreshing = refreshing;
        RefreshingChanged?.Invoke();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        Refreshes++;
        if (OnRefresh != null) await OnRefresh();
    }

    public Task RaiseChecksumDriftedAsync(ChecksumDriftNotice notice)
    {
        ChecksumDrifted?.Invoke(notice);
        return Task.CompletedTask;
    }
}
