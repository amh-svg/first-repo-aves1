// -----------------------------------------------------------------------
// Title: Revisions & Title Block Fill
// Summary: Opens a sheet picker/tool window where the user selects sheets,
//          chooses revisions to add, and resolves title block field values
//          to apply, then writes those changes to the selected sheets and
//          reports the outcome.
// Key logic: One transaction per sheet adds the picked revision IDs via
//            SetAdditionalRevisionIds and sets resolved title block shared
//            parameters, skipping fields the user didn't confirm a value
//            for (e.g. mixed values across sheets).
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AVES.Core;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfDataGridCell = System.Windows.Controls.DataGridCell;
using WpfDataGridColumnHeader = System.Windows.Controls.Primitives.DataGridColumnHeader;
using WpfDataGridTemplateColumn = System.Windows.Controls.DataGridTemplateColumn;
using WpfDataGridTextColumn = System.Windows.Controls.DataGridTextColumn;
using WpfFontWeights = System.Windows.FontWeights;
using WpfGrid = System.Windows.Controls.Grid;
using WpfHAlign = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfRowDef = System.Windows.Controls.RowDefinition;
using WpfScrollBarVis = System.Windows.Controls.ScrollBarVisibility;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfThickness = System.Windows.Thickness;
using WpfVisibility = System.Windows.Visibility;

namespace AVES.Commands
{
    // ─────────────────────────────────────────────────────────────────────────
    //  REPORT ROW
    // ─────────────────────────────────────────────────────────────────────────

    internal class TbFillReportRow
    {
        public string SheetNumber { get; set; } = "";
        public string SheetName { get; set; } = "";
        public string StatusKind { get; set; } = "ok";   // "ok" | "skip" | "fail" | "warn"
        public string Note { get; set; } = "";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SHEET PICKER POPUP (search box + checkbox list + select-all)
    // ─────────────────────────────────────────────────────────────────────────

    internal class SheetPickerWindow : Window
    {
        public List<ViewSheet> SelectedSheets { get; private set; } = new List<ViewSheet>();

        private readonly List<(ViewSheet Sheet, WpfBorder Row, WpfCheckBox Box)> _rows
            = new List<(ViewSheet, WpfBorder, WpfCheckBox)>();
        private readonly WpfTextBox _searchBox;
        private readonly WpfTextBlock _countLabel;
        private bool _confirmed;

        public SheetPickerWindow(List<ViewSheet> allSheets, List<ViewSheet> currentlySelected)
        {
            var selectedIds = new HashSet<ElementId>(currentlySelected.Select(s => s.Id));
            var ordered = allSheets.OrderBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase).ToList();

            Title = "Select Sheets";
            Width = 480;
            Height = 620;
            MinWidth = 380;
            MinHeight = 380;
            ResizeMode = ResizeMode.CanResize;
            // Close-only title bar that still resizes. ResizeMode.NoResize also
            // hides minimize/maximize but takes resizing with it, which a long
            // scrolling list needs.
            WindowStyle = WindowStyle.ToolWindow;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = AvTokens.B(AvTokens.Surface);
            FontFamily = AvTokens.Ui;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var shell = new WpfGrid();
            shell.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(44) });
            shell.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(1, GridUnitType.Star) });
            shell.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });
            Content = shell;

            var titleBar = AvControls.TitleBar("Select Sheets");
            WpfGrid.SetRow(titleBar, 0);
            shell.Children.Add(titleBar);

            // Search box
            var searchWrap = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new WpfThickness(20, 14, 20, 10),
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
                ToolTip = "Filter sheets by number or name"
            };
            _searchBox.TextChanged += (s, e) => ApplyFilter();
            searchRow.Children.Add(_searchBox);
            searchWrap.Child = searchRow;
            WpfGrid.SetRow(searchWrap, 1);
            shell.Children.Add(searchWrap);

            // Select all / none row
            var selRow = new WpfGrid { Margin = new WpfThickness(20, 0, 20, 8) };
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var btnAll = AvControls.SecondaryBtn("Select All", () => SetAllChecked(true));
            btnAll.FontSize = 12.5;
            btnAll.Padding = new WpfThickness(10, 5, 10, 5);
            btnAll.Margin = new WpfThickness(0, 0, 6, 0);
            btnAll.Foreground = AvTokens.B(AvTokens.Accent);
            var btnNone = AvControls.SecondaryBtn("Deselect All", () => SetAllChecked(false));
            btnNone.FontSize = 12.5;
            btnNone.Padding = new WpfThickness(10, 5, 10, 5);
            _countLabel = new WpfTextBlock
            {
                FontFamily = AvTokens.Ui,
                FontSize = 11,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = WpfHAlign.Right
            };
            WpfGrid.SetColumn(btnAll, 0);
            WpfGrid.SetColumn(btnNone, 1);
            WpfGrid.SetColumn(_countLabel, 2);
            selRow.Children.Add(btnAll);
            selRow.Children.Add(btnNone);
            selRow.Children.Add(_countLabel);
            WpfGrid.SetRow(selRow, 2);
            shell.Children.Add(selRow);

            // List
            var list = new WpfStackPanel { Margin = new WpfThickness(20, 0, 20, 10) };
            foreach (var sheet in ordered)
            {
                string label = $"{sheet.SheetNumber} — {sheet.Name}";
                var (visual, box) = AvControls.CheckRow(label, false);
                box.IsChecked = selectedIds.Contains(sheet.Id);
                box.Checked += (s, e) => UpdateCount();
                box.Unchecked += (s, e) => UpdateCount();
                visual.MouseLeftButtonUp += (s, e) => { box.IsChecked = !box.IsChecked; };
                list.Children.Add(visual);
                _rows.Add((sheet, visual, box));
            }
            var scroll = new WpfScrollViewer
            {
                VerticalScrollBarVisibility = WpfScrollBarVis.Auto,
                HorizontalScrollBarVisibility = WpfScrollBarVis.Disabled,
                Content = list
            };
            WpfGrid.SetRow(scroll, 3);
            shell.Children.Add(scroll);

            // Footer
            var footer = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(0, 1, 0, 0),
                Padding = new WpfThickness(20, 14, 20, 14)
            };
            var footerRow = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHAlign.Right };
            var btnCancel = AvControls.GhostBtn("Cancel", OnCancel);
            var btnOk = AvControls.PrimaryBtn("Use Selection", OnOk);
            btnCancel.Margin = new WpfThickness(0, 0, 8, 0);
            footerRow.Children.Add(btnCancel);
            footerRow.Children.Add(btnOk);
            footer.Child = footerRow;
            WpfGrid.SetRow(footer, 4);
            shell.Children.Add(footer);

            UpdateCount();
        }

        private void ApplyFilter()
        {
            var filter = _searchBox.Text?.Trim() ?? "";
            foreach (var (sheet, row, _) in _rows)
            {
                bool match = filter.Length == 0
                    || sheet.SheetNumber.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                    || sheet.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                row.Visibility = match ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            }
        }

        private void SetAllChecked(bool value)
        {
            foreach (var (_, row, box) in _rows)
            {
                if (row.Visibility == WpfVisibility.Visible)
                    box.IsChecked = value;
            }
            UpdateCount();
        }

        private void UpdateCount()
        {
            int checkedCount = _rows.Count(r => r.Box.IsChecked == true);
            _countLabel.Text = $"{checkedCount} of {_rows.Count} selected";
        }

        private void OnOk()
        {
            SelectedSheets = _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Sheet).ToList();
            _confirmed = true;
            Close();
        }

        private void OnCancel() => Close();

        protected override void OnClosed(EventArgs e)
        {
            if (!_confirmed) SelectedSheets = new List<ViewSheet>();
            base.OnClosed(e);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  REVISION PICKER POPUP
    // ─────────────────────────────────────────────────────────────────────────

    internal class RevisionPickerWindow : Window
    {
        public List<Revision> SelectedRevisions { get; private set; } = new List<Revision>();

        private readonly List<(Revision Rev, WpfCheckBox Box)> _rows = new List<(Revision, WpfCheckBox)>();
        private bool _confirmed;

        public RevisionPickerWindow(List<Revision> allRevisions, List<Revision> currentlySelected)
        {
            var selectedIds = new HashSet<ElementId>(currentlySelected.Select(r => r.Id));
            var ordered = allRevisions.OrderBy(r => r.SequenceNumber).ToList();

            Title = "Select Revisions";
            Width = 520;
            Height = 560;
            MinWidth = 420;
            MinHeight = 360;
            ResizeMode = ResizeMode.CanResize;
            WindowStyle = WindowStyle.ToolWindow;   // close-only, still resizable
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

            var titleBar = AvControls.TitleBar("Select Revisions");
            WpfGrid.SetRow(titleBar, 0);
            shell.Children.Add(titleBar);

            var list = new WpfStackPanel { Margin = new WpfThickness(20, 14, 20, 10) };
            if (ordered.Count == 0)
            {
                list.Children.Add(new WpfTextBlock
                {
                    Text = "No revisions exist in this project.",
                    FontFamily = AvTokens.Ui,
                    FontSize = 13,
                    Foreground = AvTokens.B(AvTokens.TextFaint)
                });
            }
            foreach (var rev in ordered)
            {
                string label = $"Rev {rev.SequenceNumber} — {rev.RevisionDate} — {rev.Description}";
                var (visual, box) = AvControls.CheckRow(label, false);
                box.IsChecked = selectedIds.Contains(rev.Id);
                visual.MouseLeftButtonUp += (s, e) => { box.IsChecked = !box.IsChecked; };
                list.Children.Add(visual);
                _rows.Add((rev, box));
            }
            var scroll = new WpfScrollViewer
            {
                VerticalScrollBarVisibility = WpfScrollBarVis.Auto,
                HorizontalScrollBarVisibility = WpfScrollBarVis.Disabled,
                Content = list
            };
            WpfGrid.SetRow(scroll, 1);
            shell.Children.Add(scroll);

            var footer = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(0, 1, 0, 0),
                Padding = new WpfThickness(20, 14, 20, 14)
            };
            var footerRow = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHAlign.Right };
            var btnCancel = AvControls.GhostBtn("Cancel", OnCancel);
            var btnOk = AvControls.PrimaryBtn("Use Selection", OnOk);
            btnCancel.Margin = new WpfThickness(0, 0, 8, 0);
            footerRow.Children.Add(btnCancel);
            footerRow.Children.Add(btnOk);
            footer.Child = footerRow;
            WpfGrid.SetRow(footer, 2);
            shell.Children.Add(footer);
        }

        private void OnOk()
        {
            SelectedRevisions = _rows.Where(r => r.Box.IsChecked == true).Select(r => r.Rev).ToList();
            _confirmed = true;
            Close();
        }

        private void OnCancel() => Close();

        protected override void OnClosed(EventArgs e)
        {
            if (!_confirmed) SelectedRevisions = new List<Revision>();
            base.OnClosed(e);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TITLE BLOCK PARAMETER FIELD (one text input per discovered parameter)
    // ─────────────────────────────────────────────────────────────────────────

    internal class TbParamField
    {
        public string ParamName = "";
        public bool Agreed;
        public string? AgreedValue;
        public WpfTextBox Input = null!;
        public WpfTextBlock Placeholder = null!;

        /// <summary>
        /// The value to apply, or null if this field should be left untouched
        /// (mixed values across sheets and the user never typed a replacement).
        /// </summary>
        public string? ResolveValueToApply()
        {
            if (Agreed) return Input.Text;
            return Input.Text.Length > 0 ? Input.Text : null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  MAIN WINDOW
    // ─────────────────────────────────────────────────────────────────────────

    internal class MainToolWindow : Window
    {
        private readonly Document _doc;
        private List<ViewSheet> _allSheets;
        private List<Revision> _allRevisions;

        public List<ViewSheet> SelectedSheets { get; private set; } = new List<ViewSheet>();
        public List<Revision> SelectedRevisions { get; private set; } = new List<Revision>();
        public List<TbParamField> TitleBlockFields { get; private set; } = new List<TbParamField>();
        public bool Confirmed { get; private set; }

        private readonly WpfTextBlock _sheetsSummary;
        private readonly WpfTextBlock _revisionsSummary;
        private readonly WpfStackPanel _tbSection;
        private readonly WpfButton _runBtn;

        public MainToolWindow(Document doc, List<ViewSheet> allSheets, List<Revision> allRevisions)
        {
            _doc = doc;
            _allSheets = allSheets;
            _allRevisions = allRevisions;

            Title = "Revisions & Title Block Fill";
            Width = 560;
            Height = 640;
            MinWidth = 460;
            MinHeight = 420;
            ResizeMode = ResizeMode.CanResize;
            WindowStyle = WindowStyle.ToolWindow;   // close-only, still resizable
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

            var titleBar = AvControls.TitleBar("Revisions & Title Block Fill");
            WpfGrid.SetRow(titleBar, 0);
            shell.Children.Add(titleBar);

            var bodyScroll = new WpfScrollViewer
            {
                VerticalScrollBarVisibility = WpfScrollBarVis.Auto,
                HorizontalScrollBarVisibility = WpfScrollBarVis.Disabled
            };
            var body = new WpfStackPanel { Margin = new WpfThickness(20) };
            bodyScroll.Content = body;
            WpfGrid.SetRow(bodyScroll, 1);
            shell.Children.Add(bodyScroll);

            // ── Section 1: Sheets ──
            body.Children.Add(AvControls.FieldLabel(1, "Sheets"));

            var sheetsRow = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 20) };
            sheetsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sheetsRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _sheetsSummary = new WpfTextBlock
            {
                Text = "No sheets selected",
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                Foreground = AvTokens.B(AvTokens.TextDim),
                VerticalAlignment = VerticalAlignment.Center
            };
            var pickSheetsBtn = AvControls.SecondaryBtn("Select Sheets…", OnPickSheets);
            WpfGrid.SetColumn(_sheetsSummary, 0);
            WpfGrid.SetColumn(pickSheetsBtn, 1);
            sheetsRow.Children.Add(_sheetsSummary);
            sheetsRow.Children.Add(pickSheetsBtn);
            body.Children.Add(sheetsRow);

            // ── Section 2: Revisions ──
            body.Children.Add(AvControls.FieldLabel(2, "Revisions to Add"));

            var revRow = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 20) };
            revRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            revRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _revisionsSummary = new WpfTextBlock
            {
                Text = "No revisions selected",
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                Foreground = AvTokens.B(AvTokens.TextDim),
                VerticalAlignment = VerticalAlignment.Center
            };
            var pickRevBtn = AvControls.SecondaryBtn("Select Revisions…", OnPickRevisions);
            WpfGrid.SetColumn(_revisionsSummary, 0);
            WpfGrid.SetColumn(pickRevBtn, 1);
            revRow.Children.Add(_revisionsSummary);
            revRow.Children.Add(pickRevBtn);
            body.Children.Add(revRow);

            // ── Section 3: Title block parameters (populated dynamically) ──
            body.Children.Add(AvControls.FieldLabel(3, "Title Block Parameters"));

            _tbSection = new WpfStackPanel();
            body.Children.Add(_tbSection);
            RenderTbSection(new List<string> { "Select sheets to discover title block parameters." }, new List<TbParamField>());

            // Footer
            var footer = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(0, 1, 0, 0),
                Padding = new WpfThickness(20, 14, 20, 14)
            };
            var footerRow = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHAlign.Right };
            var btnCancel = AvControls.GhostBtn("Cancel", OnCancel);
            _runBtn = AvControls.PrimaryBtn("▶  Run Script", OnRun);
            _runBtn.IsEnabled = false;
            btnCancel.Margin = new WpfThickness(0, 0, 8, 0);
            footerRow.Children.Add(btnCancel);
            footerRow.Children.Add(_runBtn);
            footer.Child = footerRow;
            WpfGrid.SetRow(footer, 2);
            shell.Children.Add(footer);
        }

        private void OnPickSheets()
        {
            var picker = new SheetPickerWindow(_allSheets, SelectedSheets) { Owner = this };
            picker.ShowDialog();
            if (picker.SelectedSheets.Count == 0 && SelectedSheets.Count == 0) return;

            SelectedSheets = picker.SelectedSheets;
            _sheetsSummary.Text = SelectedSheets.Count == 0
                ? "No sheets selected"
                : $"{SelectedSheets.Count} sheet{(SelectedSheets.Count == 1 ? "" : "s")} selected";

            RefreshTitleBlockDiscovery();
            UpdateRunEnabled();
        }

        private void OnPickRevisions()
        {
            var picker = new RevisionPickerWindow(_allRevisions, SelectedRevisions) { Owner = this };
            picker.ShowDialog();
            SelectedRevisions = picker.SelectedRevisions;
            _revisionsSummary.Text = SelectedRevisions.Count == 0
                ? "No revisions selected"
                : $"{SelectedRevisions.Count} revision{(SelectedRevisions.Count == 1 ? "" : "s")} selected";
            UpdateRunEnabled();
        }

        private void RefreshTitleBlockDiscovery()
        {
            if (SelectedSheets.Count == 0)
            {
                TitleBlockFields = new List<TbParamField>();
                RenderTbSection(new List<string> { "Select sheets to discover title block parameters." }, new List<TbParamField>());
                return;
            }

            var (errors, fields) = DiscoverSheetLabelParams(SelectedSheets);
            TitleBlockFields = fields;
            RenderTbSection(errors, fields);
        }

        /// <summary>
        /// The title block's on-sheet labels turned out to be driven by shared
        /// parameters bound to the Sheets category (OST_Sheets), not by
        /// parameters on the title block family instance/type — so discovery
        /// reads the document's parameter bindings directly, and values are
        /// read/written on each ViewSheet itself. Title block placement/type no
        /// longer has any bearing on this operation, since every ViewSheet
        /// carries these parameters regardless of what title block (if any)
        /// is placed on it.
        /// </summary>
        private (List<string> errors, List<TbParamField> fields) DiscoverSheetLabelParams(List<ViewSheet> sheets)
        {
            var errors = new List<string>();

            var boundDefs = new List<Definition>();
            var it = _doc.ParameterBindings.ForwardIterator();
            it.Reset();
            while (it.MoveNext())
            {
                var def = it.Key;
                if (it.Current is InstanceBinding ib)
                {
                    foreach (Category cat in ib.Categories)
                    {
                        if (cat.Id.Value == (int)BuiltInCategory.OST_Sheets)
                        {
                            boundDefs.Add(def);
                            break;
                        }
                    }
                }
            }

            // Definition alone doesn't expose StorageType/IsReadOnly/IsShared —
            // those live on a Parameter instance, so resolve each bound
            // definition against a representative sheet to qualify it.
            var representative = sheets[0];
            var qualifying = new List<Definition>();
            foreach (var def in boundDefs)
            {
                var p = representative.get_Parameter(def);
                if (p == null) continue;
                if (p.StorageType != StorageType.String) continue;
                if (p.IsReadOnly) continue;
                if (!p.IsShared) continue;
                if (p.Definition is InternalDefinition pDef && pDef.BuiltInParameter != BuiltInParameter.INVALID) continue;
                qualifying.Add(def);
            }

            if (qualifying.Count == 0)
            {
                errors.Add("No matching sheet parameters found (string, editable, shared parameter bound to Sheets).");
                return (errors, new List<TbParamField>());
            }

            var fields = new List<TbParamField>();
            foreach (var def in qualifying)
            {
                var values = sheets.Select(s => s.get_Parameter(def)?.AsString() ?? "").ToList();
                bool agreed = values.Distinct().Count() == 1;
                fields.Add(new TbParamField
                {
                    ParamName = def.Name,
                    Agreed = agreed,
                    AgreedValue = agreed ? values[0] : null
                });
            }

            return (errors, fields);
        }

        private void RenderTbSection(List<string> errors, List<TbParamField> fields)
        {
            _tbSection.Children.Clear();

            if (errors.Count > 0)
            {
                foreach (var err in errors)
                {
                    var banner = new WpfBorder
                    {
                        Background = AvTokens.B(AvTokens.FailBg),
                        CornerRadius = new CornerRadius(5),
                        Padding = new WpfThickness(12, 10, 12, 10),
                        Margin = new WpfThickness(0, 0, 0, 8)
                    };
                    banner.Child = new WpfTextBlock
                    {
                        Text = err,
                        FontFamily = AvTokens.Ui,
                        FontSize = 12,
                        Foreground = AvTokens.B(AvTokens.FailFg),
                        TextWrapping = TextWrapping.Wrap
                    };
                    _tbSection.Children.Add(banner);
                }
                _tbSection.Children.Add(new WpfTextBlock
                {
                    Text = "Title block parameters will not be applied until this is resolved. Revisions can still be added.",
                    FontFamily = AvTokens.Ui,
                    FontSize = 11.5,
                    Foreground = AvTokens.B(AvTokens.TextFaint),
                    Margin = new WpfThickness(0, 0, 0, 10),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            if (fields.Count == 0)
            {
                _tbSection.Children.Add(new WpfTextBlock
                {
                    Text = "Select sheets to discover title block parameters.",
                    FontFamily = AvTokens.Ui,
                    FontSize = 12.5,
                    Foreground = AvTokens.B(AvTokens.TextFaint),
                    Margin = new WpfThickness(0, 0, 0, 10)
                });
                return;
            }

            foreach (var field in fields)
            {
                var row = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var label = new WpfTextBlock
                {
                    Text = field.ParamName + ":",
                    FontFamily = AvTokens.Ui,
                    FontSize = 12,
                    FontWeight = WpfFontWeights.SemiBold,
                    Foreground = AvTokens.B(AvTokens.TextDim),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new WpfThickness(0, 0, 8, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                var inputWrap = new WpfGrid();
                var input = new WpfTextBox
                {
                    FontFamily = AvTokens.Ui,
                    FontSize = 13,
                    Foreground = AvTokens.B(AvTokens.Text),
                    Background = AvTokens.B(AvTokens.Surface),
                    BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                    BorderThickness = new WpfThickness(1),
                    Padding = new WpfThickness(8, 5, 8, 5),
                    Text = field.Agreed ? (field.AgreedValue ?? "") : ""
                };
                var placeholder = new WpfTextBlock
                {
                    Text = "(mixed values)",
                    FontFamily = AvTokens.Ui,
                    FontSize = 12.5,
                    FontStyle = System.Windows.FontStyles.Italic,
                    Foreground = AvTokens.B(AvTokens.TextFaint),
                    Margin = new WpfThickness(9, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                    Visibility = !field.Agreed ? WpfVisibility.Visible : WpfVisibility.Collapsed
                };
                input.TextChanged += (s, e) =>
                {
                    placeholder.Visibility = (!field.Agreed && input.Text.Length == 0) ? WpfVisibility.Visible : WpfVisibility.Collapsed;
                };
                field.Input = input;
                field.Placeholder = placeholder;

                inputWrap.Children.Add(input);
                inputWrap.Children.Add(placeholder);

                WpfGrid.SetColumn(label, 0);
                WpfGrid.SetColumn(inputWrap, 1);
                row.Children.Add(label);
                row.Children.Add(inputWrap);
                _tbSection.Children.Add(row);
            }
        }

        private void UpdateRunEnabled()
        {
            _runBtn.IsEnabled = SelectedSheets.Count > 0;
        }

        private void OnRun()
        {
            Confirmed = true;
            Close();
        }

        private void OnCancel()
        {
            Confirmed = false;
            Close();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  REPORT WINDOW
    // ─────────────────────────────────────────────────────────────────────────

    internal class TbReportWindow : Window
    {
        private readonly List<TbFillReportRow> _rows;

        public TbReportWindow(List<TbFillReportRow> rows, int processed, int skipped, int failed, string docTitle)
        {
            _rows = rows;

            Title = "Revisions & Title Block Fill";
            Width = 720;
            Height = 560;
            MinWidth = 560;
            MinHeight = 380;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // Surface: the token table scopes Surface-2 to footers, header strips
            // and inset panels, not to a window body.
            Background = AvTokens.B(AvTokens.Surface);
            FontFamily = AvTokens.Ui;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var root = new WpfGrid();
            root.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });
            root.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });
            Content = root;

            // ReportHeaderBar rather than TitleBar: 52px on Surface with a 24px
            // logo, against TitleBar's 44px on Surface2 with a 20px one. The
            // picker windows in this file keep TitleBar -- only reports move.
            var titleBar = AvControls.ReportHeaderBar("Revisions & Title Block Fill");
            WpfGrid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            var bodyScroll = new WpfScrollViewer
            {
                VerticalScrollBarVisibility = WpfScrollBarVis.Auto,
                HorizontalScrollBarVisibility = WpfScrollBarVis.Disabled
            };
            var body = new WpfStackPanel { Margin = new WpfThickness(20) };
            bodyScroll.Content = body;
            WpfGrid.SetRow(bodyScroll, 1);
            root.Children.Add(bodyScroll);

            var statGrid = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 20) };
            for (int c = 0; c < 3; c++) statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var stats = new (string v, string k, string cls)[]
            {
                (processed.ToString(), "Processed", "ok"),
                (skipped.ToString(), "Skipped", ""),
                (failed.ToString(), "Failed", "fail"),
            };
            for (int i = 0; i < stats.Length; i++)
            {
                var tile = AvControls.StatTile(stats[i].v, stats[i].k, stats[i].cls);
                tile.Margin = new WpfThickness(i == 0 ? 0 : 6, 0, i == stats.Length - 1 ? 0 : 6, 0);
                WpfGrid.SetColumn(tile, i);
                statGrid.Children.Add(tile);
            }
            body.Children.Add(statGrid);

            var tableWrap = new WpfBorder
            {
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new CornerRadius(5),
                ClipToBounds = true,
                Background = AvTokens.B(AvTokens.Surface)
            };
            tableWrap.Child = BuildResultsGrid(rows);
            body.Children.Add(tableWrap);

            // LastChildFill, buttons docked first and the caption added last: as
            // a Dock.Left child it sized to content and would have pushed the
            // buttons on a long document name rather than ellipsing.
            var footerInner = new DockPanel { LastChildFill = true };
            var footerRow = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHAlign.Right };
            var copyBtn = AvControls.SecondaryBtn("⧉  Copy to clipboard", () => { });
            copyBtn.Margin = new WpfThickness(0, 0, 10, 0);
            copyBtn.Click += (s, e) => OnCopyToClipboard(copyBtn);
            footerRow.Children.Add(copyBtn);
            var closeBtn = AvControls.PrimaryBtn("Close", () => Close());
            footerRow.Children.Add(closeBtn);
            DockPanel.SetDock(footerRow, Dock.Right);
            footerInner.Children.Add(footerRow);
            footerInner.Children.Add(AvControls.FooterCaption($"Source · {docTitle}"));
            var footer = AvControls.FooterStrip(footerInner);
            WpfGrid.SetRow(footer, 2);
            root.Children.Add(footer);
        }

        /// <summary>
        /// Copies the report as TSV, then flashes the button for two seconds.
        ///
        /// StatusKind is written raw ("ok" / "skip" / "fail" / "warn"), matching
        /// what the other report windows put on the clipboard rather than the
        /// pill's display wording.
        /// </summary>
        private void OnCopyToClipboard(WpfButton sourceBtn)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Sheet Number\tSheet Name\tStatus\tNote");
            foreach (var r in _rows)
                sb.AppendLine(string.Join("\t", r.SheetNumber, r.SheetName, r.StatusKind, r.Note));

            try
            {
                System.Windows.Clipboard.SetText(sb.ToString());
                if (sourceBtn != null)
                {
                    var original = sourceBtn.Content;
                    sourceBtn.Content = "✓  Copied!";
                    var timer = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (s, e) => { sourceBtn.Content = original; timer.Stop(); };
                    timer.Start();
                }
            }
            catch { /* clipboard may be locked by another process */ }
        }

        private WpfDataGrid BuildResultsGrid(List<TbFillReportRow> rows)
        {
            var grid = new WpfDataGrid
            {
                ItemsSource = rows,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = true,
                HeadersVisibility = System.Windows.Controls.DataGridHeadersVisibility.Column,
                GridLinesVisibility = System.Windows.Controls.DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = AvTokens.B(AvTokens.Border),
                RowHeight = 40,
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
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.BackgroundProperty, AvTokens.B(AvTokens.Surface2)));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.ForegroundProperty, AvTokens.B(AvTokens.TextFaint)));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.FontFamilyProperty, AvTokens.Ui));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.FontSizeProperty, 10.5));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.FontWeightProperty, WpfFontWeights.Bold));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.PaddingProperty, new WpfThickness(12, 9, 12, 9)));
            grid.ColumnHeaderStyle = headerStyle;

            var cellStyle = new System.Windows.Style(typeof(WpfDataGridCell));
            cellStyle.Setters.Add(new System.Windows.Setter(WpfDataGridCell.BorderThicknessProperty, new WpfThickness(0)));
            cellStyle.Setters.Add(new System.Windows.Setter(WpfDataGridCell.PaddingProperty, new WpfThickness(12, 8, 12, 8)));
            cellStyle.Setters.Add(new System.Windows.Setter(WpfDataGridCell.BackgroundProperty, WpfBrushes.Transparent));
            cellStyle.Setters.Add(new System.Windows.Setter(WpfDataGridCell.ForegroundProperty, AvTokens.B(AvTokens.Text)));
            // The stock DataGridCell template consumes VerticalContentAlignment
            // nowhere, so setting it here did nothing. None of the three text
            // columns carries an ElementStyle either, so they had no other route
            // to centring. The status pill was never affected -- StatusPillTemplate
            // centres its own root.
            cellStyle.Setters.Add(new System.Windows.Setter(WpfDataGridCell.TemplateProperty, AvControls.DataGridCellTemplate()));
            grid.CellStyle = cellStyle;

            grid.Columns.Add(new WpfDataGridTextColumn { Header = "SHEET", Binding = new System.Windows.Data.Binding("SheetNumber"), Width = new System.Windows.Controls.DataGridLength(0.8, System.Windows.Controls.DataGridLengthUnitType.Star) });
            grid.Columns.Add(new WpfDataGridTextColumn { Header = "NAME", Binding = new System.Windows.Data.Binding("SheetName"), Width = new System.Windows.Controls.DataGridLength(1.6, System.Windows.Controls.DataGridLengthUnitType.Star) });

            var statusCol = new WpfDataGridTemplateColumn { Header = "STATUS", Width = System.Windows.Controls.DataGridLength.Auto, MinWidth = 100 };
            statusCol.CellTemplate = AvControls.StatusPillTemplate();
            grid.Columns.Add(statusCol);

            grid.Columns.Add(new WpfDataGridTextColumn { Header = "NOTES", Binding = new System.Windows.Data.Binding("Note"), Width = new System.Windows.Controls.DataGridLength(2, System.Windows.Controls.DataGridLengthUnitType.Star) });

            return grid;
        }

        // BuildStatusPillTemplate is gone: it reimplemented the pill as a parsed
        // XAML string with ten hardcoded hex values and four hardcoded labels.
        // The geometry matched AvControls.StatusPill exactly, so this is a
        // like-for-like swap -- but the colours now come from AvTokens rather
        // than needing to be re-synced by hand whenever the tokens move.
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  COMMAND
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    public class Cmd_DOC02_SheetInfo : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            var doc = uiDoc.Document;

            try
            {
                var allSheets = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Sheets)
                    .WhereElementIsNotElementType()
                    .Cast<ViewSheet>()
                    .ToList();

                if (allSheets.Count == 0)
                {
                    AvControls.ShowAlert("Revisions & Title Block Fill", "No sheets found in this document.", AlertSeverity.Warning);
                    return Result.Cancelled;
                }

                var allRevisions = new FilteredElementCollector(doc)
                    .OfClass(typeof(Revision))
                    .Cast<Revision>()
                    .ToList();

                var main = new MainToolWindow(doc, allSheets, allRevisions);
                main.ShowDialog();

                if (!main.Confirmed || main.SelectedSheets.Count == 0)
                    return Result.Cancelled;

                var reportRows = new List<TbFillReportRow>();
                int processed = 0, skipped = 0, failed = 0;

                var revisionIdsToAdd = main.SelectedRevisions.Select(r => r.Id).ToList();

                // Only apply title block label fields the user actually resolved
                // (agreed value or explicitly typed a replacement for a mixed
                // field). These are shared parameters bound to the Sheets
                // category, so they're read/written on the ViewSheet directly.
                var fieldsToApply = main.TitleBlockFields
                    .Select(f => new { f.ParamName, Value = f.ResolveValueToApply() })
                    .Where(f => f.Value != null)
                    .ToList();

                foreach (var sheet in main.SelectedSheets)
                {
                    using (var tx = new Transaction(doc, $"Revisions & Title Block — {sheet.SheetNumber}"))
                    {
                        try
                        {
                            tx.Start();
                            var notes = new List<string>();

                            if (revisionIdsToAdd.Count > 0)
                            {
                                var existing = new HashSet<ElementId>(sheet.GetAdditionalRevisionIds());
                                existing.UnionWith(revisionIdsToAdd);
                                sheet.SetAdditionalRevisionIds(existing.ToList());
                            }

                            foreach (var f in fieldsToApply)
                            {
                                var param = sheet.LookupParameter(f.ParamName);
                                if (param != null && !param.IsReadOnly && param.StorageType == StorageType.String)
                                    param.Set(f.Value);
                            }

                            tx.Commit();
                            processed++;
                            reportRows.Add(new TbFillReportRow
                            {
                                SheetNumber = sheet.SheetNumber,
                                SheetName = sheet.Name,
                                StatusKind = notes.Count > 0 ? "warn" : "ok",
                                Note = string.Join(" ", notes)
                            });
                        }
                        catch (Exception txEx)
                        {
                            if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                            failed++;
                            reportRows.Add(new TbFillReportRow
                            {
                                SheetNumber = sheet.SheetNumber,
                                SheetName = sheet.Name,
                                StatusKind = "fail",
                                Note = txEx.Message
                            });
                        }
                    }
                }

                var report = new TbReportWindow(reportRows, processed, skipped, failed, doc.Title);
                report.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AvControls.ShowAlert("Revisions & Title Block Fill – Error", ex.Message, AlertSeverity.Error);
                return Result.Failed;
            }
        }
    }
}
