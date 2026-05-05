namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Theme-independent pixel scale used for <c>Margin</c>, <c>Padding</c>, and grid gaps.
    /// Replaces ad-hoc magic numbers across the shell. Use the named constants instead of literals:
    /// <c>new Thickness(Spacing.Md)</c>, <c>new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, Spacing.Sm)</c>.
    /// </summary>
    public static class Spacing
    {
        public static readonly double Xs  = 4.0;
        public static readonly double Sm  = 8.0;
        public static readonly double Md  = 12.0;
        public static readonly double Lg  = 16.0;
        public static readonly double Xl  = 24.0;
        public static readonly double Xxl = 32.0;
    }
}
