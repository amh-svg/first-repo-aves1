// Autodesk
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
// System
using System;
using System.Collections.Generic;
using System.Linq;
// WPF
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using WpfBrush = System.Windows.Media.SolidColorBrush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfColor = System.Windows.Media.Color;
using WpfColorConv = System.Windows.Media.ColorConverter;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfVisibility = System.Windows.Visibility;

namespace AVES.Cmds_PullDownAutoTag
{
    // ============================================================
    //  DESIGN TOKENS
    // ============================================================
    internal static class T
    {
        public const string Accent = "#E64A2E";
        public const string AccentDeep = "#C2402A";
        public const string AccentTint = "#FBE9E4";
        public const string Surface = "#FFFFFF";
        public const string Surface2 = "#FAFAFB";
        public const string Border = "#E5E6EA";
        public const string BorderStrong = "#D4D6DC";
        public const string Text = "#1F2329";
        public const string TextDim = "#5A6069";
        public const string TextFaint = "#8B9099";
        public const string OkBg = "#E4F3EA"; public const string OkFg = "#2C7A52";
        public const string SkipBg = "#EDEFF2"; public const string SkipFg = "#5B6573";
        public const string FailBg = "#FBE3DE"; public const string FailFg = "#C0381F";
        public const string WarnBg = "#FAF0DA"; public const string WarnFg = "#9A6B12";
        public const string InfoBg = "#E7EFFB"; public const string InfoFg = "#2E66A8";
        public static WpfBrush B(string hex) =>
            new WpfBrush((WpfColor)WpfColorConv.ConvertFromString(hex));
        public static readonly WpfFontFamily Ui = new WpfFontFamily("Segoe UI");
        public static readonly WpfFontFamily Mono = new WpfFontFamily("Consolas");
    }

    // ============================================================
    //  SHARED UI HELPERS
    // ============================================================
    internal static class W
    {
        // Logo PNG embedded as Base64 — single const string, decoded once at first use.
        private const string LogoB64 = "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";
        private static System.Windows.Media.ImageSource? _logoImg;
        private static System.Windows.Media.ImageSource LogoImage
        {
            get
            {
                if (_logoImg != null) return _logoImg;
                var bytes = Convert.FromBase64String(LogoB64);
                using var ms = new System.IO.MemoryStream(bytes);
                var bi = new System.Windows.Media.Imaging.BitmapImage();
                bi.BeginInit();
                bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();
                _logoImg = bi;
                return _logoImg;
            }
        }

        public static Border BrandTile(double size = 20)
        {
            var img = new System.Windows.Controls.Image
            {
                Source = LogoImage,
                Height = size,
                Width = double.NaN,
                Stretch = System.Windows.Media.Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            return new Border
            {
                Background = WpfBrushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Child = img
            };
        }

        public static Border TitleBar(string title, string? breadcrumb = null)
        {
            var brand = BrandTile(20);
            brand.Margin = new Thickness(0, 0, 10, 0);
            var lbl = new TextBlock
            {
                Text = title,
                FontFamily = T.Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = T.B(T.Text),
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(brand); row.Children.Add(lbl);
            if (!string.IsNullOrEmpty(breadcrumb))
            {
                row.Children.Add(new TextBlock
                {
                    Text = "\u2014 " + breadcrumb,
                    FontFamily = T.Ui,
                    FontSize = 12,
                    Foreground = T.B(T.TextFaint),
                    Margin = new Thickness(6, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            return new Border
            {
                Background = T.B(T.Surface2),
                BorderBrush = T.B(T.Border),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(14, 0, 12, 0),
                Height = 44,
                Child = row
            };
        }

        public static TextBlock SectionLabel(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontFamily = T.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = T.B(T.TextFaint),
            Margin = new Thickness(0, 0, 0, 10)
        };

        public static Border StatusPill(string kind)
        {
            string bg, fg, label;
            switch (kind)
            {
                case "ok": bg = T.OkBg; fg = T.OkFg; label = "Done"; break;
                case "skip": bg = T.SkipBg; fg = T.SkipFg; label = "Skipped"; break;
                case "fail": bg = T.FailBg; fg = T.FailFg; label = "Failed"; break;
                case "warn": bg = T.WarnBg; fg = T.WarnFg; label = "Warn"; break;
                default: bg = T.InfoBg; fg = T.InfoFg; label = "Info"; break;
            }
            var dot = new WpfEllipse
            {
                Width = 7,
                Height = 7,
                Fill = T.B(fg),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            var txt = new TextBlock
            {
                Text = label,
                Foreground = T.B(fg),
                FontFamily = T.Ui,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(dot); row.Children.Add(txt);
            return new Border
            {
                Background = T.B(bg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = row
            };
        }

        public static Border StatTile(string value, string label, string accent = "")
        {
            string vc;
            switch (accent)
            {
                case "ok": vc = T.OkFg; break;
                case "fail": vc = T.FailFg; break;
                case "warn": vc = T.WarnFg; break;
                case "accent": vc = T.Accent; break;
                default: vc = T.Text; break;
            }
            var val = new TextBlock
            {
                Text = value,
                FontFamily = T.Mono,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = T.B(vc),
                Margin = new Thickness(0, 0, 0, 4),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = value
            };
            var cap = new TextBlock
            {
                Text = label,
                FontFamily = T.Ui,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = T.B(T.TextFaint)
            };
            var stack = new StackPanel();
            stack.Children.Add(val); stack.Children.Add(cap);
            return new Border
            {
                Background = T.B(T.Surface),
                BorderBrush = T.B(T.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(14, 12, 14, 12),
                Child = stack
            };
        }

        public static Button PrimaryButton(string text) => new Button
        {
            Content = text,
            FontFamily = T.Ui,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = WpfBrushes.White,
            Background = T.B(T.Accent),
            Padding = new Thickness(16, 9, 16, 9),
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = BtnTemplate(T.Accent, T.AccentDeep, null, 0)
        };

        public static Button SecondaryButton(string text) => new Button
        {
            Content = text,
            FontFamily = T.Ui,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = T.B(T.Text),
            Background = T.B(T.Surface),
            Padding = new Thickness(16, 9, 16, 9),
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = BtnTemplate(T.Surface, T.Surface2, T.BorderStrong, 1)
        };

        public static Button GhostButton(string text) => new Button
        {
            Content = text,
            FontFamily = T.Ui,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = T.B(T.TextDim),
            Background = WpfBrushes.Transparent,
            Padding = new Thickness(14, 9, 14, 9),
            Cursor = System.Windows.Input.Cursors.Hand,
            Template = BtnTemplate(null, T.Surface2, null, 0)
        };

        private static ControlTemplate BtnTemplate(
            string? normalBg, string hoverBg, string? borderHex, double borderPx)
        {
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "bd";
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            bd.SetValue(Border.BackgroundProperty,
                normalBg != null ? (object)T.B(normalBg) : WpfBrushes.Transparent);
            if (borderHex != null)
            {
                bd.SetValue(Border.BorderBrushProperty, T.B(borderHex));
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
            trigger.Setters.Add(new Setter(Border.BackgroundProperty, T.B(hoverBg), "bd"));
            tpl.Triggers.Add(trigger);
            return tpl;
        }

        public static Border HDivider() => new Border { Height = 1, Background = T.B(T.Border) };

        // ── Number badge (circled step number, e.g. "① Section title") ────────
        public static Border NumberBadge(int n)
        {
            var t = new TextBlock
            {
                Text = n.ToString(),
                FontFamily = T.Ui,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = T.B(T.Accent),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = T.B(T.AccentTint),
                Margin = new Thickness(0, 0, 7, 0),
                Child = t
            };
        }

        // Rounded, bordered, hover-highlighted look shared by SecondaryButton and
        // any clickable "field" control (e.g. the tag-family picker trigger).
        public static ControlTemplate FieldButtonTemplate() =>
            BtnTemplate(T.Surface, T.Surface2, T.BorderStrong, 1);

        public static (Border Visual, WpfCheckBox Box) CheckRow(string viewName, bool isFirst)
        {
            var box = new WpfCheckBox { Width = 0, Height = 0, Opacity = 0 };

            var checkSquare = new Border
            {
                Width = 17,
                Height = 17,
                CornerRadius = new CornerRadius(4),
                BorderBrush = T.B(T.BorderStrong),
                BorderThickness = new Thickness(1.5),
                Background = WpfBrushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 12, 0)
            };
            var checkMark = new TextBlock
            {
                Text = "✓",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = WpfBrushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = WpfVisibility.Hidden
            };
            checkSquare.Child = checkMark;

            var nameLabel = new TextBlock
            {
                Text = viewName,
                FontFamily = T.Ui,
                FontSize = 13,
                Foreground = T.B(T.Text),
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
                Background = WpfBrushes.Transparent,
                Child = grid
            };

            box.Checked += (s, e) =>
            {
                checkSquare.Background = T.B(T.Accent);
                checkSquare.BorderBrush = T.B(T.Accent);
                checkMark.Visibility = WpfVisibility.Visible;
                nameLabel.Foreground = T.B(T.Accent);
                nameLabel.FontWeight = FontWeights.SemiBold;
                rowBorder.Background = T.B(T.AccentTint);
            };
            box.Unchecked += (s, e) =>
            {
                checkSquare.Background = WpfBrushes.Transparent;
                checkSquare.BorderBrush = T.B(T.BorderStrong);
                checkMark.Visibility = WpfVisibility.Hidden;
                nameLabel.Foreground = T.B(T.Text);
                nameLabel.FontWeight = FontWeights.Normal;
                rowBorder.Background = WpfBrushes.Transparent;
            };

            return (rowBorder, box);
        }
    }

    // ============================================================
    //  TAG FAMILY MEMORY
    //  Remembers the last tag family picked per category (MTQ vs
    //  Vendor use different BuiltInCategory values) so returning
    //  users get it pre-selected next time. Stored per-Windows-user
    //  in %AppData%\AVES\last_tag_family.txt as "Category<TAB>Label" lines.
    // ============================================================
    internal static class TagFamilyMemory
    {
        private static readonly string ConfigDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVES");

        private static readonly string ConfigFile =
            System.IO.Path.Combine(ConfigDir, "last_tag_family.txt");

        internal static string? TryGet(BuiltInCategory cat)
        {
            try
            {
                if (!System.IO.File.Exists(ConfigFile)) return null;
                string prefix = cat.ToString() + "\t";
                foreach (string line in System.IO.File.ReadAllLines(ConfigFile))
                    if (line.StartsWith(prefix)) return line.Substring(prefix.Length);
            }
            catch { }
            return null;
        }

        internal static void Save(BuiltInCategory cat, string label)
        {
            try
            {
                System.IO.Directory.CreateDirectory(ConfigDir);
                string prefix = cat.ToString() + "\t";
                var lines = System.IO.File.Exists(ConfigFile)
                    ? System.IO.File.ReadAllLines(ConfigFile).Where(l => !l.StartsWith(prefix)).ToList()
                    : new List<string>();
                lines.Add(prefix + label);
                System.IO.File.WriteAllLines(ConfigFile, lines);
            }
            catch { /* Non-fatal — worst case the user just picks again next run. */ }
        }
    }

    // ============================================================
    //  WINDOW 1 — BATCH MULTI-SELECT 3D VIEW PICKER
    //  Lists every 3D view that lives on any sheet, grouped by
    //  sheet. Mouse-wheel scrollable. Click a row to toggle its
    //  checkbox. Live search filters by view name or sheet number.
    //  "Select all" / "Deselect all" operate on the current filter.
    //  Real-time counter + confirm button in footer.
    // ============================================================
    // ============================================================
    //  TAG FAMILY PICKER WINDOW (modal popup, replaces the old
    //  button-anchored dropdown for aesthetic/behavior consistency
    //  with the rest of the AVES picker windows)
    // ============================================================
    internal sealed class TagFamilyPickerWindow : Window
    {
        public string? SelectedLabel { get; private set; }

        private readonly List<string> _allLabels;
        private readonly string? _currentLabel;
        private WpfTextBox _searchBox = null!;
        private ListBox _listBox = null!;
        private Button _selectBtn = null!;

        public TagFamilyPickerWindow(string title, List<string> labels, string? currentLabel)
        {
            _allLabels = labels;
            _currentLabel = currentLabel;

            Title = title;
            Width = 480; Height = 560;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = T.B(T.Surface); FontFamily = T.Ui;
            UseLayoutRounding = true; SnapsToDevicePixels = true;

            var root = new DockPanel();

            var tb = W.TitleBar(title);
            DockPanel.SetDock(tb, Dock.Top);
            root.Children.Add(tb);

            var footer = BuildFooter();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var body = new DockPanel { Margin = new Thickness(20, 16, 20, 0) };

            var search = BuildSearchBox();
            DockPanel.SetDock(search, Dock.Top);
            body.Children.Add(search);

            _listBox = new ListBox
            {
                FontFamily = T.Ui,
                FontSize = 13,
                BorderThickness = new Thickness(0),
                Background = WpfBrushes.Transparent,
                Focusable = true
            };
            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(12, 9, 12, 9)));
            itemStyle.Setters.Add(new Setter(ListBoxItem.FontFamilyProperty, T.Ui));
            itemStyle.Setters.Add(new Setter(ListBoxItem.FontSizeProperty, 13.0));
            itemStyle.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, T.B(T.Text)));
            itemStyle.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, WpfBrushes.Transparent));

            // Custom template: a plain ListBoxItem's default chrome uses the OS/theme
            // highlight for the selected state and ignores a Background setter, so we
            // supply our own Border-based template bound to Background/Padding.
            var itemTemplate = new ControlTemplate(typeof(ListBoxItem));
            var itemBorder = new FrameworkElementFactory(typeof(Border));
            itemBorder.Name = "RowBorder";
            itemBorder.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            itemBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            itemBorder.SetBinding(Border.PaddingProperty,
                new System.Windows.Data.Binding("Padding")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var itemContent = new FrameworkElementFactory(typeof(ContentPresenter));
            itemContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            itemBorder.AppendChild(itemContent);
            itemTemplate.VisualTree = itemBorder;
            itemStyle.Setters.Add(new Setter(ListBoxItem.TemplateProperty, itemTemplate));

            var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selectedTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, T.B(T.AccentTint)));
            selectedTrigger.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, T.B(T.Accent)));
            itemStyle.Triggers.Add(selectedTrigger);
            _listBox.ItemContainerStyle = itemStyle;
            _listBox.SelectionChanged += (s, e) => _selectBtn.IsEnabled = _listBox.SelectedItem != null;
            _listBox.MouseDoubleClick += (s, e) => { if (_listBox.SelectedItem != null) OnSelect(); };

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _listBox,
                Focusable = false
            };
            scroll.PreviewMouseWheel += (s, e) =>
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 0.5);
                e.Handled = true;
            };

            var listBorder = new Border
            {
                Background = T.B(T.Surface),
                BorderBrush = T.B(T.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 10, 0, 16),
                Child = scroll
            };
            body.Children.Add(listBorder);
            root.Children.Add(body);
            Content = root;

            RefreshList();

            Loaded += (s, e) => _searchBox.Focus();
            KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) DialogResult = false;
                if (e.Key == System.Windows.Input.Key.Enter && _selectBtn.IsEnabled) OnSelect();
            };
        }

        private Border BuildSearchBox()
        {
            var g = new WpfGrid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new TextBlock
            {
                Text = "\u2315",
                FontSize = 15,
                Foreground = T.B(T.TextFaint),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            WpfGrid.SetColumn(icon, 0);

            _searchBox = new WpfTextBox
            {
                FontFamily = T.Ui,
                FontSize = 13,
                Foreground = T.B(T.Text),
                Background = WpfBrushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0, 8, 10, 8),
                ToolTip = "Filter tag families by name"
            };
            _searchBox.TextChanged += (s, e) => RefreshList();
            WpfGrid.SetColumn(_searchBox, 1);

            g.Children.Add(icon); g.Children.Add(_searchBox);
            return new Border
            {
                Background = T.B(T.Surface2),
                BorderBrush = T.B(T.BorderStrong),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Child = g
            };
        }

        private UIElement BuildFooter()
        {
            var footerBorder = new Border
            {
                Background = T.B(T.Surface2),
                BorderBrush = T.B(T.Border),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 14, 20, 14)
            };

            var inner = new DockPanel { LastChildFill = false };

            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(btns, Dock.Right);

            var cancelBtn = W.GhostButton("Cancel");
            cancelBtn.Margin = new Thickness(0, 0, 10, 0);
            cancelBtn.Click += (s, e) => DialogResult = false;

            _selectBtn = W.PrimaryButton("Select");
            _selectBtn.IsEnabled = false;
            _selectBtn.Click += (s, e) => OnSelect();

            btns.Children.Add(cancelBtn); btns.Children.Add(_selectBtn);
            inner.Children.Add(btns);
            footerBorder.Child = inner;
            return footerBorder;
        }

        private bool _initialSelectionApplied;

        private void RefreshList()
        {
            string q = (_searchBox.Text ?? "").Trim().ToLowerInvariant();
            string? previouslySelected = _listBox.SelectedItem as string;

            var filtered = string.IsNullOrEmpty(q)
                ? _allLabels
                : _allLabels.Where(l => l.ToLowerInvariant().Contains(q)).ToList();

            _listBox.Items.Clear();
            foreach (var label in filtered) _listBox.Items.Add(label);

            string? toSelect = previouslySelected;
            if (!_initialSelectionApplied)
            {
                _initialSelectionApplied = true;
                toSelect = _currentLabel;
            }

            if (toSelect != null && filtered.Contains(toSelect))
            {
                _listBox.SelectedItem = toSelect;
                _listBox.ScrollIntoView(toSelect);
            }
            _selectBtn.IsEnabled = _listBox.SelectedItem != null;
        }

        private void OnSelect()
        {
            if (_listBox.SelectedItem is string label)
            {
                SelectedLabel = label;
                DialogResult = true;
            }
        }
    }


    internal sealed class BatchViewPickerWindow : Window
    {
        public List<View3D> SelectedViews { get; } = new List<View3D>();

        public struct ViewEntry
        {
            public View3D View;
            public string SheetNum;
            public string SheetName;
            public string BadgeBg, BadgeFg, BadgeLabel;
        }

        private readonly List<ViewEntry> _all;
        private readonly List<(Border Row, WpfCheckBox Box, View3D View)> _visible
            = new List<(Border, WpfCheckBox, View3D)>();

        private WpfTextBox _searchBox = null!;
        private StackPanel _listPanel = null!;
        private TextBlock _counterLbl = null!;
        private Button _confirmBtn = null!;
        private int _checkedCount;

        // Tag family section (moved in from the former TagFamilyPickerWindow popup)
        private List<(string Label, ElementId Id)> _tagFamilies = null!;
        private Button _tagFamilyBtn = null!;
        private ElementId _selectedTagFamilyId = ElementId.InvalidElementId;
        public ElementId SelectedTagFamilyId => _selectedTagFamilyId;

        // confirmLabel is injected so both MTQ and Vendor can share this window
        // with their own footer button text. tagCat/tagSectionLabel let each
        // button load its own tag family list (MultiCategoryTags vs GenericModelTags)
        // into the same in-window picker.
        public BatchViewPickerWindow(
            List<ViewEntry> entries, Document doc, BuiltInCategory tagCat, string tagSectionLabel,
            string confirmLabel = "▶  Run Script")
        {
            _all = entries;

            Title = "Batch Tag — Select Views";
            Width = 520; Height = 680;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = T.B(T.Surface); FontFamily = T.Ui;
            UseLayoutRounding = true; SnapsToDevicePixels = true;

            var root = new DockPanel();

            var tb = W.TitleBar("Batch Tag", "Select Views");
            DockPanel.SetDock(tb, Dock.Top);
            root.Children.Add(tb);

            var footer = BuildFooter(confirmLabel);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var body = new DockPanel { Margin = new Thickness(20, 16, 20, 0) };

            var tagSection = BuildTagFamilySection(doc, tagCat, tagSectionLabel);
            DockPanel.SetDock(tagSection, Dock.Top);
            body.Children.Add(tagSection);

            var viewsSectionTitle = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 10) };
            viewsSectionTitle.Children.Add(W.NumberBadge(2));
            viewsSectionTitle.Children.Add(new TextBlock
            {
                Text = "Select Views",
                FontFamily = T.Ui,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = T.B(T.TextFaint),
                VerticalAlignment = VerticalAlignment.Center
            });
            DockPanel.SetDock(viewsSectionTitle, Dock.Top);
            body.Children.Add(viewsSectionTitle);

            var search = BuildSearchBox();
            DockPanel.SetDock(search, Dock.Top);
            body.Children.Add(search);

            var toolbar = BuildToolbar();
            DockPanel.SetDock(toolbar, Dock.Top);
            body.Children.Add(toolbar);

            _listPanel = new StackPanel();
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _listPanel,
                Focusable = true
            };
            scroll.PreviewMouseWheel += (s, e) =>
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 0.5);
                e.Handled = true;
            };

            var listBorder = new Border
            {
                Background = T.B(T.Surface),
                BorderBrush = T.B(T.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 0, 16),
                Child = scroll
            };
            body.Children.Add(listBorder);
            root.Children.Add(body);
            Content = root;

            Loaded += (s, e) => { ApplyFilter(); _searchBox.Focus(); };
            KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) DialogResult = false;
                if (e.Key == System.Windows.Input.Key.Enter && _confirmBtn.IsEnabled) OnConfirm();
            };
        }

        private Border BuildTagFamilySection(Document doc, BuiltInCategory tagCat, string sectionLabel)
        {
            _tagFamilies = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(tagCat)
                .Cast<FamilySymbol>()
                .OrderBy(fs => fs.FamilyName)
                .ThenBy(fs => fs.Name)
                .Select(fs => (Label: fs.FamilyName + "  ·  " + fs.Name, Id: fs.Id))
                .ToList();

            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            titleRow.Children.Add(W.NumberBadge(1));
            titleRow.Children.Add(new TextBlock
            {
                Text = sectionLabel,
                FontFamily = T.Ui,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = T.B(T.TextFaint),
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(titleRow);

            bool hasFamilies = _tagFamilies.Count > 0;

            _tagFamilyBtn = new Button
            {
                FontFamily = T.Ui,
                FontSize = 13,
                Foreground = T.B(T.TextFaint),
                Background = T.B(T.Surface),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 8, 10, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Content = hasFamilies ? "Select Tag Family\u2026" : "No tag families of this type are loaded in the project.",
                IsEnabled = hasFamilies,
                Template = W.FieldButtonTemplate(),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            // Pre-select the last family remembered for this tag category, if it
            // still exists in this project.
            if (hasFamilies)
            {
                string? rememberedLabel = TagFamilyMemory.TryGet(tagCat);
                int rememberedIdx = rememberedLabel == null
                    ? -1 : _tagFamilies.FindIndex(f => f.Label == rememberedLabel);
                if (rememberedIdx >= 0)
                {
                    var remembered = _tagFamilies[rememberedIdx];
                    _selectedTagFamilyId = remembered.Id;
                    _tagFamilyBtn.Content = remembered.Label;
                    _tagFamilyBtn.Foreground = T.B(T.Text);
                }
            }

            _tagFamilyBtn.Click += (s, e) =>
            {
                var picker = new TagFamilyPickerWindow(
                    sectionLabel,
                    _tagFamilies.Select(f => f.Label).ToList(),
                    _tagFamilyBtn.Content as string == "Select Tag Family\u2026" ? null : _tagFamilyBtn.Content as string)
                { Owner = this };

                if (picker.ShowDialog() == true && picker.SelectedLabel != null)
                {
                    var picked = _tagFamilies.First(f => f.Label == picker.SelectedLabel);
                    _selectedTagFamilyId = picked.Id;
                    _tagFamilyBtn.Content = picked.Label;
                    _tagFamilyBtn.Foreground = T.B(T.Text);
                    TagFamilyMemory.Save(tagCat, picked.Label);
                    UpdateCounter();
                }
            };

            stack.Children.Add(_tagFamilyBtn);
            return new Border { Child = stack };
        }

        private Border BuildSearchBox()
        {
            var g = new WpfGrid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var icon = new TextBlock
            {
                Text = "\u2315",
                FontSize = 15,
                Foreground = T.B(T.TextFaint),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            WpfGrid.SetColumn(icon, 0);

            _searchBox = new WpfTextBox
            {
                FontFamily = T.Ui,
                FontSize = 13,
                Foreground = T.B(T.TextFaint),
                Background = WpfBrushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0, 8, 10, 8),
                Text = "Search by view or sheet\u2026"
            };
            _searchBox.GotFocus += (s, e) =>
            {
                if (_searchBox.Text == "Search by view or sheet\u2026")
                { _searchBox.Text = ""; _searchBox.Foreground = T.B(T.Text); }
            };
            _searchBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_searchBox.Text))
                { _searchBox.Text = "Search by view or sheet\u2026"; _searchBox.Foreground = T.B(T.TextFaint); }
            };
            _searchBox.TextChanged += (s, e) => ApplyFilter();
            WpfGrid.SetColumn(_searchBox, 1);

            g.Children.Add(icon); g.Children.Add(_searchBox);
            return new Border
            {
                Background = T.B(T.Surface2),
                BorderBrush = T.B(T.BorderStrong),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 0, 8),
                Child = g
            };
        }

        private UIElement BuildToolbar()
        {
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };

            var left = new StackPanel { Orientation = Orientation.Horizontal };

            var selAllBtn = W.SecondaryButton("Select All");
            selAllBtn.FontSize = 12;
            selAllBtn.Foreground = T.B(T.Accent);
            selAllBtn.Padding = new Thickness(12, 5, 12, 5);
            selAllBtn.Click += (s, e) => SetAllVisible(true);

            var deselAllBtn = W.SecondaryButton("Deselect All");
            deselAllBtn.FontSize = 12;
            deselAllBtn.Foreground = T.B(T.TextDim);
            deselAllBtn.Padding = new Thickness(12, 5, 12, 5);
            deselAllBtn.Margin = new Thickness(8, 0, 0, 0);
            deselAllBtn.Click += (s, e) => SetAllVisible(false);

            left.Children.Add(selAllBtn); left.Children.Add(deselAllBtn);
            DockPanel.SetDock(left, Dock.Left);
            bar.Children.Add(left);

            _counterLbl = new TextBlock
            {
                Text = "0 views selected",
                FontFamily = T.Ui,
                FontSize = 12,
                Foreground = T.B(T.TextDim),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(_counterLbl, Dock.Right);
            bar.Children.Add(_counterLbl);

            return bar;
        }

        private UIElement BuildFooter(string confirmLabel)
        {
            var footerBorder = new Border
            {
                Background = T.B(T.Surface2),
                BorderBrush = T.B(T.Border),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 14, 20, 14)
            };

            var inner = new DockPanel { LastChildFill = false };

            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(btns, Dock.Right);

            var cancelBtn = W.GhostButton("Cancel");
            cancelBtn.Margin = new Thickness(0, 0, 10, 0);
            cancelBtn.Click += (s, e) => DialogResult = false;

            _confirmBtn = W.PrimaryButton(confirmLabel);
            _confirmBtn.IsEnabled = false;
            _confirmBtn.Click += (s, e) => OnConfirm();

            btns.Children.Add(cancelBtn); btns.Children.Add(_confirmBtn);
            inner.Children.Add(btns);
            footerBorder.Child = inner;
            return footerBorder;
        }

        private void ApplyFilter()
        {
            bool placeholder = _searchBox.Text == "Search by view or sheet\u2026";
            string q = placeholder ? "" : (_searchBox.Text ?? "").Trim().ToLowerInvariant();

            var filtered = string.IsNullOrEmpty(q)
                ? _all
                : _all.Where(e =>
                    e.View.Name.ToLowerInvariant().Contains(q) ||
                    e.SheetNum.ToLowerInvariant().Contains(q) ||
                    e.SheetName.ToLowerInvariant().Contains(q))
                  .ToList();

            var checkedIds = new HashSet<ElementId>(
                _visible.Where(v => v.Box.IsChecked == true).Select(v => v.View.Id));

            _visible.Clear();
            _listPanel.Children.Clear();

            if (filtered.Count == 0)
            {
                _listPanel.Children.Add(new TextBlock
                {
                    Text = "No views match.",
                    FontFamily = T.Ui,
                    FontSize = 12.5,
                    Foreground = T.B(T.TextFaint),
                    Margin = new Thickness(16, 14, 16, 14),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            var sorted = filtered.OrderBy(e => e.View.Name).ToList();

            bool firstRow = true;
            foreach (var entry in sorted)
            {
                var (rowBorder, box) = W.CheckRow(entry.View.Name, firstRow);
                firstRow = false;

                if (checkedIds.Contains(entry.View.Id)) box.IsChecked = true;

                bool isChecked = box.IsChecked == true;
                box.Checked += (s, e) => { if (!isChecked) { isChecked = true; _checkedCount++; UpdateCounter(); } };
                box.Unchecked += (s, e) => { if (isChecked) { isChecked = false; _checkedCount--; UpdateCounter(); } };

                var capturedView = entry.View;
                rowBorder.MouseLeftButtonUp += (s, e) => { box.IsChecked = !box.IsChecked; };

                _listPanel.Children.Add(rowBorder);
                _visible.Add((rowBorder, box, capturedView));
            }

            _checkedCount = _visible.Count(v => v.Box.IsChecked == true);
            UpdateCounter();
        }

        private void SetAllVisible(bool check)
        {
            foreach (var (_, box, _) in _visible) box.IsChecked = check;
            _checkedCount = check ? _visible.Count : 0;
            UpdateCounter();
        }

        private void UpdateCounter()
        {
            _counterLbl.Text = _checkedCount == 1 ? "1 view selected"
                                                   : _checkedCount + " views selected";
            _confirmBtn.IsEnabled = _checkedCount > 0 && _selectedTagFamilyId != ElementId.InvalidElementId;
        }

        private void OnConfirm()
        {
            SelectedViews.Clear();
            foreach (var (_, box, view) in _visible)
                if (box.IsChecked == true) SelectedViews.Add(view);
            DialogResult = true;
        }

        // Collect 3D views that live on at least one sheet
        public static List<ViewEntry> CollectEntries(Document doc)
        {
            var sheetByView = new Dictionary<ElementId, (string Num, string Name)>();
            foreach (ViewSheet vs in new FilteredElementCollector(doc)
                                         .OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
                foreach (ElementId vpId in vs.GetAllViewports())
                    if (doc.GetElement(vpId) is Viewport vp)
                        sheetByView[vp.ViewId] = (vs.SheetNumber, vs.Name);

            var allViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D)).Cast<View3D>()
                .Where(v => !v.IsTemplate && sheetByView.ContainsKey(v.Id))
                .ToList();

            var entries = new List<ViewEntry>();
            foreach (var v in allViews)
            {
                var si = sheetByView[v.Id];
                string bbg, bfg, blbl;
                if (v.IsAssemblyView) { bbg = T.InfoBg; bfg = T.InfoFg; blbl = "Assembly"; }
                else { bbg = T.SkipBg; bfg = T.SkipFg; blbl = "3D"; }
                entries.Add(new ViewEntry
                {
                    View = v,
                    SheetNum = si.Num,
                    SheetName = si.Name,
                    BadgeBg = bbg,
                    BadgeFg = bfg,
                    BadgeLabel = blbl
                });
            }
            return entries.OrderBy(e => e.SheetNum).ThenBy(e => e.View.Name).ToList();
        }
    }

    // ============================================================
    //  VIEW LOG ROW
    // ============================================================
    internal sealed class ViewLogRow
    {
        public string ViewName { get; set; } = "";
        public string SheetNum { get; set; } = "";
        public string Status { get; set; } = "";
        public int Created { get; set; }
        public int Skipped { get; set; }
        public string Notes { get; set; } = "";
    }

    // ============================================================
    //  WINDOW 2 — RESULTS REPORT
    // ============================================================
    internal sealed class AutoTagResultsWindow : Window
    {
        public AutoTagResultsWindow(
            int totalCreated, int totalSkipped, int viewsProcessed,
            List<ViewLogRow> rows, string windowTitle = "Auto Tag \u2014 Results")
        {
            string tsvText = BuildTsv(rows);

            Title = windowTitle;
            Width = 680; MaxHeight = 660;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = T.B(T.Surface); FontFamily = T.Ui;
            UseLayoutRounding = true; SnapsToDevicePixels = true;

            var root = new StackPanel();
            root.Children.Add(W.TitleBar(windowTitle));

            var body = new StackPanel { Margin = new Thickness(20, 20, 20, 20) };
            root.Children.Add(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = body
            });

            body.Children.Add(W.SectionLabel("Run Summary"));
            var tileGrid = new UniformGrid { Columns = 3, Rows = 1, Margin = new Thickness(0, 0, 0, 20) };
            foreach (var tile in new[]
            {
                W.StatTile(totalCreated.ToString(),   "Tags created",    totalCreated  > 0 ? "ok"   : ""),
                W.StatTile(totalSkipped.ToString(),   "Skipped",         totalSkipped  > 0 ? "warn" : ""),
                W.StatTile(viewsProcessed.ToString(), "Views processed", ""),
            })
            { tile.Margin = new Thickness(0, 0, 8, 8); tileGrid.Children.Add(tile); }
            body.Children.Add(tileGrid);

            body.Children.Add(W.SectionLabel("Results per view"));
            body.Children.Add(BuildResultsTable(rows));

            root.Children.Add(W.HDivider());
            var footer = new DockPanel { Background = T.B(T.Surface2), LastChildFill = false };

            var caption = new TextBlock
            {
                Text = string.Format("{0} view(s) processed", viewsProcessed),
                FontFamily = T.Ui,
                FontSize = 11.5,
                Foreground = T.B(T.TextFaint),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(20, 14, 10, 14)
            };
            DockPanel.SetDock(caption, Dock.Left);
            footer.Children.Add(caption);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 14, 20, 14) };
            DockPanel.SetDock(btnPanel, Dock.Right);

            var copyBtn = W.SecondaryButton("\u29c9  Copy table");
            copyBtn.Margin = new Thickness(0, 0, 10, 0);
            DispatcherTimer? copyTimer = null;
            copyBtn.Click += (s, e) =>
            {
                try { Clipboard.SetText(tsvText); } catch { }
                copyBtn.Content = "\u2713  Copied!"; copyBtn.IsEnabled = false;
                copyTimer?.Stop();
                copyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                copyTimer.Tick += (ts, te) =>
                { copyTimer.Stop(); copyBtn.Content = "\u29c9  Copy table"; copyBtn.IsEnabled = true; };
                copyTimer.Start();
            };
            var closeBtn = W.PrimaryButton("Close");
            closeBtn.Click += (s, e) => Close();
            btnPanel.Children.Add(copyBtn); btnPanel.Children.Add(closeBtn);
            footer.Children.Add(btnPanel);
            root.Children.Add(footer);
            Content = root;
        }

        private static Border BuildResultsTable(List<ViewLogRow> rows)
        {
            var headerGrid = new WpfGrid { Background = T.B(T.Surface2) };
            DefineTableColumns(headerGrid);

            string[] headers = { "Status", "View", "Sheet", "Created", "Skipped", "Notes" };
            for (int i = 0; i < headers.Length; i++)
            {
                var hdr = new TextBlock
                {
                    Text = headers[i],
                    FontFamily = T.Ui,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = T.B(T.TextFaint),
                    Padding = new Thickness(i == 0 ? 14 : 12, 9, 12, 9)
                };
                WpfGrid.SetColumn(hdr, i);
                headerGrid.Children.Add(hdr);
            }

            var rowsPanel = new StackPanel();
            bool odd = false;
            foreach (var r in rows)
            {
                var rowGrid = new WpfGrid { Background = T.B(odd ? "#F7F8FA" : T.Surface) };
                DefineTableColumns(rowGrid);

                var pill = W.StatusPill(r.Status);
                pill.Margin = new Thickness(14, 0, 0, 0);
                pill.VerticalAlignment = VerticalAlignment.Center;
                WpfGrid.SetColumn(pill, 0); rowGrid.Children.Add(pill);

                var nameCell = MakeCell(r.ViewName, T.Ui, 12.5, T.Text, bold: true);
                WpfGrid.SetColumn(nameCell, 1); rowGrid.Children.Add(nameCell);

                var sheetCell = MakeCell(r.SheetNum, T.Mono, 11.5, T.TextDim);
                WpfGrid.SetColumn(sheetCell, 2); rowGrid.Children.Add(sheetCell);

                string createdFg = r.Created > 0 ? T.OkFg : T.TextFaint;
                var createdCell = MakeCell(r.Created > 0 ? r.Created.ToString() : "\u2014", T.Mono, 11.5, createdFg, centre: true);
                WpfGrid.SetColumn(createdCell, 3); rowGrid.Children.Add(createdCell);

                string skippedFg = r.Skipped > 0 ? T.WarnFg : T.TextFaint;
                var skippedCell = MakeCell(r.Skipped > 0 ? r.Skipped.ToString() : "\u2014", T.Mono, 11.5, skippedFg, centre: true);
                WpfGrid.SetColumn(skippedCell, 4); rowGrid.Children.Add(skippedCell);

                var notesCell = MakeCell(r.Notes, T.Ui, 11.5, T.TextFaint);
                WpfGrid.SetColumn(notesCell, 5); rowGrid.Children.Add(notesCell);

                rowsPanel.Children.Add(new Border
                {
                    BorderBrush = T.B(T.Border),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Child = rowGrid,
                    MinHeight = 42
                });
                odd = !odd;
            }

            var tableStack = new StackPanel();
            tableStack.Children.Add(new Border
            {
                BorderBrush = T.B(T.BorderStrong),
                BorderThickness = new Thickness(0, 0, 0, 1.5),
                Child = headerGrid
            });

            var dataScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 320,
                Content = rowsPanel
            };
            dataScroll.PreviewMouseWheel += (s, e) =>
            {
                dataScroll.ScrollToVerticalOffset(dataScroll.VerticalOffset - e.Delta * 0.5);
                e.Handled = true;
            };
            tableStack.Children.Add(dataScroll);

            return new Border
            {
                Background = T.B(T.Surface),
                BorderBrush = T.B(T.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 0, 0),
                Child = tableStack
            };
        }

        private static void DefineTableColumns(WpfGrid g)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        }

        private static TextBlock MakeCell(
            string text, WpfFontFamily font, double size, string fg,
            bool bold = false, bool centre = false) => new TextBlock
            {
                Text = text,
                FontFamily = font,
                FontSize = size,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = T.B(fg),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = text,
                Padding = new Thickness(12, 11, 12, 11),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = centre ? TextAlignment.Center : TextAlignment.Left
            };

        private static string BuildTsv(List<ViewLogRow> rows)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Status\tView\tSheet\tCreated\tSkipped\tNotes");
            foreach (var r in rows)
                sb.AppendLine(string.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}",
                    r.Status, r.ViewName, r.SheetNum, r.Created, r.Skipped, r.Notes));
            return sb.ToString().TrimEnd();
        }
    }

    // ============================================================
    //  BUTTON 1 — CMD_MTQTAG  (MTQ Drawings — untouched)
    // ============================================================
    [Transaction(TransactionMode.Manual)]
    public class Cmd_MTQTag : IExternalCommand
    {
        private const double EdgeClearanceFt = 1.0;
        private const double MinTagSpacingFt = 0.40;
        private const bool LeaderEnabled = true;
        private const double TallRatio = 1.4;
        private const double TallTopBottomFraction = 0.05;
        private const double TallTopBottomMaxHeightFraction = 0.10;
        private const double WideTopFraction = 0.30;
        private const double WideTopMaxHeightFraction = 0.50;
        private const double WideBottomFraction = 0.05;
        private const double WideBottomMaxHeightFraction = 0.10;
        private const double WideElementFraction = 0.55;
        private const double ClearThreshold = 0.08;
        private const double ColumnVerticalPaddingFt = 0.5;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var doc = uiDoc.Document;

            // 1. Collect all 3D views on sheets
            var entries = BatchViewPickerWindow.CollectEntries(doc);
            if (entries.Count == 0)
            {
                TaskDialog.Show("Auto Tag", "No 3D views found on any sheet in this project.");
                return Result.Failed;
            }

            // 2. Single window: pick tag family (in-window section) + multi-select views
            var picker = new BatchViewPickerWindow(
                entries, doc, BuiltInCategory.OST_MultiCategoryTags,
                "MTQ Tag Family", "▶  Run Script");
            if (picker.ShowDialog() != true
                || picker.SelectedViews.Count == 0
                || picker.SelectedTagFamilyId == ElementId.InvalidElementId)
                return Result.Cancelled;

            ElementId tagTypeId = picker.SelectedTagFamilyId;
            List<View3D> selectedViews = picker.SelectedViews;

            // 3. Tag each selected view, collect structured log rows
            int totalCreated = 0, totalSkipped = 0;
            var logRows = new List<ViewLogRow>();

            var sheetByViewId = new Dictionary<ElementId, string>();
            foreach (ViewSheet vs in new FilteredElementCollector(doc)
                                         .OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
                foreach (ElementId vpId in vs.GetAllViewports())
                    if (doc.GetElement(vpId) is Viewport vp)
                        sheetByViewId[vp.ViewId] = vs.SheetNumber;

            foreach (View3D view in selectedViews)
            {
                sheetByViewId.TryGetValue(view.Id, out string? sheetNum);
                var row = new ViewLogRow
                {
                    ViewName = view.Name,
                    SheetNum = sheetNum ?? "\u2014"
                };

                List<Element> modelElements = CollectModelElements(doc, view);
                if (modelElements.Count == 0)
                {
                    row.Status = "skip"; row.Notes = "No taggable elements";
                    logRows.Add(row); continue;
                }

                GetViewAxes(view, out XYZ right, out XYZ up, out XYZ fwd);

                var elData = new List<(Element El,
                    (double UMin, double UMax, double VMin, double VMax) Rect,
                    double Depth)>();
                foreach (Element el in modelElements)
                {
                    if (!TryGetScreenRect(el, view, right, up, out var rect)) continue;
                    elData.Add((el, rect, GetElementDepth(el, view, fwd)));
                }

                if (elData.Count == 0)
                {
                    row.Status = "skip"; row.Notes = "Could not project elements";
                    logRows.Add(row); continue;
                }

                double asmUMin = elData.Min(e => e.Rect.UMin);
                double asmUMax = elData.Max(e => e.Rect.UMax);
                double asmVMin = elData.Min(e => e.Rect.VMin);
                double asmVMax = elData.Max(e => e.Rect.VMax);
                double medDepth = elData.Select(e => e.Depth).OrderBy(d => d)
                                        .ElementAt(elData.Count / 2);

                double colLeft = asmUMin - EdgeClearanceFt;
                double colRight = asmUMax + EdgeClearanceFt;
                double rowTop = asmVMax + EdgeClearanceFt;
                double rowBottom = asmVMin - EdgeClearanceFt;

                var (topFrac, topHFrac, botFrac, botHFrac, _) =
                    GetThresholds(asmUMin, asmUMax, asmVMin, asmVMax);

                var zoneMap = AssignZonesTwoPass(elData,
                    asmUMin, asmUMax, asmVMin, asmVMax,
                    topFrac, topHFrac, botFrac, botHFrac);

                var zones = new Dictionary<string, List<(Element El,
                    (double UMin, double UMax, double VMin, double VMax) Rect,
                    double Depth)>>
                {
                    ["LEFT"] = new List<(Element, (double, double, double, double), double)>(),
                    ["RIGHT"] = new List<(Element, (double, double, double, double), double)>(),
                    ["TOP"] = new List<(Element, (double, double, double, double), double)>(),
                    ["BOTTOM"] = new List<(Element, (double, double, double, double), double)>(),
                };
                foreach (var entry in elData) zones[zoneMap[entry.El.Id.Value]].Add(entry);

                var leaderMap = PrecomputeLeaderEndpoints(zones, right, up, fwd, view);
                var slotMap = BuildSlotMap(zones, colLeft, colRight, rowTop, rowBottom,
                                             asmUMin, asmUMax, asmVMin, asmVMax);
                var allOrdered = zones["RIGHT"].Concat(zones["LEFT"])
                                              .Concat(zones["TOP"])
                                              .Concat(zones["BOTTOM"]).ToList();

                int created = 0;
                var warnMsgs = new List<string>();
                var skippedIds = new List<long>();

                // Tx 1: lock the view (tags require a locked 3D view)
                using (var tLock = new Transaction(doc, "Lock view: " + view.Name))
                {
                    tLock.Start();
                    try { view.SaveOrientationAndLock(); } catch { }
                    tLock.Commit();
                }

                // Tx 2: place tags into the now-locked view
                using (var t = new Transaction(doc, "Auto Tag: " + view.Name))
                {
                    t.Start();
                    foreach (var entry in allOrdered)
                    {
                        Element el = entry.El;
                        try
                        {
                            long elKey = el.Id.Value;
                            var slot = slotMap[elKey];
                            XYZ head = FromScreen(slot.U, slot.V, medDepth, right, up, fwd);

                            if (!leaderMap.TryGetValue(elKey, out XYZ? lEnd) || lEnd == null)
                            { skippedIds.Add(elKey); continue; }

                            IndependentTag tag = IndependentTag.Create(
                                doc, tagTypeId, view.Id,
                                new Reference(el), LeaderEnabled,
                                TagOrientation.Horizontal, head);

                            if (tag == null) { skippedIds.Add(elKey); continue; }
                            try { tag.TagHeadPosition = head; } catch { }

                            if (LeaderEnabled)
                            {
                                try
                                {
                                    tag.LeaderEndCondition = LeaderEndCondition.Free;
                                    tag.SetLeaderEnd(new Reference(el), lEnd);
                                }
                                catch { }
                            }
                            created++;
                        }
                        catch (Exception ex)
                        {
                            skippedIds.Add(el.Id.Value);
                            warnMsgs.Add(ex.Message);
                        }
                    }
                    t.Commit();
                }

                row.Created = created;
                row.Skipped = skippedIds.Count;
                row.Status = skippedIds.Count == 0 ? "ok" : "warn";
                row.Notes = warnMsgs.Count > 0
                    ? warnMsgs[0] + (warnMsgs.Count > 1
                        ? string.Format(" (+{0} more)", warnMsgs.Count - 1) : "")
                    : "";
                logRows.Add(row);
                totalCreated += created;
                totalSkipped += skippedIds.Count;
            }

            new AutoTagResultsWindow(
                totalCreated, totalSkipped, selectedViews.Count, logRows,
                "Auto Tag \u2014 Results")
                .ShowDialog();

            return Result.Succeeded;
        }

        // ── Projection helpers ────────────────────────────────────────────────
        private static double Dot(XYZ a, XYZ b) =>
            a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        private static void GetViewAxes(View view, out XYZ right, out XYZ up, out XYZ fwd)
        { right = view.RightDirection; up = view.UpDirection; fwd = up.CrossProduct(right); }

        private static XYZ FromScreen(double u, double v, double depth, XYZ right, XYZ up, XYZ fwd) =>
            new XYZ(u * right.X + v * up.X + depth * fwd.X,
                    u * right.Y + v * up.Y + depth * fwd.Y,
                    u * right.Z + v * up.Z + depth * fwd.Z);

        private static bool TryGetScreenRect(
            Element element, View view, XYZ right, XYZ up,
            out (double UMin, double UMax, double VMin, double VMax) rect)
        {
            rect = default;
            BoundingBoxXYZ? bb = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
            if (bb == null) return false;
            XYZ mn = bb.Min, mx = bb.Max;
            XYZ[] pts =
            {
                new XYZ(mn.X,mn.Y,mn.Z), new XYZ(mx.X,mn.Y,mn.Z),
                new XYZ(mn.X,mx.Y,mn.Z), new XYZ(mx.X,mx.Y,mn.Z),
                new XYZ(mn.X,mn.Y,mx.Z), new XYZ(mx.X,mn.Y,mx.Z),
                new XYZ(mn.X,mx.Y,mx.Z), new XYZ(mx.X,mx.Y,mx.Z),
            };
            double[] us = pts.Select(p => Dot(p, right)).ToArray();
            double[] vs = pts.Select(p => Dot(p, up)).ToArray();
            rect = (us.Min(), us.Max(), vs.Min(), vs.Max());
            return true;
        }

        private static double GetElementDepth(Element element, View view, XYZ fwd)
        {
            BoundingBoxXYZ? bb = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
            if (bb == null) return 0.0;
            return (Dot(bb.Min, fwd) + Dot(bb.Max, fwd)) / 2.0;
        }

        // ── Zone assignment ───────────────────────────────────────────────────
        private static (double TopFrac, double TopHFrac, double BotFrac, double BotHFrac, double ElFrac)
            GetThresholds(double uMin, double uMax, double vMin, double vMax)
        {
            double w = uMax - uMin, h = vMax - vMin;
            bool tall = h > 0 && (w / h) < (1.0 / TallRatio);
            return tall
                ? (TallTopBottomFraction, TallTopBottomMaxHeightFraction,
                   TallTopBottomFraction, TallTopBottomMaxHeightFraction, WideElementFraction)
                : (WideTopFraction, WideTopMaxHeightFraction,
                   WideBottomFraction, WideBottomMaxHeightFraction, WideElementFraction);
        }

        private static Dictionary<long, string> AssignZonesTwoPass(
            List<(Element El, (double UMin, double UMax, double VMin, double VMax) Rect, double Depth)> elData,
            double asmUMin, double asmUMax, double asmVMin, double asmVMax,
            double topFrac, double topHFrac, double botFrac, double botHFrac)
        {
            double w = asmUMax - asmUMin, h = asmVMax - asmVMin;
            double topThresh = asmVMax - topFrac * h;
            double botThresh = asmVMin + botFrac * h;
            double topMaxH = topHFrac * h;
            double botMaxH = botHFrac * h;

            var result = new Dictionary<long, string>();
            var unassigned = new List<(Element El, (double UMin, double UMax, double VMin, double VMax) Rect, double Depth)>();

            foreach (var item in elData)
            {
                double vc = (item.Rect.VMin + item.Rect.VMax) / 2.0;
                double elH = item.Rect.VMax - item.Rect.VMin;
                double elW = item.Rect.UMax - item.Rect.UMin;

                if (vc >= topThresh && elH <= topMaxH)
                    result[item.El.Id.Value] = "TOP";
                else if (vc <= botThresh && elH <= botMaxH)
                    result[item.El.Id.Value] = "BOTTOM";
                else
                    unassigned.Add(item);
            }

            foreach (var item in unassigned)
            {
                double uc = (item.Rect.UMin + item.Rect.UMax) / 2.0;
                double relU = w > 0 ? (uc - asmUMin) / w : 0.5;
                result[item.El.Id.Value] = relU <= 0.5 ? "LEFT" : "RIGHT";
            }

            return result;
        }

        // ── Slot layout ───────────────────────────────────────────────────────
        private struct SlotUV { public double U, V; }

        private static List<SlotUV> EvenSlotsLR(int count, double colU, double vMin, double vMax)
        {
            if (count == 0) return new List<SlotUV>();
            var slots = new List<SlotUV>();
            double step = count == 1 ? 0 : (vMax - vMin) / (count - 1);
            for (int i = 0; i < count; i++)
                slots.Add(new SlotUV { U = colU, V = count == 1 ? (vMin + vMax) / 2.0 : vMax - i * step });
            return slots;
        }

        private static List<SlotUV> EvenSlotsTB(int count, double rowV, double uMin, double uMax)
        {
            if (count == 0) return new List<SlotUV>();
            var slots = new List<SlotUV>();
            double step = count == 1 ? 0 : (uMax - uMin) / (count - 1);
            for (int i = 0; i < count; i++)
                slots.Add(new SlotUV { U = uMin + i * step, V = rowV });
            return slots;
        }

        private static double BoundaryInterceptV(
            (double UMin, double UMax, double VMin, double VMax) rect,
            string zone, double colU, double asmEdge)
        {
            bool isLeft = zone == "LEFT";
            double relX = isLeft ? (rect.UMax - colU) : (rect.UMin - colU);
            if (Math.Abs(relX) < 1e-9) return (rect.VMin + rect.VMax) / 2.0;
            double slope = (rect.VMax - rect.VMin) / (2.0 * relX);
            return (rect.VMin + rect.VMax) / 2.0 + slope * (asmEdge - colU);
        }

        private static Dictionary<long, SlotUV> BuildSlotMap(
            Dictionary<string, List<(Element El,
                (double UMin, double UMax, double VMin, double VMax) Rect,
                double Depth)>> zones,
            double colLeft, double colRight, double rowTop, double rowBottom,
            double asmUMin, double asmUMax, double asmVMin, double asmVMax)
        {
            var sm = new Dictionary<long, SlotUV>();
            foreach (var col in new[]
            {
                (Zname: "LEFT",  ColU: colLeft,  AsmEdge: asmUMin),
                (Zname: "RIGHT", ColU: colRight, AsmEdge: asmUMax)
            })
            {
                var ord = zones[col.Zname]
                    .OrderByDescending(x => BoundaryInterceptV(x.Rect, col.Zname, col.ColU, col.AsmEdge))
                    .ThenBy(x => x.Depth).ToList();
                var sl = EvenSlotsLR(ord.Count, col.ColU, asmVMin, asmVMax);
                for (int i = 0; i < ord.Count; i++) sm[ord[i].El.Id.Value] = sl[i];
            }
            foreach (var row in new[]
            {
                (RowV: rowTop,    Zname: "TOP"),
                (RowV: rowBottom, Zname: "BOTTOM")
            })
            {
                var ord = zones[row.Zname].OrderBy(x => (x.Rect.UMin + x.Rect.UMax) / 2.0).ToList();
                var sl = EvenSlotsTB(ord.Count, row.RowV, asmUMin, asmUMax);
                for (int i = 0; i < ord.Count; i++) sm[ord[i].El.Id.Value] = sl[i];
            }
            return sm;
        }

        // ── Geometry / leader endpoint ────────────────────────────────────────
        private static Options GetGeometryOptions(View view) => new Options
        { View = view, ComputeReferences = true, IncludeNonVisibleObjects = false };

        private static double XyzDist(XYZ a, XYZ b)
        { double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z; return Math.Sqrt(dx * dx + dy * dy + dz * dz); }

        private static XYZ? ClosestOnFace(Face face, XYZ pt)
        { try { return face.Project(pt)?.XYZPoint; } catch { return null; } }

        private static XYZ? GetClosestGeometryPoint(Element element, View view, XYZ probe)
        {
            GeometryElement? geom;
            try { geom = element.get_Geometry(GetGeometryOptions(view)); } catch { return null; }
            if (geom == null) return null;
            XYZ? best = null; double bestDist = double.MaxValue;
            void PS(Solid s)
            {
                if (s == null || s.Volume <= 0) return;
                try { foreach (Face f in s.Faces) { var p = ClosestOnFace(f, probe); if (p == null) continue; double d = XyzDist(p, probe); if (d < bestDist) { bestDist = d; best = p; } } } catch { }
            }
            void Walk(GeometryObject o)
            {
                try { if (o is GeometryInstance gi) foreach (var g in gi.GetInstanceGeometry()) Walk(g); else if (o is Solid sl) PS(sl); } catch { }
            }
            try { foreach (GeometryObject o in geom) Walk(o); } catch { }
            return best;
        }

        private static XYZ? GetElementCenter(Element element, View view)
        {
            BoundingBoxXYZ? bb = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
            if (bb == null) return null;
            return new XYZ((bb.Min.X + bb.Max.X) / 2.0, (bb.Min.Y + bb.Max.Y) / 2.0, (bb.Min.Z + bb.Max.Z) / 2.0);
        }

        private static Dictionary<long, XYZ> PrecomputeLeaderEndpoints(
            Dictionary<string, List<(Element El,
                (double UMin, double UMax, double VMin, double VMax) Rect,
                double Depth)>> zones,
            XYZ right, XYZ up, XYZ fwd, View view)
        {
            var lm = new Dictionary<long, XYZ>();
            foreach (var zl in zones.Values)
                foreach (var item in zl)
                {
                    double uc = (item.Rect.UMin + item.Rect.UMax) / 2.0;
                    double vc = (item.Rect.VMin + item.Rect.VMax) / 2.0;
                    XYZ probe = FromScreen(uc, vc, item.Depth, right, up, fwd);
                    lm[item.El.Id.Value] =
                        GetClosestGeometryPoint(item.El, view, probe)
                        ?? GetElementCenter(item.El, view)
                        ?? probe;
                }
            return lm;
        }

        // ── Revit helpers ─────────────────────────────────────────────────────
        private static List<Element> CollectModelElements(Document doc, View view)
        {
            var skip = new HashSet<int>
            {
                (int)BuiltInCategory.OST_Cameras,   (int)BuiltInCategory.OST_Grids,
                (int)BuiltInCategory.OST_Levels,    (int)BuiltInCategory.OST_SectionBox,
                (int)BuiltInCategory.OST_Views,     (int)BuiltInCategory.OST_RvtLinks,
            };
            var seen = new HashSet<long>(); var res = new List<Element>();
            foreach (Element el in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                if (seen.Contains(el.Id.Value)) continue;
                Category? cat = el.Category;
                if (cat == null || !cat.HasMaterialQuantities) continue;
                try { if (skip.Contains((int)cat.BuiltInCategory)) continue; } catch { continue; }
                seen.Add(el.Id.Value); res.Add(el);
            }
            return res;
        }

    }

    // ============================================================
    //  BUTTON 2 — CMD_VENDORTAG  (Vendor Drawings)
    // ============================================================
    [Transaction(TransactionMode.Manual)]
    public class Cmd_VendorTag : IExternalCommand
    {
        private const double EdgeClearanceFt = 1.0;
        private const double MinTagSpacingFt = 0.40;
        private const bool LeaderEnabled = true;
        private const double TallRatio = 1.4;
        private const double TallTopBottomFraction = 0.05;
        private const double TallTopBottomMaxHeightFraction = 0.10;
        private const double WideTopFraction = 0.30;
        private const double WideTopMaxHeightFraction = 0.50;
        private const double WideBottomFraction = 0.05;
        private const double WideBottomMaxHeightFraction = 0.10;
        private const double WideElementFraction = 0.55;
        private const double ClearThreshold = 0.08;
        private const double ColumnVerticalPaddingFt = 0.5;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;
            var doc = uiDoc.Document;

            // 1. Collect all 3D views on sheets
            var entries = BatchViewPickerWindow.CollectEntries(doc);
            if (entries.Count == 0)
            {
                TaskDialog.Show("Vendor Auto Tag", "No 3D views found on any sheet in this project.");
                return Result.Failed;
            }

            // 2. Single window: pick tag family (in-window section) + multi-select views
            var picker = new BatchViewPickerWindow(
                entries, doc, BuiltInCategory.OST_GenericModelTags,
                "Vendor Tag Family", "▶  Run Script");
            if (picker.ShowDialog() != true
                || picker.SelectedViews.Count == 0
                || picker.SelectedTagFamilyId == ElementId.InvalidElementId)
                return Result.Cancelled;

            ElementId tagTypeId = picker.SelectedTagFamilyId;
            List<View3D> selectedViews = picker.SelectedViews;

            // 3. Tag each selected view, collect structured log rows
            int totalCreated = 0, totalSkipped = 0;
            var logRows = new List<ViewLogRow>();

            var sheetByViewId = new Dictionary<ElementId, string>();
            foreach (ViewSheet vs in new FilteredElementCollector(doc)
                                         .OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
                foreach (ElementId vpId in vs.GetAllViewports())
                    if (doc.GetElement(vpId) is Viewport vp)
                        sheetByViewId[vp.ViewId] = vs.SheetNumber;

            foreach (View3D view in selectedViews)
            {
                sheetByViewId.TryGetValue(view.Id, out string? sheetNum);
                var row = new ViewLogRow
                {
                    ViewName = view.Name,
                    SheetNum = sheetNum ?? "\u2014"
                };

                List<Element> modelElements = CollectModelElements(doc, view);
                if (modelElements.Count == 0)
                {
                    row.Status = "skip"; row.Notes = "No taggable elements";
                    logRows.Add(row); continue;
                }

                GetViewAxes(view, out XYZ right, out XYZ up, out XYZ fwd);

                // Build full elData for ALL elements (needed for assembly bounds)
                var elDataAll = new List<(Element El,
                    (double UMin, double UMax, double VMin, double VMax) Rect,
                    double Depth)>();
                foreach (Element el in modelElements)
                {
                    if (!TryGetScreenRect(el, view, right, up, out var rect)) continue;
                    elDataAll.Add((el, rect, GetElementDepth(el, view, fwd)));
                }

                if (elDataAll.Count == 0)
                {
                    row.Status = "skip"; row.Notes = "Could not project elements";
                    logRows.Add(row); continue;
                }

                // Select representatives: group by Comments, pick top N by screen area
                var elData = SelectRepresentativesByComments(elDataAll);

                if (elData.Count == 0)
                {
                    row.Status = "skip"; row.Notes = "No representatives selected";
                    logRows.Add(row); continue;
                }

                // Assembly bounds come from ALL elements so slot columns/rows
                // sit at the true assembly edges
                double asmUMin = elDataAll.Min(e => e.Rect.UMin);
                double asmUMax = elDataAll.Max(e => e.Rect.UMax);
                double asmVMin = elDataAll.Min(e => e.Rect.VMin);
                double asmVMax = elDataAll.Max(e => e.Rect.VMax);
                double medDepth = elDataAll.Select(e => e.Depth).OrderBy(d => d)
                                           .ElementAt(elDataAll.Count / 2);

                double colLeft = asmUMin - EdgeClearanceFt;
                double colRight = asmUMax + EdgeClearanceFt;
                double rowTop = asmVMax + EdgeClearanceFt;
                double rowBottom = asmVMin - EdgeClearanceFt;

                var (topFrac, topHFrac, botFrac, botHFrac, _) =
                    GetThresholds(asmUMin, asmUMax, asmVMin, asmVMax);

                // Zone assignment and slot layout use only the representatives
                var zoneMap = AssignZonesTwoPass(elData,
                    asmUMin, asmUMax, asmVMin, asmVMax,
                    topFrac, topHFrac, botFrac, botHFrac);

                var zones = new Dictionary<string, List<(Element El,
                    (double UMin, double UMax, double VMin, double VMax) Rect,
                    double Depth)>>
                {
                    ["LEFT"] = new List<(Element, (double, double, double, double), double)>(),
                    ["RIGHT"] = new List<(Element, (double, double, double, double), double)>(),
                    ["TOP"] = new List<(Element, (double, double, double, double), double)>(),
                    ["BOTTOM"] = new List<(Element, (double, double, double, double), double)>(),
                };
                foreach (var entry in elData) zones[zoneMap[entry.El.Id.Value]].Add(entry);

                var leaderMap = PrecomputeLeaderEndpoints(zones, right, up, fwd, view);
                var slotMap = BuildSlotMap(zones, colLeft, colRight, rowTop, rowBottom,
                                             asmUMin, asmUMax, asmVMin, asmVMax);
                var allOrdered = zones["RIGHT"].Concat(zones["LEFT"])
                                              .Concat(zones["TOP"])
                                              .Concat(zones["BOTTOM"]).ToList();

                int created = 0;
                var warnMsgs = new List<string>();
                var skippedIds = new List<long>();

                // Tx 1: lock the view (tags require a locked 3D view)
                using (var tLock = new Transaction(doc, "Lock view: " + view.Name))
                {
                    tLock.Start();
                    try { view.SaveOrientationAndLock(); } catch { }
                    tLock.Commit();
                }

                // Tx 2: place tags into the now-locked view
                using (var t = new Transaction(doc, "Vendor Auto Tag: " + view.Name))
                {
                    t.Start();
                    foreach (var entry in allOrdered)
                    {
                        Element el = entry.El;
                        try
                        {
                            long elKey = el.Id.Value;
                            var slot = slotMap[elKey];
                            XYZ head = FromScreen(slot.U, slot.V, medDepth, right, up, fwd);

                            if (!leaderMap.TryGetValue(elKey, out XYZ? lEnd) || lEnd == null)
                            { skippedIds.Add(elKey); continue; }

                            IndependentTag tag = IndependentTag.Create(
                                doc, tagTypeId, view.Id,
                                new Reference(el), LeaderEnabled,
                                TagOrientation.Horizontal, head);

                            if (tag == null) { skippedIds.Add(elKey); continue; }
                            try { tag.TagHeadPosition = head; } catch { }

                            if (LeaderEnabled)
                            {
                                try
                                {
                                    tag.LeaderEndCondition = LeaderEndCondition.Free;
                                    tag.SetLeaderEnd(new Reference(el), lEnd);
                                }
                                catch { }
                            }
                            created++;
                        }
                        catch (Exception ex)
                        {
                            skippedIds.Add(el.Id.Value);
                            warnMsgs.Add(ex.Message);
                        }
                    }
                    t.Commit();
                }

                row.Created = created;
                row.Skipped = skippedIds.Count;
                row.Status = skippedIds.Count == 0 ? "ok" : "warn";
                row.Notes = warnMsgs.Count > 0
                    ? warnMsgs[0] + (warnMsgs.Count > 1
                        ? string.Format(" (+{0} more)", warnMsgs.Count - 1) : "")
                    : "";
                logRows.Add(row);
                totalCreated += created;
                totalSkipped += skippedIds.Count;
            }

            new AutoTagResultsWindow(
                totalCreated, totalSkipped, selectedViews.Count, logRows,
                "Vendor Auto Tag \u2014 Results")
                .ShowDialog();

            return Result.Succeeded;
        }

        // ── Comments-group representative selection ───────────────────────────
        // Rules:
        //   group size 1 → tag 1   (100%)
        //   group size 2 → tag 2   (100%, 30% of 2 < 1)
        //   group size 3+ → tag ceil(count * 0.30), pick by largest screen area
        private static List<(Element El,
            (double UMin, double UMax, double VMin, double VMax) Rect,
            double Depth)>
            SelectRepresentativesByComments(
                List<(Element El,
                    (double UMin, double UMax, double VMin, double VMax) Rect,
                    double Depth)> all)
        {
            // Group by Comments parameter value; elements with null/empty Comments
            // each form their own singleton group so they are still tagged.
            var groups = all.GroupBy(e =>
            {
                Parameter? p = e.El.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                string v = p?.AsString() ?? "";
                // Use element id as fallback key so blank-comment elements don't merge
                return string.IsNullOrWhiteSpace(v) ? "__NO_COMMENT_" + e.El.Id.Value : v;
            });

            var result = new List<(Element El,
                (double UMin, double UMax, double VMin, double VMax) Rect,
                double Depth)>();

            foreach (var group in groups)
            {
                var members = group.ToList();
                int total = members.Count;

                int pick = total <= 2
                    ? total
                    : (int)Math.Ceiling(total * 0.30);

                // Sort descending by screen-projected area, take top N
                var top = members
                    .OrderByDescending(e =>
                        (e.Rect.UMax - e.Rect.UMin) * (e.Rect.VMax - e.Rect.VMin))
                    .Take(pick);

                result.AddRange(top);
            }

            return result;
        }

        // ── Projection helpers (copy — shared with Cmd_MTQTag) ────────────────
        private static double Dot(XYZ a, XYZ b) =>
            a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        private static void GetViewAxes(View view, out XYZ right, out XYZ up, out XYZ fwd)
        { right = view.RightDirection; up = view.UpDirection; fwd = up.CrossProduct(right); }

        private static XYZ FromScreen(double u, double v, double depth, XYZ right, XYZ up, XYZ fwd) =>
            new XYZ(u * right.X + v * up.X + depth * fwd.X,
                    u * right.Y + v * up.Y + depth * fwd.Y,
                    u * right.Z + v * up.Z + depth * fwd.Z);

        private static bool TryGetScreenRect(
            Element element, View view, XYZ right, XYZ up,
            out (double UMin, double UMax, double VMin, double VMax) rect)
        {
            rect = default;
            BoundingBoxXYZ? bb = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
            if (bb == null) return false;
            XYZ mn = bb.Min, mx = bb.Max;
            XYZ[] pts =
            {
                new XYZ(mn.X,mn.Y,mn.Z), new XYZ(mx.X,mn.Y,mn.Z),
                new XYZ(mn.X,mx.Y,mn.Z), new XYZ(mx.X,mx.Y,mn.Z),
                new XYZ(mn.X,mn.Y,mx.Z), new XYZ(mx.X,mn.Y,mx.Z),
                new XYZ(mn.X,mx.Y,mx.Z), new XYZ(mx.X,mx.Y,mx.Z),
            };
            double[] us = pts.Select(p => Dot(p, right)).ToArray();
            double[] vs = pts.Select(p => Dot(p, up)).ToArray();
            rect = (us.Min(), us.Max(), vs.Min(), vs.Max());
            return true;
        }

        private static double GetElementDepth(Element element, View view, XYZ fwd)
        {
            BoundingBoxXYZ? bb = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
            if (bb == null) return 0.0;
            return (Dot(bb.Min, fwd) + Dot(bb.Max, fwd)) / 2.0;
        }

        private static (double TopFrac, double TopHFrac, double BotFrac, double BotHFrac, double ElFrac)
            GetThresholds(double uMin, double uMax, double vMin, double vMax)
        {
            double w = uMax - uMin, h = vMax - vMin;
            bool tall = h > 0 && (w / h) < (1.0 / TallRatio);
            return tall
                ? (TallTopBottomFraction, TallTopBottomMaxHeightFraction,
                   TallTopBottomFraction, TallTopBottomMaxHeightFraction, WideElementFraction)
                : (WideTopFraction, WideTopMaxHeightFraction,
                   WideBottomFraction, WideBottomMaxHeightFraction, WideElementFraction);
        }

        private static Dictionary<long, string> AssignZonesTwoPass(
            List<(Element El, (double UMin, double UMax, double VMin, double VMax) Rect, double Depth)> elData,
            double asmUMin, double asmUMax, double asmVMin, double asmVMax,
            double topFrac, double topHFrac, double botFrac, double botHFrac)
        {
            double h = asmVMax - asmVMin;
            double topThresh = asmVMax - topFrac * h;
            double botThresh = asmVMin + botFrac * h;
            double topMaxH = topHFrac * h;
            double botMaxH = botHFrac * h;

            var result = new Dictionary<long, string>();
            var unassigned = new List<(Element El, (double UMin, double UMax, double VMin, double VMax) Rect, double Depth)>();

            foreach (var item in elData)
            {
                double vc = (item.Rect.VMin + item.Rect.VMax) / 2.0;
                double elH = item.Rect.VMax - item.Rect.VMin;

                if (vc >= topThresh && elH <= topMaxH)
                    result[item.El.Id.Value] = "TOP";
                else if (vc <= botThresh && elH <= botMaxH)
                    result[item.El.Id.Value] = "BOTTOM";
                else
                    unassigned.Add(item);
            }

            foreach (var item in unassigned)
            {
                double w = asmUMax - asmUMin;
                double uc = (item.Rect.UMin + item.Rect.UMax) / 2.0;
                double relU = w > 0 ? (uc - asmUMin) / w : 0.5;
                result[item.El.Id.Value] = relU <= 0.5 ? "LEFT" : "RIGHT";
            }

            return result;
        }

        private struct SlotUV { public double U, V; }

        private static List<SlotUV> EvenSlotsLR(int count, double colU, double vMin, double vMax)
        {
            if (count == 0) return new List<SlotUV>();
            var slots = new List<SlotUV>();
            double step = count == 1 ? 0 : (vMax - vMin) / (count - 1);
            for (int i = 0; i < count; i++)
                slots.Add(new SlotUV { U = colU, V = count == 1 ? (vMin + vMax) / 2.0 : vMax - i * step });
            return slots;
        }

        private static List<SlotUV> EvenSlotsTB(int count, double rowV, double uMin, double uMax)
        {
            if (count == 0) return new List<SlotUV>();
            var slots = new List<SlotUV>();
            double step = count == 1 ? 0 : (uMax - uMin) / (count - 1);
            for (int i = 0; i < count; i++)
                slots.Add(new SlotUV { U = uMin + i * step, V = rowV });
            return slots;
        }

        private static double BoundaryInterceptV(
            (double UMin, double UMax, double VMin, double VMax) rect,
            string zone, double colU, double asmEdge)
        {
            bool isLeft = zone == "LEFT";
            double relX = isLeft ? (rect.UMax - colU) : (rect.UMin - colU);
            if (Math.Abs(relX) < 1e-9) return (rect.VMin + rect.VMax) / 2.0;
            double slope = (rect.VMax - rect.VMin) / (2.0 * relX);
            return (rect.VMin + rect.VMax) / 2.0 + slope * (asmEdge - colU);
        }

        private static Dictionary<long, SlotUV> BuildSlotMap(
            Dictionary<string, List<(Element El,
                (double UMin, double UMax, double VMin, double VMax) Rect,
                double Depth)>> zones,
            double colLeft, double colRight, double rowTop, double rowBottom,
            double asmUMin, double asmUMax, double asmVMin, double asmVMax)
        {
            var sm = new Dictionary<long, SlotUV>();
            foreach (var col in new[]
            {
                (Zname: "LEFT",  ColU: colLeft,  AsmEdge: asmUMin),
                (Zname: "RIGHT", ColU: colRight, AsmEdge: asmUMax)
            })
            {
                var ord = zones[col.Zname]
                    .OrderByDescending(x => BoundaryInterceptV(x.Rect, col.Zname, col.ColU, col.AsmEdge))
                    .ThenBy(x => x.Depth).ToList();
                var sl = EvenSlotsLR(ord.Count, col.ColU, asmVMin, asmVMax);
                for (int i = 0; i < ord.Count; i++) sm[ord[i].El.Id.Value] = sl[i];
            }
            foreach (var row in new[]
            {
                (RowV: rowTop,    Zname: "TOP"),
                (RowV: rowBottom, Zname: "BOTTOM")
            })
            {
                var ord = zones[row.Zname].OrderBy(x => (x.Rect.UMin + x.Rect.UMax) / 2.0).ToList();
                var sl = EvenSlotsTB(ord.Count, row.RowV, asmUMin, asmUMax);
                for (int i = 0; i < ord.Count; i++) sm[ord[i].El.Id.Value] = sl[i];
            }
            return sm;
        }

        private static Options GetGeometryOptions(View view) => new Options
        { View = view, ComputeReferences = true, IncludeNonVisibleObjects = false };

        private static double XyzDist(XYZ a, XYZ b)
        { double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z; return Math.Sqrt(dx * dx + dy * dy + dz * dz); }

        private static XYZ? ClosestOnFace(Face face, XYZ pt)
        { try { return face.Project(pt)?.XYZPoint; } catch { return null; } }

        private static XYZ? GetClosestGeometryPoint(Element element, View view, XYZ probe)
        {
            GeometryElement? geom;
            try { geom = element.get_Geometry(GetGeometryOptions(view)); } catch { return null; }
            if (geom == null) return null;
            XYZ? best = null; double bestDist = double.MaxValue;
            void PS(Solid s)
            {
                if (s == null || s.Volume <= 0) return;
                try { foreach (Face f in s.Faces) { var p = ClosestOnFace(f, probe); if (p == null) continue; double d = XyzDist(p, probe); if (d < bestDist) { bestDist = d; best = p; } } } catch { }
            }
            void Walk(GeometryObject o)
            {
                try { if (o is GeometryInstance gi) foreach (var g in gi.GetInstanceGeometry()) Walk(g); else if (o is Solid sl) PS(sl); } catch { }
            }
            try { foreach (GeometryObject o in geom) Walk(o); } catch { }
            return best;
        }

        private static XYZ? GetElementCenter(Element element, View view)
        {
            BoundingBoxXYZ? bb = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
            if (bb == null) return null;
            return new XYZ((bb.Min.X + bb.Max.X) / 2.0, (bb.Min.Y + bb.Max.Y) / 2.0, (bb.Min.Z + bb.Max.Z) / 2.0);
        }

        private static Dictionary<long, XYZ> PrecomputeLeaderEndpoints(
            Dictionary<string, List<(Element El,
                (double UMin, double UMax, double VMin, double VMax) Rect,
                double Depth)>> zones,
            XYZ right, XYZ up, XYZ fwd, View view)
        {
            var lm = new Dictionary<long, XYZ>();
            foreach (var zl in zones.Values)
                foreach (var item in zl)
                {
                    double uc = (item.Rect.UMin + item.Rect.UMax) / 2.0;
                    double vc = (item.Rect.VMin + item.Rect.VMax) / 2.0;
                    XYZ probe = FromScreen(uc, vc, item.Depth, right, up, fwd);
                    lm[item.El.Id.Value] =
                        GetClosestGeometryPoint(item.El, view, probe)
                        ?? GetElementCenter(item.El, view)
                        ?? probe;
                }
            return lm;
        }

        private static List<Element> CollectModelElements(Document doc, View view)
        {
            var skip = new HashSet<int>
            {
                (int)BuiltInCategory.OST_Cameras,   (int)BuiltInCategory.OST_Grids,
                (int)BuiltInCategory.OST_Levels,    (int)BuiltInCategory.OST_SectionBox,
                (int)BuiltInCategory.OST_Views,     (int)BuiltInCategory.OST_RvtLinks,
                (int)BuiltInCategory.OST_Assemblies,
            };
            var seen = new HashSet<long>(); var res = new List<Element>();
            foreach (Element el in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                if (seen.Contains(el.Id.Value)) continue;
                Category? cat = el.Category;
                if (cat == null) continue;

                // Always include Generic Model families, even if HasMaterialQuantities is false
                bool isGenericModel = false;
                try { isGenericModel = cat.BuiltInCategory == BuiltInCategory.OST_GenericModel; } catch { }

                if (!isGenericModel)
                {
                    if (!cat.HasMaterialQuantities) continue;
                    try { if (skip.Contains((int)cat.BuiltInCategory)) continue; } catch { continue; }
                }

                seen.Add(el.Id.Value); res.Add(el);
            }
            return res;
        }

    }
}