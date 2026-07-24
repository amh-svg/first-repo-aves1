// System
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
// Autodesk
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AVES.Core;

// ---- aliases that resolve WPF ↔ Revit namespace collisions ----
// These four types exist in both Autodesk.Revit.DB and WPF namespaces.
// We alias the WPF versions; Revit versions keep their unqualified names.
using WGrid = System.Windows.Controls.Grid;
using WEllipse = System.Windows.Shapes.Ellipse;
using WColor = System.Windows.Media.Color;
using SIOPath = System.IO.Path;

// ============================================================
//  Cmds_Button9.cs — Export Families command + WPF Report
//  ------------------------------------------------------------
//  1. Select family instances in the Revit viewport.
//  2. Click the button → choose a destination folder.
//  3. Each unique parent family is exported as an .rfa file.
//  4. A themed WPF report window shows the results.
//
//  Design: AVES "Refined Native / AVES Red" (see AvesTheme.xaml)
//  Revit 2025 | .NET 8 | Visual Studio 2022
// ============================================================

namespace AVES.Cmds_Button9
{
    // ================================================================
    //  EXTERNAL COMMAND
    // ================================================================

    /// <summary>
    /// Reads the active Revit selection, resolves each unique parent Family,
    /// exports it as an .rfa file to a user-chosen folder, then displays a
    /// themed WPF report window with the results.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class Cmd_Button : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            // Collect the document
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            var doc = uiDoc.Document;

            // ===== START LOGIC =====

            // ------------------------------------------------------------------
            // Step 1 — Read the current Revit selection
            // ------------------------------------------------------------------
            var selectedIds = uiDoc.Selection.GetElementIds();

            if (selectedIds == null || selectedIds.Count == 0)
            {
                AvControls.ShowAlert(
                    "No Selection",
                    "Nothing is selected.\n\n" +
                    "Please select one or more family instances in the viewport first, " +
                    "then click this button.",
                    AlertSeverity.Warning
                );
                return Result.Cancelled;
            }

            // ------------------------------------------------------------------
            // Step 2 — Resolve unique parent Family objects
            // ------------------------------------------------------------------
            GetUniqueFamilies(selectedIds, doc,
                out Dictionary<long, Family> familyMap,
                out int skippedCount);

            if (familyMap.Count == 0)
            {
                AvControls.ShowAlert(
                    "No Exportable Families",
                    "None of the selected elements are exportable family instances.\n\n" +
                    "Make sure you are selecting placed families (furniture, doors, " +
                    "equipment, etc.) and not system family geometry like walls or floors.",
                    AlertSeverity.Warning
                );
                return Result.Cancelled;
            }

            List<Family> uniqueFamilies = familyMap.Values
                .OrderBy(f => f.Name)
                .ToList();

            // ------------------------------------------------------------------
            // Step 3 — Choose destination folder
            // Uses the Windows Shell COM folder picker — no WinForms reference needed.
            // ------------------------------------------------------------------
            string? destFolder = PickFolder("Select Destination Folder for .rfa Files");
            if (destFolder == null)
                return Result.Cancelled;

            // ------------------------------------------------------------------
            // Step 4 — Export and collect report data
            // ------------------------------------------------------------------
            var reportRows = new List<(string ItemNumber, string FamilyName,
                                       string Category, string TypeCount,
                                       string Status, string Notes)>();

            var saveOptions = new SaveAsOptions { OverwriteExistingFile = true };

            foreach (Family fam in uniqueFamilies)
            {
                string safeName = SanitizeFilename(fam.Name);
                string rfaPath = SIOPath.Combine(destFolder, safeName + ".rfa");
                Document? famDoc = null;

                string itemNumber = GetParamValue(fam, "Item Number", doc);
                string category = GetCategoryName(fam);
                string typeCount = GetTypeCount(fam).ToString();
                string status = string.Empty;
                string notes = string.Empty;

                try
                {
                    famDoc = doc.EditFamily(fam);

                    if (famDoc == null)
                        throw new InvalidOperationException(
                            "EditFamily() returned null — family may be locked or open elsewhere.");

                    famDoc.SaveAs(rfaPath, saveOptions);
                    status = "Exported";
                    notes = rfaPath;
                }
                catch (Exception ex)
                {
                    status = "Failed";
                    notes = ex.Message;
                    rfaPath = string.Empty;
                }
                finally
                {
                    if (famDoc != null)
                    {
                        try { famDoc.Close(false); }
                        catch { /* ignored */ }
                    }
                }

                reportRows.Add((itemNumber, fam.Name, category, typeCount, status, notes));
            }

            // ------------------------------------------------------------------
            // Step 5 — Show the WPF report window
            // ------------------------------------------------------------------
            var reportWindow = new FamilyExportReportWindow(reportRows, destFolder);
            reportWindow.ShowDialog();

            // ===== END LOGIC =====

            return Result.Succeeded;
        }

        // ======================================================================
        // Helper — Shell COM folder browser (no WinForms reference required)
        // ======================================================================
        /// <summary>
        /// Opens the Windows Shell "Browse For Folder" dialog via COM interop.
        /// Returns the selected path, or <c>null</c> if the user cancelled.
        /// </summary>
        private static string? PickFolder(string title)
        {
            var dialog = (IFileOpenDialog)new FileOpenDialog();
            try
            {
                dialog.SetTitle(title);
                dialog.SetOptions(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM);

                int hr = dialog.Show(IntPtr.Zero);
                if (hr != 0) return null;

                dialog.GetResult(out IShellItem item);
                item.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out string path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        // --- COM interop declarations for IFileOpenDialog (Vista+ folder picker) ---

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog { }

        [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(FOS fos);
            void GetOptions(out FOS pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int alignment);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [Flags]
        private enum FOS : uint
        {
            FOS_PICKFOLDERS = 0x00000020,
            FOS_FORCEFILESYSTEM = 0x00000040,
        }

        private enum SIGDN : uint
        {
            SIGDN_FILESYSPATH = 0x80058000,
        }

        // ======================================================================
        // Helper — Sanitize a string for use as a Windows file name
        // ======================================================================
        /// <summary>
        /// Replaces characters illegal in Windows file names with underscores
        /// and trims leading/trailing whitespace.
        /// </summary>
        private static string SanitizeFilename(string name)
        {
            char[] illegal = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            foreach (char ch in illegal)
                name = name.Replace(ch, '_');
            return name.Trim();
        }

        // ======================================================================
        // Helper — Retrieve unique parent Family objects from a selection
        // ======================================================================
        /// <summary>
        /// Iterates over the provided ElementIds, extracts the parent
        /// <see cref="Family"/> from each <see cref="FamilyInstance"/>, and
        /// deduplicates by Family ElementId. Non-FamilyInstance elements are
        /// silently counted as skipped.
        /// </summary>
        private static void GetUniqueFamilies(
            ICollection<ElementId> elementIds,
            Document doc,
            out Dictionary<long, Family> familyMap,
            out int skippedCount)
        {
            familyMap = new Dictionary<long, Family>();
            skippedCount = 0;

            foreach (ElementId eid in elementIds)
            {
                Element element = doc.GetElement(eid);

                if (element is not FamilyInstance fi)
                {
                    skippedCount++;
                    continue;
                }

                try
                {
                    Family? fam = fi.Symbol?.Family;
                    if (fam == null) { skippedCount++; continue; }

                    long key = fam.Id.Value;
                    if (!familyMap.ContainsKey(key))
                        familyMap[key] = fam;
                }
                catch
                {
                    skippedCount++;
                }
            }
        }

        // ======================================================================
        // Helper — Read a named parameter from a Family (then its first type)
        // ======================================================================
        /// <summary>
        /// Safely retrieves a parameter value from a <see cref="Family"/> by name.
        /// Falls back to the first <see cref="FamilySymbol"/> if not found on
        /// the Family itself. Returns an empty string when absent or unreadable.
        /// </summary>
        private static string GetParamValue(Family family, string paramName, Document doc)
        {
            Parameter p = family.LookupParameter(paramName);
            if (p != null && p.HasValue)
                return ReadParam(p);

            try
            {
                var symbolIds = family.GetFamilySymbolIds();
                if (symbolIds != null && symbolIds.Count > 0)
                {
                    Element sym = doc.GetElement(symbolIds.First());
                    Parameter? pSym = sym?.LookupParameter(paramName);
                    if (pSym != null && pSym.HasValue)
                        return ReadParam(pSym);
                }
            }
            catch { /* ignored */ }

            return string.Empty;
        }

        // ======================================================================
        // Helper — Convert a Parameter to a plain string
        // ======================================================================
        /// <summary>
        /// Reads the value of a <see cref="Parameter"/> and returns it as a
        /// plain <see cref="string"/>, handling all four storage types.
        /// </summary>
        private static string ReadParam(Parameter p)
        {
            try
            {
                return p.StorageType switch
                {
                    StorageType.String => p.AsString() ?? string.Empty,
                    StorageType.Integer => p.AsInteger().ToString(),
                    StorageType.Double => Math.Round(p.AsDouble(), 4).ToString(),
                    _ => p.AsValueString() ?? string.Empty,
                };
            }
            catch { return string.Empty; }
        }

        // ======================================================================
        // Helper — Return the category name of a Family
        // ======================================================================
        /// <summary>
        /// Returns the <see cref="Category"/> name of the given
        /// <see cref="Family"/>, or "Unknown" if unavailable.
        /// </summary>
        private static string GetCategoryName(Family family)
        {
            try { return family.FamilyCategory?.Name ?? "Unknown"; }
            catch { return "Unknown"; }
        }

        // ======================================================================
        // Helper — Return the number of FamilySymbols (types) in a Family
        // ======================================================================
        /// <summary>
        /// Returns the count of <see cref="FamilySymbol"/> elements that belong
        /// to the given <see cref="Family"/>.
        /// </summary>
        private static int GetTypeCount(Family family)
        {
            try { return family.GetFamilySymbolIds()?.Count ?? 0; }
            catch { return 0; }
        }
    }

    // ================================================================
    //  ROW MODEL
    // ================================================================

    /// <summary>Data model for one row in the Family Export report.</summary>
    public sealed class FamilyExportRow
    {
        public string ItemNumber { get; set; } = string.Empty;
        public string FamilyName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string TypeCount { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;  // "Exported" | "Failed"
        public string Notes { get; set; } = string.Empty;

        /// <summary>Maps Status to an AVES pill kind string.</summary>
        public string StatusKind => Status == "Exported" ? "ok" : "fail";
    }

    // ================================================================
    //  WPF REPORT WINDOW
    // ================================================================

    /// <summary>
    /// Self-contained WPF report window for the Family Export command.
    /// Follows the AVES "Refined Native / AVES Red" design language:
    /// branded header, stat tiles, status pills, and a themed DataGrid.
    /// </summary>
    public sealed class FamilyExportReportWindow : Window
    {
        // ---- state ----
        private readonly List<FamilyExportRow> _rows;
        private readonly string _destFolder;
        private readonly string _timestamp;
        private readonly int _exportedCount;
        private readonly int _failedCount;

        private DispatcherTimer? _copyTimer;
        private Button? _copyButton;

        // ----------------------------------------------------------------
        //  Constructor
        // ----------------------------------------------------------------
        /// <summary>
        /// Creates the report window from the export result tuple collection.
        /// </summary>
        public FamilyExportReportWindow(
            IEnumerable<(string ItemNumber, string FamilyName, string Category,
                         string TypeCount, string Status, string Notes)> rows,
            string destFolder)
        {
            _rows = rows.Select(r => new FamilyExportRow
            {
                ItemNumber = r.ItemNumber,
                FamilyName = r.FamilyName,
                Category = r.Category,
                TypeCount = r.TypeCount,
                Status = r.Status,
                Notes = r.Notes,
            }).ToList();

            _destFolder = destFolder;
            _timestamp = DateTime.Now.ToString("yyyy-MM-dd  HH:mm");
            _exportedCount = _rows.Count(r => r.Status == "Exported");
            _failedCount = _rows.Count(r => r.Status == "Failed");

            Title = "Family Export Report";
            Width = 980;
            Height = 680;
            MinWidth = 700;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = AvTokens.Br(AvTokens.Surface);
            FontFamily = AvTokens.Ui;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            Content = BuildLayout();
        }

        // ================================================================
        //  Layout — four stacked rows
        // ================================================================
        private UIElement BuildLayout()
        {
            var root = new WGrid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // A · header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // B · summary
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });   // C · table
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                        // D · footer

            var header = BuildHeaderBar(); WGrid.SetRow(header, 0); root.Children.Add(header);
            var summary = BuildSummaryStrip(); WGrid.SetRow(summary, 1); root.Children.Add(summary);
            var table = BuildTableZone(); WGrid.SetRow(table, 2); root.Children.Add(table);
            var footer = BuildFooter(); WGrid.SetRow(footer, 3); root.Children.Add(footer);

            return root;
        }

        // ----------------------------------------------------------------
        //  A · Header bar — logo + title + timestamp
        // ----------------------------------------------------------------
        private UIElement BuildHeaderBar()
        {
            var bar = new Border
            {
                Background = AvTokens.Br(AvTokens.Surface),
                BorderBrush = AvTokens.Br(AvTokens.Border),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20, 0, 20, 0),
                Height = 52,
            };

            var grid = new WGrid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // logo
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // title
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // timestamp

            // Logo — falls back to brand tile if image not found
            var logo = BuildLogoImage();
            logo.VerticalAlignment = VerticalAlignment.Center;
            logo.Margin = new Thickness(0, 0, 16, 0);
            WGrid.SetColumn(logo, 0);
            grid.Children.Add(logo);

            // Title
            var titleStack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Center,
            };
            titleStack.Children.Add(new TextBlock
            {
                Text = "Family Export Report",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = AvTokens.Br(AvTokens.Text),
            });
            WGrid.SetColumn(titleStack, 1);
            grid.Children.Add(titleStack);

            // Timestamp
            var ts = new TextBlock
            {
                Text = _timestamp,
                FontFamily = AvTokens.Mono,
                FontSize = 11,
                Foreground = AvTokens.Br(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Center,
            };
            WGrid.SetColumn(ts, 2);
            grid.Children.Add(ts);

            bar.Child = grid;
            return bar;
        }

        /// <summary>
        /// Loads the Øglænd System / Hilti logo from a Base64 string embedded
        /// directly in the source. Works identically in Debug and Release —
        /// no file path, no Build Action, no setup required.
        /// </summary>
        private FrameworkElement BuildLogoImage()
        {
            const string b64 = AvTokens.LogoBase64;

            try
            {
                byte[] bytes = System.Convert.FromBase64String(b64);
                using var stream = new System.IO.MemoryStream(bytes);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = stream;
                bmp.DecodePixelHeight = 28;
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // reads stream fully before it closes
                bmp.EndInit();
                bmp.Freeze();                                        // safe across threads

                return new Image
                {
                    Source = bmp,
                    Height = 28,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
            catch
            {
                // Fallback to AVES Red brand tile if image fails to decode
                return BuildBrandTile(28);
            }
        }

        /// <summary>Accent rounded square + white dot — AvesUi.BrandTile pattern.</summary>
        private static Border BuildBrandTile(double size = 20)
        {
            var dot = new WEllipse
            {
                Width = size * 0.36,
                Height = size * 0.36,
                Fill = new SolidColorBrush(WColor.FromArgb(235, 255, 255, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return new Border
            {
                Width = size,
                Height = size,
                Background = AvTokens.Br(AvTokens.Accent),
                CornerRadius = new CornerRadius(6),
                Child = dot,
            };
        }

        // ----------------------------------------------------------------
        //  B · Summary strip — stat tiles + config panel
        // ----------------------------------------------------------------
        private UIElement BuildSummaryStrip()
        {
            var strip = new Border
            {
                Background = AvTokens.Br(AvTokens.Surface2),
                BorderBrush = AvTokens.Br(AvTokens.Border),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(20, 16, 20, 16),
            };

            var cols = new WGrid();
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });

            // Stat tiles
            var tiles = new UniformGrid { Rows = 1, Columns = 3 };
            tiles.Children.Add(BuildStatTile(_exportedCount.ToString(), "FAMILIES EXPORTED", "ok"));
            tiles.Children.Add(BuildStatTile(_failedCount.ToString(), "FAILED", _failedCount > 0 ? "fail" : ""));
            tiles.Children.Add(BuildStatTile(_rows.Count.ToString(), "TOTAL SELECTED", ""));

            for (int i = 0; i < tiles.Children.Count - 1; i++)
                if (tiles.Children[i] is FrameworkElement fe)
                    fe.Margin = new Thickness(0, 0, 10, 0);

            WGrid.SetColumn(tiles, 0);
            cols.Children.Add(tiles);

            // Config panel
            var configStack = new StackPanel();
            configStack.Children.Add(MakeSectionLabel("DESTINATION"));
            configStack.Children.Add(BuildConfigRow("Folder", TruncatePath(_destFolder, 32)));
            configStack.Children.Add(BuildConfigRow("Exported", $"{_exportedCount} of {_rows.Count}"));
            configStack.Children.Add(BuildConfigRow("Timestamp", _timestamp));

            var config = new Border
            {
                Background = AvTokens.Br(AvTokens.Surface),
                BorderBrush = AvTokens.Br(AvTokens.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(14, 10, 14, 10),
                Child = configStack,
            };
            WGrid.SetColumn(config, 2);
            cols.Children.Add(config);

            strip.Child = cols;
            return strip;
        }

        // Routed to AvControls.StatTile. Visual change: no "warn" case was
        // ever unreachable here (this file's switch never had one; Core's
        // does, and now covers it too), label FontSize 10.5 -> 11, value
        // margin-bottom 3 -> 4, adds TextTrimming/ToolTip. No spacing impact:
        // this file already adds inter-tile spacing explicitly at the call
        // site (see the loop right after the 3 BuildStatTile calls below),
        // not via the tile's own margin.
        private static Border BuildStatTile(string value, string label, string accentKind) =>
            AvControls.StatTile(value, label, accentKind);

        private static UIElement BuildConfigRow(string key, string val)
        {
            var row = new WGrid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var k = new TextBlock
            {
                Text = key,
                FontSize = 11.5,
                Foreground = AvTokens.Br(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Top,
            };
            var v = new TextBlock
            {
                Text = val,
                FontFamily = AvTokens.Mono,
                FontSize = 11,
                Foreground = AvTokens.Br(AvTokens.Text),
                TextWrapping = TextWrapping.Wrap,
            };
            WGrid.SetColumn(k, 0); row.Children.Add(k);
            WGrid.SetColumn(v, 1); row.Children.Add(v);
            return row;
        }

        // ----------------------------------------------------------------
        //  C · Table zone — section label + DataGrid
        // ----------------------------------------------------------------
        private UIElement BuildTableZone()
        {
            var zone = new WGrid { Margin = new Thickness(20, 16, 20, 0) };
            zone.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            zone.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Section label row
            var labelRow = new WGrid { Margin = new Thickness(0, 0, 0, 10) };
            labelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            labelRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sectionLabel = MakeSectionLabel("RESULTS PER FAMILY");
            WGrid.SetColumn(sectionLabel, 0);
            labelRow.Children.Add(sectionLabel);

            var countLabel = new TextBlock
            {
                Text = $"showing {_rows.Count} of {_rows.Count}",
                FontSize = 11,
                Foreground = AvTokens.Br(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Center,
            };
            WGrid.SetColumn(countLabel, 1);
            labelRow.Children.Add(countLabel);

            WGrid.SetRow(labelRow, 0);
            zone.Children.Add(labelRow);

            // DataGrid
            var dgBorder = BuildDataGrid();
            WGrid.SetRow(dgBorder, 1);
            zone.Children.Add(dgBorder);

            return zone;
        }

        private Border BuildDataGrid()
        {
            var dg = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = AvTokens.Br(AvTokens.Border),
                BorderBrush = AvTokens.Br(AvTokens.Border),
                BorderThickness = new Thickness(1),
                Background = AvTokens.Br(AvTokens.Surface),
                RowBackground = AvTokens.Br(AvTokens.Surface),
                AlternatingRowBackground = AvTokens.Br(AvTokens.Zebra),
                AlternationCount = 2,
                RowHeight = 42,
                FontFamily = AvTokens.Ui,
                CanUserResizeColumns = true,
                CanUserSortColumns = true,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                ColumnHeaderStyle = BuildColumnHeaderStyle(),
                CellStyle = BuildCellStyle(),
                RowStyle = BuildRowStyle(),
                ItemsSource = _rows,
            };

            dg.Columns.Add(TextCol("ITEM NO.", nameof(FamilyExportRow.ItemNumber), 80, false));
            dg.Columns.Add(TextCol("FAMILY NAME", nameof(FamilyExportRow.FamilyName), 220, true, bold: true));
            dg.Columns.Add(TextCol("CATEGORY", nameof(FamilyExportRow.Category), 140, false));
            dg.Columns.Add(NumCol("TYPES", nameof(FamilyExportRow.TypeCount), 60));
            dg.Columns.Add(PillCol());
            dg.Columns.Add(NoteCol("FILE PATH / ERROR", nameof(FamilyExportRow.Notes), 300));

            return new Border
            {
                BorderBrush = AvTokens.Br(AvTokens.Border),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                ClipToBounds = true,
                Child = dg,
            };
        }

        // Plain text column
        private static DataGridTextColumn TextCol(
            string header, string binding, double width,
            bool starWidth = false, bool bold = false)
        {
            var col = new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(binding),
                Width = starWidth
                    ? new DataGridLength(1, DataGridLengthUnitType.Star)
                    : new DataGridLength(width),
            };
            if (bold)
                col.ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters = { new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold) }
                };
            return col;
        }

        // Numeric column — mono, dim, centred
        private static DataGridTextColumn NumCol(string header, string binding, double width)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(binding),
                Width = new DataGridLength(width),
                ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters =
                    {
                        new Setter(TextBlock.FontFamilyProperty,          AvTokens.Mono),
                        new Setter(TextBlock.FontSizeProperty,            11.5),
                        new Setter(TextBlock.ForegroundProperty,          AvTokens.Br(AvTokens.TextDim)),
                        new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center),
                    }
                },
            };
        }

        // Status pill column
        private DataGridTemplateColumn PillCol()
        {
            return new DataGridTemplateColumn
            {
                Header = "STATUS",
                Width = new DataGridLength(110),
                CellTemplate = BuildPillTemplate(),
            };
        }

        private DataTemplate BuildPillTemplate()
        {
            var template = new DataTemplate(typeof(FamilyExportRow));
            var cpFactory = new FrameworkElementFactory(typeof(ContentControl));
            cpFactory.SetBinding(
                ContentControl.ContentProperty,
                new System.Windows.Data.Binding(nameof(FamilyExportRow.StatusKind))
                {
                    Converter = new StatusPillConverter(),
                });
            cpFactory.SetValue(ContentControl.VerticalAlignmentProperty, VerticalAlignment.Center);
            template.VisualTree = cpFactory;
            return template;
        }

        // Notes column — faint, wrapping
        private static DataGridTextColumn NoteCol(string header, string binding, double width)
        {
            return new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(binding),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters =
                    {
                        new Setter(TextBlock.FontSizeProperty,    11.5),
                        new Setter(TextBlock.ForegroundProperty,  AvTokens.Br(AvTokens.TextFaint)),
                        new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap),
                    }
                },
            };
        }

        // DataGrid sub-styles
        private static Style BuildColumnHeaderStyle()
        {
            var s = new Style(typeof(DataGridColumnHeader));
            s.Setters.Add(new Setter(DataGridColumnHeader.FontFamilyProperty, AvTokens.Ui));
            s.Setters.Add(new Setter(DataGridColumnHeader.FontSizeProperty, 10.5));
            s.Setters.Add(new Setter(DataGridColumnHeader.FontWeightProperty, FontWeights.Bold));
            s.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty, AvTokens.Br(AvTokens.TextFaint)));
            s.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty, AvTokens.Br(AvTokens.Surface2)));
            s.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(12, 9, 12, 9)));
            s.Setters.Add(new Setter(DataGridColumnHeader.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            s.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, AvTokens.Br(AvTokens.BorderStrong)));
            s.Setters.Add(new Setter(DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 0, 1.5)));
            return s;
        }

        private static Style BuildCellStyle()
        {
            var ct = new ControlTemplate(typeof(DataGridCell));
            var bdFactory = new FrameworkElementFactory(typeof(Border));
            bdFactory.SetValue(Border.PaddingProperty, new Thickness(12, 0, 12, 0));
            bdFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bdFactory.AppendChild(cp);
            ct.VisualTree = bdFactory;

            var s = new Style(typeof(DataGridCell));
            s.Setters.Add(new Setter(DataGridCell.FontFamilyProperty, AvTokens.Ui));
            s.Setters.Add(new Setter(DataGridCell.FontSizeProperty, 12.5));
            s.Setters.Add(new Setter(DataGridCell.ForegroundProperty, AvTokens.Br(AvTokens.Text)));
            s.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
            s.Setters.Add(new Setter(DataGridCell.TemplateProperty, ct));
            return s;
        }

        private static Style BuildRowStyle()
        {
            var s = new Style(typeof(DataGridRow));
            s.Setters.Add(new Setter(DataGridRow.BackgroundProperty, AvTokens.Br(AvTokens.Surface)));
            var trigger = new Trigger { Property = DataGridRow.AlternationIndexProperty, Value = 1 };
            trigger.Setters.Add(new Setter(DataGridRow.BackgroundProperty, AvTokens.Br(AvTokens.Zebra)));
            s.Triggers.Add(trigger);
            return s;
        }

        // ----------------------------------------------------------------
        //  D · Footer — destination caption + Copy + Close
        // ----------------------------------------------------------------
        private UIElement BuildFooter()
        {
            var footer = new Border
            {
                Background = AvTokens.Br(AvTokens.Surface2),
                BorderBrush = AvTokens.Br(AvTokens.Border),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 12, 20, 12),
            };

            var row = new WGrid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var caption = new TextBlock
            {
                Text = $"Destination · {_destFolder}",
                FontSize = 11,
                Foreground = AvTokens.Br(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            WGrid.SetColumn(caption, 0);
            row.Children.Add(caption);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            _copyButton = AvControls.SecondaryBtn("⧉  Copy to clipboard", OnCopy);
            _copyButton.Margin = new Thickness(0, 0, 10, 0);   // gap between the two buttons
            btnRow.Children.Add(_copyButton);
            btnRow.Children.Add(AvControls.PrimaryBtn("Close", () => Close()));
            WGrid.SetColumn(btnRow, 1);
            row.Children.Add(btnRow);

            footer.Child = row;
            return footer;
        }

        // ----------------------------------------------------------------
        //  Copy to clipboard — TSV + 2-second "Copied!" revert
        // ----------------------------------------------------------------
        private void OnCopy()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Item Number\tFamily Name\tCategory\tNo. of Types\tExport Status\tFile Path / Error");
            foreach (var r in _rows)
                sb.AppendLine($"{r.ItemNumber}\t{r.FamilyName}\t{r.Category}\t{r.TypeCount}\t{r.Status}\t{r.Notes}");

            try { Clipboard.SetText(sb.ToString()); }
            catch { /* clipboard may be locked */ }

            if (_copyButton == null) return;

            _copyButton.Content = "✓  Copied!";
            _copyTimer?.Stop();
            _copyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _copyTimer.Tick += (_, _) =>
            {
                _copyButton.Content = "⧉  Copy to clipboard";
                _copyTimer!.Stop();
            };
            _copyTimer.Start();
        }

        // ----------------------------------------------------------------
        //  Small helpers
        // ----------------------------------------------------------------
        private static TextBlock MakeSectionLabel(string text) => new TextBlock
        {
            Text = text,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = AvTokens.Br(AvTokens.TextFaint),
            Margin = new Thickness(0, 0, 0, 10),
        };

        private static string TruncatePath(string path, int maxChars) =>
            path.Length <= maxChars ? path : "…" + path[^maxChars..];
    }

    // ================================================================
    //  STATUS PILL CONVERTER
    // ================================================================

    /// <summary>
    /// Converts a status-kind string ("ok" | "fail" | "skip" | "warn")
    /// into a styled pill <see cref="Border"/> for use in a DataTemplate.
    /// The IValueConverter wrapper stays -- structurally required, since
    /// this is consumed via a Binding.Converter inside a code-behind
    /// DataTemplate (BuildPillTemplate), which a static factory method
    /// can't be bound to. Its body now delegates to AvControls.StatusPill
    /// for geometry, keeping only the "ok" case's existing "Exported"
    /// wording via labelOverride (reconciliation decision: unify styling,
    /// keep existing per-file wording; architecture stays, only the visual
    /// construction routes to Core).
    /// </summary>
    internal sealed class StatusPillConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter,
                              System.Globalization.CultureInfo culture)
        {
            string kind = value as string ?? "info";
            return AvControls.StatusPill(kind, kind == "ok" ? "Exported" : null);
        }

        public object ConvertBack(object value, Type targetType, object parameter,
                                  System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}