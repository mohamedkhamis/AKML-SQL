#nullable enable
using System;
using System.Windows;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Per-build context passed to an <see cref="IPageBuilder"/>: theme brushes,
    /// the live <see cref="AppSettings"/> reference, the row factory (zebra striping
    /// + Add* helpers), and the search-registration callback the host owns.
    ///
    /// <see cref="RowFactory"/>'s Add* helpers already register search entries
    /// internally, so most page builders won't call <see cref="RegisterSearch"/>
    /// directly — it's exposed for the rare case (e.g. custom non-row UI that
    /// still needs to be findable from the search box).
    /// </summary>
    internal sealed class PageContext
    {
        public PageContext(
            PageTheme theme,
            AppSettings settings,
            RowFactory rows,
            Action<string, string, string, FrameworkElement> registerSearch)
        {
            Theme = theme;
            Settings = settings;
            Rows = rows;
            RegisterSearch = registerSearch;
        }

        public PageTheme Theme { get; }

        public AppSettings Settings { get; }

        public RowFactory Rows { get; }

        /// <summary>
        /// Registers a setting in the search index. Signature matches the host's
        /// internal <c>RegisterSearchEntry(label, description, kind, row)</c>.
        /// </summary>
        public Action<string, string, string, FrameworkElement> RegisterSearch { get; }
    }
}
