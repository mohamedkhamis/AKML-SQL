#nullable enable
using System.Reflection;
using System.Windows.Forms;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ui;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 040 (T019, OPT-01) — the Bulk Format wizard's backup checkbox starts from
    /// Options › Format › Styles "Create backups before formatting".
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public class BulkFormatWizardDefaultsTests : AppDataIsolatedTest
    {
        public BulkFormatWizardDefaultsTests() : base("akml-bulkwizard-tests-") { }

        [StaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public void Backup_checkbox_starts_from_the_setting(bool createBackups)
        {
            var settings = new AppSettings();
            settings.Formatter.CreateBackups = createBackups;
            ConfigManager.Save(settings);

            using var wizard = new BulkFormatWizard(new[] { "Khamis Style" });
            var box = (CheckBox)typeof(BulkFormatWizard)
                .GetField("_createBackupsCheck", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(wizard)!;

            Assert.Equal(createBackups, box.Checked);
        }
    }
}
