#nullable enable
using System.Windows;
using AkmlSql.Core.Config;

namespace AkmlSql.Shell.Shared.Dialogs.Pages
{
    /// <summary>
    /// Builds one page of the Options dialog. Implementations are stateless —
    /// each <see cref="Build"/> call produces a fresh <see cref="UIElement"/>
    /// and a corresponding <see cref="IPageControls"/> for Save/Load/Reset.
    /// </summary>
    internal interface IPageBuilder
    {
        /// <summary>Page key used as <c>TreeViewItem.Tag</c> and in Reset / Search lookups.</summary>
        string Key { get; }

        /// <summary>Display label shown in the page header (breadcrumb format).</summary>
        string Display { get; }

        /// <summary>
        /// Constructs the WPF panel + a controls bag the host uses to load/save settings.
        /// </summary>
        (UIElement Element, IPageControls Controls) Build(PageContext ctx);
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
