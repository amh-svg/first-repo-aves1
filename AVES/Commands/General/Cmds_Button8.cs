// -----------------------------------------------------------------------
// Title: Assembly Weight
// Summary: Lets the user pick assemblies and a source weight parameter,
//          totals the weight of each assembly's members, and writes the
//          result to a chosen parameter on that assembly's sheet(s).
// Key logic: Pre-computes an assembly-to-sheets map for performance on
//            large projects, sums each member's weight parameter per
//            assembly, and reports any members missing a weight value.
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
using WpfClipboard = System.Windows.Clipboard;
using WpfColor = System.Windows.Media.Color;
using WpfControl = System.Windows.Controls.Control;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfDataGridCell = System.Windows.Controls.DataGridCell;
using WpfDataGridColumnHeader = System.Windows.Controls.Primitives.DataGridColumnHeader;
using WpfDataGridTemplateColumn = System.Windows.Controls.DataGridTemplateColumn;
using WpfDataGridTextColumn = System.Windows.Controls.DataGridTextColumn;
using WpfDataTemplate = System.Windows.DataTemplate;
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
using WpfSolidBrush = System.Windows.Media.SolidColorBrush;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfThickness = System.Windows.Thickness;

namespace AVES.Cmds_Button8
{
    // ─────────────────────────────────────────────────────────────────────────
    //  LAST-USED PARAMETER MEMORY (persists across Revit sessions)
    // ─────────────────────────────────────────────────────────────────────────

    internal static class LastParamSettings
    {
        private static readonly string SettingsPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AVES", "Button8_LastParams.txt");

        public static string LastWeightParam { get; private set; }
        public static string LastSheetParam { get; private set; }

        private static bool _loaded;

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (System.IO.File.Exists(SettingsPath))
                {
                    var lines = System.IO.File.ReadAllLines(SettingsPath);
                    if (lines.Length > 0) LastWeightParam = lines[0];
                    if (lines.Length > 1) LastSheetParam = lines[1];
                }
            }
            catch
            {
                // Non-critical: if reading fails, just fall back to default selection logic.
            }
        }

        public static void Save(string weightParam, string sheetParam)
        {
            LastWeightParam = weightParam;
            LastSheetParam = sheetParam;
            try
            {
                var dir = System.IO.Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllLines(SettingsPath, new[] { weightParam ?? "", sheetParam ?? "" });
            }
            catch
            {
                // Non-critical: failing to persist shouldn't interrupt the workflow.
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  DATA HELPERS
    // ─────────────────────────────────────────────────────────────────────────

    internal class ReportRow
    {
        public string AssemblyName { get; set; }
        public string StatusKind { get; set; }   // "ok" | "skip" | "fail" | "warn"
        public string Note { get; set; } = "";
        public double TotalWeight { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PARAMETER PICKER WINDOW (single-select popup, replaces inline dropdowns)
    // ─────────────────────────────────────────────────────────────────────────

    internal class ParamPickerWindow : Window
    {
        public string SelectedValue { get; private set; }

        private readonly List<string> _allOptions;
        private readonly WpfListBox _listBox;
        private readonly WpfTextBox _searchBox;

        public ParamPickerWindow(string title, List<string> options, string currentValue)
        {
            _allOptions = options ?? new List<string>();

            Title = title;
            Width = 380;
            Height = 480;
            MinWidth = 300;
            MinHeight = 320;
            ResizeMode = ResizeMode.CanResize;
            // Close-only title bar that still resizes. ResizeMode.NoResize also
            // hides minimize/maximize but takes resizing with it, which a long
            // scrolling list needs.
            WindowStyle = WindowStyle.ToolWindow;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = AvTokens.B(AvTokens.Surface);
            FontFamily = AvTokens.Ui;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var shell = new WpfGrid();
            shell.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(44) });
            shell.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(1, GridUnitType.Star) });
            shell.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });
            Content = shell;

            // Title bar
            var titleBar = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(0, 0, 0, 1),
                Padding = new WpfThickness(20, 0, 20, 0)
            };
            var titleRow = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(AvControls.BrandTile(24));
            titleRow.Children.Add(new WpfTextBlock
            {
                Text = title,
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

            // Search box
            var searchWrap = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new WpfThickness(16, 14, 16, 10),
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
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new WpfThickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 200,
                ToolTip = "Filter parameters by name"
            };
            _searchBox.TextChanged += (s, e) => RefreshList();
            searchRow.Children.Add(_searchBox);
            searchWrap.Child = searchRow;
            WpfGrid.SetRow(searchWrap, 1);
            shell.Children.Add(searchWrap);

            // List box
            _listBox = new WpfListBox
            {
                SelectionMode = WpfSelMode.Single,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                Foreground = AvTokens.B(AvTokens.Text),
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(1, 0, 1, 0),
                Padding = new WpfThickness(0),
                Margin = new WpfThickness(16, 0, 16, 10)
            };

            // Rows are built by RefreshList as bare ListBoxItems whose Content is a
            // plain string, so the style's FontWeight reaches the text with nothing
            // local to override it.
            _listBox.ItemContainerStyle = AvControls.PickerListItemStyle();
            _listBox.PreviewMouseWheel += ListBox_PreviewMouseWheel;

            _listBox.MouseDoubleClick += (s, e) => { if (_listBox.SelectedItem != null) OnOk(); };

            var scrollViewer = new WpfScrollViewer
            {
                VerticalScrollBarVisibility = WpfScrollBarVis.Auto,
                HorizontalScrollBarVisibility = WpfScrollBarVis.Disabled,
                Content = _listBox
            };
            WpfGrid.SetRow(scrollViewer, 2);
            shell.Children.Add(scrollViewer);

            // Footer
            var footer = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(0, 1, 0, 0),
                Padding = new WpfThickness(16, 12, 16, 12)
            };
            var footerRow = new WpfStackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHAlign.Right
            };
            var btnCancel = AvControls.GhostBtn("Cancel", OnCancel);
            var btnOk = AvControls.PrimaryBtn("Select", OnOk);
            btnCancel.Margin = new WpfThickness(0, 0, 8, 0);
            footerRow.Children.Add(btnCancel);
            footerRow.Children.Add(btnOk);
            footer.Child = footerRow;
            WpfGrid.SetRow(footer, 3);
            shell.Children.Add(footer);

            RefreshList();

            if (!string.IsNullOrEmpty(currentValue))
            {
                var match = _listBox.Items.Cast<WpfListBoxItem>().FirstOrDefault(i => (string)i.Tag == currentValue);
                if (match != null)
                {
                    match.IsSelected = true;
                    _listBox.ScrollIntoView(match);
                }
            }
        }

        private void RefreshList()
        {
            var filter = _searchBox?.Text?.Trim() ?? string.Empty;
            var previouslySelected = (_listBox.SelectedItem as WpfListBoxItem)?.Tag as string;
            _listBox.Items.Clear();
            foreach (var name in _allOptions)
            {
                if (filter.Length == 0 || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var item = new WpfListBoxItem
                    {
                        Tag = name,
                        Content = name,
                        VerticalContentAlignment = VerticalAlignment.Center
                    };
                    _listBox.Items.Add(item);
                    if (name == previouslySelected) item.IsSelected = true;
                }
            }
        }

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
            var item = _listBox.SelectedItem as WpfListBoxItem;
            if (item == null)
            {
                AvControls.ShowAlert("Missing Selection", "Please select a parameter.", AlertSeverity.Warning);
                return;
            }
            SelectedValue = item.Tag as string;
            DialogResult = true;
            Close();
        }

        private void OnCancel()
        {
            DialogResult = false;
            Close();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ASSEMBLY PICKER WINDOW
    // ─────────────────────────────────────────────────────────────────────────

    internal class AssemblyPickerWindow : Window
    {
        public List<AssemblyInstance> SelectedAssemblies { get; private set; }
            = new List<AssemblyInstance>();

        public string SelectedWeightParamName { get; private set; }
        public string SelectedSheetParamName { get; private set; }

        private readonly List<AssemblyInstance> _allAssemblies;
        private readonly WpfListBox _listBox;
        private readonly WpfTextBox _searchBox;
        private readonly WpfBorder _weightParamField;
        private readonly WpfTextBlock _weightParamFieldText;
        private readonly WpfBorder _sheetParamField;
        private readonly WpfTextBlock _sheetParamFieldText;
        private List<string> _weightParamOptions = new List<string>();
        private List<string> _sheetParamOptions = new List<string>();
        private string _selectedWeightParam;
        private string _selectedSheetParam;
        private bool _confirmed;
        private readonly Document _doc;

        public AssemblyPickerWindow(Document doc, List<AssemblyInstance> assemblies)
        {
            _doc = doc;
            _allAssemblies = assemblies.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();

            Title = "Add Total Weight to Sheets";
            Width = 520;
            Height = 640;
            MinWidth = 420;
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

            // Title bar
            var titleBar = new System.Windows.Controls.Border
            {
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(0, 0, 0, 1),
                Padding = new WpfThickness(20, 0, 20, 0)
            };
            var titleRow = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(AvControls.BrandTile(26));
            titleRow.Children.Add(new WpfTextBlock
            {
                Text = "Add Total Weight to Sheets",
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

            // Body
            var body = new WpfGrid { Margin = new WpfThickness(20, 16, 20, 16) };
            body.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto }); // weight section label
            body.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto }); // weight param
            body.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto }); // sheet param
            body.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto }); // assemblies section label
            body.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto }); // search
            body.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto }); // sel buttons
            body.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(1, GridUnitType.Star) }); // list
            WpfGrid.SetRow(body, 1);
            shell.Children.Add(body);

            // ── Section 1: Weight parameters ────────────────────────────────
            var weightSectionLabel = AvControls.FieldLabel(1, "Select Weight Parameters");
            weightSectionLabel.Margin = new WpfThickness(0, 0, 0, 10);
            WpfGrid.SetRow(weightSectionLabel, 0);
            body.Children.Add(weightSectionLabel);

            // Weight parameter
            var weightParamRow = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 8) };
            weightParamRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            weightParamRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var weightLabel = new WpfTextBlock
            {
                Text = "Weight Parameter:",
                FontFamily = AvTokens.Ui,
                FontSize = 12,
                FontWeight = WpfFontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.TextDim),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new WpfThickness(0, 0, 8, 0)
            };
            _weightParamField = CreateParamField(
                "Select the project parameter that stores the weight (mass) of each element",
                out _weightParamFieldText);
            _weightParamField.MouseLeftButtonUp += (s, e) => OpenWeightParamPicker();
            PopulateWeightParameters();
            WpfGrid.SetColumn(weightLabel, 0);
            WpfGrid.SetColumn(_weightParamField, 1);
            weightParamRow.Children.Add(weightLabel);
            weightParamRow.Children.Add(_weightParamField);
            WpfGrid.SetRow(weightParamRow, 1);
            body.Children.Add(weightParamRow);

            // Sheet output parameter
            var sheetParamRow = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 8) };
            sheetParamRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sheetParamRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var sheetLabel = new WpfTextBlock
            {
                Text = "Sheet Output Param:",
                FontFamily = AvTokens.Ui,
                FontSize = 12,
                FontWeight = WpfFontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.TextDim),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new WpfThickness(0, 0, 8, 0)
            };
            _sheetParamField = CreateParamField(
                "Select the name of the sheet parameter where the total weight will be written",
                out _sheetParamFieldText);
            _sheetParamField.MouseLeftButtonUp += (s, e) => OpenSheetParamPicker();
            PopulateSheetParameters();
            WpfGrid.SetColumn(sheetLabel, 0);
            WpfGrid.SetColumn(_sheetParamField, 1);
            sheetParamRow.Children.Add(sheetLabel);
            sheetParamRow.Children.Add(_sheetParamField);
            WpfGrid.SetRow(sheetParamRow, 2);
            body.Children.Add(sheetParamRow);

            // ── Section 2: Select assemblies ────────────────────────────────
            var sectionLabel = AvControls.FieldLabel(2, "Select Assemblies");
            sectionLabel.Margin = new WpfThickness(0, 12, 0, 10);
            WpfGrid.SetRow(sectionLabel, 3);
            body.Children.Add(sectionLabel);

            // Search box
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
            WpfGrid.SetRow(searchWrap, 4);
            body.Children.Add(searchWrap);

            // Select All row
            var selRow = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 8) };
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            selRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var btnSelAll = AvControls.SecondaryBtn("Select All", () => SetAllSelection(true));
            btnSelAll.Foreground = AvTokens.B("#E64A2E");
            btnSelAll.FontSize = 13;
            btnSelAll.Padding = new WpfThickness(10, 5, 10, 5);
            var btnSelNone = AvControls.SecondaryBtn("Deselect All", () => SetAllSelection(false));
            btnSelNone.FontSize = 13;
            btnSelNone.Padding = new WpfThickness(10, 5, 10, 5);
            btnSelAll.Margin = new WpfThickness(0, 0, 6, 0);
            btnSelNone.Margin = new WpfThickness(0, 0, 8, 0);

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
            WpfGrid.SetRow(selRow, 5);
            body.Children.Add(selRow);

            // ListBox
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
            itemStyle.Setters.Add(new System.Windows.Setter(WpfListBoxItem.PaddingProperty, new WpfThickness(14, 0, 14, 0)));
            itemStyle.Setters.Add(new System.Windows.Setter(WpfListBoxItem.HeightProperty, 40.0));
            itemStyle.Setters.Add(new System.Windows.Setter(WpfListBoxItem.FontFamilyProperty, AvTokens.Ui));
            itemStyle.Setters.Add(new System.Windows.Setter(WpfListBoxItem.FontSizeProperty, 13.0));
            itemStyle.Setters.Add(new System.Windows.Setter(WpfListBoxItem.ForegroundProperty, AvTokens.B(AvTokens.Text)));
            itemStyle.Setters.Add(new System.Windows.Setter(WpfListBoxItem.BackgroundProperty, System.Windows.Media.Brushes.Transparent));

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
            selectedTrigger.Setters.Add(new System.Windows.Setter(WpfListBoxItem.BackgroundProperty, AvTokens.B(AvTokens.AccentTint)));
            itemStyle.Triggers.Add(selectedTrigger);
            _listBox.ItemContainerStyle = itemStyle;
            _listBox.PreviewMouseWheel += ListBox_PreviewMouseWheel;

            _listBox.SelectionChanged += (s, e) =>
            {
                foreach (var removed in e.RemovedItems.OfType<WpfListBoxItem>()) SyncRowVisual(removed);
                foreach (var added in e.AddedItems.OfType<WpfListBoxItem>()) SyncRowVisual(added);
                int sel = _listBox.SelectedItems.Count;
                int total = _listBox.Items.Count;
                countLabel.Text = sel > 0 ? $"{sel} of {total} selected" : $"{total} assemblies";
            };

            _listBox.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource is System.Windows.DependencyObject d)
                {
                    var item = System.Windows.Controls.ItemsControl.ContainerFromElement(_listBox, d) as WpfListBoxItem;
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
            WpfGrid.SetRow(scrollViewer, 6);
            body.Children.Add(scrollViewer);

            // Footer
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

        // ── Clickable "combo-like" field that opens a ParamPickerWindow popup ──
        private static WpfBorder CreateParamField(string tooltip, out WpfTextBlock textBlock)
        {
            textBlock = new WpfTextBlock
            {
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                Foreground = AvTokens.B(AvTokens.Text),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis
            };
            var row = new WpfGrid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            WpfGrid.SetColumn(textBlock, 0);
            row.Children.Add(textBlock);

            var field = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new WpfThickness(8, 5, 8, 5),
                MinHeight = 28,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = tooltip,
                Child = row
            };
            field.MouseEnter += (s, e) => field.Background = AvTokens.B(AvTokens.Surface2);
            field.MouseLeave += (s, e) => field.Background = AvTokens.B(AvTokens.Surface);
            return field;
        }

        private void OpenWeightParamPicker()
        {
            var picker = new ParamPickerWindow("Select Weight Parameter", _weightParamOptions, _selectedWeightParam)
            {
                Owner = this
            };
            if (picker.ShowDialog() == true && picker.SelectedValue != null)
            {
                _selectedWeightParam = picker.SelectedValue;
                _weightParamFieldText.Text = _selectedWeightParam;
            }
        }

        private void OpenSheetParamPicker()
        {
            var picker = new ParamPickerWindow("Select Sheet Output Parameter", _sheetParamOptions, _selectedSheetParam)
            {
                Owner = this
            };
            if (picker.ShowDialog() == true && picker.SelectedValue != null)
            {
                _selectedSheetParam = picker.SelectedValue;
                _sheetParamFieldText.Text = _selectedSheetParam;
            }
        }

        private void PopulateWeightParameters()
        {
            var paramElements = new FilteredElementCollector(_doc)
                .OfClass(typeof(ParameterElement))
                .Cast<ParameterElement>()
                .ToList();

            var nonBuiltinParams = new List<ParameterElement>();
            foreach (var pe in paramElements)
            {
                var def = pe.GetDefinition();
                if (def is InternalDefinition internalDef)
                {
                    if (internalDef.BuiltInParameter != BuiltInParameter.INVALID)
                        continue;
                }
                nonBuiltinParams.Add(pe);
            }

            _weightParamOptions = nonBuiltinParams.OrderBy(p => p.Name).Select(p => p.Name).ToList();

            LastParamSettings.Load();
            if (!string.IsNullOrEmpty(LastParamSettings.LastWeightParam) &&
                _weightParamOptions.Contains(LastParamSettings.LastWeightParam))
            {
                _selectedWeightParam = LastParamSettings.LastWeightParam;
            }
            else
            {
                _selectedWeightParam = _weightParamOptions.Contains("Weigth")
                    ? "Weigth"
                    : _weightParamOptions.FirstOrDefault();
            }
            _weightParamFieldText.Text = _selectedWeightParam ?? "Select parameter…";
        }

        private void PopulateSheetParameters()
        {
            var paramElements = new FilteredElementCollector(_doc)
                .OfClass(typeof(ParameterElement))
                .Cast<ParameterElement>()
                .ToList();

            var nonBuiltinParams = new List<ParameterElement>();
            foreach (var pe in paramElements)
            {
                var def = pe.GetDefinition();
                if (def is InternalDefinition internalDef)
                {
                    if (internalDef.BuiltInParameter != BuiltInParameter.INVALID)
                        continue;
                }
                nonBuiltinParams.Add(pe);
            }

            _sheetParamOptions = nonBuiltinParams.OrderBy(p => p.Name).Select(p => p.Name).ToList();

            LastParamSettings.Load();
            if (!string.IsNullOrEmpty(LastParamSettings.LastSheetParam) &&
                _sheetParamOptions.Contains(LastParamSettings.LastSheetParam))
            {
                _selectedSheetParam = LastParamSettings.LastSheetParam;
            }
            else
            {
                _selectedSheetParam = _sheetParamOptions.Contains("Weigth")
                    ? "Weigth"
                    : _sheetParamOptions.FirstOrDefault();
            }
            _sheetParamFieldText.Text = _selectedSheetParam ?? "Select parameter…";
        }

        private void RefreshList()
        {
            var filter = _searchBox?.Text?.Trim() ?? string.Empty;
            _listBox.Items.Clear();
            foreach (var asm in _allAssemblies)
            {
                if (filter.Length == 0 || asm.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var item = new WpfListBoxItem
                    {
                        Tag = asm,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Content = BuildCheckboxRow(asm.Name, false)
                    };
                    _listBox.Items.Add(item);
                }
            }
        }

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

        private static void SyncRowVisual(WpfListBoxItem item)
        {
            if (item?.Tag is AssemblyInstance asm)
                item.Content = BuildCheckboxRow(asm.Name, item.IsSelected);
        }

        private void SetAllSelection(bool select)
        {
            foreach (WpfListBoxItem item in _listBox.Items)
            {
                item.IsSelected = select;
                SyncRowVisual(item);
            }
        }

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
            if (string.IsNullOrEmpty(_selectedWeightParam))
            {
                AvControls.ShowAlert("Missing Selection", "Please select a weight parameter.", AlertSeverity.Warning);
                return;
            }
            if (string.IsNullOrEmpty(_selectedSheetParam))
            {
                AvControls.ShowAlert("Missing Selection", "Please select a sheet parameter.", AlertSeverity.Warning);
                return;
            }
            SelectedWeightParamName = _selectedWeightParam;
            SelectedSheetParamName = _selectedSheetParam;
            LastParamSettings.Save(_selectedWeightParam, _selectedSheetParam);
            SelectedAssemblies = _listBox.SelectedItems
                .Cast<WpfListBoxItem>()
                .Select(item => item.Tag as AssemblyInstance)
                .Where(a => a != null)
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
    //  REPORT WINDOW — REFINED NATIVE DESIGN
    // ─────────────────────────────────────────────────────────────────────────

    internal class ReportWindow : Window
    {
        private readonly List<ReportRow> _rows;
        private readonly string _sourceCaption;

        public ReportWindow(
            List<ReportRow> rows,
            int processed, int skipped, int failed,
            int assembliesFound,
            double totalWeightSum,
            string weightParamName,
            string sheetParamName,
            string docTitle)
        {
            _rows = rows;
            _sourceCaption = $"Source · {docTitle}";

            Title = "Part List Format — Report";
            Width = 820;
            Height = 620;
            MinWidth = 680;
            MinHeight = 420;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = AvTokens.B(AvTokens.Surface);
            FontFamily = AvTokens.Ui;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var root = new WpfGrid();
            root.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });
            root.RowDefinitions.Add(new WpfRowDef { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new WpfRowDef { Height = GridLength.Auto });
            Content = root;

            // Title bar
            var titleBar = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(0, 0, 0, 1),
                Padding = new WpfThickness(20, 0, 20, 0),
                Height = 52
            };
            var titleRow = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
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
                Text = $"  — {processed} of {assembliesFound} assemblies processed · Total weight: {totalWeightSum:F3} kg",
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

            // Body
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

            // Summary row
            var summaryRow = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 22) };
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            summaryRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });

            var statsCol = new WpfStackPanel();
            statsCol.Children.Add(SectionLabel("Run summary"));
            var statGrid = new WpfGrid { Margin = new WpfThickness(0, 8, 0, 0) };
            for (int c = 0; c < 3; c++) statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
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
                (totalWeightSum.ToString("F3"), "Total Weight (kg)", "accent"),
            };

            for (int i = 0; i < stats.Length; i++)
            {
                int col = i % 3;
                var tile = AvControls.StatTile(stats[i].v, stats[i].k, stats[i].cls);
                tile.Margin = new WpfThickness(
                    col == 0 ? 0 : 6,
                    0,
                    col == 2 ? 0 : 6,
                    8);
                WpfGrid.SetRow(tile, i / 3);
                WpfGrid.SetColumn(tile, col);
                statGrid.Children.Add(tile);
            }
            statsCol.Children.Add(statGrid);
            WpfGrid.SetColumn(statsCol, 0);
            summaryRow.Children.Add(statsCol);

            // Configuration panel
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
                ("Mode", "Selected assemblies"),
                ("Scope", "Instance"),
                ("Weight parameter", weightParamName),
                ("Sheet output param", sheetParamName),
                ("Assemblies found", assembliesFound.ToString()),
                ("Document", docTitle),
            };
            for (int i = 0; i < cfgRows.Length; i++)
                cfgStack.Children.Add(KvRow(cfgRows[i].k, cfgRows[i].v, i == cfgRows.Length - 1));
            cfgPanel.Child = cfgStack;
            cfgCol.Children.Add(cfgPanel);
            WpfGrid.SetColumn(cfgCol, 2);
            summaryRow.Children.Add(cfgCol);
            body.Children.Add(summaryRow);

            // Results table header
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

            // Table
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

            // Footer
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

        private static WpfTextBlock SectionLabel(string text) => new WpfTextBlock
        {
            Text = text.ToUpperInvariant(),
            FontFamily = AvTokens.Ui,
            FontSize = 10.5,
            FontWeight = WpfFontWeights.Bold,
            Foreground = AvTokens.B(AvTokens.TextFaint)
        };

        private static System.Windows.UIElement KvRow(string k, string v, bool isLast)
        {
            var grid = new WpfGrid { Margin = new WpfThickness(0, 7, 0, 7) };
            if (!isLast) grid.Margin = new WpfThickness(0, 7, 0, 0);
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

            // ── Column Header Style ──
            var headerStyle = new System.Windows.Style(typeof(WpfDataGridColumnHeader));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.BackgroundProperty, AvTokens.B(AvTokens.Surface2)));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.ForegroundProperty, AvTokens.B(AvTokens.TextFaint)));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.FontFamilyProperty, AvTokens.Ui));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.FontSizeProperty, 10.5));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.FontWeightProperty, WpfFontWeights.Bold));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.PaddingProperty, new WpfThickness(12, 9, 12, 9)));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.BorderBrushProperty, AvTokens.B(AvTokens.BorderStrong)));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.BorderThicknessProperty, new WpfThickness(0, 0, 0, 1.5)));
            headerStyle.Setters.Add(new System.Windows.Setter(WpfDataGridColumnHeader.HorizontalContentAlignmentProperty, WpfHAlign.Left));
            grid.ColumnHeaderStyle = headerStyle;

            // ── Cell Style ──
            var cellStyle = new System.Windows.Style(typeof(WpfDataGridCell));
            cellStyle.Setters.Add(new System.Windows.Setter(WpfDataGridCell.BorderThicknessProperty, new WpfThickness(0)));
            cellStyle.Setters.Add(new System.Windows.Setter(WpfDataGridCell.PaddingProperty, new WpfThickness(12, 11, 12, 11)));
            cellStyle.Setters.Add(new System.Windows.Setter(WpfDataGridCell.BackgroundProperty, WpfBrushes.Transparent));
            cellStyle.Setters.Add(new System.Windows.Setter(WpfDataGridCell.ForegroundProperty, AvTokens.B(AvTokens.Text)));
            cellStyle.Setters.Add(new System.Windows.Setter(WpfDataGridCell.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            grid.CellStyle = cellStyle;

            // ── Column: Assembly Name ──
            var nameCol = new WpfDataGridTextColumn
            {
                Header = "ASSEMBLY NAME",
                Binding = new System.Windows.Data.Binding("AssemblyName"),
                Width = new System.Windows.Controls.DataGridLength(1.5, System.Windows.Controls.DataGridLengthUnitType.Star)
            };
            var nameStyle = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
            nameStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.FontWeightProperty, WpfFontWeights.SemiBold));
            nameCol.ElementStyle = nameStyle;
            grid.Columns.Add(nameCol);

            // ── Column: Status (with pill) ──
            // Auto-width so the pill is never clipped: a fixed pixel width can be
            // narrower than the longest status label (e.g. "Warning"), which
            // squeezes the pill and makes pill sizes look inconsistent row to row.
            var statusCol = new WpfDataGridTemplateColumn
            {
                Header = "STATUS",
                Width = System.Windows.Controls.DataGridLength.Auto,
                MinWidth = 100
            };
            statusCol.CellTemplate = AvControls.StatusPillTemplate();
            grid.Columns.Add(statusCol);


            // ── Column: Weight (numeric, mono) ──
            var weightCol = new WpfDataGridTextColumn
            {
                Header = "WEIGHT (KG)",
                Binding = new System.Windows.Data.Binding("TotalWeight") { StringFormat = "F3" },
                Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Star)
            };
            var weightStyle = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
            weightStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.FontFamilyProperty, AvTokens.Mono));
            weightStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.FontSizeProperty, 11.5));
            weightStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.ForegroundProperty, AvTokens.B(AvTokens.TextDim)));
            weightStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.TextAlignmentProperty, System.Windows.TextAlignment.Left));
            weightStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            weightCol.ElementStyle = weightStyle;
            grid.Columns.Add(weightCol);

            // ── Column: Notes ──
            var notesCol = new WpfDataGridTextColumn
            {
                Header = "NOTES",
                Binding = new System.Windows.Data.Binding("Note"),
                Width = new System.Windows.Controls.DataGridLength(1.4, System.Windows.Controls.DataGridLengthUnitType.Star)
            };
            var notesStyle = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
            notesStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.FontSizeProperty, 11.5));
            notesStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.ForegroundProperty, AvTokens.B(AvTokens.TextFaint)));
            notesStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.TextTrimmingProperty, System.Windows.TextTrimming.CharacterEllipsis));
            notesStyle.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            var warnTrigger = new System.Windows.DataTrigger
            {
                Binding = new System.Windows.Data.Binding("StatusKind"),
                Value = "warn"
            };
            warnTrigger.Setters.Add(new System.Windows.Setter(System.Windows.Controls.TextBlock.ForegroundProperty, AvTokens.B("#9A6B12")));
            notesStyle.Triggers.Add(warnTrigger);
            notesCol.ElementStyle = notesStyle;
            grid.Columns.Add(notesCol);

            return grid;
        }

        // BuildStatusPillTemplate is gone: it reimplemented the pill as a parsed
        // XAML string with hardcoded hex and labels, the same duplicate
        // Cmds_Button10.cs carried. Geometry matched AvControls.StatusPill
        // exactly, so AvControls.StatusPillTemplate is a like-for-like swap --
        // but the colours now follow AvTokens instead of needing manual re-sync.

        private void OnCopyToClipboard(WpfButton sourceBtn)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Assembly name\tStatus\tWeight (kg)\tNotes");
            foreach (var r in _rows)
                sb.AppendLine(string.Join("\t", r.AssemblyName, r.StatusKind, r.TotalWeight.ToString("F3"), r.Note));
            try
            {
                WpfClipboard.SetText(sb.ToString());
                if (sourceBtn != null)
                {
                    var original = sourceBtn.Content;
                    sourceBtn.Content = "✓  Copied!";
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
                    timer.Tick += (s, e) => { sourceBtn.Content = original; timer.Stop(); };
                    timer.Start();
                }
            }
            catch { }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  COMMAND 2: WEIGHT ONLY (Optimized)
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    public class Cmd_Button : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            var doc = uiDoc.Document;

            try
            {
                var allAssemblies = new FilteredElementCollector(doc)
                    .OfClass(typeof(AssemblyInstance))
                    .Cast<AssemblyInstance>()
                    .ToList();

                if (allAssemblies.Count == 0)
                {
                    AvControls.ShowAlert("Assembly Weight", "No AssemblyInstance elements found in this document.", AlertSeverity.Warning);
                    return Result.Cancelled;
                }

                var picker = new AssemblyPickerWindow(doc, allAssemblies);
                picker.ShowDialog();

                if (picker.SelectedAssemblies == null || picker.SelectedAssemblies.Count == 0)
                    return Result.Cancelled;

                string weightParamName = picker.SelectedWeightParamName;
                string sheetParamName = picker.SelectedSheetParamName;

                // ── Precompute assembly → sheets mapping (once) ──
                var assemblyToSheets = BuildAssemblyToSheetsMap(doc);

                var reportRows = new List<ReportRow>();
                int processed = 0, skipped = 0, failed = 0;
                double totalWeightSum = 0.0;

                foreach (var assembly in picker.SelectedAssemblies)
                {
                    var memberIds = assembly.GetMemberIds();
                    var members = memberIds.Select(id => doc.GetElement(id)).Where(el => el != null).ToList();

                    if (members.Count == 0)
                    {
                        reportRows.Add(new ReportRow { AssemblyName = assembly.Name, StatusKind = "skip", Note = "No members found", TotalWeight = 0 });
                        skipped++;
                        continue;
                    }

                    double assemblyTotalWeight = 0.0;
                    int missingWeightCount = 0;
                    var missingWeightNames = new List<string>(); // ← NEW: collect names of missing weight elements

                    foreach (var el in members)
                    {
                        double? weight = GetWeightValue(doc, el, weightParamName);
                        if (weight.HasValue)
                            assemblyTotalWeight += weight.Value;
                        else
                        {
                            missingWeightCount++;
                            missingWeightNames.Add(GetElementDisplayName(el)); // ← NEW: store the element name
                        }
                    }
                    assemblyTotalWeight = Math.Round(assemblyTotalWeight, 3);

                    using (var tx = new Transaction(doc, $"Write Weight – {assembly.Name}"))
                    {
                        try
                        {
                            tx.Start();

                            if (assemblyToSheets.TryGetValue(assembly.Id, out var sheets))
                            {
                                foreach (var sheet in sheets)
                                {
                                    Parameter sheetParam = sheet.LookupParameter(sheetParamName);
                                    if (sheetParam != null && !sheetParam.IsReadOnly && sheetParam.StorageType == StorageType.Double)
                                        sheetParam.Set(Math.Round(assemblyTotalWeight, 3));
                                }
                            }

                            tx.Commit();
                            processed++;
                        }
                        catch (Exception txEx)
                        {
                            if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                            reportRows.Add(new ReportRow
                            {
                                AssemblyName = assembly.Name,
                                StatusKind = "fail",
                                Note = txEx.Message,
                                TotalWeight = 0
                            });
                            failed++;
                            continue;
                        }
                    }

                    // ── Build note with family names ──
                    string note = "";
                    string status = "ok";
                    if (missingWeightCount > 0 && missingWeightCount < members.Count)
                    {
                        string namesStr = string.Join(", ", missingWeightNames.Take(5));
                        if (missingWeightNames.Count > 5) namesStr += ", ...";
                        note = $"Missing weights: {namesStr} ({missingWeightCount} of {members.Count})";
                        status = "warn";
                    }
                    else if (missingWeightCount == members.Count)
                    {
                        string namesStr = string.Join(", ", missingWeightNames.Take(5));
                        if (missingWeightNames.Count > 5) namesStr += ", ...";
                        note = $"No weight values found for: {namesStr}";
                        status = "warn";
                    }

                    reportRows.Add(new ReportRow
                    {
                        AssemblyName = assembly.Name,
                        StatusKind = status,
                        Note = note,
                        TotalWeight = assemblyTotalWeight
                    });
                    totalWeightSum += assemblyTotalWeight;
                }

                var report = new ReportWindow(
                    rows: reportRows,
                    processed: processed,
                    skipped: skipped,
                    failed: failed,
                    assembliesFound: picker.SelectedAssemblies.Count,
                    totalWeightSum: Math.Round(totalWeightSum, 3),
                    weightParamName: weightParamName,
                    sheetParamName: sheetParamName,
                    docTitle: doc.Title);
                report.ShowDialog();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AvControls.ShowAlert("Assembly Weight – Error", ex.Message, AlertSeverity.Error);
                return Result.Failed;
            }
        }

        // ── Helper to get user-friendly element display name ──
        private static string GetElementDisplayName(Element el)
        {
            if (el == null) return "Unknown";
            if (el is FamilyInstance fi && fi.Symbol != null)
                return fi.Symbol.Name;
            return el.Name;
        }

        // ── Helper to build map: assembly ID → list of sheets ──
        private Dictionary<ElementId, List<ViewSheet>> BuildAssemblyToSheetsMap(Document doc)
        {
            var map = new Dictionary<ElementId, List<ViewSheet>>();

            var allSheets = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Sheets)
                .WhereElementIsNotElementType()
                .Cast<ViewSheet>()
                .ToList();

            foreach (var sheet in allSheets)
            {
                var viewIds = sheet.GetAllPlacedViews();
                foreach (ElementId vId in viewIds)
                {
                    View view = doc.GetElement(vId) as View;
                    if (view == null) continue;

                    var assembliesInView = new FilteredElementCollector(doc, vId)
                        .OfClass(typeof(AssemblyInstance))
                        .Cast<AssemblyInstance>()
                        .ToList();

                    foreach (var asm in assembliesInView)
                    {
                        if (!map.TryGetValue(asm.Id, out var sheetList))
                        {
                            sheetList = new List<ViewSheet>();
                            map[asm.Id] = sheetList;
                        }
                        if (!sheetList.Contains(sheet))
                            sheetList.Add(sheet);
                    }
                }
            }

            return map;
        }

        private double? GetWeightValue(Document doc, Element elem, string paramName)
        {
            Parameter param = elem.LookupParameter(paramName);
            if (param == null || !param.HasValue)
            {
                ElementId typeId = elem.GetTypeId();
                if (typeId != ElementId.InvalidElementId)
                {
                    Element typeElem = doc.GetElement(typeId);
                    param = typeElem?.LookupParameter(paramName);
                }
            }
            if (param == null || !param.HasValue) return null;
            switch (param.StorageType)
            {
                case StorageType.Double: return param.AsDouble();
                case StorageType.Integer: return param.AsInteger();
                case StorageType.String: return double.TryParse(param.AsString(), out double d) ? d : (double?)null;
                default: return null;
            }
        }
    }
}
