#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    internal sealed class NavigationPage : IPageBuilder
    {
        public string Key     => "Navigation";
        public string Display => "Editor › Navigation";
        public string Title   => "Navigation";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            var (rowGoTo, chkGoTo) = ctx.Rows.AddToggle(panel,
                "Go to Definition", "Enable Go to Definition (F12)");
            ctx.RegisterSearch("Go to Definition", "Enable Go to Definition (F12)", "Toggle", rowGoTo);

            var (rowPeek, chkPeek) = ctx.Rows.AddToggle(panel,
                "Peek Definition", "Enable Peek Definition (Alt+F12)");
            ctx.RegisterSearch("Peek Definition", "Enable Peek Definition (Alt+F12)", "Toggle", rowPeek);

            var (rowFindRefs, chkFindRefs) = ctx.Rows.AddToggle(panel,
                "Find All References", "Enable Find All References (Shift+F12)");
            ctx.RegisterSearch("Find All References", "Enable Find All References (Shift+F12)", "Toggle", rowFindRefs);

            var (rowObjSearch, chkObjSearch) = ctx.Rows.AddToggle(panel,
                "Object Search", "Enable Object Search (Ctrl+T)");
            ctx.RegisterSearch("Object Search", "Enable Object Search (Ctrl+T)", "Toggle", rowObjSearch);

            return new NavigationControls(chkGoTo, chkPeek, chkFindRefs, chkObjSearch);
        }
    }

    internal sealed class NavigationControls : IPageControls
    {
        private readonly CheckBox _goTo;
        private readonly CheckBox _peek;
        private readonly CheckBox _findRefs;
        private readonly CheckBox _objSearch;

        public NavigationControls(CheckBox goTo, CheckBox peek, CheckBox findRefs, CheckBox objSearch)
        {
            _goTo = goTo;
            _peek = peek;
            _findRefs = findRefs;
            _objSearch = objSearch;
        }

        public void Load(AppSettings settings)
        {
            var nav = settings.Navigation;
            _goTo.IsChecked = nav.GoToDefinition;
            _peek.IsChecked = nav.PeekDefinition;
            _findRefs.IsChecked = nav.FindReferences;
            _objSearch.IsChecked = nav.ObjectSearch;
        }

        public void Save(AppSettings settings)
        {
            settings.Navigation.GoToDefinition = _goTo.IsChecked == true;
            settings.Navigation.PeekDefinition = _peek.IsChecked == true;
            settings.Navigation.FindReferences = _findRefs.IsChecked == true;
            settings.Navigation.ObjectSearch = _objSearch.IsChecked == true;
        }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
