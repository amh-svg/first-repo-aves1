// ============================================================================
//  Auto-Dimension PullDown  –  Revit 2025 / C#
//
//  Button 1 – MT Drawings  (Cmd_MtDimension)
//    Full chain dimensioning: bay segments + overall for each 2D view
//    associated with selected AssemblyInstance elements.
//    Identical behaviour to the original Cmds_Button7 (MT) command.
//
//  Button 2 – Vendor Drawings  (Cmd_VendorDimension)
//    Simplified dimensioning: ONE overall dimension per axis per view.
//    Outputs exactly 3 numbers across all 2D views of an assembly:
//      • Width  – horizontal span (RightDirection axis)
//      • Height – vertical span  (UpDirection axis)
//      • Depth  – out-of-plane span (derived from the plan/top view,
//                 or from the bounding box when no plan view exists)
//    No bay / segment chains are created – only the outermost face-to-face
//    overall dimension on each axis.
//
//  Required project references:
//    RevitAPI.dll, RevitAPIUI.dll
//    PresentationCore, PresentationFramework, WindowsBase, System.Xaml  (WPF)
// ============================================================================

#nullable enable

using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AVES.Core;
using DBTransform = Autodesk.Revit.DB.Transform;
using WinTextBox = System.Windows.Controls.TextBox;
using WpfColor = System.Windows.Media.Color;
using WpfVisibility = System.Windows.Visibility;

namespace AVES.Cmds_PullDownAutoDimension
{
    // =========================================================================
    //  BUTTON 1 – MT Drawings
    //  Full chain dimensioning (bay segments + overall), identical to Button7 (MT).
    // =========================================================================

    [Transaction(TransactionMode.Manual)]
    public class Cmd_MTDimensions : IExternalCommand
    {
        // ── Configuration (all distances in feet) ────────────────────────────
        private static class DimConfig
        {
            public static readonly double OffsetL1 = -200.0 / 304.8;
            public static readonly double ChainGap = 200.0 / 304.8;
            public static readonly double CoordTol = 5.0 / 304.8;
            public static readonly double MinSegment = 200.0 / 304.8;
            public static readonly double MaxMemberThickness = 300.0 / 304.8;

            public static readonly HashSet<int> MemberCategories =
            [
                (int)BuiltInCategory.OST_StructuralFraming,
                (int)BuiltInCategory.OST_StructuralColumns,
                (int)BuiltInCategory.OST_StructuralFoundation,
            ];

            public static readonly string[] PlanKeywords = ["top", "plan", "pianta", "sopra", "roof"];
            public static readonly string[] FrontKeywords = ["front", "fronte", "elevation", "alzato"];
            public static readonly string[] BackKeywords = ["back", "retro", "rear"];
            public static readonly string[] LeftKeywords = ["left", "sinistra", "lato sx"];
            public static readonly string[] RightKeywords = ["right", "destra", "lato dx"];
        }

        private enum ViewRole { Plan, Front, Back, Left, Right, ThreeD, Other }

        private struct MemberFace
        {
            public double NearCoord;
            public Reference? NearRef;
            public double FarCoord;
            public Reference? FarRef;
        }

        private static double Mm(double millimetres) => millimetres / 304.8;

        // ── Execute ──────────────────────────────────────────────────────────
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            var doc = uiDoc.Document;

            // Collect target assemblies
            var selIds = uiDoc.Selection.GetElementIds();
            var assemblies = new List<AssemblyInstance>();

            foreach (var eid in selIds)
                if (doc.GetElement(eid) is AssemblyInstance ai)
                    assemblies.Add(ai);

            DimensionType? dimType;

            if (assemblies.Count == 0)
            {
                var allAsm = new FilteredElementCollector(doc)
                    .OfClass(typeof(AssemblyInstance))
                    .Cast<AssemblyInstance>()
                    .ToList();

                if (allAsm.Count == 0)
                {
                    message = "No AssemblyInstance elements found in the model.";
                    return Result.Failed;
                }

                var chosen = ShowAssemblyPicker(doc, allAsm,
                    "Auto-Dimension MT Views");
                if (chosen == null || chosen.Value.Assemblies.Count == 0) return Result.Cancelled;
                assemblies = chosen.Value.Assemblies;
                dimType = chosen.Value.DimType;
            }
            else
            {
                dimType = AvesUI.ShowDimensionTypePicker(
                    doc, "Auto-Dimension MT Views", "MT");
                if (dimType == null) return Result.Cancelled;
            }

            if (dimType == null)
            {
                message = "No Linear Dimension Type found in the project.";
                return Result.Failed;
            }

            bool anyError = false;
            var summary = new StringBuilder();
            var sink = new StringBuilder();

            using (var t = new Transaction(doc, "Auto-Dimension MT 2D Views"))
            {
                try
                {
                    t.Start();

                    foreach (var assembly in assemblies)
                    {
                        summary.AppendLine($"Assembly: {assembly.Name}");

                        var members = GetMembers(doc, assembly);
                        if (members.Count == 0)
                        {
                            summary.AppendLine("  No structural members found – skipped.");
                            summary.AppendLine();
                            continue;
                        }

                        var views = GetAssemblyViews(doc, assembly, sink);
                        if (views.Count == 0)
                        {
                            summary.AppendLine("  No associated 2D views found – skipped.");
                            summary.AppendLine();
                            continue;
                        }

                        int assemblyChains = 0;

                        foreach (var kv in views)
                        {
                            string vname = kv.Key;
                            View view = kv.Value;
                            var role = ClassifyView(view);

                            sink.Clear();
                            try
                            {
                                int n;
                                switch (role)
                                {
                                    case ViewRole.Plan:
                                        n = DimensionPlanView(doc, view, members, dimType, sink);
                                        break;
                                    case ViewRole.Front:
                                    case ViewRole.Back:
                                    case ViewRole.Left:
                                    case ViewRole.Right:
                                        n = DimensionElevationView(doc, view, members, dimType, sink);
                                        break;
                                    default:
                                        continue;
                                }
                                assemblyChains += n;
                                summary.AppendLine($"  {vname}  →  {n} chain(s)");
                            }
                            catch (Exception ex)
                            {
                                summary.AppendLine($"  {vname}  →  Error: {ex.Message}");
                                anyError = true;
                            }
                        }

                        summary.AppendLine($"  Total: {assemblyChains} chain(s)");
                        summary.AppendLine();
                    }

                    t.Commit();
                }
                catch (Exception ex)
                {
                    if (t.HasStarted()) t.RollBack();
                    message = $"Transaction failed: {ex.Message}";
                    summary.AppendLine($"Transaction failed: {ex.Message}");
                    ShowLogWindow(summary.ToString(), error: true,
                        title: "Auto-Dimension MT Views");
                    return Result.Failed;
                }
            }

            ShowLogWindow(summary.ToString(), error: anyError,
                title: "Auto-Dimension MT Views");
            return anyError ? Result.Failed : Result.Succeeded;
        }

        // ── Revit query helpers ──────────────────────────────────────────────

        private static Dictionary<string, View> GetAssemblyViews(
            Document doc, AssemblyInstance assembly, StringBuilder log)
        {
            var views = new Dictionary<string, View>();
            var assemId = assembly.Id;

            foreach (var v in new FilteredElementCollector(doc)
                                  .OfClass(typeof(View)).Cast<View>())
            {
                try
                {
                    if (v.IsTemplate) continue;
                    var aid = v.AssemblyInstanceId;
                    if (aid != null && aid == assemId) views[v.Name] = v;
                }
                catch { }
            }

            if (views.Count > 0) return views;

            // Fallback: name prefix
            foreach (var v in new FilteredElementCollector(doc)
                                  .OfClass(typeof(View)).Cast<View>())
            {
                try
                {
                    if (v.IsTemplate) continue;
                    if (v.Name.StartsWith(assembly.Name, StringComparison.OrdinalIgnoreCase))
                        views[v.Name] = v;
                }
                catch { }
            }
            return views;
        }

        private static List<Element> GetMembers(Document doc, AssemblyInstance assembly)
        {
            var members = new List<Element>();
            foreach (var mid in assembly.GetMemberIds())
            {
                var elem = doc.GetElement(mid);
                if (elem?.Category == null) continue;
                if (DimConfig.MemberCategories.Contains((int)elem.Category.Id.Value))
                    members.Add(elem);
            }
            return members;
        }

        // ── Geometry – face reference collection ─────────────────────────────

        private static List<MemberFace> CollectMemberFaces(
            Document _, List<Element> members, View view, XYZ axisVec)
        {
            double tol = DimConfig.CoordTol;
            double maxSpan = DimConfig.MaxMemberThickness;
            const double DotTol = 0.95;

            var opts = new Options
            {
                View = view,
                ComputeReferences = true,
                IncludeNonVisibleObjects = false,
            };

            XYZ origin = view.Origin;
            double originProj = origin.DotProduct(axisVec);
            var result = new List<MemberFace>();

            foreach (var member in members)
            {
                BoundingBoxXYZ? bbv;
                try { bbv = member.get_BoundingBox(view); } catch { continue; }
                if (bbv == null) continue;

                double spanV =
                      Math.Abs(axisVec.X) * (bbv.Max.X - bbv.Min.X)
                    + Math.Abs(axisVec.Y) * (bbv.Max.Y - bbv.Min.Y)
                    + Math.Abs(axisVec.Z) * (bbv.Max.Z - bbv.Min.Z);

                if (spanV < tol || spanV > maxSpan) continue;

                GeometryElement? geom;
                try { geom = member.get_Geometry(opts); } catch { continue; }
                if (geom == null) continue;

                var aligned = new List<(double Coord, Reference Ref)>();

                foreach (var obj in geom)
                {
                    if (obj is not GeometryInstance gi) continue;

                    DBTransform? xf;
                    GeometryElement? symGeom;
                    try { xf = gi.Transform; symGeom = gi.GetSymbolGeometry(); }
                    catch { continue; }
                    if (xf == null || symGeom == null) continue;

                    foreach (var item in symGeom)
                    {
                        if (item is not Solid solid || solid.Volume < 1e-9) continue;
                        foreach (Face face in solid.Faces)
                        {
                            try
                            {
                                Reference? refr = face.Reference;
                                if (refr == null) continue;

                                BoundingBoxUV fbb = face.GetBoundingBox();
                                var fuv = new UV(
                                    (fbb.Min.U + fbb.Max.U) * 0.5,
                                    (fbb.Min.V + fbb.Max.V) * 0.5);

                                XYZ worldNormal = xf.OfVector(face.ComputeNormal(fuv));
                                if (Math.Abs(worldNormal.DotProduct(axisVec)) < DotTol)
                                    continue;

                                XYZ worldPt = xf.OfPoint(face.Evaluate(fuv));
                                double vCoord = worldPt.DotProduct(axisVec) - originProj;
                                aligned.Add((vCoord, refr));
                            }
                            catch { }
                        }
                    }
                }

                if (aligned.Count < 2) continue;
                aligned.Sort((a, b) => a.Coord.CompareTo(b.Coord));

                var (nearCoord, nearRef) = aligned[0];
                var (farCoord, farRef) = aligned[^1];
                if (farCoord - nearCoord < tol) continue;

                result.Add(new MemberFace
                {
                    NearCoord = nearCoord,
                    NearRef = nearRef,
                    FarCoord = farCoord,
                    FarRef = farRef,
                });
            }
            return result;
        }

        private static List<MemberFace> GetMemberClusters(
            Document doc, List<Element> members, View view, XYZ axisVec)
        {
            var raw = CollectMemberFaces(doc, members, view, axisVec);
            if (raw.Count == 0) return [];

            raw.Sort((a, b) => a.NearCoord.CompareTo(b.NearCoord));
            double tol = DimConfig.CoordTol;
            List<MemberFace> clusters = [raw[0]];

            for (int i = 1; i < raw.Count; i++)
            {
                var cur = raw[i];
                var prev = clusters[^1];

                if (cur.NearCoord <= prev.FarCoord + tol)
                {
                    if (cur.FarCoord > prev.FarCoord)
                        clusters[^1] = new MemberFace
                        {
                            NearCoord = prev.NearCoord,
                            NearRef = prev.NearRef,
                            FarCoord = cur.FarCoord,
                            FarRef = cur.FarRef,
                        };
                }
                else
                {
                    clusters.Add(cur);
                }
            }
            return clusters;
        }

        // ── Dimension creation helpers ───────────────────────────────────────

        private static Dimension? MakeDim(
            Document doc, View view, ReferenceArray refs,
            Line line, DimensionType dimType, StringBuilder? log)
        {
            if (refs.Size < 2) return null;
            try { return doc.Create.NewDimension(view, line, refs, dimType); }
            catch (Exception ex) { log?.AppendLine($"    ✖  NewDimension: {ex.Message}"); return null; }
        }

        private static XYZ WorldPt(View view, double along, double perp, bool isHoriz)
        {
            XYZ o = view.Origin;
            XYZ r = view.RightDirection;
            XYZ u = view.UpDirection;

            double xL = isHoriz ? along : perp;
            double yL = isHoriz ? perp : along;

            return new XYZ(
                o.X + r.X * xL + u.X * yL,
                o.Y + r.Y * xL + u.Y * yL,
                o.Z + r.Z * xL + u.Z * yL);
        }

        private static void ExpandCropBox(View view, double extraFt)
        {
            try
            {
                BoundingBoxXYZ cb = view.CropBox;
                view.CropBox = new BoundingBoxXYZ
                {
                    Min = new XYZ(cb.Min.X - extraFt, cb.Min.Y - extraFt, cb.Min.Z),
                    Max = new XYZ(cb.Max.X + extraFt, cb.Max.Y + extraFt, cb.Max.Z),
                    Transform = cb.Transform,
                };
            }
            catch { }
        }

        private static bool PlaceTwoRefDim(
            Document doc, View view, DimensionType dimType,
            double coordA, Reference? refA,
            double coordB, Reference? refB,
            bool isHoriz, double perpWorld, StringBuilder? log)
        {
            if (refA == null || refB == null) return false;

            double pad = Mm(20);
            double start = Math.Min(coordA, coordB) - pad;
            double end = Math.Max(coordA, coordB) + pad;

            XYZ ptS = WorldPt(view, start, perpWorld, isHoriz);
            XYZ ptE = WorldPt(view, end, perpWorld, isHoriz);

            Line line;
            try { line = Line.CreateBound(ptS, ptE); }
            catch (Exception ex) { log?.AppendLine($"    ✖  Line.CreateBound: {ex.Message}"); return false; }

            var refs = new ReferenceArray();
            refs.Append(refA);
            refs.Append(refB);

            return MakeDim(doc, view, refs, line, dimType, log) != null;
        }

        private static int PlaceBayDims(
            Document doc, View view, DimensionType dimType,
            List<MemberFace> clusters, bool isHoriz,
            double offsetL1, double gapFt,
            BoundingBoxXYZ originalCb, StringBuilder? log)
        {
            if (clusters.Count < 2) return 0;

            double cropEdge = isHoriz ? originalCb.Min.Y : originalCb.Min.X;
            double perp1 = cropEdge - offsetL1;
            double perp2 = cropEdge - offsetL1 - gapFt;

            int created = 0;
            double minSeg = DimConfig.MinSegment;

            // Chain 1 – bay segments
            for (int i = 0; i < clusters.Count - 1; i++)
            {
                var cur = clusters[i];
                var next = clusters[i + 1];
                double bayWidth = next.NearCoord - cur.FarCoord;
                if (bayWidth < minSeg) continue;

                if (PlaceTwoRefDim(doc, view, dimType,
                        cur.FarCoord, cur.FarRef,
                        next.NearCoord, next.NearRef,
                        isHoriz, perp1, log))
                    created++;
            }

            // Chain 2 – overall
            var first = clusters[0];
            var last = clusters[^1];

            if (PlaceTwoRefDim(doc, view, dimType,
                    first.NearCoord, first.NearRef,
                    last.FarCoord, last.FarRef,
                    isHoriz, perp2, log))
                created++;

            return created;
        }

        // ── View classification ──────────────────────────────────────────────

        private static ViewRole ClassifyView(View view)
        {
            try
            {
                ViewType vt = view.ViewType;
                if (vt == ViewType.ThreeD) return ViewRole.ThreeD;
                if (vt is ViewType.Schedule or ViewType.Legend
                       or ViewType.ColumnSchedule or ViewType.PanelSchedule)
                    return ViewRole.Other;
            }
            catch { }

            string name = view.Name.ToLowerInvariant();
            foreach (var k in DimConfig.PlanKeywords) if (name.Contains(k)) return ViewRole.Plan;
            foreach (var k in DimConfig.FrontKeywords) if (name.Contains(k)) return ViewRole.Front;
            foreach (var k in DimConfig.BackKeywords) if (name.Contains(k)) return ViewRole.Back;
            foreach (var k in DimConfig.LeftKeywords) if (name.Contains(k)) return ViewRole.Left;
            foreach (var k in DimConfig.RightKeywords) if (name.Contains(k)) return ViewRole.Right;
            return ViewRole.Other;
        }

        // ── Per-view dimension logic ─────────────────────────────────────────

        private static int DimensionElevationView(
            Document doc, View view, List<Element> members,
            DimensionType dimType, StringBuilder log)
        {
            XYZ right = view.RightDirection;
            XYZ up = view.UpDirection;
            double offsetL1 = DimConfig.OffsetL1;
            double gap = DimConfig.ChainGap;
            int total = 0;

            BoundingBoxXYZ originalCb = view.CropBox;
            ExpandCropBox(view, Mm(50));

            // Horizontal clusters
            var hClusters = GetMemberClusters(doc, members, view, right);
            if (hClusters.Count >= 2)
                total += PlaceBayDims(doc, view, dimType, hClusters,
                    isHoriz: true, offsetL1: offsetL1, gapFt: gap,
                    originalCb: originalCb, log: log);

            // Vertical clusters
            var vClusters = GetMemberClusters(doc, members, view, up);
            if (vClusters.Count >= 2)
                total += PlaceBayDims(doc, view, dimType, vClusters,
                    isHoriz: false, offsetL1: offsetL1, gapFt: gap,
                    originalCb: originalCb, log: log);

            return total;
        }

        private static int DimensionPlanView(
            Document doc, View view, List<Element> members,
            DimensionType dimType, StringBuilder log)
            => DimensionElevationView(doc, view, members, dimType, log);

        // ── Shared UI helpers (delegated to AvesUI + local methods) ─────────

        private static (List<AssemblyInstance> Assemblies, DimensionType DimType)? ShowAssemblyPicker(
            Document doc, List<AssemblyInstance> all, string title)
            => AvesUI.ShowAssemblyPicker(doc, all, title, "MT");

        private static void ShowLogWindow(string summaryText, bool error, string title)
            => AvesUI.ShowLogWindow(summaryText, error, title);
    }

    // =========================================================================
    //  BUTTON 2 – Vendor Drawings
    //
    //  Places ONE overall dimension per axis per view (no bay chains):
    //    • Width  – RightDirection span in each elevation/plan view
    //    • Height – UpDirection span in each elevation view
    //    • Depth  – RightDirection span in the plan/top view
    //               (or UpDirection in plan if depth axis maps there)
    //
    //  The result is exactly 3 readable numbers: overall H × W × D.
    // =========================================================================

    [Transaction(TransactionMode.Manual)]
    public class Cmd_VendorDimensions : IExternalCommand
    {
        // ── Configuration ────────────────────────────────────────────────────
        private static class VendorConfig
        {
            /// <summary>Dimension line offset outward from the crop box edge (ft).</summary>
            public static readonly double Offset = -200.0 / 304.8;

            public static readonly double CoordTol = 5.0 / 304.8;
            public static readonly double MaxMemberThickness = 300.0 / 304.8;

            // Vendor Drawing assemblies are built from Generic Model families.
            public static readonly HashSet<int> MemberCategories =
            [
                (int)BuiltInCategory.OST_GenericModel,
            ];

            public static readonly string[] PlanKeywords = ["top", "plan", "pianta", "sopra", "roof"];
            public static readonly string[] FrontKeywords = ["front", "fronte", "elevation", "alzato"];
            public static readonly string[] BackKeywords = ["back", "retro", "rear"];
            public static readonly string[] LeftKeywords = ["left", "sinistra", "lato sx"];
            public static readonly string[] RightKeywords = ["right", "destra", "lato dx"];
        }

        private enum ViewRole { Plan, Front, Back, Left, Right, ThreeD, Other }

        private struct AxisExtent
        {
            public double NearCoord;
            public Reference? NearRef;
            public double FarCoord;
            public Reference? FarRef;
            public bool Valid => NearRef != null && FarRef != null
                                      && FarCoord - NearCoord > VendorConfig.CoordTol;
        }

        private static double Mm(double mm) => mm / 304.8;

        // ── Execute ──────────────────────────────────────────────────────────
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            var doc = uiDoc.Document;

            // Collect target assemblies
            var selIds = uiDoc.Selection.GetElementIds();
            var assemblies = new List<AssemblyInstance>();

            foreach (var eid in selIds)
                if (doc.GetElement(eid) is AssemblyInstance ai)
                    assemblies.Add(ai);

            DimensionType? dimType;

            if (assemblies.Count == 0)
            {
                var allAsm = new FilteredElementCollector(doc)
                    .OfClass(typeof(AssemblyInstance))
                    .Cast<AssemblyInstance>()
                    .ToList();

                if (allAsm.Count == 0)
                {
                    message = "No AssemblyInstance elements found in the model.";
                    return Result.Failed;
                }

                var chosen = AvesUI.ShowAssemblyPicker(doc, allAsm,
                    "Vendor Dimension — Overall H×W×D", "Vendor");
                if (chosen == null || chosen.Value.Assemblies.Count == 0) return Result.Cancelled;
                assemblies = chosen.Value.Assemblies;
                dimType = chosen.Value.DimType;
            }
            else
            {
                dimType = AvesUI.ShowDimensionTypePicker(
                    doc, "Vendor Dimension — Overall H×W×D", "Vendor");
                if (dimType == null) return Result.Cancelled;
            }

            if (dimType == null)
            {
                message = "No Linear Dimension Type found in the project.";
                return Result.Failed;
            }

            bool anyError = false;
            var summary = new StringBuilder();

            using (var t = new Transaction(doc, "Vendor Overall Dimensions H×W×D"))
            {
                try
                {
                    t.Start();

                    foreach (var assembly in assemblies)
                    {
                        summary.AppendLine($"Assembly: {assembly.Name}");

                        var members = GetMembers(doc, assembly);
                        if (members.Count == 0)
                        {
                            summary.AppendLine("  No structural members found – skipped.");
                            summary.AppendLine();
                            continue;
                        }

                        var views = GetAssemblyViews(doc, assembly);
                        if (views.Count == 0)
                        {
                            summary.AppendLine("  No associated 2D views found – skipped.");
                            summary.AppendLine();
                            continue;
                        }

                        int assemblyDims = 0;

                        foreach (var kv in views)
                        {
                            string vname = kv.Key;
                            View view = kv.Value;
                            var role = ClassifyView(view);

                            try
                            {
                                int n = role switch
                                {
                                    ViewRole.Plan => DimensionPlanView(doc, view, members, dimType),
                                    ViewRole.Front or ViewRole.Back
                                    or ViewRole.Left or ViewRole.Right => DimensionElevationView(doc, view, members, dimType),
                                    _ => -1,   // skip
                                };

                                if (n < 0) continue;   // silently skip 3D / schedules

                                assemblyDims += n;
                                summary.AppendLine($"  {vname}  →  {n} dimension(s)");
                            }
                            catch (Exception ex)
                            {
                                summary.AppendLine($"  {vname}  →  Error: {ex.Message}");
                                anyError = true;
                            }
                        }

                        summary.AppendLine($"  Total: {assemblyDims} dimension(s)");
                        summary.AppendLine();
                    }

                    t.Commit();
                }
                catch (Exception ex)
                {
                    if (t.HasStarted()) t.RollBack();
                    message = $"Transaction failed: {ex.Message}";
                    summary.AppendLine($"Transaction failed: {ex.Message}");
                    AvesUI.ShowLogWindow(summary.ToString(), error: true,
                        title: "Vendor Dimension — Overall H×W×D");
                    return Result.Failed;
                }
            }

            AvesUI.ShowLogWindow(summary.ToString(), error: anyError,
                title: "Vendor Dimension — Overall H×W×D");
            return anyError ? Result.Failed : Result.Succeeded;
        }

        // ── Core: collect outermost face references along one axis ──────────
        //
        // Uses the IDENTICAL geometry strategy as Button 1 (CollectMemberFaces):
        //   GetSymbolGeometry() + gi.Transform for accurate face normals and coords,
        //   gi.Transform applied to get world-space positions.
        //
        // The only differences from Button 1:
        //   • No MaxMemberThickness upper-bound filter — Generic Model families
        //     can span the full assembly in any direction.
        //   • Returns only the single outermost near/far reference pair across
        //     ALL members (no bay clustering needed).

        private static (double NearCoord, Reference? NearRef,
                         double FarCoord, Reference? FarRef)
            GetOverallExtent(List<Element> members, View view, XYZ axisVec)
        {
            double tol = VendorConfig.CoordTol;
            double originProj = view.Origin.DotProduct(axisVec);

            var opts = new Options
            {
                View = view,
                ComputeReferences = true,
                IncludeNonVisibleObjects = false,
            };

            double nearCoord = double.MaxValue;
            Reference? nearRef = null;
            double farCoord = double.MinValue;
            Reference? farRef = null;

            foreach (var member in members)
            {
                GeometryElement? geom;
                try { geom = member.get_Geometry(opts); } catch { continue; }
                if (geom == null) continue;

                foreach (var obj in geom)
                {
                    if (obj is not GeometryInstance gi) continue;

                    DBTransform? xf;
                    GeometryElement? symGeom;
                    try { xf = gi.Transform; symGeom = gi.GetSymbolGeometry(); }
                    catch { continue; }
                    if (xf == null || symGeom == null) continue;

                    foreach (var item in symGeom)
                    {
                        if (item is not Solid solid || solid.Volume < 1e-9) continue;

                        foreach (Edge edge in solid.Edges)
                        {
                            try
                            {
                                // Endpoint positions in world space (symbol space + instance transform)
                                IList<XYZ> pts = edge.Tessellate();
                                XYZ p0 = xf.OfPoint(pts.First());
                                XYZ p1 = xf.OfPoint(pts.Last());
                                double c0 = p0.DotProduct(axisVec) - originProj;
                                double c1 = p1.DotProduct(axisVec) - originProj;

                                // GetEndPointReference on symbol-geometry edges returns a
                                // PointOnEdge reference that Revit resolves through the
                                // GeometryInstance to the placed instance — valid for NewDimension.
                                if (c0 < nearCoord) { var r = edge.GetEndPointReference(0); if (r != null) { nearCoord = c0; nearRef = r; } }
                                if (c1 < nearCoord) { var r = edge.GetEndPointReference(1); if (r != null) { nearCoord = c1; nearRef = r; } }
                                if (c0 > farCoord) { var r = edge.GetEndPointReference(0); if (r != null) { farCoord = c0; farRef = r; } }
                                if (c1 > farCoord) { var r = edge.GetEndPointReference(1); if (r != null) { farCoord = c1; farRef = r; } }
                            }
                            catch { }
                        }
                    }
                }
            }

            if (farCoord - nearCoord < tol) return (nearCoord, null, farCoord, null);
            return (nearCoord, nearRef, farCoord, farRef);
        }

        /// <summary>
        /// Places one overall dimension between the outermost face references
        /// along <paramref name="axisVec"/>.  Uses Button 1's proven WorldPt
        /// helper for dimension-line placement.
        /// </summary>
        private static bool PlaceOverallDim(
            Document doc, View view, DimensionType dimType,
            List<Element> members, XYZ axisVec,
            bool isHoriz, double perpWorld)
        {
            var (nearCoord, nearRef, farCoord, farRef) =
                GetOverallExtent(members, view, axisVec);

            if (nearRef == null || farRef == null) return false;
            if (farCoord - nearCoord < VendorConfig.CoordTol) return false;

            double pad = Mm(20);
            XYZ ptS = WorldPt(view, nearCoord - pad, perpWorld, isHoriz);
            XYZ ptE = WorldPt(view, farCoord + pad, perpWorld, isHoriz);

            Line line;
            try { line = Line.CreateBound(ptS, ptE); }
            catch { return false; }

            var refs = new ReferenceArray();
            refs.Append(nearRef);
            refs.Append(farRef);

            try { doc.Create.NewDimension(view, line, refs, dimType); return true; }
            catch { return false; }
        }

        // ── Elevation view: Width (horiz) + Height (vert) ───────────────────

        private static int DimensionElevationView(
            Document doc, View view,
            List<Element> members, DimensionType dimType)
        {
            BoundingBoxXYZ cb = view.CropBox;
            ExpandCropBox(view, Mm(50));

            double offset = VendorConfig.Offset;
            int created = 0;

            double perpH = cb.Min.Y + offset;
            if (PlaceOverallDim(doc, view, dimType, members,
                    view.RightDirection, isHoriz: true, perpWorld: perpH))
                created++;

            double perpV = cb.Min.X + offset;
            if (PlaceOverallDim(doc, view, dimType, members,
                    view.UpDirection, isHoriz: false, perpWorld: perpV))
                created++;

            return created;
        }

        // ── Plan / top view: Depth ───────────────────────────────────────────

        private static int DimensionPlanView(
            Document doc, View view,
            List<Element> members, DimensionType dimType)
        {
            BoundingBoxXYZ cb = view.CropBox;
            ExpandCropBox(view, Mm(50));

            double perpV = cb.Min.X + VendorConfig.Offset;
            return PlaceOverallDim(doc, view, dimType, members,
                view.UpDirection, isHoriz: false, perpWorld: perpV) ? 1 : 0;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static XYZ WorldPt(View view, double along, double perp, bool isHoriz)
        {
            XYZ o = view.Origin;
            XYZ r = view.RightDirection;
            XYZ u = view.UpDirection;
            double xL = isHoriz ? along : perp;
            double yL = isHoriz ? perp : along;
            return new XYZ(o.X + r.X * xL + u.X * yL,
                           o.Y + r.Y * xL + u.Y * yL,
                           o.Z + r.Z * xL + u.Z * yL);
        }

        private static void ExpandCropBox(View view, double extraFt)
        {
            try
            {
                BoundingBoxXYZ cb = view.CropBox;
                view.CropBox = new BoundingBoxXYZ
                {
                    Min = new XYZ(cb.Min.X - extraFt, cb.Min.Y - extraFt, cb.Min.Z),
                    Max = new XYZ(cb.Max.X + extraFt, cb.Max.Y + extraFt, cb.Max.Z),
                    Transform = cb.Transform,
                };
            }
            catch { }
        }

        private static Dictionary<string, View> GetAssemblyViews(
            Document doc, AssemblyInstance assembly)
        {
            var views = new Dictionary<string, View>();
            var assemId = assembly.Id;

            foreach (var v in new FilteredElementCollector(doc)
                                  .OfClass(typeof(View)).Cast<View>())
            {
                try
                {
                    if (v.IsTemplate) continue;
                    var aid = v.AssemblyInstanceId;
                    if (aid != null && aid == assemId) views[v.Name] = v;
                }
                catch { }
            }
            if (views.Count > 0) return views;

            foreach (var v in new FilteredElementCollector(doc)
                                  .OfClass(typeof(View)).Cast<View>())
            {
                try
                {
                    if (v.IsTemplate) continue;
                    if (v.Name.StartsWith(assembly.Name, StringComparison.OrdinalIgnoreCase))
                        views[v.Name] = v;
                }
                catch { }
            }
            return views;
        }

        private static List<Element> GetMembers(Document doc, AssemblyInstance assembly)
        {
            var members = new List<Element>();
            foreach (var mid in assembly.GetMemberIds())
            {
                var elem = doc.GetElement(mid);
                if (elem?.Category == null) continue;
                if (VendorConfig.MemberCategories.Contains((int)elem.Category.Id.Value))
                    members.Add(elem);
            }
            return members;
        }

        private static ViewRole ClassifyView(View view)
        {
            try
            {
                ViewType vt = view.ViewType;
                if (vt == ViewType.ThreeD) return ViewRole.ThreeD;
                if (vt is ViewType.Schedule or ViewType.Legend
                       or ViewType.ColumnSchedule or ViewType.PanelSchedule)
                    return ViewRole.Other;
            }
            catch { }

            string name = view.Name.ToLowerInvariant();
            foreach (var k in VendorConfig.PlanKeywords) if (name.Contains(k)) return ViewRole.Plan;
            foreach (var k in VendorConfig.FrontKeywords) if (name.Contains(k)) return ViewRole.Front;
            foreach (var k in VendorConfig.BackKeywords) if (name.Contains(k)) return ViewRole.Back;
            foreach (var k in VendorConfig.LeftKeywords) if (name.Contains(k)) return ViewRole.Left;
            foreach (var k in VendorConfig.RightKeywords) if (name.Contains(k)) return ViewRole.Right;
            return ViewRole.Other;
        }
    }

    // =========================================================================
    //  AVES UI DESIGN SYSTEM
    //  Shared assembly picker, log window, and widget factory.
    //  Both commands delegate all WPF work here.
    // =========================================================================

    internal static class AvesUI
    {
        // ── Widgets ───────────────────────────────────────────────────────────
        // StatTile and buttons routed to AvControls (Family B); geometry was
        // already Family-B-shaped here except padding, which Core's factories
        // fix automatically. Kept as thin same-name wrappers (rather than
        // updating every call site to an AvControls.-qualified call) since
        // several callers are nested Window classes that reach these via
        // unqualified enclosing-type lookup. StatusPill keeps its
        // labelOverride wrapper to preserve this file's "fail"->"Error"
        // wording (reconciliation decision: unify styling, keep existing
        // per-file wording).
        public static Border StatusPill(string kind) =>
            AvControls.StatusPill(kind, kind == "fail" ? "Error" : null);

        public static Border StatTile(string value, string label, string accent = "") =>
            AvControls.StatTile(value, label, accent);

        public static Button PrimaryButton(string text) => AvControls.PrimaryButton(text);
        public static Button SecondaryButton(string text) => AvControls.SecondaryButton(text);
        public static Button GhostButton(string text) => AvControls.GhostButton(text);

        // ── Dimension Type Memory ────────────────────────────────────────────
        //  Remembers the last linear dimension type picked, per button
        //  (MT vs Vendor use separate keys), so returning users get it
        //  pre-selected next time. Stored per-Windows-user in
        //  %AppData%\AVES\last_dim_type.txt as "Key<TAB>Label" lines.
        private static class DimTypeMemory
        {
            private static readonly string ConfigDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVES");

            private static readonly string ConfigFile =
                System.IO.Path.Combine(ConfigDir, "last_dim_type.txt");

            internal static string? TryGet(string key)
            {
                try
                {
                    if (!System.IO.File.Exists(ConfigFile)) return null;
                    string prefix = key + "\t";
                    foreach (string line in System.IO.File.ReadAllLines(ConfigFile))
                        if (line.StartsWith(prefix)) return line.Substring(prefix.Length);
                }
                catch { }
                return null;
            }

            internal static void Save(string key, string label)
            {
                try
                {
                    System.IO.Directory.CreateDirectory(ConfigDir);
                    string prefix = key + "\t";
                    var lines = System.IO.File.Exists(ConfigFile)
                        ? System.IO.File.ReadAllLines(ConfigFile).Where(l => !l.StartsWith(prefix)).ToList()
                        : new List<string>();
                    lines.Add(prefix + label);
                    System.IO.File.WriteAllLines(ConfigFile, lines);
                }
                catch { /* Non-fatal — worst case the user just picks again next run. */ }
            }
        }

        // ── Dimension Type Section ───────────────────────────────────────────
        //  Field control (label + button) that opens a modal DimTypePickerWindow
        //  for choosing which linear Dimension Type to draw with. Reused both
        //  inline in the assembly picker window and in the standalone
        //  ShowDimensionTypePicker window for the "assemblies already
        //  selected in Revit" fast path.
        private sealed class DimTypeSection
        {
            public Border Control { get; }
            public ElementId SelectedId { get; private set; } = ElementId.InvalidElementId;
            public event Action? Changed;

            private readonly List<(string Label, ElementId Id)> _items;
            private readonly Button _btn;
            private readonly string _memoryKey;

            public DimTypeSection(Document doc, string sectionLabel, string memoryKey, int badgeNumber = 1)
            {
                _memoryKey = memoryKey;

                _items = new FilteredElementCollector(doc)
                    .OfClass(typeof(DimensionType))
                    .Cast<DimensionType>()
                    .Where(dt => { try { return dt.StyleType == DimensionStyleType.Linear; } catch { return false; } })
                    .OrderBy(dt => dt.Name)
                    .Select(dt => (Label: dt.Name, Id: dt.Id))
                    .ToList();

                bool hasItems = _items.Count > 0;

                var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
                var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
                titleRow.Children.Add(AvControls.NumberBadge(badgeNumber));
                titleRow.Children.Add(new TextBlock
                {
                    Text = sectionLabel,
                    FontFamily = AvTokens.Ui,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = AvTokens.B(AvTokens.TextFaint),
                    VerticalAlignment = VerticalAlignment.Center
                });
                stack.Children.Add(titleRow);

                _btn = new Button
                {
                    FontFamily = AvTokens.Ui,
                    FontSize = 13,
                    Foreground = AvTokens.B(AvTokens.TextFaint),
                    Background = AvTokens.B(AvTokens.Surface),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10, 8, 10, 8),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Content = hasItems ? "Select dimension type…" : "No linear Dimension Types found in the project.",
                    IsEnabled = hasItems,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Template = FieldButtonTemplate()
                };

                if (hasItems)
                {
                    string? remembered = DimTypeMemory.TryGet(_memoryKey);
                    int idx = remembered == null ? -1 : _items.FindIndex(it => it.Label == remembered);
                    if (idx >= 0)
                    {
                        SelectedId = _items[idx].Id;
                        _btn.Content = _items[idx].Label;
                        _btn.Foreground = AvTokens.B(AvTokens.Text);
                    }
                }

                _btn.Click += (s, e) =>
                {
                    var picker = new DimTypePickerWindow(
                        sectionLabel,
                        _items.Select(it => it.Label).ToList(),
                        _btn.Content as string == "Select dimension type…" ? null : _btn.Content as string);

                    if (picker.ShowDialog() == true && picker.SelectedLabel != null)
                    {
                        var picked = _items.First(it => it.Label == picker.SelectedLabel);
                        SelectedId = picked.Id;
                        _btn.Content = picked.Label;
                        _btn.Foreground = AvTokens.B(AvTokens.Text);
                        DimTypeMemory.Save(_memoryKey, picked.Label);
                        Changed?.Invoke();
                    }
                };

                stack.Children.Add(_btn);
                Control = new Border { Child = stack };
            }
        }

        // Rounded, bordered, hover-highlighted look shared by SecondaryButton and
        // any clickable "field" control (e.g. the dimension-type picker trigger).
        private static ControlTemplate FieldButtonTemplate()
        {
            var tpl = new ControlTemplate(typeof(Button));
            var bd = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            bd.Name = "bd";
            bd.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(5));
            bd.SetValue(System.Windows.Controls.Border.BackgroundProperty, AvTokens.B(AvTokens.Surface));
            bd.SetValue(System.Windows.Controls.Border.BorderBrushProperty, AvTokens.B(AvTokens.BorderStrong));
            bd.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetBinding(ContentPresenter.MarginProperty,
                new System.Windows.Data.Binding("Padding")
                { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            bd.AppendChild(cp);
            tpl.VisualTree = bd;
            var trigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(System.Windows.Controls.Border.BackgroundProperty, AvTokens.B(AvTokens.Surface2), "bd"));
            tpl.Triggers.Add(trigger);
            return tpl;
        }

        // ── Dimension Type Picker Window ─────────────────────────────────────
        //  Modal popup used by DimTypeSection: search box + scrollable single-
        //  select list with a reliably-visible colored selection background
        //  (a plain ListBoxItem's default chrome ignores a Background setter
        //  for the selected state, so we supply a custom Border-based template).
        private sealed class DimTypePickerWindow : Window
        {
            public string? SelectedLabel { get; private set; }

            private readonly List<string> _allLabels;
            private readonly string? _currentLabel;
            private bool _initialSelectionApplied;
            private WinTextBox _searchBox = null!;
            private ListBox _listBox = null!;
            private Button _selectBtn = null!;

            public DimTypePickerWindow(string title, List<string> labels, string? currentLabel)
            {
                _allLabels = labels;
                _currentLabel = currentLabel;

                Title = title;
                Width = 420; Height = 520;
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.NoResize;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Background = AvTokens.B(AvTokens.Surface); FontFamily = AvTokens.Ui;
                UseLayoutRounding = true; SnapsToDevicePixels = true;
                ShowInTaskbar = false;

                var root = new DockPanel();

                var tb = AvControls.TitleBar(title);
                DockPanel.SetDock(tb, Dock.Top);
                root.Children.Add(tb);

                var footer = BuildFooter();
                DockPanel.SetDock(footer, Dock.Bottom);
                root.Children.Add(footer);

                var body = new DockPanel { Margin = new Thickness(20, 16, 20, 0) };

                var search = BuildSearchBox();
                DockPanel.SetDock(search, Dock.Top);
                body.Children.Add(search);

                _listBox = new ListBox
                {
                    FontFamily = AvTokens.Ui,
                    FontSize = 13,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Focusable = true
                };
                var itemStyle = new Style(typeof(ListBoxItem));
                itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(12, 9, 12, 9)));
                itemStyle.Setters.Add(new Setter(ListBoxItem.FontFamilyProperty, AvTokens.Ui));
                itemStyle.Setters.Add(new Setter(ListBoxItem.FontSizeProperty, 13.0));
                itemStyle.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, AvTokens.B(AvTokens.Text)));
                itemStyle.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, Brushes.Transparent));

                var itemTemplate = new ControlTemplate(typeof(ListBoxItem));
                var itemBorder = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
                itemBorder.Name = "RowBorder";
                itemBorder.SetBinding(System.Windows.Controls.Border.BackgroundProperty,
                    new System.Windows.Data.Binding("Background")
                    { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
                itemBorder.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(4));
                itemBorder.SetBinding(System.Windows.Controls.Border.PaddingProperty,
                    new System.Windows.Data.Binding("Padding")
                    { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
                var itemContent = new FrameworkElementFactory(typeof(ContentPresenter));
                itemContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                itemBorder.AppendChild(itemContent);
                itemTemplate.VisualTree = itemBorder;
                itemStyle.Setters.Add(new Setter(ListBoxItem.TemplateProperty, itemTemplate));

                var selectedTrigger = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
                selectedTrigger.Setters.Add(new Setter(ListBoxItem.BackgroundProperty, AvTokens.B(AvTokens.AccentTint)));
                selectedTrigger.Setters.Add(new Setter(ListBoxItem.ForegroundProperty, AvTokens.B(AvTokens.Accent)));
                itemStyle.Triggers.Add(selectedTrigger);
                _listBox.ItemContainerStyle = itemStyle;
                _listBox.SelectionChanged += (s, e) => _selectBtn.IsEnabled = _listBox.SelectedItem != null;
                _listBox.MouseDoubleClick += (s, e) => { if (_listBox.SelectedItem != null) OnSelect(); };

                var scroll = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = _listBox,
                    Focusable = false
                };
                scroll.PreviewMouseWheel += (s, e) =>
                {
                    scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 0.5);
                    e.Handled = true;
                };

                var listBorder = new Border
                {
                    Background = AvTokens.B(AvTokens.Surface),
                    BorderBrush = AvTokens.B(AvTokens.Border),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Margin = new Thickness(0, 10, 0, 16),
                    Child = scroll
                };
                body.Children.Add(listBorder);
                root.Children.Add(body);
                Content = root;

                RefreshList();

                Loaded += (s, e) => _searchBox.Focus();
                KeyDown += (s, e) =>
                {
                    if (e.Key == System.Windows.Input.Key.Escape) DialogResult = false;
                    if (e.Key == System.Windows.Input.Key.Enter && _selectBtn.IsEnabled) OnSelect();
                };
            }

            private Border BuildSearchBox()
            {
                var g = new System.Windows.Controls.Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var icon = new TextBlock
                {
                    Text = "⌕",
                    FontSize = 15,
                    Foreground = AvTokens.B(AvTokens.TextFaint),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                System.Windows.Controls.Grid.SetColumn(icon, 0);

                _searchBox = new WinTextBox
                {
                    FontFamily = AvTokens.Ui,
                    FontSize = 13,
                    Foreground = AvTokens.B(AvTokens.Text),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(0, 8, 10, 8),
                    ToolTip = "Filter dimension types by name"
                };
                _searchBox.TextChanged += (s, e) => RefreshList();
                System.Windows.Controls.Grid.SetColumn(_searchBox, 1);

                g.Children.Add(icon); g.Children.Add(_searchBox);
                return new Border
                {
                    Background = AvTokens.B(AvTokens.Surface2),
                    BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Child = g
                };
            }

            private UIElement BuildFooter()
            {
                var footerBorder = new Border
                {
                    Background = AvTokens.B(AvTokens.Surface2),
                    BorderBrush = AvTokens.B(AvTokens.Border),
                    BorderThickness = new Thickness(0, 1, 0, 0),
                    Padding = new Thickness(20, 14, 20, 14)
                };

                var inner = new DockPanel { LastChildFill = false };

                var btns = new StackPanel { Orientation = Orientation.Horizontal };
                DockPanel.SetDock(btns, Dock.Right);

                var cancelBtn = GhostButton("Cancel");
                cancelBtn.Margin = new Thickness(0, 0, 10, 0);
                cancelBtn.Click += (s, e) => DialogResult = false;

                _selectBtn = PrimaryButton("Select");
                _selectBtn.IsEnabled = false;
                _selectBtn.Click += (s, e) => OnSelect();

                btns.Children.Add(cancelBtn); btns.Children.Add(_selectBtn);
                inner.Children.Add(btns);
                footerBorder.Child = inner;
                return footerBorder;
            }

            private void RefreshList()
            {
                string q = (_searchBox.Text ?? "").Trim().ToLowerInvariant();
                string? previouslySelected = _listBox.SelectedItem as string;

                var filtered = string.IsNullOrEmpty(q)
                    ? _allLabels
                    : _allLabels.Where(l => l.ToLowerInvariant().Contains(q)).ToList();

                _listBox.Items.Clear();
                foreach (var label in filtered) _listBox.Items.Add(label);

                string? toSelect = previouslySelected;
                if (!_initialSelectionApplied)
                {
                    _initialSelectionApplied = true;
                    toSelect = _currentLabel;
                }

                if (toSelect != null && filtered.Contains(toSelect))
                {
                    _listBox.SelectedItem = toSelect;
                    _listBox.ScrollIntoView(toSelect);
                }
                _selectBtn.IsEnabled = _listBox.SelectedItem != null;
            }

            private void OnSelect()
            {
                if (_listBox.SelectedItem is string label)
                {
                    SelectedLabel = label;
                    DialogResult = true;
                }
            }
        }

        // ── Standalone Dimension Type Picker ─────────────────────────────────
        //  Used when assemblies were already selected in Revit (so the full
        //  assembly picker window is skipped) but a dimension type still
        //  needs to be chosen.
        public static DimensionType? ShowDimensionTypePicker(
            Document doc, string title, string memoryKey)
        {
            var section = new DimTypeSection(doc, "Dimension type", memoryKey);

            var cancelBtn = GhostButton("Cancel");
            cancelBtn.Margin = new Thickness(0, 0, 10, 0);
            var confirmBtn = PrimaryButton("Confirm");
            confirmBtn.IsEnabled = section.SelectedId != ElementId.InvalidElementId;
            section.Changed += () => confirmBtn.IsEnabled = section.SelectedId != ElementId.InvalidElementId;

            var footerBtns = new StackPanel { Orientation = Orientation.Horizontal };
            footerBtns.Children.Add(cancelBtn); footerBtns.Children.Add(confirmBtn);
            var footerInner = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(footerBtns, Dock.Right);
            footerInner.Children.Add(footerBtns);

            var footerStack = new StackPanel();
            footerStack.Children.Add(AvControls.HDivider());
            footerStack.Children.Add(new Border { Background = AvTokens.B(AvTokens.Surface2), Child = new Border { Margin = new Thickness(20, 14, 20, 14), Child = footerInner } });

            var body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            body.Children.Add(section.Control);

            var root = new DockPanel();
            var titleBar = AvControls.TitleBar(title);
            DockPanel.SetDock(titleBar, Dock.Top);
            DockPanel.SetDock(footerStack, Dock.Bottom);
            root.Children.Add(titleBar); root.Children.Add(footerStack); root.Children.Add(body);

            var win = new Window
            {
                Title = title,
                Content = root,
                Width = 520,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.SingleBorderWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = AvTokens.B(AvTokens.Surface),
                FontFamily = AvTokens.Ui,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                ShowInTaskbar = false,
            };

            bool confirmed = false;
            confirmBtn.Click += (_, _) => { confirmed = true; win.Close(); };
            cancelBtn.Click += (_, _) => win.Close();
            win.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) win.Close();
                if (e.Key == System.Windows.Input.Key.Enter && confirmBtn.IsEnabled) { confirmed = true; win.Close(); }
            };

            win.ShowDialog();
            if (!confirmed || section.SelectedId == ElementId.InvalidElementId) return null;
            return doc.GetElement(section.SelectedId) as DimensionType;
        }

        // ── Assembly Picker ───────────────────────────────────────────────────

        /// <summary>
        /// Multi-select assembly picker with live search and checkbox rows.
        /// </summary>
        public static (List<AssemblyInstance> Assemblies, DimensionType DimType)? ShowAssemblyPicker(
            Document doc, List<AssemblyInstance> all, string title, string dimTypeMemoryKey)
        {
            // ── Build flat entry list with dedup labels ───────────────────────────────
            var labelToAsm = new Dictionary<string, AssemblyInstance>();
            var nameCounts = new Dictionary<string, int>();
            foreach (var a in all)
            {
                string baseName = string.IsNullOrWhiteSpace(a.Name) ? "(unnamed)" : a.Name;
                nameCounts.TryGetValue(baseName, out int count);
                nameCounts[baseName] = count + 1;
                string label = count == 0 ? baseName : $"{baseName} ({count + 1})";
                labelToAsm[label] = a;
            }
            var entries = labelToAsm.OrderBy(kv => kv.Key).ToList();

            // ── Dimension type section (compact picker embedded in this window) ────────
            var dimTypeSection = new DimTypeSection(doc, "Dimension type", dimTypeMemoryKey);

            // ── State ─────────────────────────────────────────────────────────────────
            var checkedLabels = new HashSet<string>();
            var rowMap = new Dictionary<string, (Border Row, System.Windows.Controls.CheckBox Box)>();
            int checkedCount = 0;

            TextBlock counterLbl = null!;
            Button confirmBtn = null!;

            void RefreshCounter()
            {
                counterLbl.Text = checkedCount == 1 ? "1 selected" : $"{checkedCount} selected";
                counterLbl.Foreground = AvTokens.B(checkedCount > 0 ? AvTokens.Accent : AvTokens.TextDim);
                confirmBtn.IsEnabled = checkedCount > 0 && dimTypeSection.SelectedId != ElementId.InvalidElementId;
            }

            dimTypeSection.Changed += RefreshCounter;

            // ── List panel ────────────────────────────────────────────────────────────
            var listPanel = new StackPanel();

            void PopulateList(string query)
            {
                listPanel.Children.Clear();
                var filtered = string.IsNullOrEmpty(query)
                    ? entries
                    : entries.Where(kv => kv.Key.ToLowerInvariant().Contains(query.ToLowerInvariant())).ToList();

                bool first = true;
                foreach (var kv in filtered)
                {
                    string lbl = kv.Key;
                    if (!rowMap.TryGetValue(lbl, out var rb))
                    {
                        var checkSquare = new Border
                        {
                            Width = 17,
                            Height = 17,
                            CornerRadius = new CornerRadius(4),
                            BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                            BorderThickness = new Thickness(1.5),
                            Background = Brushes.Transparent,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(14, 0, 12, 0)
                        };
                        var checkMark = new TextBlock
                        {
                            Text = "✓",
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = Brushes.White,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            Visibility = WpfVisibility.Hidden
                        };
                        checkSquare.Child = checkMark;

                        var nameLabel = new TextBlock
                        {
                            Text = lbl,
                            FontFamily = AvTokens.Ui,
                            FontSize = 13,
                            Foreground = AvTokens.B(AvTokens.Text),
                            VerticalAlignment = VerticalAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            ToolTip = lbl
                        };

                        var grid = new System.Windows.Controls.Grid { Height = 44 };
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(43) });
                        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        System.Windows.Controls.Grid.SetColumn(checkSquare, 0);
                        System.Windows.Controls.Grid.SetColumn(nameLabel, 1);
                        grid.Children.Add(checkSquare); grid.Children.Add(nameLabel);

                        var rowBorder = new Border
                        {
                            Background = Brushes.Transparent,
                            Child = grid
                        };

                        var box = new System.Windows.Controls.CheckBox
                        { Width = 0, Height = 0, Opacity = 0, IsChecked = checkedLabels.Contains(lbl) };

                        box.Checked += (s, e) =>
                        {
                            checkedLabels.Add(lbl); checkedCount = checkedLabels.Count;
                            checkSquare.Background = AvTokens.B(AvTokens.Accent); checkSquare.BorderBrush = AvTokens.B(AvTokens.Accent);
                            checkMark.Visibility = WpfVisibility.Visible;
                            nameLabel.Foreground = AvTokens.B(AvTokens.Accent); nameLabel.FontWeight = FontWeights.SemiBold;
                            rowBorder.Background = AvTokens.B(AvTokens.AccentTint);
                            RefreshCounter();
                        };
                        box.Unchecked += (s, e) =>
                        {
                            checkedLabels.Remove(lbl); checkedCount = checkedLabels.Count;
                            checkSquare.Background = Brushes.Transparent; checkSquare.BorderBrush = AvTokens.B(AvTokens.BorderStrong);
                            checkMark.Visibility = WpfVisibility.Hidden;
                            nameLabel.Foreground = AvTokens.B(AvTokens.Text); nameLabel.FontWeight = FontWeights.Normal;
                            rowBorder.Background = Brushes.Transparent;
                            RefreshCounter();
                        };

                        if (checkedLabels.Contains(lbl))
                        {
                            checkSquare.Background = AvTokens.B(AvTokens.Accent); checkSquare.BorderBrush = AvTokens.B(AvTokens.Accent);
                            checkMark.Visibility = WpfVisibility.Visible;
                            nameLabel.Foreground = AvTokens.B(AvTokens.Accent); nameLabel.FontWeight = FontWeights.SemiBold;
                            rowBorder.Background = AvTokens.B(AvTokens.AccentTint);
                        }

                        rowBorder.MouseLeftButtonUp += (s, e) => { box.IsChecked = !(box.IsChecked == true); e.Handled = true; };
                        rowBorder.MouseEnter += (s, e) => { if (box.IsChecked != true) rowBorder.Background = AvTokens.B(AvTokens.Surface2); };
                        rowBorder.MouseLeave += (s, e) => { if (box.IsChecked != true) rowBorder.Background = Brushes.Transparent; };

                        rb = (rowBorder, box);
                        rowMap[lbl] = rb;
                    }

                    listPanel.Children.Add(rb.Row);
                    first = false;
                }

                if (filtered.Count == 0)
                    listPanel.Children.Add(new TextBlock
                    {
                        Text = "No assemblies match.",
                        FontFamily = AvTokens.Ui,
                        FontSize = 12.5,
                        Foreground = AvTokens.B(AvTokens.TextFaint),
                        Margin = new Thickness(16, 14, 16, 14),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
            }

            // ── Select Assemblies header ─────────────────────────────────────────────
            var assembliesTitleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 10) };
            assembliesTitleRow.Children.Add(AvControls.NumberBadge(2));
            assembliesTitleRow.Children.Add(new TextBlock
            {
                Text = "Select Assemblies",
                FontFamily = AvTokens.Ui,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Center
            });

            // ── Search box ────────────────────────────────────────────────────────────
            string placeholder = "Search assemblies…";

            var searchGrid = new System.Windows.Controls.Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var searchIcon = new TextBlock
            {
                Text = "⌕",
                FontSize = 15,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            System.Windows.Controls.Grid.SetColumn(searchIcon, 0);

            var searchInput = new WinTextBox
            {
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                Text = placeholder,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0, 8, 10, 8)
            };
            searchInput.GotFocus += (s, e) => { if (searchInput.Text == placeholder) { searchInput.Text = ""; searchInput.Foreground = AvTokens.B(AvTokens.Text); } };
            searchInput.LostFocus += (s, e) => { if (string.IsNullOrWhiteSpace(searchInput.Text)) { searchInput.Text = placeholder; searchInput.Foreground = AvTokens.B(AvTokens.TextFaint); } };
            searchInput.TextChanged += (s, e) => PopulateList(searchInput.Text == placeholder ? "" : searchInput.Text.Trim());
            System.Windows.Controls.Grid.SetColumn(searchInput, 1);
            searchGrid.Children.Add(searchIcon); searchGrid.Children.Add(searchInput);

            var searchBorder = new Border
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 0, 0, 8),
                Child = searchGrid
            };

            // ── 6. Toolbar (bordered Select All / Deselect All + right-aligned counter) ─
            counterLbl = new TextBlock { FontFamily = AvTokens.Ui, FontSize = 12, Foreground = AvTokens.B(AvTokens.Accent), VerticalAlignment = VerticalAlignment.Center };

            var selAllBtn = SecondaryButton("Select All");
            selAllBtn.FontSize = 12;
            selAllBtn.Foreground = AvTokens.B(AvTokens.Accent);
            selAllBtn.Padding = new Thickness(12, 5, 12, 5);
            selAllBtn.Click += (s, e) =>
            {
                foreach (var kv in rowMap)
                    if (listPanel.Children.Contains(kv.Value.Row))
                        kv.Value.Box.IsChecked = true;
            };

            var deselAllBtn = SecondaryButton("Deselect All");
            deselAllBtn.FontSize = 12;
            deselAllBtn.Foreground = AvTokens.B(AvTokens.TextDim);
            deselAllBtn.Padding = new Thickness(12, 5, 12, 5);
            deselAllBtn.Margin = new Thickness(8, 0, 0, 0);
            deselAllBtn.Click += (s, e) =>
            {
                foreach (var kv in rowMap)
                    if (listPanel.Children.Contains(kv.Value.Row))
                        kv.Value.Box.IsChecked = false;
            };

            var toolbarLeft = new StackPanel { Orientation = Orientation.Horizontal };
            toolbarLeft.Children.Add(selAllBtn); toolbarLeft.Children.Add(deselAllBtn);

            var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 8), LastChildFill = false };
            DockPanel.SetDock(toolbarLeft, Dock.Left);
            DockPanel.SetDock(counterLbl, Dock.Right);
            toolbar.Children.Add(toolbarLeft); toolbar.Children.Add(counterLbl);

            // ── 7. Scrollable list ────────────────────────────────────────────────────
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = listPanel };
            scroll.PreviewMouseWheel += (s, e) => { scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 0.5); e.Handled = true; };
            var listBorder = new Border { Background = AvTokens.B(AvTokens.Surface), BorderBrush = AvTokens.B(AvTokens.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), MaxHeight = 360, Child = scroll };

            // ── 8. Footer ─────────────────────────────────────────────────────────────
            var cancelBtn = GhostButton("Cancel");
            cancelBtn.Margin = new Thickness(0, 0, 10, 0);

            confirmBtn = PrimaryButton("▶  Run Script");
            confirmBtn.IsEnabled = false;

            var footerBtns = new StackPanel { Orientation = Orientation.Horizontal };
            footerBtns.Children.Add(cancelBtn); footerBtns.Children.Add(confirmBtn);

            var footerInner = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(footerBtns, Dock.Right);
            footerInner.Children.Add(footerBtns);

            var footerStack = new StackPanel();
            footerStack.Children.Add(AvControls.HDivider());
            footerStack.Children.Add(new Border { Background = AvTokens.B(AvTokens.Surface2), Child = new Border { Margin = new Thickness(20, 14, 20, 14), Child = footerInner } });

            // ── 9. Window layout ──────────────────────────────────────────────────────
            var body = new DockPanel { Margin = new Thickness(20, 16, 20, 0) };
            DockPanel.SetDock(dimTypeSection.Control, Dock.Top);
            DockPanel.SetDock(assembliesTitleRow, Dock.Top);
            DockPanel.SetDock(searchBorder, Dock.Top);
            DockPanel.SetDock(toolbar, Dock.Top);
            body.Children.Add(dimTypeSection.Control); body.Children.Add(assembliesTitleRow); body.Children.Add(searchBorder); body.Children.Add(toolbar); body.Children.Add(listBorder);

            var root = new DockPanel();
            var titleBar = AvControls.TitleBar(title);
            DockPanel.SetDock(titleBar, Dock.Top);
            DockPanel.SetDock(footerStack, Dock.Bottom);
            root.Children.Add(titleBar); root.Children.Add(footerStack); root.Children.Add(body);

            var win = new Window
            {
                Title = title,
                Content = root,
                Width = 520,
                Height = 700,
                WindowStyle = WindowStyle.SingleBorderWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = AvTokens.B(AvTokens.Surface),
                FontFamily = AvTokens.Ui,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                ShowInTaskbar = false,
            };

            PopulateList("");
            RefreshCounter();

            bool confirmed = false;
            confirmBtn.Click += (_, _) => { confirmed = true; win.Close(); };
            cancelBtn.Click += (_, _) => win.Close();
            win.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) win.Close();
                if (e.Key == System.Windows.Input.Key.Enter && confirmBtn.IsEnabled) { confirmed = true; win.Close(); }
            };

            win.ShowDialog();
            if (!confirmed || dimTypeSection.SelectedId == ElementId.InvalidElementId) return null;
            if (doc.GetElement(dimTypeSection.SelectedId) is not DimensionType chosenDimType) return null;

            var chosenAssemblies = entries
                .Where(kv => checkedLabels.Contains(kv.Key))
                .Select(kv => kv.Value)
                .ToList();

            return (chosenAssemblies, chosenDimType);
        }

        // ── Log / Results Window ──────────────────────────────────────────────

        public static void ShowLogWindow(string summaryText, bool error, string title)
        {
            var rows = new List<(string Assembly, string View, string Count, string Status, string Notes)>();
            string? currentAssembly = null;
            int totalDims = 0, totalErrors = 0;

            foreach (var rawLine in summaryText.Split('\n'))
            {
                string line = rawLine.TrimEnd();
                if (line.StartsWith("Assembly: "))
                    currentAssembly = line.Substring("Assembly: ".Length).Trim();
                else if (line.TrimStart().StartsWith("Total: "))
                {
                    string val = line.TrimStart().Substring("Total: ".Length)
                        .Replace("chain(s)", "").Replace("dimension(s)", "").Trim();
                    if (int.TryParse(val, out int n)) totalDims += n;
                }
                else if (line.Contains("→") || line.Contains("->"))
                {
                    string trimmed = line.TrimStart();
                    int arrow = trimmed.IndexOf("→");
                    if (arrow < 0) arrow = trimmed.IndexOf("->");
                    if (arrow < 0) continue;
                    string vname = trimmed.Substring(0, arrow).Trim();
                    string result = trimmed.Substring(arrow + 1).Trim();
                    bool isError = result.StartsWith("Error:");
                    if (isError) totalErrors++;
                    rows.Add((
                        Assembly: currentAssembly ?? "",
                        View: vname,
                        Count: isError ? "—" : result.Replace("chain(s)", "").Replace("dimension(s)", "").Trim(),
                        Status: isError ? "fail" : "ok",
                        Notes: isError ? result.Substring("Error:".Length).Trim() : ""
                    ));
                }
                else if (!string.IsNullOrWhiteSpace(line) && currentAssembly != null
                         && (line.Contains("No structural") || line.Contains("No associated")))
                {
                    rows.Add((currentAssembly, "(all views)", "—", "skip", line.Trim()));
                }
            }

            int totalViews = rows.Count;

            var body = new StackPanel { Margin = new Thickness(20, 20, 20, 20) };
            body.Children.Add(AvControls.SectionLabel("Run Summary"));

            var tileGrid = new UniformGrid { Columns = 3, Rows = 1, Margin = new Thickness(0, 0, 0, 20) };
            var t1 = StatTile(totalDims.ToString(), "Dims created", totalDims > 0 ? "ok" : "");
            var t2 = StatTile(totalErrors.ToString(), "Errors", totalErrors > 0 ? "fail" : "");
            var t3 = StatTile(totalViews.ToString(), "Views processed", "");
            t1.Margin = new Thickness(0, 0, 8, 8); t2.Margin = new Thickness(0, 0, 8, 8);
            tileGrid.Children.Add(t1); tileGrid.Children.Add(t2); tileGrid.Children.Add(t3);
            body.Children.Add(tileGrid);

            body.Children.Add(AvControls.SectionLabel("Results per view"));
            body.Children.Add(BuildResultsTable(rows));

            var scrollBody = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = body
            };

            var caption = new TextBlock
            {
                Text = error ? "Completed with errors" : "Completed successfully",
                FontFamily = AvTokens.Ui,
                FontSize = 11.5,
                Foreground = AvTokens.B(error ? AvTokens.FailFg : AvTokens.OkFg),
                VerticalAlignment = VerticalAlignment.Center
            };

            Button? copyBtn = null;
            copyBtn = SecondaryButton("⧉  Copy log");
            copyBtn.Margin = new Thickness(0, 0, 10, 0);
            System.Windows.Threading.DispatcherTimer? copyTimer = null;
            string logText = summaryText;
            copyBtn.Click += (s, e) =>
            {
                try { System.Windows.Clipboard.SetText(logText); } catch { }
                copyBtn!.Content = "✓  Copied!"; copyBtn.IsEnabled = false;
                copyTimer?.Stop();
                copyTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                copyTimer.Tick += (ts, te) => { copyTimer.Stop(); copyBtn!.Content = "⧉  Copy log"; copyBtn.IsEnabled = true; };
                copyTimer.Start();
            };

            var closeBtn = PrimaryButton("Close");
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            btns.Children.Add(copyBtn); btns.Children.Add(closeBtn);

            var footerInner = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(caption, Dock.Left);
            DockPanel.SetDock(btns, Dock.Right);
            footerInner.Children.Add(caption); footerInner.Children.Add(btns);

            var footerStack = new StackPanel();
            footerStack.Children.Add(AvControls.HDivider());
            footerStack.Children.Add(new Border { Background = AvTokens.B(AvTokens.Surface2), Child = new Border { Margin = new Thickness(20, 14, 20, 14), Child = footerInner } });

            var root = new DockPanel();
            var titleBar_ = AvControls.TitleBar(
                error ? $"{title} — Completed with errors" : $"{title} — Done");

            DockPanel.SetDock(titleBar_, Dock.Top);
            DockPanel.SetDock(footerStack, Dock.Bottom);
            root.Children.Add(titleBar_); root.Children.Add(footerStack); root.Children.Add(scrollBody);

            var win = new Window
            {
                Title = error ? $"{title} – Errors" : $"{title} – Done",
                Content = root,
                Width = 680,
                MaxHeight = 700,
                SizeToContent = SizeToContent.Height,
                WindowStyle = WindowStyle.SingleBorderWindow,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = AvTokens.B(AvTokens.Surface),
                FontFamily = AvTokens.Ui,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true,
                ShowInTaskbar = false,
            };

            closeBtn.Click += (_, _) => win.Close();
            win.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Escape) win.Close(); };
            win.ShowDialog();
        }

        private static Border BuildResultsTable(
            List<(string Assembly, string View, string Count, string Status, string Notes)> rows)
        {
            var headerGrid = new System.Windows.Controls.Grid { Background = AvTokens.B(AvTokens.Surface2) };
            SetTableCols(headerGrid);
            string[] hdrs = { "Status", "Assembly", "View", "Count", "Notes" };
            for (int i = 0; i < hdrs.Length; i++)
            {
                var h = new TextBlock { Text = hdrs[i], FontFamily = AvTokens.Ui, FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = AvTokens.B(AvTokens.TextFaint), Padding = new Thickness(i == 0 ? 14 : 12, 9, 12, 9) };
                System.Windows.Controls.Grid.SetColumn(h, i);
                headerGrid.Children.Add(h);
            }

            var rowsPanel = new StackPanel();
            bool odd = false;
            foreach (var r in rows)
            {
                var rg = new System.Windows.Controls.Grid { Background = AvTokens.B(odd ? AvTokens.Zebra : AvTokens.Surface), MinHeight = 42 };
                SetTableCols(rg);

                var pill = StatusPill(r.Status);
                pill.Margin = new Thickness(14, 0, 0, 0);
                pill.VerticalAlignment = VerticalAlignment.Center;
                System.Windows.Controls.Grid.SetColumn(pill, 0); rg.Children.Add(pill);

                var aCell = MakeCell(r.Assembly, AvTokens.Ui, 11.5, AvTokens.TextDim);
                System.Windows.Controls.Grid.SetColumn(aCell, 1); rg.Children.Add(aCell);

                var vCell = MakeCell(r.View, AvTokens.Ui, 12.5, AvTokens.Text, bold: true);
                System.Windows.Controls.Grid.SetColumn(vCell, 2); rg.Children.Add(vCell);

                string cFg = r.Status == "ok" ? AvTokens.OkFg : AvTokens.TextFaint;
                var cCell = MakeCell(r.Count, AvTokens.Mono, 11.5, cFg, centre: true);
                System.Windows.Controls.Grid.SetColumn(cCell, 3); rg.Children.Add(cCell);

                var nCell = MakeCell(r.Notes, AvTokens.Ui, 11.5, AvTokens.TextFaint);
                System.Windows.Controls.Grid.SetColumn(nCell, 4); rg.Children.Add(nCell);

                rowsPanel.Children.Add(new Border { BorderBrush = AvTokens.B(AvTokens.Border), BorderThickness = new Thickness(0, 0, 0, 1), Child = rg });
                odd = !odd;
            }

            if (rows.Count == 0)
                rowsPanel.Children.Add(new TextBlock
                {
                    Text = "No views were processed.",
                    FontFamily = AvTokens.Ui,
                    FontSize = 12.5,
                    Foreground = AvTokens.B(AvTokens.TextFaint),
                    Margin = new Thickness(16, 14, 16, 14),
                    HorizontalAlignment = HorizontalAlignment.Center
                });

            var tableStack = new StackPanel();
            tableStack.Children.Add(new Border { BorderBrush = AvTokens.B(AvTokens.BorderStrong), BorderThickness = new Thickness(0, 0, 0, 1.5), Child = headerGrid });

            var dataScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 280, Content = rowsPanel };
            dataScroll.PreviewMouseWheel += (s, e) => { dataScroll.ScrollToVerticalOffset(dataScroll.VerticalOffset - e.Delta * 0.5); e.Handled = true; };
            tableStack.Children.Add(dataScroll);

            return new Border { Background = AvTokens.B(AvTokens.Surface), BorderBrush = AvTokens.B(AvTokens.Border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Child = tableStack };
        }

        private static void SetTableCols(System.Windows.Controls.Grid g)
        {
            g.ColumnDefinitions.Clear();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        }

        private static TextBlock MakeCell(
            string text, FontFamily font, double size, string fg,
            bool bold = false, bool centre = false) => new TextBlock
            {
                Text = text,
                FontFamily = font,
                FontSize = size,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = AvTokens.B(fg),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = text,
                Padding = new Thickness(12, 11, 12, 11),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = centre ? TextAlignment.Center : TextAlignment.Left
            };
    }
}