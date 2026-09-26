// -----------------------------------------------------------------------
// TEMPORARY DIAGNOSTIC -- remove once the Vendor re-run strategy is settled.
// Title: Vendor Sheet 2 Viewport Diagnostic
// Summary: For each selected sheet (or the active sheet), dumps every
//          viewport: ids, the view it shows, that view's name / title on
//          sheet / orientation, the viewport's position, rotation and label,
//          and every Extensible Storage entity on the sheet, viewport and
//          view. Also prints a per-assembly geometry report -- centroid
//          offset, members ranked along each local axis -- so a Front/Back
//          or Left/Right sign check has an identifiable real element to look
//          for instead of an impression. Front/Back is a fixed convention in
//          production now (AssemblyDrawingBuilder.SideLookDirections), not a
//          derivation, so this report no longer carries extents, a long/
//          short ratio, or a stamp comparison -- those existed to protect a
//          derived choice that no longer exists.
// Key logic: Read-only against the model. The one write is an optional
//            probe that attaches a throwaway entity to a viewport and its
//            view inside a transaction that is always rolled back, to prove
//            whether a Viewport can carry Extensible Storage at all.
// -----------------------------------------------------------------------

using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using AVES.Core;
using Grid = System.Windows.Controls.Grid;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;
using Transform = Autodesk.Revit.DB.Transform;

namespace AVES.Cmds_DiagVendorViewports;

[Transaction(TransactionMode.Manual)]
public sealed class Cmd_DiagVendorViewports : IExternalCommand
{
    private const string AlertTitle = "Vendor Viewport Diagnostic";

    // Throwaway schema for the write probe only. Never the Korea package GUID.
    private static readonly Guid ProbeSchemaGuid = new("B3F0E1D2-4C5A-4E6B-9A7D-0F1E2D3C4B5A");

    private static readonly string[] Keys = ["FRONT", "BACK", "LEFT", "RIGHT", "TOP", "BOTTOM"];

    // What the builder DECLARES as each key's BasisZ (Cmds_PullDown2.cs:3374-3377,
    // :3396-3398), in assembly axes -- i.e. the SideLookDirections/CreateUpwardSection
    // constants themselves, unmodified. Kept separate from ExpectedViewDirections
    // below because the two are NOT the same vector: three real Revit samples in this
    // investigation showed a ViewSection's reported ViewDirection is the NEGATION of
    // the BasisZ used to build its box (FRONT declared (-1,0,0) reported as (1,0,0),
    // and so on for BACK/LEFT/RIGHT/BOTTOM), while a ViewPlan (TOP) reports its
    // declared vector unchanged. This is why BOTTOM's declared (0,0,-1) collides with
    // TOP's (0,0,1) once negated -- the bug flagged separately, not fixed here.
    private static readonly (string Key, XYZ Dir)[] DeclaredBasisZ =
    [
        ("FRONT", new XYZ(-1, 0, 0)),
        ("BACK", new XYZ(1, 0, 0)),
        ("RIGHT", new XYZ(0, -1, 0)),
        ("LEFT", new XYZ(0, 1, 0)),
        ("TOP", new XYZ(0, 0, 1)),
        ("BOTTOM", new XYZ(0, 0, -1)),
    ];

    // What View.ViewDirection actually reports for each key, given DeclaredBasisZ
    // above and the negate-for-sections/keep-for-plans rule just described. This is
    // what ClassifyByDirection compares real views against -- the earlier version of
    // this table used DeclaredBasisZ directly, which is why every prior run of this
    // command misclassified all four horizontals (FRONT<->BACK, LEFT<->RIGHT) in a
    // perfectly consistent pattern across all three real samples so far. The raw
    // local-frame vector is now also printed for every view, so this table is a
    // convenience hint, not the ground truth.
    //
    // IMPORTANT, updated more than once as production changed underneath it:
    // production went fixed table (X-axis Front) -> geometry-derived per
    // assembly -> fixed table again (Y-axis Front, the project's own assembly-
    // origin convention -- see AssemblyDrawingBuilder.SideLookDirections). This
    // table's own vectors were never updated to match that final state (still
    // X-axis Front/Back below) and are not being changed now either -- the raw
    // local-frame vector is printed for every view specifically so nothing has
    // to trust this table for ground truth. What DOES still hold regardless of
    // which production generation a sample came from: ClassifyByDirection does
    // NOT match a view against ITS OWN key's slot, it reports whichever table
    // key's vector happens to equal that view's reported direction, so a
    // "Direction match" label frequently names a DIFFERENT key than the view's
    // own name -- expected, not a fault -- and the per-sheet "resolved N/6"
    // tally means: does every one of the 6 table slots get claimed by exactly
    // one view (using THIS table's own, possibly-mismatched-to-production,
    // vectors as the yardstick).
    private static readonly (string Key, XYZ Dir)[] ExpectedViewDirections =
    [
        ("FRONT", new XYZ(1, 0, 0)),
        ("BACK", new XYZ(-1, 0, 0)),
        ("RIGHT", new XYZ(0, 1, 0)),
        ("LEFT", new XYZ(0, -1, 0)),
        ("TOP", new XYZ(0, 0, 1)),
        ("BOTTOM", new XYZ(0, 0, -1)),
    ];

    private static readonly Regex CollisionSuffix = new(@" \(\d+\)$", RegexOptions.Compiled);

    /// <summary>One resolved view, kept for the cross-package comparison that runs
    /// after every selected sheet has been dumped.</summary>
    private readonly record struct ViewSample(
        string AssemblyUniqueId, string AssemblyName, string PackagePrefix,
        string Key, XYZ LocalDir, string ViewName, string SheetName);

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        var sheets = uiDoc.Selection.GetElementIds()
            .Select(id => doc.GetElement(id))
            .OfType<ViewSheet>()
            .ToList();

        if (sheets.Count == 0 && doc.ActiveView is ViewSheet active)
            sheets.Add(active);

        if (sheets.Count == 0)
        {
            AvControls.ShowAlert(AlertTitle,
                "Select one or more sheets in the Project Browser, or open a sheet, " +
                "then run this again.", AlertSeverity.Warning);
            return Result.Cancelled;
        }

        var probe = TaskDialog.Show(AlertTitle,
            "Also run the write probe?\n\n" +
            "It attaches a throwaway Extensible Storage entity to the first viewport " +
            "and its view, then rolls the transaction back. Nothing is saved to the " +
            "model; a probe schema is registered in this Revit session only.",
            TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) == TaskDialogResult.Yes;

        var swapProbe = TaskDialog.Show(AlertTitle,
            "Also run the sign + swap probe?\n\n" +
            "Creates throwaway sections the way the builder does and reports the " +
            "ViewDirection Revit gives them, then points the first viewport (an " +
            "assembly view if one is selected) at a new plain section and reports what " +
            "happened to its position, rotation, label and detail number. The " +
            "transaction is always rolled back; nothing is saved to the model.",
            TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) == TaskDialogResult.Yes;

        var assemblies = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Assemblies)
            .WhereElementIsNotElementType()
            .OfType<AssemblyInstance>()
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"AVES Vendor viewport diagnostic -- {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Document: {doc.Title}");
        sb.AppendLine("Legend -- NOTE: production no longer has a fixed direction table " +
                      "(AssemblyDrawingBuilder now derives Front/Back per assembly from its " +
                      "own geometry -- see the per-assembly GEOMETRY REPORT below). The table " +
                      "below is kept only to classify views from OLDER, fixed-table packages " +
                      "already sampled in this investigation. DeclaredBasisZ = what a fixed-" +
                      "table build asked Revit for; ExpectedViewDirections = what " +
                      "View.ViewDirection then reported for that ask:");
        foreach (var (key, dir) in DeclaredBasisZ)
        {
            var expected = ExpectedViewDirections.First(e => e.Key == key).Dir;
            sb.AppendLine($"  {key,-7} declared={F(dir)}  ->  expected reported={F(expected)}");
        }
        sb.AppendLine($"Schemas loaded in session: {Schema.ListSchemas().Count}");
        foreach (var s in Schema.ListSchemas())
            sb.AppendLine($"  - {s.SchemaName} {{{s.GUID}}} vendor={s.VendorId}");
        sb.AppendLine();

        var assemblyExtentsPrinted = new HashSet<ElementId>();
        var samples = new List<ViewSample>();

        foreach (var sheet in sheets)
            DumpSheet(doc, sheet, assemblies, assemblyExtentsPrinted, samples, sb);

        PrintCrossPackageComparison(samples, sb);

        if (probe)
            RunWriteProbe(doc, sheets, sb);

        if (swapProbe)
            RunSignAndSwapProbe(doc, sheets, sb);

        var path = Path.Combine(Path.GetTempPath(),
            $"AVES_VendorViewportDiag_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(path, sb.ToString());

        new DiagWindow(sb.ToString(), path, commandData.Application.MainWindowHandle).ShowDialog();
        return Result.Succeeded;
    }

    private static void DumpSheet(
        Document doc, ViewSheet sheet, List<AssemblyInstance> assemblies,
        HashSet<ElementId> assemblyExtentsPrinted, List<ViewSample> samples, StringBuilder sb)
    {
        sb.AppendLine(new string('=', 78));
        sb.AppendLine($"SHEET  {sheet.SheetNumber} - {sheet.Name}");
        sb.AppendLine($"  Id={sheet.Id}  UniqueId={sheet.UniqueId}");
        DumpEntities(sheet, "  ", sb);

        // Resolve the assembly the sheet documents: from its <PREFIX>-ASM_<name>_NN
        // name (V for Vendor, K for Korea), since unstamped legacy sheets carry
        // nothing better. The prefix is what lets the cross-package comparison
        // below match a Korea sheet's assembly to a Vendor sheet's.
        var nameMatch = Regex.Match(sheet.Name, @"^([A-Z]+)-ASM_(.+)_(\d\d)(?: \(\d+\))?$");
        var packagePrefix = nameMatch.Success ? nameMatch.Groups[1].Value : "?";
        var assembly = nameMatch.Success
            ? assemblies.FirstOrDefault(a => a.Name.Equals(nameMatch.Groups[2].Value,
                StringComparison.OrdinalIgnoreCase))
            : null;

        sb.AppendLine(assembly is null
            ? "  Assembly (from sheet name): NOT RESOLVED"
            : $"  Assembly (from sheet name): {assembly.Name}  Id={assembly.Id}  " +
              $"UniqueId={assembly.UniqueId}  Package prefix: {packagePrefix}");
        if (assembly is not null)
        {
            var t = assembly.GetTransform();
            sb.AppendLine($"    Transform  Origin={F(t.Origin)}  X={F(t.BasisX)}  " +
                          $"Y={F(t.BasisY)}  Z={F(t.BasisZ)}");

            if (assemblyExtentsPrinted.Add(assembly.Id))
            {
                // Full round-trip precision (F()/N() elsewhere round to 4
                // decimals, which cannot show a deviation on the scale
                // IsAxisAlignedRotation's 1e-6 tolerance actually cares about),
                // plus the angle each basis vector sits off its nearest signed
                // world axis, in degrees -- directly answers "is this a real
                // tiny tilt or a comparison bug" without hand arithmetic on
                // raw components.
                sb.AppendLine($"    Transform (full precision)  X={FullPrecision(t.BasisX)}");
                sb.AppendLine($"                                 Y={FullPrecision(t.BasisY)}");
                sb.AppendLine($"                                 Z={FullPrecision(t.BasisZ)}");
                sb.AppendLine($"    Angular deviation from nearest signed axis: " +
                              $"X={AngleOffNearestAxisDeg(t.BasisX):0.000000} deg  " +
                              $"Y={AngleOffNearestAxisDeg(t.BasisY):0.000000} deg  " +
                              $"Z={AngleOffNearestAxisDeg(t.BasisZ):0.000000} deg  " +
                              $"(IsAxisAlignedRotation's current tolerance: componentwise " +
                              $"1e-6, roughly equivalent to ~0.0001 deg for a single-axis tilt)");

                PrintAssemblyGeometryReport(doc, assembly, sb);
            }
        }

        var viewportIds = sheet.GetAllViewports();
        sb.AppendLine($"  Viewports: {viewportIds.Count}");

        var claimsByName = new Dictionary<string, int>();
        var claimsByGeometry = new Dictionary<string, int>();

        foreach (var vpId in viewportIds)
        {
            if (doc.GetElement(vpId) is not Viewport vp) continue;
            var view = doc.GetElement(vp.ViewId) as View;

            sb.AppendLine();
            sb.AppendLine($"  VIEWPORT Id={vp.Id}  UniqueId={vp.UniqueId}");
            sb.AppendLine($"    Type: {doc.GetElement(vp.GetTypeId())?.Name ?? "<none>"}");
            sb.AppendLine($"    BoxCenter={F(vp.GetBoxCenter())}  Rotation={vp.Rotation}");
            var outline = vp.GetBoxOutline();
            sb.AppendLine($"    BoxOutline Min={F(outline.MinimumPoint)} Max={F(outline.MaximumPoint)}");
            sb.AppendLine($"    LabelOffset={F(vp.LabelOffset)}  LabelLineLength={N(vp.LabelLineLength)}");
            sb.AppendLine($"    Detail number: " +
                          $"{vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString() ?? "<none>"}");
            sb.AppendLine("    Entities on viewport:");
            DumpEntities(vp, "      ", sb);

            if (view is null)
            {
                sb.AppendLine($"    View Id={vp.ViewId}  <not resolvable>");
                continue;
            }

            sb.AppendLine($"    VIEW Id={view.Id}  UniqueId={view.UniqueId}");
            sb.AppendLine($"      Name: \"{view.Name}\"");
            sb.AppendLine($"      Title on sheet: " +
                          $"\"{view.get_Parameter(BuiltInParameter.VIEW_DESCRIPTION)?.AsString() ?? ""}\"");
            sb.AppendLine($"      Class={view.GetType().Name}  ViewType={view.ViewType}  Scale=1:{view.Scale}");
            sb.AppendLine($"      Assembly view: {view.IsAssemblyView}  " +
                          $"AssociatedAssemblyInstanceId={view.AssociatedAssemblyInstanceId}");
            sb.AppendLine($"      ViewDirection={F(view.ViewDirection)}  RightDirection={F(view.RightDirection)}  " +
                          $"UpDirection={F(view.UpDirection)}");
            sb.AppendLine("      Entities on view:");
            DumpEntities(view, "        ", sb);

            // Strategy (a): the builder names views "<assembly> - <KEY>", and
            // NameRegistry appends " (n)" on a collision.
            var byName = ClassifyByName(view.Name, assembly?.Name);
            sb.AppendLine($"      Strategy (a) name match: {byName}");
            if (byName.StartsWith("KEY ", StringComparison.Ordinal)) Count(claimsByName, byName[4..]);

            // Raw local-frame direction -- ground truth, no table involved. Printed
            // explicitly so nothing downstream has to trust the classifier below or
            // redo this rotation by hand.
            var localVec = assembly?.GetTransform().Inverse.OfVector(view.ViewDirection);
            sb.AppendLine($"      Local-frame ViewDirection (raw): " +
                          (localVec is null ? "n/a (no assembly)" : F(localVec)));

            // Geometric cross-check against the current builder's convention (hint
            // only -- see the raw vector above for ground truth).
            var world = ClassifyByDirection(view.ViewDirection);
            var local = localVec is null ? "n/a (no assembly)" : ClassifyByDirection(localVec);
            sb.AppendLine($"      Direction match (world frame): {world}");
            sb.AppendLine($"      Direction match (assembly frame): {local}");
            if (local.StartsWith("KEY ", StringComparison.Ordinal)) Count(claimsByGeometry, local[4..]);

            // Cross-package sample: keyed off the NAME match (established reliable,
            // 6/6, across every sample so far), not the direction classifier.
            if (assembly is not null && localVec is not null &&
                byName.StartsWith("KEY ", StringComparison.Ordinal))
            {
                var key = byName["KEY ".Length..byName.IndexOf(' ', "KEY ".Length)];
                samples.Add(new ViewSample(
                    assembly.UniqueId, assembly.Name, packagePrefix, key, localVec,
                    view.Name, sheet.Name));
            }
        }

        sb.AppendLine();
        sb.AppendLine("  SUMMARY");
        sb.AppendLine($"    Name strategy:      {Summarise(claimsByName)}");
        sb.AppendLine($"    Direction strategy: {Summarise(claimsByGeometry)}");

        var duplicates = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => s.Id != sheet.Id
                        && CollisionSuffix.Replace(s.Name, "")
                            .Equals(CollisionSuffix.Replace(sheet.Name, ""), StringComparison.OrdinalIgnoreCase))
            .Select(s => $"{s.SheetNumber} - {s.Name}")
            .ToList();
        sb.AppendLine($"    Same-name sheets (earlier duplicated runs): " +
                      (duplicates.Count == 0 ? "none" : string.Join("; ", duplicates)));
        sb.AppendLine();
    }

    private static string ClassifyByName(string viewName, string? assemblyName)
    {
        var stripped = CollisionSuffix.Replace(viewName, "");
        var hadSuffix = stripped.Length != viewName.Length;

        foreach (var key in Keys)
        {
            var tail = $" - {key}";
            if (!stripped.EndsWith(tail, StringComparison.Ordinal)) continue;

            var prefix = stripped[..^tail.Length];
            var prefixNote = assemblyName is null
                ? "prefix not checked"
                : prefix.Equals(assemblyName, StringComparison.OrdinalIgnoreCase)
                    ? "prefix = assembly name"
                    : $"PREFIX MISMATCH (\"{prefix}\")";
            return $"KEY {key}  [{prefixNote}{(hadSuffix ? ", had collision suffix" : "")}]";
        }

        return "NO MATCH";
    }

    /// <summary>
    /// One member element's centre, converted into the assembly's own axes.
    private readonly record struct MemberSample(Element El, XYZ Local);

    /// <summary>
    /// Per-assembly geometry report for the remaining Front/Back and Left/
    /// Right sign check: the centroid's signed offset from the bounding-box
    /// centre along each local axis, and member elements ranked along each
    /// local axis with enough identity to find them in the model.
    /// <para>
    /// Everything else this report used to carry -- DX/DY/DZ extents, the
    /// long/short ratio and its 1.15 warning, and a comparison against a
    /// stamped axis/sign -- was in service of a geometry-DERIVED Front/Back
    /// choice that turned out to be wrong (this project's assembly origins
    /// place +Y as Front deliberately; it is not inferred from proportions).
    /// Production no longer derives or stamps an axis/sign at all
    /// (AssemblyDrawingBuilder.SideLookDirections is a fixed table now), so
    /// there is nothing left to measure a projection error against, no tie to
    /// warn about, and no stamp to compare -- all of that is gone from here
    /// too, not left printing numbers for a question production no longer
    /// asks. predictedAxis/sideAxis below are the same fixed constants
    /// production uses, not a computation.
    /// </para>
    /// </summary>
    private static void PrintAssemblyGeometryReport(
        Document doc, AssemblyInstance assembly, StringBuilder sb)
    {
        var assemblyFrame = assembly.GetTransform();
        var inverse = assemblyFrame.Inverse;

        var memberElements = assembly.GetMemberIds()
            .Select(id => doc.GetElement(id))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();

        // World-aligned bounding box (assembly + every member), same source
        // GeometryUtils.GetAssemblyBoundingBox uses in production -- projected
        // into assembly axes for the local extents. Exact at rotations that are
        // a multiple of 90 deg (every sample so far); an OVER-ESTIMATE
        // otherwise -- see AssemblyDrawingBuilder.IsAxisAlignedRotation, which
        // is exactly what the non-orthogonal-rotation warning checks for.
        var world = WorldBoundingBoxOfAssembly(doc, assembly, memberElements);

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        for (var i = 0; i < 8; i++)
        {
            var corner = new XYZ(
                (i & 1) == 0 ? world.Min.X : world.Max.X,
                (i & 2) == 0 ? world.Min.Y : world.Max.Y,
                (i & 4) == 0 ? world.Min.Z : world.Max.Z);
            var p = inverse.OfPoint(corner);
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
        }
        var localCenter = new XYZ((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);

        var members = new List<MemberSample>();
        foreach (var el in memberElements)
        {
            BoundingBoxXYZ? bb;
            try { bb = el.get_BoundingBox(null); } catch { bb = null; }
            if (bb is null) continue;
            members.Add(new MemberSample(el, inverse.OfPoint((bb.Min + bb.Max) / 2.0)));
        }

        // Unweighted centroid of member element centres, in local axes -- a
        // cheap stand-in for a true volumetric centroid (no geometry
        // extraction, just each member's own bounding-box centre), good
        // enough to say which end of the assembly carries more stuff.
        var centroid = members.Count == 0
            ? localCenter
            : new XYZ(members.Average(m => m.Local.X), members.Average(m => m.Local.Y),
                      members.Average(m => m.Local.Z));

        // Fixed convention, matching AssemblyDrawingBuilder.SideLookDirections
        // exactly -- not computed. Front/Back always ride local Y (the
        // project's own assembly-origin placement convention: +Y faces the
        // part's front); Left/Right always ride local X.
        const string predictedAxis = "Y";
        const int predictedSign = 1;
        const string sideAxis = "X";

        sb.AppendLine($"  GEOMETRY REPORT for {assembly.Name} (all values in ASSEMBLY axes)");
        sb.AppendLine($"    Centroid offset from bounding-box centre: " +
                      $"local X={SignedMm(centroid.X - localCenter.X)} mm, " +
                      $"local Y={SignedMm(centroid.Y - localCenter.Y)} mm, " +
                      $"local Z={SignedMm(centroid.Z - localCenter.Z)} mm");
        sb.AppendLine($"    FIXED convention: Front/Back on local {predictedAxis}; " +
                      $"FRONT = {AxisLabel(predictedAxis, predictedSign)}, " +
                      $"BACK = {AxisLabel(predictedAxis, -predictedSign)}; RIGHT = " +
                      $"{AxisLabel(sideAxis, -1)}, LEFT = {AxisLabel(sideAxis, 1)}");

        var sortedByX = members.OrderBy(m => m.Local.X).ToList();
        var sortedByY = members.OrderBy(m => m.Local.Y).ToList();

        if (members.Count > 0)
        {
            // Explicit per-axis accessor + range, keyed by axis name -- avoids
            // any risk of picking the wrong sorted list/range pair by mixing up
            // which one is "this axis" vs. "the other axis" for FRONT/BACK
            // (predictedAxis) vs. RIGHT/LEFT (sideAxis).
            Func<MemberSample, double> AxisValue(string axis) => axis == "X" ? m => m.Local.X : m => m.Local.Y;
            List<MemberSample> SortedBy(string axis) => axis == "X" ? sortedByX : sortedByY;
            (double Min, double Max) RangeOf(string axis)
            {
                var sorted = SortedBy(axis);
                var v = AxisValue(axis);
                return (v(sorted[0]), v(sorted[^1]));
            }

            var front = PickAxisWitness(SortedBy(predictedAxis), wantNegativeEnd: false,
                AxisValue(sideAxis), RangeOf(sideAxis));
            var back = PickAxisWitness(SortedBy(predictedAxis), wantNegativeEnd: true,
                AxisValue(sideAxis), RangeOf(sideAxis));
            var right = PickAxisWitness(SortedBy(sideAxis), wantNegativeEnd: true,
                AxisValue(predictedAxis), RangeOf(predictedAxis));
            var left = PickAxisWitness(SortedBy(sideAxis), wantNegativeEnd: false,
                AxisValue(predictedAxis), RangeOf(predictedAxis));

            sb.AppendLine("    WHAT TO CHECK -- each witness is extreme on ITS OWN predicted axis " +
                          "AND near the centre of the OTHER axis, so finding it on a sheet is " +
                          "evidence for that ONE check, not both at once (a corner member that is " +
                          "extreme on both axes proves nothing, since it would show up on either " +
                          "sheet regardless of which sign is right):");
            AppendWitnessLine(sb, "FRONT", "BACK", "local " + predictedAxis + " positive", front);
            AppendWitnessLine(sb, "BACK", "FRONT", "local " + predictedAxis + " negative", back);
            AppendWitnessLine(sb, "RIGHT", "LEFT", "local " + sideAxis + " negative", right);
            AppendWitnessLine(sb, "LEFT", "RIGHT", "local " + sideAxis + " positive", left);
        }
        else
        {
            sb.AppendLine("    WHAT TO CHECK: no member elements resolved -- cannot point at a " +
                          "specific element. Compare the FRONT/BACK and LEFT/RIGHT sheets by eye " +
                          "against the model instead.");
        }

        sb.AppendLine($"    Members ranked along local X, near->far (n={members.Count}):");
        PrintRankedMembers(sortedByX, m => m.Local.X, sb);
        sb.AppendLine($"    Members ranked along local Y, near->far (n={members.Count}):");
        PrintRankedMembers(sortedByY, m => m.Local.Y, sb);
    }

    /// <summary>
    /// Among the members nearest the wanted end of an axis-sorted list, picks
    /// the one closest to the CENTRE of the other axis -- so it is a witness
    /// for that one axis alone, not a corner member that would show up on
    /// either sheet regardless of which sign is right. "Close to centre" is
    /// the central 50% of the other axis's own range (i.e. not itself sitting
    /// in the outer quarter at either end); candidates are drawn from the 8
    /// nearest members to the wanted end, or the whole list if smaller.
    /// </summary>
    private static (MemberSample? Witness, bool NoIndependentWitness) PickAxisWitness(
        List<MemberSample> sortedByThisAxis, bool wantNegativeEnd,
        Func<MemberSample, double> otherAxisValue, (double Min, double Max) otherAxisRange)
    {
        if (sortedByThisAxis.Count == 0) return (null, true);

        var poolSize = Math.Min(8, sortedByThisAxis.Count);
        var candidates = wantNegativeEnd
            ? sortedByThisAxis.Take(poolSize)
            : sortedByThisAxis.TakeLast(poolSize);

        var otherCentre = (otherAxisRange.Min + otherAxisRange.Max) / 2.0;
        var otherHalfRange = (otherAxisRange.Max - otherAxisRange.Min) / 2.0;

        var ranked = candidates
            .Select(m => (Member: m, OtherOffset: Math.Abs(otherAxisValue(m) - otherCentre)))
            .OrderBy(x => x.OtherOffset)
            .ToList();

        var best = ranked[0];
        // otherHalfRange near zero means the OTHER axis barely varies across
        // the assembly at all -- nothing can be "off-centre" on it, so any
        // candidate qualifies.
        var isIndependent = otherHalfRange < 1e-6 || best.OtherOffset <= otherHalfRange * 0.5;

        return (best.Member, !isIndependent);
    }

    private static void AppendWitnessLine(
        StringBuilder sb, string thisKey, string otherKey, string predictedDirection,
        (MemberSample? Witness, bool NoIndependentWitness) result)
    {
        if (result.Witness is null)
        {
            sb.AppendLine($"      {thisKey}: no member elements resolved -- cannot name a witness.");
            return;
        }

        if (result.NoIndependentWitness)
        {
            sb.AppendLine($"      {thisKey} ({predictedDirection}): NO INDEPENDENT WITNESS -- every " +
                          $"member near this end is also near an extreme of the other axis " +
                          $"(closest available: {DescribeMember(result.Witness.Value)}). This check " +
                          "cannot be confirmed by element id alone for this assembly; compare the " +
                          "sheets by eye against the model instead.");
            return;
        }

        sb.AppendLine($"      {thisKey} ({predictedDirection}): {DescribeMember(result.Witness.Value)} -- " +
                      $"if the sign is RIGHT, this is what you see on the {thisKey} sheet; if " +
                      $"INVERTED, it shows up on {otherKey} instead.");
    }

    /// <summary>Up to 6 members from each end of an axis-sorted list; the
    /// full list if it is short enough that "each end" would overlap.</summary>
    private static void PrintRankedMembers(
        List<MemberSample> sorted, Func<MemberSample, double> axisValue, StringBuilder sb)
    {
        if (sorted.Count == 0) { sb.AppendLine("      (no members resolved)"); return; }

        var shown = sorted.Count <= 12 ? sorted : sorted.Take(6).Concat(sorted.TakeLast(6)).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            if (sorted.Count > 12 && i == 6)
                sb.AppendLine($"      ... {sorted.Count - 12} member(s) omitted ...");
            sb.AppendLine($"      {axisValue(shown[i]),9:0.00} ft   {DescribeMember(shown[i])}");
        }
    }

    private static string DescribeMember(MemberSample m)
    {
        var el = m.El;
        var typeEl = el.Document.GetElement(el.GetTypeId());
        var familyType = typeEl is FamilySymbol fs ? $"{fs.FamilyName} : {typeEl.Name}" : typeEl?.Name;
        return $"{el.Category?.Name ?? "<no category>"} -- {familyType ?? "<no type>"}  Id={el.Id}";
    }

    private static BoundingBoxXYZ WorldBoundingBoxOfAssembly(
        Document doc, AssemblyInstance assembly, List<Element> members)
    {
        BoundingBoxXYZ? TryBBox(Element el) { try { return el.get_BoundingBox(null); } catch { return null; } }
        BoundingBoxXYZ? Union(BoundingBoxXYZ? a, BoundingBoxXYZ? b)
        {
            if (a is null) return b;
            if (b is null) return a;
            return new BoundingBoxXYZ
            {
                Min = new XYZ(Math.Min(a.Min.X, b.Min.X), Math.Min(a.Min.Y, b.Min.Y), Math.Min(a.Min.Z, b.Min.Z)),
                Max = new XYZ(Math.Max(a.Max.X, b.Max.X), Math.Max(a.Max.Y, b.Max.Y), Math.Max(a.Max.Z, b.Max.Z)),
            };
        }

        var world = TryBBox(assembly);
        foreach (var el in members) world = Union(world, TryBBox(el));
        return world ?? new BoundingBoxXYZ { Min = XYZ.Zero, Max = XYZ.Zero };
    }

    private static double ToMm(double ft) => UnitUtils.ConvertFromInternalUnits(ft, UnitTypeId.Millimeters);

    private static string SignedMm(double ft)
    {
        var mm = ToMm(ft);
        return mm >= 0 ? $"+{mm:0}" : $"{mm:0}";
    }

    private static string AxisLabel(string axis, int sign) =>
        $"{(sign < 0 ? "negative" : "positive")} local {axis}";

    /// <summary>
    /// For every assembly that has resolved views from more than one package
    /// (V-ASM_ and K-ASM_ sheets both selected), matches each key's raw local-frame
    /// direction against every key of the other package by cosine similarity, so
    /// "does Korea FRONT match Vendor FRONT or Vendor RIGHT" is read off a number
    /// instead of derived by hand.
    /// </summary>
    private static void PrintCrossPackageComparison(List<ViewSample> samples, StringBuilder sb)
    {
        var byAssembly = samples.GroupBy(s => s.AssemblyUniqueId).ToList();
        var multiPackage = byAssembly.Where(g => g.Select(s => s.PackagePrefix).Distinct().Count() > 1).ToList();

        sb.AppendLine(new string('=', 78));
        sb.AppendLine("CROSS-PACKAGE COMPARISON");

        if (multiPackage.Count == 0)
        {
            sb.AppendLine("  No assembly in this selection has resolved views from more than " +
                          "one package (need both a V-ASM_ and a K-ASM_ sheet selected together).");
            return;
        }

        foreach (var group in multiPackage)
        {
            var first = group.First();
            sb.AppendLine($"  Assembly {first.AssemblyName}  UniqueId={first.AssemblyUniqueId}");

            var prefixes = group.Select(s => s.PackagePrefix).Distinct().OrderBy(p => p).ToList();
            for (var i = 0; i < prefixes.Count; i++)
            for (var j = i + 1; j < prefixes.Count; j++)
            {
                var a = group.Where(s => s.PackagePrefix == prefixes[i]).ToList();
                var b = group.Where(s => s.PackagePrefix == prefixes[j]).ToList();
                sb.AppendLine($"    {prefixes[i]}-package vs {prefixes[j]}-package:");

                foreach (var sa in a)
                {
                    var ranked = b
                        .Select(sb2 => (sb2.Key, Cos: sa.LocalDir.Normalize().DotProduct(sb2.LocalDir.Normalize())))
                        .OrderByDescending(x => x.Cos)
                        .ToList();
                    var best = ranked.First();
                    var verdict = best.Cos > 0.999 ? "SAME direction"
                        : best.Cos < -0.999 ? "OPPOSITE direction"
                        : "NEITHER (not aligned)";
                    sb.AppendLine($"      {prefixes[i]} {sa.Key,-6} local={F(sa.LocalDir)}  " +
                                  $"best match: {prefixes[j]} {best.Key,-6} " +
                                  $"(cos={best.Cos:0.###}, {verdict})");
                }
            }
        }
    }

    private static string ClassifyByDirection(XYZ dir)
    {
        foreach (var (key, expected) in ExpectedViewDirections)
        {
            if (dir.Normalize().IsAlmostEqualTo(expected, 1e-3)) return $"KEY {key}";
        }
        return $"NO MATCH ({F(dir)})";
    }

    private static void Count(Dictionary<string, int> claims, string text)
    {
        var key = text.Split(' ')[0];
        claims[key] = claims.TryGetValue(key, out var n) ? n + 1 : 1;
    }

    private static string Summarise(Dictionary<string, int> claims)
    {
        var missing = Keys.Where(k => !claims.ContainsKey(k)).ToList();
        var dupes = claims.Where(kv => kv.Value > 1).Select(kv => $"{kv.Key}x{kv.Value}").ToList();
        return $"resolved {claims.Count}/6" +
               (missing.Count > 0 ? $"; missing {string.Join(",", missing)}" : "") +
               (dupes.Count > 0 ? $"; AMBIGUOUS {string.Join(",", dupes)}" : "");
    }

    /// <summary>Every entity on <paramref name="element"/>, from every schema
    /// loaded in this session, with its simple fields decoded.</summary>
    private static void DumpEntities(Element element, string indent, StringBuilder sb)
    {
        IList<Guid> guids;
        try { guids = element.GetEntitySchemaGuids(); }
        catch (Exception ex) { sb.AppendLine($"{indent}<GetEntitySchemaGuids failed: {ex.Message}>"); return; }

        if (guids.Count == 0) { sb.AppendLine($"{indent}(none)"); return; }

        foreach (var guid in guids)
        {
            var schema = Schema.Lookup(guid);
            if (schema is null) { sb.AppendLine($"{indent}{{{guid}}} <schema not loaded>"); continue; }

            sb.AppendLine($"{indent}{schema.SchemaName} {{{guid}}}");
            if (!schema.ReadAccessGranted()) { sb.AppendLine($"{indent}  <read access denied>"); continue; }

            Entity entity;
            try { entity = element.GetEntity(schema); }
            catch (Exception ex) { sb.AppendLine($"{indent}  <GetEntity failed: {ex.Message}>"); continue; }

            foreach (var field in schema.ListFields())
                sb.AppendLine($"{indent}  {field.FieldName} = {ReadField(entity, field)}");
        }
    }

    private static string ReadField(Entity entity, Field field)
    {
        if (field.ContainerType != ContainerType.Simple) return $"<{field.ContainerType} container>";
        try
        {
            var t = field.ValueType;
            if (t == typeof(string)) return $"\"{entity.Get<string>(field)}\"";
            if (t == typeof(ElementId)) return entity.Get<ElementId>(field).ToString();
            if (t == typeof(int)) return entity.Get<int>(field).ToString(CultureInfo.InvariantCulture);
            if (t == typeof(bool)) return entity.Get<bool>(field).ToString();
            if (t == typeof(Guid)) return entity.Get<Guid>(field).ToString();
            return $"<{t.Name}, not decoded>";
        }
        catch (Exception ex) { return $"<read failed: {ex.Message}>"; }
    }

    /// <summary>
    /// Attaches a throwaway entity to the first viewport found and to its view,
    /// reads it back, then rolls everything back.
    /// </summary>
    private static void RunWriteProbe(Document doc, List<ViewSheet> sheets, StringBuilder sb)
    {
        sb.AppendLine(new string('=', 78));
        sb.AppendLine("WRITE PROBE (transaction rolled back)");

        var vp = sheets
            .SelectMany(s => s.GetAllViewports())
            .Select(id => doc.GetElement(id))
            .OfType<Viewport>()
            .FirstOrDefault();

        if (vp is null) { sb.AppendLine("  No viewport to probe."); return; }

        Schema schema;
        try
        {
            schema = Schema.Lookup(ProbeSchemaGuid) ?? BuildProbeSchema();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  Probe schema could not be created: {ex.Message}");
            return;
        }

        using var tx = new Transaction(doc, "AVES diagnostic probe (rolled back)");
        tx.Start();
        try
        {
            ProbeOne(vp, "Viewport", schema, sb);
            if (doc.GetElement(vp.ViewId) is View view)
                ProbeOne(view, "View", schema, sb);
        }
        finally
        {
            tx.RollBack();
        }
    }

    private static Schema BuildProbeSchema()
    {
        using var builder = new SchemaBuilder(ProbeSchemaGuid);
        builder.SetSchemaName("AVESDiagViewportProbe");
        builder.SetVendorId("Development");
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField("Key", typeof(string));
        return builder.Finish();
    }

    private static void ProbeOne(Element target, string label, Schema schema, StringBuilder sb)
    {
        try
        {
            var entity = new Entity(schema);
            entity.Set("Key", "PROBE");
            target.SetEntity(entity);

            var readBack = target.GetEntity(schema);
            var ok = readBack.IsValid() && readBack.Get<string>("Key") == "PROBE";
            sb.AppendLine($"  {label} Id={target.Id}: SetEntity OK, read back {(ok ? "OK" : "FAILED")}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  {label} Id={target.Id}: SetEntity FAILED -- {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Part 1: sections built exactly as ViewUtils.CreateOrthoSection /
    /// CreateUpwardSection build them (Cmds_PullDown2.cs:335, :384), so the
    /// requested BasisZ can be compared with the ViewDirection Revit reports.
    /// Part 2: an in-place ViewId swap from the first viewport's view (an
    /// assembly view when one is selected) to a new plain section, reporting the
    /// viewport's state before, straight after, and after the old view is
    /// deleted -- the same order SwapViewportTo uses. All rolled back.
    /// </summary>
    private static void RunSignAndSwapProbe(Document doc, List<ViewSheet> sheets, StringBuilder sb)
    {
        sb.AppendLine(new string('=', 78));
        sb.AppendLine("SIGN + SWAP PROBE (transaction rolled back)");

        var sectionVft = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .FirstOrDefault(v => v.ViewFamily == ViewFamily.Section);
        if (sectionVft is null) { sb.AppendLine("  No Section view family type."); return; }

        using var tx = new Transaction(doc, "AVES diagnostic sign/swap probe (rolled back)");
        tx.Start();
        try
        {
            sb.AppendLine("  Sign test -- builder frame construction, world axes:");
            var c30 = Math.Cos(Math.PI / 6);
            var s30 = Math.Sin(Math.PI / 6);
            foreach (var (label, dir) in new (string, XYZ)[]
                     {
                         ("FRONT-like (-1,0,0)", new XYZ(-1, 0, 0)),
                         ("BACK-like (1,0,0)", new XYZ(1, 0, 0)),
                         ("RIGHT-like (0,-1,0)", new XYZ(0, -1, 0)),
                         ("LEFT-like (0,1,0)", new XYZ(0, 1, 0)),
                         ("30deg (cos30,sin30,0)", new XYZ(c30, s30, 0)),
                     })
            {
                try
                {
                    var basisZ = dir.Normalize();
                    var basisX = XYZ.BasisZ.CrossProduct(basisZ).Normalize();
                    var basisY = basisZ.CrossProduct(basisX).Normalize();
                    var v = CreateProbeSection(doc, sectionVft.Id, basisX, basisY, basisZ);
                    sb.AppendLine($"    {label,-24} BasisZ={F(basisZ)}  ->  ViewDirection={F(v.ViewDirection)}  " +
                                  $"Right={F(v.RightDirection)}  Up={F(v.UpDirection)}");
                }
                catch (Exception ex) { sb.AppendLine($"    {label}: FAILED -- {ex.Message}"); }
            }

            try
            {
                // CreateUpwardSection with topScreenRight = (0,1,0).
                var basisZ = new XYZ(0, 0, -1);
                var basisX = new XYZ(0, 1, 0).Negate();
                var basisY = basisZ.CrossProduct(basisX).Normalize();
                var v = CreateProbeSection(doc, sectionVft.Id, basisX, basisY, basisZ);
                sb.AppendLine($"    {"BOTTOM-like upward",-24} BasisZ={F(basisZ)}  ->  ViewDirection={F(v.ViewDirection)}  " +
                              $"Right={F(v.RightDirection)}  Up={F(v.UpDirection)}");
            }
            catch (Exception ex) { sb.AppendLine($"    BOTTOM-like upward: FAILED -- {ex.Message}"); }

            var viewports = sheets
                .SelectMany(s => s.GetAllViewports())
                .Select(id => doc.GetElement(id))
                .OfType<Viewport>()
                .ToList();
            var vp = viewports.FirstOrDefault(p => doc.GetElement(p.ViewId) is View { IsAssemblyView: true })
                     ?? viewports.FirstOrDefault();

            sb.AppendLine();
            if (vp is null) { sb.AppendLine("  Swap test: no viewport selected."); return; }

            var oldView = (View)doc.GetElement(vp.ViewId);
            sb.AppendLine($"  Swap test on viewport {vp.Id}: \"{oldView.Name}\" " +
                          $"(assembly view: {oldView.IsAssemblyView}, {oldView.ViewType}) -> new plain section");

            var replacement = CreateProbeSection(doc, sectionVft.Id,
                oldView.RightDirection, oldView.UpDirection, oldView.ViewDirection);
            replacement.Scale = oldView.Scale;

            sb.AppendLine($"    Before:        {ViewportState(vp)}");
            sb.AppendLine($"    IsViewIdValidForViewport(new) = {vp.IsViewIdValidForViewport(replacement.Id)}");

            var boxCenter = vp.GetBoxCenter();
            try
            {
                vp.ViewId = replacement.Id;
                doc.Regenerate();
                sb.AppendLine($"    After swap:    {ViewportState(vp)}");

                vp.SetBoxCenter(boxCenter);
                doc.Regenerate();
                sb.AppendLine($"    After restore: {ViewportState(vp)}");

                var oldName = oldView.Name;
                doc.Delete(oldView.Id);
                doc.Regenerate();
                var stillThere = doc.GetElement(vp.Id) is Viewport;
                sb.AppendLine($"    Old view deleted; viewport still exists: {stillThere}" +
                              (stillThere ? $"  {ViewportState(vp)}" : ""));

                try { replacement.Name = oldName; sb.AppendLine($"    Replacement took old name: OK"); }
                catch (Exception ex) { sb.AppendLine($"    Replacement took old name: FAILED -- {ex.Message}"); }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    Swap FAILED -- {ex.GetType().Name}: {ex.Message}");
            }
        }
        finally
        {
            tx.RollBack();
        }
    }

    private static ViewSection CreateProbeSection(
        Document doc, ElementId vftId, XYZ basisX, XYZ basisY, XYZ basisZ)
    {
        var t = Transform.Identity;
        t.Origin = XYZ.Zero;
        t.BasisX = basisX;
        t.BasisY = basisY;
        t.BasisZ = basisZ;
        return ViewSection.CreateSection(doc, vftId, new BoundingBoxXYZ
        {
            Transform = t,
            Min = new XYZ(-2, -2, 0),
            Max = new XYZ(2, 2, 2),
        });
    }

    private static string ViewportState(Viewport vp)
    {
        var outline = vp.GetBoxOutline();
        return $"center={F(vp.GetBoxCenter())} rot={vp.Rotation} labelOffset={F(vp.LabelOffset)} " +
               $"labelLen={N(vp.LabelLineLength)} detail#=" +
               $"{vp.get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER)?.AsString() ?? "<none>"} " +
               $"type={vp.Document.GetElement(vp.GetTypeId())?.Name ?? "<none>"} " +
               $"outline={F(outline.MinimumPoint)}..{F(outline.MaximumPoint)}";
    }

    private static string F(XYZ? p) => p is null ? "<null>" : $"({N(p.X)}, {N(p.Y)}, {N(p.Z)})";

    /// <summary>Round-trip ("R") precision, for telling a genuine tiny tilt
    /// apart from a comparison bug -- F()/N()'s 4-decimal rounding cannot show
    /// a deviation anywhere near IsAxisAlignedRotation's 1e-6 tolerance.</summary>
    private static string FullPrecision(XYZ p) =>
        $"({p.X.ToString("R", CultureInfo.InvariantCulture)}, " +
        $"{p.Y.ToString("R", CultureInfo.InvariantCulture)}, " +
        $"{p.Z.ToString("R", CultureInfo.InvariantCulture)})";

    /// <summary>Angle, in degrees, between v and whichever signed world axis it
    /// sits closest to (largest |component|) -- e.g. for v close to (0,1,0),
    /// how far it actually tilts away from exactly +Y.</summary>
    private static double AngleOffNearestAxisDeg(XYZ v)
    {
        var normalized = v.Normalize();
        var nearest = new[] { XYZ.BasisX, XYZ.BasisY, XYZ.BasisZ }
            .Select(axis => normalized.DotProduct(axis) >= 0 ? axis : axis.Negate())
            .OrderByDescending(axis => normalized.DotProduct(axis))
            .First();
        var cosAngle = Math.Clamp(normalized.DotProduct(nearest), -1.0, 1.0);
        return Math.Acos(cosAngle) * 180.0 / Math.PI;
    }

    private static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Plain scrollable text window with copy.</summary>
    private sealed class DiagWindow : Window
    {
        public DiagWindow(string text, string savedPath, IntPtr owner)
        {
            Title = $"{AlertTitle} (temporary)";
            Width = 1000;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            new WindowInteropHelper(this) { Owner = owner };

            var box = new TextBox
            {
                Text = text,
                IsReadOnly = true,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap,
                Margin = new Thickness(8),
            };

            var copy = new Button { Content = "Copy to clipboard", Padding = new Thickness(12, 4, 12, 4) };
            copy.Click += (_, _) => Clipboard.SetText(text);

            var footer = new DockPanel { Margin = new Thickness(8, 0, 8, 8) };
            DockPanel.SetDock(copy, Dock.Right);
            footer.Children.Add(copy);
            footer.Children.Add(new TextBlock
            {
                Text = $"Saved to {savedPath}",
                VerticalAlignment = VerticalAlignment.Center,
            });

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(box, 0);
            Grid.SetRow(footer, 1);
            grid.Children.Add(box);
            grid.Children.Add(footer);
            Content = grid;
        }
    }
}
