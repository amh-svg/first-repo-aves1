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
    /// (DOC01_FillSchedules.cs, DOC07_AddWeight.cs), "AvesUiHelper" (DOC03_CreateSheets.cs),
    /// "AvesUI" (DOC04_AutoDimension.cs) and "W" (DOC05_AutoTag.cs).
    ///
    /// Only widgets shared by two or more of those sources live here. Per the
    /// reviewed decision, FAM03_Parameters.cs's "Av" class turned out to be a much
    /// larger, separately-evolved surface (StyledDataGrid, PillColumn,
    /// SegmentedToggle, RoundedProgressBar, FooterStrip, ConfigRow, PickerButton,
    /// FieldLabel, HairlineSeparator, TextColumn, plus its own BrandTile with
    /// DecodePixelHeight/bicubic scaling and its own width-parameterized button
    /// factories) used by no other file. That surface stays local to
    /// FAM03_Parameters.cs (mirroring how DOC04_AutoDimension's DimType* logic stays
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

        /// <summary>
        /// Logo decoded at twice its display height, so it stays sharp on
        /// high-DPI displays instead of being downscaled from full resolution at
        /// render time.
        ///
        /// Four separate loaders for the same AvTokens.LogoBase64 had grown up
        /// across the plugin -- BrandTile with no DecodePixelHeight at all,
        /// FAM01_Rename.cs decoding at 1x, FAM03_Parameters.cs at 2x, and
        /// FAM02_Download.cs with its own try/catch decode. This is
        /// FAM03_Parameters.cs's, the sharpest of them.
        /// </summary>
        private static Image SharpLogo(double height)
        {
            var bytes = Convert.FromBase64String(AvTokens.LogoBase64);
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new System.IO.MemoryStream(bytes);
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.DecodePixelHeight = (int)(height * 2);
            bmp.EndInit();
            bmp.Freeze();

            return new Image
            {
                Source = bmp,
                Height = height,
                Width = double.NaN,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                SnapsToDevicePixels = true
            };
        }

        /// <summary>
        /// The canonical report-window header: a 52px Surface strip carrying a
        /// 24px logo and the tool's name, and nothing else.
        ///
        /// Every report window in the plugin had built its own. Four sizes of
        /// logo (20/24/28/32), three heights (44/52/Auto) and two backgrounds
        /// were in use across eight windows, and FAM01_Rename.cs's showed no
        /// title text at all. The geometry here is DOC01_FillSchedules.cs's and
        /// DOC07_AddWeight.cs's, which was both the most common and the cleanest
        /// instance of it.
        ///
        /// One parameter, deliberately. DOC01_FillSchedules.cs and DOC07_AddWeight.cs
        /// carried a run-summary subtitle and FAM02_Download.cs a timestamp; both
        /// are dropped rather than preserved behind optional arguments, because
        /// an optional slot is how a header format drifts apart again. Anything
        /// beyond the tool's name belongs in the body -- which is where
        /// FAM02_Download.cs's DESTINATION panel already repeats its timestamp.
        ///
        /// The window's own Title should be set to the same string, so the OS
        /// title bar and this strip never disagree.
        ///
        /// AvControls.TitleBar and AvControls.HeaderStrip stay as they are: this
        /// replaces neither, and both keep callers that are not report windows.
        /// </summary>
        public static Border ReportHeaderBar(string title)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var logo = SharpLogo(24);
            logo.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(logo);

            row.Children.Add(new TextBlock
            {
                Text = title,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.Text),
                VerticalAlignment = VerticalAlignment.Center
            });

            return new Border
            {
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20, 0, 20, 0),
                Height = 52,
                Child = row
            };
        }

        // ====================================================================
        //  Report footer
        // ====================================================================

        /// <summary>
        /// The strip a report window's footer sits in: Surface-2, a 1px rule
        /// along the top, 20/14 padding.
        ///
        /// Promoted verbatim from FAM03_Parameters.cs's Av class, which already had
        /// the canonical values -- this is a move, not a redesign, so its
        /// existing callers render identically. The class remarks above explain
        /// that Av's surface stayed local because no other file used it; that
        /// stopped being true of this one widget once all eight report windows
        /// wanted it. Av.FooterStrip now forwards here.
        ///
        /// Surface-2 rather than Surface, per the Refined Native token table
        /// scoping Surface-2 to "footers, header strips, inset panels, table
        /// header". Four windows were on Surface and move.
        ///
        /// Takes the whole footer content, because the layout inside it stays
        /// per-window: some use a Grid, some a DockPanel, and the difference is
        /// invisible once the caption and buttons land in the same places.
        /// </summary>
        public static Border FooterStrip(UIElement content)
            => new Border
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 14, 20, 14),
                Child = content
            };

        /// <summary>
        /// The faint caption on the left of a report footer.
        ///
        /// Text is a parameter because the eight windows deliberately say
        /// different things: five name their source document, FAM02_Download.cs
        /// names its destination folder, and DOC03_CreateSheets.cs and
        /// FAM01_Rename.cs give result counts. Only the styling unifies.
        ///
        /// TextTrimming is the point of having this: paths and document names
        /// both overflow, and only two of the eight were trimming. Note that
        /// trimming needs the caption to be width-constrained by its parent --
        /// a Grid Auto column or a DockPanel Dock.Left child sizes to content
        /// and will push the buttons instead of ellipsing, so callers put this
        /// in a Star column or make it the LastChildFill child.
        /// </summary>
        public static TextBlock FooterCaption(string text)
            => new TextBlock
            {
                Text = text,
                FontFamily = AvTokens.Ui,
                FontSize = 11.5,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

        // ====================================================================
        //  Title bars (kept as two distinct methods -- see class remarks)
        // ====================================================================

        /// <summary>
        /// Title bar with an optional trailing breadcrumb ("-- breadcrumb").
        /// Matches W's (DOC05_AutoTag.cs) implementation: Height=44.
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
        /// Matches AvesUiHelper's (DOC03_CreateSheets.cs) implementation.
        ///
        /// Background is Surface2, per the Refined Native token table which scopes
        /// Surface-2 to "footers, header strips, inset panels, table header". This
        /// was Surface until the design handoff settled it; the change reaches all
        /// four callers in DOC03_CreateSheets.cs, ReportWindow and
        /// LayoutSettingsDialogBase included, since the strip is shared.
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
                Background = AvTokens.B(AvTokens.Surface2),
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
        /// (DOC03_CreateSheets.cs) implementation -- a visually distinct widget from
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
        //  Picker list rows
        // ====================================================================

        /// <summary>
        /// Row styling for a single-select picker list: generous padding, rounded
        /// row, tinted hover, accent-coloured and bold selection.
        ///
        /// Consolidated from two byte-identical copies in DOC04_AutoDimension.cs
        /// (DimTypePickerWindow) and DOC05_AutoTag.cs (BatchViewPickerWindow).
        /// Two independent identical implementations is the strongest available
        /// signal of the intended design, so that shape won.
        ///
        /// The hover trigger is NEW -- neither source had one. It is added here
        /// rather than locally so every picker gains it together instead of a
        /// fourth variant appearing.
        ///
        /// Third and fourth copies exist in DOC01_FillSchedules.cs and DOC07_AddWeight.cs
        /// and are deliberately NOT migrated. Button7's diverges: it tints the
        /// selected background but never sets the foreground, so its selected rows
        /// do not go accent-coloured. Adopting this style there would change that
        /// window's appearance, which belongs in its own task -- it joins the
        /// follow-up backlog alongside Button8's CreateParamField, Pulldown3's
        /// Left-aligned FieldButtonTemplate copy, and the StatusPill / StatTile /
        /// button-technique items.
        ///
        /// Returns a fresh Style per call: WPF seals a Style once it is applied,
        /// so a shared static instance could not be reused across windows.
        /// </summary>
        public static Style PickerListItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(12, 9, 12, 9)));
            style.Setters.Add(new Setter(ListBoxItem.FontFamilyProperty, AvTokens.Ui));
            style.Setters.Add(new Setter(ListBoxItem.FontSizeProperty, 13.0));
            style.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, AvTokens.B(AvTokens.Text)));
            style.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent));

            // A plain ListBoxItem's default chrome uses the OS/theme highlight for
            // the selected state and ignores a Background setter, so supply a
            // Border-based template bound to Background/Padding.
            var tpl = new ControlTemplate(typeof(ListBoxItem));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "RowBorder";
            bd.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            bd.SetBinding(Border.PaddingProperty,
                new System.Windows.Data.Binding("Padding")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            style.Setters.Add(new Setter(ListBoxItem.TemplateProperty, tpl));

            // Order matters: when several triggers set the same property, the last
            // active one wins. Hover is registered first so a row that is both
            // selected and hovered keeps its accent treatment rather than
            // flickering back to the hover tint under the cursor.
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, AvTokens.B(AvTokens.Zebra)));
            style.Triggers.Add(hover);

            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, AvTokens.B(AvTokens.AccentTint)));
            selected.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, AvTokens.B(AvTokens.Accent)));
            selected.Setters.Add(new Setter(ListBoxItem.FontWeightProperty, FontWeights.SemiBold));
            style.Triggers.Add(selected);

            return style;
        }

        // ====================================================================
        //  Field label + picker box
        // ====================================================================

        /// <summary>
        /// Numbered field label -- step badge plus a dim title -- intended to sit
        /// ABOVE its control rather than beside it, so the label and the thing it
        /// labels read as two separate pieces.
        ///
        /// Promoted verbatim from FAM03_Parameters.cs's Av class, where this pattern
        /// was already in production behind Batch Shared Parameters and Batch
        /// Remove Parameters. Sizes, colours and the 8px bottom margin are
        /// unchanged, so that window renders identically through this.
        /// </summary>
        public static StackPanel FieldLabel(int n, string text)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 8)
            };
            row.Children.Add(NumberBadge(n));
            row.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = AvTokens.Ui,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.TextDim),
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        /// <summary>
        /// A clickable box showing the current value of a field, opening whatever
        /// picker the caller wires to Click. Pairs with <see cref="FieldLabel"/>.
        ///
        /// Promoted from FAM03_Parameters.cs's Av class. showChevron is new and
        /// defaults to false, so the original callers render exactly as before;
        /// the source's own doc comment claimed a chevron but never drew one.
        ///
        /// Returns the Button so the caller owns Click wiring, matching the
        /// PrimaryButton/SecondaryButton convention. The value TextBlock is
        /// stashed in Tag so <see cref="SetPickerValue(Button, string)"/> can find
        /// it without the caller holding a reference.
        /// </summary>
        public static Button PickerButton(string currentValue, bool showChevron = false)
        {
            var label = new TextBlock
            {
                Text = currentValue,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                Foreground = AvTokens.B(AvTokens.Text),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            object content = label;
            if (showChevron)
            {
                var chevron = new TextBlock
                {
                    Text = "›",
                    FontFamily = AvTokens.Ui,
                    FontSize = 18,
                    Foreground = AvTokens.B(AvTokens.TextFaint),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                };

                var grid = new WpfGrid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                WpfGrid.SetColumn(label, 0);
                WpfGrid.SetColumn(chevron, 1);
                grid.Children.Add(label);
                grid.Children.Add(chevron);
                content = grid;
            }

            return new Button
            {
                Content = content,
                Tag = label,
                Height = 38,
                Padding = new Thickness(10, 0, 10, 0),
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Template = PickerButtonTemplate()
            };
        }

        /// <summary>Updates the label text inside a <see cref="PickerButton"/>.</summary>
        public static void SetPickerValue(Button btn, string value)
        {
            if (btn.Tag is TextBlock lbl) lbl.Text = value;
        }

        /// <summary>
        /// Updates a <see cref="PickerButton"/> that can be empty: null or blank
        /// restores the placeholder wording in the faint colour, a real value
        /// switches to the normal text colour. That colour shift is how "chosen"
        /// reads at a glance.
        ///
        /// A separate overload rather than a default argument, because the
        /// two-argument form above never touches Foreground and its existing
        /// callers must keep that behaviour.
        /// </summary>
        public static void SetPickerValue(Button btn, string? value, string placeholder)
        {
            if (btn.Tag is not TextBlock lbl) return;

            bool chosen = !string.IsNullOrWhiteSpace(value);
            lbl.Text = chosen ? value : placeholder;
            lbl.Foreground = AvTokens.B(chosen ? AvTokens.Text : AvTokens.TextFaint);
        }

        /// <summary>
        /// White, bordered, rounded look for a picker box, with an accent hover.
        ///
        /// Promoted from FAM03_Parameters.cs. Deliberately NOT merged with
        /// FieldButtonTemplate: that one hovers to Surface2, this one hovers to an
        /// Accent border over AccentTint, and it pins the content margin and
        /// stretches the presenter. Collapsing them would change the appearance of
        /// one window or the other, so both live here as distinct templates.
        /// </summary>
        public static ControlTemplate PickerButtonTemplate()
        {
            var tpl = new ControlTemplate(typeof(Button));

            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            // Surface is #FFFFFF, the same value the source used via Brushes.White.
            bd.SetValue(Border.BackgroundProperty, AvTokens.B(AvTokens.Surface));
            bd.SetValue(Border.BorderBrushProperty, AvTokens.B(AvTokens.BorderStrong));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.MarginProperty, new Thickness(10, 0, 10, 0));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
            bd.AppendChild(cp);

            tpl.VisualTree = bd;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, AvTokens.B(AvTokens.Accent), "bd"));
            hover.Setters.Add(new Setter(Border.BackgroundProperty, AvTokens.B(AvTokens.AccentTint), "bd"));
            tpl.Triggers.Add(hover);

            return tpl;
        }

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
            // Trimming and tooltip to match the value above, which has had both
            // all along. Without them a label too wide for its tile was simply cut
            // off mid-word with nothing to show it had happened -- and tiles are
            // laid out in equal star columns, so how much room a label gets
            // depends on how many tiles share the row and how far the window has
            // been dragged in, neither of which the label can know.
            //
            // No visible change at sizes where the label already fitted. Where it
            // did not, a hard cut becomes an ellipsis with the full text on hover.
            var k = new TextBlock
            {
                Text = label,
                FontFamily = AvTokens.Ui,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = label
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
        /// labelOverride, when non-null, replaces the default label text while keeping
        /// kind's color mapping -- lets callers keep their own existing wording (e.g.
        /// "Cache mismatch", "Completed with errors", "Warn", "Error") while still
        /// picking up this shared pill's geometry (dot size, padding, corner radius,
        /// font size).
        /// </summary>
        public static Border StatusPill(string kind, string? labelOverride = null)
        {
            (string bg, string fg, string label) = kind switch
            {
                "ok" => (AvTokens.OkBg, AvTokens.OkFg, "Done"),
                "skip" => (AvTokens.SkipBg, AvTokens.SkipFg, "Skipped"),
                "fail" => (AvTokens.FailBg, AvTokens.FailFg, "Failed"),
                "warn" => (AvTokens.WarnBg, AvTokens.WarnFg, "Warning"),
                _ => (AvTokens.InfoBg, AvTokens.InfoFg, "Info"),
            };
            label = labelOverride ?? label;

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
        //  DataGrid cell template
        // ====================================================================

        /// <summary>
        /// Replacement ControlTemplate for DataGridCell, whose only job is to
        /// make cell content sit vertically centred in its row.
        ///
        /// It exists because DataGridCell.VerticalContentAlignment does not work.
        /// The stock DataGridCell template is a Border wrapping a bare
        /// ContentPresenter, and -- unlike Button or ListBoxItem, whose default
        /// templates bind it -- nothing in that template consumes
        /// VerticalContentAlignment. Setting it compiles, looks correct in
        /// source, and changes nothing on screen. Five report grids across the
        /// plugin were top-aligned for exactly that reason.
        ///
        /// Centring the ContentPresenter directly is the approach FAM02_Download.cs
        /// arrived at independently and is the one confirmed to render correctly.
        /// It also covers DataGridTemplateColumn cells, not just text ones, and
        /// does not depend on each column's ElementStyle remembering to set
        /// VerticalAlignment on its TextBlock -- the per-column gap that left
        /// DOC07_AddWeight.cs's Assembly column misaligned while its neighbours
        /// looked fine.
        ///
        /// Background, BorderBrush, BorderThickness and Padding are template-bound
        /// so each grid's existing DataGridCell setters keep working. Note that
        /// Padding likely had no effect before this, for the same reason
        /// VerticalContentAlignment had none: adopting this template is what makes
        /// those already-declared paddings real.
        ///
        /// The trade-off is that the stock template's cell-level focus and
        /// selection chrome goes away. Every caller is a read-only report with a
        /// transparent cell background that stripes at the DataGridRow level, so
        /// nothing visible is lost; row selection and zebra banding are unaffected.
        ///
        /// Returns a fresh template per call, matching PickerListItemStyle: a
        /// template is sealed once applied, so a shared static instance would be
        /// a trap for any future caller that wanted to tweak one.
        /// </summary>
        public static ControlTemplate DataGridCellTemplate()
        {
            var tpl = new ControlTemplate(typeof(DataGridCell));

            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.BorderBrushProperty,
                new System.Windows.Data.Binding("BorderBrush")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.BorderThicknessProperty,
                new System.Windows.Data.Binding("BorderThickness")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetBinding(Border.PaddingProperty,
                new System.Windows.Data.Binding("Padding")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            bd.AppendChild(cp);

            tpl.VisualTree = bd;
            return tpl;
        }

        /// <summary>
        /// StatusPill as a DataTemplate, for DataGridTemplateColumn cells.
        ///
        /// StatusPill returns a Border, which a DataTemplate cannot return
        /// directly, so every report window that wanted a pill in a grid column
        /// worked around it separately: DOC02_SheetInfo.cs reimplemented the whole
        /// pill as a parsed XAML string with hardcoded hex, and DOC06_TagReviewer.cs
        /// assigned StatusPill's output from a ContentControl's Loaded handler.
        ///
        /// Geometry is StatusPill's, value for value -- radius 6, padding
        /// 8/5/10/5, a 7px dot with a 6px right margin, label 11.5 SemiBold --
        /// and the colours come from AvTokens rather than literals, so the pill
        /// follows the tokens instead of having to be re-synced by hand.
        ///
        /// bindingPath names the row property holding the kind ("ok" / "skip" /
        /// "fail" / "warn" / "info"). Rows with a single fixed outcome can expose
        /// a computed property returning a constant.
        ///
        /// FAM01_Rename.cs deliberately keeps its own approach: it injects pills
        /// via DataGrid.LoadingRow rather than a template, and maps its own status
        /// vocabulary ("Renamed", "Already Correct") onto these kinds.
        /// </summary>
        public static DataTemplate StatusPillTemplate(string bindingPath = "StatusKind")
        {
            var pill = new FrameworkElementFactory(typeof(Border));
            pill.Name = "pill";
            pill.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            pill.SetValue(Border.PaddingProperty, new Thickness(8, 5, 10, 5));
            pill.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            pill.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);

            var dot = new FrameworkElementFactory(typeof(WpfEllipse));
            dot.Name = "dot";
            dot.SetValue(FrameworkElement.WidthProperty, 7.0);
            dot.SetValue(FrameworkElement.HeightProperty, 7.0);
            dot.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            dot.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));

            var lbl = new FrameworkElementFactory(typeof(TextBlock));
            lbl.Name = "lbl";
            lbl.SetValue(TextBlock.FontFamilyProperty, AvTokens.Ui);
            lbl.SetValue(TextBlock.FontSizeProperty, 11.5);
            lbl.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            lbl.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            row.AppendChild(dot);
            row.AppendChild(lbl);
            pill.AppendChild(row);

            var tpl = new DataTemplate { VisualTree = pill };

            AddPillKind(tpl, bindingPath, "ok", AvTokens.OkBg, AvTokens.OkFg, "Done");
            AddPillKind(tpl, bindingPath, "skip", AvTokens.SkipBg, AvTokens.SkipFg, "Skipped");
            AddPillKind(tpl, bindingPath, "fail", AvTokens.FailBg, AvTokens.FailFg, "Failed");
            AddPillKind(tpl, bindingPath, "warn", AvTokens.WarnBg, AvTokens.WarnFg, "Warning");
            AddPillKind(tpl, bindingPath, "info", AvTokens.InfoBg, AvTokens.InfoFg, "Info");

            return tpl;
        }

        private static void AddPillKind(
            DataTemplate tpl, string bindingPath,
            string kind, string bg, string fg, string label)
        {
            var trigger = new DataTrigger
            {
                Binding = new System.Windows.Data.Binding(bindingPath),
                Value = kind
            };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, AvTokens.B(bg), "pill"));
            trigger.Setters.Add(new Setter(WpfEllipse.FillProperty, AvTokens.B(fg), "dot"));
            trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, AvTokens.B(fg), "lbl"));
            trigger.Setters.Add(new Setter(TextBlock.TextProperty, label, "lbl"));
            tpl.Triggers.Add(trigger);
        }

        // ====================================================================
        //  Buttons -- three distinct families, one per hover/wiring technique.
        //  See class remarks: these are NOT collapsed into one shape because
        //  doing so would change either the calling convention (onClick wiring)
        //  or the hover mechanism (ControlTemplate vs. Loaded-event hack).
        // ====================================================================

        // -- Family A: onClick-wired, ControlTemplate via XamlReader.Parse.
        // Matches Av (DOC01_FillSchedules.cs, DOC07_AddWeight.cs).

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
        // Matches W (DOC05_AutoTag.cs) and AvesUI (DOC04_AutoDimension.cs) -- both used
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
        /// FieldButtonTemplate (DOC05_AutoTag.cs).
        ///
        /// contentAlign defaults to Center, which is what the template has always
        /// produced. It matters because the template's ContentPresenter alignment
        /// OVERRIDES a Button's own HorizontalContentAlignment -- a caller that
        /// sets Left on the button and takes the default here silently gets
        /// centred content, which is exactly what happened in DOC05_AutoTag.cs.
        ///
        /// DOC04_AutoDimension.cs still has a private copy identical but for Left
        /// alignment, predating this parameter. That is a genuine drifted
        /// duplicate and a follow-up consolidation item; it is left alone because
        /// migrating it would change that window's appearance in a task that is
        /// not about it.
        /// </summary>
        public static ControlTemplate FieldButtonTemplate(
            HorizontalAlignment contentAlign = HorizontalAlignment.Center) =>
            FactoryBtnTemplate(AvTokens.Surface, AvTokens.Surface2, AvTokens.BorderStrong, 1, contentAlign);

        private static ControlTemplate FactoryBtnTemplate(
            string? normalBg, string hoverBg, string? borderHex, double borderPx,
            HorizontalAlignment contentAlign = HorizontalAlignment.Center)
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
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, contentAlign);
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
        // ControlTemplate at all). Matches AvesUiHelper (DOC03_CreateSheets.cs).
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
        //  Checkbox row (view picker). Matches W's CheckRow (DOC05_AutoTag.cs).
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
        /// severity. Modeled on FAM01_Rename.cs's ShowWpfAlert.
        /// </summary>
        public static void ShowAlert(string title, string message, AlertSeverity severity)
            => new AlertWindow(title, message, severity).ShowDialog();
    }

    // ========================================================================
    //  Alert severity + window
    // ========================================================================

    public enum AlertSeverity { Error, Warning, Info, Success }

    /// <summary>
    /// Modal icon-badge alert window. Same shape as FAM01_Rename.cs's
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

            // Message text. TextWrapping.Wrap only does anything once the
            // element actually has a constrained width to wrap within -- a
            // horizontal StackPanel (the previous contentRow) hands each
            // child unconstrained width, so a long message rendered on one
            // line and clipped, however wide the window was fixed at. A Grid
            // column gives it a real width to wrap against.
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

            // Caps how tall the message area alone can grow before it
            // scrolls, so a very long message (this window now has to carry
            // multi-assembly, multi-sentence run reports) gets a scrollbar
            // instead of pushing the window past MaxHeight below and off the
            // usable part of the screen.
            var msgScroll = new ScrollViewer
            {
                Content = msg,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 420,
            };

            var contentRow = new WpfGrid { Margin = new Thickness(20, 20, 20, 0) };
            contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            contentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            WpfGrid.SetColumn(icon, 0);
            WpfGrid.SetColumn(msgScroll, 1);
            contentRow.Children.Add(icon);
            contentRow.Children.Add(msgScroll);

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
            Width = 480;
            MinWidth = 360;
            MaxWidth = 720;
            MinHeight = 180;
            MaxHeight = 640;
            // Height auto-fits the wrapped message (now that it actually
            // wraps); width is a fixed starting point the user can still drag
            // wider or narrower via ResizeMode below -- not SizeToContent.Width,
            // which would let one long unbroken word or a very wide message
            // push the window arbitrarily wide instead of wrapping.
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = AvTokens.B(AvTokens.Surface);
            FontFamily = AvTokens.Ui;
            UseLayoutRounding = true;
            ShowInTaskbar = false;
            Content = root;
        }
    }
}
