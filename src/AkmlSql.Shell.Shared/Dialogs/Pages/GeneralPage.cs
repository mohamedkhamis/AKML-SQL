#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class GeneralPage : IPageBuilder
    {
        public string Key     => "General";
        public string Display => "Miscellaneous › Application";
        public string Title   => "General Settings";
        public string Help    => "Configure the AKML SQL dialog theme, automatic update checks, and anonymous usage telemetry. This page also shows the configuration file, log directory, and installed version.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Appearance");
            var (rowTheme, cboTheme) = ctx.Rows.AddDropdown(panel,
                "Theme",
                new[] { "Dark", "Light", "System" },
                "UI color theme for AKML SQL dialogs");
            ctx.RegisterSearch("Theme", "UI color theme for AKML SQL dialogs", "Dropdown", rowTheme);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Updates & Telemetry");
            var (rowAutoUpdate, chkAutoUpdate) = ctx.Rows.AddToggle(panel,
                "Check for updates automatically",
                "Checks for new versions every 24 hours on startup");
            ctx.RegisterSearch("Check for updates automatically", "Checks for new versions every 24 hours on startup", "Toggle", rowAutoUpdate);

            var (rowTelemetry, chkTelemetry) = ctx.Rows.AddToggle(panel,
                "Send anonymous usage telemetry",
                "No personally identifiable information is collected");
            ctx.RegisterSearch("Send anonymous usage telemetry", "No personally identifiable information is collected", "Toggle", rowTelemetry);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Paths");
            var (rowConfig, _) = ctx.Rows.AddReadOnlyField(panel, "Configuration file", Constants.ConfigFilePath);
            ctx.RegisterSearch("Configuration file", Constants.ConfigFilePath, "Info", rowConfig);
            var (rowLogs, _) = ctx.Rows.AddReadOnlyField(panel, "Log directory", Constants.LogsPath);
            ctx.RegisterSearch("Log directory", Constants.LogsPath, "Info", rowLogs);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "About");
            var versionRow = ctx.Rows.AddInfoRow(panel, "Version", Constants.RuntimeVersion + " (" + Constants.BuildDate + ")");
            ctx.RegisterSearch("Version", Constants.RuntimeVersion, "Info", versionRow);

            return new GeneralControls(cboTheme, chkAutoUpdate, chkTelemetry);
        }
    }

    internal sealed class GeneralControls : IPageControls
    {
        // Exposed publicly so SettingsWindow can wire OnThemeSelectionChanged
        // after Build returns. The handler lives on the host because changing
        // theme triggers SaveControlsToSettings + ConfigManager.Save + window
        // rebuild — all host responsibilities.
        public ComboBox Theme { get; }

        private readonly CheckBox _autoUpdate;
        private readonly CheckBox _telemetry;

        public GeneralControls(ComboBox theme, CheckBox autoUpdate, CheckBox telemetry)
        {
            Theme = theme;
            _autoUpdate = autoUpdate;
            _telemetry = telemetry;
        }

        public void Load(AppSettings settings)
        {
            _autoUpdate.IsChecked = settings.AutoUpdateEnabled;
            _telemetry.IsChecked = settings.TelemetryEnabled;
            Theme.SelectedIndex = (settings.Theme?.ToLowerInvariant()) switch
            {
                "light"  => 1,
                "system" => 2,
                _        => 0,
            };
        }

        public void Save(AppSettings settings)
        {
            settings.AutoUpdateEnabled = _autoUpdate.IsChecked == true;
            settings.TelemetryEnabled = _telemetry.IsChecked == true;
            settings.Theme = Theme.SelectedIndex switch
            {
                1 => "light",
                2 => "system",
                _ => "dark",
            };
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
