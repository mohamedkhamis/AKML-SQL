#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Editor › Navigation. Spec 040 (OPT-01): the Go to Definition, Peek Definition, Find All
    /// References and Object Search toggles changed nothing (the commands always run) and are
    /// hidden; their saved values are kept. The page keeps one pointer to where the commands are
    /// until US6 rearranges the tree.
    /// </summary>
    internal sealed class NavigationPage : IPageBuilder
    {
        public string Key     => "Navigation";
        public string Display => "Editor › Navigation";
        public string Title   => "Navigation";
        public string Help    => "Go to Definition (F12), Peek Definition (Alt+F12), Find All References (Shift+F12) and Object Search (Ctrl+T) are always available from the AKML SQL menu.";

        public IPageControls Build(StackPanel panel, PageContext ctx)
        {
            ctx.Rows.AddInfoRow(panel, "Navigation commands", "Navigation commands are in AKML SQL › Navigate.");
            return new NavigationControls();
        }
    }

    internal sealed class NavigationControls : IPageControls
    {
        public void Load(AppSettings settings) { }

        public void Save(AppSettings settings) { }

        public void Reset(AppSettings defaults) => Load(defaults);
    }
}
