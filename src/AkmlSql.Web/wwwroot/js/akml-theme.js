// Spec 021 (web edition) -- M2 task T034. Theme bootstrap. Owns:
//   1. OS preference detection via prefers-color-scheme (FR-005).
//   2. The <link rel="stylesheet"> swap that loads the right theme CSS.
//   3. The matchMedia listener that re-applies when the user changes their OS theme.
//
// The Blazor ThemeService.cs calls into this module on startup with the user override
// (null = follow system). The shim then applies the right CSS file and listens for OS
// changes to keep the page in sync when the override is "System".

const THEME_LINK_ID = 'akml-theme-css';
const THEMES = { light: 'css/themes/light.css', dark: 'css/themes/dark.css', 'high-contrast': 'css/themes/high-contrast.css' };

let _osListener = null;
let _currentMode = 'system';

function detectOsTheme() {
    if (typeof window.matchMedia !== 'function') return 'light';
    if (window.matchMedia('(prefers-contrast: more)').matches) return 'high-contrast';
    if (window.matchMedia('(prefers-color-scheme: dark)').matches) return 'dark';
    return 'light';
}

function applyTheme(theme) {
    const href = THEMES[theme] ?? THEMES.light;
    let link = document.getElementById(THEME_LINK_ID);
    if (!link) {
        link = document.createElement('link');
        link.id = THEME_LINK_ID;
        link.rel = 'stylesheet';
        document.head.appendChild(link);
    }
    link.href = href;
    document.documentElement.dataset.akmlTheme = theme;
}

/**
 * Apply a theme mode. Modes: "system" | "light" | "dark" | "high-contrast".
 * When "system" is selected, the page follows prefers-color-scheme and reacts to OS
 * changes until a different mode is chosen.
 */
export function apply(mode) {
    _currentMode = mode ?? 'system';

    // Drop any existing OS listener.
    if (_osListener) {
        window.matchMedia('(prefers-color-scheme: dark)').removeEventListener('change', _osListener);
        window.matchMedia('(prefers-contrast: more)').removeEventListener('change', _osListener);
        _osListener = null;
    }

    if (_currentMode === 'system') {
        const reapply = () => applyTheme(detectOsTheme());
        reapply();
        _osListener = reapply;
        window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', _osListener);
        window.matchMedia('(prefers-contrast: more)').addEventListener('change', _osListener);
    } else {
        applyTheme(_currentMode);
    }
}

/** Read the effective theme (resolved to a concrete file, not "system"). */
export function effective() {
    return _currentMode === 'system' ? detectOsTheme() : _currentMode;
}
