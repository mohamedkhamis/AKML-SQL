#nullable enable
using System.Globalization;
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class SafetyPage : IPageBuilder
    {
        public string Key     => "Safety";
        public string Display => "Queries › Execution Warnings";
        public string Title   => "Execution Safety";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddGroupHeader(panel, "Warnings");

            var (rowProd, chkProd) = ctx.Rows.AddToggle(panel,
                "Production server warning",
                "Show a warning banner when connected to production environments");
            ctx.RegisterSearch("Production server warning", "Show a warning banner when connected to production environments", "Toggle", rowProd);

            var (rowDel, chkDel) = ctx.Rows.AddToggle(panel,
                "DELETE without WHERE",
                "Warn before executing DELETE statements with no WHERE clause");
            ctx.RegisterSearch("DELETE without WHERE", "Warn before executing DELETE statements with no WHERE clause", "Toggle", rowDel);

            var (rowUpd, chkUpd) = ctx.Rows.AddToggle(panel,
                "UPDATE without WHERE",
                "Warn before executing UPDATE statements with no WHERE clause");
            ctx.RegisterSearch("UPDATE without WHERE", "Warn before executing UPDATE statements with no WHERE clause", "Toggle", rowUpd);

            var (rowDrop, chkDrop) = ctx.Rows.AddToggle(panel,
                "DROP confirmation",
                "Require confirmation before executing DROP statements");
            ctx.RegisterSearch("DROP confirmation", "Require confirmation before executing DROP statements", "Toggle", rowDrop);

            var (rowTrunc, chkTrunc) = ctx.Rows.AddToggle(panel,
                "TRUNCATE confirmation",
                "Require confirmation before executing TRUNCATE statements");
            ctx.RegisterSearch("TRUNCATE confirmation", "Require confirmation before executing TRUNCATE statements", "Toggle", rowTrunc);

            ctx.Rows.AddGroupSeparator(panel);
            ctx.Rows.AddGroupHeader(panel, "Transaction Reminder");

            var (rowTxRem, chkTxRem) = ctx.Rows.AddToggle(panel,
                "Enable transaction reminder",
                "Periodically remind about open transactions on production servers");
            ctx.RegisterSearch("Enable transaction reminder", "Periodically remind about open transactions on production servers", "Toggle", rowTxRem);

            var (rowSlider, sldInterval, lblInterval) = ctx.Rows.AddSlider(panel,
                "Reminder interval (seconds)", 30, 3600, 300,
                "Time between transaction reminder notifications");
            ctx.RegisterSearch("Reminder interval (seconds)", "Time between transaction reminder notifications", "Slider", rowSlider);

            return new SafetyControls(chkProd, chkDel, chkUpd, chkDrop, chkTrunc, chkTxRem, sldInterval, lblInterval);
        }
    }

    internal sealed class SafetyControls : IPageControls
    {
        private readonly CheckBox _prodWarning;
        private readonly CheckBox _deleteNoWhere;
        private readonly CheckBox _updateNoWhere;
        private readonly CheckBox _dropConfirm;
        private readonly CheckBox _truncateConfirm;
        private readonly CheckBox _txReminder;
        private readonly Slider _txReminderInterval;
        private readonly TextBlock _txReminderLabel;

        public SafetyControls(CheckBox prod, CheckBox del, CheckBox upd, CheckBox drop, CheckBox trunc,
            CheckBox txRem, Slider sldInterval, TextBlock lblInterval)
        {
            _prodWarning = prod;
            _deleteNoWhere = del;
            _updateNoWhere = upd;
            _dropConfirm = drop;
            _truncateConfirm = trunc;
            _txReminder = txRem;
            _txReminderInterval = sldInterval;
            _txReminderLabel = lblInterval;
        }

        public void Load(AppSettings settings)
        {
            var sf = settings.Safety;
            _prodWarning.IsChecked = sf.ProductionWarning;
            _deleteNoWhere.IsChecked = sf.DeleteWithoutWhere;
            _updateNoWhere.IsChecked = sf.UpdateWithoutWhere;
            _dropConfirm.IsChecked = sf.DropConfirmation;
            _truncateConfirm.IsChecked = sf.TruncateConfirmation;
            _txReminder.IsChecked = sf.TransactionReminder;
            _txReminderInterval.Value = sf.TransactionReminderInterval;
            _txReminderLabel.Text = sf.TransactionReminderInterval.ToString(CultureInfo.InvariantCulture);
        }

        public void Save(AppSettings settings)
        {
            settings.Safety.ProductionWarning = _prodWarning.IsChecked == true;
            settings.Safety.DeleteWithoutWhere = _deleteNoWhere.IsChecked == true;
            settings.Safety.UpdateWithoutWhere = _updateNoWhere.IsChecked == true;
            settings.Safety.DropConfirmation = _dropConfirm.IsChecked == true;
            settings.Safety.TruncateConfirmation = _truncateConfirm.IsChecked == true;
            settings.Safety.TransactionReminder = _txReminder.IsChecked == true;
            settings.Safety.TransactionReminderInterval = (int)_txReminderInterval.Value;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
