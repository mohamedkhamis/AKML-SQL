using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using FlaUI.Core.AutomationElements;

namespace AkmlSql.UiTests.Driver;

/// <summary>
/// Screenshot capture, the desktop counterpart of Playwright's <c>page.screenshot()</c> and
/// <c>locator.screenshot()</c>.
///
/// <para>
/// Everything is captured off the composited screen rather than with <c>PrintWindow</c>. The VS
/// shell renders through DirectComposition, and while <c>PrintWindow</c> with
/// <c>PW_RENDERFULLCONTENT</c> does capture its client area, it silently misses the popups this
/// suite most wants to photograph — completion lists, lightbulb menus and the glyph context menu
/// are separate top-level windows layered over the frame, so they are simply absent from a
/// window-scoped grab. Reading the screen gets what a person would actually see.
/// </para>
///
/// <para>
/// The trade-off is that the target must genuinely be on screen: unobscured, not minimised, on a
/// connected session. <see cref="SsmsWindow.BringToFront"/> handles the first two;
/// <see cref="Preconditions"/> explains the third.
/// </para>
/// </summary>
public static class Shot
{
    /// <summary>Where captures land. Overridable so a docs run can target the site's image folder.</summary>
    public static string ArtifactDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "screenshots");

    /// <summary>Captures the whole virtual desktop.</summary>
    public static string Screen(string name)
    {
        var b = SystemInformation.VirtualScreen;
        return Capture(new Rectangle(b.X, b.Y, b.Width, b.Height), name);
    }

    /// <summary>Captures one automation element — the <c>locator.screenshot()</c> equivalent.</summary>
    public static string Element(AutomationElement element, string name)
    {
        var r = element.BoundingRectangle;
        return Capture(new Rectangle(r.X, r.Y, r.Width, r.Height), name);
    }

    /// <summary>
    /// Captures an element plus <paramref name="pad"/> pixels of surrounding context — useful for a
    /// squiggle or glyph, which means little cropped to its own few pixels.
    /// </summary>
    public static string ElementWithContext(AutomationElement element, string name, int pad = 24)
    {
        var r = element.BoundingRectangle;
        return Capture(new Rectangle(r.X - pad, r.Y - pad, r.Width + pad * 2, r.Height + pad * 2), name);
    }

    /// <summary>
    /// Captures <paramref name="bounds"/>, clamped to the virtual screen.
    /// <para>
    /// The clamp is not defensive padding, it is required. A maximised window on Windows reports a
    /// rectangle inset by the invisible resize border — 1936x1056 at (-8,-8) on a 1920x1080 desktop
    /// — and asking GDI+ to clone a region that starts off-screen throws a bare "Out of memory"
    /// that says nothing about the real cause.
    /// </para>
    /// </summary>
    public static string Capture(Rectangle bounds, string name)
    {
        var screen = SystemInformation.VirtualScreen;
        bounds = Rectangle.Intersect(bounds, screen);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new InvalidOperationException(
                $"Nothing to capture for '{name}': the requested region does not overlap the desktop. " +
                "The window is most likely minimised or on another monitor.");

        Directory.CreateDirectory(ArtifactDirectory);
        var path = Path.Combine(ArtifactDirectory, Sanitize(name) + ".png");

        using var bmp = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bmp.Size);
        }
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>
    /// True when the capture is a single flat colour — the signature of a disconnected RDP session,
    /// where the desktop stops compositing and every grab comes back uniformly black. Worth
    /// asserting early: without it a suite fails later with confusing "element not visible" errors
    /// that point anywhere but at the real cause.
    ///
    /// <para>
    /// The test is deliberately "exactly one colour", not "few colours". An editor holding a single
    /// line of code is legitimately 99% background — a looser threshold flags a perfectly good
    /// screenshot as blank, which is exactly what a first attempt at this did. Sampling is dense so
    /// that a sparse feature is not missed between grid lines.
    /// </para>
    /// </summary>
    public static bool LooksBlank(string pngPath)
    {
        using var bmp = new Bitmap(pngPath);
        var first = bmp.GetPixel(0, 0).ToArgb();
        var step = Math.Max(1, Math.Min(bmp.Width, bmp.Height) / 200);

        for (var x = 0; x < bmp.Width; x += step)
        for (var y = 0; y < bmp.Height; y += step)
        {
            if (bmp.GetPixel(x, y).ToArgb() != first) return false;
        }
        return true;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        return name;
    }
}
