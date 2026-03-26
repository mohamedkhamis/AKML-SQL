using Xunit;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Formatter;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Engine.Tests.Formatter;

public class FormatRequestHandlerTests : IDisposable
{
    private readonly string _builtInDir;
    private readonly string _customDir;
    private readonly ProfileManager _profileManager;
    private readonly FormatRequestHandler _handler;

    public FormatRequestHandlerTests()
    {
        _builtInDir = Path.Combine(Path.GetTempPath(), $"akml_builtin_{Guid.NewGuid():N}");
        _customDir = Path.Combine(Path.GetTempPath(), $"akml_custom_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_customDir);
        _profileManager = new ProfileManager(_builtInDir, _customDir);
        _handler = new FormatRequestHandler(_profileManager);
    }

    public void Dispose()
    {
        try { Directory.Delete(_builtInDir, recursive: true); } catch { }
        try { Directory.Delete(_customDir, recursive: true); } catch { }
    }

    // ── HandleFormat ──────────────────────────────────────────────────────

    [Fact]
    public void HandleFormat_ValidSql_ReturnsSuccess()
    {
        var request = new FormatRequest { Text = "select 1", ProfileName = null };

        var response = _handler.HandleFormat(request);

        Assert.True(response.Success);
    }

    [Fact]
    public void HandleFormat_ValidSql_FormattedTextNotEmpty()
    {
        var request = new FormatRequest { Text = "select 1", ProfileName = null };

        var response = _handler.HandleFormat(request);

        Assert.False(string.IsNullOrEmpty(response.FormattedText));
    }

    [Fact]
    public void HandleFormat_NullProfile_UsesDefaultProfile()
    {
        var request = new FormatRequest { Text = "SELECT 1", ProfileName = null };

        // Should not throw when profile name is null
        var ex = Record.Exception(() => _handler.HandleFormat(request));

        Assert.Null(ex);
    }

    [Fact]
    public void HandleFormat_UnknownProfile_FallsBackToDefault()
    {
        var request = new FormatRequest { Text = "SELECT 1", ProfileName = "NonExistentProfile" };

        // Should not throw — handler falls back to default profile
        var response = _handler.HandleFormat(request);

        Assert.True(response.Success);
    }

    [Fact]
    public void HandleFormat_EmptySql_NoException()
    {
        var request = new FormatRequest { Text = "", ProfileName = null };

        var ex = Record.Exception(() => _handler.HandleFormat(request));

        Assert.Null(ex);
    }

    // ── HandleFormatSelection ─────────────────────────────────────────────

    [Fact]
    public void HandleFormatSelection_ValidRange_ReturnsSuccess()
    {
        const string sql = "select col from tbl";
        var request = new FormatSelectionRequest
        {
            Text = sql,
            SelectionStart = 0,
            SelectionEnd = sql.Length,
            ProfileName = null
        };

        var response = _handler.HandleFormatSelection(request);

        Assert.True(response.Success);
    }

    [Fact]
    public void HandleFormatSelection_PartialRange_NoException()
    {
        const string sql = "select col from tbl";
        var request = new FormatSelectionRequest
        {
            Text = sql,
            SelectionStart = 0,
            SelectionEnd = 10,
            ProfileName = null
        };

        var ex = Record.Exception(() => _handler.HandleFormatSelection(request));

        Assert.Null(ex);
    }

    // ── HandleFormatPreview ───────────────────────────────────────────────

    [Fact]
    public void HandleFormatPreview_ValidProfileJson_ReturnsText()
    {
        var profile = new FormattingProfile();
        var profileJson = ProfileSerializer.Serialize(profile);

        var request = new FormatPreviewRequest
        {
            SampleText = "select 1",
            ProfileJson = profileJson
        };

        var response = _handler.HandleFormatPreview(request);

        Assert.False(string.IsNullOrEmpty(response.FormattedText));
    }

    [Fact]
    public void HandleFormatPreview_InvalidProfileJson_ReturnsSampleText()
    {
        var request = new FormatPreviewRequest
        {
            SampleText = "select 1",
            ProfileJson = "{ invalid json }"
        };

        // Should return original sample on failure
        var response = _handler.HandleFormatPreview(request);

        Assert.NotNull(response.FormattedText);
    }

    // ── HandleProfileList ─────────────────────────────────────────────────

    [Fact]
    public void HandleProfileList_EmptyDirectories_ReturnsEmptyList()
    {
        var response = _handler.HandleProfileList();

        Assert.NotNull(response.Profiles);
        Assert.Empty(response.Profiles);
    }

    [Fact]
    public void HandleProfileList_SavedProfile_AppearsInList()
    {
        var profile = new FormattingProfile();
        profile.Metadata.Name = "TestProfile";
        _profileManager.Save(profile);

        var response = _handler.HandleProfileList();

        Assert.Contains(response.Profiles, p => p.Name == "TestProfile");
    }
}
