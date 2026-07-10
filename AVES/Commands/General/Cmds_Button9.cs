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
                TaskDialog.Show(
                    "No Selection",
                    "Nothing is selected.\n\n" +
                    "Please select one or more family instances in the viewport first, " +
                    "then click this button."
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
                TaskDialog.Show(
                    "No Exportable Families",
                    "None of the selected elements are exportable family instances.\n\n" +
                    "Make sure you are selecting placed families (furniture, doors, " +
                    "equipment, etc.) and not system family geometry like walls or floors."
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
        // ---- design tokens (mirror AvesTheme.xaml / AvesUi.cs) ----
        private static readonly FontFamily UiFont = new FontFamily("Segoe UI");
        private static readonly FontFamily MonoFont = new FontFamily("Consolas");

        private const string CAccent = "#E64A2E";
        private const string CAccentDeep = "#C2402A";
        private const string CAccentTint = "#FBE9E4";
        private const string CSurface = "#FFFFFF";
        private const string CSurface2 = "#FAFAFB";
        private const string CZebra = "#F7F8FA";
        private const string CBorder = "#E5E6EA";
        private const string CBorderStrong = "#D4D6DC";
        private const string CText = "#1F2329";
        private const string CTextDim = "#5A6069";
        private const string CTextFaint = "#8B9099";
        private const string COkFg = "#2C7A52";
        private const string CFailFg = "#C0381F";

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
            Background = Br(CSurface);
            FontFamily = UiFont;
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
                Background = Br(CSurface),
                BorderBrush = Br(CBorder),
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

            // Title + subtitle
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
                Foreground = Br(CText),
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = "Selected families exported as .rfa files",
                FontSize = 11.5,
                Foreground = Br(CTextFaint),
                Margin = new Thickness(0, 1, 0, 0),
            });
            WGrid.SetColumn(titleStack, 1);
            grid.Children.Add(titleStack);

            // Timestamp
            var ts = new TextBlock
            {
                Text = _timestamp,
                FontFamily = MonoFont,
                FontSize = 11,
                Foreground = Br(CTextFaint),
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
            const string b64 =
                "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";

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
                Background = Br(CAccent),
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
                Background = Br(CSurface2),
                BorderBrush = Br(CBorder),
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
                Background = Br(CSurface),
                BorderBrush = Br(CBorder),
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

        private static Border BuildStatTile(string value, string label, string accentKind)
        {
            string valColor = accentKind switch
            {
                "ok" => COkFg,
                "fail" => CFailFg,
                "accent" => CAccent,
                _ => CText,
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontFamily = MonoFont,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Br(valColor),
                Margin = new Thickness(0, 0, 0, 3),
            });
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Br(CTextFaint),
            });
            return new Border
            {
                Background = Br(CSurface),
                BorderBrush = Br(CBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(14, 12, 14, 12),
                Child = stack,
            };
        }

        private static UIElement BuildConfigRow(string key, string val)
        {
            var row = new WGrid { Margin = new Thickness(0, 0, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var k = new TextBlock
            {
                Text = key,
                FontSize = 11.5,
                Foreground = Br(CTextFaint),
                VerticalAlignment = VerticalAlignment.Top,
            };
            var v = new TextBlock
            {
                Text = val,
                FontFamily = MonoFont,
                FontSize = 11,
                Foreground = Br(CText),
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
                Foreground = Br(CTextFaint),
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
                HorizontalGridLinesBrush = Br(CBorder),
                BorderBrush = Br(CBorder),
                BorderThickness = new Thickness(1),
                Background = Br(CSurface),
                RowBackground = Br(CSurface),
                AlternatingRowBackground = Br(CZebra),
                AlternationCount = 2,
                RowHeight = 42,
                FontFamily = UiFont,
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
                BorderBrush = Br(CBorder),
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
                        new Setter(TextBlock.FontFamilyProperty,          MonoFont),
                        new Setter(TextBlock.FontSizeProperty,            11.5),
                        new Setter(TextBlock.ForegroundProperty,          Br(CTextDim)),
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
                        new Setter(TextBlock.ForegroundProperty,  Br(CTextFaint)),
                        new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap),
                    }
                },
            };
        }

        // DataGrid sub-styles
        private static Style BuildColumnHeaderStyle()
        {
            var s = new Style(typeof(DataGridColumnHeader));
            s.Setters.Add(new Setter(DataGridColumnHeader.FontFamilyProperty, UiFont));
            s.Setters.Add(new Setter(DataGridColumnHeader.FontSizeProperty, 10.5));
            s.Setters.Add(new Setter(DataGridColumnHeader.FontWeightProperty, FontWeights.Bold));
            s.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty, Br(CTextFaint)));
            s.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty, Br(CSurface2)));
            s.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(12, 9, 12, 9)));
            s.Setters.Add(new Setter(DataGridColumnHeader.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            s.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, Br(CBorderStrong)));
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
            s.Setters.Add(new Setter(DataGridCell.FontFamilyProperty, UiFont));
            s.Setters.Add(new Setter(DataGridCell.FontSizeProperty, 12.5));
            s.Setters.Add(new Setter(DataGridCell.ForegroundProperty, Br(CText)));
            s.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
            s.Setters.Add(new Setter(DataGridCell.TemplateProperty, ct));
            return s;
        }

        private static Style BuildRowStyle()
        {
            var s = new Style(typeof(DataGridRow));
            s.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(CSurface)));
            var trigger = new Trigger { Property = DataGridRow.AlternationIndexProperty, Value = 1 };
            trigger.Setters.Add(new Setter(DataGridRow.BackgroundProperty, Br(CZebra)));
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
                Background = Br(CSurface2),
                BorderBrush = Br(CBorder),
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
                Foreground = Br(CTextFaint),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            WGrid.SetColumn(caption, 0);
            row.Children.Add(caption);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal };
            _copyButton = BuildButton("⧉  Copy to clipboard", ButtonKind.Secondary, OnCopy);
            _copyButton.Margin = new Thickness(0, 0, 10, 0);   // gap between the two buttons
            btnRow.Children.Add(_copyButton);
            btnRow.Children.Add(BuildButton("Close", ButtonKind.Primary, (_, _) => Close()));
            WGrid.SetColumn(btnRow, 1);
            row.Children.Add(btnRow);

            footer.Child = row;
            return footer;
        }

        // ----------------------------------------------------------------
        //  Button factory
        // ----------------------------------------------------------------
        private enum ButtonKind { Primary, Secondary, Ghost }

        private static Button BuildButton(string label, ButtonKind kind, RoutedEventHandler handler)
        {
            (string bg, string fg, string border) = kind switch
            {
                ButtonKind.Primary => (CAccent, "#FFFFFF", CAccent),
                ButtonKind.Secondary => (CSurface, CText, CBorderStrong),
                _ => ("Transparent", CTextDim, "Transparent"),
            };
            string hoverBg = kind == ButtonKind.Primary ? CAccentDeep : CSurface2;

            var btn = new Button
            {
                Content = label,
                FontFamily = UiFont,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Br(fg),
                Background = Br(bg),
                Cursor = System.Windows.Input.Cursors.Hand,
            };

            var ct = new ControlTemplate(typeof(Button));
            var bdF = new FrameworkElementFactory(typeof(Border));
            bdF.Name = "bd";
            bdF.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            bdF.SetValue(Border.BackgroundProperty, Br(bg));
            bdF.SetValue(Border.BorderBrushProperty, Br(border));
            bdF.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            var cpF = new FrameworkElementFactory(typeof(ContentPresenter));
            cpF.SetValue(ContentPresenter.MarginProperty, new Thickness(16, 9, 16, 9));
            cpF.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cpF.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            bdF.AppendChild(cpF);
            ct.VisualTree = bdF;

            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Br(hoverBg), "bd"));
            ct.Triggers.Add(hover);

            btn.Template = ct;
            btn.Click += handler;
            return btn;
        }

        // ----------------------------------------------------------------
        //  Copy to clipboard — TSV + 2-second "Copied!" revert
        // ----------------------------------------------------------------
        private void OnCopy(object sender, RoutedEventArgs e)
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
            Foreground = Br(CTextFaint),
            Margin = new Thickness(0, 0, 0, 10),
        };

        private static SolidColorBrush Br(string hex) =>
            new SolidColorBrush((WColor)ColorConverter.ConvertFromString(hex));

        private static string TruncatePath(string path, int maxChars) =>
            path.Length <= maxChars ? path : "…" + path[^maxChars..];
    }

    // ================================================================
    //  STATUS PILL CONVERTER
    // ================================================================

    /// <summary>
    /// Converts a status-kind string ("ok" | "fail" | "skip" | "warn")
    /// into a styled pill <see cref="Border"/> for use in a DataTemplate.
    /// Mirrors the AvesUi.StatusPill factory from the design handoff.
    /// </summary>
    internal sealed class StatusPillConverter : System.Windows.Data.IValueConverter
    {
        private static readonly FontFamily UiFont = new FontFamily("Segoe UI");

        public object Convert(object value, Type targetType, object parameter,
                              System.Globalization.CultureInfo culture)
        {
            string kind = value as string ?? "info";

            (string bg, string fg, string label) = kind switch
            {
                "ok" => ("#E4F3EA", "#2C7A52", "Exported"),
                "fail" => ("#FBE3DE", "#C0381F", "Failed"),
                "skip" => ("#EDEFF2", "#5B6573", "Skipped"),
                "warn" => ("#FAF0DA", "#9A6B12", "Warning"),
                _ => ("#E7EFFB", "#2E66A8", "Info"),
            };

            static SolidColorBrush Br(string h) =>
                new SolidColorBrush((WColor)ColorConverter.ConvertFromString(h));

            var dot = new WEllipse
            {
                Width = 7,
                Height = 7,
                Fill = Br(fg),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            var txt = new TextBlock
            {
                Text = label,
                Foreground = Br(fg),
                FontFamily = UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(txt);

            return new Border
            {
                Background = Br(bg),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = row,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter,
                                  System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}