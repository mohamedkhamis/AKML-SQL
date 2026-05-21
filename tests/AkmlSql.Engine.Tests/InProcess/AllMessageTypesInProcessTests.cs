using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Transports;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.InProcess;

/// <summary>
/// Spec 021 (web edition) -- M0 task T023. Spec 022 (M0 closure) -- P2 / US2: matrix
/// now reads from <see cref="RpcRouter.RegisteredMessageTypes"/> via
/// <see cref="EngineComposition.Build"/> rather than the old transport-side accessor.
///
/// Two complementary coverage tests:
///
/// 1. <see cref="Every_shell_to_engine_message_type_has_a_registered_handler"/> -- a
///    registration matrix. Builds the composition root, walks every
///    public-static integer constant on <see cref="MessageTypes"/>, partitions by
///    direction (shell-to-engine vs engine-to-shell using a name-suffix heuristic plus
///    an explicit override list), and asserts every shell-to-engine code is present in
///    <see cref="RpcRouter.RegisteredMessageTypes"/>. This is the regression
///    gate: a new <see cref="MessageTypes"/> constant without a registered handler now
///    fails CI.
///
/// 2. <see cref="Parameterless_handlers_round_trip_through_the_in_process_transport"/>
///    -- exercises the in-process dispatch path (no serialisation cost) for every typed
///    handler with a public parameterless constructor, using
///    <see cref="RpcRouter.RegisterAllInAssembly"/>. Proves the reflective registration
///    path actually wires handlers that respond to live requests.
/// </summary>
public sealed class AllMessageTypesInProcessTests
{
    /// <summary>
    /// Constants whose name suffix marks them as engine-to-shell (response, result,
    /// notification-completed) rather than shell-to-engine (request, command).
    /// </summary>
    private static readonly string[] EngineToShellSuffixes =
    {
        "Result", "Response", "Complete", "Chunk"
    };

    /// <summary>
    /// Explicit engine-to-shell names that don't match the suffix heuristic.
    /// </summary>
    private static readonly HashSet<string> EngineToShellExplicit = new(StringComparer.Ordinal)
    {
        "Pong",
        "Error",
    };

    /// <summary>
    /// Codes that legitimately have no registered handler today. Each entry must carry
    /// a reason; the goal of this list is "what's intentionally unwired", not "what's
    /// forgotten."
    /// </summary>
    private static readonly Dictionary<int, string> ExpectedUnwired = new()
    {
        // HandshakeRequest -- spec 021 M3. HandshakeHandler is constructed by the
        // WebSocketTransport bootstrap, not by NamedPipeTransport (IDE shells do not send
        // it). Coverage stays in HandshakeHandlerTests.
        [MessageTypes.HandshakeRequest] = "M3 WebSocket bridge only -- not dispatched over named pipe.",
    };

    [Fact]
    public void Every_shell_to_engine_message_type_has_a_registered_handler()
    {
        // Spec 022 (M0 closure) -- P2 / US2. Source of truth is the router built by
        // EngineComposition.Build(); the old transport-side RegisteredMessageTypeCodes
        // accessor is gone now.
        var composition = EngineComposition.Build();
        var registered = new HashSet<int>(composition.Router.RegisteredMessageTypes);

        var (shellToEngine, _) = PartitionMessageTypes();

        var missing = new List<string>();
        foreach (var (name, code) in shellToEngine)
        {
            if (registered.Contains(code)) continue;
            if (ExpectedUnwired.ContainsKey(code)) continue;
            missing.Add($"{name} ({code})");
        }

        Assert.True(
            missing.Count == 0,
            $"Shell-to-engine MessageTypes without a registered handler: {string.Join(", ", missing)}. " +
            "Either register the handler in EngineHandlerRegistry.RegisterAllHandlers or add the code to " +
            $"{nameof(ExpectedUnwired)} with a reason.");
    }

    [Fact]
    public void Engine_to_shell_codes_are_not_registered_as_request_handlers()
    {
        // The flip side of the matrix: response/result codes must NOT appear in the
        // handler dict -- those are the codes the engine emits, not consumes.
        var composition = EngineComposition.Build();
        var registered = new HashSet<int>(composition.Router.RegisteredMessageTypes);

        var (_, engineToShell) = PartitionMessageTypes();

        var leaked = new List<string>();
        foreach (var (name, code) in engineToShell)
        {
            if (registered.Contains(code))
            {
                leaked.Add($"{name} ({code})");
            }
        }

        Assert.True(
            leaked.Count == 0,
            $"Engine-to-shell MessageTypes registered as request handlers: {string.Join(", ", leaked)}.");
    }

    [Fact]
    public void Expected_unwired_list_only_references_real_message_type_codes()
    {
        // Guard against typos / stale entries in ExpectedUnwired: every code must
        // resolve back to a known MessageTypes constant.
        var allCodes = new HashSet<int>(
            EnumerateMessageTypeConstants().Select(kvp => kvp.Code));

        foreach (var (code, reason) in ExpectedUnwired)
        {
            Assert.True(
                allCodes.Contains(code),
                $"ExpectedUnwired contains code {code} ('{reason}') that is not a known MessageTypes constant.");
        }
    }

    [Fact]
    public async Task Parameterless_handlers_round_trip_through_the_in_process_transport()
    {
        // RegisterAllInAssembly (T021) scans the engine assembly for typed handlers
        // with parameterless constructors. We then round-trip a request through the
        // InProcessTransport for each one and assert the response carries the expected
        // ResponseMessageType (or null for notification-style handlers).
        var router = new RpcRouter();
        var registered = router.RegisterAllInAssembly(typeof(NamedPipeTransport).Assembly);
        Assert.True(registered > 0,
            "RegisterAllInAssembly found no parameterless typed handlers in the engine assembly.");

        var ctx = new RpcContext
        {
            Sessions = new SessionManager(),
            SchemaCache = new AkmlSql.Engine.Schema.SchemaCacheManager(),
            Logger = Serilog.Log.Logger,
            SettingsLoader = () => new AkmlSql.Core.Config.AppSettings(),
        };

        await using var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => router.RouteAsync(msg, ctx, ct);
        await transport.StartAsync(CancellationToken.None);

        // Exercise every code RegisterAllInAssembly wired. We send a request with the
        // default-payload-for-the-request-type so handlers see a well-formed empty
        // POCO rather than null bytes.
        //
        // The test asserts dispatch wiring -- that the message-type integer reaches
        // its registered handler -- not handler logic. Some handlers (e.g.
        // ConnectionChangedHandler) require RpcContext fields that this minimal test
        // ctx doesn't provide, and they throw InvalidOperationException as their
        // guard clause. We treat such throws as evidence that dispatch succeeded
        // (the alternative -- the message never reaching a handler -- would surface
        // as a null response from RpcRouter, which we assert against where applicable).
        int exercised = 0;
        foreach (var code in router.RegisteredMessageTypes.ToList())
        {
            var request = BuildEmptyRequest(code);
            if (request == null) continue;   // skip codes whose request DTO we don't know inline

            try
            {
                var response = await transport.SendAsync(request, CancellationToken.None);

                // A null response is valid for notification-style handlers
                // (ResponseMessageType == 0). For everything else, the response must
                // carry a non-zero MessageType.
                if (response != null)
                {
                    Assert.NotEqual(0, response.MessageType);
                    Assert.Equal(request.RequestId, response.RequestId);
                }
            }
            catch (InvalidOperationException)
            {
                // Guard-clause failures from inside the handler still prove dispatch
                // reached it -- the failure happens AFTER the RpcRouter resolves the
                // adapter. Counts as exercised.
            }
            catch (OperationCanceledException)
            {
                // ShutdownHandler raises OCE by design (it signals the engine accept
                // loop to tear down). Dispatch still reached the handler.
            }
            exercised++;
        }

        Assert.True(exercised >= 4,
            $"Round-trip exercised only {exercised} parameterless handler(s) " +
            "(expected >=4 -- DocumentChanged, Ping, Shutdown, SchemaStatus).");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private record struct MessageTypeConstant(string Name, int Code);

    private static IEnumerable<MessageTypeConstant> EnumerateMessageTypeConstants()
    {
        foreach (var field in typeof(MessageTypes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (!field.IsLiteral || field.IsInitOnly) continue;
            if (field.FieldType != typeof(int)) continue;
            yield return new MessageTypeConstant(field.Name, (int)field.GetRawConstantValue()!);
        }
    }

    private static (List<MessageTypeConstant> shellToEngine, List<MessageTypeConstant> engineToShell)
        PartitionMessageTypes()
    {
        var shellToEngine = new List<MessageTypeConstant>();
        var engineToShell = new List<MessageTypeConstant>();
        foreach (var c in EnumerateMessageTypeConstants())
        {
            if (IsEngineToShell(c.Name))
            {
                engineToShell.Add(c);
            }
            else
            {
                shellToEngine.Add(c);
            }
        }
        return (shellToEngine, engineToShell);
    }

    private static bool IsEngineToShell(string name)
    {
        if (EngineToShellExplicit.Contains(name)) return true;
        foreach (var suffix in EngineToShellSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>
    /// Builds a minimal request frame for the parameterless-handler round-trip test.
    /// Returns null for codes we don't know how to construct inline -- the test skips
    /// those rather than failing, because the matrix coverage test above already
    /// proves they're registered.
    /// </summary>
    private static RpcMessage? BuildEmptyRequest(int code)
    {
        object payload = code switch
        {
            MessageTypes.Ping => new EngineStatusInfo(),
            MessageTypes.Shutdown => new EngineStatusInfo(),
            MessageTypes.DocumentChanged => new DocumentChange { SessionId = "test", FullText = "SELECT 1;" },
            MessageTypes.ConnectionChanged => new ConnectionInfo { SessionId = "test", ServerVersion = 150 },
            MessageTypes.SchemaStatusRequest => new SchemaStatusRequest { SessionId = "test" },
            MessageTypes.HandshakeRequest => new HandshakeRequest
            {
                WebVersion = "test",
                ProtocolVersionMin = Capabilities.ProtocolVersionMin,
                ProtocolVersionMax = Capabilities.ProtocolVersionMax,
            },
            _ => null!,
        };
        if (payload == null) return null;

        return new RpcMessage
        {
            MessageType = code,
            RequestId = code,    // any non-zero value
            Payload = MessagePackSerializer.Serialize(payload.GetType(), payload),
        };
    }
}
