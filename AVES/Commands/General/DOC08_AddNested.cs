// -----------------------------------------------------------------------
// Title: DOC08_AddNested
// Summary: Lets the user pick one or more assemblies, then adds the shared
//          nested families of their members (all levels, intermediate combos
//          included) to the same assembly, and shows a report of the outcome.
// Key logic: Nested ids come from FamilyInstance.GetSubComponentIds()
//            collected recursively. Each id is classified by its
//            AssemblyInstanceId: already in this assembly, owned by another
//            assembly (conflict, never moved), or free (added). One batched
//            AddMemberIds per assembly inside its own transaction, all inside
//            a TransactionGroup; a failed batch is retried element by element
//            in sub-transactions to isolate the bad ones.
// -----------------------------------------------------------------------
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
using WpfControl = System.Windows.Controls.Control;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfDataGridCell = System.Windows.Controls.DataGridCell;
using WpfDataGridColumnHeader = System.Windows.Controls.Primitives.DataGridColumnHeader;
using WpfDataGridTemplateColumn = System.Windows.Controls.DataGridTemplateColumn;
using WpfDataGridTextColumn = System.Windows.Controls.DataGridTextColumn;
using WpfFontWeights = System.Windows.FontWeights;
using WpfGrid = System.Windows.Controls.Grid;
using WpfHAlign = System.Windows.HorizontalAlignment;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfListBoxItem = System.Windows.Controls.ListBoxItem;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfRowDef = System.Windows.Controls.RowDefinition;
using WpfScrollBarVis = System.Windows.Controls.ScrollBarVisibility;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;
using WpfSelMode = System.Windows.Controls.SelectionMode;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfThickness = System.Windows.Thickness;

namespace AVES.Commands
{
    // ─────────────────────────────────────────────────────────────────────────
    //  DATA HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>One row in the report — one per processed assembly.</summary>
    internal class NestedReportRow
    {
        public string AssemblyName { get; set; } = "";
        public string StatusKind { get; set; } = "ok";   // "ok" | "skip" | "fail" | "warn"
        /// <summary>Pieces added (Combo Item != 1). Shown in the tile, grid and clipboard.</summary>
        public int Added { get; set; }

        /// <summary>Combo/host families added. Reported in Notes only.</summary>
        public int ComboAdded { get; set; }

        /// <summary>Pieces already in the assembly (combos are not counted).</summary>
        public int Present { get; set; }
        public int Conflicts { get; set; }
        public int Failed { get; set; }
        public string Note { get; set; } = "";

        /// <summary>Per-element lines (conflicts, failures) for the clipboard copy.</summary>
        public List<string> Details { get; } = new List<string>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  WPF ASSEMBLY PICKER WINDOW  — AVES Refined Native / AVES Red
    //  Same look as the Part List Format picker, but with a preselection and a
    //  checked set that survives the search filter (the list is rebuilt on every
    //  keystroke, so state cannot live on the list items).
    // ─────────────────────────────────────────────────────────────────────────

    internal class NestedAssemblyPickerWindow : Window
    {
        public List<AssemblyInstance> SelectedAssemblies { get; private set; }
            = new List<AssemblyInstance>();

        private readonly List<AssemblyInstance> _allAssemblies;
        private readonly HashSet<long> _checked;
        private readonly WpfListBox _listBox;
        private readonly WpfTextBox _searchBox;
        private readonly WpfTextBlock _countLabel;
        private bool _rebuilding;
        private bool _confirmed;

        public NestedAssemblyPickerWindow(
            List<AssemblyInstance> assemblies, IEnumerable<long> preselected)
        {
            _allAssemblies = assemblies
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _checked = new HashSet<long>(preselected);

            // ── Window chrome ─────────────────────────────────────────────────
            Title = "Add Nested to Assemblies";
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

            var shell = new WpfGrid();
            shell.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(44) });
            shell.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(1, GridUnitType.Star) });
            shell.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });
            Content = shell;

            // ── Title bar ─────────────────────────────────────────────────────
            var titleBar = new WpfBorder
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
                Text = "Add Nested to Assemblies",
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                FontWeight = WpfFontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.Text),
                Margin = new WpfThickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            titleBar.Child = titleRow;
            WpfGrid.SetRow(titleBar, 0);
            shell.Children.Add(titleBar);

            // ── Body ──────────────────────────────────────────────────────────
            var body = new WpfGrid { Margin = new WpfThickness(20, 16, 20, 16) };
            body.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto }); // section label
            body.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto }); // search
            body.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto }); // sel-all row
            body.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(1, GridUnitType.Star) }); // list
            WpfGrid.SetRow(body, 1);
            shell.Children.Add(body);

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

            // ── Search box ────────────────────────────────────────────────────
            var searchWrap = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new WpfThickness(0, 0, 0, 10),
                Padding = new WpfThickness(10, 0, 10, 0),
                Height = 34
            };
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
                Background = WpfBrushes.Transparent,
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
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var btnSelAll = AvControls.SecondaryBtn("Select All", () => SetAllSelection(true));
            btnSelAll.Foreground = AvTokens.B("#E64A2E");
            var btnSelNone = AvControls.SecondaryBtn("Deselect All", () => SetAllSelection(false));
            btnSelAll.Margin = new WpfThickness(0, 0, 6, 0);

            _countLabel = new WpfTextBlock
            {
                FontFamily = AvTokens.Ui,
                FontSize = 11,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = WpfHAlign.Right
            };

            WpfGrid.SetColumn(btnSelAll, 0);
            WpfGrid.SetColumn(btnSelNone, 1);
            WpfGrid.SetColumn(_countLabel, 3);
            selRow.Children.Add(btnSelAll);
            selRow.Children.Add(btnSelNone);
            selRow.Children.Add(_countLabel);
            WpfGrid.SetRow(selRow, 2);
            body.Children.Add(selRow);

            // ── ListBox (checkbox rows) ───────────────────────────────────────
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
                WpfListBoxItem.BackgroundProperty, WpfBrushes.Transparent));

            // The default ListBoxItem template paints its own selection highlight
            // over any plain Background setter/trigger, so a custom template is
            // required for the accent-tint background to actually render.
            var itemTemplate = new ControlTemplate(typeof(WpfListBoxItem));
            var itemBorder = new FrameworkElementFactory(typeof(WpfBorder));
            itemBorder.SetBinding(WpfBorder.BackgroundProperty,
                new System.Windows.Data.Binding("Background")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            itemBorder.SetBinding(WpfBorder.PaddingProperty,
                new System.Windows.Data.Binding("Padding")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var itemContent = new FrameworkElementFactory(typeof(ContentPresenter));
            itemContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
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

            _listBox.SelectionChanged += (s, e) =>
            {
                // RefreshList clears and repopulates the list; those selection
                // events are bookkeeping, not user intent, and must not touch
                // the persisted checked set.
                if (_rebuilding) return;

                foreach (var removed in e.RemovedItems.OfType<WpfListBoxItem>())
                {
                    if (removed.Tag is AssemblyInstance a) _checked.Remove(a.Id.Value);
                    SyncRowVisual(removed);
                }
                foreach (var added in e.AddedItems.OfType<WpfListBoxItem>())
                {
                    if (added.Tag is AssemblyInstance a) _checked.Add(a.Id.Value);
                    SyncRowVisual(added);
                }
                UpdateCountLabel();
            };

            // SelectionMode.Multiple normally requires Ctrl/Shift to add to an
            // existing selection. Since our rows are checkboxes, a plain click
            // should always TOGGLE that row without touching the others.
            _listBox.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject d)
                {
                    var item = ItemsControl.ContainerFromElement(_listBox, d) as WpfListBoxItem;
                    if (item != null)
                    {
                        item.IsSelected = !item.IsSelected;
                        e.Handled = true;
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

            // ── Footer ────────────────────────────────────────────────────────
            var footer = new WpfBorder
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

        // ── List refresh: build AVES checkbox rows from the persisted set ────
        private void RefreshList()
        {
            var filter = _searchBox?.Text?.Trim() ?? string.Empty;

            _rebuilding = true;
            try
            {
                _listBox.Items.Clear();
                foreach (var asm in _allAssemblies)
                {
                    if (filter.Length != 0 &&
                        asm.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    bool isChecked = _checked.Contains(asm.Id.Value);
                    var item = new WpfListBoxItem
                    {
                        Tag = asm,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Content = BuildCheckboxRow(asm.Name, isChecked)
                    };
                    _listBox.Items.Add(item);
                    item.IsSelected = isChecked;
                }
            }
            finally
            {
                _rebuilding = false;
            }
            UpdateCountLabel();
        }

        private void UpdateCountLabel()
        {
            int sel = _checked.Count;
            _countLabel.Text = sel > 0
                ? $"{sel} of {_allAssemblies.Count} selected"
                : $"{_allAssemblies.Count} assemblies";
        }

        // ── Builds a single AVES checkbox row: 17px rounded-5 box + label ────
        private static UIElement BuildCheckboxRow(string label, bool isChecked)
        {
            var box = new WpfBorder
            {
                Width = 17,
                Height = 17,
                CornerRadius = new CornerRadius(5),
                Background = isChecked ? AvTokens.B(AvTokens.Accent) : WpfBrushes.Transparent,
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
                    Foreground = WpfBrushes.White,
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

        private void OnOk()
        {
            SelectedAssemblies = _allAssemblies
                .Where(a => _checked.Contains(a.Id.Value))
                .ToList();
            _confirmed = true;
            Close();
        }

        private void OnCancel() => Close();

        protected override void OnClosed(EventArgs e)
        {
            if (!_confirmed) SelectedAssemblies = new List<AssemblyInstance>();
            base.OnClosed(e);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  REPORT WINDOW  (AVES Refined Native)
    //  Body: A. Totals (stat tiles) + configuration panel
    //        B. Results per assembly (status pill + counts + notes)
    //        C. Footer (source caption · copy to clipboard · close)
    // ─────────────────────────────────────────────────────────────────────────

    internal class NestedReportWindow : Window
    {
        private readonly List<NestedReportRow> _rows;
        private readonly string _sourceCaption;

        public NestedReportWindow(
            List<NestedReportRow> rows,
            int assembliesSelected,
            string docTitle)
        {
            _rows = rows;
            _sourceCaption = $"Source · {docTitle}";

            Title = "Add Nested to Assemblies";
            Width = 880;
            Height = 620;
            MinWidth = 700;
            MinHeight = 420;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = AvTokens.B(AvTokens.Surface);
            FontFamily = AvTokens.Ui;

            var root = new WpfGrid();
            root.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });
            root.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });
            Content = root;

            var titleBar = AvControls.ReportHeaderBar("Add Nested to Assemblies");
            WpfGrid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

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

            // ── A. Totals: four stat tiles in one row ─────────────────────────
            int totalAdded = _rows.Sum(r => r.Added);
            int totalPresent = _rows.Sum(r => r.Present);
            int totalConflicts = _rows.Sum(r => r.Conflicts);
            int totalFailed = _rows.Sum(r => r.Failed);

            body.Children.Add(SectionLabel("Totals"));
            var statGrid = new WpfGrid { Margin = new WpfThickness(0, 8, 0, 16) };
            var stats = new (string v, string k, string cls)[]
            {
                (totalAdded.ToString(),     "Nested added",     "ok"),
                (totalPresent.ToString(),   "Already present",  ""),
                (totalConflicts.ToString(), "Conflicts",        "warn"),
                (totalFailed.ToString(),    "Failed",           "fail"),
            };
            for (int i = 0; i < stats.Length; i++)
            {
                statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var tile = AvControls.StatTile(stats[i].v, stats[i].k, stats[i].cls);
                tile.Margin = new WpfThickness(i == 0 ? 0 : 6, 0, i == stats.Length - 1 ? 0 : 6, 0);
                WpfGrid.SetColumn(tile, i);
                statGrid.Children.Add(tile);
            }
            body.Children.Add(statGrid);

            // ── Configuration panel ───────────────────────────────────────────
            body.Children.Add(SectionLabel("Configuration"));
            var cfgPanel = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new WpfThickness(14, 4, 14, 4),
                Margin = new WpfThickness(0, 8, 0, 22)
            };
            var cfgStack = new WpfStackPanel();
            var cfgRows = new (string k, string v)[]
            {
                ("Mode",                "Selected assemblies"),
                ("Assemblies selected", assembliesSelected.ToString()),
                ("Document",            docTitle),
            };
            for (int i = 0; i < cfgRows.Length; i++)
                cfgStack.Children.Add(KvRow(cfgRows[i].k, cfgRows[i].v, isLast: i == cfgRows.Length - 1));
            cfgPanel.Child = cfgStack;
            body.Children.Add(cfgPanel);

            // ── B. Results per assembly ───────────────────────────────────────
            var resultsHeader = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 8) };
            resultsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            resultsHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var lbl = SectionLabel("Results per assembly");
            WpfGrid.SetColumn(lbl, 0);
            resultsHeader.Children.Add(lbl);
            var showingLbl = new WpfTextBlock
            {
                Text = $"showing {_rows.Count} of {assembliesSelected}",
                FontFamily = AvTokens.Ui,
                FontSize = 11,
                FontWeight = WpfFontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            WpfGrid.SetColumn(showingLbl, 1);
            resultsHeader.Children.Add(showingLbl);
            body.Children.Add(resultsHeader);

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

            // ── C. Footer ─────────────────────────────────────────────────────
            var footerRow = new WpfGrid();
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sourceLbl = AvControls.FooterCaption(_sourceCaption);
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

            var footer = AvControls.FooterStrip(footerRow);
            WpfGrid.SetRow(footer, 2);
            root.Children.Add(footer);
        }

        private static WpfTextBlock SectionLabel(string text) => new WpfTextBlock
        {
            Text = text.ToUpperInvariant(),
            FontFamily = AvTokens.Ui,
            FontSize = 10.5,
            FontWeight = WpfFontWeights.Bold,
            Foreground = AvTokens.B(AvTokens.TextFaint)
        };

        // ── Config panel key/value row with bottom hairline ──────────────────
        private static UIElement KvRow(string k, string v, bool isLast)
        {
            var row = new WpfStackPanel
            {
                Margin = isLast ? new WpfThickness(0, 7, 0, 7) : new WpfThickness(0, 7, 0, 0)
            };
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
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 420
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

        // ── Builds the styled results DataGrid ────────────────────────────────
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
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
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
                WpfDataGridCell.TemplateProperty, AvControls.DataGridCellTemplate()));
            grid.CellStyle = cellStyle;

            // Assembly name — SemiBold
            var nameCol = new WpfDataGridTextColumn
            {
                Header = "Assembly name",
                Binding = new System.Windows.Data.Binding("AssemblyName"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            };
            var nameStyle = new System.Windows.Style(typeof(WpfTextBlock));
            nameStyle.Setters.Add(new System.Windows.Setter(
                WpfTextBlock.FontWeightProperty, WpfFontWeights.SemiBold));
            nameCol.ElementStyle = nameStyle;
            grid.Columns.Add(nameCol);

            // Status — shared pill template
            grid.Columns.Add(new WpfDataGridTemplateColumn
            {
                Header = "Status",
                Width = new DataGridLength(110),
                CellTemplate = AvControls.StatusPillTemplate("StatusKind")
            });

            // Counts
            grid.Columns.Add(CountColumn("Added", "Added"));
            grid.Columns.Add(CountColumn("Present", "Present"));
            grid.Columns.Add(CountColumn("Conflicts", "Conflicts"));
            grid.Columns.Add(CountColumn("Failed", "Failed"));

            // Notes — faint, amber when warning
            var notesCol = new WpfDataGridTextColumn
            {
                Header = "Notes",
                Binding = new System.Windows.Data.Binding("Note"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            };
            var notesStyle = new System.Windows.Style(typeof(WpfTextBlock));
            notesStyle.Setters.Add(new System.Windows.Setter(
                WpfTextBlock.FontSizeProperty, 11.5));
            notesStyle.Setters.Add(new System.Windows.Setter(
                WpfTextBlock.ForegroundProperty, AvTokens.B(AvTokens.TextFaint)));
            notesStyle.Setters.Add(new System.Windows.Setter(
                WpfTextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            var warnTrigger = new System.Windows.DataTrigger
            {
                Binding = new System.Windows.Data.Binding("StatusKind"),
                Value = "warn"
            };
            warnTrigger.Setters.Add(new System.Windows.Setter(
                WpfTextBlock.ForegroundProperty, AvTokens.B("#9A6B12")));
            notesStyle.Triggers.Add(warnTrigger);
            notesCol.ElementStyle = notesStyle;
            grid.Columns.Add(notesCol);

            return grid;
        }

        private static WpfDataGridTextColumn CountColumn(string header, string path)
        {
            var col = new WpfDataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(path),
                Width = new DataGridLength(78)
            };
            var style = new System.Windows.Style(typeof(WpfTextBlock));
            style.Setters.Add(new System.Windows.Setter(
                WpfTextBlock.FontFamilyProperty, AvTokens.Mono));
            style.Setters.Add(new System.Windows.Setter(
                WpfTextBlock.FontSizeProperty, 12.0));
            col.ElementStyle = style;
            return col;
        }

        // ── Copy results as TSV (summary rows, then per-element details) ─────
        private void OnCopyToClipboard(WpfButton sourceBtn)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Assembly name\tStatus\tAdded\tPresent\tConflicts\tFailed\tNotes");
            foreach (var r in _rows)
            {
                sb.AppendLine(string.Join("\t",
                    r.AssemblyName, r.StatusKind, r.Added, r.Present,
                    r.Conflicts, r.Failed, r.Note));
            }

            if (_rows.Any(r => r.Details.Count > 0))
            {
                sb.AppendLine();
                sb.AppendLine("Assembly name\tDetail");
                foreach (var r in _rows)
                    foreach (var d in r.Details)
                        sb.AppendLine($"{r.AssemblyName}\t{d}");
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
                // Clipboard can throw if locked by another process — non-critical.
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MAIN COMMAND
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    public class Cmd_DOC08_AddNested : IExternalCommand
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
                    AvControls.ShowAlert("Add Nested to Assemblies",
                        "No AssemblyInstance elements found in this document.", AlertSeverity.Warning);
                    return Result.Cancelled;
                }

                // ── 1. Preselect from the current selection ───────────────────
                // A selected assembly is preselected directly; a selected member
                // preselects the assembly that owns it.
                var preselected = new HashSet<long>();
                foreach (var id in uiDoc.Selection.GetElementIds())
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    if (el is AssemblyInstance)
                        preselected.Add(el.Id.Value);
                    else if (el.AssemblyInstanceId != null
                             && el.AssemblyInstanceId != ElementId.InvalidElementId)
                        preselected.Add(el.AssemblyInstanceId.Value);
                }

                // ── 2. Show WPF picker ────────────────────────────────────────
                var picker = new NestedAssemblyPickerWindow(allAssemblies, preselected);
                picker.ShowDialog();

                if (picker.SelectedAssemblies == null || picker.SelectedAssemblies.Count == 0)
                    return Result.Cancelled;

                // ── 3. Process each selected assembly in one TransactionGroup ─
                var reportRows = new List<NestedReportRow>();

                using (var group = new TransactionGroup(doc, "Add Nested to Assemblies"))
                {
                    group.Start();

                    foreach (var assembly in picker.SelectedAssemblies)
                    {
                        NestedReportRow row;
                        try
                        {
                            row = ProcessAssembly(doc, assembly);
                        }
                        catch (Exception ex)
                        {
                            row = new NestedReportRow
                            {
                                AssemblyName = assembly.Name,
                                StatusKind = "fail",
                                Note = ex.Message
                            };
                        }
                        reportRows.Add(row);
                    }

                    // Nothing changed -> leave no empty undo entry behind.
                    // Combos count here too: a run that added only combos still
                    // changed the model.
                    if (reportRows.Sum(r => r.Added + r.ComboAdded) > 0)
                        group.Assimilate();
                    else
                        group.RollBack();
                }

                // ── 4. Show AVES Report window ─────────────────────────────────
                var report = new NestedReportWindow(
                    reportRows, picker.SelectedAssemblies.Count, doc.Title);
                report.ShowDialog();
            }
            catch (Exception ex)
            {
                AvControls.ShowAlert("Add Nested to Assemblies — Error", ex.Message, AlertSeverity.Error);
                return Result.Failed;
            }

            // ===== END LOGIC =====

            return Result.Succeeded;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  PER-ASSEMBLY WORK
        // ─────────────────────────────────────────────────────────────────────

        private static NestedReportRow ProcessAssembly(Document doc, AssemblyInstance assembly)
        {
            var row = new NestedReportRow { AssemblyName = assembly.Name };
            long assemblyId = assembly.Id.Value;
            var typeBeforeId = assembly.GetTypeId();

            // ── A. Collect every nested id, all levels ────────────────────────
            var nested = CollectNested(doc, assembly);
            if (nested.Count == 0)
            {
                row.StatusKind = "skip";
                row.Note = "No nested families found";
                return row;
            }

            // ── B. Classify by current owner ──────────────────────────────────
            var toAdd = new List<ElementId>();
            var comboToAdd = new HashSet<long>();
            var ownerNames = new Dictionary<long, string>();
            foreach (var id in nested)
            {
                var el = doc.GetElement(id);
                if (el == null) continue;

                var owner = el.AssemblyInstanceId;
                if (owner == null || owner == ElementId.InvalidElementId)
                {
                    toAdd.Add(id);
                    if (ComboItemHelper.IsComboItem(doc, el))
                        comboToAdd.Add(id.Value);
                }
                else if (owner.Value == assemblyId)
                {
                    // Present counts pieces only; combos are not reported.
                    if (!ComboItemHelper.IsComboItem(doc, el))
                        row.Present++;
                }
                else
                {
                    row.Conflicts++;
                    if (!ownerNames.TryGetValue(owner.Value, out var ownerName))
                    {
                        ownerName = doc.GetElement(owner)?.Name ?? $"id {owner.Value}";
                        ownerNames[owner.Value] = ownerName;
                    }
                    row.Details.Add(
                        $"Conflict: {DescribeElement(el)} is in assembly '{ownerName}'");
                }
            }

            // ── C. Add: batch first, element-by-element only on failure ───────
            var addedIds = new List<ElementId>();
            bool retried = false;
            string firstError = "";

            if (toAdd.Count > 0)
            {
                string batchError = TryAddBatch(doc, assembly, toAdd);
                if (batchError.Length == 0)
                {
                    addedIds = toAdd;
                }
                else
                {
                    retried = true;
                    firstError = batchError;
                    addedIds = AddOneByOne(doc, assembly, toAdd, row);
                }
            }

            row.ComboAdded = addedIds.Count(id => comboToAdd.Contains(id.Value));
            row.Added = addedIds.Count - row.ComboAdded;
            if (row.Failed > 0 && firstError.Length == 0 && row.Details.Count > 0)
                firstError = row.Details.Last();

            // ── D. Assembly type may change when members change ───────────────
            string typeNote = "";
            var after = doc.GetElement(assembly.Id);
            if (after != null)
            {
                var typeAfterId = after.GetTypeId();
                if (typeAfterId.Value != typeBeforeId.Value)
                {
                    string oldName = doc.GetElement(typeBeforeId)?.Name ?? $"id {typeBeforeId.Value}";
                    string newName = doc.GetElement(typeAfterId)?.Name ?? $"id {typeAfterId.Value}";
                    typeNote = $"type changed: {oldName} → {newName}";
                }
            }

            // ── E. Status and notes ───────────────────────────────────────────
            bool wholeFailure = toAdd.Count > 0 && addedIds.Count == 0;
            row.StatusKind = wholeFailure ? "fail"
                : (row.Conflicts > 0 || row.Failed > 0 || typeNote.Length > 0) ? "warn"
                : "ok";

            var parts = new List<string>();
            // Only combos added (or nothing at all) reads as "Nothing to add":
            // there were no pieces to add.
            if (row.Added == 0 && row.Conflicts == 0 && row.Failed == 0)
                parts.Add("Nothing to add");
            else
                parts.Add($"{row.Added} added");
            if (row.ComboAdded > 0) parts.Add($"{row.ComboAdded} combo added");
            if (row.Present > 0) parts.Add($"{row.Present} already present");
            if (row.Conflicts > 0) parts.Add($"{row.Conflicts} in other assemblies");
            if (row.Failed > 0)
                parts.Add($"{row.Failed} failed" + (firstError.Length > 0 ? $": {firstError}" : ""));
            if (retried) parts.Add("batch failed, retried individually");
            if (typeNote.Length > 0) parts.Add(typeNote);
            row.Note = string.Join(" · ", parts);

            return row;
        }

        /// <summary>
        /// Every shared nested instance below the assembly's members, at any
        /// depth. Intermediate levels are included: GetSubComponentIds returns
        /// the direct children of one instance, and each child is walked in turn.
        /// </summary>
        private static HashSet<ElementId> CollectNested(Document doc, AssemblyInstance assembly)
        {
            var found = new HashSet<ElementId>();
            var visited = new HashSet<ElementId>();
            var stack = new Stack<FamilyInstance>();

            foreach (var id in assembly.GetMemberIds())
                if (doc.GetElement(id) is FamilyInstance fi)
                    stack.Push(fi);

            while (stack.Count > 0)
            {
                var fi = stack.Pop();
                if (!visited.Add(fi.Id)) continue;

                ICollection<ElementId>? subs;
                try { subs = fi.GetSubComponentIds(); }
                catch { continue; }
                if (subs == null) continue;

                foreach (var subId in subs)
                {
                    found.Add(subId);
                    if (doc.GetElement(subId) is FamilyInstance subFi)
                        stack.Push(subFi);
                }
            }

            return found;
        }

        /// <summary>
        /// One AddMemberIds call for the whole list in its own transaction.
        /// Returns "" on success, otherwise why it did not land (the transaction
        /// is rolled back, so nothing from a failed batch persists).
        /// </summary>
        private static string TryAddBatch(
            Document doc, AssemblyInstance assembly, List<ElementId> ids)
        {
            using var tx = new Transaction(doc, $"Add nested — {assembly.Name}");
            try
            {
                tx.Start();
                SuppressWarnings(tx);
                assembly.AddMemberIds(ids);
                var status = tx.Commit();
                return status == TransactionStatus.Committed
                    ? ""
                    : $"transaction not committed ({status})";
            }
            catch (Exception ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                return ex.Message;
            }
        }

        /// <summary>
        /// Failure path: retry each id in its own sub-transaction inside a single
        /// transaction, so a bad element is rolled back alone. Returns the ids
        /// that were added; the rest are recorded on the row as failed.
        /// </summary>
        private static List<ElementId> AddOneByOne(
            Document doc, AssemblyInstance assembly, List<ElementId> ids, NestedReportRow row)
        {
            var addedIds = new List<ElementId>();
            var failures = new List<(ElementId Id, string Reason)>();

            using var tx = new Transaction(doc, $"Add nested (retry) — {assembly.Name}");
            string commitProblem = "";
            try
            {
                tx.Start();
                SuppressWarnings(tx);

                foreach (var id in ids)
                {
                    using var sub = new SubTransaction(doc);
                    try
                    {
                        sub.Start();
                        assembly.AddMemberIds(new List<ElementId> { id });
                        sub.Commit();
                        addedIds.Add(id);
                    }
                    catch (Exception ex)
                    {
                        if (sub.GetStatus() == TransactionStatus.Started)
                            sub.RollBack();
                        failures.Add((id, ex.Message));
                    }
                }

                var status = tx.Commit();
                if (status != TransactionStatus.Committed)
                    commitProblem = $"transaction not committed ({status})";
            }
            catch (Exception ex)
            {
                if (tx.GetStatus() == TransactionStatus.Started)
                    tx.RollBack();
                commitProblem = ex.Message;
            }

            // If the retry transaction itself did not land, none of the
            // sub-transactions persisted either.
            if (commitProblem.Length > 0)
            {
                foreach (var id in addedIds)
                    failures.Add((id, commitProblem));
                addedIds.Clear();
            }

            foreach (var (id, reason) in failures)
            {
                row.Failed++;
                row.Details.Add($"Failed: {DescribeElement(doc.GetElement(id), id)}: {reason}");
            }

            return addedIds;
        }

        private static string DescribeElement(Element? el, ElementId? fallbackId = null)
        {
            if (el == null) return $"id {fallbackId?.Value}";
            return $"{el.Name} (id {el.Id.Value})";
        }

        // ── Failure handling: drop warnings so no dialog interrupts the run ──
        private static void SuppressWarnings(Transaction tx)
        {
            var opts = tx.GetFailureHandlingOptions();
            opts.SetFailuresPreprocessor(new WarningSwallower());
            opts.SetClearAfterRollback(true);
            tx.SetFailureHandlingOptions(opts);
        }

        private sealed class WarningSwallower : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
            {
                foreach (var f in a.GetFailureMessages())
                    if (f.GetSeverity() == FailureSeverity.Warning)
                        a.DeleteWarning(f);
                return FailureProcessingResult.Continue;
            }
        }
    }
}
