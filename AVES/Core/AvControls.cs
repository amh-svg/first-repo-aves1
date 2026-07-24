using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfGrid = System.Windows.Controls.Grid;
using WpfVisibility = System.Windows.Visibility;

namespace AVES.Core
{
    /// <summary>
    /// Shared AVES widget factory methods, consolidated from the "Av" class
    /// (Cmds_Button7.cs, Cmds_Button8.cs), "AvesUiHelper" (Cmds_PullDown2.cs),
    /// "AvesUI" (Cmds_Pulldown3.cs) and "W" (Cmds_Pulldown4.cs).
    ///
    /// Only widgets shared by two or more of those sources live here. Per the
    /// reviewed decision, Cmds_PullDown.cs's "Av" class turned out to be a much
    /// larger, separately-evolved surface (StyledDataGrid, PillColumn,
    /// SegmentedToggle, RoundedProgressBar, FooterStrip, ConfigRow, PickerButton,
    /// FieldLabel, HairlineSeparator, TextColumn, plus its own BrandTile with
    /// DecodePixelHeight/bicubic scaling and its own width-parameterized button
    /// factories) used by no other file. That surface stays local to
    /// Cmds_PullDown.cs (mirroring how Cmds_Pulldown3's DimType* logic stays
    /// local) rather than being folded in here.
    ///
    /// Where sources genuinely differ in technique (button hover: ControlTemplate
    /// vs. Loaded-event hack; title bar: breadcrumb vs. subtitle vs. header-strip
    /// layout) both variants are kept as separately-named methods rather than
    /// silently collapsed into one, per the "freeze behavior" rule for this
    /// consolidation. See the migration summary for the full list of flagged
    /// differences.
    /// </summary>
    internal static class AvControls
    {
        // ====================================================================
        //  Brand tile
        // ====================================================================

        /// <summary>
        /// Logo image, size in device-independent pixels (Height = size, auto
        /// width). Matches Av's (Button7/Button8) and W's (Pulldown4) plain
        /// decode -- no DecodePixelHeight, no bicubic scaling override.
        /// </summary>
        public static UIElement BrandTile(double size = 20)
        {
            return new Image
            {
                Source = AvTokens.LogoImage,
                Height = size,
                Width = double.NaN,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        // ====================================================================
        //  Title bars (kept as two distinct methods -- see class remarks)
        // ====================================================================

        /// <summary>
        /// Title bar with an optional trailing breadcrumb ("-- breadcrumb").
        /// Matches W's (Cmds_Pulldown4.cs) implementation: Height=44.
        /// </summary>
        public static Border TitleBar(string title, string? breadcrumb = null)
        {
            var brand = BrandTile(20);
            var brandBorder = new Border { Margin = new Thickness(0, 0, 10, 0), Child = brand, VerticalAlignment = VerticalAlignment.Center };
            var lbl = new TextBlock
            {
                Text = title,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.Text),
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(brandBorder);
            row.Children.Add(lbl);
            if (!string.IsNullOrEmpty(breadcrumb))
            {
                row.Children.Add(new TextBlock
                {
                    Text = "— " + breadcrumb,
                    FontFamily = AvTokens.Ui,
                    FontSize = 12,
                    Foreground = AvTokens.B(AvTokens.TextFaint),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            return new Border
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 0, 12, 0),
                Height = 44,
                Child = row
            };
        }

        /// <summary>
        /// Interior header strip (logo + title text, no trailing rule/height cap).
        /// Matches AvesUiHelper's (Cmds_PullDown2.cs) implementation.
        /// </summary>
        public static Border HeaderStrip(string titleText)
        {
            var logo = new Image
            {
                Source = AvTokens.LogoImage,
                Height = 28,
                Width = double.NaN,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                SnapsToDevicePixels = true
            };

            var title = new TextBlock
            {
                Text = titleText,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.Text),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0)
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(logo);
            row.Children.Add(title);

            return new Border
            {
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20, 10, 20, 10),
                Child = row
            };
        }

        // ====================================================================
        //  Section labels / headers
        // ====================================================================

        /// <summary>Plain uppercase + bold + faint section label.</summary>
        public static TextBlock SectionLabel(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontFamily = AvTokens.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = AvTokens.B(AvTokens.TextFaint),
            Margin = new Thickness(0, 0, 0, 10)
        };

        /// <summary>
        /// Section header with an accent left stripe. Matches AvesUiHelper's
        /// (Cmds_PullDown2.cs) implementation -- a visually distinct widget from
        /// SectionLabel, not a restyled duplicate of it.
        /// </summary>
        public static Border SectionHeader(string text)
        {
            var label = new TextBlock
            {
                Text = text.ToUpperInvariant(),
                FontFamily = AvTokens.Ui,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Center
            };

            var inner = new Border
            {
                BorderBrush = AvTokens.B(AvTokens.Accent),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Background = AvTokens.B(AvTokens.Surface2),
                Padding = new Thickness(8, 6, 8, 6),
                Child = label
            };

            return new Border
            {
                Margin = new Thickness(0, 12, 0, 8),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = inner
            };
        }

        // ====================================================================
        //  Number badge / divider
        // ====================================================================

        /// <summary>Circled step number, e.g. used as "(1) Section title".</summary>
        public static Border NumberBadge(int n)
        {
            var t = new TextBlock
            {
                Text = n.ToString(),
                FontFamily = AvTokens.Ui,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = AvTokens.B(AvTokens.Accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = AvTokens.B(AvTokens.AccentTint),
                Margin = new Thickness(0, 0, 7, 0),
                Child = t
            };
        }

        /// <summary>1px horizontal divider. Matches T's and AvesUI's HDivider.</summary>
        public static Border HDivider() => new Border { Height = 1, Background = AvTokens.B(AvTokens.Border) };

        // ====================================================================
        //  Stat tile / status pill
        // ====================================================================

        /// <summary>
        /// Big mono value + faint label tile. accent: "" | "ok" | "fail" | "warn" | "accent".
        /// Matches Av's/T's/AvesUiHelper's sizing (label FontSize 11, value margin
        /// bottom 4). AvesUI's (Pulldown3) copy used FontSize 10.5 / margin-bottom 3
        /// for the label -- a very small spacing difference not reproduced here;
        /// flagged in the migration summary rather than added as a second overload.
        /// </summary>
        public static Border StatTile(string value, string label, string accent = "")
        {
            string valColor = accent switch
            {
                "ok" => AvTokens.OkFg,
                "fail" => AvTokens.FailFg,
                "warn" => AvTokens.WarnFg,
                "accent" => AvTokens.Accent,
                _ => AvTokens.Text,
            };

            var v = new TextBlock
            {
                Text = value,
                FontFamily = AvTokens.Mono,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = AvTokens.B(valColor),
                Margin = new Thickness(0, 0, 0, 4),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = value
            };
            var k = new TextBlock
            {
                Text = label,
                FontFamily = AvTokens.Ui,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.TextFaint)
            };
            var stack = new StackPanel();
            stack.Children.Add(v);
            stack.Children.Add(k);

            return new Border
            {
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(14, 12, 14, 12),
                Child = stack
            };
        }

        /// <summary>
        /// Colored dot + label pill. kind: "ok" | "skip" | "fail" | "warn" | (default = info).
        /// Uses the majority label text (Av/T): Done / Skipped / Failed / Warning / Info.
        /// NOTE: AvesUiHelper's (PullDown2) copy said "Completed with errors" for warn,
        /// and AvesUI's (Pulldown3) copy said "Error" for fail -- both real text
        /// differences, not reproduced here. Flagged in the migration summary for a
        /// separate decision on whether to standardize wording when those two files
        /// migrate, rather than silently resolved now.
        /// </summary>
        public static Border StatusPill(string kind)
        {
            (string bg, string fg, string label) = kind switch
            {
                "ok" => (AvTokens.OkBg, AvTokens.OkFg, "Done"),
                "skip" => (AvTokens.SkipBg, AvTokens.SkipFg, "Skipped"),
                "fail" => (AvTokens.FailBg, AvTokens.FailFg, "Failed"),
                "warn" => (AvTokens.WarnBg, AvTokens.WarnFg, "Warning"),
                _ => (AvTokens.InfoBg, AvTokens.InfoFg, "Info"),
            };

            var dot = new WpfEllipse
            {
                Width = 7,
                Height = 7,
                Fill = AvTokens.B(fg),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            var text = new TextBlock
            {
                Text = label,
                Foreground = AvTokens.B(fg),
                FontFamily = AvTokens.Ui,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(text);

            return new Border
            {
                Background = AvTokens.B(bg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = row
            };
        }

        // ====================================================================
        //  Buttons -- three distinct families, one per hover/wiring technique.
        //  See class remarks: these are NOT collapsed into one shape because
        //  doing so would change either the calling convention (onClick wiring)
        //  or the hover mechanism (ControlTemplate vs. Loaded-event hack).
        // ====================================================================

        // -- Family A: onClick-wired, ControlTemplate via XamlReader.Parse.
        // Matches Av (Cmds_Button7.cs, Cmds_Button8.cs).

        public static Button PrimaryBtn(string label, Action onClick)
        {
            var btn = new Button
            {
                Content = label,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = AvTokens.B(AvTokens.Accent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 9, 16, 9),
                Cursor = Cursors.Hand
            };
            btn.Template = BuildXamlBtnTemplate(AvTokens.Accent, AvTokens.AccentDeep);
            btn.Click += (s, e) => onClick();
            return btn;
        }

        public static Button GhostBtn(string label, Action onClick)
        {
            var btn = new Button
            {
                Content = label,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.TextDim),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(14, 9, 14, 9),
                Cursor = Cursors.Hand
            };
            btn.Template = BuildXamlBtnTemplate("Transparent", AvTokens.Surface2);
            btn.Click += (s, e) => onClick();
            return btn;
        }

        public static Button SecondaryBtn(string label, Action onClick)
        {
            var btn = new Button
            {
                Content = label,
                FontFamily = AvTokens.Ui,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.TextDim),
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 5, 10, 5),
                Cursor = Cursors.Hand
            };
            const string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='Button'>
  <Border x:Name='bd' CornerRadius='5'
          Background='{TemplateBinding Background}'
          BorderBrush='{TemplateBinding BorderBrush}'
          BorderThickness='{TemplateBinding BorderThickness}'>
    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'
                      Margin='{TemplateBinding Padding}'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'>
      <Setter TargetName='bd' Property='Background' Value='#FAFAFB'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";
            btn.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
            btn.Click += (s, e) => onClick();
            return btn;
        }

        private static ControlTemplate BuildXamlBtnTemplate(string normalBg, string hoverBg)
        {
            const string xaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                 TargetType='Button'>
  <Border x:Name='bd' CornerRadius='5'
          Background='{TemplateBinding Background}'>
    <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'
                      Margin='{TemplateBinding Padding}'/>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'>
      <Setter TargetName='bd' Property='Background' Value='__HOVER__'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";
            var filled = xaml.Replace("__HOVER__", hoverBg);
            return (ControlTemplate)System.Windows.Markup.XamlReader.Parse(filled);
        }

        // -- Family B: no onClick wiring, ControlTemplate via FrameworkElementFactory.
        // Matches W (Cmds_Pulldown4.cs) and AvesUI (Cmds_Pulldown3.cs) -- both used
        // the identical technique (verified: same Border/ContentPresenter/hover-trigger
        // shape), so they safely collapse into one implementation.

        public static Button PrimaryButton(string text) => new Button
        {
            Content = text,
            FontFamily = AvTokens.Ui,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = AvTokens.B(AvTokens.Accent),
            Padding = new Thickness(16, 9, 16, 9),
            Cursor = Cursors.Hand,
            Template = FactoryBtnTemplate(AvTokens.Accent, AvTokens.AccentDeep, null, 0)
        };

        public static Button SecondaryButton(string text) => new Button
        {
            Content = text,
            FontFamily = AvTokens.Ui,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = AvTokens.B(AvTokens.Text),
            Background = AvTokens.B(AvTokens.Surface),
            Padding = new Thickness(16, 9, 16, 9),
            Cursor = Cursors.Hand,
            Template = FactoryBtnTemplate(AvTokens.Surface, AvTokens.Surface2, AvTokens.BorderStrong, 1)
        };

        public static Button GhostButton(string text) => new Button
        {
            Content = text,
            FontFamily = AvTokens.Ui,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = AvTokens.B(AvTokens.TextDim),
            Background = Brushes.Transparent,
            Padding = new Thickness(14, 9, 14, 9),
            Cursor = Cursors.Hand,
            Template = FactoryBtnTemplate(null, AvTokens.Surface2, null, 0)
        };

        /// <summary>
        /// Rounded, bordered, hover-highlighted look shared by SecondaryButton and
        /// any clickable "field" control (e.g. a picker trigger). Matches W's
        /// FieldButtonTemplate (Cmds_Pulldown4.cs).
        /// </summary>
        public static ControlTemplate FieldButtonTemplate() =>
            FactoryBtnTemplate(AvTokens.Surface, AvTokens.Surface2, AvTokens.BorderStrong, 1);

        private static ControlTemplate FactoryBtnTemplate(
            string? normalBg, string hoverBg, string? borderHex, double borderPx)
        {
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            bd.SetValue(Border.BackgroundProperty,
                normalBg != null ? (object)AvTokens.B(normalBg) : Brushes.Transparent);
            if (borderHex != null)
            {
                bd.SetValue(Border.BorderBrushProperty, AvTokens.B(borderHex));
                bd.SetValue(Border.BorderThicknessProperty, new Thickness(borderPx));
            }
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetBinding(ContentPresenter.MarginProperty,
                new System.Windows.Data.Binding("Padding")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.TemplatedParent)
                });
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            var trigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, AvTokens.B(hoverBg), "bd"));
            tpl.Triggers.Add(trigger);
            return tpl;
        }

        // -- Family C: no onClick wiring, hover via Loaded-event hack (no
        // ControlTemplate at all). Matches AvesUiHelper (Cmds_PullDown2.cs).
        // Kept as its own family per the brief: this is a real technique
        // difference, not merely a styling one, and must not be silently
        // swapped for the ControlTemplate approach used elsewhere.

        public static Button PrimaryButtonHoverLoaded(string content)
        {
            // Dead code in the original AvesUiHelper.PrimaryButton, preserved
            // verbatim for exact parity: builds a Border+hover wiring that is
            // never attached to the returned Button's visual tree.
            var deadBorder = new Border
            {
                Background = AvTokens.B(AvTokens.Accent),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(16, 9, 16, 9),
                Cursor = Cursors.Hand,
                Child = new ContentPresenter()
            };
            ApplyHover(deadBorder, AvTokens.Accent, AvTokens.AccentDeep);

            var btn = new Button
            {
                Content = content,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(16, 9, 16, 9),
                Cursor = Cursors.Hand
            };
            StyleButtonAsPrimaryLoaded(btn);
            return btn;
        }

        public static Button SecondaryButtonHoverLoaded(string content)
        {
            var btn = new Button
            {
                Content = content,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.Text),
                Padding = new Thickness(16, 9, 16, 9),
                Cursor = Cursors.Hand
            };
            StyleButtonAsSecondaryLoaded(btn);
            return btn;
        }

        public static Button GhostButtonHoverLoaded(string content)
        {
            var btn = new Button
            {
                Content = content,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.TextDim),
                Padding = new Thickness(14, 9, 14, 9),
                Cursor = Cursors.Hand
            };
            StyleButtonAsGhostLoaded(btn);
            return btn;
        }

        public static void ApplyHover(Border bd, string normalHex, string hoverHex)
        {
            var normal = AvTokens.B(normalHex);
            var hover = AvTokens.B(hoverHex);
            bd.MouseEnter += (_, _) => bd.Background = hover;
            bd.MouseLeave += (_, _) => bd.Background = normal;
        }

        private static void StyleButtonAsPrimaryLoaded(Button btn)
        {
            var normalBg = AvTokens.B(AvTokens.Accent);
            var hoverBg = AvTokens.B(AvTokens.AccentDeep);
            btn.Foreground = Brushes.White;
            btn.Background = normalBg;
            btn.BorderThickness = new Thickness(0);
            btn.Padding = new Thickness(16, 9, 16, 9);
            btn.Cursor = Cursors.Hand;
            ApplyRoundedStyleLoaded(btn, normalBg, hoverBg, null, new Thickness(0));
        }

        private static void StyleButtonAsSecondaryLoaded(Button btn)
        {
            var normalBg = AvTokens.B(AvTokens.Surface);
            var hoverBg = AvTokens.B(AvTokens.Surface2);
            btn.Foreground = AvTokens.B(AvTokens.Text);
            btn.Background = normalBg;
            btn.BorderBrush = AvTokens.B(AvTokens.BorderStrong);
            btn.BorderThickness = new Thickness(1);
            btn.Padding = new Thickness(16, 9, 16, 9);
            btn.Cursor = Cursors.Hand;
            ApplyRoundedStyleLoaded(btn, normalBg, hoverBg, AvTokens.B(AvTokens.BorderStrong), new Thickness(1));
        }

        private static void StyleButtonAsGhostLoaded(Button btn)
        {
            var normalBg = new SolidColorBrush(Colors.Transparent);
            var hoverBg = AvTokens.B(AvTokens.Surface2);
            btn.Foreground = AvTokens.B(AvTokens.TextDim);
            btn.Background = normalBg;
            btn.BorderThickness = new Thickness(0);
            btn.Padding = new Thickness(14, 9, 14, 9);
            btn.Cursor = Cursors.Hand;
            ApplyRoundedStyleLoaded(btn, normalBg, hoverBg, null, new Thickness(0));
        }

        /// <summary>
        /// Gives a Button rounded corners and hover colour by replacing its content
        /// border at Loaded time. No FrameworkElementFactory, no ControlTemplate.
        /// </summary>
        private static void ApplyRoundedStyleLoaded(
            Button btn,
            SolidColorBrush normalBg,
            SolidColorBrush hoverBg,
            SolidColorBrush? borderBrush,
            Thickness borderThickness)
        {
            btn.Loaded += (_, _) =>
            {
                if (btn.Template?.FindName("border", btn) is Border outerBd)
                {
                    outerBd.CornerRadius = new CornerRadius(5);
                    outerBd.Background = normalBg;
                    outerBd.BorderBrush = borderBrush ?? new SolidColorBrush(Colors.Transparent);
                    outerBd.BorderThickness = borderThickness;
                }
            };

            btn.MouseEnter += (_, _) => btn.Background = hoverBg;
            btn.MouseLeave += (_, _) => btn.Background = normalBg;
        }

        // ====================================================================
        //  Checkbox row (view picker). Matches W's CheckRow (Cmds_Pulldown4.cs).
        // ====================================================================

        public static (Border Visual, CheckBox Box) CheckRow(string viewName, bool isFirst)
        {
            var box = new CheckBox { Width = 0, Height = 0, Opacity = 0 };

            var checkSquare = new Border
            {
                Width = 17,
                Height = 17,
                CornerRadius = new CornerRadius(4),
                BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                BorderThickness = new Thickness(1.5),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 12, 0)
            };
            var checkMark = new TextBlock
            {
                Text = "✓",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = WpfVisibility.Hidden
            };
            checkSquare.Child = checkMark;

            var nameLabel = new TextBlock
            {
                Text = viewName,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                Foreground = AvTokens.B(AvTokens.Text),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = viewName
            };

            var grid = new WpfGrid { Height = 44 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(43) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            WpfGrid.SetColumn(checkSquare, 0);
            WpfGrid.SetColumn(nameLabel, 1);
            grid.Children.Add(checkSquare);
            grid.Children.Add(nameLabel);

            var rowBorder = new Border
            {
                Background = Brushes.Transparent,
                Child = grid
            };

            box.Checked += (s, e) =>
            {
                checkSquare.Background = AvTokens.B(AvTokens.Accent);
                checkSquare.BorderBrush = AvTokens.B(AvTokens.Accent);
                checkMark.Visibility = WpfVisibility.Visible;
                nameLabel.Foreground = AvTokens.B(AvTokens.Accent);
                nameLabel.FontWeight = FontWeights.SemiBold;
                rowBorder.Background = AvTokens.B(AvTokens.AccentTint);
            };
            box.Unchecked += (s, e) =>
            {
                checkSquare.Background = Brushes.Transparent;
                checkSquare.BorderBrush = AvTokens.B(AvTokens.BorderStrong);
                checkMark.Visibility = WpfVisibility.Hidden;
                nameLabel.Foreground = AvTokens.B(AvTokens.Text);
                nameLabel.FontWeight = FontWeights.Normal;
                rowBorder.Background = Brushes.Transparent;
            };

            return (rowBorder, box);
        }

        // ====================================================================
        //  Alert window
        // ====================================================================

        /// <summary>
        /// Shows a modal alert with an icon badge whose glyph/color reflects
        /// severity. Modeled on Cmds_Button6.cs's ShowWpfAlert.
        /// </summary>
        public static void ShowAlert(string title, string message, AlertSeverity severity)
            => new AlertWindow(title, message, severity).ShowDialog();
    }

    // ========================================================================
    //  Alert severity + window
    // ========================================================================

    public enum AlertSeverity { Error, Warning, Info, Success }

    /// <summary>
    /// Modal icon-badge alert window. Same shape as Cmds_Button6.cs's
    /// ShowWpfAlert (36x36 tinted circle + glyph, wrapped message, single OK
    /// button in a bordered footer) but with the icon badge's background/
    /// foreground/glyph driven by AlertSeverity, reusing AvTokens'
    /// fail/warn/info/ok token pairs rather than new colors.
    /// </summary>
    internal sealed class AlertWindow : Window
    {
        public AlertWindow(string title, string message, AlertSeverity severity)
        {
            var (bg, fg, glyph) = severity switch
            {
                AlertSeverity.Error => (AvTokens.FailBg, AvTokens.FailFg, "✕"),
                AlertSeverity.Warning => (AvTokens.WarnBg, AvTokens.WarnFg, "!"),
                AlertSeverity.Info => (AvTokens.InfoBg, AvTokens.InfoFg, "i"),
                AlertSeverity.Success => (AvTokens.OkBg, AvTokens.OkFg, "✓"),
                _ => (AvTokens.InfoBg, AvTokens.InfoFg, "i")
            };

            // Icon area (tinted circle with severity glyph)
            var icon = new Border
            {
                Width = 36,
                Height = 36,
                Background = AvTokens.B(bg),
                CornerRadius = new CornerRadius(18),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = AvTokens.Ui,
                    FontSize = 17,
                    FontWeight = FontWeights.Bold,
                    Foreground = AvTokens.B(fg),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            // Message text
            var msg = new TextBlock
            {
                Text = message,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                Foreground = AvTokens.B(AvTokens.Text),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                VerticalAlignment = VerticalAlignment.Top
            };

            var contentRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(20, 20, 20, 0)
            };
            contentRow.Children.Add(icon);
            contentRow.Children.Add(msg);

            // Footer
            var okBtn = AvControls.PrimaryBtn("OK", () => Close());
            var footerInner = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(okBtn, Dock.Right);
            footerInner.Children.Add(okBtn);
            var footer = new Border
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 12, 20, 12),
                Margin = new Thickness(0, 16, 0, 0),
                Child = footerInner
            };

            var root = new StackPanel();
            root.Children.Add(contentRow);
            root.Children.Add(footer);

            Title = title;
            Width = 420;
            Height = 200;
            MinHeight = 180;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;
            Background = AvTokens.B(AvTokens.Surface);
            FontFamily = AvTokens.Ui;
            UseLayoutRounding = true;
            ShowInTaskbar = false;
            Content = root;
        }
    }
}
