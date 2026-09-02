using System;
using System.Windows;
using System.Windows.Threading;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Singleton authority for the active theme palette. Holds a <see cref="ResourceDictionary"/>
    /// that AKML-owned <see cref="Window"/> and <see cref="System.Windows.Controls.UserControl"/>
    /// instances merge into their own <c>Resources</c> via <see cref="AttachTo"/>. Surfaces consume
    /// tokens via <c>FrameworkElement.SetResourceReference(prop, ThemeTokens.Foo)</c>.
    /// On variant change the registry replaces brushes in the dictionary by key; WPF's resource
    /// lookup re-resolves every <c>DynamicResource</c> consumer automatically.
    /// </summary>
    public sealed class ThemeRegistry
    {
        private static readonly object SLock = new object();
        private static ThemeRegistry _instance;

        public static ThemeRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (SLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new ThemeRegistry();
                        }
                    }
                }
                return _instance;
            }
        }

        // Stored inputs to the resolver.
        private string _preference = "light";  // "light" | "dark" | "system"
        private ThemeVariant _hostDetected = ThemeVariant.Light;
        private bool _isHighContrast;

        private bool _initialized;

        /// <summary>
        /// Resource dictionary that surfaces merge into their own <c>Resources</c>. Mutated whole-brush
        /// on variant change; brushes inside remain frozen.
        /// </summary>
        public ResourceDictionary Resources { get; } = new ResourceDictionary();

        /// <summary>The currently-active variant (after preference + host + High Contrast resolution).</summary>
        public ThemeVariant Current { get; private set; } = ThemeVariant.Light;

        /// <summary>Raised after a variant swap completes. Surfaces with imperative work subscribe; surfaces
        /// driven entirely by <c>SetResourceReference</c> do not need to.</summary>
        public event EventHandler VariantChanged;

        private ThemeRegistry() { }

        /// <summary>
        /// Called once at shell-package startup. Reads the user's preference, records an initial
        /// host-detected variant (typically supplied by <c>HostThemeWatcher</c> after its first probe),
        /// and populates the dictionary. Idempotent.
        /// </summary>
        public void Initialize(string preference, ThemeVariant initialHostVariant, bool isHighContrast)
        {
            _preference = NormalizePreference(preference);
            _hostDetected = initialHostVariant;
            _isHighContrast = isHighContrast;
            SeedInvariantTokens();
            ResolveAndApply(raiseEvent: false);
            _initialized = true;
        }

        /// <summary>
        /// Updates the user preference (typically called when the user changes the AKML theme dropdown).
        /// </summary>
        public void SetPreference(string preference)
        {
            _preference = NormalizePreference(preference);
            ResolveAndApply(raiseEvent: true);
        }

        /// <summary>
        /// Notification from <c>HostThemeWatcher</c> that the host VS/SSMS theme has changed.
        /// </summary>
        public void OnHostThemeChanged(ThemeVariant detected)
        {
            _hostDetected = detected;
            ResolveAndApply(raiseEvent: true);
        }

        /// <summary>
        /// Notification from <c>HostThemeWatcher</c> that Windows High Contrast has toggled.
        /// </summary>
        public void OnHighContrastChanged(bool isHighContrast)
        {
            if (_isHighContrast == isHighContrast) return;
            _isHighContrast = isHighContrast;
            ResolveAndApply(raiseEvent: true);
        }

        /// <summary>
        /// Merges this registry's <see cref="Resources"/> into the given element's <c>Resources</c>.
        /// Idempotent — calling it twice on the same element is a no-op.
        /// </summary>
        public void AttachTo(FrameworkElement element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            EnsureInitialized();

            if (element.Resources == null)
            {
                element.Resources = new ResourceDictionary();
            }

            foreach (var existing in element.Resources.MergedDictionaries)
            {
                if (ReferenceEquals(existing, Resources)) return;
            }
            element.Resources.MergedDictionaries.Add(Resources);
        }

        // -------------------------------------------------------------------

        private static string NormalizePreference(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "light";
            var lower = p.Trim().ToLowerInvariant();
            return (lower == "dark" || lower == "system") ? lower : "light";
        }

        private ThemeVariant Resolve()
        {
            if (_isHighContrast) return ThemeVariant.HighContrast;
            switch (_preference)
            {
                case "dark":   return ThemeVariant.Dark;
                case "light":  return ThemeVariant.Light;
                default:       return _hostDetected; // "system"
            }
        }

        private void ResolveAndApply(bool raiseEvent)
        {
            // Marshal to UI thread if called off-thread.
            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            if (!dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => ResolveAndApply(raiseEvent));
                return;
            }

            var newVariant = Resolve();
            if (_initialized && newVariant == Current) return;

            var palette = ThemePalette.ForVariant(newVariant);
            foreach (var kvp in palette.Brushes)
            {
                Resources[kvp.Key] = kvp.Value;
            }
            Current = newVariant;

            if (raiseEvent)
            {
                VariantChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            // Spec 036: serialize + complete the lazy fallback. Before this, EnsureInitialized
            // never set _initialized, so every pre-Initialize AttachTo (e.g. each unit test
            // constructing a ThemeAwareUserControl on its own STA thread) re-wrote the shared
            // dictionary — a cross-thread InvalidOperationException once another thread's
            // control owned it. Production calls all arrive on the VS UI thread; the lock only
            // matters for parallel test threads.
            lock (SLock)
            {
                if (_initialized) return;
                // Lazy fallback — populate Light palette so AttachTo works during early calls
                // (e.g., during a unit test, or if a surface is constructed before Initialize ran).
                var palette = ThemePalette.Light;
                foreach (var kvp in palette.Brushes)
                {
                    if (!Resources.Contains(kvp.Key))
                    {
                        Resources[kvp.Key] = kvp.Value;
                    }
                }
                SeedInvariantTokens();
                Current = ThemeVariant.Light;
                _initialized = true;
            }
        }

        /// <summary>
        /// Spec 020 (SQL Prompt visual parity): seeds non-brush tokens that are invariant across
        /// themes — <c>Spacing.*</c> scalars (DIU) and <c>Typography.*</c> composites
        /// (<see cref="TypographySpec"/>). Idempotent — re-seeding overwrites with identical values.
        /// Called from both <see cref="Initialize"/> and <see cref="EnsureInitialized"/> so the
        /// tokens are present regardless of which path populated the registry first.
        /// <para>
        /// Numeric and font values delegate to the existing <see cref="Spacing"/> and
        /// <see cref="Typography"/> static classes (introduced by spec 016) so there's a single
        /// source of truth for the actual values; the token keys here are the
        /// <c>DynamicResource</c>-addressable façade over them, for XAML and category-aware lookup.
        /// </para>
        /// </summary>
        private void SeedInvariantTokens()
        {
            // Spacing — boxed doubles in ResourceDictionary; consumers cast back to double.
            Resources[ThemeTokens.SpacingXs] = Spacing.Xs;
            Resources[ThemeTokens.SpacingS]  = Spacing.Sm;
            Resources[ThemeTokens.SpacingM]  = Spacing.Md;
            Resources[ThemeTokens.SpacingL]  = Spacing.Lg;

            // Typography — frozen TypographySpec composites; FontFamily references resolved
            // from the hoisted static fields on Typography (CLAUDE.md WPF convention).
            Resources[ThemeTokens.TypographyChrome]      = new TypographySpec(Typography.UiFont,   12.0,           Typography.WeightRegular);
            Resources[ThemeTokens.TypographyChromeTitle] = new TypographySpec(Typography.UiFont,   Typography.H4,  Typography.WeightSemiBold);
            Resources[ThemeTokens.TypographyEditor]      = new TypographySpec(Typography.MonoFont, Typography.Body, Typography.WeightRegular);
            Resources[ThemeTokens.TypographyIconBadge]   = new TypographySpec(Typography.UiFont,   9.0,            Typography.WeightSemiBold);
        }
    }
}
