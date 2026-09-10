using System;
using System.IO;
using System.Text.Json;
using AkmlSql.Core.Logging;
using Xunit;

namespace AkmlSql.Core.Tests.Logging
{
    public class TelemetryIdentityTests
    {
        [Fact]
        public void MissingConfig_GeneratesAndPersistsInstallId()
        {
            var dir = Path.Combine(Path.GetTempPath(), "akml-tel-" + Guid.NewGuid().ToString("N"));
            var configPath = Path.Combine(dir, "config.json");
            try
            {
                var id = TelemetryIdentity.GetOrCreateInstallId(configPath);

                Assert.False(string.IsNullOrWhiteSpace(id));
                Assert.True(File.Exists(configPath));

                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                Assert.Equal(id, doc.RootElement.GetProperty("installId").GetString());
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void ExistingInstallId_IsReused_AndOtherSettingsSurvive()
        {
            var dir = Path.Combine(Path.GetTempPath(), "akml-tel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(configPath, """
                {
                  "theme": "dark",
                  "installId": "abc123"
                }
                """);
            try
            {
                var id = TelemetryIdentity.GetOrCreateInstallId(configPath);

                Assert.Equal("abc123", id);

                using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
                Assert.Equal("dark", doc.RootElement.GetProperty("theme").GetString());
                Assert.Equal("abc123", doc.RootElement.GetProperty("installId").GetString());
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void UnreadableConfig_ReturnsAnIdWithoutThrowing()
        {
            var dir = Path.Combine(Path.GetTempPath(), "akml-tel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var configPath = Path.Combine(dir, "config.json");
            File.WriteAllText(configPath, "{ not json");
            try
            {
                var id = TelemetryIdentity.GetOrCreateInstallId(configPath);

                Assert.False(string.IsNullOrWhiteSpace(id));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
