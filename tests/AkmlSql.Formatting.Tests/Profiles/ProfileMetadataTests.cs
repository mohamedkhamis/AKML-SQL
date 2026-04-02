using Xunit;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Profiles;

public class ProfileMetadataTests
{
    [Fact]
    public void ProfileMetadata_Defaults()
    {
        var m = new ProfileMetadata();
        Assert.NotEmpty(m.Id); // GUID
        Assert.Equal(1, m.SchemaVersion);
        Assert.Equal("Untitled", m.Name);
        Assert.Equal(string.Empty, m.Description);
        Assert.Equal(string.Empty, m.Author);
        Assert.Equal("1.0.0", m.Version);
        Assert.Null(m.BasedOn);
        Assert.False(m.IsBuiltIn);
        // Created and Modified should be recent UTC times
        Assert.True((DateTime.UtcNow - m.Created).TotalSeconds < 5);
        Assert.True((DateTime.UtcNow - m.Modified).TotalSeconds < 5);
    }

    [Fact]
    public void ProfileMetadata_Mutations()
    {
        var created = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var modified = new DateTime(2025, 3, 15, 12, 0, 0, DateTimeKind.Utc);

        var m = new ProfileMetadata
        {
            Id = "fixed-id",
            SchemaVersion = 2,
            Name = "My Profile",
            Description = "Test profile",
            Author = "Test User",
            Version = "2.0.0",
            Created = created,
            Modified = modified,
            BasedOn = "Default",
            IsBuiltIn = true
        };

        Assert.Equal("fixed-id", m.Id);
        Assert.Equal(2, m.SchemaVersion);
        Assert.Equal("My Profile", m.Name);
        Assert.Equal("Test profile", m.Description);
        Assert.Equal("Test User", m.Author);
        Assert.Equal("2.0.0", m.Version);
        Assert.Equal(created, m.Created);
        Assert.Equal(modified, m.Modified);
        Assert.Equal("Default", m.BasedOn);
        Assert.True(m.IsBuiltIn);
    }

    [Fact]
    public void ProfileMetadata_Id_IsUniquePerInstance()
    {
        var m1 = new ProfileMetadata();
        var m2 = new ProfileMetadata();
        Assert.NotEqual(m1.Id, m2.Id);
    }
}
