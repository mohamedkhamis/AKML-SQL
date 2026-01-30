using AKML.SQL.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Moq;
using Xunit;

namespace AKML.SQL.Core.Tests.Services;

/// <summary>
/// Tests for SqlVersionService.
/// Covers Story 11.2: Multi-Version Support.
/// </summary>
public class SqlVersionServiceTests
{
    private readonly SqlVersionService _sut;

    public SqlVersionServiceTests()
    {
        var loggerMock = new Mock<ILogger<SqlVersionService>>();
        _sut = new SqlVersionService(loggerMock.Object);
    }

    #region GetParser Tests

    [Theory]
    [InlineData(SqlServerVersion.Sql2008, typeof(TSql100Parser))]
    [InlineData(SqlServerVersion.Sql2012, typeof(TSql110Parser))]
    [InlineData(SqlServerVersion.Sql2016, typeof(TSql130Parser))]
    [InlineData(SqlServerVersion.Sql2019, typeof(TSql150Parser))]
    [InlineData(SqlServerVersion.Sql2022, typeof(TSql160Parser))]
    public void GetParser_ReturnsCorrectParser(SqlServerVersion version, Type expectedType)
    {
        // Act
        var parser = _sut.GetParser(version);

        // Assert
        parser.Should().BeOfType(expectedType);
    }

    [Fact]
    public void GetParser_Latest_Returns160Parser()
    {
        // Act
        var parser = _sut.GetParser(SqlServerVersion.Latest);

        // Assert
        parser.Should().BeOfType<TSql160Parser>();
    }

    #endregion

    #region GetScriptGenerator Tests

    [Theory]
    [InlineData(SqlServerVersion.Sql2008, typeof(Sql100ScriptGenerator))]
    [InlineData(SqlServerVersion.Sql2012, typeof(Sql110ScriptGenerator))]
    [InlineData(SqlServerVersion.Sql2022, typeof(Sql160ScriptGenerator))]
    public void GetScriptGenerator_ReturnsCorrectGenerator(SqlServerVersion version, Type expectedType)
    {
        // Act
        var generator = _sut.GetScriptGenerator(version);

        // Assert
        generator.Should().BeOfType(expectedType);
    }

    #endregion

    #region DetectVersion Tests

    [Theory]
    [InlineData("10.0.6000.29", SqlServerVersion.Sql2008)]
    [InlineData("11.0.7001.0", SqlServerVersion.Sql2012)]
    [InlineData("12.0.6024.0", SqlServerVersion.Sql2014)]
    [InlineData("13.0.5026.0", SqlServerVersion.Sql2016)]
    [InlineData("14.0.3456.2", SqlServerVersion.Sql2017)]
    [InlineData("15.0.4153.1", SqlServerVersion.Sql2019)]
    [InlineData("16.0.1000.6", SqlServerVersion.Sql2022)]
    public void DetectVersion_FromString_ReturnsCorrectVersion(string versionString, SqlServerVersion expected)
    {
        // Act
        var version = _sut.DetectVersion(versionString);

        // Assert
        version.Should().Be(expected);
    }

    [Fact]
    public void DetectVersion_NullString_ReturnsLatest()
    {
        // Act
        var version = _sut.DetectVersion(null!);

        // Assert
        version.Should().Be(SqlServerVersion.Latest);
    }

    [Fact]
    public void DetectVersion_EmptyString_ReturnsLatest()
    {
        // Act
        var version = _sut.DetectVersion("");

        // Assert
        version.Should().Be(SqlServerVersion.Latest);
    }

    [Fact]
    public void DetectVersion_InvalidFormat_ReturnsLatest()
    {
        // Act
        var version = _sut.DetectVersion("invalid");

        // Assert
        version.Should().Be(SqlServerVersion.Latest);
    }

    #endregion

    #region DetectVersionFromProductVersion Tests

    [Theory]
    [InlineData(10, SqlServerVersion.Sql2008)]
    [InlineData(11, SqlServerVersion.Sql2012)]
    [InlineData(12, SqlServerVersion.Sql2014)]
    [InlineData(13, SqlServerVersion.Sql2016)]
    [InlineData(14, SqlServerVersion.Sql2017)]
    [InlineData(15, SqlServerVersion.Sql2019)]
    [InlineData(16, SqlServerVersion.Sql2022)]
    public void DetectVersionFromProductVersion_ReturnsCorrectVersion(int major, SqlServerVersion expected)
    {
        // Act
        var version = _sut.DetectVersionFromProductVersion(major);

        // Assert
        version.Should().Be(expected);
    }

    [Fact]
    public void DetectVersionFromProductVersion_FutureVersion_ReturnsLatest()
    {
        // Act
        var version = _sut.DetectVersionFromProductVersion(20);

        // Assert
        version.Should().Be(SqlServerVersion.Sql2022); // Highest known
    }

    #endregion

    #region GetFeatures Tests

    [Fact]
    public void GetFeatures_Sql2008_HasLimitedFeatures()
    {
        // Act
        var features = _sut.GetFeatures(SqlServerVersion.Sql2008);

        // Assert
        features.AvailableFeatures.Should().Contain(SqlFeature.CommonTableExpressions);
        features.AvailableFeatures.Should().Contain(SqlFeature.MergeStatement);
        features.AvailableFeatures.Should().NotContain(SqlFeature.JsonSupport);
    }

    [Fact]
    public void GetFeatures_Sql2016_HasJsonSupport()
    {
        // Act
        var features = _sut.GetFeatures(SqlServerVersion.Sql2016);

        // Assert
        features.AvailableFeatures.Should().Contain(SqlFeature.JsonSupport);
        features.AvailableFeatures.Should().Contain(SqlFeature.TemporalTables);
    }

    [Fact]
    public void GetFeatures_Sql2022_HasAllFeatures()
    {
        // Act
        var features = _sut.GetFeatures(SqlServerVersion.Sql2022);

        // Assert
        features.AvailableFeatures.Should().Contain(SqlFeature.LedgerTables);
        features.AvailableFeatures.Should().Contain(SqlFeature.IsDistinctFrom);
    }

    #endregion

    #region IsFeatureAvailable Tests

    [Fact]
    public void IsFeatureAvailable_JsonSupport_Sql2016_ReturnsTrue()
    {
        // Act
        var available = _sut.IsFeatureAvailable(SqlServerVersion.Sql2016, SqlFeature.JsonSupport);

        // Assert
        available.Should().BeTrue();
    }

    [Fact]
    public void IsFeatureAvailable_JsonSupport_Sql2012_ReturnsFalse()
    {
        // Act
        var available = _sut.IsFeatureAvailable(SqlServerVersion.Sql2012, SqlFeature.JsonSupport);

        // Assert
        available.Should().BeFalse();
    }

    [Fact]
    public void IsFeatureAvailable_CTE_AllVersions_ReturnsTrue()
    {
        // Act & Assert
        foreach (var version in _sut.GetSupportedVersions())
        {
            _sut.IsFeatureAvailable(version, SqlFeature.CommonTableExpressions).Should().BeTrue();
        }
    }

    #endregion

    #region GetVersionDisplayName Tests

    [Theory]
    [InlineData(SqlServerVersion.Sql2008, "SQL Server 2008")]
    [InlineData(SqlServerVersion.Sql2016, "SQL Server 2016")]
    [InlineData(SqlServerVersion.Sql2022, "SQL Server 2022")]
    [InlineData(SqlServerVersion.Latest, "SQL Server (Latest)")]
    public void GetVersionDisplayName_ReturnsCorrectName(SqlServerVersion version, string expected)
    {
        // Act
        var name = _sut.GetVersionDisplayName(version);

        // Assert
        name.Should().Be(expected);
    }

    #endregion

    #region GetSupportedVersions Tests

    [Fact]
    public void GetSupportedVersions_ReturnsAllVersions()
    {
        // Act
        var versions = _sut.GetSupportedVersions();

        // Assert
        versions.Should().Contain(SqlServerVersion.Sql2008);
        versions.Should().Contain(SqlServerVersion.Sql2022);
        versions.Should().NotContain(SqlServerVersion.Latest);
    }

    [Fact]
    public void GetSupportedVersions_OrderedByVersionDescending()
    {
        // Act
        var versions = _sut.GetSupportedVersions();

        // Assert
        versions[0].Should().Be(SqlServerVersion.Sql2022);
        versions[^1].Should().Be(SqlServerVersion.Sql2008);
    }

    #endregion

    #region GetVersionKeywords Tests

    [Fact]
    public void GetVersionKeywords_Sql2012_IncludesOffset()
    {
        // Act
        var keywords = _sut.GetVersionKeywords(SqlServerVersion.Sql2012);

        // Assert
        keywords.Should().Contain("OFFSET");
        keywords.Should().Contain("FETCH");
    }

    [Fact]
    public void GetVersionKeywords_Sql2016_IncludesJson()
    {
        // Act
        var keywords = _sut.GetVersionKeywords(SqlServerVersion.Sql2016);

        // Assert
        keywords.Should().Contain("JSON");
    }

    #endregion

    #region GetVersionFunctions Tests

    [Fact]
    public void GetVersionFunctions_Sql2012_IncludesTryConvert()
    {
        // Act
        var functions = _sut.GetVersionFunctions(SqlServerVersion.Sql2012);

        // Assert
        functions.Should().Contain("TRY_CONVERT");
        functions.Should().Contain("FORMAT");
    }

    [Fact]
    public void GetVersionFunctions_Sql2016_IncludesStringSplit()
    {
        // Act
        var functions = _sut.GetVersionFunctions(SqlServerVersion.Sql2016);

        // Assert
        functions.Should().Contain("STRING_SPLIT");
    }

    #endregion

    #region ValidateForVersion Tests

    [Fact]
    public void ValidateForVersion_ValidSql_ReturnsValid()
    {
        // Arrange
        var sql = "SELECT * FROM Users";

        // Act
        var result = _sut.ValidateForVersion(sql, SqlServerVersion.Sql2016);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateForVersion_InvalidSql_ReturnsErrors()
    {
        // Arrange
        var sql = "SELEC * FRM Users";

        // Act
        var result = _sut.ValidateForVersion(sql, SqlServerVersion.Sql2016);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void ValidateForVersion_EmptySql_ReturnsValid()
    {
        // Act
        var result = _sut.ValidateForVersion("", SqlServerVersion.Sql2016);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateForVersion_SetsVersion()
    {
        // Act
        var result = _sut.ValidateForVersion("SELECT 1", SqlServerVersion.Sql2019);

        // Assert
        result.Version.Should().Be(SqlServerVersion.Sql2019);
    }

    #endregion

    #region VersionFeatures Tests

    [Fact]
    public void VersionFeatures_HasMajorVersion()
    {
        // Act
        var features = _sut.GetFeatures(SqlServerVersion.Sql2016);

        // Assert
        features.MajorVersion.Should().Be(13);
    }

    #endregion
}
