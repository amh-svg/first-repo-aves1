// System
// Autodesk
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AVES.Core;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfClipboard = System.Windows.Clipboard;
// WPF aliases — resolves all ambiguities with Autodesk.Revit.DB / Autodesk.Revit.UI
using WpfColor = System.Windows.Media.Color;
using WpfControl = System.Windows.Controls.Control;        // fixes Control ambiguity
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfDataGridCell = System.Windows.Controls.DataGridCell;
using WpfDataGridColumnHeader = System.Windows.Controls.Primitives.DataGridColumnHeader;
using WpfDataGridRow = System.Windows.Controls.DataGridRow;
using WpfDataGridTemplateColumn = System.Windows.Controls.DataGridTemplateColumn;
using WpfDataGridTextColumn = System.Windows.Controls.DataGridTextColumn;
using WpfDataTemplate = System.Windows.DataTemplate;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfFontWeights = System.Windows.FontWeights;
using WpfGrid = System.Windows.Controls.Grid;
using WpfHAlign = System.Windows.HorizontalAlignment;
using WpfImage = System.Windows.Controls.Image;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfRowDef = System.Windows.Controls.RowDefinition;
using WpfScrollBarVis = System.Windows.Controls.ScrollBarVisibility;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;
using WpfSelMode = System.Windows.Controls.SelectionMode;
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;        // fixes TextBox ambiguity
using WpfThickness = System.Windows.Thickness;
using WpfVisibility = System.Windows.Visibility;

namespace AVES.Cmds_Button7
{
    // ─────────────────────────────────────────────────────────────────────────
    //  DATA HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    internal class InstanceData
    {
        public ElementId Id { get; set; }
        public Element Instance { get; set; }
        public bool HasLength { get; set; }
        public int LengthMm { get; set; }
        public string Title { get; set; }
        public string PartNumber { get; set; }
        public string Description { get; set; }
        public string PartNumberMerge { get; set; }
        public string DescriptionMerge { get; set; }
    }

    internal class PartListRow
    {
        public string PartNumberMerge { get; set; }
        public string RowNumber { get; set; }
        public string Title { get; set; }
        public string PartNumber { get; set; }
        public string Description { get; set; }   // third discriminator for Non-Length rows
        public int LengthMm { get; set; }
        public bool HasLength { get; set; }
    }

    /// <summary>One row in the Unified Report — one per processed assembly.</summary>
    internal class ReportRow
    {
        public string AssemblyName { get; set; }
        public string StatusKind { get; set; }   // "ok" | "skip" | "fail" | "warn"
        public string Note { get; set; } = "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  WPF ASSEMBLY PICKER WINDOW  — AVES Refined Native / AVES Red
    // ─────────────────────────────────────────────────────────────────────────

    internal class AssemblyPickerWindow : Window
    {
        public List<AssemblyInstance> SelectedAssemblies { get; private set; }
            = new List<AssemblyInstance>();

        private readonly List<AssemblyInstance> _allAssemblies;
        private readonly WpfListBox _listBox;
        private readonly WpfTextBox _searchBox;
        private bool _confirmed;

        public AssemblyPickerWindow(List<AssemblyInstance> assemblies)
        {
            _allAssemblies = assemblies
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // ── Window chrome ─────────────────────────────────────────────────
            Title = "Part List Format";
            Width = 460;
            Height = 600;
            MinWidth = 360;
            MinHeight = 340;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = AvTokens.B(AvTokens.Surface);
            FontFamily = AvTokens.Ui;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            // ── Outer shell: title bar + body + footer ────────────────────────
            var shell = new WpfGrid();
            shell.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(44) });            // title bar
            shell.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(1, GridUnitType.Star) }); // body
            shell.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });               // footer
            Content = shell;

            // ── Title bar (white, 1px bottom border) ──────────────────────────
            var titleBar = new System.Windows.Controls.Border
            {
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(0, 0, 0, 1),
                Padding = new WpfThickness(20, 0, 20, 0)
            };
            var titleRow = new WpfStackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleRow.Children.Add(AvControls.BrandTile(26));
            titleRow.Children.Add(new WpfTextBlock
            {
                Text = "Part List Format",
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                FontWeight = WpfFontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.Text),
                Margin = new WpfThickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleRow.Children.Add(new WpfTextBlock
            {
                Text = "",
                FontFamily = AvTokens.Ui,
                FontSize = 12,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                Margin = new WpfThickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleBar.Child = titleRow;
            WpfGrid.SetRow(titleBar, 0);
            shell.Children.Add(titleBar);

            // ── Body (20px padding) ───────────────────────────────────────────
            var body = new WpfGrid { Margin = new WpfThickness(20, 16, 20, 16) };
            body.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto }); // section label
            body.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto }); // search
            body.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto }); // sel-all row
            body.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(1, GridUnitType.Star) }); // list
            WpfGrid.SetRow(body, 1);
            shell.Children.Add(body);

            // Section label — numbered like the other AVES settings windows
            var sectionLabel = new WpfStackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                Margin = new WpfThickness(0, 0, 0, 10)
            };
            sectionLabel.Children.Add(AvControls.NumberBadge(1));
            sectionLabel.Children.Add(new WpfTextBlock
            {
                Text = "Select Assemblies",
                FontFamily = AvTokens.Ui,
                FontSize = 12.5,
                FontWeight = WpfFontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Center
            });
            WpfGrid.SetRow(sectionLabel, 0);
            body.Children.Add(sectionLabel);

            // ── Search box (Surface2 bg, 1px BorderStrong, radius 5) ──────────
            var searchWrap = new System.Windows.Controls.Border
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new WpfThickness(0, 0, 0, 10),
                Padding = new WpfThickness(10, 0, 10, 0),
                Height = 34
            };
            // magnifier glyph + text box in a row
            var searchRow = new WpfStackPanel { Orientation = WpfOrientation.Horizontal };
            searchRow.Children.Add(new WpfTextBlock
            {
                Text = "⌕",
                FontSize = 15,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new WpfThickness(0, 0, 7, 0)
            });
            _searchBox = new WpfTextBox
            {
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                Foreground = AvTokens.B(AvTokens.Text),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new WpfThickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 200,
                ToolTip = "Filter assemblies by name"
            };
            _searchBox.TextChanged += (s, e) => RefreshList();
            searchRow.Children.Add(_searchBox);
            searchWrap.Child = searchRow;
            WpfGrid.SetRow(searchWrap, 1);
            body.Children.Add(searchWrap);

            // ── Select All / Deselect All row ─────────────────────────────────
            var selRow = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 8) };
            selRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            selRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
            selRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            selRow.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

            var btnSelAll = AvControls.SecondaryBtn("Select All", () => SetAllSelection(true));
            btnSelAll.Foreground = AvTokens.B("#E64A2E");
            var btnSelNone = AvControls.SecondaryBtn("Deselect All", () => SetAllSelection(false));
            btnSelAll.Margin = new WpfThickness(0, 0, 6, 0);
            btnSelNone.Margin = new WpfThickness(0, 0, 0, 0);

            // count badge on the right
            var countLabel = new WpfTextBlock
            {
                FontFamily = AvTokens.Ui,
                FontSize = 11,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = WpfHAlign.Right,
                Name = "CountLabel",
                Text = $"{_allAssemblies.Count} assemblies"
            };

            WpfGrid.SetColumn(btnSelAll, 0);
            WpfGrid.SetColumn(btnSelNone, 1);
            WpfGrid.SetColumn(countLabel, 3);
            selRow.Children.Add(btnSelAll);
            selRow.Children.Add(btnSelNone);
            selRow.Children.Add(countLabel);
            WpfGrid.SetRow(selRow, 2);
            body.Children.Add(selRow);

            // ── ListBox (AVES-styled, mouse-wheel scrollable, checkbox rows) ──
            _listBox = new WpfListBox
            {
                SelectionMode = WpfSelMode.Multiple,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                Foreground = AvTokens.B(AvTokens.Text),
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(1),
                Padding = new WpfThickness(0)
            };

            // Item container style: 40px tall rows, accent-tint when selected
            // (kept for keyboard nav / focus visuals — visual check state lives
            // in the per-row checkbox glyph built in RefreshList)
            var itemStyle = new System.Windows.Style(typeof(WpfListBoxItem));
            itemStyle.Setters.Add(new System.Windows.Setter(
                WpfListBoxItem.PaddingProperty, new WpfThickness(14, 0, 14, 0)));
            itemStyle.Setters.Add(new System.Windows.Setter(
                WpfListBoxItem.HeightProperty, 40.0));
            itemStyle.Setters.Add(new System.Windows.Setter(
                WpfListBoxItem.FontFamilyProperty, AvTokens.Ui));
            itemStyle.Setters.Add(new System.Windows.Setter(
                WpfListBoxItem.FontSizeProperty, 13.0));
            itemStyle.Setters.Add(new System.Windows.Setter(
                WpfListBoxItem.ForegroundProperty, AvTokens.B(AvTokens.Text)));
            itemStyle.Setters.Add(new System.Windows.Setter(
                WpfListBoxItem.BackgroundProperty, System.Windows.Media.Brushes.Transparent));

            // The default ListBoxItem template paints its own selection highlight
            // over any plain Background setter/trigger, so a custom template is
            // required for the accent-tint background to actually render.
            var itemTemplate = new System.Windows.Controls.ControlTemplate(typeof(WpfListBoxItem));
            var itemBorder = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            itemBorder.Name = "RowBorder";
            itemBorder.SetBinding(System.Windows.Controls.Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            itemBorder.SetBinding(System.Windows.Controls.Border.PaddingProperty,
                new System.Windows.Data.Binding("Padding")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var itemContent = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            itemContent.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            itemBorder.AppendChild(itemContent);
            itemTemplate.VisualTree = itemBorder;
            itemStyle.Setters.Add(new System.Windows.Setter(WpfListBoxItem.TemplateProperty, itemTemplate));

            var selectedTrigger = new System.Windows.Trigger
            {
                Property = WpfListBoxItem.IsSelectedProperty,
                Value = true
            };
            selectedTrigger.Setters.Add(new System.Windows.Setter(
                WpfListBoxItem.BackgroundProperty, AvTokens.B(AvTokens.AccentTint)));
            itemStyle.Triggers.Add(selectedTrigger);
            _listBox.ItemContainerStyle = itemStyle;
            _listBox.PreviewMouseWheel += ListBox_PreviewMouseWheel;

            // update count label + checkbox glyphs when selection changes
            _listBox.SelectionChanged += (s, e) =>
            {
                foreach (var removed in e.RemovedItems.OfType<WpfListBoxItem>())
                    SyncRowVisual(removed);
                foreach (var added in e.AddedItems.OfType<WpfListBoxItem>())
                    SyncRowVisual(added);

                int sel = _listBox.SelectedItems.Count;
                int total = _listBox.Items.Count;
                countLabel.Text = sel > 0
                    ? $"{sel} of {total} selected"
                    : $"{total} assemblies";
            };

            // SelectionMode.Multiple normally requires Ctrl/Shift to add to an
            // existing selection — a plain click replaces it. Since our rows are
            // checkboxes, a plain click should always TOGGLE that row without
            // touching the others. We intercept on PreviewMouseLeftButtonDown
            // (before the native selection logic runs) and handle it ourselves.
            _listBox.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is System.Windows.DependencyObject d)
                {
                    var item = System.Windows.Controls.ItemsControl
                        .ContainerFromElement(_listBox, d) as WpfListBoxItem;
                    if (item != null)
                    {
                        item.IsSelected = !item.IsSelected;
                        e.Handled = true; // prevent native click-replaces-selection behavior
                    }
                }
            };

            var scrollViewer = new WpfScrollViewer
            {
                VerticalScrollBarVisibility = WpfScrollBarVis.Auto,
                HorizontalScrollBarVisibility = WpfScrollBarVis.Disabled,
                Content = _listBox
            };
            WpfGrid.SetRow(scrollViewer, 3);
            body.Children.Add(scrollViewer);

            // ── Footer (Surface2, top border, 14/20 padding) ──────────────────
            var footer = new System.Windows.Controls.Border
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(0, 1, 0, 0),
                Padding = new WpfThickness(20, 14, 20, 14)
            };
            var footerRow = new WpfStackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHAlign.Right
            };
            var btnCancel = AvControls.GhostBtn("Cancel", OnCancel);
            var btnOk = AvControls.PrimaryBtn("▶  Run Script", OnOk);
            btnCancel.Margin = new WpfThickness(0, 0, 8, 0);
            footerRow.Children.Add(btnCancel);
            footerRow.Children.Add(btnOk);
            footer.Child = footerRow;
            WpfGrid.SetRow(footer, 2);
            shell.Children.Add(footer);

            RefreshList();
        }

        // ── List refresh: build AVES checkbox rows ──────────────────────────
        private void RefreshList()
        {
            var filter = _searchBox?.Text?.Trim() ?? string.Empty;
            _listBox.Items.Clear();
            foreach (var asm in _allAssemblies)
            {
                if (filter.Length == 0 ||
                    asm.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var item = new WpfListBoxItem
                    {
                        Tag = asm,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Content = BuildCheckboxRow(asm.Name, isChecked: false)
                    };
                    _listBox.Items.Add(item);
                }
            }
        }

        // ── Builds a single AVES checkbox row: 17px rounded-5 box + label ────
        private static System.Windows.UIElement BuildCheckboxRow(string label, bool isChecked)
        {
            var box = new System.Windows.Controls.Border
            {
                Width = 17,
                Height = 17,
                CornerRadius = new CornerRadius(5),
                Background = isChecked ? AvTokens.B(AvTokens.Accent) : System.Windows.Media.Brushes.Transparent,
                BorderBrush = isChecked ? AvTokens.B(AvTokens.Accent) : AvTokens.B(AvTokens.BorderStrong),
                BorderThickness = new WpfThickness(1.5),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new WpfThickness(0, 0, 10, 0)
            };
            if (isChecked)
            {
                box.Child = new WpfTextBlock
                {
                    Text = "✓",
                    FontSize = 11,
                    FontWeight = WpfFontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = WpfHAlign.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new WpfThickness(0, -1, 0, 0)
                };
            }

            var text = new WpfTextBlock
            {
                Text = label,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                Foreground = isChecked ? AvTokens.B(AvTokens.Accent) : AvTokens.B(AvTokens.Text),
                FontWeight = isChecked ? WpfFontWeights.SemiBold : WpfFontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new WpfStackPanel { Orientation = WpfOrientation.Horizontal };
            row.Children.Add(box);
            row.Children.Add(text);
            return row;
        }

        // ── Refreshes a row's checkbox glyph to match its IsSelected state ───
        private static void SyncRowVisual(WpfListBoxItem item)
        {
            if (item?.Tag is AssemblyInstance asm)
                item.Content = BuildCheckboxRow(asm.Name, item.IsSelected);
        }

        // ── Select / deselect all visible ────────────────────────────────────
        private void SetAllSelection(bool select)
        {
            foreach (WpfListBoxItem item in _listBox.Items)
            {
                item.IsSelected = select;
                SyncRowVisual(item);
            }
        }

        // ── Mouse-wheel passthrough fix ───────────────────────────────────────
        private void ListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;
            var args = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
                Source = sender
            };
            var parent = ((WpfControl)sender).Parent as UIElement;
            parent?.RaiseEvent(args);
        }

        // ── OK ───────────────────────────────────────────────────────────────
        private void OnOk()
        {
            SelectedAssemblies = _listBox.SelectedItems
                .Cast<WpfListBoxItem>()
                .Select(item => item.Tag as AssemblyInstance)
                .Where(a => a != null)
                .ToList();
            _confirmed = true;
            Close();
        }

        // ── Cancel ───────────────────────────────────────────────────────────
        private void OnCancel() => Close();

        protected override void OnClosed(EventArgs e)
        {
            if (!_confirmed) SelectedAssemblies = new List<AssemblyInstance>();
            base.OnClosed(e);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UNIFIED REPORT WINDOW  (AVES Refined Native — "the hero")
    //  Body: A. Run summary (stat tiles + config panel)
    //        B. Results per assembly (status-pill table)
    //        C. Footer (source caption · copy to clipboard · close)
    // ─────────────────────────────────────────────────────────────────────────

    internal class ReportWindow : Window
    {
        private readonly List<ReportRow> _rows;
        private readonly string _sourceCaption;

        public ReportWindow(
            List<ReportRow> rows,
            int processed, int skipped, int failed,
            int assembliesFound,
            string docTitle)
        {
            _rows = rows;
            _sourceCaption = $"Source · {docTitle}";

            // ── Window chrome ────────────────────────────────────────────────
            Title = "Part List Format — Report";
            Width = 760;
            Height = 620;
            MinWidth = 600;
            MinHeight = 420;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = AvTokens.B(AvTokens.Surface2);
            FontFamily = AvTokens.Ui;

            var root = new WpfGrid();
            root.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });  // title bar
            root.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(1, GridUnitType.Star) }); // body
            root.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });  // footer
            Content = root;

            // ── Title bar ────────────────────────────────────────────────────
            var titleBar = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(0, 0, 0, 1),
                Padding = new WpfThickness(20, 0, 20, 0),
                Height = 52
            };
            var titleRow = new WpfStackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleRow.Children.Add(AvControls.BrandTile(24));
            var titleTextStack = new WpfStackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new WpfThickness(10, 0, 0, 0)
            };
            titleTextStack.Children.Add(new WpfTextBlock
            {
                Text = "Part List Format",
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                FontWeight = WpfFontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.Text)
            });
            titleTextStack.Children.Add(new WpfTextBlock
            {
                Text = $"  — {processed} of {assembliesFound} assemblies processed",
                FontFamily = AvTokens.Ui,
                FontSize = 12,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                Margin = new WpfThickness(2, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Bottom
            });
            titleRow.Children.Add(titleTextStack);
            titleBar.Child = titleRow;
            WpfGrid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            // ── Body ─────────────────────────────────────────────────────────
            var bodyScroll = new WpfScrollViewer
            {
                VerticalScrollBarVisibility = WpfScrollBarVis.Auto,
                HorizontalScrollBarVisibility = WpfScrollBarVis.Disabled,
                Padding = new WpfThickness(0)
            };
            var body = new WpfStackPanel { Margin = new WpfThickness(20) };
            bodyScroll.Content = body;
            WpfGrid.SetRow(bodyScroll, 1);
            root.Children.Add(bodyScroll);

            // ── A. Run summary: stat tile grid (left) + config panel (right) ──
            var summaryRow = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 22) };
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });

            // Left: section label + 2‑column stat tile grid (Processed, Skipped, Failed, Warnings)
            var statsCol = new WpfStackPanel();
            statsCol.Children.Add(SectionLabel("Run summary"));
            var statGrid = new WpfGrid { Margin = new WpfThickness(0, 8, 0, 0) };
            // 2 columns, 2 rows
            for (int c = 0; c < 2; c++)
                statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statGrid.RowDefinitions.Add(new WpfRowDef());
            statGrid.RowDefinitions.Add(new WpfRowDef());

            int skippedCount = skipped;
            int failedCount = failed;
            int warnings = _rows.Count(r => r.StatusKind == "warn");

            var stats = new (string v, string k, string cls)[]
            {
                (processed.ToString(),     "Processed",       "ok"),
                (skippedCount.ToString(),  "Skipped",         ""),
                (failedCount.ToString(),   "Failed",          "fail"),
                (warnings.ToString(),      "Warnings",        "warn"),
            };

            for (int i = 0; i < stats.Length; i++)
            {
                var tile = AvControls.StatTile(stats[i].v, stats[i].k, stats[i].cls);
                tile.Margin = new WpfThickness(
                    i % 2 == 0 ? 0 : 6, 0,
                    i % 2 == 1 ? 0 : 6, 8);
                WpfGrid.SetRow(tile, i / 2);
                WpfGrid.SetColumn(tile, i % 2);
                statGrid.Children.Add(tile);
            }
            statsCol.Children.Add(statGrid);
            WpfGrid.SetColumn(statsCol, 0);
            summaryRow.Children.Add(statsCol);

            // Right: configuration panel
            var cfgCol = new WpfStackPanel();
            cfgCol.Children.Add(SectionLabel("Configuration"));
            var cfgPanel = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new WpfThickness(14, 4, 14, 4),
                Margin = new WpfThickness(0, 8, 0, 0)
            };
            var cfgStack = new WpfStackPanel();
            var cfgRows = new (string k, string v)[]
            {
                ("Mode",             "Selected assemblies"),
                ("Scope",            "Instance"),
                ("Assemblies found", assembliesFound.ToString()),
                ("Document",         docTitle),
            };
            for (int i = 0; i < cfgRows.Length; i++)
                cfgStack.Children.Add(KvRow(cfgRows[i].k, cfgRows[i].v, isLast: i == cfgRows.Length - 1));
            cfgPanel.Child = cfgStack;
            cfgCol.Children.Add(cfgPanel);
            WpfGrid.SetColumn(cfgCol, 2);
            summaryRow.Children.Add(cfgCol);

            body.Children.Add(summaryRow);

            // ── B. Results per assembly ────────────────────────────────────────
            var resultsHeaderRow = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 8) };
            resultsHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            resultsHeaderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lbl = SectionLabel("Results per assembly");
            WpfGrid.SetColumn(lbl, 0);
            resultsHeaderRow.Children.Add(lbl);
            var showingLbl = new WpfTextBlock
            {
                Text = $"showing {_rows.Count} of {assembliesFound}",
                FontFamily = AvTokens.Ui,
                FontSize = 11,
                FontWeight = WpfFontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            WpfGrid.SetColumn(showingLbl, 1);
            resultsHeaderRow.Children.Add(showingLbl);
            body.Children.Add(resultsHeaderRow);

            var tableWrap = new WpfBorder
            {
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new CornerRadius(5),
                ClipToBounds = true,
                Background = AvTokens.B(AvTokens.Surface)
            };
            tableWrap.Child = BuildResultsGrid();
            body.Children.Add(tableWrap);

            // ── C. Footer ────────────────────────────────────────────────────
            var footer = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(0, 1, 0, 0),
                Padding = new WpfThickness(20, 14, 20, 14)
            };
            var footerRow = new WpfGrid();
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sourceLbl = new WpfTextBlock
            {
                Text = _sourceCaption,
                FontFamily = AvTokens.Ui,
                FontSize = 11.5,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Center
            };
            WpfGrid.SetColumn(sourceLbl, 0);
            footerRow.Children.Add(sourceLbl);

            var copyBtn = AvControls.SecondaryBtn("⧉  Copy to clipboard", () => { });
            copyBtn.Margin = new WpfThickness(0, 0, 10, 0);
            copyBtn.Click += (s, e) => OnCopyToClipboard(copyBtn);
            WpfGrid.SetColumn(copyBtn, 2);
            footerRow.Children.Add(copyBtn);

            var closeBtn = AvControls.PrimaryBtn("Close", () => Close());
            WpfGrid.SetColumn(closeBtn, 3);
            footerRow.Children.Add(closeBtn);

            footer.Child = footerRow;
            WpfGrid.SetRow(footer, 2);
            root.Children.Add(footer);
        }

        // ── Section label: 10.5 bold faint, uppercase tracking ────────────────
        private static WpfTextBlock SectionLabel(string text) => new WpfTextBlock
        {
            Text = text.ToUpperInvariant(),
            FontFamily = AvTokens.Ui,
            FontSize = 10.5,
            FontWeight = WpfFontWeights.Bold,
            Foreground = AvTokens.B(AvTokens.TextFaint)
        };

        // ── Config panel key/value row with dashed bottom hairline ───────────
        private static System.Windows.UIElement KvRow(string k, string v, bool isLast)
        {
            var grid = new WpfGrid
            {
                Margin = new WpfThickness(0, 7, 0, 7)
            };
            if (!isLast)
            {
                grid.Margin = new WpfThickness(0, 7, 0, 0);
            }
            var row = new WpfStackPanel();
            var kv = new WpfGrid();
            kv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            kv.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var kTb = new WpfTextBlock
            {
                Text = k,
                FontFamily = AvTokens.Ui,
                FontSize = 11.5,
                Foreground = AvTokens.B(AvTokens.TextDim)
            };
            var vTb = new WpfTextBlock
            {
                Text = v,
                FontFamily = AvTokens.Mono,
                FontSize = 11.5,
                Foreground = AvTokens.B(AvTokens.Text),
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
                MaxWidth = 150
            };
            WpfGrid.SetColumn(kTb, 0);
            WpfGrid.SetColumn(vTb, 1);
            kv.Children.Add(kTb);
            kv.Children.Add(vTb);
            row.Children.Add(kv);
            if (!isLast)
            {
                row.Children.Add(new WpfBorder
                {
                    BorderBrush = AvTokens.B(AvTokens.Border),
                    BorderThickness = new WpfThickness(0, 0, 0, 1),
                    Margin = new WpfThickness(0, 7, 0, 0)
                });
            }
            return row;
        }

        // ── Builds the styled results DataGrid (status pill + notes only) ──
        private WpfDataGrid BuildResultsGrid()
        {
            var grid = new WpfDataGrid
            {
                ItemsSource = _rows,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserResizeRows = false,
                IsReadOnly = true,
                HeadersVisibility = System.Windows.Controls.DataGridHeadersVisibility.Column,
                GridLinesVisibility = System.Windows.Controls.DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = AvTokens.B(AvTokens.Border),
                RowHeight = 42,
                FontFamily = AvTokens.Ui,
                FontSize = 12.5,
                Background = AvTokens.B(AvTokens.Surface),
                BorderThickness = new WpfThickness(0),
                RowBackground = AvTokens.B(AvTokens.Surface),
                AlternatingRowBackground = AvTokens.B(AvTokens.Zebra),
                AlternationCount = 2,
                ColumnHeaderHeight = 34,
                MaxHeight = 360
            };

            // Header style: 10.5 bold faint, Surface-2 bg
            var headerStyle = new System.Windows.Style(typeof(WpfDataGridColumnHeader));
            headerStyle.Setters.Add(new System.Windows.Setter(
                WpfDataGridColumnHeader.BackgroundProperty, AvTokens.B(AvTokens.Surface2)));
            headerStyle.Setters.Add(new System.Windows.Setter(
                WpfDataGridColumnHeader.ForegroundProperty, AvTokens.B(AvTokens.TextFaint)));
            headerStyle.Setters.Add(new System.Windows.Setter(
                WpfDataGridColumnHeader.FontFamilyProperty, AvTokens.Ui));
            headerStyle.Setters.Add(new System.Windows.Setter(
                WpfDataGridColumnHeader.FontSizeProperty, 10.5));
            headerStyle.Setters.Add(new System.Windows.Setter(
                WpfDataGridColumnHeader.FontWeightProperty, WpfFontWeights.Bold));
            headerStyle.Setters.Add(new System.Windows.Setter(
                WpfDataGridColumnHeader.PaddingProperty, new WpfThickness(12, 0, 12, 0)));
            headerStyle.Setters.Add(new System.Windows.Setter(
                WpfDataGridColumnHeader.BorderBrushProperty, AvTokens.B(AvTokens.Border)));
            headerStyle.Setters.Add(new System.Windows.Setter(
                WpfDataGridColumnHeader.BorderThicknessProperty, new WpfThickness(0, 0, 0, 1)));
            grid.ColumnHeaderStyle = headerStyle;

            // Cell style: no selection border/highlight (read-only report)
            var cellStyle = new System.Windows.Style(typeof(WpfDataGridCell));
            cellStyle.Setters.Add(new System.Windows.Setter(
                WpfDataGridCell.BorderThicknessProperty, new WpfThickness(0)));
            cellStyle.Setters.Add(new System.Windows.Setter(
                WpfDataGridCell.PaddingProperty, new WpfThickness(12, 0, 12, 0)));
            cellStyle.Setters.Add(new System.Windows.Setter(
                WpfDataGridCell.BackgroundProperty, WpfBrushes.Transparent));
            cellStyle.Setters.Add(new System.Windows.Setter(
                WpfDataGridCell.ForegroundProperty, AvTokens.B(AvTokens.Text)));
            cellStyle.Setters.Add(new System.Windows.Setter(
                WpfDataGridCell.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            grid.CellStyle = cellStyle;

            // Column: Assembly name — SemiBold
            var nameCol = new WpfDataGridTextColumn
            {
                Header = "Assembly name",
                Binding = new System.Windows.Data.Binding("AssemblyName"),
                Width = new System.Windows.Controls.DataGridLength(2, System.Windows.Controls.DataGridLengthUnitType.Star)
            };
            var nameStyle = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
            nameStyle.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.TextBlock.FontWeightProperty, WpfFontWeights.SemiBold));
            nameCol.ElementStyle = nameStyle;
            grid.Columns.Add(nameCol);

            // Column: Status — pill template
            var statusCol = new WpfDataGridTemplateColumn
            {
                Header = "Status",
                Width = new System.Windows.Controls.DataGridLength(110)
            };
            statusCol.CellTemplateSelector = null;
            statusCol.CellTemplate = BuildStatusPillTemplate();
            grid.Columns.Add(statusCol);

            // Column: Notes — 11.5 faint, amber when warning
            var notesCol = new WpfDataGridTextColumn
            {
                Header = "Notes",
                Binding = new System.Windows.Data.Binding("Note"),
                Width = new System.Windows.Controls.DataGridLength(1.4, System.Windows.Controls.DataGridLengthUnitType.Star)
            };
            var notesStyle = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
            notesStyle.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.TextBlock.FontSizeProperty, 11.5));
            notesStyle.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.TextBlock.ForegroundProperty, AvTokens.B(AvTokens.TextFaint)));
            notesStyle.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.TextBlock.TextTrimmingProperty,
                System.Windows.TextTrimming.CharacterEllipsis));
            var warnTrigger = new System.Windows.DataTrigger
            {
                Binding = new System.Windows.Data.Binding("StatusKind"),
                Value = "warn"
            };
            warnTrigger.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.TextBlock.ForegroundProperty, AvTokens.B("#9A6B12")));
            notesStyle.Triggers.Add(warnTrigger);
            notesCol.ElementStyle = notesStyle;
            grid.Columns.Add(notesCol);

            return grid;
        }

        // ── Status pill cell template via XamlReader.Parse ──────────────────
        private static WpfDataTemplate BuildStatusPillTemplate()
        {
            string xaml = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
              xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Border x:Name='pill' CornerRadius='6' Padding='8,5,10,5' HorizontalAlignment='Left' VerticalAlignment='Center'>
    <StackPanel Orientation='Horizontal'>
      <Ellipse x:Name='dot' Width='7' Height='7' VerticalAlignment='Center' Margin='0,0,6,0'/>
      <TextBlock x:Name='lbl' FontSize='11.5' FontWeight='SemiBold' VerticalAlignment='Center'/>
    </StackPanel>
  </Border>
  <DataTemplate.Triggers>
    <DataTrigger Binding='{Binding StatusKind}' Value='ok'>
      <Setter TargetName='pill' Property='Background' Value='#E4F3EA'/>
      <Setter TargetName='dot'  Property='Fill' Value='#2C7A52'/>
      <Setter TargetName='lbl'  Property='Foreground' Value='#2C7A52'/>
      <Setter TargetName='lbl'  Property='Text' Value='Done'/>
    </DataTrigger>
    <DataTrigger Binding='{Binding StatusKind}' Value='skip'>
      <Setter TargetName='pill' Property='Background' Value='#EDEFF2'/>
      <Setter TargetName='dot'  Property='Fill' Value='#5B6573'/>
      <Setter TargetName='lbl'  Property='Foreground' Value='#5B6573'/>
      <Setter TargetName='lbl'  Property='Text' Value='Skipped'/>
    </DataTrigger>
    <DataTrigger Binding='{Binding StatusKind}' Value='fail'>
      <Setter TargetName='pill' Property='Background' Value='#FBE3DE'/>
      <Setter TargetName='dot'  Property='Fill' Value='#C0381F'/>
      <Setter TargetName='lbl'  Property='Foreground' Value='#C0381F'/>
      <Setter TargetName='lbl'  Property='Text' Value='Failed'/>
    </DataTrigger>
    <DataTrigger Binding='{Binding StatusKind}' Value='warn'>
      <Setter TargetName='pill' Property='Background' Value='#FAF0DA'/>
      <Setter TargetName='dot'  Property='Fill' Value='#9A6B12'/>
      <Setter TargetName='lbl'  Property='Foreground' Value='#9A6B12'/>
      <Setter TargetName='lbl'  Property='Text' Value='Warning'/>
    </DataTrigger>
  </DataTemplate.Triggers>
</DataTemplate>";
            return (WpfDataTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        // ── Copy results as TSV to clipboard, with brief confirmation ─────────
        private void OnCopyToClipboard(WpfButton sourceBtn)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Assembly name\tStatus\tNotes");
            foreach (var r in _rows)
            {
                sb.AppendLine(string.Join("\t",
                    r.AssemblyName, r.StatusKind, r.Note));
            }
            try
            {
                WpfClipboard.SetText(sb.ToString());
                if (sourceBtn != null)
                {
                    var original = sourceBtn.Content;
                    sourceBtn.Content = "✓  Copied!";
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1.4)
                    };
                    timer.Tick += (s, e) =>
                    {
                        sourceBtn.Content = original;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
            catch
            {
                // Clipboard can throw if locked by another process — fail silently,
                // this is a non-critical convenience action.
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MAIN COMMAND
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    public class Cmd_Button : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            var doc = uiDoc.Document;

            // ===== START LOGIC =====

            try
            {
                // ── 0. Collect all assemblies ─────────────────────────────────
                var allAssemblies = new FilteredElementCollector(doc)
                    .OfClass(typeof(AssemblyInstance))
                    .Cast<AssemblyInstance>()
                    .ToList();

                if (allAssemblies.Count == 0)
                {
                    AvControls.ShowAlert("Part List Format",
                        "No AssemblyInstance elements found in this document.", AlertSeverity.Warning);
                    return Result.Cancelled;
                }

                // ── 1. Show WPF picker ────────────────────────────────────────
                var picker = new AssemblyPickerWindow(allAssemblies);
                picker.ShowDialog();

                if (picker.SelectedAssemblies == null || picker.SelectedAssemblies.Count == 0)
                    return Result.Cancelled;

                // ── 2. Process each selected assembly ─────────────────────────
                int totalAssembliesProcessed = 0;
                var reportRows = new List<ReportRow>();

                foreach (var assembly in picker.SelectedAssemblies)
                {
                    // ── A. Collect OST_GenericModel members ───────────────────
                    var gmInstances = assembly.GetMemberIds()
                        .Select(id => doc.GetElement(id))
                        .Where(el => el?.Category != null
                            && el.Category.Id.Value
                               == (int)BuiltInCategory.OST_GenericModel)
                        .ToList();

                    if (gmInstances.Count == 0)
                    {
                        reportRows.Add(new ReportRow
                        {
                            AssemblyName = assembly.Name,
                            StatusKind = "skip",
                            Note = "No Generic Model members"
                        });
                        continue;
                    }

                    // ── B. Compute merge strings in memory (Step A of algorithm)
                    var instanceDataList = new List<InstanceData>();

                    foreach (var el in gmInstances)
                    {
                        // Length is an instance parameter stored in Revit internal units (feet).
                        // Convert to millimetres and round to nearest integer.
                        var lenParam = el.LookupParameter("Length");
                        bool hasLength = lenParam != null
                            && lenParam.StorageType == StorageType.Double
                            && lenParam.AsDouble() > 0;
                        int lengthMm = hasLength
                            ? (int)Math.Round(
                                UnitUtils.ConvertFromInternalUnits(
                                    lenParam.AsDouble(), UnitTypeId.Millimeters))
                            : 0;

                        // Part Number, Description and Title are TYPE parameters.
                        // LookupParameter on the instance returns null for type params,
                        // so we read them from the family symbol (element type).
                        var symbol = el is FamilyInstance fi ? fi.Symbol as Element : null;
                        var typeEl = symbol ?? doc.GetElement(el.GetTypeId());

                        string partNumber = GetParamStringFromTypeOrInstance(typeEl, el, "Part Number");
                        string description = GetParamStringFromTypeOrInstance(typeEl, el, "Description");
                        string title = GetParamStringFromTypeOrInstance(typeEl, el, "Title");

                        instanceDataList.Add(new InstanceData
                        {
                            Id = el.Id,
                            Instance = el,
                            HasLength = hasLength,
                            LengthMm = lengthMm,
                            Title = title,
                            PartNumber = partNumber,
                            Description = description,
                            PartNumberMerge = hasLength ? $"{partNumber}-{lengthMm}" : partNumber,
                            DescriptionMerge = hasLength ? $"{description}-{lengthMm}" : description
                        });
                    }

                    // ── C. Build rows and assign sequence numbers ─────────────

                    // Group 1: Length families — keyed (Title + PartNumber + LengthMm).
                    // PartNumber is required here too: two different families (e.g.
                    // "SPB-RF50 CT-150" PN 88408 and "SPB-RF50 CT-200" PN 88409) can
                    // share the same Title ("Cable tray") and the same Length value,
                    // and would otherwise collapse into a single incorrect row.
                    var lengthRows = instanceDataList
                        .Where(d => d.HasLength)
                        .GroupBy(d => $"{d.Title}|{d.PartNumber}|{d.LengthMm}",
                                 StringComparer.OrdinalIgnoreCase)
                        .Select(g => new PartListRow
                        {
                            HasLength = true,
                            Title = g.First().Title,
                            LengthMm = g.First().LengthMm,
                            PartNumber = g.First().PartNumber,
                            PartNumberMerge = g.First().PartNumberMerge
                        })
                        .OrderBy(r => r.PartNumberMerge, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    // Group 2: Non-Length families — keyed (Title + PartNumber + Description).
                    // Description is the third discriminator: two families can share Title and
                    // PartNumber (e.g. two bolt types both named "78775 / Locking bolt") but
                    // always differ in Description (e.g. "LB-M6x20" vs "LB-M6x25").
                    var nonLengthRows = instanceDataList
                        .Where(d => !d.HasLength)
                        .GroupBy(d => $"{d.Title}|{d.PartNumber}|{d.Description}",
                                 StringComparer.OrdinalIgnoreCase)
                        .Select(g => new PartListRow
                        {
                            HasLength = false,
                            Title = g.First().Title,
                            PartNumber = g.First().PartNumber,
                            Description = g.First().Description,
                            LengthMm = 0,
                            PartNumberMerge = g.First().PartNumberMerge
                        })
                        .OrderBy(r => r.PartNumberMerge, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    // Flat list: Length rows first, then Non-Length rows
                    var allRows = lengthRows.Concat(nonLengthRows).ToList();
                    for (int i = 0; i < allRows.Count; i++)
                        allRows[i].RowNumber = (i + 1).ToString();

                    // Lookup: (Title|PartNumber|LengthMm) → RowNumber  for Length families
                    var lengthLookup = allRows
                        .Where(r => r.HasLength)
                        .ToDictionary(
                            r => $"{r.Title}|{r.PartNumber}|{r.LengthMm}",
                            r => r.RowNumber,
                            StringComparer.OrdinalIgnoreCase);

                    // Lookup: (Title|PartNumber|Description) → RowNumber  for Non-Length families
                    var nonLengthLookup = allRows
                        .Where(r => !r.HasLength)
                        .ToDictionary(
                            r => $"{r.Title}|{r.PartNumber}|{r.Description}",
                            r => r.RowNumber,
                            StringComparer.OrdinalIgnoreCase);

                    // ── D. Write to Revit ─────────────────────────────────────
                    using var tx = new Transaction(doc, $"Part List Format — {assembly.Name}");
                    tx.Start();
                    try
                    {
                        foreach (var data in instanceDataList)
                        {
                            var el = data.Instance;

                            SetParamString(el, "PART NUMBER MERGE", data.PartNumberMerge);
                            SetParamString(el, "DESCRIPTION MERGE", data.DescriptionMerge);

                            string rowNum = null;
                            if (data.HasLength)
                                lengthLookup.TryGetValue(
                                    $"{data.Title}|{data.PartNumber}|{data.LengthMm}", out rowNum);
                            else
                                nonLengthLookup.TryGetValue(
                                    $"{data.Title}|{data.PartNumber}|{data.Description}", out rowNum);

                            if (!string.IsNullOrEmpty(rowNum))
                                SetParamString(el, "Comments", rowNum);
                        }

                        tx.Commit();
                        totalAssembliesProcessed++;

                        reportRows.Add(new ReportRow
                        {
                            AssemblyName = assembly.Name,
                            StatusKind = "ok",
                            Note = ""
                        });
                    }
                    catch (Exception txEx)
                    {
                        tx.RollBack();
                        reportRows.Add(new ReportRow
                        {
                            AssemblyName = assembly.Name,
                            StatusKind = "fail",
                            Note = txEx.Message
                        });
                        AvControls.ShowAlert("Part List Format — Transaction Error",
                            $"Assembly: {assembly.Name}\n\n{txEx.Message}", AlertSeverity.Error);
                    }
                }

                // ── 3. Show AVES Report window ─────────────────────────────────
                int skippedTotal = reportRows.Count(r => r.StatusKind == "skip");
                int failedTotal = reportRows.Count(r => r.StatusKind == "fail");

                var report = new ReportWindow(
                    rows: reportRows,
                    processed: totalAssembliesProcessed,
                    skipped: skippedTotal,
                    failed: failedTotal,
                    assembliesFound: picker.SelectedAssemblies.Count,
                    docTitle: doc.Title);
                report.ShowDialog();
            }
            catch (Exception ex)
            {
                AvControls.ShowAlert("Part List Format — Error", ex.Message, AlertSeverity.Error);
                return Result.Failed;
            }

            // ===== END LOGIC =====

            return Result.Succeeded;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PARAMETER HELPERS
        // ─────────────────────────────────────────────────────────────────────

        private static string GetParamString(Element el, string paramName)
        {
            if (el == null) return string.Empty;
            var p = el.LookupParameter(paramName);
            if (p == null) return string.Empty;
            var raw = p.StorageType == StorageType.String
                ? (p.AsString() ?? string.Empty)
                : (p.AsValueString() ?? string.Empty);
            // Trim to guard against trailing/leading whitespace in family parameters
            // which would cause dictionary key mismatches and missing row numbers.
            return raw.Trim();
        }

        /// <summary>
        /// Reads a parameter that may live on the type or the instance.
        /// Tries the type element first (covers Part Number, Description, Title
        /// which are Type parameters in these families), then falls back to the
        /// instance so the helper works universally.
        /// </summary>
        private static string GetParamStringFromTypeOrInstance(
            Element typeEl, Element instanceEl, string paramName)
        {
            var val = GetParamString(typeEl, paramName);
            if (!string.IsNullOrEmpty(val)) return val;
            return GetParamString(instanceEl, paramName);
        }

        private static void SetParamString(Element el, string paramName, string value)
        {
            var p = el.LookupParameter(paramName);
            if (p == null || p.IsReadOnly) return;
            if (p.StorageType == StorageType.String)
                p.Set(value);
        }
    }
}