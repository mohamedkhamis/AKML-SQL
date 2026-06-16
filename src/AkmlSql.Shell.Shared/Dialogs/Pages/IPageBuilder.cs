#nullable enable
using System.Windows.Controls;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Builds one page of the Options dialog. Implementations are stateless —
    /// each <see cref="Build"/> call appends rows to a host-owned <see cref="StackPanel"/>
    /// (which already has the page header) and returns an <see cref="IPageControls"/>
    /// for Save/Load/Reset.
    /// </summary>
    internal interface IPageBuilder
    {
        /// <summary>Page key used as <c>TreeViewItem.Tag</c> and in Reset / Search lookups.</summary>
        string Key { get; }

        /// <summary>Breadcrumb-style display label used in search results (e.g. "Snippets", "Suggestions › Behavior").</summary>
        string Display { get; }

        /// <summary>Heading text rendered at the top of the page by the host's <c>AddPageHeader</c>.</summary>
        string Title { get; }

        /// <summary>
        /// Page-specific help/intro text (spec 030 T083 / FR-044), rendered by the host beneath the
        /// page header in an accent-bordered block. Every page supplies its own so help coverage is
        /// uniform by construction; return <see cref="string.Empty"/> to render nothing.
        /// </summary>
        string Help { get; }

        /// <summary>
        /// Appends this page's body rows to <paramref name="panel"/> (which the host
        /// has pre-populated with the page header). Returns a controls bag the host
        /// uses to load/save settings for this page.
        /// </summary>
        IPageControls Build(StackPanel panel, PageContext ctx);
    }

    /// <summary>
    /// Per-page handle for moving values between <see cref="AppSettings"/> and the
    /// page's WPF controls. Each page implementation provides its own concrete record.
    /// </summary>
    internal interface IPageControls
    {
        /// <summary>Reads from <paramref name="settings"/> into the page's controls.</summary>
        void Load(AppSettings settings);

        /// <summary>Writes the page's control values back into <paramref name="settings"/>.</summary>
        void Save(AppSettings settings);

        /// <summary>
        /// Restores this page's controls to the values held in <paramref name="defaults"/>
        /// — typically by calling <see cref="Load"/> after the host has reset its
        /// in-memory <see cref="AppSettings"/> sub-object for this page.
        /// </summary>
        void Reset(AppSettings defaults);
    }
}
