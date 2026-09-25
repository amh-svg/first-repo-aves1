// -----------------------------------------------------------------------
// Title: Assembly Tag Completeness Checker
// Summary: Lets the user pick assembly views, scans the assemblies shown
//          in them for family types with untagged members, and displays a
//          report of which types are missing tags in each view.
// Key logic: Builds a set of already-tagged type IDs per view from all
//            independent tags, then for each assembly in the selected
//            views compares its distinct family types against that set to
//            flag the untagged ones (with an example untagged member ID).
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
using WpfClipboard = System.Windows.Clipboard;
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

namespace AVES.Cmds_Button11
{
    // ─────────────────────────────────────────────────────────────────────────
    //  SCAN DATA MODEL
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>One distinct member family type within one assembly, pass or fail.</summary>
    internal class FamilyTypeScanRow
    {
        public ElementId TypeId = ElementId.InvalidElementId;
        public string TypeName = "";
        public int MemberCountInAssembly;
        public bool Passed;
        /// <summary>
        /// The actual member elements of this type, IN THIS ASSEMBLY, that are
        /// themselves directly tagged in the scanned view -- empty if none,
        /// even when Passed is true via a same-type element tagged elsewhere
        /// in the same view.
        /// </summary>
        public List<ElementId> TaggedMembersWithinAssembly = new List<ElementId>();
        public ElementId ExampleUntaggedMemberId = ElementId.InvalidElementId;
    }

    /// <summary>Result of a tag scan scoped to a specific view (or set of views).</summary>
    internal class TaggedTypeScanResult
    {
        public HashSet<ElementId> TaggedElementIds = new HashSet<ElementId>();
        public HashSet<ElementId> TaggedTypeIds = new HashSet<ElementId>();
        public Dictionary<ElementId, List<ElementId>> TaggingTagsByElement = new Dictionary<ElementId, List<ElementId>>();
        public int TotalTagsScanned;
        public int OrphanedTagsSkipped;
        public List<(ElementId TagId, string Error)> Exceptions = new List<(ElementId, string)>();
    }

    /// <summary>One assembly's scan result within one view.</summary>
    internal class AssemblyScanResult
    {
        public AssemblyInstance Assembly = null!;
        public List<FamilyTypeScanRow> Types = new List<FamilyTypeScanRow>();
        public int DistinctFamilyTypeCount => Types.Count;
        public List<FamilyTypeScanRow> Failing => Types.Where(t => !t.Passed).ToList();
    }

    internal static class TagCompletenessScanner
    {
        /// <summary>
        /// Assemblies whose member elements are visible in the given view, per
        /// the confirmed detection approach: collect elements the view's own
        /// FilteredElementCollector would show, then group by AssemblyInstanceId.
        /// </summary>
        public static List<AssemblyInstance> GetAssembliesInView(Document doc, View view)
        {
            var elems = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToElements();

            var assemblyIds = new HashSet<ElementId>();
            foreach (var el in elems)
            {
                var aid = el.AssemblyInstanceId;
                if (aid != ElementId.InvalidElementId)
                    assemblyIds.Add(aid);
            }

            return assemblyIds
                .Select(id => doc.GetElement(id) as AssemblyInstance)
                .Where(a => a != null)
                .Select(a => a!)
                .ToList();
        }

        /// <summary>
        /// Every IndependentTag in the document, fetched once and reused across
        /// every selected view (each view then filters this down by OwnerViewId,
        /// rather than re-querying the whole document per view).
        /// </summary>
        public static List<IndependentTag> GetAllIndependentTags(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();
        }

        /// <summary>
        /// View types that can host model elements and therefore support a
        /// view-scoped FilteredElementCollector. Deliberately an allow-list:
        /// schedules, legends, sheets and the various internal/report view
        /// types either carry no model geometry or make
        /// FilteredElementCollector(doc, viewId) throw, so anything not named
        /// here is out. Callouts need no entry of their own -- a callout
        /// reports the ViewType of the view it was cut from.
        /// </summary>
        private static readonly HashSet<ViewType> ScannableViewTypes = new HashSet<ViewType>
        {
            ViewType.FloorPlan,
            ViewType.CeilingPlan,
            ViewType.EngineeringPlan,
            ViewType.Section,
            ViewType.Elevation,
            ViewType.Detail,
            ViewType.ThreeD,
        };

        /// <summary>
        /// Every view the scanner can inspect: printable model views of a
        /// supported type that are actually placed on a sheet, templates
        /// excluded. Assembly views are included on the same terms as any other
        /// view -- they report the ViewType of the view they are (Section,
        /// FloorPlan, ThreeD ...), so no assembly-specific test is needed to
        /// keep them.
        /// <para>
        /// Placement comes from View.GetPlacementOnSheetStatus() (Revit 2023+),
        /// a direct relationship rather than a scan of Viewport elements, so no
        /// per-view sheet search is involved. PartiallyPlaced counts as placed:
        /// it exists for segmented schedules, which this allow-list already
        /// excludes, but accepting it keeps the predicate correct if the
        /// allow-list ever widens.
        /// </para>
        /// <para>
        /// The scan itself is unchanged and still assembly-centric: a view with
        /// no assembly members visible simply produces no rows.
        /// </para>
        /// </summary>
        public static List<View> GetScannableViews(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v =>
                {
                    var placement = v.GetPlacementOnSheetStatus();

                    return !v.IsTemplate
                        && v.CanBePrinted
                        && ScannableViewTypes.Contains(v.ViewType)
                        && (placement == ViewPlacementOnSheetStatus.CompletelyPlaced
                            || placement == ViewPlacementOnSheetStatus.PartiallyPlaced);
                })
                .ToList();
        }

        /// <summary>
        /// The views whose tags count as "tagging" for the given view.
        /// <para>
        /// Dependent views share their view-specific elements with the primary
        /// and with each other, but a tag's OwnerViewId is the single view it
        /// was placed in. Scoping a dependent view to itself alone would
        /// therefore report every family type as untagged whenever the tags
        /// were placed in the primary. The scope is widened to the whole
        /// dependency family in both directions: a dependent picks up its
        /// primary and its siblings, a primary picks up its dependents.
        /// </para>
        /// </summary>
        public static HashSet<ElementId> GetTagScopeForView(Document doc, View view)
        {
            var scope = new HashSet<ElementId> { view.Id };

            var primaryId = view.GetPrimaryViewId();
            var primary = primaryId != ElementId.InvalidElementId
                ? doc.GetElement(primaryId) as View
                : view;

            if (primary == null) return scope;

            scope.Add(primary.Id);
            foreach (var dependentId in primary.GetDependentViewIds())
                scope.Add(dependentId);

            return scope;
        }

        /// <summary>
        /// Tagged-element/tagged-type sets built only from tags whose
        /// OwnerViewId falls within the given scope. Orphaned tags
        /// (IsOrphaned == true, host/reference lost) are skipped
        /// deterministically up front rather than relying on
        /// GetTaggedLocalElementIds throwing.
        /// </summary>
        public static TaggedTypeScanResult BuildTaggedTypeIdsForViews(
            Document doc,
            ILookup<ElementId, IndependentTag> tagsByOwnerView,
            HashSet<ElementId> scopeViewIds)
        {
            var result = new TaggedTypeScanResult();
            // Indexed once by the caller, so this is a handful of key lookups
            // rather than a full pass over every tag in the document per view.
            var scopedTags = scopeViewIds.SelectMany(id => tagsByOwnerView[id]).ToList();
            result.TotalTagsScanned = scopedTags.Count;

            foreach (var tag in scopedTags)
            {
                if (tag.IsOrphaned)
                {
                    result.OrphanedTagsSkipped++;
                    continue;
                }

                ICollection<ElementId> taggedIds;
                try
                {
                    taggedIds = tag.GetTaggedLocalElementIds();
                }
                catch (Exception ex)
                {
                    result.Exceptions.Add((tag.Id, ex.Message));
                    continue;
                }

                foreach (var id in taggedIds)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    result.TaggedElementIds.Add(id);
                    if (!result.TaggingTagsByElement.TryGetValue(id, out var tagIds))
                    {
                        tagIds = new List<ElementId>();
                        result.TaggingTagsByElement[id] = tagIds;
                    }
                    tagIds.Add(tag.Id);

                    var typeId = el.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                        result.TaggedTypeIds.Add(typeId);
                }
            }

            return result;
        }

        /// <summary>
        /// Scans one assembly: groups its members by family type (dedup level
        /// per the confirmed spec), and reports every distinct family type's
        /// pass/fail status against a tag scan already scoped to the view
        /// being checked.
        /// </summary>
        public static AssemblyScanResult ScanAssembly(Document doc, AssemblyInstance assembly, TaggedTypeScanResult tagScan)
        {
            var membersByType = new Dictionary<ElementId, List<Element>>();
            foreach (var id in assembly.GetMemberIds())
            {
                var el = doc.GetElement(id);
                if (el == null) continue;
                var typeId = el.GetTypeId();
                if (typeId == ElementId.InvalidElementId) continue;

                if (!membersByType.TryGetValue(typeId, out var list))
                {
                    list = new List<Element>();
                    membersByType[typeId] = list;
                }
                list.Add(el);
            }

            var result = new AssemblyScanResult { Assembly = assembly };

            foreach (var kv in membersByType)
            {
                bool passed = tagScan.TaggedTypeIds.Contains(kv.Key);
                var taggedMembers = kv.Value.Where(el => tagScan.TaggedElementIds.Contains(el.Id)).Select(el => el.Id).ToList();
                var untaggedMember = kv.Value.FirstOrDefault(el => !tagScan.TaggedElementIds.Contains(el.Id));
                var typeElem = doc.GetElement(kv.Key);
                result.Types.Add(new FamilyTypeScanRow
                {
                    TypeId = kv.Key,
                    TypeName = typeElem?.Name ?? "(unknown type)",
                    MemberCountInAssembly = kv.Value.Count,
                    Passed = passed,
                    TaggedMembersWithinAssembly = taggedMembers,
                    ExampleUntaggedMemberId = untaggedMember?.Id ?? ElementId.InvalidElementId
                });
            }

            return result;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  VIEW PICKER (assembly views only, grouped/labeled by owning assembly)
    // ─────────────────────────────────────────────────────────────────────────

    internal class AssemblyViewPickerWindow : Window
    {
        public List<View> SelectedViews { get; private set; } = new List<View>();

        private readonly List<(View View, string SearchKey, WpfBorder Row, WpfCheckBox Box)> _rows
            = new List<(View, string, WpfBorder, WpfCheckBox)>();
        private readonly List<(WpfBorder Header, List<WpfBorder> ChildRows)> _sections
            = new List<(WpfBorder, List<WpfBorder>)>();
        private readonly WpfTextBox _searchBox;
        private readonly WpfTextBlock _countLabel;
        private bool _confirmed;

        /// <summary>
        /// Section label for a view in the picker. Assembly views keep their own
        /// group so the original workflow stays one glance away; everything else
        /// groups by view type, which is the only stable grouping a normal view
        /// has (it belongs to no assembly, and may sit on no sheet at all).
        /// </summary>
        private static string GroupLabelFor(View view)
        {
            if (view.IsAssemblyView) return "Assembly Views";

            switch (view.ViewType)
            {
                case ViewType.FloorPlan: return "Floor Plans";
                case ViewType.CeilingPlan: return "Ceiling Plans";
                case ViewType.EngineeringPlan: return "Structural Plans";
                case ViewType.Section: return "Sections";
                case ViewType.Elevation: return "Elevations";
                case ViewType.Detail: return "Detail Views";
                case ViewType.ThreeD: return "3D Views";
                default: return view.ViewType.ToString();
            }
        }

        public AssemblyViewPickerWindow(Document doc, List<View> scannableViews)
        {
            var ordered = scannableViews
                .Select(v => new { View = v, Group = GroupLabelFor(v) })
                .OrderBy(x => x.Group, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.View.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Title = "Tag Reviewer";
            Width = 520;
            Height = 640;
            MinWidth = 420;
            MinHeight = 400;
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

            var titleBar = AvControls.TitleBar("Tag Reviewer");
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
                ToolTip = "Filter by assembly or view name"
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
            if (ordered.Count == 0)
            {
                list.Children.Add(new WpfTextBlock
                {
                    Text = "No scannable views placed on sheets were found in this project.",
                    FontFamily = AvTokens.Ui,
                    FontSize = 13,
                    Foreground = AvTokens.B(AvTokens.TextFaint)
                });
            }
            foreach (var group in ordered.GroupBy(e => e.Group))
            {
                var headerVisual = AvControls.SectionHeader(group.Key);
                list.Children.Add(headerVisual);
                var childRows = new List<WpfBorder>();

                foreach (var entry in group)
                {
                    string searchKey = $"{group.Key} {entry.View.Name}";
                    var (visual, box) = AvControls.CheckRow(entry.View.Name, false);
                    visual.MouseLeftButtonUp += (s, e) => { box.IsChecked = !box.IsChecked; };
                    box.Checked += (s, e) => UpdateCount();
                    box.Unchecked += (s, e) => UpdateCount();
                    list.Children.Add(visual);
                    _rows.Add((entry.View, searchKey, visual, box));
                    childRows.Add(visual);
                }

                _sections.Add((headerVisual, childRows));
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
            var btnOk = AvControls.PrimaryBtn("▶  Run Script", OnOk);
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
            foreach (var (_, searchKey, row, _) in _rows)
            {
                bool match = filter.Length == 0 || searchKey.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                row.Visibility = match ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            }

            foreach (var (header, childRows) in _sections)
            {
                bool anyVisible = childRows.Any(r => r.Visibility == WpfVisibility.Visible);
                header.Visibility = anyVisible ? WpfVisibility.Visible : WpfVisibility.Collapsed;
            }
        }

        private void SetAllChecked(bool value)
        {
            foreach (var (_, _, row, box) in _rows)
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
            SelectedViews = _rows.Where(r => r.Box.IsChecked == true).Select(r => r.View).ToList();
            _confirmed = true;
            Close();
        }

        private void OnCancel() => Close();

        protected override void OnClosed(EventArgs e)
        {
            if (!_confirmed) SelectedViews = new List<View>();
            base.OnClosed(e);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  REPORT ROW + WINDOW
    // ─────────────────────────────────────────────────────────────────────────

    internal class TagReportRow
    {
        public string ViewName { get; set; } = "";
        public string AssemblyName { get; set; } = "";
        public string FamilyTypeName { get; set; } = "";
        public string ElementIdDisplay { get; set; } = "";
        public ElementId ExampleElementId { get; set; } = ElementId.InvalidElementId;

        /// <summary>
        /// Always "fail": this report lists only family types that are missing a
        /// tag, so every row is a failure by construction. Exposed as a property
        /// rather than hardcoded in the view so the status column can bind to it
        /// like every other report's does.
        /// </summary>
        public string StatusKind => "fail";
    }

    /// <summary>
    /// Shown non-modally (Show(), not ShowDialog()) so its "Select" action can
    /// change Revit's live selection/active view while the report stays open.
    /// </summary>
    internal class TagReportWindow : Window
    {
        private readonly UIDocument _uiDoc;
        private readonly List<TagReportRow> _rows;

        public TagReportWindow(
            UIDocument uiDoc,
            List<TagReportRow> failingRows,
            int viewsScanned,
            int assembliesScanned,
            int familyTypesChecked,
            string docTitle)
        {
            _uiDoc = uiDoc;
            _rows = failingRows;

            Title = "Assembly Tag Completeness";
            Width = 900;
            Height = 620;
            MinWidth = 680;
            MinHeight = 420;
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
            // logo, against TitleBar's 44px on Surface2 with a 20px one. The Tag
            // Reviewer picker in this file keeps TitleBar -- only reports move.
            var titleBar = AvControls.ReportHeaderBar("Assembly Tag Completeness");
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
            for (int c = 0; c < 4; c++) statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var stats = new (string v, string k, string cls)[]
            {
                (viewsScanned.ToString(), "Views scanned", ""),
                (assembliesScanned.ToString(), "Assemblies scanned", ""),
                (familyTypesChecked.ToString(), "Family types checked", ""),
                (failingRows.Count.ToString(), "Failing", failingRows.Count > 0 ? "fail" : "ok"),
            };
            for (int i = 0; i < stats.Length; i++)
            {
                var tile = AvControls.StatTile(stats[i].v, stats[i].k, stats[i].cls);
                tile.Margin = new WpfThickness(i == 0 ? 0 : 6, 0, i == stats.Length - 1 ? 0 : 6, 0);
                WpfGrid.SetColumn(tile, i);
                statGrid.Children.Add(tile);
            }
            body.Children.Add(statGrid);

            if (failingRows.Count == 0)
            {
                body.Children.Add(new WpfTextBlock
                {
                    Text = "Every checked family type is tagged in its scanned view. Nothing to fix.",
                    FontFamily = AvTokens.Ui,
                    FontSize = 13,
                    Foreground = AvTokens.B(AvTokens.TextDim)
                });
            }
            else
            {
                var tableWrap = new WpfBorder
                {
                    BorderBrush = AvTokens.B(AvTokens.Border),
                    BorderThickness = new WpfThickness(1),
                    CornerRadius = new CornerRadius(5),
                    ClipToBounds = true,
                    Background = AvTokens.B(AvTokens.Surface)
                };
                tableWrap.Child = BuildResultsGrid(failingRows);
                body.Children.Add(tableWrap);
            }

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

        private WpfDataGrid BuildResultsGrid(List<TagReportRow> rows)
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
                MaxHeight = 400
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
            // nowhere, so setting it here did nothing. These four text columns
            // carry no ElementStyle at all, which made this grid the clean proof
            // that the failure was never an ElementStyle override.
            cellStyle.Setters.Add(new System.Windows.Setter(WpfDataGridCell.TemplateProperty, AvControls.DataGridCellTemplate()));
            grid.CellStyle = cellStyle;

            grid.Columns.Add(new WpfDataGridTextColumn { Header = "VIEW", Binding = new System.Windows.Data.Binding("ViewName"), Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Star) });
            grid.Columns.Add(new WpfDataGridTextColumn { Header = "ASSEMBLY", Binding = new System.Windows.Data.Binding("AssemblyName"), Width = new System.Windows.Controls.DataGridLength(1.4, System.Windows.Controls.DataGridLengthUnitType.Star) });
            grid.Columns.Add(new WpfDataGridTextColumn { Header = "FAMILY TYPE", Binding = new System.Windows.Data.Binding("FamilyTypeName"), Width = new System.Windows.Controls.DataGridLength(1.6, System.Windows.Controls.DataGridLengthUnitType.Star) });
            grid.Columns.Add(new WpfDataGridTextColumn { Header = "ELEMENT ID", Binding = new System.Windows.Data.Binding("ElementIdDisplay"), Width = new System.Windows.Controls.DataGridLength(0.8, System.Windows.Controls.DataGridLengthUnitType.Star) });

            // Every row shown is already a failure -- the pill is decorative/
            // consistent labeling here, not tied to any pass/fail branching.
            var statusCol = new WpfDataGridTemplateColumn { Header = "STATUS", Width = System.Windows.Controls.DataGridLength.Auto, MinWidth = 90 };
            statusCol.CellTemplate = AvControls.StatusPillTemplate();
            grid.Columns.Add(statusCol);

            var selectCol = new WpfDataGridTemplateColumn { Header = "", Width = System.Windows.Controls.DataGridLength.Auto, MinWidth = 70 };
            selectCol.CellTemplate = BuildSelectButtonTemplate();
            grid.Columns.Add(selectCol);

            return grid;
        }

        // BuildStatusPillTemplate is gone: it wrapped a ContentControl and filled
        // it from a Loaded handler, because StatusPill returns a Border and a
        // DataTemplate cannot return one directly. AvControls.StatusPillTemplate
        // solves that once, and binds the kind from the row rather than assigning
        // a hardcoded "fail" from an event handler.

        private DataTemplate BuildSelectButtonTemplate()
        {
            var template = new DataTemplate();
            var factory = new FrameworkElementFactory(typeof(WpfButton));
            factory.SetValue(WpfButton.ContentProperty, "Select");
            factory.SetValue(WpfButton.FontFamilyProperty, AvTokens.Ui);
            factory.SetValue(WpfButton.FontSizeProperty, 11.5);
            factory.SetValue(WpfButton.FontWeightProperty, WpfFontWeights.SemiBold);
            factory.SetValue(WpfButton.ForegroundProperty, AvTokens.B(AvTokens.TextDim));
            factory.SetValue(WpfButton.BackgroundProperty, AvTokens.B(AvTokens.Surface2));
            // No border: StatusPill's own Border has none either (measured:
            // adding even a 1px border here made the button 27.297px tall vs.
            // the pill's 25.297px -- a 2px mismatch from the border alone).
            // Padding/CornerRadius below match StatusPill's exactly.
            factory.SetValue(WpfButton.PaddingProperty, new WpfThickness(8, 5, 10, 5));
            factory.SetValue(WpfButton.CursorProperty, System.Windows.Input.Cursors.Hand);
            // The chip's own template centres its label, but the Button element
            // itself defaulted to Stretch and grew to fill the row, so it stood
            // taller than the pill beside it. Match the pill, which sets the
            // same on its root.
            factory.SetValue(System.Windows.FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.SetValue(WpfButton.TemplateProperty, BuildChipButtonTemplate());
            factory.AddHandler(WpfButton.ClickEvent, new RoutedEventHandler(OnSelectClick));
            template.VisualTree = factory;
            return template;
        }

        /// <summary>
        /// Pixel-matched to AvControls.StatusPill's own Border: same
        /// CornerRadius (6), same Padding (8,5,10,5), and -- measured via
        /// FormattedText -- no BorderThickness at all, since even a 1px
        /// border adds 2px to the auto-sized height (27.297px vs. the pill's
        /// 25.297px for identical 11.5pt SemiBold Segoe UI content). Kept
        /// local rather than changing the shared Core FieldButtonTemplate,
        /// which does have a border and is used elsewhere for that look.
        /// </summary>
        private static System.Windows.Controls.ControlTemplate BuildChipButtonTemplate()
        {
            var tpl = new System.Windows.Controls.ControlTemplate(typeof(WpfButton));
            var bd = new FrameworkElementFactory(typeof(WpfBorder));
            bd.Name = "bd";
            bd.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(6));
            bd.SetValue(WpfBorder.BackgroundProperty, AvTokens.B(AvTokens.Surface2));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, WpfHAlign.Center);
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
            trigger.Setters.Add(new Setter(WpfBorder.BackgroundProperty, AvTokens.B(AvTokens.AccentTint), "bd"));
            tpl.Triggers.Add(trigger);
            return tpl;
        }

        private void OnSelectClick(object sender, RoutedEventArgs e)
        {
            if (sender is WpfButton btn && btn.DataContext is TagReportRow row && row.ExampleElementId != ElementId.InvalidElementId)
            {
                try
                {
                    _uiDoc.ShowElements(row.ExampleElementId);
                    _uiDoc.Selection.SetElementIds(new List<ElementId> { row.ExampleElementId });
                }
                catch (Exception ex)
                {
                    AvControls.ShowAlert("Select in View", $"Could not select element {row.ExampleElementId.Value}:\n{ex.Message}", AlertSeverity.Error);
                }
            }
        }

        private void OnCopyToClipboard(WpfButton sourceBtn)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("View\tAssembly\tFamily Type\tElement ID\tStatus");
            foreach (var r in _rows)
                sb.AppendLine(string.Join("\t", r.ViewName, r.AssemblyName, r.FamilyTypeName, r.ElementIdDisplay, "Failed"));
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
    //  COMMAND
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
                var scannableViews = TagCompletenessScanner.GetScannableViews(doc);
                if (scannableViews.Count == 0)
                {
                    AvControls.ShowAlert("Assembly Tag Completeness",
                        "No scannable views placed on sheets were found in this project. This tool checks " +
                        "tags in plans, sections, elevations, detail and 3D views -- including an " +
                        "assembly's own generated views -- but only where the view has been placed " +
                        "on a sheet.", AlertSeverity.Warning);
                    return Result.Cancelled;
                }

                var picker = new AssemblyViewPickerWindow(doc, scannableViews);
                picker.ShowDialog();

                if (picker.SelectedViews.Count == 0)
                    return Result.Cancelled;

                // Indexed once for the whole run: the per-view scan then does key
                // lookups instead of re-scanning every tag in the document.
                var tagsByOwnerView = TagCompletenessScanner
                    .GetAllIndependentTags(doc)
                    .ToLookup(t => t.OwnerViewId);
                var failingRows = new List<TagReportRow>();
                int assembliesScanned = 0;
                int familyTypesChecked = 0;

                foreach (var view in picker.SelectedViews)
                {
                    var assemblies = TagCompletenessScanner.GetAssembliesInView(doc, view);
                    if (assemblies.Count == 0) continue;

                    var tagScan = TagCompletenessScanner.BuildTaggedTypeIdsForViews(
                        doc, tagsByOwnerView,
                        TagCompletenessScanner.GetTagScopeForView(doc, view));

                    foreach (var assembly in assemblies.OrderBy(a => a.AssemblyTypeName, StringComparer.OrdinalIgnoreCase))
                    {
                        var scan = TagCompletenessScanner.ScanAssembly(doc, assembly, tagScan);
                        assembliesScanned++;
                        familyTypesChecked += scan.DistinctFamilyTypeCount;

                        foreach (var failing in scan.Failing.OrderBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase))
                        {
                            failingRows.Add(new TagReportRow
                            {
                                ViewName = view.Name,
                                AssemblyName = assembly.AssemblyTypeName,
                                FamilyTypeName = failing.TypeName,
                                ElementIdDisplay = failing.ExampleUntaggedMemberId.Value.ToString(),
                                ExampleElementId = failing.ExampleUntaggedMemberId
                            });
                        }
                    }
                }

                var report = new TagReportWindow(
                    uiDoc,
                    failingRows,
                    viewsScanned: picker.SelectedViews.Count,
                    assembliesScanned: assembliesScanned,
                    familyTypesChecked: familyTypesChecked,
                    docTitle: doc.Title);
                report.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AvControls.ShowAlert("Assembly Tag Completeness – Error", ex.Message, AlertSeverity.Error);
                return Result.Failed;
            }
        }
    }
}
