using System.Windows;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Theme-independent font definitions. Hoisted as <c>static readonly</c> per the
    /// <c>CLAUDE.md</c> WPF UI conventions — never instantiate <c>FontFamily</c> per call site.
    /// Surfaces consume these directly: <c>FontFamily = Typography.UiFont</c>, <c>FontSize = Typography.Body</c>.
    /// </summary>
    public static class Typography
    {
        public static readonly FontFamily UiFont   = new FontFamily("Segoe UI");
        public static readonly FontFamily MonoFont = new FontFamily("Consolas");

        // Sizes (DIPs).
        public static readonly double Small       = 11.0;
        public static readonly double Body        = 12.5;
        public static readonly double BodyStrong  = 13.0;
        public static readonly double H4          = 14.0;
        public static readonly double H3          = 16.0;
        public static readonly double H2          = 19.0;
        public static readonly double H1          = 22.0;

        // Weights.
        public static readonly FontWeight WeightRegular  = FontWeights.Regular;
        public static readonly FontWeight WeightSemiBold = FontWeights.SemiBold;
        public static readonly FontWeight WeightBold     = FontWeights.Bold;
    }
}
