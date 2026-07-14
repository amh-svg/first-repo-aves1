// =============================================================================
// AVES Vendor Drawing Automation — Single-File Edition
// Target: Autodesk Revit 2025 (.NET 8) | C# 12
// Namespace: AVES.Cmds_PullDownCreateSheets
// =============================================================================
//
// UI REDESIGN — AVES Refined Native / AVES Red
// Changes applied:
//   • AvesUiHelper static class added (buttons, section headers, brand tile,
//     status pill, stat tile, hover wiring).
//   • FormHelpers.SectionHeader replaced with accent-stripe variant.
//   • NumericRow TextBox styled with token borders, invalid = AccentTint bg.
//   • LayoutSettingsDialogBase: window backdrop, Segoe UI, UseLayoutRounding,
//     interior header strip (brand tile + title), footer strip with
//     Ghost/Secondary/Primary buttons replacing old inline buttons.
//   • StatusText repositioned to right column, styled Text Faint 10pt.
//   • ReportWindow: backdrop + font, interior header strip, stat tiles,
//     status pill, styled body TextBox, token footer with Secondary + Primary.
//   • TemplatePickerWindow: minimal token styling.
//   • LayoutPreviewCanvas colour constants updated to new palette.
//   No business logic, Revit API calls, settings models, or command flow changed.
//
// CONTENTS (in order)
//   1. AvesUiHelper            — design-system factory methods
//   2. Shared core             — RevitConstants, GeometryUtils, ViewUtils,
//                                TitleblockUtils, NameRegistry, FailureHandling,
//                                Reporting, TemplatePickerWindow, TemplatePicker.
//   3. Settings models         — VendorDrawingsSettings, AssemblyDrawingsSettings,
//                                MTQDrawingsSettings
//   4. Persistence             — SettingsStore
//   5. UI building blocks      — NumericRow, BoolRow, FormHelpers,
//                                LayoutPreviewCanvas, dialog base, three dialogs.
//   6. SettingsGate
//   7. Commands                — Cmd_VendorDrawings, Cmd_AssemblyDrawings,
//                                Cmd_MTQDrawings.
// =============================================================================

// Autodesk
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using AVES.Core;
// File dialogs
using Microsoft.Win32;
// System
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
// WPF
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
// ---------------------------------------------------------------------------
// Type aliases — resolve the WPF/Revit/System name collisions.
// ---------------------------------------------------------------------------
using Color = System.Windows.Media.Color;
using Ellipse = System.Windows.Shapes.Ellipse;
using Grid = System.Windows.Controls.Grid;
using Line = System.Windows.Shapes.Line;
using Panel = System.Windows.Controls.Panel;
using Path = System.IO.Path;
using Rectangle = System.Windows.Shapes.Rectangle;
using RevitException = Autodesk.Revit.Exceptions.ApplicationException;
using TextBox = System.Windows.Controls.TextBox;
using Transform = Autodesk.Revit.DB.Transform;
using WpfControl = System.Windows.Controls.Control;

namespace AVES.Cmds_PullDownCreateSheets;


// =============================================================================
// 1. AVES UI HELPER  — design-system factory methods
// =============================================================================

// Tokens, fonts, ApplyHover, the brand logo, HeaderStrip, SectionHeader,
// PrimaryButton/SecondaryButton/GhostButton and StatTile all routed to
// AVES.Core (AvTokens/AvControls) during the Phase 3 migration -- verified
// byte-for-byte/pixel-for-pixel identical first. StatusPill stays local: its
// warn label ("Completed with errors") and outer Border's missing
// VerticalAlignment differ from AvControls.StatusPill, and per the
// "freeze behavior" rule that's flagged for a later decision, not silently
// resolved here.
internal static class AvesUiHelper
{
    // ── Status pill — coloured dot + label ───────────────────────────────────
    public static Border StatusPill(string kind)
    {
        (string bg, string fg, string label) = kind switch
        {
            "ok" => (AvTokens.OkBg, AvTokens.OkFg, "Done"),
            "skip" => (AvTokens.SkipBg, AvTokens.SkipFg, "Skipped"),
            "fail" => (AvTokens.FailBg, AvTokens.FailFg, "Failed"),
            "warn" => (AvTokens.WarnBg, AvTokens.WarnFg, "Completed with errors"),
            _ => (AvTokens.InfoBg, AvTokens.InfoFg, "Info"),
        };

        var dot = new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = AvTokens.Br(fg),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var txt = new TextBlock
        {
            Text = label,
            Foreground = AvTokens.Br(fg),
            FontFamily = AvTokens.Ui,
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(txt);

        return new Border
        {
            Background = AvTokens.Br(bg),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 5, 10, 5),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = row,
        };
    }
}

// =============================================================================
// 2. SHARED CORE
// =============================================================================

/// <summary>Internal-feet conversions for common paper sizes and limits.</summary>
internal static class RevitConstants
{
    public static readonly double A3WidthFt =
        UnitUtils.ConvertToInternalUnits(420.0, UnitTypeId.Millimeters);
    public static readonly double A3HeightFt =
        UnitUtils.ConvertToInternalUnits(297.0, UnitTypeId.Millimeters);

    public static readonly double A1WidthFt =
        UnitUtils.ConvertToInternalUnits(1189.0, UnitTypeId.Millimeters);
    public static readonly double A1HeightFt =
        UnitUtils.ConvertToInternalUnits(841.0, UnitTypeId.Millimeters);

    public const int MaxScale = 500;
}

/// <summary>Bounding-box, sizing and scale helpers.</summary>
internal static class GeometryUtils
{
    public static BoundingBoxXYZ? Union(BoundingBoxXYZ? a, BoundingBoxXYZ? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return new BoundingBoxXYZ
        {
            Min = new XYZ(
                Math.Min(a.Min.X, b.Min.X),
                Math.Min(a.Min.Y, b.Min.Y),
                Math.Min(a.Min.Z, b.Min.Z)),
            Max = new XYZ(
                Math.Max(a.Max.X, b.Max.X),
                Math.Max(a.Max.Y, b.Max.Y),
                Math.Max(a.Max.Z, b.Max.Z)),
        };
    }

    public static BoundingBoxXYZ? GetAssemblyBoundingBox(Document doc, AssemblyInstance assembly)
    {
        var total = TryGetBoundingBox(assembly);
        foreach (var id in assembly.GetMemberIds())
        {
            if (doc.GetElement(id) is { } el)
                total = Union(total, TryGetBoundingBox(el));
        }
        return total;
    }

    public static BoundingBoxXYZ? GetCombinedBoundingBox(IEnumerable<Element> elements)
    {
        BoundingBoxXYZ? total = null;
        foreach (var el in elements)
            total = Union(total, TryGetBoundingBox(el));
        return total;
    }

    public static (double X, double Y, double Z) GetSize(BoundingBoxXYZ bb) => (
        Math.Abs(bb.Max.X - bb.Min.X),
        Math.Abs(bb.Max.Y - bb.Min.Y),
        Math.Abs(bb.Max.Z - bb.Min.Z));

    public static int PickScale(double modelW, double modelH, double availW, double availH)
    {
        if (availW <= 0 || availH <= 0) return RevitConstants.MaxScale;
        var s = (int)Math.Ceiling(Math.Max(modelW / availW, modelH / availH));
        return Math.Clamp(s < 1 ? 1 : s, 1, RevitConstants.MaxScale);
    }

    private static BoundingBoxXYZ? TryGetBoundingBox(Element el)
    {
        try { return el.get_BoundingBox(null); }
        catch { return null; }
    }
}

/// <summary>View-template, view-family, view-property helpers.</summary>
internal static class ViewUtils
{
    public static IReadOnlyDictionary<string, View> GetTemplates(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate)
            .GroupBy(v => v.Name)
            .ToDictionary(g => g.Key, g => g.First());

    public static View? FindTemplateByPrefix(Document doc, string prefix) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .FirstOrDefault(v => v.IsTemplate &&
                                 v.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static ViewFamilyType? Get3DViewFamilyType(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional);

    public static View3D? Find3DViewByName(Document doc, string name) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(v => !v.IsTemplate &&
                                 v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static ViewSheet? FindSheetByName(Document doc, string name) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static void SetDescription(View view, string text)
    {
        var p = view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION);
        if (p is { IsReadOnly: false }) p.Set(text);
    }

    public static void ApplySectionBox(View3D view, BoundingBoxXYZ? bbox, double padding)
    {
        if (bbox is null || view.IsTemplate) return;

        var sb = new BoundingBoxXYZ
        {
            Min = new XYZ(bbox.Min.X - padding, bbox.Min.Y - padding, bbox.Min.Z - padding),
            Max = new XYZ(bbox.Max.X + padding, bbox.Max.Y + padding, bbox.Max.Z + padding),
            Transform = Transform.Identity,
        };

        view.SetSectionBox(sb);
        view.IsSectionBoxActive = true;
    }

    public static Viewport? PlaceView(Document doc, ViewSheet sheet, View view, XYZ point) =>
        Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id)
            ? Viewport.Create(doc, sheet.Id, view.Id, point)
            : null;

    public static Dictionary<string, View> CreateDetailViews(
        Document doc,
        ElementId assemblyId,
        ElementId templateId,
        string assemblyName,
        NameRegistry viewNames,
        IReadOnlyDictionary<string, AssemblyDetailViewOrientation> orientations)
    {
        var result = new Dictionary<string, View>(orientations.Count);
        foreach (var (key, orientation) in orientations)
        {
            var v = AssemblyViewUtils.CreateDetailSection(doc, assemblyId, orientation);
            v.Name = viewNames.Reserve($"{assemblyName} - {key}");
            v.ViewTemplateId = templateId;
            result[key] = v;
        }
        return result;
    }
}

internal static class TitleblockUtils
{
    public static Element? FirstAvailable(Document doc) =>
        new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsElementType()
            .FirstElement();
}

internal sealed class NameRegistry
{
    private readonly HashSet<string> _names;

    public NameRegistry(IEnumerable<string> seed) =>
        _names = new HashSet<string>(seed, StringComparer.OrdinalIgnoreCase);

    public string Reserve(string baseName)
    {
        if (_names.Add(baseName)) return baseName;
        for (int i = 1; ; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (_names.Add(candidate)) return candidate;
        }
    }
}

internal static class FailureHandling
{
    public static void SuppressWarnings(Transaction tx)
    {
        var opts = tx.GetFailureHandlingOptions();
        opts.SetFailuresPreprocessor(new SilentWarningPreprocessor());
        opts.SetClearAfterRollback(true);
        opts.SetForcedModalHandling(false);
        tx.SetFailureHandlingOptions(opts);
    }

    private sealed class SilentWarningPreprocessor : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            foreach (var f in a.GetFailureMessages())
            {
                if (f.GetSeverity() == FailureSeverity.Warning)
                    a.DeleteWarning(f);
            }
            return FailureProcessingResult.Continue;
        }
    }
}

/// <summary>One row in the results table.</summary>
internal sealed class ReportRow
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string Status { get; init; } = "";   // "ok" | "fail"
    public string Details { get; init; } = "";
}

/// <summary>Shared result reporter used by all three commands.</summary>
internal static class Reporting
{
    /// <summary>
    /// Call from commands: pass structured tuples so the window can build a table.
    /// Each entry is (assemblyName, errorMessage) — errorMessage is empty on success.
    /// </summary>
    public static void ShowResult(
        string title,
        int ok, int bad,
        IReadOnlyList<string> failLog,
        IntPtr revitHandle = default)
    {
        // Build structured row list. failLog only contains failures; we reconstruct
        // the succeeded count as rows without detail.
        var rows = new List<ReportRow>();
        int idx = 1;

        // Parse fail entries — original format: "  • AssemblyName: error message"
        var failParsed = failLog
            .Select(line =>
            {
                var trimmed = line.TrimStart(' ', '•').Trim();
                var colon = trimmed.IndexOf(':');
                return colon > 0
                    ? (Name: trimmed[..colon].Trim(), Error: trimmed[(colon + 1)..].Trim())
                    : (Name: trimmed, Error: "Unknown error");
            })
            .ToList();

        // Add succeeded rows (anonymous — we only know the count, not the names)
        for (int i = 0; i < ok; i++)
            rows.Add(new ReportRow
            {
                Index = idx++,
                Name = ok == 1 ? "Assembly" : $"Assembly {i + 1}",
                Status = "ok",
                Details = "",
            });

        // Add failed rows
        foreach (var (name, error) in failParsed)
            rows.Add(new ReportRow
            {
                Index = idx++,
                Name = name,
                Status = "fail",
                Details = error,
            });

        new ReportWindow(title, rows, ok, bad, revitHandle).ShowDialog();
    }

    /// <summary>
    /// Preferred overload: commands pass named tuples directly for accurate row names.
    /// </summary>
    public static void ShowResult(
        string title,
        IReadOnlyList<(string Name, string? Error)> results,
        IntPtr revitHandle = default)
    {
        int ok = results.Count(r => r.Error is null or "");
        int bad = results.Count - ok;

        var rows = results
            .Select((r, i) => new ReportRow
            {
                Index = i + 1,
                Name = r.Name,
                Status = string.IsNullOrEmpty(r.Error) ? "ok" : "fail",
                Details = r.Error ?? "",
            })
            .ToList();

        new ReportWindow(title, rows, ok, bad, revitHandle).ShowDialog();
    }
}

/// <summary>Redesigned report dialog with a DataGrid results table.</summary>
internal sealed class ReportWindow : Window
{
    private readonly IReadOnlyList<ReportRow> _rows;
    private Button _copyBtn = null!;

    public ReportWindow(
        string title,
        IReadOnlyList<ReportRow> rows,
        int ok, int bad,
        IntPtr revitHandle = default)
    {
        _rows = rows;

        Title = title;
        Width = 700;
        Height = 560;
        MinWidth = 560;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        FontFamily = AvTokens.Ui;
        Background = AvTokens.Br(AvTokens.Backdrop);

        if (revitHandle != IntPtr.Zero)
            _ = new WindowInteropHelper(this) { Owner = revitHandle };

        var root = new DockPanel();

        // Header
        var header = AvControls.HeaderStrip(title);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // Footer
        var footer = BuildFooter(ok, bad);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // Body
        root.Children.Add(BuildBody(ok, bad));
        Content = root;
    }

    // ── Body ─────────────────────────────────────────────────────────────────
    private UIElement BuildBody(int ok, int bad)
    {
        var outer = new DockPanel
        {
            Margin = new Thickness(20, 16, 20, 12),
        };

        // ── Top strip: status pill + stat tiles ───────────────────────────────
        var topStrip = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(topStrip, Dock.Top);

        string pillKind = bad == 0 ? "ok" : (ok == 0 ? "fail" : "warn");
        var pill = AvesUiHelper.StatusPill(pillKind);
        pill.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(pill, Dock.Left);
        topStrip.Children.Add(pill);

        // Stat tiles — right side of the top strip
        var tiles = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Border MakeTile(string val, string lbl, string accent)
        {
            var t = AvControls.StatTile(val, lbl, accent);
            t.Margin = new Thickness(8, 0, 0, 0);
            return t;
        }

        tiles.Children.Add(MakeTile(ok.ToString(), "Succeeded", ok > 0 ? "ok" : ""));
        tiles.Children.Add(MakeTile(bad.ToString(), "Failed", bad > 0 ? "fail" : ""));
        tiles.Children.Add(MakeTile((ok + bad).ToString(), "Total", ""));

        topStrip.Children.Add(tiles);
        outer.Children.Add(topStrip);
        DockPanel.SetDock(topStrip, Dock.Top);

        // ── Section label ─────────────────────────────────────────────────────
        var sectionLabel = new TextBlock
        {
            Text = "RESULTS",
            FontFamily = AvTokens.Ui,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = AvTokens.Br(AvTokens.TextFaint),
            Margin = new Thickness(0, 0, 0, 6),
        };
        DockPanel.SetDock(sectionLabel, Dock.Top);
        outer.Children.Add(sectionLabel);

        // ── DataGrid ─────────────────────────────────────────────────────────
        var grid = BuildDataGrid();
        outer.Children.Add(grid);   // LastChildFill — fills remaining height

        return outer;
    }

    private DataGrid BuildDataGrid()
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
            AlternatingRowBackground = AvTokens.Br(AvTokens.Surface2),
            AlternationCount = 2,
            RowHeight = 38,
            CanUserReorderColumns = false,
            CanUserResizeRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        // Apply column header style via XAML
        dg.Resources.Add(
            typeof(DataGridColumnHeader),
            BuildColumnHeaderStyle());

        // Apply cell style
        dg.CellStyle = BuildCellStyle();

        // ── Column: # ─────────────────────────────────────────────────────────
        dg.Columns.Add(new DataGridTextColumn
        {
            Header = "#",
            Binding = new System.Windows.Data.Binding(nameof(ReportRow.Index)),
            Width = new DataGridLength(36),
            ElementStyle = BuildMonoCellStyle(AvTokens.TextFaint, TextAlignment.Center),
        });

        // ── Column: Assembly Name ─────────────────────────────────────────────
        dg.Columns.Add(new DataGridTextColumn
        {
            Header = "ASSEMBLY",
            Binding = new System.Windows.Data.Binding(nameof(ReportRow.Name)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            ElementStyle = BuildNameCellStyle(),
        });

        // ── Column: Status — pill rendered via inline XAML DataTemplate ──────
        var statusCol = new DataGridTemplateColumn
        {
            Header = "STATUS",
            Width = new DataGridLength(110),
            IsReadOnly = true,
            CellTemplate = BuildStatusCellTemplate(),
        };
        dg.Columns.Add(statusCol);

        // ── Column: Details ───────────────────────────────────────────────────
        dg.Columns.Add(new DataGridTextColumn
        {
            Header = "DETAILS",
            Binding = new System.Windows.Data.Binding(nameof(ReportRow.Details)),
            Width = new DataGridLength(1.4, DataGridLengthUnitType.Star),
            ElementStyle = BuildDetailsCellStyle(),
        });

        dg.ItemsSource = _rows;
        return dg;
    }

    // ── Style helpers ─────────────────────────────────────────────────────────

    private static Style BuildColumnHeaderStyle()
    {
        var s = new Style(typeof(DataGridColumnHeader));
        s.Setters.Add(new Setter(DataGridColumnHeader.FontFamilyProperty, AvTokens.Ui));
        s.Setters.Add(new Setter(DataGridColumnHeader.FontSizeProperty, 10.5));
        s.Setters.Add(new Setter(DataGridColumnHeader.FontWeightProperty, FontWeights.Bold));
        s.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty, AvTokens.Br(AvTokens.TextFaint)));
        s.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty, AvTokens.Br(AvTokens.Surface2)));
        s.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(10, 8, 10, 8)));
        s.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, AvTokens.Br(AvTokens.BorderStrong)));
        s.Setters.Add(new Setter(DataGridColumnHeader.BorderThicknessProperty, new Thickness(0, 0, 0, 1.5)));
        s.Setters.Add(new Setter(DataGridColumnHeader.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        return s;
    }

    private static Style BuildCellStyle()
    {
        var s = new Style(typeof(DataGridCell));
        s.Setters.Add(new Setter(DataGridCell.BorderThicknessProperty, new Thickness(0)));
        s.Setters.Add(new Setter(DataGridCell.FocusVisualStyleProperty, null));
        s.Setters.Add(new Setter(DataGridCell.PaddingProperty, new Thickness(0)));
        return s;
    }

    private static Style BuildMonoCellStyle(string colorHex, TextAlignment align)
    {
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(TextBlock.FontFamilyProperty, AvTokens.Mono));
        s.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.5));
        s.Setters.Add(new Setter(TextBlock.ForegroundProperty, AvTokens.Br(colorHex)));
        s.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, align));
        s.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        s.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(10, 0, 10, 0)));
        return s;
    }

    private static Style BuildNameCellStyle()
    {
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(TextBlock.FontFamilyProperty, AvTokens.Ui));
        s.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.5));
        s.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        s.Setters.Add(new Setter(TextBlock.ForegroundProperty, AvTokens.Br(AvTokens.Text)));
        s.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        s.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(10, 0, 10, 0)));
        s.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        return s;
    }

    private static Style BuildDetailsCellStyle()
    {
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(TextBlock.FontFamilyProperty, AvTokens.Ui));
        s.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.5));
        s.Setters.Add(new Setter(TextBlock.ForegroundProperty, AvTokens.Br(AvTokens.TextFaint)));
        s.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        s.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(10, 0, 10, 0)));
        s.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
        s.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        return s;
    }

    /// <summary>
    /// Builds the Status cell template entirely in inline XAML — no
    /// FrameworkElementFactory, no runtime tree mutation, no recursion.
    /// Renders an AvesUi-style pill (coloured dot + label) using DataTriggers
    /// on the bound Status string ("ok" | "fail" | "warn" | "skip").
    /// </summary>
    private static DataTemplate BuildStatusCellTemplate()
    {
        // Colour definitions matching AvesUiHelper constants
        const string okBg = "#E4F3EA"; const string okFg = "#2C7A52"; const string okLabel = "Done";
        const string failBg = "#FBE3DE"; const string failFg = "#C0381F"; const string failLabel = "Failed";
        const string warnBg = "#FAF0DA"; const string warnFg = "#9A6B12"; const string warnLabel = "Warning";
        const string skipBg = "#EDEFF2"; const string skipFg = "#5B6573"; const string skipLabel = "Skipped";

        // Build inline XAML: a Border (pill shape) containing a horizontal
        // StackPanel with a coloured Ellipse dot + TextBlock label.
        // Default (ok) colours are set on the elements; DataTriggers override
        // for each other status value.
        string xaml =
            "<DataTemplate " +
            "  xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "  xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "  <Border x:Name=\"pill\" CornerRadius=\"6\" Margin=\"10,0,10,0\"" +
            $"    Background=\"{okBg}\" Padding=\"8,5,10,5\"" +
            "    HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\">" +
            "    <StackPanel Orientation=\"Horizontal\">" +
            $"      <Ellipse x:Name=\"dot\" Width=\"7\" Height=\"7\" Fill=\"{okFg}\"" +
            "        VerticalAlignment=\"Center\" Margin=\"0,0,6,0\"/>" +
            $"      <TextBlock x:Name=\"lbl\" Text=\"{okLabel}\" Foreground=\"{okFg}\"" +
            "        FontSize=\"11.5\" FontWeight=\"SemiBold\" VerticalAlignment=\"Center\"/>" +
            "    </StackPanel>" +
            "  </Border>" +
            "  <DataTemplate.Triggers>" +
            // fail
            $"    <DataTrigger Binding=\"{{Binding Status}}\" Value=\"fail\">" +
            $"      <Setter TargetName=\"pill\" Property=\"Background\" Value=\"{failBg}\"/>" +
            $"      <Setter TargetName=\"dot\"  Property=\"Fill\"       Value=\"{failFg}\"/>" +
            $"      <Setter TargetName=\"lbl\"  Property=\"Foreground\" Value=\"{failFg}\"/>" +
            $"      <Setter TargetName=\"lbl\"  Property=\"Text\"       Value=\"{failLabel}\"/>" +
            "    </DataTrigger>" +
            // warn
            $"    <DataTrigger Binding=\"{{Binding Status}}\" Value=\"warn\">" +
            $"      <Setter TargetName=\"pill\" Property=\"Background\" Value=\"{warnBg}\"/>" +
            $"      <Setter TargetName=\"dot\"  Property=\"Fill\"       Value=\"{warnFg}\"/>" +
            $"      <Setter TargetName=\"lbl\"  Property=\"Foreground\" Value=\"{warnFg}\"/>" +
            $"      <Setter TargetName=\"lbl\"  Property=\"Text\"       Value=\"{warnLabel}\"/>" +
            "    </DataTrigger>" +
            // skip
            $"    <DataTrigger Binding=\"{{Binding Status}}\" Value=\"skip\">" +
            $"      <Setter TargetName=\"pill\" Property=\"Background\" Value=\"{skipBg}\"/>" +
            $"      <Setter TargetName=\"dot\"  Property=\"Fill\"       Value=\"{skipFg}\"/>" +
            $"      <Setter TargetName=\"lbl\"  Property=\"Foreground\" Value=\"{skipFg}\"/>" +
            $"      <Setter TargetName=\"lbl\"  Property=\"Text\"       Value=\"{skipLabel}\"/>" +
            "    </DataTrigger>" +
            "  </DataTemplate.Triggers>" +
            "</DataTemplate>";

        return (DataTemplate)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    // ── Footer ────────────────────────────────────────────────────────────────
    private Border BuildFooter(int ok, int bad)
    {
        // Left: summary counts as text
        var summary = new TextBlock
        {
            Text = $"{ok} succeeded · {bad} failed · {ok + bad} total",
            FontFamily = AvTokens.Ui,
            FontSize = 11,
            Foreground = AvTokens.Br(AvTokens.TextFaint),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _copyBtn = AvControls.SecondaryButtonHoverLoaded("Copy as TSV");
        _copyBtn.Margin = new Thickness(0, 0, 8, 0);
        _copyBtn.Click += OnCopy;

        var closeBtn = AvControls.PrimaryButtonHoverLoaded("Close");
        closeBtn.IsDefault = true;
        closeBtn.IsCancel = true;
        closeBtn.Click += (_, _) => Close();

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        btnRow.Children.Add(_copyBtn);
        btnRow.Children.Add(closeBtn);

        var inner = new DockPanel();
        DockPanel.SetDock(summary, Dock.Left);
        inner.Children.Add(summary);
        inner.Children.Add(btnRow);

        return new Border
        {
            Background = AvTokens.Br(AvTokens.Surface2),
            BorderBrush = AvTokens.Br(AvTokens.Border),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 14, 20, 14),
            Child = inner,
        };
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            // TSV format: #\tAssembly\tStatus\tDetails
            var sb = new StringBuilder();
            sb.AppendLine("#\tAssembly\tStatus\tDetails");
            foreach (var r in _rows)
                sb.AppendLine($"{r.Index}\t{r.Name}\t{r.Status}\t{r.Details}");

            Clipboard.SetText(sb.ToString());
            _copyBtn.Content = "Copied!";
            var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(1500) };
            timer.Tick += (_, _) => { _copyBtn.Content = "Copy as TSV"; timer.Stop(); };
            timer.Start();
        }
        catch { }
    }
}


/// <summary>Minimal WPF list picker — styled with AVES tokens.</summary>
internal sealed class TemplatePickerWindow : Window
{
    public string? SelectedTemplateName { get; private set; }
    private readonly ListBox _listBox;
    private readonly List<string> _allNames;

    public TemplatePickerWindow(IReadOnlyList<string> names, string title,
        IntPtr revitHandle = default)
    {
        _allNames = names.ToList();

        Title = title;
        Width = 460;
        Height = 580;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResize;      // allow resize so long lists work
        MinWidth = 340;
        MinHeight = 340;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        FontFamily = AvTokens.Ui;
        Background = AvTokens.Br(AvTokens.Backdrop);

        if (revitHandle != IntPtr.Zero)
            _ = new WindowInteropHelper(this) { Owner = revitHandle };

        var root = new DockPanel();

        // ── Header strip ──────────────────────────────────────────────────────
        var header = AvControls.HeaderStrip(title);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // ── Footer ────────────────────────────────────────────────────────────
        var okBtn = AvControls.PrimaryButtonHoverLoaded("OK");
        okBtn.IsDefault = true;
        okBtn.Margin = new Thickness(0, 0, 8, 0);
        okBtn.Click += (_, _) => Confirm();

        var cancelBtn = AvControls.GhostButtonHoverLoaded("Cancel");
        cancelBtn.IsCancel = true;
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        btnRow.Children.Add(okBtn);
        btnRow.Children.Add(cancelBtn);

        var footer = new Border
        {
            Background = AvTokens.Br(AvTokens.Surface2),
            BorderBrush = AvTokens.Br(AvTokens.Border),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 14, 20, 14),
            Child = btnRow,
        };
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // ── Body — DockPanel so ListBox fills all remaining height ────────────
        var body = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };

        // Hint label
        var hint = new TextBlock
        {
            Text = "Select a template, then click OK.",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = AvTokens.Br(AvTokens.TextDim),
            Margin = new Thickness(0, 0, 0, 10),
        };
        DockPanel.SetDock(hint, Dock.Top);
        body.Children.Add(hint);

        // ── Search box ────────────────────────────────────────────────────────
        // Container: Surface2 bg, 1px BorderStrong border, radius-5 look via
        // inner padding. Icon drawn as a TextBlock glyph (🔍 or plain text).
        var searchIcon = new TextBlock
        {
            Text = "⌕",
            FontSize = 15,
            Foreground = AvTokens.Br(AvTokens.TextFaint),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 6, 0),
        };

        var searchBox = new TextBox
        {
            FontFamily = AvTokens.Ui,
            FontSize = 13,
            Foreground = AvTokens.Br(AvTokens.Text),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0, 6, 8, 6),
        };

        // Placeholder text behaviour
        var placeholder = new TextBlock
        {
            Text = "Filter templates…",
            FontFamily = AvTokens.Ui,
            FontSize = 13,
            Foreground = AvTokens.Br(AvTokens.TextFaint),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            IsHitTestVisible = false,
        };

        // Show/hide placeholder
        searchBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrEmpty(searchBox.Text)
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            FilterList(searchBox.Text);
        };

        // Overlay grid so placeholder sits behind the TextBox
        var searchInner = new Grid();
        searchInner.Children.Add(placeholder);
        searchInner.Children.Add(searchBox);

        var searchRow = new StackPanel { Orientation = Orientation.Horizontal };
        searchRow.Children.Add(searchIcon);
        searchRow.Children.Add(searchInner);
        Grid.SetColumn(searchInner, 0);

        var searchBorder = new Border
        {
            Background = AvTokens.Br(AvTokens.Surface2),
            BorderBrush = AvTokens.Br(AvTokens.BorderStrong),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Margin = new Thickness(0, 0, 0, 8),
            Child = searchRow,
        };

        // Make the TextBox fill the remaining width inside searchRow
        searchInner.HorizontalAlignment = HorizontalAlignment.Stretch;
        searchBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        searchRow.HorizontalAlignment = HorizontalAlignment.Stretch;

        DockPanel.SetDock(searchBorder, Dock.Top);
        body.Children.Add(searchBorder);

        // ── ListBox — fills all remaining space, scrollbar always visible ─────
        _listBox = new ListBox
        {
            ItemsSource = _allNames,
            FontFamily = AvTokens.Ui,
            FontSize = 13,
            Foreground = AvTokens.Br(AvTokens.Text),
            Background = AvTokens.Br(AvTokens.Surface),
            BorderBrush = AvTokens.Br(AvTokens.Border),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        // Attached properties must be set via SetValue, not in the initializer.
        // Vertical scroll always on so mouse-wheel is captured by the ListBox
        // itself rather than bubbling to the window.
        _listBox.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,
            ScrollBarVisibility.Visible);
        _listBox.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty,
            ScrollBarVisibility.Disabled);
        _listBox.MouseDoubleClick += (_, _) => Confirm();

        // LastChildFill = true (default) — ListBox stretches to fill body
        body.Children.Add(_listBox);

        root.Children.Add(body);
        Content = root;

        // Focus the search box immediately so the user can type right away
        Loaded += (_, _) => searchBox.Focus();
    }

    // ── Live filter ───────────────────────────────────────────────────────────
    private void FilterList(string query)
    {
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allNames
            : _allNames
                .Where(n => n.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

        _listBox.ItemsSource = filtered;

        // Re-select first match if the previous selection dropped out
        if (_listBox.SelectedItem is null && filtered.Count > 0)
            _listBox.SelectedIndex = 0;
    }

    private void Confirm()
    {
        if (_listBox.SelectedItem is not string name) return;
        SelectedTemplateName = name;
        DialogResult = true;
        Close();
    }
}

internal static class TemplatePicker
{
    public static View? Pick(IReadOnlyDictionary<string, View> templates, string title,
        IntPtr revitHandle = default)
    {
        var sortedNames = templates.Keys.OrderBy(k => k).ToList();
        var dialog = new TemplatePickerWindow(sortedNames, title, revitHandle);
        var ok = dialog.ShowDialog();
        if (ok != true || dialog.SelectedTemplateName is null) return null;
        return templates[dialog.SelectedTemplateName];
    }

    public static (View? V3d, View? V2d, View? Sched) PickTriple(
        IReadOnlyDictionary<string, View> templates, IntPtr revitHandle = default)
    {
        var v3d = Pick(templates, "Select 3D View Template", revitHandle);
        if (v3d is null) return (null, null, null);
        var v2d = Pick(templates, "Select 2D View Template", revitHandle);
        if (v2d is null) return (null, null, null);
        var sched = Pick(templates, "Select Schedule Template", revitHandle);
        if (sched is null) return (null, null, null);
        return (v3d, v2d, sched);
    }
}


// =============================================================================
// 3. SETTINGS MODELS
// =============================================================================

public interface ICloneableSettings<T> where T : ICloneableSettings<T>
{
    T Clone();
}

public sealed class VendorDrawingsSettings : ICloneableSettings<VendorDrawingsSettings>
{
    public double OffsetXMm { get; set; } = -250.0;
    public double OffsetYMm { get; set; } = 150.0;

    public double View3DOffsetXMm { get; set; } = -84.0;
    public double View3DOffsetYMm { get; set; } = 0.0;

    public double ScheduleOffsetXMm { get; set; } = 125.0;
    public double ScheduleOffsetYMm { get; set; } = 90.0;

    public double ClusterSpacingFactor { get; set; } = 0.20;

    public double LeftPercent { get; set; } = 0.60;
    public double BottomRightReservedXPercent { get; set; } = 0.35;
    public double BottomRightReservedYPercent { get; set; } = 0.35;

    public VendorDrawingsSettings Clone() => (VendorDrawingsSettings)MemberwiseClone();

    public double OffsetXFt() => UnitUtils.ConvertToInternalUnits(OffsetXMm, UnitTypeId.Millimeters);
    public double OffsetYFt() => UnitUtils.ConvertToInternalUnits(OffsetYMm, UnitTypeId.Millimeters);
    public double View3DOffsetXFt() => UnitUtils.ConvertToInternalUnits(View3DOffsetXMm, UnitTypeId.Millimeters);
    public double View3DOffsetYFt() => UnitUtils.ConvertToInternalUnits(View3DOffsetYMm, UnitTypeId.Millimeters);
    public double ScheduleOffsetXFt() => UnitUtils.ConvertToInternalUnits(ScheduleOffsetXMm, UnitTypeId.Millimeters);
    public double ScheduleOffsetYFt() => UnitUtils.ConvertToInternalUnits(ScheduleOffsetYMm, UnitTypeId.Millimeters);
}

public sealed class AssemblyDrawingsSettings : ICloneableSettings<AssemblyDrawingsSettings>
{
    public double AsmViewXMm { get; set; } = -365.76;
    public double AsmViewYMm { get; set; } = 243.84;
    public double BoltViewXMm { get; set; } = 365.76;
    public double BoltViewStartYMm { get; set; } = 243.84;
    public double BoltViewStepYMm { get; set; } = -182.88;
    public double SectionBoxPaddingMm { get; set; } = 60.96;

    public AssemblyDrawingsSettings Clone() => (AssemblyDrawingsSettings)MemberwiseClone();
    public double AsmViewXFt() => UnitUtils.ConvertToInternalUnits(AsmViewXMm, UnitTypeId.Millimeters);
    public double AsmViewYFt() => UnitUtils.ConvertToInternalUnits(AsmViewYMm, UnitTypeId.Millimeters);
    public double BoltViewXFt() => UnitUtils.ConvertToInternalUnits(BoltViewXMm, UnitTypeId.Millimeters);
    public double BoltViewStartYFt() => UnitUtils.ConvertToInternalUnits(BoltViewStartYMm, UnitTypeId.Millimeters);
    public double BoltViewStepYFt() => UnitUtils.ConvertToInternalUnits(BoltViewStepYMm, UnitTypeId.Millimeters);
    public double SectionBoxPaddingFt() => UnitUtils.ConvertToInternalUnits(SectionBoxPaddingMm, UnitTypeId.Millimeters);
}

public sealed class MTQDrawingsSettings : ICloneableSettings<MTQDrawingsSettings>
{
    public double OffsetXMm { get; set; } = -229.0;
    public double OffsetYMm { get; set; } = 400.0;
    public bool ApplyOffsetXTwice { get; set; } = true;
    public double BundleSpacingXMm { get; set; } = 80.0;
    public double BundleSpacingYMm { get; set; } = 175.0;
    public double Views2DOffsetXMm { get; set; } = -70.0;
    public double Views2DOffsetYMm { get; set; } = 0.0;
    public double PartListOffsetXMm { get; set; } = -9.3;
    public double PartListOffsetYMm { get; set; } = 83.6;
    public double View3DOffsetXMm { get; set; } = 0.0;
    public double View3DOffsetYMm { get; set; } = 0.0;

    public MTQDrawingsSettings Clone() => (MTQDrawingsSettings)MemberwiseClone();
    public double OffsetXFt() => UnitUtils.ConvertToInternalUnits(OffsetXMm, UnitTypeId.Millimeters);
    public double OffsetYFt() => UnitUtils.ConvertToInternalUnits(OffsetYMm, UnitTypeId.Millimeters);
    public double BundleSpacingXFt() => UnitUtils.ConvertToInternalUnits(BundleSpacingXMm, UnitTypeId.Millimeters);
    public double BundleSpacingYFt() => UnitUtils.ConvertToInternalUnits(BundleSpacingYMm, UnitTypeId.Millimeters);
    public double Views2DOffsetXFt() => UnitUtils.ConvertToInternalUnits(Views2DOffsetXMm, UnitTypeId.Millimeters);
    public double Views2DOffsetYFt() => UnitUtils.ConvertToInternalUnits(Views2DOffsetYMm, UnitTypeId.Millimeters);
    public double PartListOffsetXFt() => UnitUtils.ConvertToInternalUnits(PartListOffsetXMm, UnitTypeId.Millimeters);
    public double PartListOffsetYFt() => UnitUtils.ConvertToInternalUnits(PartListOffsetYMm, UnitTypeId.Millimeters);
    public double View3DOffsetXFt() => UnitUtils.ConvertToInternalUnits(View3DOffsetXMm, UnitTypeId.Millimeters);
    public double View3DOffsetYFt() => UnitUtils.ConvertToInternalUnits(View3DOffsetYMm, UnitTypeId.Millimeters);
}


// =============================================================================
// 4. PERSISTENCE
// =============================================================================

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AVES", "PulldownCreateSheets");

    public static T LoadOrDefault<T>(string filename) where T : class, new()
    {
        try
        {
            var path = Path.Combine(DefaultFolder, filename);
            if (!File.Exists(path)) return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? new T();
        }
        catch { return new T(); }
    }

    public static void Save<T>(string filename, T settings) where T : class
    {
        Directory.CreateDirectory(DefaultFolder);
        File.WriteAllText(
            Path.Combine(DefaultFolder, filename),
            JsonSerializer.Serialize(settings, JsonOpts));
    }
}


// =============================================================================
// 5. UI BUILDING BLOCKS
// =============================================================================

internal sealed class NumericRow
{
    public StackPanel Container { get; }
    public TextBox TextBox { get; }
    public event Action? ValueChanged;

    public NumericRow(string label, double initial, string suffix, string? tooltip = null)
    {
        Container = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 4),
        };

        var lbl = new TextBlock
        {
            Text = label,
            Width = 215,
            FontFamily = AvTokens.Ui,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = AvTokens.Br(AvTokens.TextDim),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (tooltip != null) lbl.ToolTip = tooltip;

        TextBox = new TextBox
        {
            Width = 82,
            Text = initial.ToString("0.###", CultureInfo.InvariantCulture),
            FontFamily = AvTokens.Ui,
            FontSize = 13,
            Foreground = AvTokens.Br(AvTokens.Text),
            Background = AvTokens.Br(AvTokens.Surface),
            BorderBrush = AvTokens.Br(AvTokens.BorderStrong),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 5, 8, 5),
            HorizontalContentAlignment = HorizontalAlignment.Right,
        };

        // Apply rounded corners via ControlTemplate
        TextBox.Style = BuildTextBoxStyle();

        TextBox.TextChanged += (_, _) =>
        {
            if (IsValid)
            {
                TextBox.Background = AvTokens.Br(AvTokens.Surface);
                TextBox.BorderBrush = AvTokens.Br(AvTokens.BorderStrong);
            }
            else
            {
                TextBox.Background = AvTokens.Br(AvTokens.AccentTint);
                TextBox.BorderBrush = AvTokens.Br(AvTokens.Accent);
            }
            ValueChanged?.Invoke();
        };

        var suffixTb = new TextBlock
        {
            Text = $" {suffix}",
            FontFamily = AvTokens.Ui,
            FontSize = 12,
            Foreground = AvTokens.Br(AvTokens.TextFaint),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            Width = 45,
        };

        Container.Children.Add(lbl);
        Container.Children.Add(TextBox);
        Container.Children.Add(suffixTb);
    }

    public bool IsValid => TryParse(TextBox.Text, out _);

    public double Value
    {
        get => TryParse(TextBox.Text, out var v) ? v : 0;
        set => TextBox.Text = value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static Style BuildTextBoxStyle()
    {
        // Built from inline XAML to avoid FrameworkElementFactory ambiguity
        // with Autodesk.Revit.DB types in this compilation unit.
        const string xaml =
            "<Style TargetType=\"TextBox\" " +
            "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Setter Property=\"Template\">" +
            "<Setter.Value>" +
            "<ControlTemplate TargetType=\"TextBox\">" +
            "<Border CornerRadius=\"5\" " +
            "Background=\"{TemplateBinding Background}\" " +
            "BorderBrush=\"{TemplateBinding BorderBrush}\" " +
            "BorderThickness=\"{TemplateBinding BorderThickness}\">" +
            "<ScrollViewer x:Name=\"PART_ContentHost\" Margin=\"0\"/>" +
            "</Border>" +
            "</ControlTemplate>" +
            "</Setter.Value>" +
            "</Setter>" +
            "</Style>";
        return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
    }

    private static bool TryParse(string? text, out double value)
    {
        var normalized = (text ?? string.Empty).Replace(',', '.');
        return double.TryParse(normalized,
            NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

internal sealed class BoolRow
{
    public StackPanel Container { get; }
    public CheckBox CheckBox { get; }
    public event Action? ValueChanged;

    public BoolRow(string label, bool initial, string? tooltip = null)
    {
        Container = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 5, 0, 5),
        };
        CheckBox = new CheckBox
        {
            Content = label,
            IsChecked = initial,
            FontFamily = AvTokens.Ui,
            FontSize = 13,
            Foreground = AvTokens.Br(AvTokens.Text),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (tooltip != null) CheckBox.ToolTip = tooltip;
        CheckBox.Checked += (_, _) => ValueChanged?.Invoke();
        CheckBox.Unchecked += (_, _) => ValueChanged?.Invoke();
        Container.Children.Add(CheckBox);
    }

    public bool Value
    {
        get => CheckBox.IsChecked == true;
        set => CheckBox.IsChecked = value;
    }
}

/// <summary>Thin wrapper — now delegates to AvControls.SectionHeader.</summary>
internal static class FormHelpers
{
    public static Border SectionHeader(string text) =>
        AvControls.SectionHeader(text);
}

internal sealed class LayoutPreviewCanvas : Canvas
{
    // ── Updated palette — AVES Refined Native tokens ──────────────────────────
    public static readonly SolidColorBrush SheetFill = new(Color.FromRgb(252, 252, 252));
    public static readonly SolidColorBrush SheetStroke = new(Color.FromRgb(229, 230, 234)); // Border
    public static readonly SolidColorBrush TitleBlockFill =
        new(Color.FromRgb(250, 250, 251));                                                    // Surface2

    // View colours use the status/accent palette
    public static readonly SolidColorBrush View3DFill = new(Color.FromRgb(231, 239, 251)); // InfoBg
    public static readonly SolidColorBrush View3DStroke = new(Color.FromRgb(46, 102, 168)); // InfoFg
    public static readonly SolidColorBrush View2DFill = new(Color.FromRgb(228, 243, 234)); // OkBg
    public static readonly SolidColorBrush View2DStroke = new(Color.FromRgb(44, 122, 82)); // OkFg
    public static readonly SolidColorBrush ScheduleFill = new(Color.FromRgb(250, 240, 218));// WarnBg
    public static readonly SolidColorBrush ScheduleStroke = new(Color.FromRgb(154, 107, 18));// WarnFg
    public static readonly SolidColorBrush BoltFill = new(Color.FromRgb(251, 233, 228)); // AccentTint
    public static readonly SolidColorBrush BoltStroke = new(Color.FromRgb(194, 64, 42)); // AccentDeep

    public LayoutPreviewCanvas()
    {
        Background = new SolidColorBrush(Color.FromRgb(247, 248, 250)); // Zebra
        Width = 380;
        Height = 320;
        ClipToBounds = true;
    }

    public void DrawSheetOutline(double x, double y, double w, double h, string label)
    {
        var r = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = SheetFill,
            Stroke = SheetStroke,
            StrokeThickness = 1,
        };
        SetLeft(r, x); SetTop(r, y); Children.Add(r);

        var tb = new Rectangle
        {
            Width = w * 0.30,
            Height = h * 0.18,
            Fill = TitleBlockFill,
            Stroke = SheetStroke,
            StrokeThickness = 0.5,
        };
        SetLeft(tb, x + w - w * 0.30);
        SetTop(tb, y + h - h * 0.18);
        Children.Add(tb);

        Children.Add(NewLabel(label, x + 4, y + 2, 9,
            new SolidColorBrush(Color.FromRgb(139, 144, 153)))); // TextFaint
    }

    public void DrawElement(double cx, double cy, double w, double h,
        Brush fill, Brush stroke, string label)
    {
        w = Math.Max(4, w);
        h = Math.Max(4, h);

        var left = cx - w / 2;
        var top = cy - h / 2;

        var r = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 1.3,
            Opacity = 0.88,
        };
        SetLeft(r, left); SetTop(r, top); Children.Add(r);

        if (w >= 26 && h >= 14)
            Children.Add(NewLabel(label, left + 3, top + 2, 10,
                new SolidColorBrush(Color.FromRgb(31, 35, 41)), FontWeights.SemiBold));
    }

    public void DrawCrosshair(double x, double y)
    {
        Children.Add(new Line
        {
            X1 = x - 4,
            Y1 = y,
            X2 = x + 4,
            Y2 = y,
            Stroke = new SolidColorBrush(Color.FromRgb(212, 214, 220)),
            StrokeThickness = 0.7,
        });
        Children.Add(new Line
        {
            X1 = x,
            Y1 = y - 4,
            X2 = x,
            Y2 = y + 4,
            Stroke = new SolidColorBrush(Color.FromRgb(212, 214, 220)),
            StrokeThickness = 0.7,
        });
    }

    public void DrawCaption(string text, double x, double y, Brush? color = null) =>
        Children.Add(NewLabel(text, x, y, 9,
            color ?? new SolidColorBrush(Color.FromRgb(139, 144, 153))));

    private static TextBlock NewLabel(string text, double x, double y,
        double fontSize, Brush color, FontWeight? weight = null)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontFamily = AvTokens.Ui,
            Foreground = color,
            FontWeight = weight ?? FontWeights.Normal,
        };
        SetLeft(t, x);
        SetTop(t, y);
        return t;
    }
}


// =============================================================================
// Dialog base — AVES Refined Native chrome
// =============================================================================

internal abstract class LayoutSettingsDialogBase<T> : Window
    where T : class, ICloneableSettings<T>, new()
{
    protected T Working;
    public T? Result { get; private set; }

    protected readonly StackPanel ControlsPanel;
    protected readonly LayoutPreviewCanvas Preview;
    protected readonly TextBlock StatusText;

    private readonly List<NumericRow> _numericRows = [];

    protected bool AllNumericRowsValid =>
        _numericRows.TrueForAll(r => r.IsValid);

    protected LayoutSettingsDialogBase(T initial, string title, IntPtr revitHandle)
    {
        Working = initial.Clone();

        Title = title;
        Width = 840;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        FontFamily = AvTokens.Ui;
        Background = AvTokens.Br(AvTokens.Backdrop);

        if (revitHandle != IntPtr.Zero)
            _ = new WindowInteropHelper(this) { Owner = revitHandle };

        var root = new DockPanel();

        // ── Interior header strip ─────────────────────────────────────────────
        var headerStrip = AvControls.HeaderStrip(title);
        DockPanel.SetDock(headerStrip, Dock.Top);
        root.Children.Add(headerStrip);

        // ── Footer strip ──────────────────────────────────────────────────────
        var footerStrip = BuildFooterStrip();
        DockPanel.SetDock(footerStrip, Dock.Bottom);
        root.Children.Add(footerStrip);

        // ── Main content area ─────────────────────────────────────────────────
        var contentBorder = new Border
        {
            Background = AvTokens.Br(AvTokens.Surface),
            Padding = new Thickness(20, 16, 20, 16),
        };

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        split.ColumnDefinitions.Add(new ColumnDefinition
        { Width = new GridLength(1, GridUnitType.Star) });

        // Left — scrollable controls
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        ControlsPanel = new StackPanel();
        scroll.Content = ControlsPanel;
        Grid.SetColumn(scroll, 0);
        split.Children.Add(scroll);

        // Right — preview + status
        var rightCol = new StackPanel();

        var previewLabel = new TextBlock
        {
            Text = "Layout Preview",
            FontFamily = AvTokens.Ui,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(60, 80, 110)),
            Margin = new Thickness(0, 0, 0, 6),
        };
        rightCol.Children.Add(previewLabel);

        Preview = new LayoutPreviewCanvas();
        rightCol.Children.Add(new Border
        {
            BorderBrush = AvTokens.Br(AvTokens.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = Preview,
        });

        var previewCaption = new TextBlock
        {
            Text = "Schematic only — element sizes are approximate; positions reflect your inputs.",
            FontSize = 9,
            FontFamily = AvTokens.Ui,
            Foreground = AvTokens.Br(AvTokens.TextFaint),
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        rightCol.Children.Add(previewCaption);

        // StatusText lives in the right column below the caption
        StatusText = new TextBlock
        {
            FontFamily = AvTokens.Ui,
            FontSize = 10,
            Foreground = AvTokens.Br(AvTokens.TextFaint),
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        rightCol.Children.Add(StatusText);

        Grid.SetColumn(rightCol, 2);
        split.Children.Add(rightCol);

        contentBorder.Child = split;
        root.Children.Add(contentBorder);
        Content = root;
    }

    // ── Footer strip: Ghost Cancel | Secondary Make Default | Primary Run ─────
    private Border BuildFooterStrip()
    {
        var cancelBtn = AvControls.GhostButtonHoverLoaded("Cancel");
        cancelBtn.IsCancel = true;
        cancelBtn.Margin = new Thickness(0, 0, 10, 0);
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };

        var makeDefaultBtn = AvControls.SecondaryButtonHoverLoaded("Make Default");
        makeDefaultBtn.Margin = new Thickness(0, 0, 10, 0);
        makeDefaultBtn.Click += (_, _) =>
        {
            if (!AllNumericRowsValid)
            {
                MessageBox.Show(this,
                    "One or more values are not valid numbers. Fix the highlighted fields before saving.",
                    "Invalid input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ApplyValues();
            SaveDefault();
            Preview.Children.Clear();
            RefreshPreview();
            StatusText.Text = "Saved as new default. Preview updated.";
        };

        var runBtn = AvControls.PrimaryButtonHoverLoaded("Run");
        runBtn.IsDefault = true;
        runBtn.Click += (_, _) =>
        {
            if (!AllNumericRowsValid)
            {
                MessageBox.Show(this,
                    "One or more values are not valid numbers. Fix the highlighted fields before running.",
                    "Invalid input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ApplyValues();
            Result = Working;
            DialogResult = true;
            Close();
        };

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(makeDefaultBtn);
        btnRow.Children.Add(runBtn);

        return new Border
        {
            Background = AvTokens.Br(AvTokens.Surface2),
            BorderBrush = AvTokens.Br(AvTokens.Border),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 14, 20, 14),
            Child = btnRow,
        };
    }

    protected abstract void SaveDefault();
    protected abstract void BuildControls();
    protected abstract void ApplyValues();
    protected abstract void RefreshPreview();

    protected void RebuildAll()
    {
        _numericRows.Clear();
        ControlsPanel.Children.Clear();
        BuildControls();
        Preview.Children.Clear();
        RefreshPreview();
    }

    protected NumericRow Row(string label, double initial, string suffix,
        string? tooltip = null)
    {
        var row = new NumericRow(label, initial, suffix, tooltip);
        _numericRows.Add(row);
        row.ValueChanged += () =>
        {
            if (!AllNumericRowsValid) return;
            ApplyValues();
            Preview.Children.Clear();
            RefreshPreview();
        };
        ControlsPanel.Children.Add(row.Container);
        return row;
    }

    protected BoolRow CheckRow(string label, bool initial, string? tooltip = null)
    {
        var row = new BoolRow(label, initial, tooltip);
        row.ValueChanged += () =>
        {
            ApplyValues();
            Preview.Children.Clear();
            RefreshPreview();
        };
        ControlsPanel.Children.Add(row.Container);
        return row;
    }
}


// =============================================================================
// Button 1 dialog — Vendor Drawings
// =============================================================================

internal sealed class VendorDrawingsSettingsDialog
    : LayoutSettingsDialogBase<VendorDrawingsSettings>
{
    private NumericRow _offsetX = null!, _offsetY = null!;
    private NumericRow _v3dX = null!, _v3dY = null!;
    private NumericRow _schedX = null!, _schedY = null!;
    private NumericRow _clusterSpacing = null!;

    public VendorDrawingsSettingsDialog(VendorDrawingsSettings initial, IntPtr revitHandle)
        : base(initial, "Button 1 — Vendor Drawings (A3, two sheets per assembly)", revitHandle)
    {
        RebuildAll();
    }

    protected override void SaveDefault() =>
        SettingsStore.Save("vendor-drawings.json", Working);

    protected override void BuildControls()
    {
        var s = Working;

        ControlsPanel.Children.Add(FormHelpers.SectionHeader("Global Sheet Offset"));
        _offsetX = Row("Offset X", s.OffsetXMm, "mm",
            "Shifts every viewport on every sheet horizontally.");
        _offsetY = Row("Offset Y", s.OffsetYMm, "mm",
            "Shifts every viewport on every sheet vertically.");

        ControlsPanel.Children.Add(FormHelpers.SectionHeader("Sheet 1 — 3D View"));
        _v3dX = Row("3D view offset X", s.View3DOffsetXMm, "mm",
            "X position of the 3D view from sheet centre (excl. Global Offset).");
        _v3dY = Row("3D view offset Y", s.View3DOffsetYMm, "mm",
            "Y position of the 3D view from sheet centre (excl. Global Offset).");

        ControlsPanel.Children.Add(FormHelpers.SectionHeader("Sheet 1 — Schedule"));
        _schedX = Row("Schedule offset X", s.ScheduleOffsetXMm, "mm",
            "X position of the schedule from sheet centre (excl. Global Offset).");
        _schedY = Row("Schedule offset Y", s.ScheduleOffsetYMm, "mm",
            "Y position of the schedule from sheet centre (excl. Global Offset).");

        ControlsPanel.Children.Add(FormHelpers.SectionHeader("Sheet 2 — 6-View Cluster"));
        _clusterSpacing = Row("Cluster spacing factor", s.ClusterSpacingFactor, "× sheet W",
            "Spacing between adjacent views in the First Angle cluster.");
    }

    protected override void ApplyValues()
    {
        Working.OffsetXMm = _offsetX.Value;
        Working.OffsetYMm = _offsetY.Value;
        Working.View3DOffsetXMm = _v3dX.Value;
        Working.View3DOffsetYMm = _v3dY.Value;
        Working.ScheduleOffsetXMm = _schedX.Value;
        Working.ScheduleOffsetYMm = _schedY.Value;
        Working.ClusterSpacingFactor = _clusterSpacing.Value;
    }

    protected override void RefreshPreview()
    {
        var s = Working;

        const double sheetW = 165.0;
        const double sheetH = 117.0;
        const double leftPad = 18.0;

        const double s1Top = 8.0;
        Preview.DrawSheetOutline(leftPad, s1Top, sheetW, sheetH, "Sheet 1 — 3D + Schedule");

        const double s1CenterX = leftPad + sheetW / 2.0;
        const double s1CenterY = s1Top + sheetH / 2.0;
        Preview.DrawCrosshair(s1CenterX, s1CenterY);

        Preview.DrawElement(s1CenterX - 45, s1CenterY, 60.0, 48.0,
            LayoutPreviewCanvas.View3DFill, LayoutPreviewCanvas.View3DStroke, "3D");

        Preview.DrawElement(s1CenterX + 55, s1CenterY - 30, 44.0, 34.0,
            LayoutPreviewCanvas.ScheduleFill, LayoutPreviewCanvas.ScheduleStroke, "Sched");

        const double s2Top = s1Top + sheetH + 16.0;
        Preview.DrawSheetOutline(leftPad, s2Top, sheetW, sheetH, "Sheet 2 — 6-View Cluster");

        const double s2CenterX = leftPad + sheetW / 2.0;
        const double s2CenterY = s2Top + sheetH / 2.0;

        const double fixedSpacing = sheetW * 0.20;
        const double viewSize = 16.0;
        DrawClusterView(s2CenterX, s2CenterY, viewSize, "F");
        DrawClusterView(s2CenterX + fixedSpacing, s2CenterY, viewSize, "B");
        DrawClusterView(s2CenterX - fixedSpacing, s2CenterY, viewSize, "R");
        DrawClusterView(s2CenterX + fixedSpacing * 2, s2CenterY, viewSize, "L");
        DrawClusterView(s2CenterX, s2CenterY - fixedSpacing, viewSize, "T");
        DrawClusterView(s2CenterX, s2CenterY + fixedSpacing, viewSize, "Bt");

        StatusText.Text =
            $"Global offset: {s.OffsetXMm:0.#} mm X, {s.OffsetYMm:0.#} mm Y  |  " +
            $"3D view: {s.View3DOffsetXMm:0.#} mm X, {s.View3DOffsetYMm:0.#} mm Y  |  " +
            $"Schedule: {s.ScheduleOffsetXMm:0.#} mm X, {s.ScheduleOffsetYMm:0.#} mm Y  |  " +
            $"Cluster spacing: {s.ClusterSpacingFactor:0.##} × sheet W";
    }

    private void DrawClusterView(double cx, double cy, double size, string label) =>
        Preview.DrawElement(cx, cy, size, size,
            LayoutPreviewCanvas.View2DFill, LayoutPreviewCanvas.View2DStroke, label);
}


// =============================================================================
// Button 2 dialog — Assembly Drawings
// =============================================================================

internal sealed class AssemblyDrawingsSettingsDialog
    : LayoutSettingsDialogBase<AssemblyDrawingsSettings>
{
    private NumericRow _asmX = null!, _asmY = null!;
    private NumericRow _boltX = null!, _boltStartY = null!, _boltStepY = null!;
    private NumericRow _padding = null!;

    public AssemblyDrawingsSettingsDialog(AssemblyDrawingsSettings initial, IntPtr revitHandle)
        : base(initial, "Button 2 — Assembly Drawings (Bolt Clustering)", revitHandle)
    {
        RebuildAll();
    }

    protected override void SaveDefault() =>
        SettingsStore.Save("assembly-drawings.json", Working);

    protected override void BuildControls()
    {
        var s = Working;

        ControlsPanel.Children.Add(FormHelpers.SectionHeader("Assembly 3D View (left of sheet)"));
        _asmX = Row("Position X", s.AsmViewXMm, "mm",
            "Sheet X coordinate where the assembly 3D view is placed.");
        _asmY = Row("Position Y", s.AsmViewYMm, "mm",
            "Sheet Y coordinate where the assembly 3D view is placed.");

        ControlsPanel.Children.Add(FormHelpers.SectionHeader("Bolt Cluster Views (right of sheet)"));
        _boltX = Row("Position X", s.BoltViewXMm, "mm",
            "Sheet X coordinate for the top bolt cluster view. All bolt views share this X.");
        _boltStartY = Row("Start Y (first view)", s.BoltViewStartYMm, "mm");
        _boltStepY = Row("Step Y (between views)", s.BoltViewStepYMm, "mm",
            "Negative = stack downwards from the first view.");

        ControlsPanel.Children.Add(FormHelpers.SectionHeader("Section Box"));
        _padding = Row("Padding around content", s.SectionBoxPaddingMm, "mm",
            "Extra space added around the bounding box when creating section boxes.");
    }

    protected override void ApplyValues()
    {
        Working.AsmViewXMm = _asmX.Value;
        Working.AsmViewYMm = _asmY.Value;
        Working.BoltViewXMm = _boltX.Value;
        Working.BoltViewStartYMm = _boltStartY.Value;
        Working.BoltViewStepYMm = _boltStepY.Value;
        Working.SectionBoxPaddingMm = _padding.Value;
    }

    protected override void RefreshPreview()
    {
        var s = Working;

        const double cx = 190, cy = 175;
        const double sheetW = 320, sheetH = 240;
        Preview.DrawSheetOutline(cx - sheetW / 2, cy - sheetH / 2, sheetW, sheetH,
            "Sheet (representative)");

        Preview.DrawCrosshair(cx, cy);
        Preview.DrawCaption("origin", cx + 5, cy + 5);

        Preview.DrawElement(cx - 80, cy, 76, 56,
            LayoutPreviewCanvas.View3DFill, LayoutPreviewCanvas.View3DStroke, "ASM 3D");

        const double boltCx = cx + 90;
        const double boltStartCy = cy - 44;
        const double boltStepCy = 44;
        for (int i = 0; i < 3; i++)
        {
            Preview.DrawElement(boltCx, boltStartCy + i * boltStepCy, 56, 32,
                LayoutPreviewCanvas.BoltFill, LayoutPreviewCanvas.BoltStroke, $"Bolt {i + 1}");
        }

        StatusText.Text =
            $"ASM 3D: ({s.AsmViewXMm:0.#}, {s.AsmViewYMm:0.#}) mm  |  " +
            $"Bolt X: {s.BoltViewXMm:0.#} mm, Start Y: {s.BoltViewStartYMm:0.#} mm, " +
            $"Step Y: {s.BoltViewStepYMm:0.#} mm  |  " +
            $"Section box padding: {s.SectionBoxPaddingMm:0.#} mm  |  " +
            $"Three sample bolt boxes shown — actual count depends on each assembly's clusters.";
    }
}


// =============================================================================
// Button 3 dialog — MTQ Drawings
// =============================================================================

internal sealed class MTQDrawingsSettingsDialog
    : LayoutSettingsDialogBase<MTQDrawingsSettings>
{
    private NumericRow _offsetX = null!, _offsetY = null!;
    private NumericRow _bundleX = null!, _bundleY = null!;
    private NumericRow _v2dX = null!, _v2dY = null!;
    private NumericRow _v3dX = null!, _v3dY = null!;
    private NumericRow _plX = null!, _plY = null!;

    public MTQDrawingsSettingsDialog(MTQDrawingsSettings initial, IntPtr revitHandle)
        : base(initial, "Button 3 — MTQ Drawings (A1, single sheet per assembly)", revitHandle)
    {
        RebuildAll();
    }

    protected override void SaveDefault() =>
        SettingsStore.Save("mtq-drawings.json", Working);

    protected override void BuildControls()
    {
        var s = Working;

        ControlsPanel.Children.Add(FormHelpers.SectionHeader("Global Sheet Offset"));
        _offsetX = Row("Offset X", s.OffsetXMm, "mm");
        _offsetY = Row("Offset Y", s.OffsetYMm, "mm");

        ControlsPanel.Children.Add(FormHelpers.SectionHeader("2D Projection Cluster"));
        _bundleX = Row("Bundle spacing X", s.BundleSpacingXMm, "mm",
            "Horizontal gap between FRONT and LEFT/RIGHT views.");
        _bundleY = Row("Bundle spacing Y", s.BundleSpacingYMm, "mm",
            "Vertical gap between FRONT and TOP views.");
        _v2dX = Row("Views 2D offset X", s.Views2DOffsetXMm, "mm");
        _v2dY = Row("Views 2D offset Y", s.Views2DOffsetYMm, "mm");

        ControlsPanel.Children.Add(FormHelpers.SectionHeader("3D View"));
        _v3dX = Row("3D view offset X", s.View3DOffsetXMm, "mm");
        _v3dY = Row("3D view offset Y", s.View3DOffsetYMm, "mm");

        ControlsPanel.Children.Add(FormHelpers.SectionHeader("Part List Schedule"));
        _plX = Row("Part list offset X", s.PartListOffsetXMm, "mm");
        _plY = Row("Part list offset Y", s.PartListOffsetYMm, "mm");
    }

    protected override void ApplyValues()
    {
        Working.OffsetXMm = _offsetX.Value;
        Working.OffsetYMm = _offsetY.Value;
        Working.BundleSpacingXMm = _bundleX.Value;
        Working.BundleSpacingYMm = _bundleY.Value;
        Working.Views2DOffsetXMm = _v2dX.Value;
        Working.Views2DOffsetYMm = _v2dY.Value;
        Working.PartListOffsetXMm = _plX.Value;
        Working.PartListOffsetYMm = _plY.Value;
        Working.View3DOffsetXMm = _v3dX.Value;
        Working.View3DOffsetYMm = _v3dY.Value;
    }

    protected override void RefreshPreview()
    {
        var s = Working;

        const double sheetWpx = 351.0;
        const double sheetHpx = 248.0;
        const double sheetLeft = 10;
        const double sheetTop = 22;

        Preview.DrawSheetOutline(sheetLeft, sheetTop, sheetWpx, sheetHpx,
            "A1 — 1189 × 841 mm");

        const double sheetCx = sheetLeft + sheetWpx / 2;
        const double sheetCy = sheetTop + sheetHpx / 2;
        Preview.DrawCrosshair(sheetCx, sheetCy);

        const double clusterCx = sheetLeft + sheetWpx * 0.20;
        const double clusterCy = sheetCy;
        const double fixedSpX = 36.0;
        const double fixedSpY = 36.0;
        const double sampleViewSize = 32;

        DrawViewBox(clusterCx, clusterCy, sampleViewSize, "F",
            LayoutPreviewCanvas.View2DFill, LayoutPreviewCanvas.View2DStroke);
        DrawViewBox(clusterCx - fixedSpX, clusterCy, sampleViewSize, "R",
            LayoutPreviewCanvas.View2DFill, LayoutPreviewCanvas.View2DStroke);
        DrawViewBox(clusterCx + fixedSpX, clusterCy, sampleViewSize, "L",
            LayoutPreviewCanvas.View2DFill, LayoutPreviewCanvas.View2DStroke);
        DrawViewBox(clusterCx, clusterCy - fixedSpY, sampleViewSize, "T",
            LayoutPreviewCanvas.View2DFill, LayoutPreviewCanvas.View2DStroke);

        Preview.DrawElement(sheetLeft + sheetWpx * 0.45, sheetCy, 44, 80,
            LayoutPreviewCanvas.View3DFill, LayoutPreviewCanvas.View3DStroke, "3D");

        Preview.DrawElement(sheetLeft + sheetWpx * 0.78, sheetCy - 80, 110, 70,
            LayoutPreviewCanvas.ScheduleFill, LayoutPreviewCanvas.ScheduleStroke, "Part List");

        StatusText.Text =
            $"Sheet offset: {s.OffsetXMm:0.#} mm X, {s.OffsetYMm:0.#} mm Y  |  " +
            $"Bundle spacing: {s.BundleSpacingXMm:0.#} mm X, {s.BundleSpacingYMm:0.#} mm Y  |  " +
            $"2D offset: ({s.Views2DOffsetXMm:0.#}, {s.Views2DOffsetYMm:0.#}) mm  |  " +
            $"3D offset: ({s.View3DOffsetXMm:0.#}, {s.View3DOffsetYMm:0.#}) mm  |  " +
            $"Part list offset: ({s.PartListOffsetXMm:0.#}, {s.PartListOffsetYMm:0.#}) mm";
    }

    private void DrawViewBox(double cx, double cy, double size, string label,
        Brush fill, Brush stroke) =>
        Preview.DrawElement(cx, cy, size, size * 0.7, fill, stroke, label);
}


// =============================================================================
// 6. SETTINGS GATE
// =============================================================================

internal static class SettingsGate
{
    public static VendorDrawingsSettings? ShowVendorDrawings(UIApplication uiApp)
    {
        var initial = SettingsStore.LoadOrDefault<VendorDrawingsSettings>("vendor-drawings.json");
        var dlg = new VendorDrawingsSettingsDialog(initial, uiApp.MainWindowHandle);
        if (dlg.ShowDialog() != true) return null;
        var result = dlg.Result;
        if (result is null) return null;
        SettingsStore.Save("vendor-drawings.json", result);
        return result;
    }

    public static AssemblyDrawingsSettings? ShowAssemblyDrawings(UIApplication uiApp)
    {
        var initial = SettingsStore.LoadOrDefault<AssemblyDrawingsSettings>("assembly-drawings.json");
        var dlg = new AssemblyDrawingsSettingsDialog(initial, uiApp.MainWindowHandle);
        if (dlg.ShowDialog() != true) return null;
        var result = dlg.Result;
        if (result is null) return null;
        SettingsStore.Save("assembly-drawings.json", result);
        return result;
    }

    public static MTQDrawingsSettings? ShowMTQDrawings(UIApplication uiApp)
    {
        var initial = SettingsStore.LoadOrDefault<MTQDrawingsSettings>("mtq-drawings.json");
        var dlg = new MTQDrawingsSettingsDialog(initial, uiApp.MainWindowHandle);
        if (dlg.ShowDialog() != true) return null;
        var result = dlg.Result;
        if (result is null) return null;
        SettingsStore.Save("mtq-drawings.json", result);
        return result;
    }
}


// =============================================================================
// 7. BUTTON 1 — Cmd_VendorDrawings (A3, two sheets per assembly)
// =============================================================================

[Transaction(TransactionMode.Manual)]
public sealed class Cmd_VendorDrawings : IExternalCommand
{
    private static readonly IReadOnlyDictionary<string, string> ViewTitles =
        new Dictionary<string, string>
        {
            ["3D"] = "3D View",
            ["TOP"] = "Top View",
            ["BOTTOM"] = "Bottom View",
            ["FRONT"] = "Front View",
            ["BACK"] = "Back View",
            ["LEFT"] = "Left View",
            ["RIGHT"] = "Right View",
        };

    private static readonly IReadOnlyDictionary<string, AssemblyDetailViewOrientation> Orientations =
        new Dictionary<string, AssemblyDetailViewOrientation>
        {
            ["FRONT"] = AssemblyDetailViewOrientation.ElevationFront,
            ["BACK"] = AssemblyDetailViewOrientation.ElevationBack,
            ["LEFT"] = AssemblyDetailViewOrientation.ElevationLeft,
            ["RIGHT"] = AssemblyDetailViewOrientation.ElevationRight,
            ["TOP"] = AssemblyDetailViewOrientation.ElevationTop,
            ["BOTTOM"] = AssemblyDetailViewOrientation.ElevationBottom,
        };

    public Result Execute(ExternalCommandData commandData, ref string message,
        ElementSet elements)
    {
        var uiApp = commandData.Application;
        var doc = uiApp.ActiveUIDocument.Document;

        try
        {
            var templates = ViewUtils.GetTemplates(doc);
            if (templates.Count == 0)
            {
                TaskDialog.Show("Vendor Automation",
                    "No view templates found in this project.");
                return Result.Failed;
            }

            var (tmpl3d, tmpl2d, tmplSched) =
                TemplatePicker.PickTriple(templates, uiApp.MainWindowHandle);
            if (tmpl3d is null || tmpl2d is null || tmplSched is null)
                return Result.Cancelled;

            var assemblies = CollectAssemblies(doc);
            if (assemblies.Count == 0)
            {
                TaskDialog.Show("Vendor Automation",
                    "No assemblies found in this project.");
                return Result.Failed;
            }

            if (TitleblockUtils.FirstAvailable(doc) is not { } titleblock)
            {
                TaskDialog.Show("Vendor Automation",
                    "No titleblock types found in this project.");
                return Result.Failed;
            }

            var settings = SettingsGate.ShowVendorDrawings(uiApp);
            if (settings is null) return Result.Cancelled;

            var viewNames = BuildViewNameRegistry(doc);
            var sheetNames = BuildSheetNameRegistry(doc);

            var results = new List<(string Name, string? Error)>();

            using var tx = new Transaction(doc, "Vendor Sheets - Batch");
            tx.Start();
            FailureHandling.SuppressWarnings(tx);

            foreach (var assembly in assemblies)
            {
                using var sub = new SubTransaction(doc);
                sub.Start();
                try
                {
                    ProcessAssembly(doc, assembly, tmpl3d, tmpl2d, tmplSched,
                        titleblock.Id, settings, viewNames, sheetNames);
                    sub.Commit();
                    results.Add((assembly.Name, null));
                }
                catch (Exception ex)
                {
                    sub.RollBack();
                    results.Add((assembly.Name, ex.Message));
                }
            }

            tx.Commit();

            Reporting.ShowResult("Vendor Drawing Automation", results,
                uiApp.MainWindowHandle);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }

    private void ProcessAssembly(
        Document doc,
        AssemblyInstance assembly,
        View tmpl3d, View tmpl2d, View tmplSched,
        ElementId titleblockId,
        VendorDrawingsSettings settings,
        NameRegistry viewNames, NameRegistry sheetNames)
    {
        var bbox = GeometryUtils.GetAssemblyBoundingBox(doc, assembly)
            ?? throw new InvalidOperationException(
                "Could not compute a bounding box for this assembly.");

        var (sizeX, sizeY, sizeZ) = GeometryUtils.GetSize(bbox);
        var modelW = Math.Max(sizeX, sizeY);
        var modelH = sizeZ;

        var offsetX = settings.OffsetXFt();
        var offsetY = settings.OffsetYFt();

        var view3d = AssemblyViewUtils.Create3DOrthographic(doc, assembly.Id);
        view3d.Name = viewNames.Reserve($"{assembly.Name} - 3D");
        view3d.ViewTemplateId = tmpl3d.Id;

        var schedule = AssemblyViewUtils.CreatePartList(doc, assembly.Id);
        schedule.Name = viewNames.Reserve($"{assembly.Name} - Part List");
        schedule.ViewTemplateId = tmplSched.Id;

        var detailViews = ViewUtils.CreateDetailViews(
            doc, assembly.Id, tmpl2d.Id, assembly.Name, viewNames, Orientations);

        var sheet1 = ViewSheet.Create(doc, titleblockId);
        sheet1.Name = sheetNames.Reserve($"V-ASM_{assembly.Name}_01");

        var sheet2 = ViewSheet.Create(doc, titleblockId);
        sheet2.Name = sheetNames.Reserve($"V-ASM_{assembly.Name}_02");

        var view3dAvailW = RevitConstants.A3WidthFt * settings.LeftPercent
                           - Math.Abs(settings.View3DOffsetXFt());
        var view3dAvailH = RevitConstants.A3HeightFt
                           - Math.Abs(settings.View3DOffsetYFt());

        view3d.Scale = GeometryUtils.PickScale(modelW, modelH, view3dAvailW, view3dAvailH);

        var reservedW = RevitConstants.A3WidthFt * settings.BottomRightReservedXPercent;
        var reservedH = RevitConstants.A3HeightFt * settings.BottomRightReservedYPercent;
        var scale2d = GeometryUtils.PickScale(
            modelW * 3.0, modelH * 3.0,
            RevitConstants.A3WidthFt - reservedW,
            RevitConstants.A3HeightFt - reservedH);
        foreach (var v in detailViews.Values) v.Scale = scale2d;

        ViewUtils.SetDescription(view3d, ViewTitles["3D"]);
        foreach (var (key, view) in detailViews)
        {
            if (ViewTitles.TryGetValue(key, out var title))
                ViewUtils.SetDescription(view, title);
        }

        var view3dPosition = new XYZ(
            settings.View3DOffsetXFt() + offsetX,
            settings.View3DOffsetYFt() + offsetY,
            0);

        var schedulePosition = new XYZ(
            settings.ScheduleOffsetXFt() + offsetX,
            settings.ScheduleOffsetYFt() + offsetY,
            0);

        Viewport.Create(doc, sheet1.Id, view3d.Id, view3dPosition);
        ScheduleSheetInstance.Create(doc, sheet1.Id, schedule.Id, schedulePosition);

        var spacing = RevitConstants.A3WidthFt * settings.ClusterSpacingFactor;
        var clusterCenter = new XYZ(offsetX, offsetY, 0);

        foreach (var (key, (dx, dy)) in BuildFirstAngleClusterPositions(spacing))
        {
            if (!detailViews.TryGetValue(key, out var v)) continue;
            Viewport.Create(doc, sheet2.Id, v.Id,
                new XYZ(clusterCenter.X + dx, clusterCenter.Y + dy, 0));
        }
    }

    private static IReadOnlyDictionary<string, (double X, double Y)>
        BuildFirstAngleClusterPositions(double spacing) =>
        new Dictionary<string, (double, double)>
        {
            ["FRONT"] = (0.0, 0.0),
            ["BACK"] = (spacing, 0.0),
            ["RIGHT"] = (-spacing, 0.0),
            ["LEFT"] = (spacing * 2, 0.0),
            ["TOP"] = (0.0, -spacing),
            ["BOTTOM"] = (0.0, spacing),
        };

    internal static List<AssemblyInstance> CollectAssemblies(Document doc) =>
        new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Assemblies)
            .WhereElementIsNotElementType()
            .OfType<AssemblyInstance>()
            .ToList();

    internal static NameRegistry BuildViewNameRegistry(Document doc) =>
        new(new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Select(v => v.Name));

    internal static NameRegistry BuildSheetNameRegistry(Document doc) =>
        new(new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .Select(s => s.Name));
}


// =============================================================================
// 7. BUTTON 2 — Cmd_AssemblyDrawings (bolt clustering, one sheet per assembly)
// =============================================================================

[Transaction(TransactionMode.Manual)]
public sealed class Cmd_AssemblyDrawings : IExternalCommand
{
    private sealed class AssemblyContext(AssemblyInstance assembly)
    {
        public AssemblyInstance Assembly { get; } = assembly;
        public ElementId Id { get; } = assembly.Id;
        public string Name { get; } = assembly.Name;
        public IReadOnlyList<ElementId> MemberIds { get; set; } = [];
        public BoundingBoxXYZ? BoundingBox { get; set; }
        public IReadOnlyList<Element> BoltElements { get; set; } = [];
        public IReadOnlyList<BoltClusterType> BoltClusterTypes { get; set; } = [];
        public View? Template { get; set; }
    }

    private sealed record BoltInfo(string FamilyName, XYZ Point, XYZ Direction);

    private sealed class BoltClusterType(string familyName, string directionKey)
    {
        public string FamilyName { get; } = familyName;
        public string DirectionKey { get; } = directionKey;
        public List<BoltInfo> Bolts { get; } = [];
        public BoundingBoxXYZ? BoundingBox { get; private set; }

        public void Add(BoltInfo bolt) => Bolts.Add(bolt);

        public void ComputeBounds()
        {
            if (Bolts.Count == 0) return;
            var pts = Bolts.Select(b => b.Point).ToList();
            BoundingBox = new BoundingBoxXYZ
            {
                Min = new XYZ(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z)),
                Max = new XYZ(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z)),
            };
        }
    }

    public Result Execute(ExternalCommandData commandData, ref string message,
        ElementSet elements)
    {
        var uiApp = commandData.Application;
        var doc = uiApp.ActiveUIDocument.Document;

        try
        {
            var assemblies = new FilteredElementCollector(doc)
                .OfClass(typeof(AssemblyInstance))
                .Cast<AssemblyInstance>()
                .ToList();

            if (assemblies.Count == 0)
            {
                TaskDialog.Show("Assembly Bolt Automation",
                    "No assemblies found in the model.");
                return Result.Failed;
            }

            var contexts = AnalyseAssemblies(doc, assemblies);
            ClusterBolts(contexts);

            var viewFamilyType3d = ViewUtils.Get3DViewFamilyType(doc);
            if (viewFamilyType3d is null)
            {
                TaskDialog.Show("Assembly Bolt Automation",
                    "No 3D ViewFamilyType found in this project.");
                return Result.Failed;
            }

            if (TitleblockUtils.FirstAvailable(doc) is not { } titleblock)
            {
                TaskDialog.Show("Assembly Bolt Automation",
                    "No title block found in this project.");
                return Result.Failed;
            }

            var missingTemplates = new List<string>();
            foreach (var ctx in contexts)
            {
                ctx.Template = ViewUtils.FindTemplateByPrefix(doc, $"Assembly {ctx.Name}");
                if (ctx.Template is null) missingTemplates.Add(ctx.Name);
            }

            if (missingTemplates.Count > 0)
            {
                TaskDialog.Show("Assembly Bolt Automation",
                    "Missing view templates for these assemblies " +
                    "(create one named \"Assembly {name}...\" each):\n\n  • " +
                    string.Join("\n  • ", missingTemplates));
                return Result.Failed;
            }

            var settings = SettingsGate.ShowAssemblyDrawings(uiApp);
            if (settings is null) return Result.Cancelled;

            var results = new List<(string Name, string? Error)>();

            using var tx = new Transaction(doc, "Assembly Bolt Drawings - Batch");
            tx.Start();
            FailureHandling.SuppressWarnings(tx);

            foreach (var ctx in contexts)
            {
                using var sub = new SubTransaction(doc);
                sub.Start();
                try
                {
                    CreateViews(doc, ctx, viewFamilyType3d, settings);
                    CreateSheetAndPlaceViews(doc, ctx, titleblock.Id, settings);
                    sub.Commit();
                    results.Add((ctx.Name, null));
                }
                catch (Exception ex)
                {
                    sub.RollBack();
                    results.Add((ctx.Name, ex.Message));
                }
            }

            tx.Commit();

            Reporting.ShowResult("Assembly Bolt Automation", results,
                uiApp.MainWindowHandle);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }

    private static List<AssemblyContext> AnalyseAssemblies(
        Document doc, List<AssemblyInstance> assemblies)
    {
        var contexts = new List<AssemblyContext>(assemblies.Count);

        foreach (var asm in assemblies)
        {
            var ctx = new AssemblyContext(asm)
            {
                MemberIds = asm.GetMemberIds().ToList(),
            };

            var members = ctx.MemberIds
                .Select(doc.GetElement)
                .Where(el => el is not null)
                .ToList()!;

            ctx.BoundingBox = GeometryUtils.GetCombinedBoundingBox(members);
            ctx.BoltElements = members.Where(IsBoltCandidate).ToList();
            contexts.Add(ctx);
        }

        return contexts;
    }

    private static bool IsBoltCandidate(Element element)
    {
        if (element is not FamilyInstance { Category: { } cat }) return false;
        var catId = cat.Id.Value;
        return catId == (long)BuiltInCategory.OST_StructConnections
            || catId == (long)BuiltInCategory.OST_GenericModel;
    }

    private static void ClusterBolts(List<AssemblyContext> contexts)
    {
        foreach (var ctx in contexts)
        {
            var infos = ctx.BoltElements
                .Select(TryBuildBoltInfo)
                .Where(b => b is not null)
                .Cast<BoltInfo>()
                .ToList();

            ctx.BoltClusterTypes = Cluster(infos);
        }
    }

    private static BoltInfo? TryBuildBoltInfo(Element element)
    {
        if (element.Location is not LocationPoint { Point: { } point }) return null;
        if (element is not FamilyInstance fi) return null;

        XYZ? direction = null;
        var facing = fi.FacingOrientation;
        if (facing is not null && facing.GetLength() > 1e-9)
            direction = facing.Normalize();
        else
        {
            var hand = fi.HandOrientation;
            if (hand is not null && hand.GetLength() > 1e-9)
                direction = hand.Normalize();
        }
        if (direction is null) return null;

        var familyName = fi.Symbol?.Family?.Name ?? element.Name;
        return new BoltInfo(familyName, point, direction);
    }

    private static string DirKey(XYZ v, int precision = 2) =>
        $"{Math.Round(v.X, precision)}_{Math.Round(v.Y, precision)}_{Math.Round(v.Z, precision)}";

    private static List<BoltClusterType> Cluster(IEnumerable<BoltInfo> bolts)
    {
        var buckets = new Dictionary<(string FamilyName, string DirKey), BoltClusterType>();

        foreach (var bolt in bolts)
        {
            var key = (bolt.FamilyName, DirKey(bolt.Direction));
            if (!buckets.TryGetValue(key, out var ct))
            {
                ct = new BoltClusterType(key.FamilyName, key.Item2);
                buckets[key] = ct;
            }
            ct.Add(bolt);
        }

        foreach (var ct in buckets.Values) ct.ComputeBounds();
        return [.. buckets.Values];
    }

    private static void CreateViews(
        Document doc,
        AssemblyContext ctx,
        ViewFamilyType vft3d,
        AssemblyDrawingsSettings settings)
    {
        var padding = settings.SectionBoxPaddingFt();
        var asmName = ctx.Name.Replace(" ", "_");
        var template = ctx.Template!;

        var asmViewName = $"ASM_{asmName}_3D";
        var asmView = ViewUtils.Find3DViewByName(doc, asmViewName);
        if (asmView is null)
        {
            asmView = View3D.CreateIsometric(doc, vft3d.Id);
            asmView.Name = asmViewName;
            asmView.ViewTemplateId = template.Id;
        }
        ViewUtils.ApplySectionBox(asmView, ctx.BoundingBox, padding);

        foreach (var ct in ctx.BoltClusterTypes)
        {
            var safeName = ct.FamilyName.Replace(" ", "_");
            var boltViewName = $"ASM_{asmName}_BOLT_{safeName}_{ct.DirectionKey}";
            if (ViewUtils.Find3DViewByName(doc, boltViewName) is not null) continue;

            var boltView = View3D.CreateIsometric(doc, vft3d.Id);
            boltView.Name = boltViewName;
            boltView.ViewTemplateId = template.Id;
            ViewUtils.ApplySectionBox(boltView, ct.BoundingBox, padding);
        }
    }

    private static void CreateSheetAndPlaceViews(
        Document doc,
        AssemblyContext ctx,
        ElementId titleblockId,
        AssemblyDrawingsSettings settings)
    {
        var asmName = ctx.Name.Replace(" ", "_");
        var asmSheetName = $"A-ASM_{asmName}";

        var sheet = ViewUtils.FindSheetByName(doc, asmSheetName)
                    ?? ViewSheet.Create(doc, titleblockId);
        sheet.Name = asmSheetName;

        var asmPosition = new XYZ(settings.AsmViewXFt(), settings.AsmViewYFt(), 0);
        var asmView = ViewUtils.Find3DViewByName(doc, $"ASM_{asmName}_3D");
        if (asmView is not null)
            ViewUtils.PlaceView(doc, sheet, asmView, asmPosition);

        var boltX = settings.BoltViewXFt();
        var y = settings.BoltViewStartYFt();
        var boltStepY = settings.BoltViewStepYFt();

        foreach (var ct in ctx.BoltClusterTypes)
        {
            var safeName = ct.FamilyName.Replace(" ", "_");
            var name = $"ASM_{asmName}_BOLT_{safeName}_{ct.DirectionKey}";
            if (ViewUtils.Find3DViewByName(doc, name) is { } bv)
            {
                ViewUtils.PlaceView(doc, sheet, bv, new XYZ(boltX, y, 0));
                y += boltStepY;
            }
        }
    }
}


// =============================================================================
// 7. BUTTON 3 — Cmd_MTQDrawings (A1, single sheet per assembly)
// =============================================================================

[Transaction(TransactionMode.Manual)]
public sealed class Cmd_MTQDrawings : IExternalCommand
{
    private const double ProjectionSpacingFactor = 0.4;

    private static readonly double LeftHalfWidth = RevitConstants.A1WidthFt * 0.50;
    private static readonly double RightHalfWidth = RevitConstants.A1WidthFt * 0.50;
    private static readonly double Left2DZoneWidth = LeftHalfWidth * 0.30;
    private static readonly double Left3DZoneWidth = LeftHalfWidth * 0.20;

    private static readonly IReadOnlyDictionary<string, string> ViewTitles =
        new Dictionary<string, string>
        {
            ["3D"] = "3D View",
            ["TOP"] = "Top View",
            ["FRONT"] = "Front View",
            ["LEFT"] = "Left View",
            ["RIGHT"] = "Right View",
        };

    private static readonly IReadOnlyDictionary<string, AssemblyDetailViewOrientation> Orientations =
        new Dictionary<string, AssemblyDetailViewOrientation>
        {
            ["FRONT"] = AssemblyDetailViewOrientation.ElevationFront,
            ["LEFT"] = AssemblyDetailViewOrientation.ElevationLeft,
            ["RIGHT"] = AssemblyDetailViewOrientation.ElevationRight,
            ["TOP"] = AssemblyDetailViewOrientation.ElevationTop,
        };

    public Result Execute(ExternalCommandData commandData, ref string message,
        ElementSet elements)
    {
        var uiApp = commandData.Application;
        var doc = uiApp.ActiveUIDocument.Document;

        try
        {
            var templates = ViewUtils.GetTemplates(doc);
            if (templates.Count == 0)
            {
                TaskDialog.Show("Vendor Automation (Single Sheet)",
                    "No view templates found in this project.");
                return Result.Failed;
            }

            var (tmpl3d, tmpl2d, tmplSched) =
                TemplatePicker.PickTriple(templates, uiApp.MainWindowHandle);
            if (tmpl3d is null || tmpl2d is null || tmplSched is null)
                return Result.Cancelled;

            var assemblies = Cmd_VendorDrawings.CollectAssemblies(doc);
            if (assemblies.Count == 0)
            {
                TaskDialog.Show("Vendor Automation (Single Sheet)",
                    "No assemblies found in this project.");
                return Result.Failed;
            }

            if (TitleblockUtils.FirstAvailable(doc) is not { } titleblock)
            {
                TaskDialog.Show("Vendor Automation (Single Sheet)",
                    "No titleblock types found in this project.");
                return Result.Failed;
            }

            var settings = SettingsGate.ShowMTQDrawings(uiApp);
            if (settings is null) return Result.Cancelled;

            var viewNames = Cmd_VendorDrawings.BuildViewNameRegistry(doc);
            var sheetNames = Cmd_VendorDrawings.BuildSheetNameRegistry(doc);

            var results = new List<(string Name, string? Error)>();

            using var tx = new Transaction(doc, "Vendor Sheet (MTQ) - Batch");
            tx.Start();
            FailureHandling.SuppressWarnings(tx);

            foreach (var assembly in assemblies)
            {
                using var sub = new SubTransaction(doc);
                sub.Start();
                try
                {
                    ProcessAssembly(doc, assembly, tmpl3d, tmpl2d, tmplSched,
                        titleblock.Id, settings, viewNames, sheetNames);
                    sub.Commit();
                    results.Add((assembly.Name, null));
                }
                catch (Exception ex)
                {
                    sub.RollBack();
                    results.Add((assembly.Name, ex.Message));
                }
            }

            tx.Commit();

            Reporting.ShowResult("MTQ Drawing Automation", results,
                uiApp.MainWindowHandle);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            return Result.Failed;
        }
    }

    private static void ProcessAssembly(
        Document doc,
        AssemblyInstance assembly,
        View tmpl3d, View tmpl2d, View tmplSched,
        ElementId titleblockId,
        MTQDrawingsSettings settings,
        NameRegistry viewNames, NameRegistry sheetNames)
    {
        var bbox = GeometryUtils.GetAssemblyBoundingBox(doc, assembly)
            ?? throw new InvalidOperationException("No bounding box for assembly.");

        var (sizeX, sizeY, sizeZ) = GeometryUtils.GetSize(bbox);
        var modelW = Math.Max(sizeX, sizeY);
        var modelH = sizeZ;

        var offsetX = settings.OffsetXFt();
        var offsetY = settings.OffsetYFt();

        var view3d = AssemblyViewUtils.Create3DOrthographic(doc, assembly.Id);
        view3d.Name = viewNames.Reserve($"{assembly.Name} - 3D");
        view3d.ViewTemplateId = tmpl3d.Id;

        var schedule = AssemblyViewUtils.CreatePartList(doc, assembly.Id);
        schedule.Name = viewNames.Reserve($"{assembly.Name} - Part List");
        schedule.ViewTemplateId = tmplSched.Id;

        var detailViews = ViewUtils.CreateDetailViews(
            doc, assembly.Id, tmpl2d.Id, assembly.Name, viewNames, Orientations);

        var sheet = ViewSheet.Create(doc, titleblockId);
        sheet.Name = sheetNames.Reserve($"MTQ-{assembly.Name}");

        var scale2d = GeometryUtils.PickScale(
            modelW * 3.0, modelH * 2.0,
            Left2DZoneWidth, RevitConstants.A1HeightFt);
        var scale3d = GeometryUtils.PickScale(
            modelW, modelH,
            Left3DZoneWidth, RevitConstants.A1HeightFt);

        view3d.Scale = scale3d;
        foreach (var v in detailViews.Values) v.Scale = scale2d;

        ViewUtils.SetDescription(view3d, ViewTitles["3D"]);
        foreach (var (key, view) in detailViews)
        {
            if (ViewTitles.TryGetValue(key, out var title))
                ViewUtils.SetDescription(view, title);
        }

        var leftHalfCenterX = -(RevitConstants.A1WidthFt / 4.0) + offsetX;
        var quirkX = settings.ApplyOffsetXTwice ? offsetX : 0.0;
        var left2dZoneCenterX = leftHalfCenterX - (LeftHalfWidth * 0.25) + quirkX;
        var left3dZoneCenterX = leftHalfCenterX + (LeftHalfWidth * 0.05) + quirkX;
        var rightHalfCenterX = (RevitConstants.A1WidthFt / 4.0) + offsetX;

        var defaultSpacing = Left2DZoneWidth * ProjectionSpacingFactor;
        var bundleX = settings.BundleSpacingXFt();
        var bundleY = settings.BundleSpacingYFt();
        var spacingX = bundleX != 0.0 ? bundleX : defaultSpacing;
        var spacingY = bundleY != 0.0 ? bundleY : defaultSpacing;

        var clusterCenterX = left2dZoneCenterX + settings.Views2DOffsetXFt();
        var clusterCenterY = offsetY + settings.Views2DOffsetYFt();

        foreach (var (key, pos) in BuildFirstAngleProjectionPositions(
                     clusterCenterX, clusterCenterY, spacingX, spacingY))
        {
            if (detailViews.TryGetValue(key, out var view))
                Viewport.Create(doc, sheet.Id, view.Id, pos);
        }

        Viewport.Create(doc, sheet.Id, view3d.Id,
            new XYZ(left3dZoneCenterX + settings.View3DOffsetXFt(),
                    offsetY + settings.View3DOffsetYFt(),
                    0));

        var scheduleBaseX = rightHalfCenterX + (RightHalfWidth * -0.10) + offsetX;
        var scheduleBaseY = (RevitConstants.A1HeightFt * 0.40) + offsetY;
        ScheduleSheetInstance.Create(doc, sheet.Id, schedule.Id,
            new XYZ(scheduleBaseX + settings.PartListOffsetXFt(),
                    scheduleBaseY + settings.PartListOffsetYFt(),
                    0));
    }

    private static IReadOnlyDictionary<string, XYZ> BuildFirstAngleProjectionPositions(
        double centerX, double centerY, double spacingX, double spacingY) =>
        new Dictionary<string, XYZ>
        {
            ["FRONT"] = new(centerX, centerY, 0),
            ["RIGHT"] = new(centerX - spacingX, centerY, 0),
            ["LEFT"] = new(centerX + spacingX, centerY, 0),
            ["TOP"] = new(centerX, centerY - spacingY, 0),
        };
}