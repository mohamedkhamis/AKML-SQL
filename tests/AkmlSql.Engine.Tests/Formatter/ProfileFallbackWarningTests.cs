using Xunit;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Engine.Tests.Formatter;

/// <summary>
/// Pins the profile-fallback notice (<see cref="FormatResponse.ProfileFallbackWarning"/>).
///
/// <para>Formatting with a style that cannot be loaded still SUCCEEDS — it just silently uses POCO
/// defaults. That made a wrong-style format indistinguishable from a correct one, which is how the
/// shipped default style ("Khamis Style") went unnoticed while never applying. The engine now
/// reports the fallback so the shell can tell the user, while formatting keeps working.</para>
/// </summary>
public class ProfileFallbackWarningTests : IDisposable
{
    private readonly string _builtInDir;
    private readonly string _customDir;
    private readonly FormatRequestHandler _handler;

    public ProfileFallbackWarningTests()
    {
        _builtInDir = Path.Combine(Path.GetTempPath(), $"akml_builtin_{Guid.NewGuid():N}");
        _customDir = Path.Combine(Path.GetTempPath(), $"akml_custom_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_customDir);

        var profile = new FormattingProfile();
        profile.Metadata.Name = "Present Style";
        File.WriteAllText(
            Path.Combine(_builtInDir, "present-style.akmlstyle"),
            ProfileSerializer.Serialize(profile));

        _handler = new FormatRequestHandler(new ProfileManager(_builtInDir, _customDir));
    }

    public void Dispose()
    {
        try { Directory.Delete(_builtInDir, recursive: true); } catch (IOException) { }
        try { Directory.Delete(_customDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void MissingStyle_StillFormats_ButReportsTheFallback()
    {
        var response = _handler.HandleFormat(new FormatRequest
        {
            Text = "select 1",
            ProfileName = "No Such Style",
        });

        Assert.True(response.Success, "formatting must keep working — the notice is additive");
        Assert.False(string.IsNullOrWhiteSpace(response.FormattedText));
        Assert.NotNull(response.ProfileFallbackWarning);
        Assert.Contains("No Such Style", response.ProfileFallbackWarning!);
    }

    [Fact]
    public void ResolvableStyle_ReportsNoFallback()
    {
        // Resolves by metadata name over a kebab-case file — the spec-033 resolution fix.
        var response = _handler.HandleFormat(new FormatRequest
        {
            Text = "select 1",
            ProfileName = "Present Style",
        });

        Assert.True(response.Success);
        Assert.Null(response.ProfileFallbackWarning);
    }

    [Fact]
    public void OmittedStyleName_IsDefaultsByDesign_NotAFallback()
    {
        // A null/empty ProfileName means "just use defaults" — that is not a misconfiguration and
        // must not nag the user on every format.
        var response = _handler.HandleFormat(new FormatRequest { Text = "select 1", ProfileName = null });

        Assert.True(response.Success);
        Assert.Null(response.ProfileFallbackWarning);
    }

    [Fact]
    public void Warning_RoundTripsOverTheWire_AndOldPayloadsDeserializeNull()
    {
        var response = new FormatResponse { Success = true, ProfileFallbackWarning = "style X missing" };

        var back = MessagePack.MessagePackSerializer.Deserialize<FormatResponse>(
            MessagePack.MessagePackSerializer.Serialize(response));
        Assert.Equal("style X missing", back.ProfileFallbackWarning);

        // A pre-change peer sends 6 elements (keys 0..5) — key 6 must default to null, not throw.
        var legacy = MessagePack.MessagePackSerializer.Serialize(
            new object?[] { true, "select 1", true, true, 5L, null });
        var legacyBack = MessagePack.MessagePackSerializer.Deserialize<FormatResponse>(legacy);
        Assert.Null(legacyBack.ProfileFallbackWarning);
        Assert.True(legacyBack.Success);
    }
}
