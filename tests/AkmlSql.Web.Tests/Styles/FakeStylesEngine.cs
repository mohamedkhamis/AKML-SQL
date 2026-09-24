using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.Web.Services;

namespace AkmlSql.Web.Tests.Styles;

/// <summary>
/// A paired engine's styles folder, answering the profile messages the way the engine's
/// FormatRequestHandler does (list / get with the SQL Prompt document / save / delete / rename /
/// reset), so the web store can be tested against "the styles SSMS and Visual Studio use".
/// </summary>
internal sealed class FakeStylesEngine : IEngineBridge
{
    private readonly Dictionary<string, string> _custom = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _builtIn = new(StringComparer.OrdinalIgnoreCase);

    public FakeStylesEngine(bool advertiseStyles = true)
    {
        EngineCapabilities = advertiseStyles ? ["styles.sqlprompt.v1", "snippets.write"] : ["snippets.write"];
        var shipped = new FormattingProfile();
        shipped.Metadata.Name = "Engine Built-in";
        _builtIn[shipped.Metadata.Name] = ProfileSerializer.Serialize(shipped);
    }

    public List<int> Sent { get; } = new();

    /// <summary>The style file as the engine stores it (custom first), or null.</summary>
    public FormattingProfile? Stored(string name) =>
        _custom.TryGetValue(name, out var json) || _builtIn.TryGetValue(name, out json) ? ProfileSerializer.Deserialize(json) : null;

    public BridgeState State { get; private set; } = BridgeState.Open;
    public string[] EngineCapabilities { get; }
    public string? EngineVersion => "test";

    public event Action<BridgeState>? StateChanged;
    public event Action<DateTimeOffset?>? RetryScheduled;
    public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected;

    public void SetState(BridgeState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public Task<TResponse> SendAsync<TRequest, TResponse>(int requestMessageType, TRequest request, CancellationToken ct)
        where TRequest : class where TResponse : class
    {
        Sent.Add(requestMessageType);
        object response = request switch
        {
            ProfileListRequest => new ProfileListResponse
            {
                Profiles = _builtIn.Keys.Union(_custom.Keys, StringComparer.OrdinalIgnoreCase)
                    .Select(name =>
                    {
                        var profile = Stored(name)!;
                        return new ProfileInfo
                        {
                            Name = name,
                            IsBuiltIn = _builtIn.ContainsKey(name) && !_custom.ContainsKey(name),
                            IsCustomizedBuiltIn = _builtIn.ContainsKey(name) && _custom.ContainsKey(name),
                            IsSqlPromptStyle = profile.SqlPrompt != null,
                        };
                    }).ToArray(),
            },
            ProfileGetRequest get => Get(get.Name),
            ProfileSaveRequest save => Save(save),
            ProfileDeleteRequest delete => _builtIn.ContainsKey(delete.Name)
                ? new ProfileDeleteResponse { Success = false, ErrorMessage = "built-in" }
                : new ProfileDeleteResponse { Success = _custom.Remove(delete.Name) },
            ProfileRenameRequest rename => Rename(rename),
            ProfileResetRequest reset => new ProfileResetResponse { Success = _builtIn.ContainsKey(reset.Name), ChangesDiscarded = _custom.Remove(reset.Name) },
            _ => throw new NotSupportedException($"Message {requestMessageType} is not modelled by the fake engine."),
        };
        return Task.FromResult((TResponse)response);
    }

    private ProfileGetResponse Get(string name)
    {
        var profile = Stored(name);
        if (profile == null) return new ProfileGetResponse { Success = false, ErrorMessage = "not found" };
        var json = _custom.TryGetValue(name, out var c) ? c : _builtIn[name];
        return new ProfileGetResponse
        {
            Success = true,
            Name = name,
            ProfileJson = json,
            IsBuiltIn = _builtIn.ContainsKey(name) && !_custom.ContainsKey(name),
            HasBuiltIn = _builtIn.ContainsKey(name),
            IsCustomizedBuiltIn = _builtIn.ContainsKey(name) && _custom.ContainsKey(name),
            SqlPromptJson = SqlPromptStyles.ToDocument(profile).ToJson(),
            IsSqlPromptStyle = profile.SqlPrompt != null,
        };
    }

    private ProfileSaveResponse Save(ProfileSaveRequest save)
    {
        var profile = ProfileSerializer.Deserialize(save.ProfileJson);
        if (profile.SqlPrompt != null)
        {
            var document = SqlPromptStyleDocument.FromNode(profile.SqlPrompt);
            document.Name = profile.Metadata.Name;
            profile = SqlPromptStyles.ToProfile(document, profile);
        }
        _custom[profile.Metadata.Name] = ProfileSerializer.Serialize(profile);
        return new ProfileSaveResponse { Success = true };
    }

    private ProfileRenameResponse Rename(ProfileRenameRequest rename)
    {
        if (!_custom.Remove(rename.OldName, out var json)) return new ProfileRenameResponse { Success = false, ErrorMessage = "not found" };
        var profile = ProfileSerializer.Deserialize(json);
        profile.Metadata.Name = rename.NewName;
        _custom[rename.NewName] = ProfileSerializer.Serialize(profile);
        return new ProfileRenameResponse { Success = true, NewName = rename.NewName };
    }

    public Task<HandshakeResponse> ConnectAsync(EngineConnection connection, string? bearerToken, string? pairingPin, CancellationToken ct) =>
        Task.FromResult(new HandshakeResponse { Status = HandshakeStatus.Ok });

    public Task SendNotificationAsync<TPayload>(int messageType, TPayload payload, CancellationToken ct) where TPayload : class => Task.CompletedTask;
    public Task DisconnectAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => default;

    private void Unused()
    {
        RetryScheduled?.Invoke(null);
        FingerprintMismatchDetected?.Invoke(default!);
    }
}
