using System;
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 047 (ENG-001, <c>pairing.manage.v1</c>) — Web → Engine: the browsers paired with this engine. Served only on the
    /// web bridge, to an authenticated connection. Nothing is ever removed automatically; only <see cref="PairingRevokeRequest"/>
    /// removes a pairing.
    /// </summary>
    [MessagePackObject]
    public class PairingListRequest
    {
    }

    /// <summary>Engine → Web: the result of <see cref="PairingListRequest"/>.</summary>
    [MessagePackObject]
    public class PairingListResponse
    {
        /// <summary>False when the engine does not require pairing (a loopback-only engine keeps no pairings).</summary>
        [Key(0)]
        public bool PairingUsed { get; set; }

        /// <summary>Every stored pairing, newest first.</summary>
        [Key(1)]
        public PairingEntry[] Entries { get; set; } = Array.Empty<PairingEntry>();
    }

    /// <summary>One paired browser. Never carries the bearer token itself.</summary>
    [MessagePackObject]
    public class PairingEntry
    {
        /// <summary>The SHA-256 hash (hex) of the pairing's token: the only identifier the engine exposes.</summary>
        [Key(0)]
        public string Id { get; set; } = string.Empty;

        /// <summary>The label the browser sent when it paired ("Edge on Windows · http://localhost:59416"; older pairings "Web edition").</summary>
        [Key(1)]
        public string Label { get; set; } = string.Empty;

        [Key(2)]
        public DateTimeOffset MintedAt { get; set; }

        [Key(3)]
        public DateTimeOffset LastUsedAt { get; set; }

        /// <summary>When the pairing stops being accepted. A past date is shown as expired; it is kept until revoked.</summary>
        [Key(4)]
        public DateTimeOffset ExpiresAt { get; set; }

        /// <summary>True for the pairing the asking connection authenticated with ("This browser").</summary>
        [Key(5)]
        public bool IsCurrent { get; set; }
    }

    /// <summary>Web → Engine: remove one pairing and close the connections that authenticated with it.</summary>
    [MessagePackObject]
    public class PairingRevokeRequest
    {
        /// <summary><see cref="PairingEntry.Id"/> of the pairing to remove.</summary>
        [Key(0)]
        public string Id { get; set; } = string.Empty;
    }

    /// <summary>Engine → Web: the result of <see cref="PairingRevokeRequest"/>.</summary>
    [MessagePackObject]
    public class PairingRevokeResponse
    {
        [Key(0)]
        public bool Ok { get; set; }

        /// <summary>Why nothing was revoked ("That pairing no longer exists."); null on success.</summary>
        [Key(1)]
        public string? Message { get; set; }

        /// <summary>How many open bridge connections the revoke closed.</summary>
        [Key(2)]
        public int ClosedConnections { get; set; }
    }
}
