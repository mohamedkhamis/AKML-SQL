using System;
using System.IO;
using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Profiles;

/// <summary>
/// Pins the cost — and the freshness — of metadata-name profile resolution.
///
/// <para>Resolution has two tiers: an exact filename probe, then a scan of the profile
/// directories comparing each file's <c>metadata.name</c>. The scan was written as the rare miss
/// path, but the product's own default (<c>"Khamis Style"</c>, stored as
/// <c>khamis-style.akmlstyle</c>) can only resolve through it — a space is not a hyphen, so the
/// filename probe always misses. Every format request therefore re-scanned and re-read the whole
/// profile directory, unbounded in the user's profile count.</para>
///
/// <para>Caching that resolution is only safe if it notices the file changing underneath it,
/// which is what the staleness cases below exist to guarantee.</para>
/// </summary>
public class ProfileResolutionCacheTests
{
    private static string BuiltInDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AKML-SQL.slnx")))
            dir = dir.Parent;
        if (dir == null) throw new DirectoryNotFoundException("AKML-SQL.slnx not found above " + AppContext.BaseDirectory);
        return Path.Combine(dir.FullName, "src", "AkmlSql.Formatting", "Profiles", "BuiltIn");
    }

    private static ProfileManager CreateManager(out string customDir)
    {
        customDir = Path.Combine(Path.GetTempPath(), "akmlsql-profile-cache-" + Guid.NewGuid());
        return new ProfileManager(BuiltInDir(), customDir);
    }

    /// <summary>Writes a custom profile whose FILENAME deliberately differs from its metadata
    /// name, so it is reachable only through the metadata scan.</summary>
    private static void WriteCustom(string customDir, string fileStem, string metadataName)
    {
        Directory.CreateDirectory(customDir);
        var profile = new FormattingProfile();
        profile.Metadata.Name = metadataName;
        File.WriteAllText(
            Path.Combine(customDir, fileStem + ".akmlstyle"),
            ProfileSerializer.Serialize(profile));
    }

    [Fact]
    public void RepeatedResolutionOfTheDefaultStyle_DoesNotRescanTheProfileDirectories()
    {
        var manager = CreateManager(out _);

        manager.Load("Khamis Style");
        var afterFirst = manager.MetadataScanFileReads;
        Assert.True(afterFirst > 0, "the default style must resolve through the metadata scan");

        manager.Load("Khamis Style");

        Assert.Equal(afterFirst, manager.MetadataScanFileReads);
    }

    [Fact]
    public void EditingAProfileInPlace_IsPickedUp_NotServedFromACachedName()
    {
        var manager = CreateManager(out var customDir);
        WriteCustom(customDir, "team", "Team Style");
        try
        {
            Assert.Equal("Team Style", manager.Load("Team Style").Metadata.Name);

            // Same file, different metadata name — the old name must stop resolving.
            File.SetLastWriteTimeUtc(Path.Combine(customDir, "team.akmlstyle"), DateTime.UtcNow.AddSeconds(1));
            WriteCustom(customDir, "team", "Renamed Style");

            Assert.Equal("Renamed Style", manager.Load("Renamed Style").Metadata.Name);
            Assert.Throws<FileNotFoundException>(() => manager.Load("Team Style"));
        }
        finally
        {
            Directory.Delete(customDir, recursive: true);
        }
    }

    [Fact]
    public void DeletingTheResolvedFile_StopsTheNameResolving()
    {
        var manager = CreateManager(out var customDir);
        WriteCustom(customDir, "team", "Team Style");
        try
        {
            Assert.Equal("Team Style", manager.Load("Team Style").Metadata.Name);

            File.Delete(Path.Combine(customDir, "team.akmlstyle"));

            Assert.Throws<FileNotFoundException>(() => manager.Load("Team Style"));
        }
        finally
        {
            Directory.Delete(customDir, recursive: true);
        }
    }

    [Fact]
    public void ANewlyAddedProfile_ResolvesWithoutRestartingTheProcess()
    {
        var manager = CreateManager(out var customDir);
        Directory.CreateDirectory(customDir);
        try
        {
            Assert.Throws<FileNotFoundException>(() => manager.Load("Late Style"));

            WriteCustom(customDir, "late", "Late Style");

            Assert.Equal("Late Style", manager.Load("Late Style").Metadata.Name);
        }
        finally
        {
            Directory.Delete(customDir, recursive: true);
        }
    }
}
