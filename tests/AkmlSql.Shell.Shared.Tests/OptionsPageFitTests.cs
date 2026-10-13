#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Dialogs.Pages;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// The fit gate of the 2026-10 "rebalanced tree" (design B): at the Options window's default
    /// size the content column is 591 px wide and 495 px tall. No page, and no tab of a tabbed
    /// page, may run past one and a half screens — before the redesign Suggestions › Behavior and
    /// Inserted code ran almost three.
    /// </summary>
    public class OptionsPageFitTests
    {
        private const double ContentWidth = 591;
        private const double Viewport = 495;
        private const double Limit = Viewport * 1.5;

        [StaFact]
        public void No_page_or_tab_runs_past_one_and_a_half_screens()
        {
            var dialog = new SettingsWindow(new AppSettings { Theme = "Light" });
            _ = dialog.TestBuildWindowForRenderTest();
            var pages = (Dictionary<string, UIElement>)typeof(SettingsWindow)
                .GetField("_pages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
            var builders = (Dictionary<string, IPageBuilder>)typeof(SettingsWindow)
                .GetField("_pageBuilders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;

            var tooTall = new List<string>();
            foreach (var (key, element) in pages.Select(p => (p.Key, p.Value)))
            {
                var content = (FrameworkElement)((ScrollViewer)element).Content;
                if (builders[key] is TabbedPage tabbed && tabbed.Strip != null)
                {
                    for (var i = 0; i < tabbed.Tabs.Count; i++)
                    {
                        tabbed.Strip.Select(i);
                        var height = Measure(content);
                        if (height > Limit) tooTall.Add($"{key} › {tabbed.Tabs[i].Label}: {height:0} px");
                    }
                    tabbed.Strip.Select(0);
                }
                else
                {
                    var height = Measure(content);
                    if (height > Limit) tooTall.Add($"{key}: {height:0} px");
                }
            }

            Assert.True(tooTall.Count == 0, "Pages over " + Limit + " px: " + string.Join("; ", tooTall));
        }

        private static double Measure(FrameworkElement content)
        {
            content.Measure(new Size(ContentWidth, double.PositiveInfinity));
            return Math.Ceiling(content.DesiredSize.Height);
        }
    }
}
