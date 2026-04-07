using System.IO;
using Xunit;

namespace AkmlSql.Core.Tests
{
    public class ConstantsTests
    {
        [Fact] public void ProductName()
        {
            Assert.Equal("AKML SQL", Constants.ProductName);
        }

        [Fact] public void Version_ConstIsSet()
        {
            Assert.Equal("1.0.0", Constants.Version);
        }

        [Fact] public void RuntimeVersion_IsNotEmpty()
        {
            Assert.False(string.IsNullOrEmpty(Constants.RuntimeVersion));
            Assert.StartsWith("1.", Constants.RuntimeVersion);
        }

        [Fact] public void AppVersion_Current_MatchesRuntimeVersion()
        {
            Assert.Equal(Constants.RuntimeVersion, AppVersion.Current);
        }

        [Fact] public void BuildDate()
        {
            Assert.Equal("2026-03-17", Constants.BuildDate);
        }

        [Fact] public void FeedbackUrl()
        {
            Assert.Equal("https://github.com/AkmlSql/feedback", Constants.FeedbackUrl);
        }

        [Fact] public void UpdateManifestUrl()
        {
            Assert.Equal("https://updates.akmlsql.com/manifest.json", Constants.UpdateManifestUrl);
        }

        [Fact] public void AppDataFolderName()
        {
            Assert.Equal("AKML SQL", Constants.AppDataFolderName);
        }

        [Fact] public void ConfigFileName()
        {
            Assert.Equal("config.json", Constants.ConfigFileName);
        }

        [Fact] public void UpdateResultFileName()
        {
            Assert.Equal("update-available.json", Constants.UpdateResultFileName);
        }

        [Fact] public void LogsFolderName()
        {
            Assert.Equal("logs", Constants.LogsFolderName);
        }

        [Fact] public void CacheFolderName()
        {
            Assert.Equal("cache", Constants.CacheFolderName);
        }

        [Fact] public void FormatDocumentCommandId()
        {
            Assert.Equal(0x0200, Constants.FormatDocumentCommandId);
        }

        [Fact] public void FormatSelectionCommandId()
        {
            Assert.Equal(0x0201, Constants.FormatSelectionCommandId);
        }

        [Fact] public void LogMaxFiles()
        {
            Assert.Equal(10, Constants.LogMaxFiles);
        }

        [Fact] public void LogMaxFileSize()
        {
            Assert.Equal(5 * 1024 * 1024L, Constants.LogMaxFileSize);
        }

        [Fact] public void UpdateCheckIntervalHours()
        {
            Assert.Equal(24, Constants.UpdateCheckIntervalHours);
        }

        [Fact]
        public void AppDataPath_IsRootedAndContainsFolderName()
        {
            var p = Constants.AppDataPath;
            Assert.True(Path.IsPathRooted(p));
            Assert.Contains("AKML SQL", p);
        }

        [Fact]
        public void LocalAppDataPath_IsRootedAndContainsFolderName()
        {
            var p = Constants.LocalAppDataPath;
            Assert.True(Path.IsPathRooted(p));
            Assert.Contains("AKML SQL", p);
        }

        [Fact]
        public void ConfigFilePath_IsUnderAppDataAndEndsWithConfigJson()
        {
            var p = Constants.ConfigFilePath;
            Assert.StartsWith(Constants.AppDataPath, p);
            Assert.EndsWith("config.json", p);
        }

        [Fact]
        public void UpdateResultFilePath_IsUnderAppDataAndEndsWithJson()
        {
            var p = Constants.UpdateResultFilePath;
            Assert.StartsWith(Constants.AppDataPath, p);
            Assert.EndsWith("update-available.json", p);
        }

        [Fact]
        public void LogsPath_IsUnderAppDataAndEndsWithLogs()
        {
            var p = Constants.LogsPath;
            Assert.StartsWith(Constants.AppDataPath, p);
            Assert.EndsWith("logs", p);
        }

        [Fact]
        public void CachePath_IsUnderLocalAppDataAndEndsWithCache()
        {
            var p = Constants.CachePath;
            Assert.StartsWith(Constants.LocalAppDataPath, p);
            Assert.EndsWith("cache", p);
        }
    }
}
