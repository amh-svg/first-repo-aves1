// .NET
using System;
using System.Collections.Generic;
using System.Text;
// Autodesk
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

// Associate with pushbutton commands
namespace AVES.Cmds_Button6
{
    /// <summary>
    /// Renames Revit Families and Types based on the "Item Number"
    /// and "PMO_Description" parameters found on selected elements.
    /// Target format: "{Item Number} _ {PMO_Description}"
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class Cmd_Button : IExternalCommand
    {
        // ── Configuration ──────────────────────────────────────────────────────
        private const string ParamItemNumber = "Item Number";
        private const string ParamShortHeader = "PMO_Description";

        /// <summary>
        /// Item Numbers in this set will be skipped entirely.
        /// </summary>
        private static readonly HashSet<string> ExclusionList =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // e.g. "A-101", "TEMP"
            };

        // ── Result row used for the table report ───────────────────────────────
        private class RenameRow
        {
            public string? Status { get; set; }   // "Renamed" | "Skipped" | "Already Correct" | "Error"
            public string? Kind { get; set; }   // "Family" | "Type"
            public string? OldName { get; set; }
            public string? NewName { get; set; }
            public string? Detail { get; set; }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            var doc = uiDoc.Document;

            // ── Result buckets ─────────────────────────────────────────────────
            List<RenameRow> rows = [];
            List<string> successfulRenames = [];
            List<string> skippedNoParams = [];
            List<string> skippedExcluded = [];
            List<string> skippedDuplicates = [];
            List<string> skippedNonFamily = [];
            List<string> alreadyCorrect = [];
            List<string> errors = [];

            // ── Selection ──────────────────────────────────────────────────────
            var selectionIds = uiDoc.Selection.GetElementIds();
            if (selectionIds == null || selectionIds.Count == 0)
            {
                ShowWpfAlert(
                    "Family Renamer",
                    "No elements selected.\n\nPlease select family instances or types and re-run.");
                return Result.Cancelled;
            }

            List<Element> selectedElements = [];
            foreach (var eid in selectionIds)
            {
                var el = doc.GetElement(eid);
                if (el != null) selectedElements.Add(el);
            }

            // ── Rename queues ──────────────────────────────────────────────────
            var symbolRenames = new Dictionary<string, (FamilySymbol Symbol, Family Family, string NewName)>();
            var familyRenames = new Dictionary<string, (Family Family, string NewName)>();

            var targetFamilyNames = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var targetSymbolNames = new Dictionary<(string FamUid, string NewName), List<string>>();

            // ── Build queues ───────────────────────────────────────────────────
            foreach (var element in selectedElements)
            {
                FamilySymbol? symbol = null;
                if (element is FamilyInstance fi) symbol = fi.Symbol;
                else if (element is FamilySymbol fs) symbol = fs;

                if (symbol == null)
                {
                    var detail = $"Not a family element ({element.GetType().Name})";
                    skippedNonFamily.Add(detail);
                    rows.Add(new RenameRow
                    {
                        Status = "Skipped",
                        Kind = "—",
                        OldName = $"ID {element.Id.Value}",
                        NewName = "—",
                        Detail = detail
                    });
                    continue;
                }

                Family? family = null;
                try { family = symbol.Family; } catch { }

                if (family == null)
                {
                    var detail = "Could not resolve parent Family";
                    skippedNonFamily.Add(detail);
                    rows.Add(new RenameRow
                    {
                        Status = "Skipped",
                        Kind = "—",
                        OldName = $"Symbol ID {symbol.Id.Value}",
                        NewName = "—",
                        Detail = detail
                    });
                    continue;
                }

                string symUid = symbol.UniqueId;
                string famUid = family.UniqueId;
                string famName = family.Name;
                string symName = symbol.Name;

                if (symbolRenames.ContainsKey(symUid)) continue;

                string? itemNumber = GetParamValue(symbol, ParamItemNumber) ?? GetParamValue(element, ParamItemNumber);
                string? shortHeader = GetParamValue(symbol, ParamShortHeader) ?? GetParamValue(element, ParamShortHeader);

                if (string.IsNullOrEmpty(itemNumber) || string.IsNullOrEmpty(shortHeader))
                {
                    var missing = new List<string>();
                    if (string.IsNullOrEmpty(itemNumber)) missing.Add(ParamItemNumber);
                    if (string.IsNullOrEmpty(shortHeader)) missing.Add(ParamShortHeader);
                    var detail = $"Missing parameter(s): {string.Join(", ", missing)}";
                    skippedNoParams.Add(detail);
                    rows.Add(new RenameRow
                    {
                        Status = "Skipped",
                        Kind = "Type",
                        OldName = $"{famName} :: {symName}",
                        NewName = "—",
                        Detail = detail
                    });
                    continue;
                }

                if (ExclusionList.Contains(itemNumber))
                {
                    var detail = $"Item Number '{itemNumber}' is excluded";
                    skippedExcluded.Add(detail);
                    rows.Add(new RenameRow
                    {
                        Status = "Skipped",
                        Kind = "Type",
                        OldName = $"{famName} :: {symName}",
                        NewName = "—",
                        Detail = detail
                    });
                    continue;
                }

                string newName = $"{itemNumber} _ {shortHeader}";

                symbolRenames[symUid] = (symbol, family, newName);
                var symKey = (famUid, newName);
                if (!targetSymbolNames.TryGetValue(symKey, out var symList))
                { symList = []; targetSymbolNames[symKey] = symList; }
                symList.Add(symUid);

                if (!familyRenames.TryGetValue(famUid, out var existingEntry))
                {
                    familyRenames[famUid] = (family, newName);
                    if (!targetFamilyNames.TryGetValue(newName, out var famList))
                    { famList = []; targetFamilyNames[newName] = famList; }
                    famList.Add(famUid);
                }
                else
                {
                    string existingName = existingEntry.NewName;
                    if (!string.Equals(existingName, newName, StringComparison.OrdinalIgnoreCase))
                    {
                        var detail = $"Types resolve to conflicting names ('{existingName}' vs '{newName}') — Family rename skipped; Type renames proceed";
                        errors.Add(detail);
                        rows.Add(new RenameRow
                        {
                            Status = "Error",
                            Kind = "Family",
                            OldName = famName,
                            NewName = newName,
                            Detail = detail
                        });
                        familyRenames.Remove(famUid);
                    }
                }
            }

            // ── Batch duplicate detection — Family level ───────────────────────
            foreach (var kvp in targetFamilyNames)
            {
                if (kvp.Value.Count > 1)
                {
                    foreach (var uid in kvp.Value)
                    {
                        if (familyRenames.TryGetValue(uid, out var entry))
                        {
                            var detail = $"{kvp.Value.Count} families share this target name";
                            skippedDuplicates.Add(detail);
                            rows.Add(new RenameRow
                            {
                                Status = "Skipped",
                                Kind = "Family",
                                OldName = entry.Family.Name,
                                NewName = kvp.Key,
                                Detail = detail
                            });
                            familyRenames.Remove(uid);
                        }
                    }
                }
            }

            // ── Batch duplicate detection — Type level ─────────────────────────
            foreach (var kvp in targetSymbolNames)
            {
                if (kvp.Value.Count > 1)
                {
                    foreach (var uid in kvp.Value)
                    {
                        if (symbolRenames.TryGetValue(uid, out var entry))
                        {
                            var detail = $"{kvp.Value.Count} types share this target name";
                            skippedDuplicates.Add(detail);
                            rows.Add(new RenameRow
                            {
                                Status = "Skipped",
                                Kind = "Type",
                                OldName = $"{entry.Family.Name} :: {entry.Symbol.Name}",
                                NewName = kvp.Key.NewName,
                                Detail = detail
                            });
                            symbolRenames.Remove(uid);
                        }
                    }
                }
            }

            // ── Project-level conflict checks ──────────────────────────────────
            var existingFamNames = GetAllFamilyNames(doc);

            var finalFamilyRenames = new Dictionary<string, (Family Family, string NewName)>();
            foreach (var kvp in familyRenames)
            {
                var fam = kvp.Value.Family;
                var newName = kvp.Value.NewName;
                var curName = fam.Name;

                if (string.Equals(curName, newName, StringComparison.OrdinalIgnoreCase))
                {
                    alreadyCorrect.Add(newName);
                    rows.Add(new RenameRow
                    {
                        Status = "Already Correct",
                        Kind = "Family",
                        OldName = curName,
                        NewName = newName,
                        Detail = ""
                    });
                    continue;
                }
                if (existingFamNames.Contains(newName))
                {
                    var detail = "Name already taken by another family in project";
                    skippedDuplicates.Add(detail);
                    rows.Add(new RenameRow
                    {
                        Status = "Skipped",
                        Kind = "Family",
                        OldName = curName,
                        NewName = newName,
                        Detail = detail
                    });
                    continue;
                }
                finalFamilyRenames[kvp.Key] = kvp.Value;
            }

            var finalSymbolRenames = new Dictionary<string, (FamilySymbol Symbol, Family Family, string NewName)>();
            foreach (var kvp in symbolRenames)
            {
                var sym = kvp.Value.Symbol;
                var fam = kvp.Value.Family;
                var newName = kvp.Value.NewName;
                var curName = sym.Name;

                if (string.Equals(curName, newName, StringComparison.OrdinalIgnoreCase))
                {
                    alreadyCorrect.Add(newName);
                    rows.Add(new RenameRow
                    {
                        Status = "Already Correct",
                        Kind = "Type",
                        OldName = $"{fam.Name} :: {curName}",
                        NewName = newName,
                        Detail = ""
                    });
                    continue;
                }

                var existingTypes = GetTypeNamesInFamily(fam);
                existingTypes.Remove(curName);
                if (existingTypes.Contains(newName))
                {
                    var detail = "Name already taken by another type in this family";
                    skippedDuplicates.Add(detail);
                    rows.Add(new RenameRow
                    {
                        Status = "Skipped",
                        Kind = "Type",
                        OldName = $"{fam.Name} :: {curName}",
                        NewName = newName,
                        Detail = detail
                    });
                    continue;
                }
                finalSymbolRenames[kvp.Key] = kvp.Value;
            }

            // ── Transaction ────────────────────────────────────────────────────
            if (finalFamilyRenames.Count > 0 || finalSymbolRenames.Count > 0)
            {
                var t = new Transaction(doc, "Family Renamer: Rename Families and Types");
                try
                {
                    t.Start();

                    foreach (var kvp in finalFamilyRenames)
                    {
                        var fam = kvp.Value.Family;
                        var newName = kvp.Value.NewName;
                        var oldName = fam.Name;
                        try
                        {
                            fam.Name = newName;
                            successfulRenames.Add(newName);
                            rows.Add(new RenameRow
                            {
                                Status = "Renamed",
                                Kind = "Family",
                                OldName = oldName,
                                NewName = newName,
                                Detail = ""
                            });
                        }
                        catch (Exception ex)
                        {
                            errors.Add(ex.Message);
                            rows.Add(new RenameRow
                            {
                                Status = "Error",
                                Kind = "Family",
                                OldName = oldName,
                                NewName = newName,
                                Detail = ex.Message
                            });
                        }
                    }

                    foreach (var kvp in finalSymbolRenames)
                    {
                        var sym = kvp.Value.Symbol;
                        var fam = kvp.Value.Family;
                        var newName = kvp.Value.NewName;
                        var oldType = sym.Name;
                        var famName = fam.Name;
                        try
                        {
                            sym.Name = newName;
                            successfulRenames.Add(newName);
                            rows.Add(new RenameRow
                            {
                                Status = "Renamed",
                                Kind = "Type",
                                OldName = $"{famName} :: {oldType}",
                                NewName = newName,
                                Detail = ""
                            });
                        }
                        catch (Exception ex)
                        {
                            errors.Add(ex.Message);
                            rows.Add(new RenameRow
                            {
                                Status = "Error",
                                Kind = "Type",
                                OldName = $"{famName} :: {oldType}",
                                NewName = newName,
                                Detail = ex.Message
                            });
                        }
                    }

                    t.Commit();
                }
                catch (Exception txError)
                {
                    try { if (t.HasStarted() && !t.HasEnded()) t.RollBack(); } catch { }
                    errors.Add($"Transaction rolled back: {txError.Message}");
                    rows.Add(new RenameRow
                    {
                        Status = "Error",
                        Kind = "—",
                        OldName = "—",
                        NewName = "—",
                        Detail = $"Transaction rolled back: {txError.Message}"
                    });
                }
                finally { t.Dispose(); }
            }

            // ── Summary counts ─────────────────────────────────────────────────
            int totalSkipped = skippedNoParams.Count + skippedExcluded.Count
                             + skippedDuplicates.Count + skippedNonFamily.Count;

            // ── Show table report ──────────────────────────────────────────────
            ShowResultsTable(
                "Family Renamer — Results",
                rows,
                selected: selectedElements.Count,
                renamed: successfulRenames.Count,
                alreadyOk: alreadyCorrect.Count,
                skipped: totalSkipped,
                errorCount: errors.Count);

            return Result.Succeeded;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  UI — "Refined Native / AVES Red" design system
        //  All WPF types are fully qualified to avoid ambiguity with Revit API.
        // ══════════════════════════════════════════════════════════════════════

        #region ── Design tokens ─────────────────────────────────────────────

        // Brand
        private const string ColAccent = "#E64A2E";
        private const string ColAccentDeep = "#C2402A";
        private const string ColAccentTint = "#FBE9E4";
        // Neutrals
        private const string ColSurface = "#FFFFFF";
        private const string ColSurface2 = "#FAFAFB";
        private const string ColZebra = "#F7F8FA";
        private const string ColBorder = "#E5E6EA";
        private const string ColBorderStrong = "#D4D6DC";
        private const string ColText = "#1F2329";
        private const string ColTextDim = "#5A6069";
        private const string ColTextFaint = "#8B9099";
        // Status
        private const string ColOkBg = "#E4F3EA"; private const string ColOkFg = "#2C7A52";
        private const string ColSkipBg = "#EDEFF2"; private const string ColSkipFg = "#5B6573";
        private const string ColFailBg = "#FBE3DE"; private const string ColFailFg = "#C0381F";
        private const string ColWarnBg = "#FAF0DA"; private const string ColWarnFg = "#9A6B12";
        private const string ColInfoBg = "#E7EFFB"; private const string ColInfoFg = "#2E66A8";

        private static readonly System.Windows.Media.FontFamily UiFont = new System.Windows.Media.FontFamily("Segoe UI");
        private static readonly System.Windows.Media.FontFamily MonoFont = new System.Windows.Media.FontFamily("Consolas");

        private static System.Windows.Media.Color HexColor(string hex) =>
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);

        private static System.Windows.Media.SolidColorBrush Brush(string hex) =>
            new System.Windows.Media.SolidColorBrush(HexColor(hex));

        #endregion

        #region ── Composite widget factories ────────────────────────────────

        /// <summary>
        /// Coloured pill badge — replaces old emoji status glyphs.
        /// kind: "Renamed" | "Skipped" | "Error" | "Already Correct"
        /// </summary>
        private static System.Windows.Controls.Border StatusPill(string? kind)
        {
            var (bg, fg, label) = kind switch
            {
                "Renamed" => (ColOkBg, ColOkFg, "Renamed"),
                "Skipped" => (ColSkipBg, ColSkipFg, "Skipped"),
                "Error" => (ColFailBg, ColFailFg, "Failed"),
                "Already Correct" => (ColInfoBg, ColInfoFg, "No change"),
                _ => (ColInfoBg, ColInfoFg, kind)
            };

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = Brush(fg),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new System.Windows.Thickness(0, 0, 5, 0)
            };
            var text = new System.Windows.Controls.TextBlock
            {
                Text = label,
                Foreground = Brush(fg),
                FontFamily = UiFont,
                FontSize = 11.5,
                FontWeight = System.Windows.FontWeights.SemiBold,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            var row = new System.Windows.Controls.StackPanel
            { Orientation = System.Windows.Controls.Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(text);

            return new System.Windows.Controls.Border
            {
                Background = Brush(bg),
                CornerRadius = new System.Windows.CornerRadius(6),
                Padding = new System.Windows.Thickness(8, 4, 10, 4),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new System.Windows.Thickness(12, 0, 12, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Child = row
            };
        }

        /// <summary>
        /// Summary stat tile — large mono number + faint label below.
        /// accentKind: "" | "ok" | "fail" | "warn" | "accent"
        /// </summary>
        private static System.Windows.Controls.Border StatTile(string value, string label, string accentKind = "")
        {
            string valColor = accentKind switch
            {
                "ok" => ColOkFg,
                "fail" => ColFailFg,
                "warn" => ColWarnFg,
                "accent" => ColAccent,
                _ => ColText
            };

            var numBlock = new System.Windows.Controls.TextBlock
            {
                Text = value,
                FontFamily = MonoFont,
                FontSize = 22,
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = Brush(valColor),
                Margin = new System.Windows.Thickness(0, 0, 0, 3)
            };
            var labelBlock = new System.Windows.Controls.TextBlock
            {
                Text = label,
                FontFamily = UiFont,
                FontSize = 11,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = Brush(ColTextFaint)
            };
            var stack = new System.Windows.Controls.StackPanel();
            stack.Children.Add(numBlock);
            stack.Children.Add(labelBlock);

            return new System.Windows.Controls.Border
            {
                Background = Brush(ColSurface),
                BorderBrush = Brush(ColBorder),
                BorderThickness = new(1),
                CornerRadius = new(5),
                Padding = new(14, 12, 14, 12),
                Margin = new(0, 0, 10, 0),
                Child = stack
            };
        }

        /// <summary>
        /// Ø glænd / Hilti logo embedded as a Base64 PNG.
        /// Rendered as an Image sized to fit the header strip height.
        /// </summary>
        private static System.Windows.Controls.Image BrandTile(double height = 28)
        {
            const string b64 = @"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";

            var bytes = System.Convert.FromBase64String(b64);
            var stream = new System.IO.MemoryStream(bytes);
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = stream;
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.DecodePixelHeight = (int)height;   // scale to target height, keep aspect ratio
            bmp.EndInit();
            bmp.Freeze();

            return new System.Windows.Controls.Image
            {
                Source = bmp,
                Height = height,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Stretch = System.Windows.Media.Stretch.Uniform,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
        }

        #endregion

        #region ── ShowResultsTable ──────────────────────────────────────────

        /// <summary>
        /// Displays the rename results in a polished "Refined Native" WPF window:
        /// a stat-tile summary bar, a colour-coded DataGrid with pill badges, and
        /// a styled footer with a Close button.
        /// </summary>
        private static void ShowResultsTable(
            string title,
            List<RenameRow> rows,
            int selected,
            int renamed,
            int alreadyOk,
            int skipped,
            int errorCount)
        {
            // ── Root layout: 3 rows (header strip | body | footer) ────────────
            var root = new System.Windows.Controls.Grid();
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
            { Height = System.Windows.GridLength.Auto });                      // header
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
            { Height = System.Windows.GridLength.Auto });                      // stat tiles
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
            { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) }); // table
            root.RowDefinitions.Add(new System.Windows.Controls.RowDefinition
            { Height = System.Windows.GridLength.Auto });                      // footer

            // ── Row 0 — Header strip ──────────────────────────────────────────
            var headerBorder = new System.Windows.Controls.Border
            {
                Background = Brush(ColSurface),
                BorderBrush = Brush(ColBorder),
                BorderThickness = new System.Windows.Thickness(0, 0, 0, 1),
                Padding = new System.Windows.Thickness(20, 12, 20, 12)
            };
            var headerRow = new System.Windows.Controls.StackPanel
            { Orientation = System.Windows.Controls.Orientation.Horizontal };
            headerRow.Children.Add(BrandTile(32));   // logo, 32 px tall — aspect ratio auto
            headerBorder.Child = headerRow;
            System.Windows.Controls.Grid.SetRow(headerBorder, 0);
            root.Children.Add(headerBorder);

            // ── Row 1 — Stat tiles ────────────────────────────────────────────
            var statBorder = new System.Windows.Controls.Border
            {
                Background = Brush(ColSurface2),
                BorderBrush = Brush(ColBorder),
                BorderThickness = new System.Windows.Thickness(0, 0, 0, 1),
                Padding = new System.Windows.Thickness(20, 14, 20, 14)
            };
            var statPanel = new System.Windows.Controls.StackPanel
            { Orientation = System.Windows.Controls.Orientation.Horizontal };
            statPanel.Children.Add(StatTile(selected.ToString(), "Selected", ""));
            statPanel.Children.Add(StatTile(renamed.ToString(), "Renamed", "ok"));
            statPanel.Children.Add(StatTile(alreadyOk.ToString(), "Already correct", ""));
            statPanel.Children.Add(StatTile(skipped.ToString(), "Skipped", ""));
            statPanel.Children.Add(StatTile(errorCount.ToString(), "Errors",
                errorCount > 0 ? "fail" : ""));
            statBorder.Child = statPanel;
            System.Windows.Controls.Grid.SetRow(statBorder, 1);
            root.Children.Add(statBorder);

            // ── Row 2 — DataGrid with section label ───────────────────────────
            var tableWrap = new System.Windows.Controls.Border
            {
                Padding = new System.Windows.Thickness(20, 14, 20, 0)
            };
            var tableStack = new System.Windows.Controls.DockPanel();

            // Section label "RESULTS"
            var sectionRow = new System.Windows.Controls.DockPanel
            {
                Margin = new System.Windows.Thickness(0, 0, 0, 8),
                LastChildFill = false
            };
            sectionRow.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = "RESULTS",
                FontFamily = UiFont,
                FontSize = 11,
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = Brush(ColTextFaint)
            });
            var countText = new System.Windows.Controls.TextBlock
            {
                Text = $"showing {rows.Count} item{(rows.Count == 1 ? "" : "s")}",
                FontFamily = UiFont,
                FontSize = 11,
                Foreground = Brush(ColTextFaint)
            };
            System.Windows.Controls.DockPanel.SetDock(countText, System.Windows.Controls.Dock.Right);
            sectionRow.Children.Add(countText);

            System.Windows.Controls.DockPanel.SetDock(sectionRow, System.Windows.Controls.Dock.Top);
            tableStack.Children.Add(sectionRow);

            // DataGrid
            var dataGrid = new System.Windows.Controls.DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserSortColumns = true,
                CanUserResizeColumns = true,
                CanUserReorderColumns = true,
                HeadersVisibility = System.Windows.Controls.DataGridHeadersVisibility.Column,
                GridLinesVisibility = System.Windows.Controls.DataGridGridLinesVisibility.None,
                SelectionMode = System.Windows.Controls.DataGridSelectionMode.Single,
                FontFamily = UiFont,
                FontSize = 12.5,
                Foreground = Brush(ColText),
                Background = Brush(ColSurface),
                RowBackground = Brush(ColSurface),
                AlternatingRowBackground = Brush(ColZebra),
                AlternationCount = 2,
                RowHeight = 42,
                BorderBrush = Brush(ColBorder),
                BorderThickness = new System.Windows.Thickness(1),
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
            };

            // Column header style (applied via a factory since we're fully programmatic)
            static System.Windows.Controls.DataGridTextColumn TextCol(string header, string bindingPath, double width = 0)
            {
                var col = new System.Windows.Controls.DataGridTextColumn
                {
                    Header = header.ToUpper(),
                    Binding = new System.Windows.Data.Binding(bindingPath)
                };
                col.Width = width > 0
                    ? new System.Windows.Controls.DataGridLength(width)
                    : new System.Windows.Controls.DataGridLength(1,
                          System.Windows.Controls.DataGridLengthUnitType.Star);

                // Header style
                var hs = new System.Windows.Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.FontFamilyProperty, UiFont));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.FontSizeProperty, 10.5));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.FontWeightProperty, System.Windows.FontWeights.Bold));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.ForegroundProperty, Brush(ColTextFaint)));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.BackgroundProperty, Brush(ColSurface2)));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.PaddingProperty,
                    new System.Windows.Thickness(12, 9, 12, 9)));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.BorderBrushProperty, Brush(ColBorderStrong)));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.BorderThicknessProperty,
                    new System.Windows.Thickness(0, 0, 0, 1.5)));
                col.HeaderStyle = hs;

                // Cell style (padding + vertical align)
                var cs = new System.Windows.Style(typeof(System.Windows.Controls.DataGridCell));
                cs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.PaddingProperty,
                    new System.Windows.Thickness(12, 0, 12, 0)));
                cs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.BorderThicknessProperty,
                    new System.Windows.Thickness(0)));
                cs.Setters.Add(new System.Windows.Setter(
                    System.Windows.FrameworkElement.VerticalAlignmentProperty,
                    System.Windows.VerticalAlignment.Center));
                col.CellStyle = cs;

                return col;
            }

            // Status column — pill badge via DataGridTemplateColumn
            var statusCol = new System.Windows.Controls.DataGridTemplateColumn
            {
                Header = "STATUS",
                Width = new System.Windows.Controls.DataGridLength(130),
                SortMemberPath = "Status"
            };
            var hs2 = new System.Windows.Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.FontFamilyProperty, UiFont));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.FontSizeProperty, 10.5));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.FontWeightProperty, System.Windows.FontWeights.Bold));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.ForegroundProperty, Brush(ColTextFaint)));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.BackgroundProperty, Brush(ColSurface2)));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.PaddingProperty,
                new System.Windows.Thickness(12, 9, 12, 9)));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.BorderBrushProperty, Brush(ColBorderStrong)));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.BorderThicknessProperty,
                new System.Windows.Thickness(0, 0, 0, 1.5)));
            statusCol.HeaderStyle = hs2;

            // DataTemplate built in code — pill is created in the LoadingRow event below
            var cellTemplate = new System.Windows.DataTemplate();
            statusCol.CellTemplate = cellTemplate;   // placeholder; actual pill drawn in LoadingRow

            dataGrid.Columns.Add(statusCol);
            dataGrid.Columns.Add(TextCol("Kind", "Kind", 70));
            dataGrid.Columns.Add(TextCol("Old Name", "OldName"));
            dataGrid.Columns.Add(TextCol("New Name", "NewName"));
            dataGrid.Columns.Add(TextCol("Detail", "Detail"));

            // Pill badges: replace first column's cell content at row-load time
            dataGrid.LoadingRow += (s, e) =>
            {
                var row = e.Row;
                if (row.Item is not RenameRow r) return;

                // Swap status cell content for a pill after the row is loaded
                row.Loaded += (rs, re) =>
                {
                    // Walk the visual tree to find cell 0
                    var presenter = FindVisualChild<System.Windows.Controls.Primitives.DataGridCellsPresenter>(row);
                    if (presenter == null) return;
                    if (presenter.ItemContainerGenerator.ContainerFromIndex(0)
                        is not System.Windows.Controls.DataGridCell cell0) return;
                    cell0.Content = StatusPill(r.Status);
                };
            };

            var source = new System.Collections.ObjectModel.ObservableCollection<RenameRow>(rows);
            dataGrid.ItemsSource = source;
            tableStack.Children.Add(dataGrid);
            tableWrap.Child = tableStack;
            System.Windows.Controls.Grid.SetRow(tableWrap, 2);
            root.Children.Add(tableWrap);

            // ── Row 3 — Footer ────────────────────────────────────────────────
            var footerBorder = new System.Windows.Controls.Border
            {
                Background = Brush(ColSurface2),
                BorderBrush = Brush(ColBorder),
                BorderThickness = new System.Windows.Thickness(0, 1, 0, 0),
                Padding = new System.Windows.Thickness(20, 12, 20, 12)
            };
            var footerDock = new System.Windows.Controls.DockPanel { LastChildFill = false };

            // Right: Close button (primary)
            var closeBtn = MakePrimaryButton("Close");
            System.Windows.Controls.DockPanel.SetDock(closeBtn, System.Windows.Controls.Dock.Right);
            footerDock.Children.Add(closeBtn);

            // Right: Copy to clipboard button (secondary), left of Close
            var copyBtn = MakeSecondaryButton("⧉  Copy to Clipboard");
            copyBtn.Margin = new System.Windows.Thickness(0, 0, 8, 0);
            System.Windows.Controls.DockPanel.SetDock(copyBtn, System.Windows.Controls.Dock.Right);
            footerDock.Children.Add(copyBtn);

            // Left: faint caption
            var caption = new System.Windows.Controls.TextBlock
            {
                Text = $"Family Renamer  ·  {rows.Count} result{(rows.Count == 1 ? "" : "s")}",
                FontFamily = UiFont,
                FontSize = 11.5,
                Foreground = Brush(ColTextFaint),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            System.Windows.Controls.DockPanel.SetDock(caption, System.Windows.Controls.Dock.Left);
            footerDock.Children.Add(caption);

            footerBorder.Child = footerDock;
            System.Windows.Controls.Grid.SetRow(footerBorder, 3);
            root.Children.Add(footerBorder);

            // ── Window ────────────────────────────────────────────────────────
            var window = new System.Windows.Window
            {
                Title = title,
                Width = 900,
                Height = 560,
                MinWidth = 640,
                MinHeight = 360,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                ResizeMode = System.Windows.ResizeMode.CanResize,
                Background = Brush(ColSurface),
                FontFamily = UiFont,
                UseLayoutRounding = true,
                Content = root
            };

            closeBtn.Click += (s, e) => window.Close();

            // Copy button: build TSV, put on clipboard, flash "Copied!" for 2 s
            copyBtn.Click += (s, e) =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Status\tKind\tOld Name\tNew Name\tDetail");
                foreach (var r in rows)
                    sb.AppendLine($"{r.Status}\t{r.Kind}\t{r.OldName}\t{r.NewName}\t{r.Detail}");
                System.Windows.Clipboard.SetText(sb.ToString());

                // Flash confirmation
                copyBtn.Content = "✓  Copied!";
                copyBtn.IsEnabled = false;
                var timer = new System.Windows.Threading.DispatcherTimer
                { Interval = System.TimeSpan.FromSeconds(2) };
                timer.Tick += (ts, te) =>
                {
                    timer.Stop();
                    copyBtn.Content = "⧉  Copy to Clipboard";
                    copyBtn.IsEnabled = true;
                };
                timer.Start();
            };

            window.ShowDialog();
        }

        #endregion

        #region ── ShowWpfAlert ──────────────────────────────────────────────

        /// <summary>
        /// Small modal alert — "Refined Native" styling.
        /// Replaces the previous plain-white alert window.
        /// </summary>
        private static void ShowWpfAlert(string title, string body)
        {
            // Icon area (accent circle with "!")
            var icon = new System.Windows.Controls.Border
            {
                Width = 36,
                Height = 36,
                Background = Brush(ColAccentTint),
                CornerRadius = new System.Windows.CornerRadius(18),
                Margin = new System.Windows.Thickness(0, 0, 14, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = "!",
                    FontFamily = UiFont,
                    FontSize = 17,
                    FontWeight = System.Windows.FontWeights.Bold,
                    Foreground = Brush(ColAccent),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                }
            };

            // Message text
            var msg = new System.Windows.Controls.TextBlock
            {
                Text = body,
                FontFamily = UiFont,
                FontSize = 13,
                Foreground = Brush(ColText),
                TextWrapping = System.Windows.TextWrapping.Wrap,
                LineHeight = 20,
                VerticalAlignment = System.Windows.VerticalAlignment.Top
            };

            var contentRow = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new System.Windows.Thickness(20, 20, 20, 0)
            };
            contentRow.Children.Add(icon);
            contentRow.Children.Add(msg);

            // Footer
            var okBtn = MakePrimaryButton("OK");
            var footer = new System.Windows.Controls.Border
            {
                Background = Brush(ColSurface2),
                BorderBrush = Brush(ColBorder),
                BorderThickness = new System.Windows.Thickness(0, 1, 0, 0),
                Padding = new System.Windows.Thickness(20, 12, 20, 12),
                Margin = new System.Windows.Thickness(0, 16, 0, 0),
                Child = new System.Windows.Controls.DockPanel
                {
                    LastChildFill = false,
                    Children = { DockRight(okBtn) }
                }
            };

            var root = new System.Windows.Controls.StackPanel();
            root.Children.Add(contentRow);
            root.Children.Add(footer);

            var window = new System.Windows.Window
            {
                Title = title,
                Width = 420,
                Height = 200,
                MinHeight = 180,
                SizeToContent = System.Windows.SizeToContent.Height,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                ResizeMode = System.Windows.ResizeMode.NoResize,
                Background = Brush(ColSurface),
                FontFamily = UiFont,
                UseLayoutRounding = true,
                Content = root
            };

            okBtn.Click += (s, e) => window.Close();
            window.ShowDialog();
        }

        #endregion

        #region ── Small UI helpers ──────────────────────────────────────────

        /// <summary>Accent-fill primary button with hover state.</summary>
        private static System.Windows.Controls.Button MakePrimaryButton(string label)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = label,
                FontFamily = UiFont,
                FontSize = 13,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                Background = Brush(ColAccent),
                Padding = new System.Windows.Thickness(20, 8, 20, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                MinWidth = 80
            };

            // Rounded corners + hover via a ControlTemplate
            var tpl = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
            var bd = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            bd.Name = "bd";
            bd.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new System.Windows.CornerRadius(5));
            bd.SetBinding(System.Windows.Controls.Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.TemplatedParent)
                });
            var cp = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            cp.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty,
                System.Windows.HorizontalAlignment.Center);
            cp.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty,
                System.Windows.VerticalAlignment.Center);
            cp.SetBinding(System.Windows.Controls.ContentPresenter.MarginProperty,
                new System.Windows.Data.Binding("Padding")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.TemplatedParent)
                });
            bd.AppendChild(cp);
            tpl.VisualTree = bd;

            tpl.Triggers.Add(new System.Windows.Trigger
            {
                Property = System.Windows.Controls.Control.IsMouseOverProperty,
                Value = true,
                Setters = { new System.Windows.Setter(
                    System.Windows.Controls.Border.BackgroundProperty, Brush(ColAccentDeep), "bd") }
            });

            btn.Template = tpl;
            return btn;
        }

        /// <summary>White-fill secondary button (1 px BorderStrong border) with hover state.</summary>
        private static System.Windows.Controls.Button MakeSecondaryButton(string label)
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = label,
                FontFamily = UiFont,
                FontSize = 13,
                FontWeight = System.Windows.FontWeights.SemiBold,
                Foreground = Brush(ColText),
                Background = Brush(ColSurface),
                Padding = new System.Windows.Thickness(16, 8, 16, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                MinWidth = 80
            };

            var tpl = new System.Windows.Controls.ControlTemplate(typeof(System.Windows.Controls.Button));
            var bd = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            bd.Name = "bd";
            bd.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new System.Windows.CornerRadius(5));
            bd.SetValue(System.Windows.Controls.Border.BorderBrushProperty, Brush(ColBorderStrong));
            bd.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new System.Windows.Thickness(1));
            bd.SetBinding(System.Windows.Controls.Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.TemplatedParent)
                });
            var cp = new System.Windows.FrameworkElementFactory(typeof(System.Windows.Controls.ContentPresenter));
            cp.SetValue(System.Windows.Controls.ContentPresenter.HorizontalAlignmentProperty,
                System.Windows.HorizontalAlignment.Center);
            cp.SetValue(System.Windows.Controls.ContentPresenter.VerticalAlignmentProperty,
                System.Windows.VerticalAlignment.Center);
            cp.SetBinding(System.Windows.Controls.ContentPresenter.MarginProperty,
                new System.Windows.Data.Binding("Padding")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.TemplatedParent)
                });
            bd.AppendChild(cp);
            tpl.VisualTree = bd;

            tpl.Triggers.Add(new System.Windows.Trigger
            {
                Property = System.Windows.Controls.Control.IsMouseOverProperty,
                Value = true,
                Setters = { new System.Windows.Setter(
                    System.Windows.Controls.Border.BackgroundProperty, Brush(ColSurface2), "bd") }
            });

            btn.Template = tpl;
            return btn;
        }

        /// <summary>Sets DockPanel.Dock=Right on a UIElement and returns it (for collection initializers).</summary>
        private static System.Windows.UIElement DockRight(System.Windows.UIElement el)
        {
            System.Windows.Controls.DockPanel.SetDock(el, System.Windows.Controls.Dock.Right);
            return el;
        }

        /// <summary>
        /// Walks the visual tree to find the first child of type T.
        /// Used to locate the DataGridCellsPresenter inside a DataGridRow.
        /// </summary>
        private static T? FindVisualChild<T>(System.Windows.DependencyObject parent)
            where T : System.Windows.DependencyObject
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                if (FindVisualChild<T>(child) is { } result) return result;
            }
            return null;
        }

        #endregion

        // ══════════════════════════════════════════════════════════════════════
        //  Revit helper methods — unchanged from original
        // ══════════════════════════════════════════════════════════════════════

        private static string? GetParamValue(Element element, string paramName)
        {
            var param = element.LookupParameter(paramName);
            if (param == null)
            {
                try
                {
                    var typeId = element.GetTypeId();
                    if (typeId != null && typeId != ElementId.InvalidElementId)
                    {
                        var elType = element.Document.GetElement(typeId);
                        if (elType != null) param = elType.LookupParameter(paramName);
                    }
                }
                catch { }
            }
            if (param == null) return null;
            try
            {
                var val = param.AsString();
                if (string.IsNullOrWhiteSpace(val)) val = param.AsValueString();
                return string.IsNullOrWhiteSpace(val) ? null : val.Trim();
            }
            catch { return null; }
        }

        private static HashSet<string> GetAllFamilyNames(Document doc)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            var collector = new FilteredElementCollector(doc).OfClass(typeof(Family));
            foreach (var el in collector)
            {
                try { names.Add(el.Name); } catch { }
            }
            return names;
        }

        private static HashSet<string> GetTypeNamesInFamily(Family family)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var symId in family.GetFamilySymbolIds())
                {
                    var sym = family.Document.GetElement(symId);
                    if (sym != null)
                    {
                        try { names.Add(sym.Name); } catch { }
                    }
                }
            }
            catch { }
            return names;
        }
    }
}