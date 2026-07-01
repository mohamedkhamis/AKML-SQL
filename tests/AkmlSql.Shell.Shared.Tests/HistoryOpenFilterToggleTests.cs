using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Drives the SQL History "open queries / closed queries" toolbar filter toggles
    /// (report §3 rec #1). The Redgate window exposes two folder icons that filter the
    /// list to only-open or only-closed queries; AKML already had an unbound
    /// <see cref="HistoryViewModel.IsOpenFilter"/> honoured by the search path but no
    /// control drove it. These tests pin the 3-state cycle behaviour the toolbar buttons
    /// rely on (null = all, true = open only, false = closed only), plus the reset.
    /// STA because <see cref="HistoryViewModel"/> touches WPF's CommandManager.
    /// </summary>
    public class HistoryOpenFilterToggleTests
    {
        [StaFact]
        public void ToggleOpenFilter_Open_SetsOpenOnly_ThenClearsToAll()
        {
            var vm = new HistoryViewModel();
            Assert.Null(vm.IsOpenFilter); // starts at "all"

            vm.ToggleOpenFilter(open: true);
            Assert.True(vm.IsOpenFilter);  // now "open only"

            vm.ToggleOpenFilter(open: true);
            Assert.Null(vm.IsOpenFilter);  // clicking the active toggle again → back to "all"
        }

        [StaFact]
        public void ToggleOpenFilter_Closed_SetsClosedOnly_ThenClearsToAll()
        {
            var vm = new HistoryViewModel();

            vm.ToggleOpenFilter(open: false);
            Assert.False(vm.IsOpenFilter); // "closed only"

            vm.ToggleOpenFilter(open: false);
            Assert.Null(vm.IsOpenFilter);  // toggle off → "all"
        }

        [StaFact]
        public void ToggleOpenFilter_OpenAndClosed_AreMutuallyExclusive()
        {
            var vm = new HistoryViewModel();

            vm.ToggleOpenFilter(open: true);   // open only
            vm.ToggleOpenFilter(open: false);  // switches straight to closed only (not "all")
            Assert.False(vm.IsOpenFilter);

            vm.ToggleOpenFilter(open: true);   // switches back to open only
            Assert.True(vm.IsOpenFilter);
        }

        [StaFact]
        public void ClearFilters_ResetsOpenFilterToAll()
        {
            var vm = new HistoryViewModel { IsOpenFilter = true };

            vm.ClearFiltersCommand.Execute(null);

            Assert.Null(vm.IsOpenFilter);
        }

        [StaFact]
        public void Search_WhenEngineNotConnected_FlagsIsDisconnected()
        {
            var vm = new HistoryViewModel();
            Assert.False(vm.IsDisconnected);

            // ClearFilters runs a search synchronously; with no engine attached in tests it must
            // flag the disconnected state (drives the "History unavailable" affordance) rather than
            // silently returning an empty list.
            vm.ClearFiltersCommand.Execute(null);

            Assert.True(vm.IsDisconnected);
        }
    }
}
