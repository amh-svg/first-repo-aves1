// -----------------------------------------------------------------------
// Title: Family Renamer
// Summary: Renames the Families and Types of the selected elements to a
//          standard "{Item Number} _ {PMO_Description}" format and shows a
//          results table of what was renamed, skipped, or errored.
// Key logic: Reads the "Item Number" and "PMO_Description" parameters from
//            each selected element (or its type), batches renames while
//            detecting duplicate/conflicting target names, then applies
//            them in a single transaction.
// -----------------------------------------------------------------------
// .NET
using System;
using System.Collections.Generic;
using System.Text;
// Autodesk
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AVES.Core;

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
                AvControls.ShowAlert(
                    "Family Renamer",
                    "No elements selected.\n\nPlease select family instances or types and re-run.",
                    AlertSeverity.Warning);
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
                "Family Renamer",
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

        #region ── Composite widget factories ────────────────────────────────

        /// <summary>
        /// Coloured pill badge — replaces old emoji status glyphs.
        /// kind: "Renamed" | "Skipped" | "Error" | "Already Correct"
        /// Maps this file's own status domain onto AvControls.StatusPill's
        /// ok/skip/fail/info kinds for geometry, using labelOverride to keep
        /// this file's exact existing label text (reconciliation decision:
        /// unify styling, keep existing per-file wording). Visual change:
        /// the pill's geometry (dot size/margin, padding) now matches Core
        /// exactly, and the extra outer Margin(12,0,12,0) this file used to
        /// add is gone (Core doesn't set one).
        /// </summary>
        private static System.Windows.Controls.Border StatusPill(string? kind)
        {
            var (coreKind, label) = kind switch
            {
                "Renamed" => ("ok", "Renamed"),
                "Skipped" => ("skip", "Skipped"),
                "Error" => ("fail", "Failed"),
                "Already Correct" => ("info", "No change"),
                _ => ("info", kind ?? "")
            };
            return AvControls.StatusPill(coreKind, label);
        }

        /// <summary>
        /// Summary stat tile — large mono number + faint label below.
        /// accentKind: "" | "ok" | "fail" | "warn" | "accent"
        /// Routed to AvControls.StatTile. Visual change: value margin-bottom
        /// 3 -> 4, adds TextTrimming/ToolTip on the value, and the extra
        /// outer Margin(0,0,10,0) this file used to add is gone.
        /// </summary>
        private static System.Windows.Controls.Border StatTile(string value, string label, string accentKind = "") =>
            AvControls.StatTile(value, label, accentKind);

        // The local BrandTile is gone with the header that was its only caller.
        // It decoded AvTokens.LogoBase64 at 1x display height; ReportHeaderBar
        // decodes at 2x, so the logo here gets sharper as well as smaller.

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
            var headerBorder = AvControls.ReportHeaderBar(title);
            System.Windows.Controls.Grid.SetRow(headerBorder, 0);
            root.Children.Add(headerBorder);

            // ── Row 1 — Stat tiles ────────────────────────────────────────────
            var statBorder = new System.Windows.Controls.Border
            {
                Background = AvTokens.Brush(AvTokens.Surface2),
                BorderBrush = AvTokens.Brush(AvTokens.Border),
                BorderThickness = new System.Windows.Thickness(0, 0, 0, 1),
                Padding = new System.Windows.Thickness(20, 14, 20, 14)
            };
            var statPanel = new System.Windows.Controls.StackPanel
            { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var statTiles = new[]
            {
                StatTile(selected.ToString(), "Selected", ""),
                StatTile(renamed.ToString(), "Renamed", "ok"),
                StatTile(alreadyOk.ToString(), "Already correct", ""),
                StatTile(skipped.ToString(), "Skipped", ""),
                StatTile(errorCount.ToString(), "Errors", errorCount > 0 ? "fail" : "")
            };
            // AvControls.StatTile has no built-in margin (unlike this file's old
            // local copy, which added a trailing 10px margin on every tile) --
            // spacing between tiles now added explicitly here instead, matching
            // the convention already used by Cmds_Button10.cs/Cmds_Button11.cs.
            for (int i = 0; i < statTiles.Length; i++)
            {
                if (i > 0) statTiles[i].Margin = new System.Windows.Thickness(10, 0, 0, 0);
                statPanel.Children.Add(statTiles[i]);
            }
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
                FontFamily = AvTokens.Ui,
                FontSize = 11,
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = AvTokens.Brush(AvTokens.TextFaint)
            });
            var countText = new System.Windows.Controls.TextBlock
            {
                Text = $"showing {rows.Count} item{(rows.Count == 1 ? "" : "s")}",
                FontFamily = AvTokens.Ui,
                FontSize = 11,
                Foreground = AvTokens.Brush(AvTokens.TextFaint)
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
                FontFamily = AvTokens.Ui,
                FontSize = 12.5,
                Foreground = AvTokens.Brush(AvTokens.Text),
                Background = AvTokens.Brush(AvTokens.Surface),
                RowBackground = AvTokens.Brush(AvTokens.Surface),
                AlternatingRowBackground = AvTokens.Brush(AvTokens.Zebra),
                AlternationCount = 2,
                RowHeight = 42,
                BorderBrush = AvTokens.Brush(AvTokens.Border),
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
                    System.Windows.Controls.Control.FontFamilyProperty, AvTokens.Ui));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.FontSizeProperty, 10.5));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.FontWeightProperty, System.Windows.FontWeights.Bold));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.ForegroundProperty, AvTokens.Brush(AvTokens.TextFaint)));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.BackgroundProperty, AvTokens.Brush(AvTokens.Surface2)));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.PaddingProperty,
                    new System.Windows.Thickness(12, 9, 12, 9)));
                hs.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.BorderBrushProperty, AvTokens.Brush(AvTokens.BorderStrong)));
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
                    System.Windows.Controls.DataGridCell.TemplateProperty,
                    AvControls.DataGridCellTemplate()));
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
                System.Windows.Controls.Control.FontFamilyProperty, AvTokens.Ui));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.FontSizeProperty, 10.5));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.FontWeightProperty, System.Windows.FontWeights.Bold));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.ForegroundProperty, AvTokens.Brush(AvTokens.TextFaint)));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.BackgroundProperty, AvTokens.Brush(AvTokens.Surface2)));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.PaddingProperty,
                new System.Windows.Thickness(12, 9, 12, 9)));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.BorderBrushProperty, AvTokens.Brush(AvTokens.BorderStrong)));
            hs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.BorderThicknessProperty,
                new System.Windows.Thickness(0, 0, 0, 1.5)));
            statusCol.HeaderStyle = hs2;

            // The status column is the one column with no CellStyle of its own.
            // Its pill was never actually misaligned -- AvControls.StatusPill
            // centres its own Border -- but it takes the same style as the text
            // columns so padding and template stay uniform across the grid.
            var cs2 = new System.Windows.Style(typeof(System.Windows.Controls.DataGridCell));
            cs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.PaddingProperty,
                new System.Windows.Thickness(12, 0, 12, 0)));
            cs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.Control.BorderThicknessProperty,
                new System.Windows.Thickness(0)));
            cs2.Setters.Add(new System.Windows.Setter(
                System.Windows.Controls.DataGridCell.TemplateProperty,
                AvControls.DataGridCellTemplate()));
            statusCol.CellStyle = cs2;

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
            // LastChildFill, with the buttons docked first and the caption added
            // last: as a Dock.Left child the caption sized to its content and
            // would have pushed the buttons rather than ellipsing. It is the
            // fill child now, so its trimming has a width to work against.
            var footerDock = new System.Windows.Controls.DockPanel { LastChildFill = true };

            // Right: Close button (primary)
            var closeBtn = MakePrimaryButton("Close");
            System.Windows.Controls.DockPanel.SetDock(closeBtn, System.Windows.Controls.Dock.Right);
            footerDock.Children.Add(closeBtn);

            // Right: Copy to clipboard button (secondary), left of Close
            var copyBtn = MakeSecondaryButton("⧉  Copy to clipboard");
            copyBtn.Margin = new System.Windows.Thickness(0, 0, 8, 0);
            System.Windows.Controls.DockPanel.SetDock(copyBtn, System.Windows.Controls.Dock.Right);
            footerDock.Children.Add(copyBtn);

            // Left: faint caption. Result counts rather than a source document,
            // which is this window's own wording and stays.
            footerDock.Children.Add(AvControls.FooterCaption(
                $"Family Renamer  ·  {rows.Count} result{(rows.Count == 1 ? "" : "s")}"));

            var footerBorder = AvControls.FooterStrip(footerDock);
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
                Background = AvTokens.Brush(AvTokens.Surface),
                FontFamily = AvTokens.Ui,
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
                    copyBtn.Content = "⧉  Copy to clipboard";
                    copyBtn.IsEnabled = true;
                };
                timer.Start();
            };

            window.ShowDialog();
        }

        #endregion

        #region ── Small UI helpers ──────────────────────────────────────────

        /// <summary>
        /// Accent-fill primary button. Routed to AvControls.PrimaryButton
        /// (Family B -- caller wires Click externally, matching this file's
        /// existing calling convention exactly). Visual change: padding
        /// (20,8,20,8) -> Core's uniform (16,9,16,9), and MinWidth=80 is
        /// gone (Core sets none).
        /// </summary>
        private static System.Windows.Controls.Button MakePrimaryButton(string label) =>
            AvControls.PrimaryButton(label);

        /// <summary>
        /// White-fill secondary button (1px BorderStrong border). Routed to
        /// AvControls.SecondaryButton (Family B). Visual change: padding
        /// (16,8,16,8) -> Core's uniform (16,9,16,9), and MinWidth=80 is
        /// gone (Core sets none).
        /// </summary>
        private static System.Windows.Controls.Button MakeSecondaryButton(string label) =>
            AvControls.SecondaryButton(label);

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