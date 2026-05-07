using System.Text.Json;
using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Engine.Tests.Config
{
    public class SettingsImportExportTests
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        [Fact]
        public void RoundTrip_NewSubObjects_PreservesValues()
        {
            var input = new AppSettings();
            input.IntelliSense.SuggestionTypes.IncludeSystemObjects = true;
            input.IntelliSense.SuggestionTypes.IncludeKeywords = false;
            input.IntelliSense.SuggestionTypes.ColumnScope = ColumnSuggestionScope.All;
            input.IntelliSense.Qualification.SchemaMode = SchemaQualifyMode.Always;
            input.IntelliSense.Qualification.BracketMode = BracketMode.Always;
            input.IntelliSense.Qualification.QualifyColumnsWithTableOrAlias = false;
            input.IntelliSense.InsertOptions.IncludeColumns = false;
            input.IntelliSense.InsertOptions.IncludeDefaultsAsComments = false;
            input.IntelliSense.InsertOptions.IncludeProcParamInfo = false;
            input.IntelliSense.JoinOptions.MatchByColumnName = false;
            input.Labs.GhostTextCompletion = true;
            input.Labs.ParallelSchemaCache = true;

            var json = JsonSerializer.Serialize(input, Options);
            var roundTripped = JsonSerializer.Deserialize<AppSettings>(json, Options)!;

            Assert.True(roundTripped.IntelliSense.SuggestionTypes.IncludeSystemObjects);
            Assert.False(roundTripped.IntelliSense.SuggestionTypes.IncludeKeywords);
            Assert.Equal(ColumnSuggestionScope.All, roundTripped.IntelliSense.SuggestionTypes.ColumnScope);
            Assert.Equal(SchemaQualifyMode.Always, roundTripped.IntelliSense.Qualification.SchemaMode);
            Assert.Equal(BracketMode.Always, roundTripped.IntelliSense.Qualification.BracketMode);
            Assert.False(roundTripped.IntelliSense.Qualification.QualifyColumnsWithTableOrAlias);
            Assert.False(roundTripped.IntelliSense.InsertOptions.IncludeColumns);
            Assert.False(roundTripped.IntelliSense.InsertOptions.IncludeDefaultsAsComments);
            Assert.False(roundTripped.IntelliSense.InsertOptions.IncludeProcParamInfo);
            Assert.False(roundTripped.IntelliSense.JoinOptions.MatchByColumnName);
            Assert.True(roundTripped.Labs.GhostTextCompletion);
            Assert.True(roundTripped.Labs.ParallelSchemaCache);
        }

        [Fact]
        public void Deserialize_OldConfigMissingNewFields_DefaultsCleanly()
        {
            // An old config.json from before Phase 2: only has the existing fields.
            var oldJson = @"{
                ""intelliSense"": {
                    ""enabled"": true,
                    ""autoTrigger"": true,
                    ""joinAssist"": true,
                    ""autoAlias"": false,
                    ""maxSuggestions"": 50
                }
            }";

            var settings = JsonSerializer.Deserialize<AppSettings>(oldJson, Options)!;

            // Old fields preserved
            Assert.True(settings.IntelliSense.Enabled);
            Assert.True(settings.IntelliSense.JoinAssist);
            Assert.False(settings.IntelliSense.AutoAlias);

            // New fields default-construct
            Assert.NotNull(settings.IntelliSense.SuggestionTypes);
            Assert.NotNull(settings.IntelliSense.Qualification);
            Assert.NotNull(settings.IntelliSense.InsertOptions);
            Assert.NotNull(settings.IntelliSense.JoinOptions);
            Assert.NotNull(settings.Labs);

            // Defaults match the spec
            Assert.False(settings.IntelliSense.SuggestionTypes.IncludeSystemObjects);
            Assert.True(settings.IntelliSense.SuggestionTypes.IncludeKeywords);
            Assert.Equal(ColumnSuggestionScope.ReferencedOnly, settings.IntelliSense.SuggestionTypes.ColumnScope);
            Assert.Equal(SchemaQualifyMode.NonDefaultOnly, settings.IntelliSense.Qualification.SchemaMode);
            Assert.Equal(BracketMode.WhenRequired, settings.IntelliSense.Qualification.BracketMode);
            Assert.True(settings.IntelliSense.InsertOptions.IncludeColumns);
            Assert.True(settings.IntelliSense.JoinOptions.MatchByColumnName);
            Assert.False(settings.Labs.GhostTextCompletion);
        }
    }
}
