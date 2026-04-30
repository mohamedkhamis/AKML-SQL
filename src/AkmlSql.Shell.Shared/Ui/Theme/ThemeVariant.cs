namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Active palette variant resolved by <see cref="ThemeRegistry"/>.
    /// Replaces the older <c>ThemeManager.VsThemeKind</c> (which had a never-fully-implemented "Blue" value).
    /// </summary>
    public enum ThemeVariant
    {
        Light,
        Dark,
        HighContrast,
    }
}
