#nullable enable
using System.Globalization;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class ExecutionPage : IPageBuilder
    {
        public string Key     => "Execution";
        public string Display => "Queries › Execution";
        public string Title   => "Execution";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            var (rowTimer, chkTimer) = ctx.Rows.AddToggle(panel,
                "Execution timer", "Show execution timer in status bar");
            ctx.RegisterSearch("Execution timer", "Show execution timer in status bar", "Toggle", rowTimer);

            var (rowMulti, chkMulti) = ctx.Rows.AddToggle(panel,
                "Multi-database execution", "Enable multi-database execution mode");
            ctx.RegisterSearch("Multi-database execution", "Enable multi-database execution mode", "Toggle", rowMulti);

            ctx.Rows.AddGroupHeader(panel, "Notifications");
            var (rowSlider, sldThreshold, lblThreshold) = ctx.Rows.AddSlider(panel,
                "Notification threshold", 5, 300, 30,
                "Seconds before showing long-running query notification");
            ctx.RegisterSearch("Notification threshold", "Seconds before showing long-running query notification", "Slider", rowSlider);

            return new ExecutionControls(chkTimer, chkMulti, sldThreshold, lblThreshold);
        }
    }

    internal sealed class ExecutionControls : IPageControls
    {
        private readonly CheckBox _showTimer;
        private readonly CheckBox _multiDatabase;
        private readonly Slider _notificationThreshold;
        private readonly TextBlock _notificationLabel;

        public ExecutionControls(CheckBox timer, CheckBox multi, Slider sld, TextBlock lbl)
        {
            _showTimer = timer;
            _multiDatabase = multi;
            _notificationThreshold = sld;
            _notificationLabel = lbl;
        }

        public void Load(AppSettings settings)
        {
            var ex = settings.ExecutionProductivity;
            _showTimer.IsChecked = ex.ShowExecutionTimer;
            _multiDatabase.IsChecked = ex.MultiDatabase;
            _notificationThreshold.Value = ex.NotificationThreshold;
            _notificationLabel.Text = ex.NotificationThreshold.ToString(CultureInfo.InvariantCulture);
        }

        public void Save(AppSettings settings)
        {
            settings.ExecutionProductivity.ShowExecutionTimer = _showTimer.IsChecked == true;
            settings.ExecutionProductivity.MultiDatabase = _multiDatabase.IsChecked == true;
            settings.ExecutionProductivity.NotificationThreshold = (int)_notificationThreshold.Value;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
