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

internal static class AvesUiHelper
{
    // ── Colour tokens ─────────────────────────────────────────────────────────
    public const string Accent = "#E64A2E";
    public const string AccentDeep = "#C2402A";
    public const string AccentTint = "#FBE9E4";
    public const string Backdrop = "#F4F4F6";
    public const string Surface = "#FFFFFF";
    public const string Surface2 = "#FAFAFB";
    public const string Border = "#E5E6EA";
    public const string BorderStrong = "#D4D6DC";
    public const string TextPrimary = "#1F2329";
    public const string TextDim = "#5A6069";
    public const string TextFaint = "#8B9099";

    // Status
    public const string OkBg = "#E4F3EA"; public const string OkFg = "#2C7A52";
    public const string SkipBg = "#EDEFF2"; public const string SkipFg = "#5B6573";
    public const string FailBg = "#FBE3DE"; public const string FailFg = "#C0381F";
    public const string WarnBg = "#FAF0DA"; public const string WarnFg = "#9A6B12";
    public const string InfoBg = "#E7EFFB"; public const string InfoFg = "#2E66A8";

    // ── Fonts ─────────────────────────────────────────────────────────────────
    public static readonly FontFamily UiFont = new("Segoe UI");
    public static readonly FontFamily MonoFont = new("Consolas");

    // ── Brush helpers ─────────────────────────────────────────────────────────
    public static SolidColorBrush Br(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    // ── Hover helper — MouseEnter/Leave on a Border ───────────────────────────
    public static void ApplyHover(Border bd, string normalHex, string hoverHex)
    {
        var normal = Br(normalHex);
        var hover = Br(hoverHex);
        bd.MouseEnter += (_, _) => bd.Background = hover;
        bd.MouseLeave += (_, _) => bd.Background = normal;
    }

    // ── Brand logo — Øglænd System / Hilti, embedded as Base64 ────────────
    // 240 chunks × 76 chars, round-trip verified. Joined at first call only.
    private static readonly string[] _logoChunks =
    {
        "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",
        "P5K4gFTpOi+HAAAAAElFTkSuQmCC"
    };

    private static System.Windows.Media.Imaging.BitmapImage? _logoImage;

    private static System.Windows.Media.Imaging.BitmapImage LogoImage()
    {
        if (_logoImage != null) return _logoImage;
        var b64 = string.Concat(_logoChunks);
        var bytes = Convert.FromBase64String(b64);
        var ms = new System.IO.MemoryStream(bytes);
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        _logoImage = bmp;
        return _logoImage;
    }

    // ── Brand tile — renders the embedded logo image ──────────────────────────
    public static System.Windows.Controls.Image BrandTile(double height = 28)
    {
        return new System.Windows.Controls.Image
        {
            Source = LogoImage(),
            Height = height,
            Width = double.NaN,   // auto-width to preserve aspect ratio
            Stretch = System.Windows.Media.Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            SnapsToDevicePixels = true,
        };
    }

    // ── Interior header strip (logo + title text) ─────────────────────────────
    public static Border HeaderStrip(string titleText)
    {
        var logo = BrandTile(28);

        var title = new TextBlock
        {
            Text = titleText,
            FontFamily = UiFont,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Br(TextPrimary),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(logo);
        row.Children.Add(title);

        return new Border
        {
            Background = Br(Surface),
            BorderBrush = Br(Border),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(20, 10, 20, 10),
            Child = row,
        };
    }

    // ── Section header — accent left stripe + uppercase label ────────────────
    public static Border SectionHeader(string text)
    {
        var label = new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontFamily = UiFont,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Br(TextFaint),
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Accent left stripe via inner Border
        var inner = new Border
        {
            BorderBrush = Br(Accent),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Background = Br(Surface2),
            Padding = new Thickness(8, 6, 8, 6),
            Child = label,
        };

        return new Border
        {
            Margin = new Thickness(0, 12, 0, 8),
            BorderBrush = Br(Border),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = inner,
        };
    }

    // ── Primary button — accent fill ─────────────────────────────────────────
    public static Button PrimaryButton(string content)
    {
        var bd = new Border
        {
            Background = Br(Accent),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(16, 9, 16, 9),
            Cursor = System.Windows.Input.Cursors.Hand,
            Child = new ContentPresenter(),
        };
        ApplyHover(bd, Accent, AccentDeep);

        var btn = new Button
        {
            Content = content,
            FontFamily = UiFont,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(16, 9, 16, 9),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        StyleButtonAsPrimary(btn);
        return btn;
    }

    // ── Secondary button — white fill, strong border ─────────────────────────
    public static Button SecondaryButton(string content)
    {
        var btn = new Button
        {
            Content = content,
            FontFamily = UiFont,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Br(TextPrimary),
            Padding = new Thickness(16, 9, 16, 9),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        StyleButtonAsSecondary(btn);
        return btn;
    }

    // ── Ghost button — transparent, dim text (Cancel) ────────────────────────
    public static Button GhostButton(string content)
    {
        var btn = new Button
        {
            Content = content,
            FontFamily = UiFont,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Br(TextDim),
            Padding = new Thickness(14, 9, 14, 9),
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        StyleButtonAsGhost(btn);
        return btn;
    }

    // ── Status pill — coloured dot + label ───────────────────────────────────
    public static Border StatusPill(string kind)
    {
        (string bg, string fg, string label) = kind switch
        {
            "ok" => (OkBg, OkFg, "Done"),
            "skip" => (SkipBg, SkipFg, "Skipped"),
            "fail" => (FailBg, FailFg, "Failed"),
            "warn" => (WarnBg, WarnFg, "Completed with errors"),
            _ => (InfoBg, InfoFg, "Info"),
        };

        var dot = new Ellipse
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
            Child = row,
        };
    }

    // ── Stat tile — large number + caption ───────────────────────────────────
    // accentKind: "" | "ok" | "fail" | "warn" | "accent"
    public static Border StatTile(string value, string label, string accentKind = "")
    {
        string valColor = accentKind switch
        {
            "ok" => OkFg,
            "fail" => FailFg,
            "warn" => WarnFg,
            "accent" => Accent,
            _ => TextPrimary,
        };

        var valTb = new TextBlock
        {
            Text = value,
            FontFamily = MonoFont,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Br(valColor),
            Margin = new Thickness(0, 0, 0, 4),
        };
        var keyTb = new TextBlock
        {
            Text = label,
            FontFamily = UiFont,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = Br(TextFaint),
        };

        var stack = new StackPanel();
        stack.Children.Add(valTb);
        stack.Children.Add(keyTb);

        return new Border
        {
            Background = Br(Surface),
            BorderBrush = Br(Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(14, 12, 14, 12),
            Child = stack,
        };
    }

    // ── Internal styling helpers — no ControlTemplate, no FrameworkElementFactory ─
    // Each button is styled by setting properties directly and wiring
    // MouseEnter/MouseLeave for hover. Corner radius is achieved by wrapping
    // the Button's content in a clipped Border at the call site (see factory
    // methods above, which return a pre-wired Button with its own Border child).

    private static void StyleButtonAsPrimary(Button btn)
    {
        var normalBg = Br(Accent);
        var hoverBg = Br(AccentDeep);
        btn.Foreground = Brushes.White;
        btn.Background = normalBg;
        btn.BorderThickness = new Thickness(0);
        btn.Padding = new Thickness(16, 9, 16, 9);
        btn.Cursor = System.Windows.Input.Cursors.Hand;
        ApplyRoundedStyle(btn, normalBg, hoverBg, null, new Thickness(0));
    }

    private static void StyleButtonAsSecondary(Button btn)
    {
        var normalBg = Br(Surface);
        var hoverBg = Br(Surface2);
        btn.Foreground = Br(TextPrimary);
        btn.Background = normalBg;
        btn.BorderBrush = Br(BorderStrong);
        btn.BorderThickness = new Thickness(1);
        btn.Padding = new Thickness(16, 9, 16, 9);
        btn.Cursor = System.Windows.Input.Cursors.Hand;
        ApplyRoundedStyle(btn, normalBg, hoverBg, Br(BorderStrong), new Thickness(1));
    }

    private static void StyleButtonAsGhost(Button btn)
    {
        var normalBg = new SolidColorBrush(Colors.Transparent);
        var hoverBg = Br(Surface2);
        btn.Foreground = Br(TextDim);
        btn.Background = normalBg;
        btn.BorderThickness = new Thickness(0);
        btn.Padding = new Thickness(14, 9, 14, 9);
        btn.Cursor = System.Windows.Input.Cursors.Hand;
        ApplyRoundedStyle(btn, normalBg, hoverBg, null, new Thickness(0));
    }

    /// <summary>
    /// Gives a Button rounded corners and hover colour by replacing its content
    /// with a Border at Loaded time. No FrameworkElementFactory, no ControlTemplate.
    /// </summary>
    private static void ApplyRoundedStyle(
        Button btn,
        SolidColorBrush normalBg,
        SolidColorBrush hoverBg,
        SolidColorBrush? borderBrush,
        Thickness borderThickness)
    {
        btn.Loaded += (_, _) =>
        {
            // Retrieve the outermost Border the default Button template creates
            // (named "border" in the WPF default template).
            if (btn.Template?.FindName("border", btn) is Border outerBd)
            {
                outerBd.CornerRadius = new CornerRadius(5);
                outerBd.Background = normalBg;
                outerBd.BorderBrush = borderBrush ?? new SolidColorBrush(Colors.Transparent);
                outerBd.BorderThickness = borderThickness;
            }
        };

        btn.MouseEnter += (_, _) => btn.Background = hoverBg;
        btn.MouseLeave += (_, _) => btn.Background = normalBg;
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
        FontFamily = AvesUiHelper.UiFont;
        Background = AvesUiHelper.Br(AvesUiHelper.Backdrop);

        if (revitHandle != IntPtr.Zero)
            _ = new WindowInteropHelper(this) { Owner = revitHandle };

        var root = new DockPanel();

        // Header
        var header = AvesUiHelper.HeaderStrip(title);
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
            var t = AvesUiHelper.StatTile(val, lbl, accent);
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
            FontFamily = AvesUiHelper.UiFont,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = AvesUiHelper.Br(AvesUiHelper.TextFaint),
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
            HorizontalGridLinesBrush = AvesUiHelper.Br(AvesUiHelper.Border),
            BorderBrush = AvesUiHelper.Br(AvesUiHelper.Border),
            BorderThickness = new Thickness(1),
            Background = AvesUiHelper.Br(AvesUiHelper.Surface),
            RowBackground = AvesUiHelper.Br(AvesUiHelper.Surface),
            AlternatingRowBackground = AvesUiHelper.Br(AvesUiHelper.Surface2),
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
            ElementStyle = BuildMonoCellStyle(AvesUiHelper.TextFaint, TextAlignment.Center),
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
        s.Setters.Add(new Setter(DataGridColumnHeader.FontFamilyProperty, AvesUiHelper.UiFont));
        s.Setters.Add(new Setter(DataGridColumnHeader.FontSizeProperty, 10.5));
        s.Setters.Add(new Setter(DataGridColumnHeader.FontWeightProperty, FontWeights.Bold));
        s.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty, AvesUiHelper.Br(AvesUiHelper.TextFaint)));
        s.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty, AvesUiHelper.Br(AvesUiHelper.Surface2)));
        s.Setters.Add(new Setter(DataGridColumnHeader.PaddingProperty, new Thickness(10, 8, 10, 8)));
        s.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, AvesUiHelper.Br(AvesUiHelper.BorderStrong)));
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
        s.Setters.Add(new Setter(TextBlock.FontFamilyProperty, AvesUiHelper.MonoFont));
        s.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.5));
        s.Setters.Add(new Setter(TextBlock.ForegroundProperty, AvesUiHelper.Br(colorHex)));
        s.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, align));
        s.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        s.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(10, 0, 10, 0)));
        return s;
    }

    private static Style BuildNameCellStyle()
    {
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(TextBlock.FontFamilyProperty, AvesUiHelper.UiFont));
        s.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12.5));
        s.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
        s.Setters.Add(new Setter(TextBlock.ForegroundProperty, AvesUiHelper.Br(AvesUiHelper.TextPrimary)));
        s.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        s.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(10, 0, 10, 0)));
        s.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        return s;
    }

    private static Style BuildDetailsCellStyle()
    {
        var s = new Style(typeof(TextBlock));
        s.Setters.Add(new Setter(TextBlock.FontFamilyProperty, AvesUiHelper.UiFont));
        s.Setters.Add(new Setter(TextBlock.FontSizeProperty, 11.5));
        s.Setters.Add(new Setter(TextBlock.ForegroundProperty, AvesUiHelper.Br(AvesUiHelper.TextFaint)));
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
            FontFamily = AvesUiHelper.UiFont,
            FontSize = 11,
            Foreground = AvesUiHelper.Br(AvesUiHelper.TextFaint),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _copyBtn = AvesUiHelper.SecondaryButton("Copy as TSV");
        _copyBtn.Margin = new Thickness(0, 0, 8, 0);
        _copyBtn.Click += OnCopy;

        var closeBtn = AvesUiHelper.PrimaryButton("Close");
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
            Background = AvesUiHelper.Br(AvesUiHelper.Surface2),
            BorderBrush = AvesUiHelper.Br(AvesUiHelper.Border),
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
        FontFamily = AvesUiHelper.UiFont;
        Background = AvesUiHelper.Br(AvesUiHelper.Backdrop);

        if (revitHandle != IntPtr.Zero)
            _ = new WindowInteropHelper(this) { Owner = revitHandle };

        var root = new DockPanel();

        // ── Header strip ──────────────────────────────────────────────────────
        var header = AvesUiHelper.HeaderStrip(title);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // ── Footer ────────────────────────────────────────────────────────────
        var okBtn = AvesUiHelper.PrimaryButton("OK");
        okBtn.IsDefault = true;
        okBtn.Margin = new Thickness(0, 0, 8, 0);
        okBtn.Click += (_, _) => Confirm();

        var cancelBtn = AvesUiHelper.GhostButton("Cancel");
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
            Background = AvesUiHelper.Br(AvesUiHelper.Surface2),
            BorderBrush = AvesUiHelper.Br(AvesUiHelper.Border),
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
            Foreground = AvesUiHelper.Br(AvesUiHelper.TextDim),
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
            Foreground = AvesUiHelper.Br(AvesUiHelper.TextFaint),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 6, 0),
        };

        var searchBox = new TextBox
        {
            FontFamily = AvesUiHelper.UiFont,
            FontSize = 13,
            Foreground = AvesUiHelper.Br(AvesUiHelper.TextPrimary),
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
            FontFamily = AvesUiHelper.UiFont,
            FontSize = 13,
            Foreground = AvesUiHelper.Br(AvesUiHelper.TextFaint),
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
            Background = AvesUiHelper.Br(AvesUiHelper.Surface2),
            BorderBrush = AvesUiHelper.Br(AvesUiHelper.BorderStrong),
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
            FontFamily = AvesUiHelper.UiFont,
            FontSize = 13,
            Foreground = AvesUiHelper.Br(AvesUiHelper.TextPrimary),
            Background = AvesUiHelper.Br(AvesUiHelper.Surface),
            BorderBrush = AvesUiHelper.Br(AvesUiHelper.Border),
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
            FontFamily = AvesUiHelper.UiFont,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = AvesUiHelper.Br(AvesUiHelper.TextDim),
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (tooltip != null) lbl.ToolTip = tooltip;

        TextBox = new TextBox
        {
            Width = 82,
            Text = initial.ToString("0.###", CultureInfo.InvariantCulture),
            FontFamily = AvesUiHelper.UiFont,
            FontSize = 13,
            Foreground = AvesUiHelper.Br(AvesUiHelper.TextPrimary),
            Background = AvesUiHelper.Br(AvesUiHelper.Surface),
            BorderBrush = AvesUiHelper.Br(AvesUiHelper.BorderStrong),
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
                TextBox.Background = AvesUiHelper.Br(AvesUiHelper.Surface);
                TextBox.BorderBrush = AvesUiHelper.Br(AvesUiHelper.BorderStrong);
            }
            else
            {
                TextBox.Background = AvesUiHelper.Br(AvesUiHelper.AccentTint);
                TextBox.BorderBrush = AvesUiHelper.Br(AvesUiHelper.Accent);
            }
            ValueChanged?.Invoke();
        };

        var suffixTb = new TextBlock
        {
            Text = $" {suffix}",
            FontFamily = AvesUiHelper.UiFont,
            FontSize = 12,
            Foreground = AvesUiHelper.Br(AvesUiHelper.TextFaint),
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
            FontFamily = AvesUiHelper.UiFont,
            FontSize = 13,
            Foreground = AvesUiHelper.Br(AvesUiHelper.TextPrimary),
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

/// <summary>Thin wrapper — now delegates to AvesUiHelper.SectionHeader.</summary>
internal static class FormHelpers
{
    public static Border SectionHeader(string text) =>
        AvesUiHelper.SectionHeader(text);
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
            FontFamily = AvesUiHelper.UiFont,
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
        FontFamily = AvesUiHelper.UiFont;
        Background = AvesUiHelper.Br(AvesUiHelper.Backdrop);

        if (revitHandle != IntPtr.Zero)
            _ = new WindowInteropHelper(this) { Owner = revitHandle };

        var root = new DockPanel();

        // ── Interior header strip ─────────────────────────────────────────────
        var headerStrip = AvesUiHelper.HeaderStrip(title);
        DockPanel.SetDock(headerStrip, Dock.Top);
        root.Children.Add(headerStrip);

        // ── Footer strip ──────────────────────────────────────────────────────
        var footerStrip = BuildFooterStrip();
        DockPanel.SetDock(footerStrip, Dock.Bottom);
        root.Children.Add(footerStrip);

        // ── Main content area ─────────────────────────────────────────────────
        var contentBorder = new Border
        {
            Background = AvesUiHelper.Br(AvesUiHelper.Surface),
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
            FontFamily = AvesUiHelper.UiFont,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(60, 80, 110)),
            Margin = new Thickness(0, 0, 0, 6),
        };
        rightCol.Children.Add(previewLabel);

        Preview = new LayoutPreviewCanvas();
        rightCol.Children.Add(new Border
        {
            BorderBrush = AvesUiHelper.Br(AvesUiHelper.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = Preview,
        });

        var previewCaption = new TextBlock
        {
            Text = "Schematic only — element sizes are approximate; positions reflect your inputs.",
            FontSize = 9,
            FontFamily = AvesUiHelper.UiFont,
            Foreground = AvesUiHelper.Br(AvesUiHelper.TextFaint),
            Margin = new Thickness(0, 5, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        rightCol.Children.Add(previewCaption);

        // StatusText lives in the right column below the caption
        StatusText = new TextBlock
        {
            FontFamily = AvesUiHelper.UiFont,
            FontSize = 10,
            Foreground = AvesUiHelper.Br(AvesUiHelper.TextFaint),
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
        var cancelBtn = AvesUiHelper.GhostButton("Cancel");
        cancelBtn.IsCancel = true;
        cancelBtn.Margin = new Thickness(0, 0, 10, 0);
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };

        var makeDefaultBtn = AvesUiHelper.SecondaryButton("Make Default");
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

        var runBtn = AvesUiHelper.PrimaryButton("Run");
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
            Background = AvesUiHelper.Br(AvesUiHelper.Surface2),
            BorderBrush = AvesUiHelper.Br(AvesUiHelper.Border),
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