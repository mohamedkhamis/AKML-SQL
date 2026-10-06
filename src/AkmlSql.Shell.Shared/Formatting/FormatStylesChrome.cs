#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using TextElement = System.Windows.Documents.TextElement;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AkmlSql.Shell.Shared.Ui.Theme;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// The Format Styles window's control chrome, painted from theme tokens. The stock Aero
    /// templates drew light-grey buttons, white check boxes, radio buttons and scroll bars and a
    /// solid highlight block over the selected page in the dark theme, and their selection colours
    /// ignored the theme altogether. Each template is a Border-based shape whose triggers carry
    /// DynamicResource references, so it follows a theme change while the window is open.
    /// <para>
    /// The selection and hover tints are a pale wash of the accent in Light and Dark, read with
    /// <see cref="ThemeTokens.TextPrimary"/>; in High Contrast both tints are the system highlight,
    /// which pairs with <see cref="ThemeTokens.TextOnAccent"/> — so the row templates are built per
    /// window, for the variant it opens under.
    /// </para>
    /// </summary>
    internal static class FormatStylesChrome
    {
        /// <summary>Glyph font for the search and information icons (inbox on Windows 10 and later).</summary>
        internal static readonly FontFamily IconFont = new FontFamily("Segoe MDL2 Assets");

        internal const string SearchGlyph = "";
        internal const string InfoGlyph = "";

        private static readonly RotateTransform Expanded = Frozen(new RotateTransform(90));

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        /// <summary>
        /// True under High Contrast, where the selection and hover tints are the system highlight:
        /// pills there are outlined and take their row's text colour instead of a tint of their own.
        /// </summary>
        internal static bool IsHighContrast => ThemeRegistry.Instance.Current == ThemeVariant.HighContrast;

        /// <summary>The token for text on a selection or hover tint (see the class remarks).</summary>
        private static string TintText => IsHighContrast ? ThemeTokens.TextOnAccent : ThemeTokens.TextPrimary;

        /// <summary>Check boxes, radio buttons and scroll bars for everything in <paramref name="root"/>.</summary>
        internal static void ApplyImplicitStyles(FrameworkElement root)
        {
            root.Resources[typeof(CheckBox)] = CheckBoxStyle;
            root.Resources[typeof(RadioButton)] = RadioButtonStyle;
            root.Resources[typeof(ScrollBar)] = ScrollBarStyle;

            // The stock ScrollViewer fills the square where its two bars meet with the system
            // control colour — a light-grey block in the dark theme. It reads that colour as a
            // resource, so the panel colour stands in for it here, kept current on a theme change
            // while the window is open.
            void PaintCorner(object? sender, System.EventArgs e) =>
                root.Resources[SystemColors.ControlBrushKey] = ThemeRegistry.Instance.Resources[ThemeTokens.SurfacePanel];
            PaintCorner(null, System.EventArgs.Empty);
            ThemeRegistry.Instance.VariantChanged += PaintCorner;
            if (root is Window window) window.Closed += (_, _) => ThemeRegistry.Instance.VariantChanged -= PaintCorner;
        }

        // ── Option tree ─────────────────────────────────────────────────────

        /// <summary>
        /// A tree row: a chevron that expands a category, the header, and a rounded full-row tint
        /// with an accent bar on the selected page. Indentation is each item's own
        /// <see cref="Control.Padding"/>, so the tint spans the whole row at every depth.
        /// </summary>
        internal static Style TreeItemStyle()
        {
            var bd = new FrameworkElementFactory(typeof(Border), "Bd");
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            bd.SetValue(FrameworkElement.MinHeightProperty, 24.0);
            bd.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 1));
            bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var dock = new FrameworkElementFactory(typeof(DockPanel));
            dock.SetValue(DockPanel.LastChildFillProperty, true);

            var expander = new FrameworkElementFactory(typeof(ToggleButton), "Expander");
            expander.SetValue(DockPanel.DockProperty, Dock.Left);
            expander.SetValue(Control.TemplateProperty, ChevronTemplate);
            expander.SetValue(UIElement.FocusableProperty, false);
            expander.SetValue(FrameworkElement.WidthProperty, 16.0);
            expander.SetValue(FrameworkElement.HeightProperty, 16.0);
            expander.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0));
            expander.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            expander.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(TreeViewItem.IsExpanded))
            {
                RelativeSource = RelativeSource.TemplatedParent,
                Mode = BindingMode.TwoWay,
            });
            dock.AppendChild(expander);

            var header = new FrameworkElementFactory(typeof(ContentPresenter), "PART_Header");
            header.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            header.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            dock.AppendChild(header);
            bd.AppendChild(dock);

            var bar = AccentBar();
            var items = new FrameworkElementFactory(typeof(ItemsPresenter), "ItemsHost");
            items.SetValue(Grid.RowProperty, 1);

            var root = new FrameworkElementFactory(typeof(Grid));
            var row0 = new FrameworkElementFactory(typeof(RowDefinition));
            row0.SetValue(RowDefinition.HeightProperty, GridLength.Auto);
            var row1 = new FrameworkElementFactory(typeof(RowDefinition));
            row1.SetValue(RowDefinition.HeightProperty, GridLength.Auto);
            root.AppendChild(row0);
            root.AppendChild(row1);
            root.AppendChild(bd);
            root.AppendChild(bar);
            root.AppendChild(items);

            var template = new ControlTemplate(typeof(TreeViewItem)) { VisualTree = root };

            var collapsed = new Trigger { Property = TreeViewItem.IsExpandedProperty, Value = false };
            collapsed.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "ItemsHost"));
            template.Triggers.Add(collapsed);

            var leaf = new Trigger { Property = ItemsControl.HasItemsProperty, Value = false };
            leaf.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "Expander"));
            template.Triggers.Add(leaf);

            AddRowStateTriggers(template, TreeViewItem.IsSelectedProperty);

            template.Seal();

            var style = new Style(typeof(TreeViewItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            return style;
        }

        /// <summary>
        /// The style list's rows: the same rounded tint, accent bar and focus ring as the option
        /// tree, so the two lists read as one family.
        /// </summary>
        internal static Style ListItemStyle()
        {
            var bd = new FrameworkElementFactory(typeof(Border), "Bd");
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            bd.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(content);

            var root = new FrameworkElementFactory(typeof(Grid));
            root.AppendChild(bd);
            root.AppendChild(AccentBar());

            var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = root };
            AddRowStateTriggers(template, ListBoxItem.IsSelectedProperty);
            template.Seal();

            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(Spacing.Sm + 2, 5, Spacing.Xs, 5)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            return style;
        }

        /// <summary>The 3 px accent bar at the left of a selected row (hidden until selected).</summary>
        private static FrameworkElementFactory AccentBar()
        {
            var bar = new FrameworkElementFactory(typeof(Border), "Bar");
            bar.SetValue(FrameworkElement.WidthProperty, 3.0);
            bar.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            bar.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 7, 0, 7));
            bar.SetValue(Border.CornerRadiusProperty, new CornerRadius(1.5));
            bar.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
            bar.SetValue(UIElement.IsHitTestVisibleProperty, false);
            bar.SetResourceReference(Border.BackgroundProperty, ThemeTokens.AccentPrimary);
            return bar;
        }

        /// <summary>Hover, selected and keyboard-focus states shared by the tree and list rows.</summary>
        private static void AddRowStateTriggers(ControlTemplate template, DependencyProperty isSelected)
        {
            // Hover on the row itself — a tree item's IsMouseOver is also true over its children.
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, SourceName = "Bd", Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(ThemeTokens.SurfaceHover), "Bd"));
            hover.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension(TintText), "Bd"));
            template.Triggers.Add(hover);

            var selected = new Trigger { Property = isSelected, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(ThemeTokens.SurfaceSelection), "Bd"));
            selected.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension(TintText), "Bd"));
            selected.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Bar"));
            template.Triggers.Add(selected);

            var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(ThemeTokens.BorderFocus), "Bd"));
            template.Triggers.Add(focused);
        }

        /// <summary>The tree's expand chevron: points right, turns down when expanded.</summary>
        private static readonly ControlTemplate ChevronTemplate = BuildChevronTemplate();

        private static ControlTemplate BuildChevronTemplate()
        {
            var hit = new FrameworkElementFactory(typeof(Border));
            hit.SetValue(Border.BackgroundProperty, Brushes.Transparent);

            var arrow = new FrameworkElementFactory(typeof(Path), "Arrow");
            arrow.SetValue(Path.DataProperty, Frozen(Geometry.Parse("M 6,4 L 10,8 L 6,12")));
            arrow.SetValue(Shape.StrokeThicknessProperty, 1.5);
            arrow.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            arrow.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            arrow.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Round);
            arrow.SetValue(FrameworkElement.WidthProperty, 16.0);
            arrow.SetValue(FrameworkElement.HeightProperty, 16.0);
            arrow.SetValue(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
            arrow.SetResourceReference(Shape.StrokeProperty, ThemeTokens.TextSecondary);
            hit.AppendChild(arrow);

            var template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = hit };
            var open = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            open.Setters.Add(new Setter(UIElement.RenderTransformProperty, Expanded, "Arrow"));
            template.Triggers.Add(open);
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Shape.StrokeProperty, new DynamicResourceExtension(ThemeTokens.TextPrimary), "Arrow"));
            template.Triggers.Add(hover);
            template.Seal();
            return template;
        }

        // ── Small buttons ───────────────────────────────────────────────────

        /// <summary>
        /// A borderless glyph button (↺ reset, ▲/▼ steppers): muted glyph, a soft tint under the
        /// mouse, faded when disabled.
        /// </summary>
        internal static void ApplyIconButton(ButtonBase button)
        {
            button.Template = IconButtonTemplate;
            button.Background = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.SetResourceReference(Control.ForegroundProperty, ThemeTokens.TextSecondary);
        }

        private static readonly ControlTemplate IconButtonTemplate = BuildIconButtonTemplate();

        private static ControlTemplate BuildIconButtonTemplate()
        {
            var bd = new FrameworkElementFactory(typeof(Border), "Bd");
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            bd.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            bd.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(content);

            var template = new ControlTemplate(typeof(ButtonBase)) { VisualTree = bd };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(ThemeTokens.SurfaceHover), "Bd"));
            hover.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension(TintText), "Bd"));
            template.Triggers.Add(hover);
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(ThemeTokens.SurfaceElevated), "Bd"));
            template.Triggers.Add(pressed);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4, "Bd"));
            template.Triggers.Add(disabled);
            template.Seal();
            return template;
        }

        /// <summary>A small rounded label ("ACTIVE", "Modified", a page's change count).</summary>
        internal static Border Pill(TextBlock text, string? backgroundToken, string? borderToken, string foregroundToken)
        {
            text.FontFamily = Typography.UiFont;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.SetResourceReference(TextBlock.ForegroundProperty, foregroundToken);
            var pill = new Border
            {
                Child = text,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 0, 6, 1),
                BorderThickness = new Thickness(borderToken == null ? 0 : 1),
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true,
            };
            if (backgroundToken != null) pill.SetResourceReference(Border.BackgroundProperty, backgroundToken);
            if (borderToken != null) pill.SetResourceReference(Border.BorderBrushProperty, borderToken);
            return pill;
        }

        // ── Check boxes and radio buttons ──────────────────────────────────

        private static readonly Style CheckBoxStyle = BuildCheckBoxStyle();

        /// <summary>
        /// A 16 px rounded box beside the content: input surface and a muted border, filled with the
        /// accent and a white tick when checked. The box sits at the first line, so a wrapped label
        /// reads from beside it.
        /// </summary>
        private static Style BuildCheckBoxStyle()
        {
            var root = new FrameworkElementFactory(typeof(Grid), "Root");
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent); // the whole row toggles
            var col0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col0.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            var col1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            col1.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            root.AppendChild(col0);
            root.AppendChild(col1);

            var box = new FrameworkElementFactory(typeof(Border), "Box");
            box.SetValue(FrameworkElement.WidthProperty, 16.0);
            box.SetValue(FrameworkElement.HeightProperty, 16.0);
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
            box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 0));
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            box.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            box.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfaceInput);
            box.SetResourceReference(Border.BorderBrushProperty, ThemeTokens.TextDisabled);

            var tick = new FrameworkElementFactory(typeof(Path), "Tick");
            tick.SetValue(Path.DataProperty, Frozen(Geometry.Parse("M 2.5,6.5 L 5.5,9.5 L 10.5,3.5")));
            tick.SetValue(Shape.StrokeThicknessProperty, 2.0);
            tick.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            tick.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            tick.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Round);
            tick.SetValue(FrameworkElement.MarginProperty, new Thickness(0.5, 0.5, 0, 0));
            tick.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            tick.SetResourceReference(Shape.StrokeProperty, ThemeTokens.TextOnAccent);
            box.AppendChild(tick);
            root.AppendChild(box);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(Grid.ColumnProperty, 1);
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            root.AppendChild(content);

            var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = root };
            AddToggleTriggers(template, checkedSetters: new[]
            {
                new Setter(Border.BackgroundProperty, new DynamicResourceExtension(ThemeTokens.AccentPrimary), "Box"),
                new Setter(Border.BorderBrushProperty, new DynamicResourceExtension(ThemeTokens.AccentPrimary), "Box"),
                new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Tick"),
            }, hoverTarget: "Box", hoverProperty: Border.BorderBrushProperty, disabledTarget: "Box");
            template.Seal();

            var style = new Style(typeof(CheckBox));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            style.Seal();
            return style;
        }

        private static readonly Style RadioButtonStyle = BuildRadioButtonStyle();

        /// <summary>A 14 px ring beside the content, accent ring and dot when checked.</summary>
        private static Style BuildRadioButtonStyle()
        {
            var root = new FrameworkElementFactory(typeof(StackPanel), "Root");
            root.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);

            var mark = new FrameworkElementFactory(typeof(Grid));
            mark.SetValue(FrameworkElement.WidthProperty, 14.0);
            mark.SetValue(FrameworkElement.HeightProperty, 14.0);
            mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var ring = new FrameworkElementFactory(typeof(Ellipse), "Ring");
            ring.SetValue(Shape.StrokeThicknessProperty, 1.5);
            ring.SetResourceReference(Shape.StrokeProperty, ThemeTokens.TextDisabled);
            ring.SetResourceReference(Shape.FillProperty, ThemeTokens.SurfaceInput);
            mark.AppendChild(ring);

            var dot = new FrameworkElementFactory(typeof(Ellipse), "Dot");
            dot.SetValue(FrameworkElement.WidthProperty, 6.0);
            dot.SetValue(FrameworkElement.HeightProperty, 6.0);
            dot.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            dot.SetResourceReference(Shape.FillProperty, ThemeTokens.AccentPrimary);
            mark.AppendChild(dot);
            root.AppendChild(mark);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 0, 0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            root.AppendChild(content);

            var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = root };
            AddToggleTriggers(template, checkedSetters: new[]
            {
                new Setter(Shape.StrokeProperty, new DynamicResourceExtension(ThemeTokens.AccentPrimary), "Ring"),
                new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Dot"),
            }, hoverTarget: "Ring", hoverProperty: Shape.StrokeProperty, disabledTarget: "Root");
            template.Seal();

            var style = new Style(typeof(RadioButton));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Seal();
            return style;
        }

        private static void AddToggleTriggers(ControlTemplate template, Setter[] checkedSetters,
            string hoverTarget, DependencyProperty hoverProperty, string disabledTarget)
        {
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(hoverProperty, new DynamicResourceExtension(ThemeTokens.AccentPrimary), hoverTarget));
            template.Triggers.Add(hover);

            var isChecked = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            foreach (var setter in checkedSetters) isChecked.Setters.Add(setter);
            template.Triggers.Add(isChecked);

            // A check box's label carries its own disabled colour; a radio button's text is plain
            // content, so the whole control fades with it.
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45, disabledTarget));
            template.Triggers.Add(disabled);
        }

        // ── Scroll bars ─────────────────────────────────────────────────────

        private static readonly Style ScrollBarStyle = BuildScrollBarStyle();

        /// <summary>
        /// A slim 10 px bar: no arrow buttons, a transparent track and a rounded muted thumb that
        /// darkens under the mouse. Clicking the track pages as before.
        /// </summary>
        private static Style BuildScrollBarStyle()
        {
            var style = new Style(typeof(ScrollBar));
            style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
            style.Setters.Add(new Setter(Control.TemplateProperty, BarTemplate(typeof(SlimVerticalTrack))));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 10.0));
            style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 10.0));

            var horizontal = new Trigger { Property = ScrollBar.OrientationProperty, Value = Orientation.Horizontal };
            horizontal.Setters.Add(new Setter(Control.TemplateProperty, BarTemplate(typeof(SlimHorizontalTrack))));
            horizontal.Setters.Add(new Setter(FrameworkElement.WidthProperty, double.NaN));
            horizontal.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 0.0));
            horizontal.Setters.Add(new Setter(FrameworkElement.HeightProperty, 10.0));
            horizontal.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 10.0));
            style.Triggers.Add(horizontal);
            style.Seal();
            return style;
        }

        private static ControlTemplate BarTemplate(System.Type trackType)
        {
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            root.AppendChild(new FrameworkElementFactory(trackType, "PART_Track"));
            var template = new ControlTemplate(typeof(ScrollBar)) { VisualTree = root };
            template.Seal();
            return template;
        }

        internal static readonly ControlTemplate ThumbTemplate = BuildThumbTemplate();

        private static ControlTemplate BuildThumbTemplate()
        {
            var face = new FrameworkElementFactory(typeof(Border), "Face");
            face.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            face.SetValue(FrameworkElement.MarginProperty, new Thickness(2));
            face.SetValue(UIElement.OpacityProperty, 0.5);
            face.SetResourceReference(Border.BackgroundProperty, ThemeTokens.TextDisabled);

            var template = new ControlTemplate(typeof(Thumb)) { VisualTree = face };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.8, "Face"));
            template.Triggers.Add(hover);
            var dragging = new Trigger { Property = Thumb.IsDraggingProperty, Value = true };
            dragging.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "Face"));
            template.Triggers.Add(dragging);
            template.Seal();
            return template;
        }

        internal static readonly ControlTemplate PageButtonTemplate = BuildPageButtonTemplate();

        private static ControlTemplate BuildPageButtonTemplate()
        {
            var hit = new FrameworkElementFactory(typeof(Border));
            hit.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var template = new ControlTemplate(typeof(RepeatButton)) { VisualTree = hit };
            template.Seal();
            return template;
        }
    }

    /// <summary>
    /// A scroll bar track that makes its own thumb and page buttons: a code-built template can't
    /// give a <see cref="Track"/> those parts (they are plain properties, not children), so the
    /// track creates them, one set per scroll bar.
    /// </summary>
    internal abstract class SlimTrack : Track
    {
        protected SlimTrack(ICommand pageBack, ICommand pageForward)
        {
            Thumb = new Thumb { Template = FormatStylesChrome.ThumbTemplate };
            DecreaseRepeatButton = PageButton(pageBack);
            IncreaseRepeatButton = PageButton(pageForward);
        }

        private static RepeatButton PageButton(ICommand command) => new RepeatButton
        {
            Command = command,
            Focusable = false,
            IsTabStop = false,
            Template = FormatStylesChrome.PageButtonTemplate,
        };
    }

    internal sealed class SlimVerticalTrack : SlimTrack
    {
        public SlimVerticalTrack() : base(ScrollBar.PageUpCommand, ScrollBar.PageDownCommand)
        {
            Orientation = Orientation.Vertical;
            IsDirectionReversed = true;
            Thumb.MinHeight = 24;
        }
    }

    internal sealed class SlimHorizontalTrack : SlimTrack
    {
        public SlimHorizontalTrack() : base(ScrollBar.PageLeftCommand, ScrollBar.PageRightCommand)
        {
            Orientation = Orientation.Horizontal;
            Thumb.MinWidth = 24;
        }
    }
}
