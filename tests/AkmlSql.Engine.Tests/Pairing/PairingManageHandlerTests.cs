using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Pairing;
using AkmlSql.Engine.Pairing;
using Xunit;

namespace AkmlSql.Engine.Tests.Pairing;

/// <summary>
/// Spec 047 (ENG-001, contracts/engine-pairing.md §5): the paired browsers an engine keeps can be listed and revoked one
/// by one from a paired browser. Nothing is ever pruned automatically (clarification Q5).
/// </summary>
public sealed class PairingManageHandlerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _path;
    private DateTimeOffset _now = new(2026, 11, 21, 9, 0, 0, TimeSpan.Zero);

    public PairingManageHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"akml_pairmgr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _path = Path.Combine(_tempDir, "tokens.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    private BearerTokenStore NewStore() => new(_path, TimeSpan.FromDays(90), () => _now);

    [Fact]
    public async Task An_engine_that_needs_no_pairing_reports_none_in_use()
    {
        var handler = new PairingListHandler(() => null);
        using (BridgeSourceIp.Set(IPAddress.Loopback))
        using (BridgeConnection.Set("socket-1"))
        {
            var response = await handler.HandleAsync(new PairingListRequest(), null!, CancellationToken.None);
            Assert.False(response.PairingUsed);
            Assert.Empty(response.Entries);
        }
    }

    [Fact]
    public async Task Pairings_are_listed_newest_first_and_the_callers_own_is_marked()
    {
        var store = NewStore();
        var older = store.Mint("Edge on Windows · http://localhost:59416");
        _now = _now.AddMinutes(5);
        var newer = store.Mint("Firefox on Linux · https://sql-host");
        var handler = new PairingListHandler(() => store);

        using (BridgeSourceIp.Set(IPAddress.Parse("10.0.0.5")))
        using (BridgeConnection.Set("socket-2"))
        {
            BridgeConnection.SetTokenHash(BearerTokenStore.HashToken(older));
            var response = await handler.HandleAsync(new PairingListRequest(), null!, CancellationToken.None);

            Assert.True(response.PairingUsed);
            Assert.Equal(new[] { "Firefox on Linux · https://sql-host", "Edge on Windows · http://localhost:59416" },
                response.Entries.Select(e => e.Label).ToArray());
            Assert.Equal(new[] { false, true }, response.Entries.Select(e => e.IsCurrent).ToArray());
            var current = response.Entries[1];
            Assert.Equal(BearerTokenStore.HashToken(older), current.Id);
            Assert.Equal(new DateTimeOffset(2026, 11, 21, 9, 0, 0, TimeSpan.Zero), current.MintedAt);
            Assert.Equal(current.MintedAt.AddDays(90), current.ExpiresAt);
            Assert.DoesNotContain(response.Entries, e => e.Id == newer || e.Id == older);   // never the raw token
        }
    }

    [Fact]
    public async Task Revoke_removes_the_pairing_and_closes_its_connections()
    {
        var store = NewStore();
        var keep = store.Mint("Edge on Windows · http://localhost:59416");
        var drop = store.Mint("Chrome on Windows · http://127.0.0.1:59416");
        string? closedFor = null;
        var handler = new PairingRevokeHandler(() => store, hash => { closedFor = hash; return 2; });

        using (BridgeSourceIp.Set(IPAddress.Parse("10.0.0.5")))
        using (BridgeConnection.Set("socket-3"))
        {
            BridgeConnection.SetTokenHash(BearerTokenStore.HashToken(keep));
            var response = await handler.HandleAsync(
                new PairingRevokeRequest { Id = BearerTokenStore.HashToken(drop) }, null!, CancellationToken.None);

            Assert.True(response.Ok, response.Message);
            Assert.Equal(2, response.ClosedConnections);
            Assert.Equal(BearerTokenStore.HashToken(drop), closedFor);
            Assert.False(store.Validate(drop));
            Assert.True(store.Validate(keep));
            Assert.Single(store.List());
        }
    }

    [Fact]
    public async Task Revoking_an_unknown_pairing_says_so()
    {
        var store = NewStore();
        store.Mint("Edge on Windows · http://localhost:59416");
        var handler = new PairingRevokeHandler(() => store, _ => throw new InvalidOperationException("must not close anything"));

        using (BridgeSourceIp.Set(IPAddress.Loopback))
        using (BridgeConnection.Set("socket-4"))
        {
            var response = await handler.HandleAsync(new PairingRevokeRequest { Id = new string('0', 64) }, null!, CancellationToken.None);
            Assert.False(response.Ok);
            Assert.Equal("That pairing no longer exists.", response.Message);
            Assert.Single(store.List());
        }
    }

    [Fact]
    public async Task A_request_from_the_named_pipe_is_refused()
    {
        var store = NewStore();
        store.Mint("Edge on Windows · http://localhost:59416");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PairingListHandler(() => store).HandleAsync(new PairingListRequest(), null!, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PairingRevokeHandler(() => store).HandleAsync(
                new PairingRevokeRequest { Id = store.List()[0].TokenHash }, null!, CancellationToken.None));
        Assert.Single(store.List());
    }

    [Fact]
    public async Task Connections_that_authenticated_with_a_revoked_pairing_are_closed()
    {
        var store = NewStore();
        var token = store.Mint("Edge on Windows · http://localhost:59416");
        var hash = BearerTokenStore.HashToken(token);
        var closed = 0;

        // Two live connections authenticated with the same pairing (one page, two windows), one with another.
        using var a = BridgeConnection.Set("socket-a");
        BridgeConnection.SetCloser(() => { Interlocked.Increment(ref closed); return Task.CompletedTask; });
        BridgeConnection.SetTokenHash(hash);
        using (BridgeConnection.Set("socket-b"))
        {
            BridgeConnection.SetCloser(() => { Interlocked.Increment(ref closed); return Task.CompletedTask; });
            BridgeConnection.SetTokenHash(hash);
            using (BridgeConnection.Set("socket-c"))
            {
                BridgeConnection.SetCloser(() => throw new InvalidOperationException("another pairing must stay open"));
                BridgeConnection.SetTokenHash(new string('1', 64));

                Assert.Equal(2, BridgeConnection.CloseConnectionsWithToken(hash));
            }
        }
        Assert.Equal(2, closed);
    }
}
