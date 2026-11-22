using System;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using MessagePack;

namespace AkmlSql.Web.Services;

/// <summary>Spec 047 (WEB-011): what the active tab's cached schema can give the Expand refactorings.</summary>
public enum RefactorSchemaState
{
    /// <summary>No connection, or no snapshot of the connected database.</summary>
    None,

    /// <summary>Object names only (Phase A): the column list is still loading.</summary>
    StillLoading,

    /// <summary>Columns and parameters are cached (Phase B).</summary>
    Ready,
}

/// <summary>
/// Spec 047 (WEB-012): one Refactor menu entry. <paramref name="Id"/> is unique in the menu (it names the button and its
/// reason); a disabled entry says why in <paramref name="Reason"/>; <paramref name="Section"/> starts a group above it.
/// </summary>
public sealed record RefactorMenuItem(string Id, string Label, bool Enabled, string? Reason, string? Section = null);

/// <summary>Spec 047 (WEB-011): the state, and the cache the Expand refactorings run against when it is ready.</summary>
public sealed record RefactorSchemaSnapshot(RefactorSchemaState State, DatabaseCache? Cache)
{
    public static RefactorSchemaSnapshot None { get; } = new(RefactorSchemaState.None, null);
}

/// <summary>
/// Spec 047 (WEB-011, WEB-012; contracts/workspace-ai.md §3) — the schema the browser's Expand refactorings use, and the
/// reasons the Refactor menu gives for an item it cannot run. The cache is the connected database's snapshot rehydrated
/// with <see cref="SchemaPhaseRehydrator"/> (the converter the AI schema context already uses, data-model §11).
/// </summary>
public static class RefactorSchema
{
    public const string StillLoadingReason = "Still loading the column list.";
    public const string NeedsSchemaReason = "Needs the database schema. Connect to a database first.";
    public const string HeavyNotInBrowserReason = "Not available in the browser yet.";
    public const string HeavyNeedsEngineReason = "Needs a paired engine.";

    /// <summary>The three refactorings that read columns or parameters from the schema.</summary>
    public static bool NeedsSchema(LightweightRefactorKind kind) => kind is
        LightweightRefactorKind.ExpandInsertColumns or LightweightRefactorKind.ExpandUpdateColumns or LightweightRefactorKind.ExpandExecParameters;

    /// <summary>The state of a cached snapshot, with the rehydrated cache when it holds columns.</summary>
    public static RefactorSchemaSnapshot From(SchemaSnapshot? snapshot)
    {
        if (snapshot == null) return RefactorSchemaSnapshot.None;
        var phaseA = Read(snapshot.PhaseA);
        var phaseB = Read(snapshot.PhaseB);
        if (phaseB != null)
            return new(RefactorSchemaState.Ready, SchemaPhaseRehydrator.Rehydrate(snapshot.CompositeKey, phaseA, phaseB));
        return phaseA != null ? new(RefactorSchemaState.StillLoading, null) : RefactorSchemaSnapshot.None;
    }

    /// <summary>The connected database's snapshot (never another one's), or <see cref="RefactorSchemaSnapshot.None"/>.</summary>
    public static async Task<RefactorSchemaSnapshot> LoadAsync(ISchemaCacheStore store, ISqlConnectionService connection)
    {
        if (store == null || connection is not { IsConnected: true }) return RefactorSchemaSnapshot.None;
        // Read before the first await (spec 043: the facade follows the active tab).
        var server = connection.SchemaIdentity ?? connection.Server;
        var database = connection.Database;
        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(database)) return RefactorSchemaSnapshot.None;
        return From(await store.GetAsync(server!, database!).ConfigureAwait(false));
    }

    /// <summary>Why an Expand item is disabled, or null when it can run.</summary>
    public static string? ExpandReason(RefactorSchemaState state) => state switch
    {
        RefactorSchemaState.Ready => null,
        RefactorSchemaState.StillLoading => StillLoadingReason,
        _ => NeedsSchemaReason,
    };

    /// <summary>Why a schema-aware (engine) item is disabled, or null when it can run.</summary>
    public static string? HeavyReason(bool heavyAvailable, bool engineOpen) =>
        heavyAvailable ? null : engineOpen ? HeavyNotInBrowserReason : HeavyNeedsEngineReason;

    private static SchemaPhasePayload? Read(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        try { return MessagePackSerializer.Deserialize<SchemaPhasePayload>(bytes); }
        catch (MessagePackSerializationException) { return null; }
    }
}
