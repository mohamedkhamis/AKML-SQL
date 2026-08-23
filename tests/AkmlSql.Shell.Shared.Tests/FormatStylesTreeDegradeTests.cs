#nullable enable
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 033 (T026) — mixed-version tolerance, tested on the pure
    /// <see cref="FormatStylesSchemaModel"/> (the window itself cannot be constructed outside
    /// a VS/SSMS process — DialogWindow's DpiHelper needs IVsSettingsManager): a v1 schema
    /// (no parentId / allowedEnumValues / description) yields the flat model with free-text
    /// enum controls; a v2 schema yields the 5-category hierarchy and enum ComboBoxes;
    /// unknown category ids land under Other.
    /// </summary>
    public class FormatStylesTreeDegradeTests
    {
        private const string V1Schema = /*lang=json*/ """
            {"schemaVersion":1,
             "groups":[{"id":"casing","displayName":"Casing","order":0},
                       {"id":"whitespace","displayName":"Whitespace","order":1}],
             "settings":[{"id":"casing.reservedKeywords","groupId":"casing","displayName":"Reserved keywords",
                          "type":"Enum","default":"UPPERCASE","status":"Implemented"}]}
            """;

        private const string V2Schema = /*lang=json*/ """
            {"schemaVersion":2,
             "groups":[{"id":"casing","displayName":"Casing","parentId":"global","order":0},
                       {"id":"whitespace","displayName":"Whitespace","parentId":"global","order":1},
                       {"id":"dml","displayName":"Dml","parentId":"statements","order":2},
                       {"id":"futureGroup","displayName":"Future","parentId":"someNewCategory","order":3}],
             "settings":[{"id":"casing.reservedKeywords","groupId":"casing","displayName":"Reserved keywords",
                          "type":"Enum","default":"UPPERCASE","status":"Implemented",
                          "allowedEnumValues":["UPPERCASE","lowercase","AsIs"],
                          "description":"Casing applied to reserved keywords.",
                          "min":null,"max":null}]}
            """;

        [Fact]
        public void V1_schema_parses_flat_with_no_v2_fields()
        {
            var model = FormatStylesSchemaModel.Parse(V1Schema);

            Assert.False(model.Categorized);
            Assert.Empty(model.Categories);
            Assert.Equal(2, model.FlatGroups.Count);
            Assert.Equal("Casing", model.FlatGroups[0].DisplayName);

            var setting = Assert.Single(model.FlatGroups[0].Settings);
            Assert.Null(setting.Description);
            Assert.Null(setting.AllowedEnumValues);
            Assert.Null(setting.Min);

            // Degrade: enum without values renders as the legacy free-text box.
            Assert.Equal(FormatStylesSchemaModel.ControlKind.EnumTextBox,
                FormatStylesSchemaModel.ControlKindFor(setting));
        }

        [Fact]
        public void V2_schema_parses_categories_in_canonical_order_with_unknown_to_other()
        {
            var model = FormatStylesSchemaModel.Parse(V2Schema);

            Assert.True(model.Categorized);
            Assert.Equal(3, model.Categories.Count);
            Assert.Equal("Global", model.Categories[0].DisplayName);
            Assert.Equal("Statements", model.Categories[1].DisplayName);
            Assert.Equal("Other", model.Categories[2].DisplayName); // unknown parentId → Other
            Assert.Equal(2, model.Categories[0].Groups.Count);      // casing + whitespace
            Assert.Equal("Future", model.Categories[2].Groups[0].DisplayName);

            var setting = Assert.Single(model.Categories[0].Groups[0].Settings);
            Assert.Equal("Casing applied to reserved keywords.", setting.Description);
            Assert.Equal(["UPPERCASE", "lowercase", "AsIs"], setting.AllowedEnumValues);

            // v2: enum with values renders as the themed ComboBox.
            Assert.Equal(FormatStylesSchemaModel.ControlKind.EnumComboBox,
                FormatStylesSchemaModel.ControlKindFor(setting));
        }

        [Fact]
        public void Control_kinds_cover_all_setting_types()
        {
            Assert.Equal(FormatStylesSchemaModel.ControlKind.CheckBox,
                FormatStylesSchemaModel.ControlKindFor(new FormatSettingNode { Type = "Bool" }));
            Assert.Equal(FormatStylesSchemaModel.ControlKind.IntBox,
                FormatStylesSchemaModel.ControlKindFor(new FormatSettingNode { Type = "Int", Min = 0, Max = 10 }));
            Assert.Equal(FormatStylesSchemaModel.ControlKind.ReadOnly,
                FormatStylesSchemaModel.ControlKindFor(new FormatSettingNode { Type = "Other" }));
        }
    }
}
