using Xunit;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Engine.Tests.Formatter;

/// <summary>
/// Reproduces the SHIPPED built-in layout — a kebab-case FILENAME whose <c>metadata.name</c> is
/// Title Case (<c>khamis-style.akmlstyle</c> → <c>"Khamis Style"</c>) — and pins both user-visible
/// symptoms it caused, at the handler boundary the shell actually talks to:
///
/// <list type="number">
/// <item><b>Selecting the style in the Format Styles editor did nothing.</b> The editor loads the
/// selected style through <c>ProfileGet</c>, which returned <c>Success = false</c> for any style
/// whose filename differed from its display name — so no settings loaded and no active mark moved.
/// Single-word styles (<c>compact</c> → <c>"Compact"</c>) worked by accident of case-insensitive
/// filesystems, which is why only SOME styles appeared to work.</item>
/// <item><b>Format SQL silently ignored the style.</b> <c>LoadProfile</c> swallowed the resulting
/// <see cref="FileNotFoundException"/> into a bare <c>new FormattingProfile()</c>, so formatting
/// used POCO defaults. <c>FormatterSettings.ActiveProfile</c> defaults to <c>"Khamis Style"</c> and
/// <c>FormatRequestDispatcher</c> sends exactly that string, so the product's own default style
/// never applied.</item>
/// </list>
/// </summary>
public class ProfileNameMismatchHandlerTests : IDisposable
{
    private const string DisplayName = "Khamis Style";
    private const string KebabFileName = "khamis-style.akmlstyle";
    private const string DescriptionMarker = "shipped-builtin-marker";

    private readonly string _builtInDir;
    private readonly string _customDir;
    private readonly FormatRequestHandler _handler;

    public ProfileNameMismatchHandlerTests()
    {
        _builtInDir = Path.Combine(Path.GetTempPath(), $"akml_builtin_{Guid.NewGuid():N}");
        _customDir = Path.Combine(Path.GetTempPath(), $"akml_custom_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_customDir);

        // The shipped shape: filename ≠ metadata.name, plus a setting that is observable in output
        // (lowercase keywords) so "the profile was applied" can be asserted rather than assumed —
        // FormattingProfile defaults ReservedKeywords to "UPPERCASE".
        var profile = new FormattingProfile();
        profile.Metadata.Name = DisplayName;
        profile.Metadata.Description = DescriptionMarker;
        profile.Casing.ReservedKeywords = "lowercase";
        File.WriteAllText(Path.Combine(_builtInDir, KebabFileName), ProfileSerializer.Serialize(profile));

        _handler = new FormatRequestHandler(new ProfileManager(_builtInDir, _customDir));
    }

    public void Dispose()
    {
        try { Directory.Delete(_builtInDir, recursive: true); } catch (IOException) { }
        try { Directory.Delete(_customDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void ProfileGet_ByDisplayName_ResolvesTheKebabCaseFile()
    {
        // Symptom 1: this returned Success=false, so selecting the style in the editor loaded nothing.
        var response = _handler.HandleProfileGet(new ProfileGetRequest { Name = DisplayName });

        Assert.True(response.Success, response.ErrorMessage);
        Assert.Contains(DescriptionMarker, response.ProfileJson ?? string.Empty);
        Assert.True(response.IsBuiltIn, "resolved from the built-in directory with no custom shadow");
    }

    [Fact]
    public void Format_ByDisplayName_AppliesThatProfile_NotPocoDefaults()
    {
        // Symptom 2: the style silently fell back to defaults, so output stayed UPPERCASE.
        var response = _handler.HandleFormat(new FormatRequest
        {
            Text = "SELECT 1",
            ProfileName = DisplayName,
        });

        Assert.True(response.Success, "format request failed");
        Assert.Contains("select", response.FormattedText ?? string.Empty);
        Assert.DoesNotContain("SELECT", response.FormattedText ?? string.Empty);
    }

    [Fact]
    public void Format_ByDisplayName_DiffersFromDefaultProfileOutput()
    {
        // Guards against a vacuous pass: the named profile must produce DIFFERENT output than the
        // implicit-default path, which is exactly what the silent fallback collapsed them into.
        var named = _handler.HandleFormat(new FormatRequest { Text = "SELECT 1", ProfileName = DisplayName });
        var defaulted = _handler.HandleFormat(new FormatRequest { Text = "SELECT 1", ProfileName = null });

        Assert.True(named.Success && defaulted.Success);
        Assert.NotEqual(defaulted.FormattedText, named.FormattedText);
    }
}
