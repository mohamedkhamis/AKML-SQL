#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class ExecutionPage : IPageBuilder
    {
        public string Key     => "Execution";
        public string Display => "Queries › Results & execution › Execution";
        public string Title   => "Execution";
        public string HelpTopic => "topics/options#execution";
        public string Help    => "Configure query execution behavior: toggle the status-bar execution timer, enable multi-database execution, and set how many seconds a query must run before a long-running notification appears.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Execution");
            var (rowTimer, chkTimer) = ctx.Rows.AddToggle(panel,
                "Execution timer", "Show execution timer in status bar");
            ctx.RegisterSearch("Execution timer", "Show execution timer in status bar", "Toggle", rowTimer);

            var (rowMulti, chkMulti) = ctx.Rows.AddToggle(panel,
                "Multi-database execution", "Enable multi-database execution mode");
            ctx.RegisterSearch("Multi-database execution", "Enable multi-database execution mode", "Toggle", rowMulti);

            ctx.Rows.AddGroupHeader(panel, "Notifications");
            var (rowThreshold, numThreshold) = ctx.Rows.AddNumber(panel,
                "Notification threshold", 5, 300, 5, "seconds",
                "How long a query runs before the long-running query notification appears");
            ctx.RegisterSearch("Notification threshold", "How long a query runs before the long-running query notification appears", "Number", rowThreshold);

            return new ExecutionControls(chkTimer, chkMulti, numThreshold);
        }
    }

    internal sealed class ExecutionControls : IPageControls
    {
        private readonly CheckBox _showTimer;
        private readonly CheckBox _multiDatabase;
        private readonly TextBox _notificationThreshold;

        public ExecutionControls(CheckBox timer, CheckBox multi, TextBox threshold)
        {
            _showTimer = timer;
            _multiDatabase = multi;
            _notificationThreshold = threshold;
        }

        public void Load(AppSettings settings)
        {
            var ex = settings.ExecutionProductivity;
            _showTimer.IsChecked = ex.ShowExecutionTimer;
            _multiDatabase.IsChecked = ex.MultiDatabase;
            RowFactory.SetNumber(_notificationThreshold, ex.NotificationThreshold);
        }

        public void Save(AppSettings settings)
        {
            settings.ExecutionProductivity.ShowExecutionTimer = _showTimer.IsChecked == true;
            settings.ExecutionProductivity.MultiDatabase = _multiDatabase.IsChecked == true;
            settings.ExecutionProductivity.NotificationThreshold = RowFactory.GetNumber(_notificationThreshold);
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
