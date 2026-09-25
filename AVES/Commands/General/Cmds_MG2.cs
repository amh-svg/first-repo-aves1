// -----------------------------------------------------------------------
// Title: Add Bolts
// Summary: Select any number of elements; every pair among them that meets
//          face to face with coinciding voids gets a bolt through each pair
//          of aligned openings.
// Flow:    Elements are picked first, then one modal window shows what was
//          found — the joints, the total bolts, the widest gap — and takes
//          the bolt to use. A single joint may also have its count reduced,
//          which places a random subset of that size; several joints each
//          take everything they hold. Placement happens after the window
//          closes, inside the command, so it needs no external event.
// Scope:   Accepts any element with solid geometry — nothing here asks what
//          it is looking at, and no dimension of any part is hardcoded. The
//          bolt is the one fixed family, being the thing placed rather than
//          the thing joined. Verified on Mekano 50 and 100 series, mixed
//          joints between them, and a cable tray with an accessory.
// Key logic: Revit cuts family voids into the host solid, so an opening is
//            not its own geometry object — it is an inner curve loop of a
//            planar face. An interface is a pair of opposed, coincident,
//            overlapping faces; a bolt point is the centroid of the region
//            where an opening on one of them overlaps an opening on the
//            other, found by extruding both loops into thin prisms and
//            intersecting them with Revit's own kernel.
// Constants carry their own reasoning and provenance where they are
// declared. Read those rather than assuming a value is arbitrary.
// -----------------------------------------------------------------------
// .NET
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
// Autodesk
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
// AVES
using AVES.Core;
// WPF, aliased. Autodesk.Revit.DB defines its own Grid and Binding, so those two
// genuinely collide; the rest are aliased alongside them so the report window
// reads consistently rather than half-qualified.
using WpfBinding = System.Windows.Data.Binding;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfClipboard = System.Windows.Clipboard;
using WpfDock = System.Windows.Controls.Dock;
using WpfColDef = System.Windows.Controls.ColumnDefinition;
using WpfContentPresenter = System.Windows.Controls.ContentPresenter;
using WpfControlTemplate = System.Windows.Controls.ControlTemplate;
using WpfCornerRadius = System.Windows.CornerRadius;
using WpfCursors = System.Windows.Input.Cursors;
using WpfDataGrid = System.Windows.Controls.DataGrid;
using WpfDataGridCell = System.Windows.Controls.DataGridCell;
using WpfDataGridColumnHeader = System.Windows.Controls.Primitives.DataGridColumnHeader;
using WpfDataGridGridLines = System.Windows.Controls.DataGridGridLinesVisibility;
using WpfDataGridHeaders = System.Windows.Controls.DataGridHeadersVisibility;
using WpfDataGridLength = System.Windows.Controls.DataGridLength;
using WpfDataGridLengthUnit = System.Windows.Controls.DataGridLengthUnitType;
using WpfDataGridTemplateColumn = System.Windows.Controls.DataGridTemplateColumn;
using WpfDataGridTextColumn = System.Windows.Controls.DataGridTextColumn;
using WpfDataTemplate = System.Windows.DataTemplate;
using WpfDockPanel = System.Windows.Controls.DockPanel;
using WpfElementFactory = System.Windows.FrameworkElementFactory;
using WpfFontWeights = System.Windows.FontWeights;
using WpfGrid = System.Windows.Controls.Grid;
using WpfGridLength = System.Windows.GridLength;
using WpfGridUnitType = System.Windows.GridUnitType;
using WpfKey = System.Windows.Input.Key;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfHAlign = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfRoutedEventArgs = System.Windows.RoutedEventArgs;
using WpfRoutedEventHandler = System.Windows.RoutedEventHandler;
using WpfRowDef = System.Windows.Controls.RowDefinition;
using WpfScrollBarVis = System.Windows.Controls.ScrollBarVisibility;
using WpfScrollViewer = System.Windows.Controls.ScrollViewer;
using WpfSetter = System.Windows.Setter;
using WpfRun = System.Windows.Documents.Run;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfStyle = System.Windows.Style;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfThickness = System.Windows.Thickness;
using WpfTrigger = System.Windows.Trigger;
using WpfUIElement = System.Windows.UIElement;
using WpfVAlign = System.Windows.VerticalAlignment;
using WpfWrapPanel = System.Windows.Controls.WrapPanel;
using WpfWindow = System.Windows.Window;

// Associate with pushbutton commands
namespace AVES.Cmds_MG2
{
    /// <summary>
    /// Shared configuration, geometry extraction and matching helpers for the
    /// Mekano bolt connector.
    /// </summary>
    internal static class MG2Shared
    {
        // ── Families ───────────────────────────────────────────────────────────

        // There is no list of accepted families any more. The command works on
        // whatever geometry it is handed: every routine below reads solids, faces
        // and voids, and none of them asks what part they belong to. The bolts are
        // the exception, being the thing placed rather than the thing joined.
        //
        // These are ITEM NUMBER values — a Type parameter — and deliberately not
        // family names. Item numbers are permanent, while family names and
        // PMO_Description may be rewritten at any time; identifying the bolts by
        // the one stable handle means a rename cannot quietly empty the picker.
        //
        // Anything carrying one of these is a bolt this command can place.
        // Anything else is not, whatever it happens to be called.
        internal static readonly string[] BOLT_ITEM_NUMBERS =
        {
            "74140", "1371983", "1371982", "1371437",
            "1371980", "84487", "1371361", "1371362",
        };

        // The bolt this command placed before it could place any of the others.
        // Kept as the first-run default so an unchanged workflow needs no
        // interaction, which is also what makes a regression check meaningful.
        internal const string DEFAULT_BOLT_ITEM_NUMBER = "1371980";

        // Type parameters carrying the bolt's identity and its label. Custom
        // parameters, so unlike built-ins their names are not localised.
        internal const string PARAM_ITEM_NUMBER = "Item Number";
        internal const string PARAM_DESCRIPTION = "PMO_Description";

        // User-facing only. The class, namespace and icon base name stay MG2 —
        // Ribbon_Utils.CommandToBaseName derives MG2_MG216.png / MG2_MG232.png
        // from the class name, so renaming it would orphan both icons.
        internal const string DIALOG_TITLE = "Add Bolts";

        // ── Placement ──────────────────────────────────────────────────────────

        // The bolt is work-plane based, so its local +Z lands on the work plane's
        // normal and its shank — which occupies local -25 mm to 0 mm, origin at
        // the head end — runs toward local -Z.
        //
        // The first element's face normal points out of that element and toward
        // the second, and taking it unchanged is right. Reversing it was tried and
        // rejected: the axial error it appeared to fix was a matter of depth, not
        // direction, and the seat depth is what settled it.
        internal const double SHANK_ORIENTATION = 1.0;

        // How far past the interface plane the bolt is seated, measured along the
        // work plane normal — further toward the second element, deeper into the
        // joint. At zero the head sits flush on the plane where the two parts
        // meet, which reads wrong in the model. Confirmed by eye at 3 mm, on the
        // 100-series first and then on the 50-series.
        //
        // It is now only the FALLBACK. The seat is measured per joint — see
        // WallThickness — and this is used when an element presents no face
        // parallel to and behind its own interface face, which is what a solid bar
        // looks like as opposed to a wall. As a fallback it is a reasonable number
        // rather than a correct one: on a part whose wall is not 3 mm it seats
        // wrong, so the report names the joints that used it.
        //
        // Tied to the work plane normal rather than to any fixed world direction,
        // so it follows the joint wherever it sits and however it is rotated. It
        // therefore also follows SHANK_ORIENTATION: were that negated, this offset
        // would reverse with it and stay on the shank's side of the plane.
        internal const double FALLBACK_SEAT_OFFSET_MM = 3.0;

        // How far behind its own interface face a parallel face must sit before it
        // counts as the far side of a wall.
        //
        // Its only job is to skip the element's own interface face, which sits at
        // exactly 0 from that reference. It is NOT a gap allowance — sizing it as
        // one is exactly what hid the original measurement bug, where the wall was
        // measured from the shared contact plane and the other element's face
        // slipped past this floor as soon as a real gap existed.
        internal const double MIN_WALL_MM = 0.1;

        // ── Tolerances ─────────────────────────────────────────────────────────
        //
        // Expressed in millimetres / square millimetres and converted to Revit
        // internal units on use, so they read the way the fabrication drawings do.

        // How far apart two opposed faces may sit and still count as touching.
        // Parts are meant to be flush, but a fraction of a millimetre of
        // play is normal in a real model, so this is deliberately generous and the
        // measured gap is reported rather than assumed to be zero.
        internal const double GAP_TOLERANCE_MM = 2.0;

        // How closely two face normals must oppose each other. cos(~2.6 deg).
        internal const double NORMAL_OPPOSITION_DOT = -0.999;

        // A face pair must share at least this much area to be the interface, and
        // a face must be at least this large to be considered at all. The second
        // is a cheap way to skip the hundreds of tiny slot-wall faces before any
        // real work happens.
        //
        // 400 mm² survived the move off Mekano on evidence, not by default: on the
        // tray/accessory pair the largest face it discarded was 388 mm², confirmed
        // not to be a joining surface. It is a real threshold on a real part now,
        // so do not move it without new measurements — a face under it never
        // becomes a candidate and so can never form an interface, silently.
        internal const double MIN_INTERFACE_AREA_MM2 = 400.0;
        internal const double MIN_CANDIDATE_FACE_AREA_MM2 = 400.0;

        // Two openings must overlap by at least this much for a bolt to go through
        // them. An M10 shank needs about 79 mm² of clear hole, so this sits below
        // that on purpose: anything that falls short is counted as a near miss and
        // reported, rather than silently dropped.
        internal const double MIN_OPENING_OVERLAP_MM2 = 30.0;

        // Overlaps below MIN_OPENING_OVERLAP_MM2 but above this are the near
        // misses worth mentioning — they usually mean the parts are offset by
        // part of a slot.
        internal const double NEAR_MISS_OVERLAP_MM2 = 1.0;

        // Depth of the throwaway prisms used for every overlap test. Only has to
        // exceed GAP_TOLERANCE_MM by a comfortable margin.
        internal const double PROBE_DEPTH_MM = 20.0;

        // Ratio of an opening's length to its width above which it has a long axis
        // worth locking a hammer head across.
        //
        // Keyed on elongation rather than on literal rectangularity: an oval slot
        // locks just as a rectangular one does, and it is the axis the roll needs,
        // not straight sides. Mekano's 35 x 11 slot reads 3.18, far clear of this,
        // so the threshold does not affect it either way.
        internal const double ELONGATION_MIN = 1.15;

        // How far around the selection to look for bolts already in the document.
        //
        // A COLLECTION RADIUS, not a precision tolerance. The real duplicate test
        // is an exact distance check against a per-point tolerance that is not
        // known until the openings are matched, so this only has to be comfortably
        // larger than any plausible such tolerance. Being generous costs a few
        // extra candidates for the exact check; being tight would silently miss
        // real duplicates, which is the failure that matters.
        internal const double EXISTING_BOLT_SEARCH_MARGIN_MM = 25.0;

        // The duplicate tolerance is no longer a constant. It is half the narrower
        // of the two openings a bolt passes through, measured per joint — two
        // bolts whose centres sit that close could not both fit through the same
        // opening, so they cannot both be real. See MatchOpenings.
        //
        // LIMITATION, unchanged by that: this catches exact and near-exact
        // collisions and nothing more. It does not ask which of the competing
        // points is geometrically better; the first one found wins. Where several
        // interfaces genuinely claim one location, picking the best among them
        // would be separate work, and is not worth doing before a case demands it.

        // Solids below this volume are noise, not geometry.
        internal const double MIN_SOLID_VOLUME = 1e-9;

        // Nested families can sit several GeometryInstance levels down.
        internal const int MAX_NESTING_DEPTH = 8;

        // Guards against normalising a vector that has collapsed to nothing.
        internal const double AXIS_EPSILON = 1e-9;

        // ── Unit helpers ───────────────────────────────────────────────────────

        /// <summary>Millimetres to feet, the internal unit for lengths.</summary>
        internal static double Ft(double millimetres)
            => UnitUtils.ConvertToInternalUnits(millimetres, UnitTypeId.Millimeters);

        /// <summary>Feet back to millimetres, for reporting.</summary>
        internal static double Mm(double feet)
            => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);

        /// <summary>Square millimetres to square feet.</summary>
        internal static double Ft2(double squareMillimetres)
            => squareMillimetres / (Mm(1.0) * Mm(1.0));

        // ── Selection filter ───────────────────────────────────────────────────

        /// <summary>
        /// Accepts anything with a category — no family check at all.
        ///
        /// The name says what this actually requires rather than what it expects
        /// to be handed. It cannot require solid geometry at pick time: that means
        /// reading and walking the geometry of every element the cursor passes
        /// over, which is far too slow for a highlight filter. Elements without
        /// usable solids are therefore accepted here and drop out later, silently,
        /// the same way a pair that does not touch does.
        ///
        /// Deliberately NOT restricted to FamilyInstance. Cable tray runs are
        /// MEPCurve, not family instances, and excluding them would have ruled out
        /// the case this generalisation was built for.
        /// </summary>
        internal sealed class SolidElementFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem.Category is not null;

            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        // ── Geometry extraction ────────────────────────────────────────────────

        /// <summary>
        /// View-independent geometry options: the real physical envelope at full
        /// detail, regardless of how any particular view is configured. Invisible
        /// objects stay out — an element's voids are already cut into its solid,
        /// so nothing is gained by pulling in reference planes and helper forms.
        /// </summary>
        internal static Options ModelGeometryOptions() => new()
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = false,
            DetailLevel = ViewDetailLevel.Fine,
        };

        /// <summary>
        /// Recursively walks a GeometryElement and collects every Solid found at
        /// any nesting depth, already transformed into the caller's coordinates.
        ///
        /// Uses GetSymbolGeometry() with an accumulated transform rather than
        /// GetInstanceGeometry(): the latter returns copies, and mixing it into a
        /// recursive walk risks double-transforming nested geometry.
        /// </summary>
        internal static void CollectSolids(
            GeometryElement? geometry, Transform transform, List<Solid> sink, int depth = 0)
        {
            if (geometry is null || depth > MAX_NESTING_DEPTH) return;

            foreach (GeometryObject obj in geometry)
            {
                switch (obj)
                {
                    case Solid solid when solid.Volume > MIN_SOLID_VOLUME && solid.Faces.Size > 0:
                        try
                        {
                            sink.Add(transform.IsIdentity
                                ? solid
                                : SolidUtils.CreateTransformed(solid, transform));
                        }
                        catch { }
                        break;

                    case GeometryInstance instance:
                        try
                        {
                            CollectSolids(
                                instance.GetSymbolGeometry(),
                                transform.Multiply(instance.Transform),
                                sink,
                                depth + 1);
                        }
                        catch { }
                        break;
                }
            }
        }

        /// <summary>
        /// How an element reads in the report.
        ///
        /// NOT "family and type". Since the selection filter accepts any Element,
        /// half of what this command joins has no Symbol at all — a cable tray run
        /// is an MEPCurve, and that is the case the generalisation was built for.
        /// Reaching for Symbol.Family.Name would come back empty on exactly those
        /// elements, or throw.
        ///
        /// So the type name where there is one, the category otherwise. Short on
        /// purpose — these sit in a table column that cannot be widened much.
        ///
        /// Two elements of the same type therefore read identically, and nothing
        /// distinguishes them in the table. That is accepted: the row's Select
        /// button is how you find out which elements a joint actually involves.
        ///
        /// Never blank, never throws: an unlabelled row in a table of joints is
        /// worse than an ugly one.
        /// </summary>
        internal static string LabelOf(Element element)
        {
            try
            {
                if (element is FamilyInstance instance)
                {
                    string? type = instance.Symbol?.Name;
                    if (!string.IsNullOrWhiteSpace(type)) return type!;
                }

                string? category = element.Category?.Name;
                if (!string.IsNullOrWhiteSpace(category)) return category!;
            }
            catch { }

            return $"Element {element.Id}";
        }

        /// <summary>Every solid of an element, in project coordinates.</summary>
        internal static List<Solid> SolidsOf(Element element)
        {
            var solids = new List<Solid>();

            GeometryElement? geometry;
            try { geometry = element.get_Geometry(ModelGeometryOptions()); }
            catch { return solids; }

            CollectSolids(geometry, Transform.Identity, solids);
            return solids;
        }

        /// <summary>
        /// An axis-aligned box around some solids.
        ///
        /// Built from the solids themselves rather than from
        /// Element.get_BoundingBox, because that returns a box whose Min and Max
        /// are relative to its own Transform, and reading them as model
        /// coordinates is only correct while that transform happens to be
        /// identity. Measuring the geometry the face sweep will actually look at
        /// sidesteps that entirely and costs one pass we already make.
        /// </summary>
        internal sealed class Bounds
        {
            internal XYZ Min { get; }
            internal XYZ Max { get; }

            private Bounds(XYZ min, XYZ max)
            {
                Min = min;
                Max = max;
            }

            /// <summary>Null when there is no geometry to bound.</summary>
            internal static Bounds? Of(IEnumerable<Solid> solids)
            {
                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

                foreach (Solid solid in solids)
                {
                    foreach (Edge edge in solid.Edges)
                    {
                        IList<XYZ> points;
                        try { points = edge.Tessellate(); }
                        catch { continue; }

                        foreach (XYZ p in points)
                        {
                            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                            minZ = Math.Min(minZ, p.Z); maxZ = Math.Max(maxZ, p.Z);
                        }
                    }
                }

                if (minX > maxX) return null;

                return new Bounds(new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
            }

            /// <summary>The box around several boxes.</summary>
            internal static Bounds? Union(IEnumerable<Bounds> boxes)
            {
                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

                foreach (Bounds box in boxes)
                {
                    minX = Math.Min(minX, box.Min.X); maxX = Math.Max(maxX, box.Max.X);
                    minY = Math.Min(minY, box.Min.Y); maxY = Math.Max(maxY, box.Max.Y);
                    minZ = Math.Min(minZ, box.Min.Z); maxZ = Math.Max(maxZ, box.Max.Z);
                }

                if (minX > maxX) return null;

                return new Bounds(new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
            }

            /// <summary>
            /// Whether two boxes come within <paramref name="margin"/> of each
            /// other on every axis. The margin matters: two elements that touch
            /// across a permitted gap must not be rejected here, so it is passed
            /// the same gap tolerance the interface test uses.
            /// </summary>
            internal bool Overlaps(Bounds other, double margin)
                => Min.X - margin <= other.Max.X && other.Min.X - margin <= Max.X
                && Min.Y - margin <= other.Max.Y && other.Min.Y - margin <= Max.Y
                && Min.Z - margin <= other.Max.Z && other.Min.Z - margin <= Max.Z;
        }

        /// <summary>Every planar face worth considering as part of an interface.</summary>
        internal static List<PlanarFace> CandidateFaces(IEnumerable<Solid> solids)
        {
            double minArea = Ft2(MIN_CANDIDATE_FACE_AREA_MM2);
            var faces = new List<PlanarFace>();

            foreach (Solid solid in solids)
            {
                foreach (Face face in solid.Faces)
                {
                    if (face is PlanarFace planar && planar.Area >= minArea)
                    {
                        faces.Add(planar);
                    }
                }
            }

            return faces;
        }

        // ── In-plane frame ─────────────────────────────────────────────────────

        /// <summary>
        /// A right-handed 2D frame lying in a plane, used to reason about loops
        /// without repeatedly projecting through full transforms.
        /// </summary>
        internal sealed class PlaneFrame
        {
            internal XYZ Origin { get; }
            internal XYZ Normal { get; }
            internal XYZ AxisU { get; }
            internal XYZ AxisV { get; }

            internal PlaneFrame(XYZ origin, XYZ normal)
            {
                Origin = origin;
                Normal = normal.Normalize();

                // Any vector not parallel to the normal seeds the in-plane axes.
                XYZ seed = Math.Abs(Normal.Z) < 0.9 ? XYZ.BasisZ : XYZ.BasisX;

                AxisU = Normal.CrossProduct(seed).Normalize();
                AxisV = Normal.CrossProduct(AxisU).Normalize();
            }

            internal UV To2D(XYZ point)
            {
                XYZ delta = point - Origin;
                return new UV(delta.DotProduct(AxisU), delta.DotProduct(AxisV));
            }
        }

        // ── Curve loops ────────────────────────────────────────────────────────

        /// <summary>
        /// Walks a curve loop as a polyline. Each curve's tessellation repeats the
        /// previous curve's end point, so all but the first drop their leading
        /// vertex.
        /// </summary>
        internal static List<XYZ> Tessellate(CurveLoop loop)
        {
            var points = new List<XYZ>();

            foreach (Curve curve in loop)
            {
                IList<XYZ> segment;
                try { segment = curve.Tessellate(); }
                catch { continue; }

                for (int i = points.Count == 0 ? 0 : 1; i < segment.Count; i++)
                {
                    points.Add(segment[i]);
                }
            }

            return points;
        }

        /// <summary>
        /// Shoelace area of a loop measured in the given frame. The sign says
        /// which way round the loop runs relative to the frame's normal, which is
        /// what decides whether it has to be reversed before extruding.
        /// </summary>
        internal static double SignedArea(List<XYZ> points, PlaneFrame frame)
        {
            if (points.Count < 3) return 0.0;

            double twiceArea = 0.0;

            for (int i = 0; i < points.Count; i++)
            {
                UV a = frame.To2D(points[i]);
                UV b = frame.To2D(points[(i + 1) % points.Count]);
                twiceArea += a.U * b.V - b.U * a.V;
            }

            return twiceArea / 2.0;
        }

        /// <summary>Rebuilds a loop running the other way round.</summary>
        internal static CurveLoop Reverse(CurveLoop loop)
        {
            var reversed = new CurveLoop();

            foreach (Curve curve in loop.ToList().AsEnumerable().Reverse())
            {
                reversed.Append(curve.CreateReversed());
            }

            return reversed;
        }

        /// <summary>
        /// Turns a planar loop into a thin prism straddling its own plane, so two
        /// prisms built from nearly-coincident planes still meet even when the
        /// elements sit a fraction of a millimetre apart.
        ///
        /// Returns null rather than throwing: a degenerate loop is a fact about
        /// the geometry, not an error, and the caller simply skips it.
        /// </summary>
        internal static Solid? Prism(CurveLoop loop, PlaneFrame frame, double depth)
        {
            try
            {
                List<XYZ> points = Tessellate(loop);
                if (points.Count < 3) return null;

                // CreateExtrusionGeometry needs the profile wound counter-clockwise
                // about the extrusion direction. Inner loops always run the other
                // way from their face's outer loop, so this is not optional.
                CurveLoop oriented = SignedArea(points, frame) < 0.0 ? Reverse(loop) : loop;

                CurveLoop shifted = CurveLoop.CreateViaTransform(
                    oriented,
                    Transform.CreateTranslation(frame.Normal * (-depth / 2.0)));

                return GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { shifted }, frame.Normal, depth);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The solid two prisms have in common, or null when they miss.</summary>
        internal static Solid? Intersect(Solid? first, Solid? second)
        {
            if (first is null || second is null) return null;

            try
            {
                Solid result = BooleanOperationsUtils.ExecuteBooleanOperation(
                    first, second, BooleanOperationsType.Intersect);

                return result.Volume > MIN_SOLID_VOLUME ? result : null;
            }
            catch
            {
                return null;
            }
        }

        // ── Face loops ─────────────────────────────────────────────────────────

        /// <summary>
        /// Splits a face's curve loops into its outer boundary and its holes.
        ///
        /// The largest loop by area is the boundary and everything else is a hole.
        /// That is more robust than trusting the returned order, and holes are
        /// exactly what this command is looking for: Revit cuts family voids into
        /// the host solid, so a slot never arrives as its own geometry object — it
        /// arrives as an inner loop of the face it pierces.
        /// </summary>
        internal static bool TrySplitLoops(
            PlanarFace face, PlaneFrame frame, out CurveLoop? outer, out List<CurveLoop> holes)
        {
            outer = null;
            holes = new List<CurveLoop>();

            IList<CurveLoop> loops;
            try { loops = face.GetEdgesAsCurveLoops(); }
            catch { return false; }

            if (loops is null || loops.Count == 0) return false;

            double bestArea = -1.0;
            int bestIndex = -1;

            for (int i = 0; i < loops.Count; i++)
            {
                double area = Math.Abs(SignedArea(Tessellate(loops[i]), frame));

                if (area > bestArea)
                {
                    bestArea = area;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0) return false;

            outer = loops[bestIndex];

            for (int i = 0; i < loops.Count; i++)
            {
                if (i != bestIndex) holes.Add(loops[i]);
            }

            return true;
        }

        // ── Interface detection ────────────────────────────────────────────────

        /// <summary>A pair of opposed, coincident, overlapping faces.</summary>
        internal sealed class FacePair
        {
            internal PlanarFace First { get; }
            internal PlanarFace Second { get; }
            internal PlaneFrame Frame { get; }
            internal double Gap { get; }
            internal double OverlapArea { get; }

            internal FacePair(
                PlanarFace first, PlanarFace second, PlaneFrame frame,
                double gap, double overlapArea)
            {
                First = first;
                Second = second;
                Frame = frame;
                Gap = gap;
                OverlapArea = overlapArea;
            }

        }

        /// <summary>
        /// Finds every place the two elements touch face to face, best first.
        ///
        /// A solid-to-solid boolean is deliberately not used: the parts butt
        /// against each other rather than interpenetrating, so their intersection
        /// has no volume and would report nothing at all.
        ///
        /// The plane-coincidence test is also what keeps hollow construction from
        /// causing trouble. A hollow part presents several parallel walls, each
        /// carrying the same void pattern; only the two that actually meet share a
        /// plane, so the far walls fall out here, before any overlap work is done.
        /// Checked against Mekano's wall stack, where the nearest rival face is a
        /// whole cavity away and the gap tolerance never comes close to it.
        /// </summary>
        internal static List<FacePair> FindInterfaces(
            List<PlanarFace> firstFaces, List<PlanarFace> secondFaces)
        {
            double gapTolerance = Ft(GAP_TOLERANCE_MM);
            double probeDepth = Ft(PROBE_DEPTH_MM);
            double minArea = Ft2(MIN_INTERFACE_AREA_MM2);

            var pairs = new List<FacePair>();

            foreach (PlanarFace a in firstFaces)
            {
                XYZ normalA = a.FaceNormal;
                XYZ originA = a.Origin;

                foreach (PlanarFace b in secondFaces)
                {
                    // The faces have to look at each other, not merely be parallel.
                    if (normalA.DotProduct(b.FaceNormal) > NORMAL_OPPOSITION_DOT) continue;

                    // ...and lie in what is effectively the same plane.
                    double gap = Math.Abs((b.Origin - originA).DotProduct(normalA));
                    if (gap > gapTolerance) continue;

                    var frame = new PlaneFrame(originA, normalA);

                    if (!TrySplitLoops(a, frame, out CurveLoop? outerA, out _)) continue;
                    if (!TrySplitLoops(b, frame, out CurveLoop? outerB, out _)) continue;

                    Solid? shared = Intersect(
                        Prism(outerA!, frame, probeDepth),
                        Prism(outerB!, frame, probeDepth));

                    if (shared is null) continue;

                    // The two prisms are offset from each other by the gap, so the
                    // depth they truly share is a little less than the probe depth.
                    double sharedDepth = probeDepth - gap;
                    if (sharedDepth <= 0.0) continue;

                    double overlapArea = shared.Volume / sharedDepth;
                    if (overlapArea < minArea) continue;

                    pairs.Add(new FacePair(a, b, frame, gap, overlapArea));
                }
            }

            return pairs.OrderByDescending(p => p.OverlapArea).ToList();
        }

        // ── Seat depth ─────────────────────────────────────────────────────────

        /// <summary>
        /// Thickness of the wall an element presents at the joint: the distance
        /// from its own interface face to the next face of its own parallel to and
        /// behind that one.
        ///
        /// Measured from <paramref name="ownInterfaceFace"/> and NOT from the
        /// shared contact plane. That distinction is the whole correctness of this
        /// function. Measured from the contact plane, the OTHER element's
        /// interface face sits at +gap, clears MIN_WALL_MM as soon as the gap is
        /// real, and is nearer than this element's true inner face — so the
        /// function returns the gap instead of the wall. It looks right on parts
        /// that sit flush, which is how it survived being caught by eye, and
        /// GAP_TOLERANCE_MM permits gaps of up to 2 mm everywhere.
        ///
        /// Two distances therefore stay separate below, and must not be merged:
        /// the thickness, from the element's own face, and the offset from the
        /// contact plane, used only to drop a candidate outline onto that plane so
        /// the overlap test measures overlap and not separation.
        ///
        /// The overlap test is what stops an unrelated parallel face elsewhere on
        /// the same element from winning.
        ///
        /// Returns null when the element presents no such face — a solid bar
        /// rather than a wall — and the caller falls back.
        /// </summary>
        internal static double? WallThickness(
            List<PlanarFace> faces, PlanarFace ownInterfaceFace, FacePair contact, XYZ inward)
        {
            double probe = Ft(PROBE_DEPTH_MM);
            double floor = Ft(MIN_WALL_MM);

            if (!TrySplitLoops(contact.First, contact.Frame, out CurveLoop? patch, out _)) return null;

            Solid? patchPrism = Prism(patch!, contact.Frame, probe);
            if (patchPrism is null) return null;

            double? best = null;

            foreach (PlanarFace face in faces)
            {
                if (Math.Abs(face.FaceNormal.DotProduct(inward)) < 0.999) continue;

                // The thickness, from this element's own face at the joint.
                double distance = (face.Origin - ownInterfaceFace.Origin).DotProduct(inward);

                if (distance <= floor) continue;
                if (best is not null && distance >= best.Value) continue;

                if (!TrySplitLoops(face, contact.Frame, out CurveLoop? outer, out _)) continue;

                // A different distance, from the contact plane. With a gap present
                // this is not the same number as the thickness above.
                double fromContactPlane = (face.Origin - contact.Frame.Origin).DotProduct(inward);

                CurveLoop flattened = CurveLoop.CreateViaTransform(
                    outer!, Transform.CreateTranslation(inward * -fromContactPlane));

                if (Intersect(patchPrism, Prism(flattened, contact.Frame, probe)) is null) continue;

                best = distance;
            }

            return best;
        }

        // ── Openings ───────────────────────────────────────────────────────────

        /// <summary>
        /// What an opening's outline is shaped like, as far as locking a bolt head
        /// across it is concerned.
        /// </summary>
        internal enum OpeningShape
        {
            /// <summary>Long enough to have a usable axis. A slot.</summary>
            Elongated,

            /// <summary>No straight edge and no elongation. Nothing to lock against.</summary>
            Round,

            /// <summary>
            /// Has straight edges but is not elongated — square, hex, polygon.
            /// There is an axis, but what "locked" means against it is not
            /// established. Treated as lockable, and reported so that treatment is
            /// visible rather than assumed.
            /// </summary>
            Other,
        }

        /// <summary>One slot or hole opening, described in its face's plane.</summary>
        internal sealed class Opening
        {
            internal CurveLoop Loop { get; }

            /// <summary>Rough centre, used only to reject pairs that cannot touch.</summary>
            internal UV Centre { get; }

            /// <summary>Furthest the outline reaches from that centre.</summary>
            internal double Radius { get; }

            /// <summary>
            /// Which way the opening runs, as a unit vector in project coordinates
            /// lying in the face's plane. For a slot this is its long axis, which
            /// is what the bolt's roll is set against — and the only thing this
            /// value feeds. See LongAxisOf for how it is measured and why.
            /// </summary>
            internal XYZ LongAxis { get; }

            /// <summary>Extent along <see cref="LongAxis"/>.</summary>
            internal double Length { get; }

            /// <summary>
            /// Extent perpendicular to <see cref="LongAxis"/>, in the face's plane.
            /// Half the narrower of two matched openings is the duplicate
            /// tolerance, so this has to be measured about the true long axis —
            /// about the old widest-diameter axis a filleted 11 mm slot read
            /// 17.9 mm, which would have inflated the tolerance by some 62%.
            /// </summary>
            internal double Width { get; }

            internal OpeningShape Shape { get; }

            internal Opening(
                CurveLoop loop, UV centre, double radius, XYZ longAxis,
                double length, double width, OpeningShape shape)
            {
                Loop = loop;
                Centre = centre;
                Radius = radius;
                LongAxis = longAxis;
                Length = length;
                Width = width;
                Shape = shape;
            }

            /// <summary>
            /// Whether a bolt head can be turned across this opening to lock.
            ///
            /// Always ask the shape, never whether LongAxis is null — it never is.
            /// LongAxisOf falls back to an arbitrary diameter for round openings
            /// on purpose, so a null test would call every round hole lockable.
            /// </summary>
            internal bool CanLock => Shape != OpeningShape.Round;
        }

        /// <summary>
        /// Describes each of a face's holes in the interface frame. The centre is
        /// the tessellation's average rather than a true area centroid — it only
        /// has to be good enough to reject pairs that cannot possibly touch, and
        /// the bolt point comes from the exact overlap solid later on.
        /// </summary>
        internal static List<Opening> OpeningsOf(PlanarFace face, PlaneFrame frame)
        {
            var openings = new List<Opening>();

            if (!TrySplitLoops(face, frame, out _, out List<CurveLoop> holes)) return openings;

            foreach (CurveLoop hole in holes)
            {
                List<XYZ> points = Tessellate(hole);
                if (points.Count < 3) continue;

                var flat = points.Select(frame.To2D).ToList();

                var centre = new UV(flat.Average(p => p.U), flat.Average(p => p.V));
                double radius = flat.Max(p => p.DistanceTo(centre));

                XYZ longAxis = LongAxisOf(hole, flat, frame);

                // Measured once here, not per pair: an opening takes part in
                // several candidate pairs and its shape does not change between
                // them. Roll, duplicate tolerance and the skip rules all read
                // these same three values.
                XYZ across = frame.Normal.CrossProduct(longAxis).Normalize();

                double length = points.Max(p => p.DotProduct(longAxis))
                              - points.Min(p => p.DotProduct(longAxis));
                double width = points.Max(p => p.DotProduct(across))
                             - points.Min(p => p.DotProduct(across));

                bool hasStraightEdge = false;
                foreach (Curve curve in hole)
                {
                    if (curve is Line) { hasStraightEdge = true; break; }
                }

                double elongation = width > 0.0 ? length / width : 0.0;

                OpeningShape shape =
                    elongation >= ELONGATION_MIN ? OpeningShape.Elongated
                    : hasStraightEdge ? OpeningShape.Other
                    : OpeningShape.Round;

                openings.Add(new Opening(hole, centre, radius, longAxis, length, width, shape));
            }

            return openings;
        }

        /// <summary>
        /// Which way an opening runs, in project coordinates. Used for one thing:
        /// the roll that turns the bolt head across the slot.
        ///
        /// The longest straight edge of the outline is the answer whenever there
        /// is one. On any slot with flat sides — filleted rectangle, obround,
        /// plain rectangle — that edge IS the long axis, exactly.
        ///
        /// This replaced a widest-diameter measurement that was wrong on the real
        /// part. The Mekano slot is a 35 x 11 mm rectangle with roughly 1.8 mm
        /// corner fillets, and on that shape the two most distant points sit on
        /// opposite fillets, so the widest diameter is the corner-to-corner
        /// diagonal rather than the long axis. Measured against a real joint it
        /// came back 12.88 degrees off, which put every bolt's head that far out
        /// of square with the slot it was meant to lock across. The error was too
        /// small to notice by eye, which is how it survived visual sign-off.
        ///
        /// The old measurement stays on as the fallback, because a genuinely round
        /// opening has no straight edge and no long axis either. There the widest
        /// diameter is arbitrary but harmless — a round hole does not constrain
        /// roll — and falling back to it keeps round openings behaving exactly as
        /// they did rather than inventing a new failure mode for them.
        /// </summary>
        internal static XYZ LongAxisOf(CurveLoop loop, List<UV> flat, PlaneFrame frame)
        {
            XYZ? straight = LongestLineAxis(loop, frame);
            if (straight is not null) return straight;

            return WidestDiameterAxis(flat, frame);
        }

        /// <summary>
        /// Direction of the longest straight segment in an outline, projected into
        /// the face's plane. Null when the loop has no straight segments, which is
        /// the signature of a round opening with no long axis to find.
        /// </summary>
        private static XYZ? LongestLineAxis(CurveLoop loop, PlaneFrame frame)
        {
            XYZ? best = null;
            double bestLength = 0.0;

            foreach (Curve curve in loop)
            {
                if (curve is not Line line) continue;

                double length;
                XYZ direction;

                try
                {
                    length = line.Length;
                    direction = line.Direction;
                }
                catch { continue; }

                if (length <= bestLength) continue;

                // A face's own edge already lies in its plane; projecting costs
                // nothing and guards against one that does not.
                XYZ inPlane = direction - frame.Normal * direction.DotProduct(frame.Normal);
                if (inPlane.GetLength() < AXIS_EPSILON) continue;

                bestLength = length;
                best = inPlane.Normalize();
            }

            return best;
        }

        /// <summary>
        /// The line between an outline's two most distant points. Correct for a
        /// shape whose extremes lie on its long axis, which is why it survives as
        /// the round-opening fallback — and wrong for a filleted rectangle, which
        /// is why it is no longer the primary measurement. See LongAxisOf.
        /// </summary>
        private static XYZ WidestDiameterAxis(List<UV> flat, PlaneFrame frame)
        {
            double best = -1.0;
            UV direction = new UV(1.0, 0.0);

            for (int i = 0; i < flat.Count; i++)
            {
                for (int j = i + 1; j < flat.Count; j++)
                {
                    double distance = flat[i].DistanceTo(flat[j]);

                    if (distance > best)
                    {
                        best = distance;
                        direction = new UV(flat[j].U - flat[i].U, flat[j].V - flat[i].V);
                    }
                }
            }

            if (best <= 0.0) return frame.AxisU;

            UV unit = direction.Normalize();
            return (frame.AxisU * unit.U + frame.AxisV * unit.V).Normalize();
        }

        // ── Void alignment ─────────────────────────────────────────────────────

        /// <summary>Where one bolt goes, and what its roll is measured against.</summary>
        internal sealed class BoltPoint
        {
            /// <summary>Centre of the region the two openings share, on the interface plane.</summary>
            internal XYZ Point { get; }

            /// <summary>
            /// Long axis of whichever of the two openings the head locks across.
            /// NOT necessarily the first one — see MatchOpenings.
            /// </summary>
            internal XYZ SlotLongAxis { get; }

            /// <summary>
            /// Half the narrower of the two openings this bolt passes through. Two
            /// bolts nearer than this are the same fastener found twice.
            /// </summary>
            internal double Tolerance { get; }

            internal BoltPoint(XYZ point, XYZ slotLongAxis, double tolerance)
            {
                Point = point;
                SlotLongAxis = slotLongAxis;
                Tolerance = tolerance;
            }
        }

        /// <summary>What the opening match found.</summary>
        internal sealed class MatchReport
        {
            internal List<BoltPoint> Matched { get; } = new();

            /// <summary>
            /// Pairs that overlapped a little but not enough. Reported on failure,
            /// because "they overlap by 4 mm²" points at a joint that is a slot
            /// off, whereas "nothing overlapped" points at something else.
            /// </summary>
            internal int NearMissCount { get; set; }

            /// <summary>
            /// Pairs whose openings aligned but where both were round, so there
            /// was no axis on either side to lock a head across.
            /// </summary>
            internal int SkippedBothRound { get; set; }

            /// <summary>
            /// Pairs whose openings aligned but where a width could not be
            /// measured, so no duplicate tolerance could be derived. Skipped
            /// rather than guessed — the one place with no fallback.
            /// </summary>
            internal int SkippedNoWidth { get; set; }

            /// <summary>
            /// Bolts whose roll was taken from an "Other"-shaped opening — square,
            /// hex, polygon. Counted so that treating those as lockable is visible
            /// in the report rather than a silent assumption.
            /// </summary>
            internal int OtherShapeRolls { get; set; }
        }

        /// <summary>
        /// Matches the openings on one interface face against those on the other.
        ///
        /// The slots run along each part's own length, so at a right-angle join
        /// they cross rather than nest, and their centres never coincide. That is
        /// why the bolt point is the centroid of the region the two openings share
        /// and not either opening's own centre — and why the overlap is computed
        /// with Revit's kernel instead of a hand-rolled polygon clipper, which
        /// would have to get the crossed-slot case exactly right on its own.
        ///
        /// The count of bolts is however many pairs survive, and it is never
        /// assumed. Measured on real joints: a 100-to-100 yields four, and a
        /// 50-to-50 yields two.
        ///
        /// Note those figures do NOT come from multiplying row counts. The
        /// 100-series presents two slot rows per interface face and the 50-series
        /// one, so rows-against-rows would predict four and *one* — yet a 50-to-50
        /// genuinely produces two. Whatever the overlap geometry does here, it is
        /// not that arithmetic, and no rule of thumb is recorded in its place. The
        /// count comes out of the matching and is simply believed.
        /// </summary>
        internal static MatchReport MatchOpenings(
            List<Opening> firstOpenings, List<Opening> secondOpenings, PlaneFrame frame)
        {
            double probeDepth = Ft(PROBE_DEPTH_MM);
            double minOverlap = Ft2(MIN_OPENING_OVERLAP_MM2);
            double nearMissFloor = Ft2(NEAR_MISS_OVERLAP_MM2);

            var report = new MatchReport();

            // Prisms are reused across the inner loop: building them is the
            // expensive part and each opening's prism never changes.
            var secondPrisms = secondOpenings
                .Select(o => Prism(o.Loop, frame, probeDepth))
                .ToList();

            foreach (Opening a in firstOpenings)
            {
                Solid? prismA = null;

                for (int j = 0; j < secondOpenings.Count; j++)
                {
                    Opening b = secondOpenings[j];

                    // Cheap rejection first. A slotted face carries dozens of
                    // openings and only the handful at the crossing can overlap,
                    // so the booleans are reserved for those.
                    if (a.Centre.DistanceTo(b.Centre) > a.Radius + b.Radius) continue;

                    prismA ??= Prism(a.Loop, frame, probeDepth);
                    if (prismA is null) break;

                    Solid? shared = Intersect(prismA, secondPrisms[j]);
                    if (shared is null) continue;

                    double overlapArea = shared.Volume / probeDepth;

                    if (overlapArea < minOverlap)
                    {
                        if (overlapArea >= nearMissFloor) report.NearMissCount++;
                        continue;
                    }

                    // Project the overlap prism's centroid back onto the interface
                    // plane: the prism straddles that plane, so this is the centre
                    // of the crossing region itself.
                    // Roll: lock across whichever opening can be locked across.
                    // The first still wins whenever it can, which is always true
                    // on Mekano, so this leaves that behaviour untouched. The
                    // second is used only where the first is round and could not
                    // have oriented anything.
                    //
                    // NOTE this is NOT the same rule as the one choosing the bolt
                    // axis. Shank direction comes from selection order, via
                    // contact.First; roll comes from shape, and may come from
                    // either opening. They were one rule and are now two,
                    // justified separately: the axis says which element the shank
                    // enters, the roll says which opening the head catches in. Do
                    // not fold them back together.
                    Opening? locker = a.CanLock ? a : b.CanLock ? b : null;

                    if (locker is null)
                    {
                        report.SkippedBothRound++;
                        continue;
                    }

                    // Tolerance: half the narrower opening. No fallback here, by
                    // decision — a void with no measurable width is not something
                    // to guess a duplicate radius for.
                    if (a.Width <= 0.0 || b.Width <= 0.0)
                    {
                        report.SkippedNoWidth++;
                        continue;
                    }

                    if (locker.Shape == OpeningShape.Other) report.OtherShapeRolls++;

                    XYZ centroid = shared.ComputeCentroid();
                    XYZ point = centroid
                        - frame.Normal * (centroid - frame.Origin).DotProduct(frame.Normal);

                    report.Matched.Add(new BoltPoint(
                        point, locker.LongAxis, Math.Min(a.Width, b.Width) / 2.0));
                }
            }

            return report;
        }

        // ── Bolt family ────────────────────────────────────────────────────────

        /// <summary>
        /// One bolt the user can choose, as the picker needs it.
        /// </summary>
        internal sealed class BoltChoice
        {
            internal FamilySymbol Symbol { get; }

            /// <summary>The stable identifier this bolt was selected by.</summary>
            internal string ItemNumber { get; }

            /// <summary>
            /// The description, or the family/type fallback when there is none.
            /// Label text only — ordering does not read this.
            /// </summary>
            internal string Description { get; }

            /// <summary>
            /// What the user SEES: "1371980 - Bolt-Locking-Set M10x25". Live from
            /// the document, never cached. Ordering never touches this either.
            /// </summary>
            internal string DisplayLabel { get; }

            // ── Ordering ───────────────────────────────────────────────────────
            //
            // The list is ordered by item number: shortest first, then ascending
            // within a length. So the two five-digit numbers lead, then the
            // 1371xxx block in numeric order.
            //
            // Two fields rather than one composite key, because the rule is two
            // comparisons and a single string cannot carry it without a
            // zero-padding trick that hides what is actually going on.
            //
            // A plain string sort is specifically WRONG here: it would put
            // "1371361" ahead of "74140" because '1' sorts before '7', ignoring
            // length entirely. Hence the explicit length-then-value pair.

            /// <summary>Digit count. The primary ordering key, shortest first.</summary>
            internal int SortLength { get; }

            /// <summary>
            /// Numeric value, breaking ties within a length. long.MaxValue when the
            /// item number is not numeric, which sorts such entries last rather
            /// than throwing — the whitelist is all digits today, but nothing
            /// enforces that the parameter holds a number.
            /// </summary>
            internal long SortValue { get; }

            internal BoltChoice(FamilySymbol symbol, string itemNumber, string description)
            {
                Symbol = symbol;
                ItemNumber = itemNumber;
                Description = description;
                DisplayLabel = $"{itemNumber} - {description}";

                SortLength = itemNumber.Length;
                SortValue = long.TryParse(itemNumber, out long value) ? value : long.MaxValue;
            }
        }

        /// <summary>
        /// Every whitelisted bolt loaded in the document, sorted by the label the
        /// user will read.
        ///
        /// Category comes from BuiltInCategory rather than the category name,
        /// which is localised — on a Spanish Revit a name comparison matches
        /// nothing and looks exactly like "no bolts are loaded".
        ///
        /// Deduplicated by item number, first wins. One type per family is the
        /// expected shape; this also covers the odder case of two families
        /// carrying the same number, without failing over it.
        ///
        /// A missing or empty PMO_Description does NOT exclude a bolt. The
        /// whitelist is the authority on what is a bolt, and letting the label
        /// gate the list would hide a loaded, valid fastener behind a blank
        /// parameter, leaving the user unable to place it at all. Unlabelled is
        /// survivable; absent is not, so the label falls back to family and type.
        ///
        /// Sorted alphabetically by that label because it is what the dialog
        /// shows. Ordering by item number or by the whitelist's own order would
        /// look arbitrary from inside the window, where neither is visible.
        /// </summary>
        internal static List<BoltChoice> LoadBoltChoices(Document doc)
        {
            var byItemNumber = new Dictionary<string, BoltChoice>(StringComparer.Ordinal);

            IEnumerable<FamilySymbol> symbols;

            try
            {
                symbols = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_GenericModel)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>();
            }
            catch
            {
                return new List<BoltChoice>();
            }

            foreach (FamilySymbol symbol in symbols)
            {
                string? itemNumber = ParameterText(symbol.LookupParameter(PARAM_ITEM_NUMBER));
                if (itemNumber is null) continue;

                itemNumber = itemNumber.Trim();

                if (!BOLT_ITEM_NUMBERS.Contains(itemNumber, StringComparer.Ordinal)) continue;
                if (byItemNumber.ContainsKey(itemNumber)) continue;

                string? description = ParameterText(symbol.LookupParameter(PARAM_DESCRIPTION));

                // The item number is prepended by BoltChoice, so it survives the
                // fallback too — a bolt with no description still shows which one
                // it is, rather than only a family and type name.
                string label = string.IsNullOrWhiteSpace(description)
                    ? $"{symbol.Family?.Name} — {symbol.Name}"
                    : description!.Trim();

                byItemNumber[itemNumber] = new BoltChoice(symbol, itemNumber, label);
            }

            // By item number: shortest first, then ascending within a length. The
            // final tiebreak only matters for non-numeric item numbers, which all
            // share long.MaxValue and would otherwise order arbitrarily.
            return byItemNumber.Values
                .OrderBy(c => c.SortLength)
                .ThenBy(c => c.SortValue)
                .ThenBy(c => c.ItemNumber, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// A parameter's value as text, whatever it is stored as.
        ///
        /// Item Number in particular could reasonably be authored as text or as a
        /// number, and assuming either one reads the other as empty — which would
        /// drop that bolt from the picker silently.
        /// </summary>
        internal static string? ParameterText(Parameter? parameter)
        {
            if (parameter is null || !parameter.HasValue) return null;

            try
            {
                return parameter.StorageType switch
                {
                    StorageType.String => parameter.AsString(),
                    StorageType.Integer => parameter.AsInteger().ToString(),
                    StorageType.Double => parameter.AsValueString()
                                          ?? parameter.AsDouble().ToString("0.###"),
                    StorageType.ElementId => parameter.AsValueString(),
                    _ => parameter.AsValueString(),
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Whether a symbol is any bolt on the whitelist.
        ///
        /// Used by duplicate detection, which asks whether a hole is already taken
        /// rather than whether it holds one particular fastener.
        /// </summary>
        internal static bool IsWhitelistedBolt(FamilySymbol? symbol)
        {
            if (symbol is null) return false;

            string? itemNumber = ParameterText(symbol.LookupParameter(PARAM_ITEM_NUMBER));

            return itemNumber is not null
                && BOLT_ITEM_NUMBERS.Contains(itemNumber.Trim(), StringComparer.Ordinal);
        }

        /// <summary>
        /// Swallows warnings raised while the measurement probe places its
        /// throwaway instance. Nothing about a placement that is about to be
        /// rolled back is worth interrupting the user for.
        /// </summary>
        private sealed class SwallowWarnings : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
            {
                foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
                {
                    if (failure.GetSeverity() == FailureSeverity.Warning)
                    {
                        accessor.DeleteWarning(failure);
                    }
                }

                return FailureProcessingResult.Continue;
            }
        }

        /// <summary>
        /// Measures the bolt by PLACING one, reading it, and rolling back.
        ///
        /// Named for what it does because it writes to the document, which a
        /// helper sitting among pure geometry functions has no business doing
        /// silently. The transaction is always rolled back — never committed — so
        /// nothing persists and the run contributes no extra undo step.
        ///
        /// This exists because a FamilySymbol cannot see shared nested content at
        /// all: it is structurally unavailable until an instance exists. Reading
        /// the symbol worked for exactly those bolts whose content happened to be
        /// flattened when they were authored, which is not the same as working.
        ///
        /// COORDINATE SPACE is the thing to be careful about here. A symbol's
        /// geometry arrives in the family's own coordinates, so spans map straight
        /// onto local X/Y/Z. An instance's geometry arrives in PROJECT
        /// coordinates. Measured as they come, the spans would describe the
        /// project axes and wideAxisIsLocalX would be a confident answer to the
        /// wrong question — feeding RollDirection a wrong roll on every bolt, in
        /// the same silent, visually-subtle way the 12.88 degree axis error did.
        /// Every point is therefore pulled back through the host instance's
        /// inverse transform first, sub-component geometry included: those arrive
        /// in project coordinates too, and the HOST's inverse is what lands them
        /// in the host family's local frame.
        /// </summary>
        internal static bool TryMeasureBoltByPlacing(
            Document doc, FamilySymbol symbol, out bool wideAxisIsLocalX)
            => TryMeasureBoltByPlacing(doc, symbol, out wideAxisIsLocalX, out _);

        /// <summary>
        /// As above, also reporting the measured spans so the comparison against
        /// the old symbol-based method can check the measurement itself rather
        /// than only the boolean it produces.
        /// </summary>
        internal static bool TryMeasureBoltByPlacing(
            Document doc, FamilySymbol symbol, out bool wideAxisIsLocalX, out XYZ spans)
        {
            wideAxisIsLocalX = false;
            spans = XYZ.Zero;

            using var tx = new Transaction(doc, "Measure bolt (rolled back)");

            try
            {
                tx.Start();

                FailureHandlingOptions failures = tx.GetFailureHandlingOptions();
                failures.SetClearAfterRollback(true);
                failures.SetForcedModalHandling(false);
                failures.SetFailuresPreprocessor(new SwallowWarnings());
                tx.SetFailureHandlingOptions(failures);

                if (!symbol.IsActive)
                {
                    symbol.Activate();
                    doc.Regenerate();
                }

                // Work-plane-based families need a plane to sit on; anything else
                // takes the bare point overload. Where it goes does not matter —
                // none of this survives the rollback.
                FamilyInstance instance =
                    symbol.Family?.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased
                        ? doc.Create.NewFamilyInstance(
                            XYZ.Zero,
                            symbol,
                            XYZ.BasisX,
                            SketchPlane.Create(
                                doc, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero)),
                            StructuralType.NonStructural)
                        : doc.Create.NewFamilyInstance(
                            XYZ.Zero, symbol, StructuralType.NonStructural);

                doc.Regenerate();

                List<XYZ> points = LocalSpacePointsOf(doc, instance);

                if (points.Count == 0) return false;

                double spanX = points.Max(p => p.X) - points.Min(p => p.X);
                double spanY = points.Max(p => p.Y) - points.Min(p => p.Y);
                double spanZ = points.Max(p => p.Z) - points.Min(p => p.Z);

                spans = new XYZ(spanX, spanY, spanZ);

                // Longest span is the shaft. Of the two axes left, the broader one
                // carries the head across its flats, and which of them it is
                // decides how the reference direction maps in RollDirection.
                if (spanX >= spanY && spanX >= spanZ)
                {
                    // The shaft is local X, so neither cross axis can be local X.
                    wideAxisIsLocalX = false;
                }
                else if (spanY >= spanX && spanY >= spanZ)
                {
                    wideAxisIsLocalX = spanX >= spanZ;
                }
                else
                {
                    wideAxisIsLocalX = spanX >= spanY;
                }

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                // Unconditional. The probe must not leave anything behind even if
                // it threw halfway through placing.
                if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
            }
        }

        /// <summary>
        /// Every solid edge vertex of a placed instance and of everything nested
        /// inside it, expressed in that instance's own local frame.
        ///
        /// Sub-components are walked recursively: a shared nested family can
        /// itself nest, and stopping at the first level would quietly measure only
        /// part of the bolt.
        /// </summary>
        private static List<XYZ> LocalSpacePointsOf(Document doc, FamilyInstance instance)
        {
            var points = new List<XYZ>();

            Transform toLocal;
            try { toLocal = instance.GetTransform().Inverse; }
            catch { return points; }

            AddLocalSpacePoints(doc, instance, toLocal, points, 0);
            return points;
        }

        private static void AddLocalSpacePoints(
            Document doc, FamilyInstance instance, Transform toLocal, List<XYZ> sink, int depth)
        {
            if (depth > MAX_NESTING_DEPTH) return;

            Options options = ModelGeometryOptions();

            GeometryElement? geometry;
            try { geometry = instance.get_Geometry(options); }
            catch { geometry = null; }

            if (geometry is not null)
            {
                var solids = new List<Solid>();
                CollectSolids(geometry, Transform.Identity, solids);

                foreach (Solid solid in solids)
                {
                    foreach (Edge edge in solid.Edges)
                    {
                        try
                        {
                            foreach (XYZ p in edge.Tessellate()) sink.Add(toLocal.OfPoint(p));
                        }
                        catch { }
                    }
                }
            }

            ICollection<ElementId> subs;
            try { subs = instance.GetSubComponentIds(); }
            catch { return; }

            foreach (ElementId id in subs)
            {
                if (doc.GetElement(id) is FamilyInstance sub)
                {
                    AddLocalSpacePoints(doc, sub, toLocal, sink, depth + 1);
                }
            }
        }

        /// <summary>
        /// The direction the bolt's local X must point for its head to sit across
        /// the slot rather than along it.
        ///
        /// This models the fastener in its LOCKED position: turned a quarter turn
        /// from how it goes in, so the head is caught behind the wall. The
        /// head measures 21.2 mm across against an 11 mm slot, so it cannot pass
        /// through broadside — it goes in end-on and is then turned, and locked is
        /// the state worth modelling.
        ///
        /// CAUTION: that reading is inferred from the measured geometry and the
        /// "Locking-Set" name — a T-slot / hammer-head fastener. It has not been
        /// checked against a physical sample or the manufacturer's drawing. If the
        /// bolts ever come out a quarter turn off, the fix is this one function,
        /// not the surrounding design.
        /// </summary>
        internal static XYZ RollDirection(XYZ planeNormal, XYZ slotLongAxis, bool wideAxisIsLocalX)
        {
            // Across the slot, staying in the interface plane.
            XYZ across = planeNormal.CrossProduct(slotLongAxis);

            // A round hole gives no meaningful long axis, and a slot parallel to
            // the normal is impossible; either way, fall back to something stable
            // rather than normalising a zero vector.
            if (across.GetLength() < AXIS_EPSILON)
            {
                across = Math.Abs(planeNormal.Z) < 0.9
                    ? planeNormal.CrossProduct(XYZ.BasisZ)
                    : planeNormal.CrossProduct(XYZ.BasisX);
            }

            across = across.Normalize();

            // The reference direction becomes the instance's local X. When the
            // head's broad dimension lies along local Y instead, local X has to be
            // set a quarter turn back so that local Y lands across the slot.
            return wideAxisIsLocalX ? across : across.CrossProduct(planeNormal).Normalize();
        }

        // ── Picking ────────────────────────────────────────────────────────────

        /// <summary>
        /// Takes the whole selection in one go. PickObjects gives click-to-add and
        /// box selection without any work of ours, and it returns each element
        /// once, which is why nothing here has to guard against the same element
        /// arriving twice.
        ///
        /// The order is preserved as PickObjects returns it, and it matters: it is
        /// what decides each joint's orientation downstream. For click-to-add that
        /// is the order the user clicked. For a box or drag selection it is
        /// whatever order Revit hands back — not the order things were enclosed,
        /// and not something the user controls.
        ///
        /// Returns false when the user escapes or the selection is unusable,
        /// having already told them why.
        /// </summary>
        internal static bool TryPickElements(UIDocument uiDoc, out List<Element> elements)
        {
            elements = new List<Element>();

            Document doc = uiDoc.Document;

            IList<Reference> picked;

            try
            {
                picked = uiDoc.Selection.PickObjects(
                    ObjectType.Element,
                    new SolidElementFilter(),
                    "Select the elements to bolt together, then finish.");
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                // User pressed Escape. Not an error.
                return false;
            }

            var seen = new HashSet<ElementId>();

            foreach (Reference reference in picked)
            {
                Element? element = doc.GetElement(reference);

                if (element is not null && seen.Add(element.Id))
                {
                    elements.Add(element);
                }
            }

            if (elements.Count < 2)
            {
                AvControls.ShowAlert(
                    DIALOG_TITLE,
                    $"Only {elements.Count} element(s) were selected.\n\n" +
                    "This command bolts elements to each other, so it needs at least two. " +
                    "Select everything in the assembly and it will bolt each joint it finds " +
                    "among them.",
                    AlertSeverity.Warning);
                return false;
            }

            return true;
        }

        // ── Duplicate guard ────────────────────────────────────────────────────

        /// <summary>
        /// Keeps track of where bolts are going, and refuses a point that lands on
        /// one already spoken for.
        ///
        /// Two separate cases, counted separately because they mean different
        /// things. A within-run collision means two joints in this one selection
        /// claimed the same hole, which is worth knowing about. A collision with
        /// the document means the command has already been run over this ground,
        /// which is routine.
        ///
        /// Both are proximity tests and nothing more: where two joints claim one
        /// location the first one found wins, with no attempt to judge which point
        /// is the better of the two.
        /// </summary>
        internal sealed class BoltLedger
        {
            private readonly List<(XYZ Point, double Tolerance)> _accepted = new();
            private readonly IReadOnlyList<XYZ> _existing;

            internal int DuplicateInRun { get; private set; }
            internal int AlreadyInDocument { get; private set; }

            internal BoltLedger(IReadOnlyList<XYZ> existingBoltPoints)
                => _existing = existingBoltPoints;

            /// <summary>
            /// Takes the point if it is free, and says so. Checks this run before
            /// the document, so a point colliding with both is reported as the
            /// within-run case — the more surprising of the two.
            ///
            /// Each point carries its own tolerance now, derived from the openings
            /// it passes through, so two points from joints of different sizes are
            /// compared at the SMALLER of the two. That is the conservative
            /// direction on purpose: merging two points that are not really the
            /// same fastener loses a bolt silently, while failing to merge two
            /// that are leaves a duplicate anyone can see and delete.
            ///
            /// Bolts already in the document carry no tolerance — nothing records
            /// what openings they went through — so they are judged at the new
            /// point's own.
            /// </summary>
            internal bool TryAccept(XYZ point, double tolerance)
            {
                if (_accepted.Any(a => a.Point.DistanceTo(point) <= Math.Min(tolerance, a.Tolerance)))
                {
                    DuplicateInRun++;
                    return false;
                }

                if (_existing.Any(p => p.DistanceTo(point) <= tolerance))
                {
                    AlreadyInDocument++;
                    return false;
                }

                _accepted.Add((point, tolerance));
                return true;
            }
        }

        /// <summary>
        /// Where the bolts of this family already sit, near the selection.
        ///
        /// The bounding-box filter is a Revit quick filter, so bolts elsewhere in
        /// the model are ruled out by the index without being expanded at all.
        /// That matters: a fully bolted job could hold thousands, and only the
        /// ones beside the elements in hand can possibly collide with anything
        /// this run places. What survives is small enough for plain distance
        /// checks and needs no structure of its own.
        ///
        /// Revit records nothing about which command placed an instance, so this
        /// finds every bolt regardless of origin. That is the right behaviour
        /// anyway — the question being asked is whether the hole is taken, not who
        /// took it.
        ///
        /// For the same reason it matches ANY whitelisted bolt, not only the one
        /// currently picked. This deliberately widens what the duplicate guard
        /// sees, and is not an oversight: a hole holding bolt A is just as taken
        /// when the user comes back with bolt B, and matching only the picked
        /// family would put two fasteners through one opening. The guard asks
        /// about the hole, so it has to look at every bolt that could be in it.
        /// </summary>
        internal static List<XYZ> ExistingBoltPoints(Document doc, Bounds? selection)
        {
            var points = new List<XYZ>();

            if (selection is null) return points;

            double margin = Ft(EXISTING_BOLT_SEARCH_MARGIN_MM);

            var outline = new Outline(
                new XYZ(selection.Min.X - margin, selection.Min.Y - margin, selection.Min.Z - margin),
                new XYZ(selection.Max.X + margin, selection.Max.Y + margin, selection.Max.Z + margin));

            IEnumerable<FamilyInstance> nearby;

            try
            {
                nearby = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .WherePasses(new BoundingBoxIntersectsFilter(outline))
                    .Cast<FamilyInstance>();
            }
            catch
            {
                return points;
            }

            // Reading a type parameter per instance would be wasteful over a dense
            // area, and every instance of one type gives the same answer, so the
            // verdict is cached per symbol.
            var verdictBySymbol = new Dictionary<ElementId, bool>();

            foreach (FamilyInstance instance in nearby)
            {
                FamilySymbol? symbol = instance.Symbol;
                if (symbol is null) continue;

                if (!verdictBySymbol.TryGetValue(symbol.Id, out bool isBolt))
                {
                    isBolt = IsWhitelistedBolt(symbol);
                    verdictBySymbol[symbol.Id] = isBolt;
                }

                if (!isBolt) continue;

                if (instance.Location is LocationPoint location)
                {
                    points.Add(location.Point);
                }
            }

            return points;
        }

        // ── Planning one joint ─────────────────────────────────────────────────

        /// <summary>Why a pair did or did not produce a joint.</summary>
        internal enum JointOutcome
        {
            /// <summary>One side contributed no candidate faces.</summary>
            NoGeometry,

            /// <summary>Bounding boxes nowhere near each other.</summary>
            BoxRejected,

            /// <summary>Near enough, but no opposed coincident faces.</summary>
            NotTouching,

            /// <summary>Touching, but no pair of openings lines up.</summary>
            NoAlignedVoids,

            /// <summary>Openings aligned, but every point was already taken.</summary>
            NoBoltsAccepted,

            /// <summary>A joint with at least one bolt to place.</summary>
            Planned,
        }

        /// <summary>Run-wide tallies a single joint contributes to.</summary>
        internal sealed class JointStats
        {
            internal int NearMisses { get; set; }
            internal int SkippedBothRound { get; set; }
            internal int SkippedNoWidth { get; set; }
            internal int OtherShapeRolls { get; set; }
            internal int FallbackSeatJoints { get; set; }
        }

        /// <summary>
        /// Everything one pair of elements needs to become a planned joint:
        /// interface, openings, matching, seat depth and roll.
        ///
        /// Extracted from the command's pair loop so the preview and the real run
        /// share ONE implementation rather than two that agree today. That is not
        /// a theoretical worry on this project — a diagnostic once reimplemented
        /// TryMeasureBolt separately and the two drifted, which is how the shared
        /// nested geometry problem stayed hidden as long as it did.
        ///
        /// Places nothing and opens no transaction. The caller decides what to do
        /// with the result, which is what lets a preview call it safely.
        ///
        /// The ledger is consulted and updated, so a caller reusing one across
        /// several pairs gets the duplicate behaviour of a real run, and a caller
        /// passing a throwaway gets an isolated answer.
        /// </summary>
        internal static JointOutcome TryPlanJoint(
            Element a,
            Element b,
            List<PlanarFace> facesA,
            List<PlanarFace> facesB,
            Bounds? boundsA,
            Bounds? boundsB,
            BoltLedger ledger,
            bool wideAxisIsLocalX,
            JointStats stats,
            out PlannedJoint? joint)
        {
            joint = null;

            if (facesA.Count == 0 || facesB.Count == 0) return JointOutcome.NoGeometry;

            // Cheap reject before the full face sweep. Dropping the family filter
            // removed an implicit limit on how many pairs reach this point, and
            // most of them in a real selection are nowhere near each other. The
            // margin is the gap tolerance, so a pair touching across a permitted
            // gap is never lost here.
            if (boundsA is not null
                && boundsB is not null
                && !boundsA.Overlaps(boundsB, Ft(GAP_TOLERANCE_MM)))
            {
                return JointOutcome.BoxRejected;
            }

            List<FacePair> found = FindInterfaces(facesA, facesB);

            if (found.Count == 0) return JointOutcome.NotTouching;

            // FindInterfaces was handed a's faces first, so contact.First is a —
            // the earlier of the two in the selection. That is the whole ordering
            // rule, and it is deliberate.
            FacePair contact = found[0];

            List<Opening> firstOpenings = OpeningsOf(contact.First, contact.Frame);
            List<Opening> secondOpenings = OpeningsOf(contact.Second, contact.Frame);

            MatchReport match = MatchOpenings(firstOpenings, secondOpenings, contact.Frame);

            stats.NearMisses += match.NearMissCount;
            stats.SkippedBothRound += match.SkippedBothRound;
            stats.SkippedNoWidth += match.SkippedNoWidth;
            stats.OtherShapeRolls += match.OtherShapeRolls;

            if (match.Matched.Count == 0) return JointOutcome.NoAlignedVoids;

            // The work plane's normal becomes every bolt's local +Z at this joint.
            // One face, one direction, all of its bolts.
            XYZ planeNormal = (contact.First.FaceNormal * SHANK_ORIENTATION).Normalize();

            // Seat the bolts past the interface plane, deeper into the joint, by
            // the thickness of the SECOND element's wall — the wall the head
            // passes through to catch behind.
            //
            // Second's rather than First's is a deliberate choice, not an
            // inherited one. Mekano's two walls are both 3 mm, so no Mekano test
            // could have told the two options apart; this follows what the trial
            // build actually placed on the tray and what was confirmed there by
            // eye.
            //
            // Measured from Second's OWN interface face. Measuring from the shared
            // contact plane returns the gap instead of the wall once the two are
            // not flush — see WallThickness.
            //
            // The gap itself is not added, split or compensated: at the permitted
            // 2 mm it is treated as noise for placement even though it is
            // meaningful for detection.
            double? measuredWall = WallThickness(
                facesB, contact.Second, contact, contact.First.FaceNormal);

            if (measuredWall is null) stats.FallbackSeatJoints++;

            double seatDepth = measuredWall ?? Ft(FALLBACK_SEAT_OFFSET_MM);

            // The work plane moves with the points. A work-plane-based instance
            // lives on its plane, so offsetting the points alone would leave them
            // off-plane and the shift would not survive placement.
            XYZ seatOffset = planeNormal * seatDepth;

            var planned = new PlannedJoint(
                planeNormal,
                contact.Frame.Origin + seatOffset,
                LabelOf(a),
                LabelOf(b),
                contact.Gap,
                seatDepth,
                measuredWall is not null);

            foreach (BoltPoint bolt in match.Matched)
            {
                XYZ point = bolt.Point + seatOffset;

                // Checked at the seated position, which is where the bolt actually
                // ends up.
                if (!ledger.TryAccept(point, bolt.Tolerance)) continue;

                planned.Bolts.Add(new PlannedBolt(
                    point,
                    RollDirection(planeNormal, bolt.SlotLongAxis, wideAxisIsLocalX)));
            }

            if (planned.Bolts.Count == 0) return JointOutcome.NoBoltsAccepted;

            joint = planned;
            return JointOutcome.Planned;
        }

        /// <summary>
        /// Everything one selection's planning produced: the joints, the run-wide
        /// tallies, the ledger they were checked against, and the funnel counters
        /// that say how many pairs fell out at each stage.
        ///
        /// Exists so PlanSelection can hand back one object rather than eight out
        /// parameters, and so the window and the command read the same figures by
        /// the same names.
        /// </summary>
        internal sealed class SelectionPlan
        {
            internal List<PlannedJoint> Joints { get; } = new();
            internal JointStats Stats { get; } = new();
            internal BoltLedger Ledger { get; }

            /// <summary>How many elements went in.</summary>
            internal int ElementCount { get; }

            /// <summary>
            /// False when not one selected element yielded a usable face. A
            /// different failure from "nothing touched" and reported differently.
            /// </summary>
            internal bool AnyGeometry { get; }

            internal int PairsConsidered { get; set; }
            internal int PairsBoxRejected { get; set; }
            internal int TouchingPairs { get; set; }
            internal int PairsWithAlignedVoids { get; set; }

            internal SelectionPlan(BoltLedger ledger, int elementCount, bool anyGeometry)
            {
                Ledger = ledger;
                ElementCount = elementCount;
                AnyGeometry = anyGeometry;
            }

            /// <summary>
            /// Every bolt this selection could take, across every joint — what
            /// gets placed when the count is left at its maximum.
            /// </summary>
            internal int MaxBoltsTotal => Joints.Sum(j => j.Bolts.Count);

            /// <summary>
            /// The most any ONE joint can take. This is what the per-joint count
            /// is capped at, and it is deliberately not the total: joints hold
            /// different numbers, and a count of 3 means "up to 3 in each".
            /// </summary>
            internal int MaxBoltsPerJoint => Joints.Count == 0 ? 0 : Joints.Max(j => j.Bolts.Count);

            /// <summary>
            /// The worst-fitting joint's gap. A mean would bury exactly the joint
            /// worth looking at, and "the gap" means nothing across several.
            /// </summary>
            internal double WidestGap => Joints.Count == 0 ? 0.0 : Joints.Max(j => j.Gap);
        }

        /// <summary>What a finished plan amounts to, in one word.</summary>
        internal enum PlanVerdict
        {
            /// <summary>Not one selected element yielded a usable face.</summary>
            NoGeometry,

            /// <summary>Geometry, but nothing met anything else.</summary>
            NothingTouching,

            /// <summary>Joints, but not one produced a bolt.</summary>
            NoBolts,

            /// <summary>Positions were found and every one was already bolted.</summary>
            AllTaken,

            /// <summary>At least one joint with at least one bolt to place.</summary>
            Ready,
        }

        /// <summary>
        /// Classifies a plan once, so the alert the command shows and the line the
        /// window writes are answering the same question.
        ///
        /// The two surfaces word it differently — a modal paragraph is not an
        /// inline sentence — but neither decides for itself which case it is in.
        /// </summary>
        internal static PlanVerdict VerdictOf(SelectionPlan plan)
        {
            if (plan.Joints.Count > 0) return PlanVerdict.Ready;

            if (!plan.AnyGeometry) return PlanVerdict.NoGeometry;

            // Checked before "nothing touched": finding positions and having them
            // all taken is a different, and much more ordinary, thing than never
            // finding any.
            if (plan.Ledger.AlreadyInDocument > 0 || plan.Ledger.DuplicateInRun > 0)
            {
                return PlanVerdict.AllTaken;
            }

            return plan.TouchingPairs == 0
                ? PlanVerdict.NothingTouching
                : PlanVerdict.NoBolts;
        }

        /// <summary>
        /// Reads every selected element's geometry once, then plans every pair
        /// among them against ONE ledger.
        ///
        /// One ledger for the whole selection is the reason this exists as a
        /// method rather than a loop over a per-pair helper. A point accepted by
        /// an earlier pair must be refused to a later one, so planning pair by
        /// pair with a fresh ledger each time over-counts wherever two joints
        /// claim the same hole. The window's preview needs the same arithmetic the
        /// real run performs, which means it needs the same single ledger.
        ///
        /// Called by the window's preview and by the command's placement, and
        /// TryPlanJoint has exactly one caller — this — so the number shown and
        /// the number placed cannot drift apart.
        ///
        /// Opens no transaction, so a preview may call it freely.
        ///
        /// wideAxisIsLocalX reaches only RollDirection, which sets each bolt's
        /// reference direction. It cannot change how many bolts are found, which
        /// is why a preview may pass a literal for it and still show the number
        /// that gets placed.
        /// </summary>
        internal static SelectionPlan PlanSelection(
            Document doc,
            List<Element> selection,
            bool wideAxisIsLocalX)
        {
            // Every element appears in N-1 pairs and this is the expensive part,
            // so it is done per element rather than per pair. Bounds come from the
            // same solids, which is both cheaper than a second pass and exactly
            // consistent with what the face sweep will see.
            var facesOf = new Dictionary<ElementId, List<PlanarFace>>();
            var boundsOf = new Dictionary<ElementId, Bounds>();

            foreach (Element element in selection)
            {
                List<Solid> solids = SolidsOf(element);

                facesOf[element.Id] = CandidateFaces(solids);

                Bounds? bounds = Bounds.Of(solids);
                if (bounds is not null) boundsOf[element.Id] = bounds;
            }

            bool anyGeometry = facesOf.Values.Any(faces => faces.Count > 0);

            // The ledger lives for the whole run, so a point is checked against
            // everything already queued from any pair, not just its own joint.
            var plan = new SelectionPlan(
                new BoltLedger(ExistingBoltPoints(doc, Bounds.Union(boundsOf.Values))),
                selection.Count,
                anyGeometry);

            // Every pair, in selection order. For a pair the earlier element is
            // First and the later one is Second — the same rule the original
            // two-pick command followed, where pick 1 was First and pick 2 was
            // Second, just generalised across N.
            //
            // That rule decides the bolt AXIS: the shank enters the First element
            // and the head seats on the Second's side. It does NOT decide roll any
            // more — roll follows the openings' shapes and may come from either
            // one. Two rules, coexisting, each justified on its own; see
            // MatchOpenings.
            //
            // Click-to-add gives full control of the axis — click the element you
            // want the shank in first. A box or drag selection does NOT: the order
            // Revit returns is whatever it is, not the order things were enclosed,
            // and it is not meaningful. Where a particular joint's orientation
            // matters, either build the selection by clicking in the order you
            // want, or run on a smaller explicit selection where that order can be
            // controlled.
            //
            // Deriving the axis from geometry instead was built and withdrawn: not
            // reliable enough to be worth the machinery. Explicit order is simpler
            // and reproduces behaviour already proven in the model.
            for (int i = 0; i < selection.Count; i++)
            {
                for (int j = i + 1; j < selection.Count; j++)
                {
                    Element a = selection[i];
                    Element b = selection[j];

                    plan.PairsConsidered++;

                    boundsOf.TryGetValue(a.Id, out Bounds? boundsA);
                    boundsOf.TryGetValue(b.Id, out Bounds? boundsB);

                    JointOutcome outcome = TryPlanJoint(
                        a, b,
                        facesOf[a.Id], facesOf[b.Id],
                        boundsA, boundsB,
                        plan.Ledger,
                        wideAxisIsLocalX,
                        plan.Stats,
                        out PlannedJoint? joint);

                    if (outcome == JointOutcome.BoxRejected) plan.PairsBoxRejected++;

                    // Anything that did touch counts as touching, whatever
                    // happened afterwards.
                    if (outcome is not JointOutcome.NoGeometry
                        and not JointOutcome.BoxRejected
                        and not JointOutcome.NotTouching)
                    {
                        plan.TouchingPairs++;
                    }

                    // NoAlignedVoids is excluded deliberately: it means the match
                    // found nothing, which is exactly what this counter must not
                    // count.
                    if (outcome is JointOutcome.NoBoltsAccepted or JointOutcome.Planned)
                    {
                        plan.PairsWithAlignedVoids++;
                    }

                    if (outcome == JointOutcome.Planned && joint is not null)
                    {
                        plan.Joints.Add(joint);
                    }
                }
            }

            return plan;
        }
    }

    /// <summary>One bolt, ready to place.</summary>
    internal sealed class PlannedBolt
    {
        internal XYZ Point { get; }
        internal XYZ ReferenceDirection { get; }

        internal PlannedBolt(XYZ point, XYZ referenceDirection)
        {
            Point = point;
            ReferenceDirection = referenceDirection;
        }
    }

    /// <summary>
    /// One joint's worth of bolts, with the work plane they all share.
    /// </summary>
    internal sealed class PlannedJoint
    {
        internal XYZ PlaneNormal { get; }
        internal XYZ PlaneOrigin { get; }
        internal List<PlannedBolt> Bolts { get; } = new();

        // ── Carried for the report ─────────────────────────────────────────────
        //
        // All of this was already computed inside the pair loop and discarded at
        // the end of each iteration. None of it costs anything to keep; it simply
        // had nowhere to go before there was a report to put it in.

        /// <summary>The two elements this joint is between, as the user sees them.</summary>
        internal string FirstLabel { get; }
        internal string SecondLabel { get; }

        /// <summary>Measured separation between the two faces, internal units.</summary>
        internal double Gap { get; }

        /// <summary>The seat depth actually used, internal units.</summary>
        internal double SeatOffset { get; }

        /// <summary>
        /// Whether that seat was measured from a real wall or fell back.
        ///
        /// Reported alongside the number because the two cases can produce the
        /// same figure and do not mean the same thing: 3 mm because a wall
        /// measured 3 mm, versus 3 mm because nothing could be measured at all.
        /// </summary>
        internal bool SeatWasMeasured { get; }

        /// <summary>
        /// The bolts actually created here. Filled during placement, not planning
        /// — they do not exist until then — and the only reason the report can
        /// select anything.
        /// </summary>
        internal List<ElementId> PlacedBoltIds { get; } = new();

        internal PlannedJoint(
            XYZ planeNormal,
            XYZ planeOrigin,
            string firstLabel,
            string secondLabel,
            double gap,
            double seatOffset,
            bool seatWasMeasured)
        {
            PlaneNormal = planeNormal;
            PlaneOrigin = planeOrigin;
            FirstLabel = firstLabel;
            SecondLabel = secondLabel;
            Gap = gap;
            SeatOffset = seatOffset;
            SeatWasMeasured = seatWasMeasured;
        }
    }

    /// <summary>
    /// Takes a selection of elements, finds every joint among them where two meet
    /// face to face with coinciding voids, and bolts each one.
    ///
    /// Works in two phases. All the geometry is read and every bolt planned
    /// first, outside any transaction; then one transaction places the lot, and
    /// rolls the whole run back if any part of it fails. That keeps the
    /// transaction short and means a geometry problem never leaves half a frame
    /// bolted.
    ///
    /// Within a joint, every bolt's axis is the interface face normal of whichever
    /// element came first in the selection — one rule for the joint, not a value
    /// re-derived per void. The roll is separate: it puts the head across
    /// whichever of the two openings can be locked across, which may be either.
    /// The seat is the measured thickness of the second element's wall.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class Cmd_MG2 : IExternalCommand
    {
        // ── Failure logging ────────────────────────────────────────────────────

        private const string FAILURE_LOG_FILE = "AVES_MG2_BoltFailure.txt";

        /// <summary>
        /// Writes everything known about a bolt that could not be placed, and
        /// returns the file path so the alert can point at it.
        ///
        /// A ShowAlert has room for a sentence, and the useful part of these
        /// failures is a placement type, a geometry probe and possibly a stack
        /// trace. The alert stays as the signal that something failed; this is
        /// where the reason lives.
        ///
        /// APPENDS rather than overwrites. Diagnosing this means running the
        /// command once per bolt, and overwriting would quietly discard an earlier
        /// run if two happen before the file is read.
        ///
        /// Also surveys every whitelisted bolt in the document, not just the one
        /// that failed: if the cause is per-family authoring — one family not
        /// work-plane based, another with no geometry — a single run shows the
        /// whole pattern instead of needing one run per bolt.
        /// </summary>
        private static string LogBoltFailure(
            Document doc,
            MG2Shared.BoltChoice choice,
            string failedCheck,
            string detail,
            Exception? exception = null)
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), FAILURE_LOG_FILE);

            try
            {
                var log = new StringBuilder();

                log.AppendLine();
                log.AppendLine("========================================================");
                log.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  —  {doc.Title}");
                log.AppendLine("========================================================");
                log.AppendLine();

                log.AppendLine("== The bolt that failed ==");
                log.AppendLine($"Item Number   : {choice.ItemNumber}");
                log.AppendLine($"Description   : {choice.Description}");
                log.AppendLine($"Family        : \"{choice.Symbol.Family?.Name}\"");
                log.AppendLine($"Type          : \"{choice.Symbol.Name}\"");
                log.AppendLine($"Symbol id     : {choice.Symbol.Id}");
                log.AppendLine($"Category      : {choice.Symbol.Category?.Name}");
                log.AppendLine();

                log.AppendLine("== What failed ==");
                log.AppendLine($"Check         : {failedCheck}");
                log.AppendLine($"Detail        : {detail}");
                log.AppendLine();

                if (exception is not null)
                {
                    log.AppendLine("== Exception ==");
                    log.AppendLine($"Type          : {exception.GetType().FullName}");
                    log.AppendLine($"Message       : {exception.Message}");
                    log.AppendLine();
                    log.AppendLine("Stack trace:");
                    log.AppendLine(exception.StackTrace ?? "(none)");
                    log.AppendLine();

                    if (exception.InnerException is not null)
                    {
                        log.AppendLine("Inner exception:");
                        log.AppendLine($"  {exception.InnerException.GetType().FullName}");
                        log.AppendLine($"  {exception.InnerException.Message}");
                        log.AppendLine();
                    }
                }
                else
                {
                    log.AppendLine("No exception was thrown — this check returned false or " +
                                   "reported an unexpected value rather than failing.");
                    log.AppendLine();
                }

                AppendWhitelistSurvey(log, doc);

                System.IO.File.AppendAllText(path, log.ToString(), Encoding.UTF8);
            }
            catch
            {
                // Logging must never be the thing that takes the command down.
            }

            return path;
        }

        /// <summary>
        /// Every whitelisted bolt loaded here, with the two things most likely to
        /// separate the ones that work from the ones that do not: placement type,
        /// and whether the symbol yields usable geometry.
        /// </summary>
        private static void AppendWhitelistSurvey(StringBuilder log, Document doc)
        {
            log.AppendLine("== Every whitelisted bolt in this document ==");
            log.AppendLine("(placement type and geometry probe — compare working against failing)");
            log.AppendLine();

            List<MG2Shared.BoltChoice> all = MG2Shared.LoadBoltChoices(doc);

            if (all.Count == 0)
            {
                log.AppendLine("(none found)");
                log.AppendLine();
                return;
            }

            foreach (MG2Shared.BoltChoice candidate in all)
            {
                FamilySymbol symbol = candidate.Symbol;

                log.AppendLine($"{candidate.ItemNumber} — \"{symbol.Family?.Name}\" / \"{symbol.Name}\"");
                log.AppendLine($"    placement type : {symbol.Family?.FamilyPlacementType}" +
                               (symbol.Family?.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased
                                   ? ""
                                   : "   <-- NOT WorkPlaneBased, this command refuses it"));
                log.AppendLine($"    in-place       : {symbol.Family?.IsInPlace}");
                log.AppendLine($"    active         : {symbol.IsActive}");
                log.AppendLine($"    geometry       : {ProbeSymbolGeometry(symbol)}");
                // The same measurement production uses, not the old symbol-based
                // one. Reporting the old method here would have the log call a
                // bolt unreadable that production places without trouble.
                //
                // Safe to open its transaction from inside logging: every
                // LogBoltFailure call site is outside a transaction, and the one
                // in the placement catch rolls back before it logs.
                log.AppendLine($"    measurement    : " +
                               (MG2Shared.TryMeasureBoltByPlacing(doc, symbol, out bool wideX)
                                   ? $"ok, broad head axis on local {(wideX ? "X" : "Y")}"
                                   : "FAILED — could not read geometry"));
                log.AppendLine();
            }
        }

        /// <summary>
        /// What a SYMBOL's geometry looks like, as opposed to a placed instance's.
        /// Reports the shape of it rather than only whether it worked, because
        /// "0 solids" and "get_Geometry threw" are different faults with different
        /// fixes.
        ///
        /// Kept in the log even though measurement no longer reads symbols: a
        /// symbol showing nothing while the placed instance measures fine is the
        /// signature of shared nested content, and that is worth being able to
        /// recognise from a log rather than rediscover.
        /// </summary>
        private static string ProbeSymbolGeometry(FamilySymbol symbol)
        {
            GeometryElement? geometry;

            try
            {
                geometry = symbol.get_Geometry(MG2Shared.ModelGeometryOptions());
            }
            catch (Exception ex)
            {
                return $"get_Geometry threw {ex.GetType().Name}: {ex.Message}";
            }

            if (geometry is null) return "get_Geometry returned null";

            int topLevel = 0;
            int instances = 0;

            foreach (GeometryObject obj in geometry)
            {
                topLevel++;
                if (obj is GeometryInstance) instances++;
            }

            var solids = new List<Solid>();
            MG2Shared.CollectSolids(geometry, Transform.Identity, solids);

            int faces = 0;
            int edgePoints = 0;

            foreach (Solid solid in solids)
            {
                faces += solid.Faces.Size;

                foreach (Edge edge in solid.Edges)
                {
                    try { edgePoints += edge.Tessellate().Count; }
                    catch { }
                }
            }

            return $"{topLevel} top-level object(s), {instances} nested instance(s), " +
                   $"{solids.Count} solid(s), {faces} face(s), {edgePoints} edge point(s)";
        }

        /// <summary>
        /// The bolt chosen last time, for the rest of the Revit session.
        ///
        /// Holds the ITEM NUMBER rather than the symbol or the label. A symbol's
        /// ElementId means nothing in another document and a label can be
        /// rewritten; the item number is the one handle stable enough to carry
        /// across either, which is the whole reason the whitelist is built on it.
        /// </summary>
        private static string? _lastPickedItemNumber;

        /// <summary>
        /// The bolt to show as already chosen: session memory first, then the one
        /// this command used to place, then whatever is at the top. Each step only
        /// applies if that bolt is actually loaded in this document.
        ///
        /// Null when nothing is loaded, which the caller is expected to say
        /// something about.
        ///
        /// Its own method because the entry window shows this before any picker
        /// opens. Two copies of the preference order would be two things to keep
        /// in step.
        /// </summary>
        internal static MG2Shared.BoltChoice? PreferredBolt(List<MG2Shared.BoltChoice> choices)
        {
            return choices.FirstOrDefault(c => string.Equals(
                       c.ItemNumber, _lastPickedItemNumber, StringComparison.Ordinal))
                   ?? choices.FirstOrDefault(c => string.Equals(
                       c.ItemNumber, MG2Shared.DEFAULT_BOLT_ITEM_NUMBER, StringComparison.Ordinal))
                   ?? choices.FirstOrDefault();
        }

        /// <summary>
        /// Puts the loaded bolts to the user and returns the one they chose, or
        /// null if they cancelled or there was nothing to choose from.
        ///
        /// Shows BoltPickerWindow, whose structure is copied from
        /// TagFamilyPickerWindow so this dialog is the same thing other commands
        /// put up rather than merely similar to it.
        ///
        /// The picker deals in strings, so the label-to-symbol mapping is ours.
        ///
        /// Internal rather than private because AddBoltsWindow — its only caller —
        /// opens this from its own section 1, and going through here is what keeps
        /// the session memory one value rather than two.
        ///
        /// <paramref name="owner"/> is that window, and the picker centres on it.
        /// </summary>
        internal static MG2Shared.BoltChoice? ChooseBolt(Document doc, WpfWindow owner)
        {
            List<MG2Shared.BoltChoice> choices = MG2Shared.LoadBoltChoices(doc);

            if (choices.Count == 0)
            {
                AvControls.ShowAlert(
                    MG2Shared.DIALOG_TITLE,
                    "None of the known bolts are loaded in this document.\n\n" +
                    "A bolt is recognised by its Item Number, one of:\n" +
                    string.Join(", ", MG2Shared.BOLT_ITEM_NUMBERS) + "\n\n" +
                    "Load one and re-run.",
                    AlertSeverity.Error);
                return null;
            }

            // Never null: choices is non-empty by the check above.
            MG2Shared.BoltChoice current = PreferredBolt(choices)!;

            // Two bolts could in principle carry the same description, and the
            // picker speaks in labels. First wins, matching how the scan itself
            // resolves a collision.
            var byLabel = new Dictionary<string, MG2Shared.BoltChoice>(StringComparer.Ordinal);

            foreach (MG2Shared.BoltChoice candidate in choices)
            {
                if (!byLabel.ContainsKey(candidate.DisplayLabel)) byLabel[candidate.DisplayLabel] = candidate;
            }

            // Instance pattern, matching TagFamilyPickerWindow and the other
            // single-select pickers: construct, ShowDialog, read the property.
            var picker = new BoltPickerWindow(
                "Select bolt", byLabel.Keys.ToList(), current.DisplayLabel)
            {
                // What makes the picker's CenterOwner mean something. It has one
                // caller and that caller always has a window, so this is not
                // conditional.
                Owner = owner,
            };

            // Cancelled, by button or Escape. Treated exactly like escaping out of
            // element selection.
            if (picker.ShowDialog() != true || picker.SelectedLabel is null) return null;

            if (!byLabel.TryGetValue(picker.SelectedLabel, out MG2Shared.BoltChoice? chosen))
            {
                return null;
            }

            _lastPickedItemNumber = chosen.ItemNumber;
            return chosen;
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument? uiDoc = commandData.Application.ActiveUIDocument;

            if (uiDoc is null)
            {
                AvControls.ShowAlert(
                    MG2Shared.DIALOG_TITLE,
                    "No active document.\n\nOpen a project and re-run.",
                    AlertSeverity.Warning);
                return Result.Cancelled;
            }

            try
            {
                // ── 1. The elements, first ─────────────────────────────────────
                // Taking the selection up front is what lets the window be a fixed
                // snapshot rather than something that has to track a moving one.
                //
                // Already alerts and returns false on fewer than two — see
                // TryPickElements — so there is nothing to add for that case here.
                if (!MG2Shared.TryPickElements(uiDoc, out List<Element> selection))
                {
                    return Result.Cancelled;
                }

                // ── 2. The window, for every selection ─────────────────────────
                //
                // Two elements and twelve go through the same door now. The larger
                // run used to place straight through with no preview and no count
                // control, which had it backwards: the run with more joints to get
                // wrong was the one nobody was shown first.
                //
                // MODAL. Two things follow from that, and both are the reason it
                // is modal rather than an accident of it.
                //
                // The selection cannot change while it is open, because Revit is
                // not reachable — so what the window previews is what exists when
                // it closes, with nothing to re-validate.
                //
                // And ShowDialog blocks inside this method, so the placement below
                // runs in the same API context the command started in. No external
                // event, no handler, no marshalling.
                var window = new AddBoltsWindow(uiDoc, selection);

                if (window.ShowDialog() != true) return Result.Cancelled;

                MG2Shared.BoltChoice? choice = window.ChosenBolt;

                if (choice is null) return Result.Cancelled;

                return PlaceSelection(uiDoc, selection, choice, window.BoltsPerJoint);
            }
            catch (Exception ex)
            {
                AvControls.ShowAlert(
                    MG2Shared.DIALOG_TITLE,
                    $"The command stopped unexpectedly.\n\n{ex.Message}",
                    AlertSeverity.Error);
                return Result.Failed;
            }
        }

        /// <summary>
        /// Places every joint the window previewed, after it has closed.
        ///
        /// Runs in Execute's own API context — ShowDialog blocked there and has
        /// just returned — which is why this can open a transaction directly and
        /// why the external event that used to be needed for it is gone.
        ///
        /// Re-plans rather than reusing the window's joints, and with the MEASURED
        /// axis this time: the window cannot call TryMeasureBoltByPlacing, which
        /// needs a transaction of its own. Same PlanSelection, same document, same
        /// inputs otherwise — the flag sets each bolt's reference direction and
        /// cannot change how many there are.
        ///
        /// <paramref name="wanted"/> is ONE number applied to EVERY joint, each
        /// clamped to what that joint actually holds. Joints hold different
        /// numbers, so a count of 3 means "up to 3 in each", not "3 in the first
        /// and nothing left for the rest".
        /// </summary>
        private static Result PlaceSelection(
            UIDocument uiDoc,
            List<Element> selection,
            MG2Shared.BoltChoice choice,
            int wanted)
        {
            Document doc = uiDoc.Document;

            // Two gates before anything is planned, both about the BOLT rather
            // than the joint.
            //
            // Work-plane based means it goes onto a sketch plane, and that is the
            // overload taking a reference direction — which is what sets the roll.
            // The simpler point-and-level overload cannot orient the family at
            // all, so anything else is refused rather than placed wrongly.
            //
            // And the measurement has to happen once, here, before planning:
            // planning needs its result to compute each bolt's reference
            // direction, and nothing in the loop may call it.
            if (choice.Symbol.Family?.FamilyPlacementType != FamilyPlacementType.WorkPlaneBased)
            {
                string log = LogBoltFailure(
                    doc, choice,
                    "FamilyPlacementType must be WorkPlaneBased",
                    $"This family reports {choice.Symbol.Family?.FamilyPlacementType}. Placement " +
                    "uses the sketch-plane overload, which is the only one taking a reference " +
                    "direction, and that direction is what sets the head's roll.");

                AvControls.ShowAlert(
                    MG2Shared.DIALOG_TITLE,
                    $"\"{choice.DisplayLabel}\" cannot be placed: it is a " +
                    $"{choice.Symbol.Family?.FamilyPlacementType} family, not work-plane based.\n\n" +
                    $"Full detail, including every whitelisted bolt in this document:\n{log}",
                    AlertSeverity.Error);
                return Result.Failed;
            }

            if (!MG2Shared.TryMeasureBoltByPlacing(doc, choice.Symbol, out bool wideAxisIsLocalX))
            {
                string log = LogBoltFailure(
                    doc, choice,
                    "TryMeasureBoltByPlacing could not measure the bolt",
                    "It places a throwaway instance and reads edge points from its solids and " +
                    "its sub-components to find the shank axis and the head's broad dimension.");

                AvControls.ShowAlert(
                    MG2Shared.DIALOG_TITLE,
                    $"The geometry of \"{choice.DisplayLabel}\" could not be read, " +
                    "so its orientation cannot be worked out.\n\n" +
                    $"Full detail, including every whitelisted bolt in this document:\n{log}",
                    AlertSeverity.Error);
                return Result.Failed;
            }

            MG2Shared.SelectionPlan plan =
                MG2Shared.PlanSelection(doc, selection, wideAxisIsLocalX);

            if (plan.Joints.Count == 0)
            {
                // The window showed these joints a moment ago and the selection
                // could not have moved behind a modal dialog, so this means the
                // document itself changed — a linked file reloaded, a worksharing
                // sync. ExplainEmptyPlan says which case it is.
                return ExplainEmptyPlan(plan);
            }

            // Read BEFORE the subsetting below, which shrinks joint.Bolts and
            // would otherwise have the report describe the reduced plan as though
            // it were the full one.
            int maxPerJointBefore = plan.MaxBoltsPerJoint;
            int maxTotalBefore = plan.MaxBoltsTotal;

            // ── How many of each joint's positions to fill ─────────────────────
            //
            // One joint: the count is the user's, and a count below what the joint
            // holds takes a random subset of that size.
            //
            // Several joints: every one takes everything it holds. The window
            // greys the count control for this case, and this line is what makes
            // that a RULE rather than a consequence of whatever the field happened
            // to contain — the default equals the largest joint's maximum, so the
            // clamp below would produce the same result today, and that is exactly
            // the kind of agreement that stops being true after an unrelated edit.
            //
            // Choosing "up to N per joint" across joints of different sizes is a
            // setting whose effect varies per joint and which nobody can picture,
            // which is why it is not offered rather than merely not shown.
            bool subsetAllowed = plan.Joints.Count == 1;

            if (subsetAllowed)
            {
                // Clamped here rather than trusted from the window: the plan was
                // just rebuilt, and it is the only thing that knows what this
                // joint holds now.
                PlannedJoint joint = plan.Joints[0];

                int available = joint.Bolts.Count;
                int take = Math.Min(available, Math.Max(1, wanted));

                if (take < available) TakeRandomSubset(joint, take);
            }

            if (!TryPlaceJoints(doc, choice, plan.Joints, out int placed))
            {
                // Already explained, already rolled back.
                return Result.Failed;
            }

            // Null when no subsetting was allowed, so the report's random-subset
            // footnote is driven by the same rule the placement was — not by the
            // two numbers happening to come out equal.
            ShowRunReport(
                uiDoc, doc, plan, placed,
                subsetAllowed ? wanted : null,
                maxPerJointBefore,
                maxTotalBefore);

            return Result.Succeeded;
        }

        /// <summary>
        /// Cuts the joint down to <paramref name="take"/> of its bolts, chosen at
        /// random.
        ///
        /// A full Fisher-Yates shuffle before taking the first N, so every subset
        /// of that size is equally likely — not "skip every other one", which is
        /// what a naive stride would give and would put the bolts in a pattern
        /// nobody asked for.
        ///
        /// Random.Shared, unseeded: repeating the same run on the same joint is
        /// meant to land somewhere else, and a seed would defeat that.
        ///
        /// Which positions get dropped is deliberately never previewed. Showing a
        /// specific subset that then re-rolls at placement would imply a promise
        /// this feature does not make.
        /// </summary>
        private static void TakeRandomSubset(PlannedJoint joint, int take)
        {
            List<PlannedBolt> pool = joint.Bolts.ToList();

            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }

            joint.Bolts.Clear();
            joint.Bolts.AddRange(pool.Take(take));
        }

        /// <summary>
        /// The run's report, whatever size the run was.
        ///
        /// One row per joint, so two elements give one row and twelve give
        /// however many joints were found.
        ///
        /// <paramref name="takePerJoint"/> is the count the user chose, or null
        /// when everything found was placed. It only decides whether the
        /// random-subset footnote appears.
        ///
        /// <paramref name="maxPerJointBefore"/> and <paramref name="maxTotalBefore"/>
        /// are the plan's maxima as they stood BEFORE any subsetting. They are
        /// passed in rather than read off the plan because the caller that
        /// subsets has already shrunk joint.Bolts by the time it gets here, and
        /// the footnote is about what was possible, not what was kept.
        ///
        /// Shown, not ShowDialog'd — its Select actions drive Revit's live
        /// selection, which is useless behind a modal.
        /// </summary>
        private static void ShowRunReport(
            UIDocument uiDoc,
            Document doc,
            MG2Shared.SelectionPlan plan,
            int placed,
            int? takePerJoint,
            int maxPerJointBefore,
            int maxTotalBefore)
        {
            MG2Shared.JointStats stats = plan.Stats;
            MG2Shared.BoltLedger ledger = plan.Ledger;

            // The two skip counts mean different things, so they stay apart.
            // Neither is mentioned when it did not happen.
            var notes = new List<string>();

            if (ledger.DuplicateInRun > 0) notes.Add($"{ledger.DuplicateInRun} duplicate skipped");
            if (ledger.AlreadyInDocument > 0) notes.Add($"{ledger.AlreadyInDocument} already placed");

            // Everything below is mentioned only when it actually happened.
            var footnotes = new List<string>();

            if (takePerJoint is int take && take < maxPerJointBefore)
            {
                footnotes.Add(
                    $"Up to {take} per joint of a possible {maxTotalBefore} position(s), " +
                    "chosen at random. Running again picks a different set.");
            }

            // Bolts placed, joints, elements and pairs are tiles. The two middle
            // funnel stages have no tile, so they stay here — but only when they
            // say something, which is when not every pair made it through. Where
            // everything touched and aligned, these just repeat the tiles.
            if (plan.TouchingPairs != plan.PairsConsidered
                || plan.PairsWithAlignedVoids != plan.TouchingPairs)
            {
                footnotes.Add(
                    $"{plan.TouchingPairs} of {plan.PairsConsidered} pair(s) touching, " +
                    $"{plan.PairsWithAlignedVoids} with aligned voids");
            }

            if (plan.PairsBoxRejected > 0)
            {
                footnotes.Add($"{plan.PairsBoxRejected} pair(s) were nowhere near each other.");
            }

            if (stats.NearMisses > 0)
            {
                footnotes.Add($"{stats.NearMisses} opening pair(s) overlapped but fell short.");
            }

            if (stats.SkippedBothRound > 0)
            {
                footnotes.Add(
                    $"{stats.SkippedBothRound} aligned pair(s) skipped — round openings on both " +
                    "sides, nothing to lock a head across.");
            }

            if (stats.SkippedNoWidth > 0)
            {
                footnotes.Add(
                    $"{stats.SkippedNoWidth} aligned pair(s) skipped — opening width not measurable.");
            }

            if (stats.OtherShapeRolls > 0)
            {
                footnotes.Add(
                    $"{stats.OtherShapeRolls} bolt(s) took their roll from a non-slot opening " +
                    "(square, hex or similar), which is treated as lockable.");
            }

            if (stats.FallbackSeatJoints > 0)
            {
                footnotes.Add(
                    $"{stats.FallbackSeatJoints} joint(s) seated at the " +
                    $"{MG2Shared.FALLBACK_SEAT_OFFSET_MM:0.##} mm fallback because no wall " +
                    "could be measured — check those by eye.");
            }

            // Goes to the footer caption rather than a tile — see BuildFooter.
            string? skipNote = notes.Count > 0 ? string.Join(", ", notes) : null;

            var rows = plan.Joints.Select(joint => new JointReportRow
            {
                ElementA = joint.FirstLabel,
                ElementB = joint.SecondLabel,
                BoltsPlaced = joint.PlacedBoltIds.Count.ToString(),
                SeatOffset = $"{MG2Shared.Mm(joint.SeatOffset):0.##} mm" +
                             (joint.SeatWasMeasured ? string.Empty : "  (fallback)"),
                Gap = $"{MG2Shared.Mm(joint.Gap):0.###} mm",
                BoltIds = joint.PlacedBoltIds,
                SeatMm = MG2Shared.Mm(joint.SeatOffset),
                SeatMeasured = joint.SeatWasMeasured,
                GapMm = MG2Shared.Mm(joint.Gap),
            }).ToList();

            new BoltRunReportWindow(
                uiDoc,
                rows,
                placed,
                plan.ElementCount,
                plan.PairsConsidered,
                footnotes,
                skipNote,
                doc.Title).Show();
        }

        /// <summary>
        /// Says why a selection produced no joints, and returns the Result that
        /// goes with it.
        ///
        /// Branches on MG2Shared.VerdictOf, which is the one place that decides
        /// which case a plan is in. The window reads the same verdict for its
        /// status line and words it for a line of text rather than an alert — one
        /// classification, two presentations, so the two surfaces cannot disagree
        /// about what happened.
        /// </summary>
        private static Result ExplainEmptyPlan(MG2Shared.SelectionPlan plan)
        {
            MG2Shared.JointStats stats = plan.Stats;

            switch (MG2Shared.VerdictOf(plan))
            {
                case MG2Shared.PlanVerdict.NoGeometry:
                    AvControls.ShowAlert(
                        MG2Shared.DIALOG_TITLE,
                        "No usable solid geometry was found on any of the selected elements.\n\n" +
                        "Check that they are visible and fully loaded. Note that a face smaller " +
                        $"than {MG2Shared.MIN_CANDIDATE_FACE_AREA_MM2:0} mm² is never considered, " +
                        "so a very small part contributes nothing.",
                        AlertSeverity.Error);
                    return Result.Failed;

                case MG2Shared.PlanVerdict.AllTaken:
                    // Every position found was already taken. Routine when
                    // re-running over ground already covered, so this reports
                    // rather than complains.
                    AvControls.ShowAlert(
                        MG2Shared.DIALOG_TITLE,
                        "No new bolts to place — every position found is already bolted.\n\n" +
                        $"{plan.Ledger.AlreadyInDocument} already in the document" +
                        (plan.Ledger.DuplicateInRun > 0
                            ? $", {plan.Ledger.DuplicateInRun} duplicate position(s) within this selection."
                            : "."),
                        AlertSeverity.Info);
                    return Result.Succeeded;

                case MG2Shared.PlanVerdict.NothingTouching:
                    AvControls.ShowAlert(
                        MG2Shared.DIALOG_TITLE,
                        $"None of the {plan.ElementCount} selected elements touch each other.\n\n" +
                        $"{plan.PairsConsidered} pair(s) considered, none with a pair of opposed " +
                        $"faces within {MG2Shared.GAP_TOLERANCE_MM:0.##} mm.\n\n" +
                        "This command expects elements meeting back to back, face against face.",
                        AlertSeverity.Warning);
                    return Result.Failed;

                default:
                    AvControls.ShowAlert(
                        MG2Shared.DIALOG_TITLE,
                        $"{plan.TouchingPairs} joint(s) were found, but none produced a bolt.\n\n" +
                        (stats.NearMisses > 0
                            ? $"{stats.NearMisses} opening pair(s) overlapped slightly but fell below the " +
                              $"{MG2Shared.MIN_OPENING_OVERLAP_MM2:0.##} mm² minimum, which usually " +
                              "means the parts are offset by part of a slot.\n"
                            : "No pair of openings overlapped at all.\n") +
                        (stats.SkippedBothRound > 0
                            ? $"{stats.SkippedBothRound} aligned pair(s) had round openings on both " +
                              "sides, so there was no slot for a head to lock across.\n"
                            : string.Empty) +
                        (stats.SkippedNoWidth > 0
                            ? $"{stats.SkippedNoWidth} aligned pair(s) had an opening whose width could " +
                              "not be measured.\n"
                            : string.Empty),
                        AlertSeverity.Warning);
                    return Result.Failed;
            }
        }

        /// <summary>
        /// Places every bolt of every planned joint in ONE transaction, filling
        /// each joint's PlacedBoltIds as it goes.
        ///
        /// Its own method rather than inline in the caller, for the same reason
        /// TryPlanJoint and PlanSelection are: a transaction, a rollback and a
        /// failure log are precisely the shape of thing that drifts once there
        /// are two copies of it.
        ///
        /// Takes a list because one joint and twelve are the same transaction. A
        /// failure rolls back the whole run rather than the joint that threw, so
        /// a geometry problem never leaves half a frame bolted.
        ///
        /// Returns false having ALREADY told the user why and written the log; the
        /// document is untouched in that case, because the rollback takes the
        /// whole run rather than the joint that failed.
        ///
        /// Must be called from a valid Revit API context. That is what the
        /// external event exists to provide.
        /// </summary>
        internal static bool TryPlaceJoints(
            Document doc,
            MG2Shared.BoltChoice choice,
            List<PlannedJoint> joints,
            out int placed)
        {
            FamilySymbol boltSymbol = choice.Symbol;

            placed = 0;

            using (var tx = new Transaction(doc, "Place Mekano Bolts"))
            {
                try
                {
                    tx.Start();

                    // Once for the run, not once per joint.
                    if (!boltSymbol.IsActive)
                    {
                        boltSymbol.Activate();
                        doc.Regenerate();
                    }

                    foreach (PlannedJoint joint in joints)
                    {
                        // One sketch plane per joint, since each joint has its
                        // own interface.
                        //
                        // They are left in the document on purpose. A
                        // work-plane-based instance is hosted on its work plane,
                        // so these are the bolts' hosts — deleting one would
                        // take its bolts with it. Keeping them is the
                        // requirement of the placement type, not an oversight;
                        // they carry no graphics and show in no view.
                        SketchPlane sketchPlane = SketchPlane.Create(
                            doc,
                            Plane.CreateByNormalAndOrigin(joint.PlaneNormal, joint.PlaneOrigin));

                        foreach (PlannedBolt bolt in joint.Bolts)
                        {
                            // The return value was being thrown away. Keeping
                            // it is the whole reason the report can select
                            // what it placed.
                            FamilyInstance created = doc.Create.NewFamilyInstance(
                                bolt.Point,
                                boltSymbol,
                                bolt.ReferenceDirection,
                                sketchPlane,
                                StructuralType.NonStructural);

                            joint.PlacedBoltIds.Add(created.Id);

                            placed++;
                        }
                    }

                    doc.Regenerate();
                    tx.Commit();
                }
                catch (Exception txEx)
                {
                    // Rolls back the whole run, not just the joint that failed.
                    if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();

                    // Covers symbol activation and NewFamilyInstance alike —
                    // both live inside this transaction, and a family Revit
                    // refuses to place surfaces here rather than at validation.
                    string log = LogBoltFailure(
                        doc, choice,
                        "Placement inside the transaction",
                        $"{placed} bolt(s) had been created before this threw. Symbol " +
                        "activation and NewFamilyInstance both run here, so a family that " +
                        "passes validation can still be refused at this point.",
                        txEx);

                    AvControls.ShowAlert(
                        MG2Shared.DIALOG_TITLE,
                        $"The bolts could not be placed, so nothing was changed.\n\n" +
                        $"{txEx.Message}\n\n" +
                        $"Full detail:\n{log}",
                        AlertSeverity.Error);
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>One joint, as the report shows it.</summary>
    internal sealed class JointReportRow
    {
        /// <summary>
        /// The two elements, in their own columns.
        ///
        /// Two columns rather than one joined string on purpose: concatenated,
        /// the first name consumes the whole column and the second is clipped away
        /// entirely. Split, each clips independently, so the start of both is
        /// always visible.
        /// </summary>
        public string ElementA { get; set; } = string.Empty;
        public string ElementB { get; set; } = string.Empty;

        public string BoltsPlaced { get; set; } = string.Empty;

        /// <summary>
        /// The seat used, marked when it came from the fallback rather than a
        /// measurement. The same number means different things in those two cases,
        /// so the distinction is on the row rather than only in a footnote.
        /// </summary>
        public string SeatOffset { get; set; } = string.Empty;

        public string Gap { get; set; } = string.Empty;

        /// <summary>Not displayed. What the Select action acts on.</summary>
        public List<ElementId> BoltIds { get; set; } = new();

        // ── For the clipboard, not the grid ───────────────────────────────────
        //
        // The displayed strings carry their unit and a "(fallback)" marker, which
        // reads well in a table and pastes badly into a spreadsheet. The other
        // report windows put the unit in the header and the bare number in the
        // cell, so these keep the raw values for that.

        public double SeatMm { get; set; }
        public bool SeatMeasured { get; set; }
        public double GapMm { get; set; }
    }

    /// <summary>
    /// End-of-run report for Add Bolts: a row per joint, the run's totals, and a
    /// way back to the bolts that were placed.
    ///
    /// Shown NON-MODALLY, following TagReportWindow in Cmds_Button11.cs, and for
    /// the same reason it gives: a modal dialog would cover the view it is asking
    /// the user to look at, and selection would be pointless until it closed.
    ///
    /// That works because selecting and zooming change the UI, not the model, so
    /// they need neither a transaction nor a Revit API context. Anything here that
    /// modified the document — deleting a bolt from the report, say — would need
    /// an ExternalEvent and could not simply copy this pattern.
    ///
    /// Replaces ONLY the end-of-run success summary. Every early-exit guard stays
    /// a ShowAlert: those are refusals with nothing to tabulate, and a table with
    /// no rows reads worse than a sentence.
    /// </summary>
    internal sealed class BoltRunReportWindow : WpfWindow
    {
        private readonly UIDocument _uiDoc;
        private readonly List<JointReportRow> _rows;

        internal BoltRunReportWindow(
            UIDocument uiDoc,
            List<JointReportRow> rows,
            int boltsPlaced,
            int elementsSelected,
            int pairsConsidered,


            List<string> footnotes,
            string? skipNote,
            string docTitle)
        {
            _uiDoc = uiDoc;
            _rows = rows;

            Title = MG2Shared.DIALOG_TITLE;

            // Matching TagReportWindow, the nearest analogue in the plugin — a
            // report with a table and per-row selection. It is the widest of the
            // report cluster because it is the one carrying an action column.
            Width = 900;
            Height = 620;
            MinWidth = 680;
            MinHeight = 420;
            ResizeMode = System.Windows.ResizeMode.CanResize;
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
            FontFamily = AvTokens.Ui;
            Background = AvTokens.B(AvTokens.Surface);
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var root = new WpfGrid();
            root.RowDefinitions.Add(new WpfRowDef { Height = WpfGridLength.Auto });
            root.RowDefinitions.Add(new WpfRowDef { Height = new WpfGridLength(1, WpfGridUnitType.Star) });
            root.RowDefinitions.Add(new WpfRowDef { Height = WpfGridLength.Auto });
            Content = root;

            var header = AvControls.ReportHeaderBar(MG2Shared.DIALOG_TITLE);
            WpfGrid.SetRow(header, 0);
            root.Children.Add(header);

            var body = new WpfStackPanel { Margin = new WpfThickness(20) };

            var bodyScroll = new WpfScrollViewer
            {
                VerticalScrollBarVisibility = WpfScrollBarVis.Auto,
                HorizontalScrollBarVisibility = WpfScrollBarVis.Disabled,
                Content = body,
            };

            WpfGrid.SetRow(bodyScroll, 1);
            root.Children.Add(bodyScroll);

            body.Children.Add(BuildStatTiles(
                boltsPlaced, rows.Count, elementsSelected, pairsConsidered));

            WpfUIElement? footnoteLine = BuildFootnoteLine(footnotes);
            if (footnoteLine is not null) body.Children.Add(footnoteLine);

            body.Children.Add(BuildTable());

            root.Children.Add(BuildFooter(docTitle, skipNote));

            return;
        }

        // ── Summary ───────────────────────────────────────────────────────────

        /// <summary>
        /// Four tiles across.
        ///
        /// The column count comes from the array below rather than a constant, so
        /// adding or removing a tile cannot leave an empty slot behind.
        ///
        /// Width is comfortable here. At the window's 680px minimum four tiles get
        /// roughly 147px each, leaving about 117px of label against a longest
        /// label of eight characters — some 46px. That was worth checking rather
        /// than assuming, because at six tiles the same sum gave only 64px and two
        /// labels did not fit.
        /// </summary>
        private static WpfGrid BuildStatTiles(
            int boltsPlaced,
            int joints,
            int elementsSelected,
            int pairsConsidered)
        {
            var tiles = new WpfGrid { Margin = new WpfThickness(0, 0, 0, 16) };

            var stats = new (string Value, string Label, string Accent)[]
            {
                (boltsPlaced.ToString(), "Bolts", boltsPlaced > 0 ? "ok" : ""),
                (joints.ToString(), "Joints", ""),
                (elementsSelected.ToString(), "Elements", ""),
                (pairsConsidered.ToString(), "Pairs", ""),
            };

            foreach (var _ in stats)
            {
                tiles.ColumnDefinitions.Add(
                    new WpfColDef { Width = new WpfGridLength(1, WpfGridUnitType.Star) });
            }

            for (int i = 0; i < stats.Length; i++)
            {
                WpfBorder tile = AvControls.StatTile(stats[i].Value, stats[i].Label, stats[i].Accent);

                tile.Margin = new WpfThickness(
                    i == 0 ? 0 : 6, 0, i == stats.Length - 1 ? 0 : 6, 0);

                WpfGrid.SetColumn(tile, i);
                tiles.Children.Add(tile);
            }

            return tiles;
        }

        /// <summary>
        /// The footnotes condensed to one line, or nothing at all.
        ///
        /// Returns null when there is nothing to say, and the caller adds no
        /// element — the space goes back to the table, which was the point of
        /// shrinking this. An empty strip where a paragraph used to be would have
        /// missed it.
        ///
        /// The tiles carry the counts, and per-joint detail is in the table, so
        /// what is left here is only what belongs to the run as a whole and has
        /// nowhere else to live.
        /// </summary>
        private static WpfUIElement? BuildFootnoteLine(List<string> footnotes)
        {
            if (footnotes.Count == 0) return null;

            return new WpfTextBlock
            {
                Text = string.Join("   ·   ", footnotes),
                FontFamily = AvTokens.Ui,
                FontSize = 11.5,
                Foreground = AvTokens.B(AvTokens.TextDim),
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin = new WpfThickness(0, 0, 0, 14),
            };
        }

        // ── Table ─────────────────────────────────────────────────────────────

        private WpfUIElement BuildTable()
        {
            var frame = new WpfBorder
            {
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new WpfCornerRadius(5),
                ClipToBounds = true,
                Background = AvTokens.B(AvTokens.Surface),
            };

            var grid = new WpfDataGrid
            {
                ItemsSource = _rows,
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                IsReadOnly = true,
                HeadersVisibility = WpfDataGridHeaders.Column,
                GridLinesVisibility = WpfDataGridGridLines.Horizontal,
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
                MaxHeight = 400,
            };

            var headerStyle = new WpfStyle(typeof(WpfDataGridColumnHeader));
            headerStyle.Setters.Add(new WpfSetter(WpfDataGridColumnHeader.BackgroundProperty, AvTokens.B(AvTokens.Surface2)));
            headerStyle.Setters.Add(new WpfSetter(WpfDataGridColumnHeader.ForegroundProperty, AvTokens.B(AvTokens.TextFaint)));
            headerStyle.Setters.Add(new WpfSetter(WpfDataGridColumnHeader.FontFamilyProperty, AvTokens.Ui));
            headerStyle.Setters.Add(new WpfSetter(WpfDataGridColumnHeader.FontSizeProperty, 10.5));
            headerStyle.Setters.Add(new WpfSetter(WpfDataGridColumnHeader.FontWeightProperty, WpfFontWeights.Bold));
            headerStyle.Setters.Add(new WpfSetter(WpfDataGridColumnHeader.PaddingProperty, new WpfThickness(12, 9, 12, 9)));
            grid.ColumnHeaderStyle = headerStyle;

            var cellStyle = new WpfStyle(typeof(WpfDataGridCell));
            cellStyle.Setters.Add(new WpfSetter(WpfDataGridCell.BorderThicknessProperty, new WpfThickness(0)));
            cellStyle.Setters.Add(new WpfSetter(WpfDataGridCell.PaddingProperty, new WpfThickness(12, 8, 12, 8)));
            cellStyle.Setters.Add(new WpfSetter(WpfDataGridCell.BackgroundProperty, WpfBrushes.Transparent));
            cellStyle.Setters.Add(new WpfSetter(WpfDataGridCell.ForegroundProperty, AvTokens.B(AvTokens.Text)));
            cellStyle.Setters.Add(new WpfSetter(WpfDataGridCell.TemplateProperty, AvControls.DataGridCellTemplate()));
            grid.CellStyle = cellStyle;

            grid.Columns.Add(Text("ELEMENT A", "ElementA", 2.0, tooltip: true));
            grid.Columns.Add(Text("ELEMENT B", "ElementB", 2.0, tooltip: true));
            grid.Columns.Add(Text("BOLTS", "BoltsPlaced", 0.5));
            grid.Columns.Add(Text("SEAT OFFSET", "SeatOffset", 0.9));
            grid.Columns.Add(Text("GAP", "Gap", 0.7));

            var selectColumn = new WpfDataGridTemplateColumn
            {
                Header = string.Empty,
                Width = WpfDataGridLength.Auto,
                MinWidth = 80,
                CellTemplate = BuildSelectButtonTemplate(),
            };

            grid.Columns.Add(selectColumn);

            frame.Child = grid;
            return frame;
        }

        private static WpfDataGridTextColumn Text(
            string header, string path, double stars, bool tooltip = false)
        {
            var column = new WpfDataGridTextColumn
            {
                Header = header,
                Binding = new WpfBinding(path),
                Width = new WpfDataGridLength(stars, WpfDataGridLengthUnit.Star),
            };

            if (tooltip)
            {
                // Element names are long enough that a column can never be wide
                // enough for all of them, so truncation has to stay recoverable
                // rather than be designed away. Bound to the same path, so the
                // tooltip is the untruncated cell text by construction.
                var style = new WpfStyle(typeof(WpfTextBlock));
                style.Setters.Add(new WpfSetter(
                    WpfTextBlock.ToolTipProperty, new WpfBinding(path)));

                column.ElementStyle = style;
            }

            return column;
        }

        // ── Select actions ────────────────────────────────────────────────────

        private WpfDataTemplate BuildSelectButtonTemplate()
        {
            var template = new WpfDataTemplate();
            var factory = new WpfElementFactory(typeof(WpfButton));

            factory.SetValue(WpfButton.ContentProperty, "Select");
            factory.SetValue(WpfButton.FontFamilyProperty, AvTokens.Ui);
            factory.SetValue(WpfButton.FontSizeProperty, 11.0);
            factory.SetValue(WpfButton.FontWeightProperty, WpfFontWeights.SemiBold);
            factory.SetValue(WpfButton.ForegroundProperty, AvTokens.B(AvTokens.TextDim));
            factory.SetValue(WpfButton.BackgroundProperty, AvTokens.B(AvTokens.Surface2));
            // Vertical padding 3, not 5, and font 11 rather than 11.5.
            //
            // The clipping was never the font on its own: DataGridCellTemplate
            // binds the cell's Padding, which is 8 top and bottom, so a 40px row
            // leaves the content 24px. At 11.5 with 5+5 padding the button wanted
            // about 25 and lost its descenders. RowHeight and the cell padding are
            // the plugin's shared convention and are left alone; the button gives
            // up the pixels instead.
            factory.SetValue(WpfButton.PaddingProperty, new WpfThickness(8, 3, 10, 3));
            factory.SetValue(WpfButton.CursorProperty, WpfCursors.Hand);
            factory.SetValue(System.Windows.FrameworkElement.VerticalAlignmentProperty, WpfVAlign.Center);
            factory.SetValue(WpfButton.TemplateProperty, ChipTemplate());
            factory.AddHandler(WpfButton.ClickEvent, new WpfRoutedEventHandler(OnSelectRow));

            template.VisualTree = factory;
            return template;
        }

        /// <summary>
        /// Borderless rounded chip, matching the one TagReportWindow uses for the
        /// same purpose. No border on purpose: adding one makes the button taller
        /// than the pills beside it, which is what that window found by measuring.
        /// </summary>
        private static WpfControlTemplate ChipTemplate()
        {
            var template = new WpfControlTemplate(typeof(WpfButton));

            var border = new WpfElementFactory(typeof(WpfBorder));
            border.Name = "chip";
            border.SetValue(WpfBorder.CornerRadiusProperty, new WpfCornerRadius(6));
            border.SetValue(WpfBorder.BackgroundProperty, AvTokens.B(AvTokens.Surface2));

            var content = new WpfElementFactory(typeof(WpfContentPresenter));
            content.SetValue(WpfContentPresenter.HorizontalAlignmentProperty, WpfHAlign.Center);
            content.SetValue(WpfContentPresenter.VerticalAlignmentProperty, WpfVAlign.Center);
            content.SetBinding(
                WpfContentPresenter.MarginProperty,
                new WpfBinding("Padding")
                {
                    RelativeSource = new System.Windows.Data.RelativeSource(
                        System.Windows.Data.RelativeSourceMode.TemplatedParent),
                });

            border.AppendChild(content);
            template.VisualTree = border;

            var hover = new WpfTrigger
            {
                Property = WpfUIElement.IsMouseOverProperty,
                Value = true,
            };

            hover.Setters.Add(new WpfSetter(
                WpfBorder.BackgroundProperty, AvTokens.B(AvTokens.AccentTint), "chip"));

            template.Triggers.Add(hover);
            return template;
        }

        /// <summary>
        /// Zooms to and selects one joint's bolts.
        ///
        /// ShowElements as well as SetElementIds here, unlike the select-all
        /// below: one joint is a small, localised target, which is exactly the
        /// case zooming helps with.
        /// </summary>
        private void OnSelectRow(object sender, WpfRoutedEventArgs e)
        {
            if (sender is not WpfButton button) return;
            if (button.DataContext is not JointReportRow row) return;
            if (row.BoltIds.Count == 0) return;

            try
            {
                _uiDoc.ShowElements(row.BoltIds);
                _uiDoc.Selection.SetElementIds(row.BoltIds);
            }
            catch (Exception ex)
            {
                AvControls.ShowAlert(
                    MG2Shared.DIALOG_TITLE,
                    $"Could not select this joint's bolts.\n\n{ex.Message}",
                    AlertSeverity.Error);
            }
        }

        /// <summary>
        /// Selects every bolt the run placed, WITHOUT zooming.
        ///
        /// A run can span joints scattered across the whole model, and zooming to
        /// fit all of them usually frames nothing useful — it can also raise
        /// Revit's own "no good view" dialog on top of this window. Selecting lets
        /// the user take it from there.
        /// </summary>
        private void OnSelectAll()
        {
            var everything = _rows.SelectMany(r => r.BoltIds).ToList();

            if (everything.Count == 0) return;

            try
            {
                _uiDoc.Selection.SetElementIds(everything);
            }
            catch (Exception ex)
            {
                AvControls.ShowAlert(
                    MG2Shared.DIALOG_TITLE,
                    $"Could not select the placed bolts.\n\n{ex.Message}",
                    AlertSeverity.Error);
            }
        }

        /// <summary>
        /// Puts the joint table on the clipboard, tab separated with a header row
        /// — the format the plugin's other four copy buttons use, which pastes
        /// straight into a spreadsheet as columns.
        ///
        /// Units go in the header and the cells stay numeric, as Cmds_Button8 does
        /// with its weights: "3 mm" in a cell is text and will not sum or sort.
        /// The fallback marker becomes its own Yes/No column for the same reason —
        /// embedded in the number it would poison the whole column.
        ///
        /// Failure is silent: the clipboard can be locked by another process, and
        /// this is a convenience, not something worth an error dialog over.
        /// </summary>
        private void OnCopyToClipboard(WpfButton sourceButton)
        {
            var text = new StringBuilder();

            text.AppendLine(
                "Element A\tElement B\tBolts\tSeat offset (mm)\tSeat measured\tGap (mm)");

            foreach (JointReportRow row in _rows)
            {
                text.AppendLine(string.Join(
                    "\t",
                    row.ElementA,
                    row.ElementB,
                    row.BoltIds.Count.ToString(),
                    row.SeatMm.ToString("F2"),
                    row.SeatMeasured ? "Yes" : "No",
                    row.GapMm.ToString("F3")));
            }

            try
            {
                WpfClipboard.SetText(text.ToString());

                // Brief in-place confirmation, as the other copy buttons give.
                object original = sourceButton.Content;
                sourceButton.Content = "✓  Copied!";

                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1.4),
                };

                timer.Tick += (_, __) =>
                {
                    sourceButton.Content = original;
                    timer.Stop();
                };

                timer.Start();
            }
            catch
            {
                // Clipboard locked elsewhere. Nothing worth interrupting for.
            }
        }

        // ── Footer ────────────────────────────────────────────────────────────

        private WpfUIElement BuildFooter(string docTitle, string? skipNote)
        {
            var buttons = new WpfStackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = WpfHAlign.Right,
            };

            // Secondary actions then the primary, each secondary with a 10px right
            // margin so they do not run into one another or into Close.
            WpfButton copyButton = AvControls.SecondaryBtn("⧉  Copy to clipboard", () => { });
            copyButton.Margin = new WpfThickness(0, 0, 10, 0);
            copyButton.Click += (_, __) => OnCopyToClipboard(copyButton);
            buttons.Children.Add(copyButton);

            int total = _rows.Sum(r => r.BoltIds.Count);

            WpfButton selectAllButton = AvControls.SecondaryBtn(
                $"Select all {total} bolt(s)", OnSelectAll);
            selectAllButton.Margin = new WpfThickness(0, 0, 10, 0);
            buttons.Children.Add(selectAllButton);

            buttons.Children.Add(AvControls.PrimaryBtn("Close", Close));

            // LastChildFill with the buttons docked first, caption last, so a long
            // document name ellipses rather than pushing the buttons off.
            //
            // SetDock is NOT optional here: DockPanel defaults to Dock.Left, so
            // without it the buttons dock left and the caption fills to their
            // right — the whole footer mirrored. Both DockPanel footers in the
            // plugin set it; this one was missing the call while carrying the
            // comment describing it.
            var inner = new WpfDockPanel { LastChildFill = true };
            WpfDockPanel.SetDock(buttons, WpfDock.Right);
            inner.Children.Add(buttons);
            // The duplicate and already-placed counts ride here rather than taking
            // a tile: they are about what did NOT happen, and they stay written
            // out separately because the two mean different things — one says two
            // joints claimed the same hole, the other says this ground was already
            // covered. Absent entirely when neither occurred.
            string caption = skipNote is null
                ? $"Source · {docTitle}"
                : $"Source · {docTitle}  ·  {skipNote}";

            inner.Children.Add(AvControls.FooterCaption(caption));

            WpfBorder footer = AvControls.FooterStrip(inner);
            WpfGrid.SetRow(footer, 2);
            return footer;
        }
    }

    /// <summary>
    /// Picks one bolt from a filtered list, before anything is selected in the
    /// model.
    ///
    /// Structure copied from TagFamilyPickerWindow (Cmds_Pulldown4.cs) rather than
    /// invented: HeaderStrip for the title, nested DockPanels, the same search
    /// box, a plain single-select ListBox with the shared PickerListItemStyle, and
    /// a Cancel/Select footer where Select stays disabled until something is
    /// chosen. Double-click and Enter both confirm; Escape cancels.
    ///
    /// That window is the plugin's closest existing analogue — the other list
    /// pickers are multi-select with a checkbox per row, which is a different
    /// control and a different contract. Copying the single-select one means
    /// almost nothing had to be adapted.
    ///
    /// No departures from the template any more. It carried three for a while —
    /// 520x680, NoResize, CenterScreen — and all three existed only because this
    /// used to open straight from the ribbon with no parent window, where
    /// CenterOwner centres on nothing. It now opens from AddBoltsWindow, which
    /// sets itself as Owner, so the template's own 480x560 / CanResize /
    /// CenterOwner is simply correct and the exceptions are gone.
    ///
    /// Not built on SinglePickerWindow, which Add Bolts used to call. That stays
    /// exactly as it is for its four callers in the Shared Parameters windows.
    /// </summary>
    internal sealed class BoltPickerWindow : WpfWindow
    {
        internal string? SelectedLabel { get; private set; }

        private readonly List<string> _allLabels;
        private readonly string? _currentLabel;

        private WpfTextBox _searchBox = null!;
        private WpfListBox _listBox = null!;
        private WpfButton _selectButton = null!;

        private bool _initialSelectionApplied;

        internal BoltPickerWindow(string title, List<string> labels, string? currentLabel)
        {
            _allLabels = labels;
            _currentLabel = currentLabel;

            Title = title;

            Width = 480;
            Height = 560;
            WindowStyle = System.Windows.WindowStyle.ToolWindow;
            ResizeMode = System.Windows.ResizeMode.CanResize;
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;

            Background = AvTokens.B(AvTokens.Surface);
            FontFamily = AvTokens.Ui;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var root = new WpfDockPanel();

            // HeaderStrip, not TitleBar: the template's own comment records that
            // this is what the picker family uses.
            WpfBorder header = AvControls.HeaderStrip(title);
            WpfDockPanel.SetDock(header, WpfDock.Top);
            root.Children.Add(header);

            WpfUIElement footer = BuildFooter();
            WpfDockPanel.SetDock(footer, WpfDock.Bottom);
            root.Children.Add(footer);

            var body = new WpfDockPanel { Margin = new WpfThickness(20, 16, 20, 0) };

            WpfBorder search = BuildSearchBox();
            WpfDockPanel.SetDock(search, WpfDock.Top);
            body.Children.Add(search);

            _listBox = new WpfListBox
            {
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                BorderThickness = new WpfThickness(0),
                Background = WpfBrushes.Transparent,
                Focusable = true,
                ItemContainerStyle = AvControls.PickerListItemStyle(),
            };

            _listBox.SelectionChanged += (_, __) =>
                _selectButton.IsEnabled = _listBox.SelectedItem is not null;

            _listBox.MouseDoubleClick += (_, __) =>
            {
                if (_listBox.SelectedItem is not null) OnSelect();
            };

            var scroll = new WpfScrollViewer
            {
                VerticalScrollBarVisibility = WpfScrollBarVis.Auto,
                HorizontalScrollBarVisibility = WpfScrollBarVis.Disabled,
                Content = _listBox,
                Focusable = false,
            };

            scroll.PreviewMouseWheel += (_, e) =>
            {
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta * 0.5);
                e.Handled = true;
            };

            var listFrame = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new WpfCornerRadius(5),
                Margin = new WpfThickness(0, 10, 0, 16),
                Child = scroll,
            };

            body.Children.Add(listFrame);
            root.Children.Add(body);

            Content = root;

            RefreshList();

            Loaded += (_, __) => _searchBox.Focus();

            KeyDown += (_, e) =>
            {
                if (e.Key == WpfKey.Escape) DialogResult = false;
                if (e.Key == WpfKey.Enter && _selectButton.IsEnabled) OnSelect();
            };
        }

        private WpfBorder BuildSearchBox()
        {
            var grid = new WpfGrid();
            grid.ColumnDefinitions.Add(new WpfColDef { Width = new WpfGridLength(34) });
            grid.ColumnDefinitions.Add(
                new WpfColDef { Width = new WpfGridLength(1, WpfGridUnitType.Star) });

            var icon = new WpfTextBlock
            {
                Text = "⌕",
                FontSize = 15,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                HorizontalAlignment = WpfHAlign.Center,
                VerticalAlignment = WpfVAlign.Center,
            };

            WpfGrid.SetColumn(icon, 0);

            _searchBox = new WpfTextBox
            {
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                Foreground = AvTokens.B(AvTokens.Text),
                Background = WpfBrushes.Transparent,
                BorderThickness = new WpfThickness(0),
                VerticalAlignment = WpfVAlign.Center,
                Padding = new WpfThickness(0, 8, 10, 8),
                ToolTip = "Filter bolts by item number or description",
            };

            _searchBox.TextChanged += (_, __) => RefreshList();
            WpfGrid.SetColumn(_searchBox, 1);

            grid.Children.Add(icon);
            grid.Children.Add(_searchBox);

            return new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new WpfCornerRadius(5),
                Child = grid,
            };
        }

        private WpfUIElement BuildFooter()
        {
            var frame = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface2),
                BorderBrush = AvTokens.B(AvTokens.Border),
                BorderThickness = new WpfThickness(0, 1, 0, 0),
                Padding = new WpfThickness(20, 14, 20, 14),
            };

            var inner = new WpfDockPanel { LastChildFill = false };

            var buttons = new WpfStackPanel { Orientation = WpfOrientation.Horizontal };
            WpfDockPanel.SetDock(buttons, WpfDock.Right);

            WpfButton cancelButton = AvControls.GhostButton("Cancel");
            cancelButton.Margin = new WpfThickness(0, 0, 10, 0);
            cancelButton.Click += (_, __) => DialogResult = false;

            _selectButton = AvControls.PrimaryButton("Select");
            _selectButton.IsEnabled = false;
            _selectButton.Click += (_, __) => OnSelect();

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(_selectButton);

            inner.Children.Add(buttons);
            frame.Child = inner;

            return frame;
        }

        /// <summary>
        /// Applies the search filter, preserving the user's selection across
        /// keystrokes and applying the caller's starting choice exactly once — so
        /// the remembered bolt is selected on open but does not reassert itself
        /// every time the filter changes.
        /// </summary>
        private void RefreshList()
        {
            string query = (_searchBox.Text ?? string.Empty).Trim().ToLowerInvariant();
            string? previouslySelected = _listBox.SelectedItem as string;

            List<string> filtered = string.IsNullOrEmpty(query)
                ? _allLabels
                : _allLabels.Where(l => l.ToLowerInvariant().Contains(query)).ToList();

            _listBox.Items.Clear();
            foreach (string label in filtered) _listBox.Items.Add(label);

            string? toSelect = previouslySelected;

            if (!_initialSelectionApplied)
            {
                _initialSelectionApplied = true;
                toSelect = _currentLabel;
            }

            if (toSelect is not null && filtered.Contains(toSelect))
            {
                _listBox.SelectedItem = toSelect;
                _listBox.ScrollIntoView(toSelect);
            }

            _selectButton.IsEnabled = _listBox.SelectedItem is not null;
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

    /// <summary>
    /// The entry window for Add Bolts: a fixed snapshot of whatever was selected
    /// — one joint or twenty — with the bolt to use and, for a single joint, how
    /// many of its positions to fill.
    ///
    /// MODAL, and opened only after the elements have been picked. Both follow
    /// from the same decision and each pays for itself:
    ///
    /// The selection is settled before this exists, so there is nothing to track.
    /// An earlier build opened from the ribbon and followed the live selection,
    /// which forced it modeless, which forced an ExternalEvent to place from,
    /// which forced a captured-pair snapshot and a staleness check to stop the
    /// preview and the placement disagreeing. Picking first removes the whole
    /// chain rather than managing it.
    ///
    /// And because ShowDialog blocks inside Cmd_MG2.Execute, the command is still
    /// in its API context when this closes — so the placement happens there,
    /// directly, with no external event of any kind. Every transaction in this
    /// plugin lives in an IExternalCommand class; this one does too.
    ///
    /// Nothing here modifies the document. It reads geometry, shows numbers, and
    /// hands back two answers.
    ///
    /// Sized as one of the plugin's FORM windows — 560x640, the same as
    /// MainToolWindow in Cmds_Button10.cs — and not as one of its reports. It
    /// carries no table, and a table is what makes the report windows 700-980
    /// wide. See the constructor for the split.
    /// </summary>
    internal sealed class AddBoltsWindow : WpfWindow
    {
        /// <summary>The bolt the user settled on, or null if none was available.</summary>
        internal MG2Shared.BoltChoice? ChosenBolt => _choice;

        /// <summary>
        /// How many of the joint's positions to fill.
        ///
        /// Meaningful for a SINGLE joint only. Above one the control is greyed and
        /// PlaceSelection ignores this outright, every joint taking everything it
        /// holds — so a caller must not read this as a per-joint cap for a
        /// multi-joint run.
        /// </summary>
        internal int BoltsPerJoint => _boltsPerJoint;

        /// <summary>
        /// The value handed to PlanSelection for this preview's roll.
        ///
        /// The real placement reads it from TryMeasureBoltByPlacing, which opens a
        /// transaction — legal in the command, pointless here. It does not matter
        /// for a preview: the flag is consumed only by RollDirection, which sets a
        /// bolt's reference direction. Whether a point is found, matched, or
        /// accepted by the ledger does not depend on it, so the count shown is the
        /// count the placement plans either way.
        /// </summary>
        private const bool PREVIEW_WIDE_AXIS = true;

        private readonly Document _doc;

        private MG2Shared.BoltChoice? _choice;

        private readonly WpfButton _boltButton;
        private readonly WpfTextBlock _countValue;
        private readonly WpfTextBlock _countOfMax;
        private readonly WpfButton _minusButton;
        private readonly WpfButton _plusButton;
        private readonly WpfUIElement _stepperRow;
        private readonly WpfButton _placeButton;

        /// <summary>
        /// The cap on the stepper: the most any ONE joint can take.
        ///
        /// Not the total. With several joints the total is larger and appears on
        /// the Max bolts chip instead; this is the number above which raising the
        /// count would stop changing anything.
        /// </summary>
        private readonly int _maxBolts;

        /// <summary>How many joints the selection produced.</summary>
        private readonly int _jointCount;

        /// <summary>
        /// True above one joint, and then the count is not the user's to set.
        ///
        /// Choosing "up to N per joint" across joints of different sizes is a
        /// setting whose effect varies per joint and which nobody can picture, so
        /// the multi-joint run does the one thing that is predictable: every joint
        /// takes everything it holds. The control is greyed rather than hidden so
        /// the window keeps its shape and the figure stays readable.
        ///
        /// PlaceSelection enforces the same rule independently. This field only
        /// decides what the window looks like; it is not what makes the rule true.
        /// </summary>
        private readonly bool _countLocked;

        private int _boltsPerJoint;

        internal AddBoltsWindow(UIDocument uiDoc, List<Element> selection)
        {
            _doc = uiDoc.Document;

            // ── The selection, planned once ────────────────────────────────────
            //
            // Once is the whole design. Nothing below can change what was
            // selected, so nothing below needs to ask again.
            //
            // The same call the placement makes, so the numbers on screen are the
            // numbers that get placed — including the single ledger shared across
            // every pair, which is what stops two joints both claiming one hole
            // and the preview counting it twice.
            MG2Shared.SelectionPlan plan =
                MG2Shared.PlanSelection(_doc, selection, PREVIEW_WIDE_AXIS);

            _maxBolts = plan.MaxBoltsPerJoint;
            _jointCount = plan.Joints.Count;
            _countLocked = _jointCount > 1;

            // Every position, by default. That is what this command has always
            // done, so the default run is the familiar one and stepping down is
            // the deliberate act.
            _boltsPerJoint = Math.Max(1, _maxBolts);

            // ── Chrome ─────────────────────────────────────────────────────────

            Title = MG2Shared.DIALOG_TITLE;

            // The FORM class, not the report class. The plugin's windows fall into
            // two groups and the split is content, not taste: the 700-980 wide
            // ones all carry a DataGrid, and their width is however many columns
            // it has. The 380-560 ones are pickers and entry forms.
            //
            // This is a form — a joint card, two fields and a footer, no table — so
            // it takes the widest form size rather than the narrowest report one:
            // MainToolWindow's 560x640 / 460x420 (Cmds_Button10.cs), the only
            // other multi-field entry form in the plugin.
            //
            // It is now visibly narrower than BoltRunReportWindow, which follows
            // it at 900. That is correct: the report has a five-column table to
            // show and this does not.
            Width = 560;
            Height = 640;
            MinWidth = 460;
            MinHeight = 420;
            ResizeMode = System.Windows.ResizeMode.CanResize;
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

            Background = AvTokens.B(AvTokens.Surface);
            FontFamily = AvTokens.Ui;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var root = new WpfGrid();
            root.RowDefinitions.Add(new WpfRowDef { Height = WpfGridLength.Auto });
            root.RowDefinitions.Add(new WpfRowDef { Height = new WpfGridLength(1, WpfGridUnitType.Star) });
            root.RowDefinitions.Add(new WpfRowDef { Height = WpfGridLength.Auto });
            Content = root;

            // ReportHeaderBar rather than the TitleBar the other form windows use.
            // The size class moved; this did not. It carries the run's result
            // tiles and hands straight over to BoltRunReportWindow, so it reads as
            // the front of that pair rather than as another picker.
            WpfBorder header = AvControls.ReportHeaderBar(MG2Shared.DIALOG_TITLE);
            WpfGrid.SetRow(header, 0);
            root.Children.Add(header);

            _placeButton = AvControls.PrimaryButton("Place bolts");
            _placeButton.IsEnabled = false;
            _placeButton.Click += (_, __) => OnPlace();

            WpfUIElement footer = BuildFooter();
            WpfGrid.SetRow(footer, 2);
            root.Children.Add(footer);

            var body = new WpfStackPanel { Margin = new WpfThickness(20) };

            var bodyScroll = new WpfScrollViewer
            {
                VerticalScrollBarVisibility = WpfScrollBarVis.Auto,
                HorizontalScrollBarVisibility = WpfScrollBarVis.Disabled,
                Content = body,
            };

            WpfGrid.SetRow(bodyScroll, 1);
            root.Children.Add(bodyScroll);

            body.Children.Add(BuildJointCard(plan, selection));

            // ── Section 1 ──────────────────────────────────────────────────────
            body.Children.Add(AvControls.FieldLabel(1, "Bolt"));

            _boltButton = AvControls.PickerButton("—");
            _boltButton.Click += (_, __) => OnPickBolt();
            body.Children.Add(_boltButton);

            WpfBorder divider = AvControls.HDivider();
            divider.Margin = new WpfThickness(0, 20, 0, 20);
            body.Children.Add(divider);

            // ── Section 2 ──────────────────────────────────────────────────────
            body.Children.Add(AvControls.FieldLabel(2, "Bolts per joint"));

            _countValue = new WpfTextBlock
            {
                FontFamily = AvTokens.Mono,
                FontSize = 17,
                FontWeight = WpfFontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.Text),
                MinWidth = 46,
                TextAlignment = System.Windows.TextAlignment.Center,
                VerticalAlignment = WpfVAlign.Center,
            };

            _countOfMax = new WpfTextBlock
            {
                FontFamily = AvTokens.Ui,
                FontSize = 12.5,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                VerticalAlignment = WpfVAlign.Center,
                Margin = new WpfThickness(12, 0, 0, 0),
            };

            _minusButton = StepButton("−", -1);
            _plusButton = StepButton("+", 1);

            _stepperRow = BuildStepperRow();
            body.Children.Add(_stepperRow);

            WpfTextBlock? status = BuildStatusLine(plan);
            if (status is not null) body.Children.Add(status);

            // ── Fill in ────────────────────────────────────────────────────────

            LoadBolt();

            if (_maxBolts > 0)
            {
                ShowCount();

                // Same greying as the no-joint state below, because both mean the
                // same thing to the user: this control is not yours to set right
                // now. The plugin has no shared disabled styling to borrow — none
                // of AvControls' button templates carries an IsEnabled trigger, so
                // IsEnabled alone stops the click and changes nothing visually —
                // and this opacity is the window's own existing answer to that.
                if (_countLocked) _stepperRow.Opacity = 0.45;
            }
            else
            {
                _stepperRow.Opacity = 0.45;
                _countValue.Text = "—";
                _countOfMax.Text = string.Empty;
            }

            KeyDown += (_, e) =>
            {
                if (e.Key == WpfKey.Escape) DialogResult = false;
            };
        }

        // ── Chrome ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The joint, as a headline naming both elements with a row of metric
        /// chips under it.
        ///
        /// ── Why this is not AvControls.StatTile ────────────────────────────────
        ///
        /// StatTile has 29 call sites across 12 files and is not touched. This is
        /// a different control for a different problem: StatTile is a fixed-width
        /// column holding ONE short value, and the thing this window most needs to
        /// show is two family type names, which are long, of unpredictable length,
        /// and would be ellipsised into uselessness by any tile that had to share
        /// a row with three others.
        ///
        /// So the hierarchy is inverted. The names become the headline and wrap as
        /// ordinary prose — no width to run out of, whatever they are called — and
        /// the numbers drop to chips beneath, which is the reverse of StatTile's
        /// big-value-small-label arrangement and reads as a different thing at a
        /// glance. Vertically it costs about 70px against the tile row's 140.
        ///
        /// Private to this window, and not promoted to AvControls, because it has
        /// exactly one caller and is shaped around this window's data. Putting it
        /// in the shared file would advertise a reuse that is not coming.
        ///
        /// Every colour, font, radius and weight below is an AvTokens value or
        /// lifted from an existing AvControls component — SectionLabel's 11/Bold/
        /// uppercase/TextFaint for the caption, StatusPill's radius 6 for the
        /// chips. Nothing here is a new design value.
        ///
        /// TWO chips, not three. "Elements: 2" was dropped earlier for being
        /// constant by construction, and the headline now names both of them
        /// anyway, so a count beside it would be saying the same thing twice.
        ///
        /// Seat is not here either. Its one piece of real signal — that the seat
        /// fell back because no wall could be measured — is said in words on the
        /// status line, and it stays there alone. Tinting Gap to carry it would
        /// make Gap mean something it does not.
        /// </summary>
        private WpfStackPanel BuildJointCard(MG2Shared.SelectionPlan plan, List<Element> selection)
        {
            var card = new WpfStackPanel { Margin = new WpfThickness(0, 0, 0, 20) };

            WpfTextBlock caption = AvControls.SectionLabel("Joint");
            caption.Margin = new WpfThickness(0, 0, 0, 6);
            card.Children.Add(caption);

            // One TextBlock with Runs rather than several side by side. Runs share
            // a baseline for free, and — the reason that matters here — the whole
            // thing wraps as a single paragraph, so a long type name takes a
            // second line instead of being cut off.
            var headline = new WpfTextBlock
            {
                FontFamily = AvTokens.Ui,
                FontSize = 14,
                FontWeight = WpfFontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.Text),
                TextWrapping = System.Windows.TextWrapping.Wrap,
                LineHeight = 21,
            };

            if (selection.Count == 2)
            {
                // One pair, so name both ends. This is the case where the arrow
                // means something: it points the way the shank goes.
                headline.Inlines.Add(new WpfRun(MG2Shared.LabelOf(selection[0])));
                headline.Inlines.Add(new WpfRun("  →  ")
                {
                    Foreground = AvTokens.B(AvTokens.Accent),
                });
                headline.Inlines.Add(new WpfRun(MG2Shared.LabelOf(selection[1])));
            }
            else
            {
                // More than one pair. Naming them all would be a list, not a
                // headline, and forcing two names into an A → B shape would be a
                // lie about which two. Counts instead — and the caption above
                // already says these are joints.
                headline.Inlines.Add(new WpfRun(plan.Joints.Count.ToString())
                {
                    Foreground = AvTokens.B(AvTokens.Accent),
                });
                headline.Inlines.Add(new WpfRun(
                    plan.Joints.Count == 1 ? " joint across " : " joints across "));
                headline.Inlines.Add(new WpfRun(plan.ElementCount.ToString())
                {
                    Foreground = AvTokens.B(AvTokens.Accent),
                });
                headline.Inlines.Add(new WpfRun(" elements"));
            }

            card.Children.Add(headline);

            // A WrapPanel, so the chips drop to a second line rather than being
            // clipped when the window is dragged to its 460px minimum.
            var chips = new WpfWrapPanel { Margin = new WpfThickness(0, 13, 0, 0) };

            // The TOTAL, not the per-joint cap the stepper uses. What gets placed
            // if the count is left alone.
            int total = plan.MaxBoltsTotal;

            chips.Children.Add(MetricChip(
                "Max bolts",
                total > 0 ? total.ToString() : "—",
                total > 0 ? AvTokens.Accent : AvTokens.FailFg));

            // One joint has a gap. Several do not share one, so the worst is
            // reported instead — a mean would bury exactly the joint worth
            // looking at before committing.
            chips.Children.Add(MetricChip(
                plan.Joints.Count > 1 ? "Widest gap" : "Gap",
                plan.Joints.Count > 0 ? $"{MG2Shared.Mm(plan.WidestGap):0.###} mm" : "—",
                AvTokens.Text));

            card.Children.Add(chips);

            return card;
        }

        /// <summary>
        /// One chip: a faint label and its value on a single baseline, in a
        /// rounded Zebra pill.
        ///
        /// Radius 6 and the label's 11/SemiBold are StatusPill's, so the shape is
        /// one the plugin already uses rather than a new one.
        /// </summary>
        private static WpfBorder MetricChip(string label, string value, string valueColor)
        {
            var text = new WpfTextBlock();

            text.Inlines.Add(new WpfRun(label + "  ")
            {
                FontFamily = AvTokens.Ui,
                FontSize = 11,
                FontWeight = WpfFontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.TextFaint),
            });

            text.Inlines.Add(new WpfRun(value)
            {
                FontFamily = AvTokens.Mono,
                FontSize = 15,
                FontWeight = WpfFontWeights.Bold,
                Foreground = AvTokens.B(valueColor),
            });

            return new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Zebra),
                CornerRadius = new WpfCornerRadius(6),
                Padding = new WpfThickness(12, 6, 12, 6),
                Margin = new WpfThickness(0, 0, 8, 8),
                Child = text,
            };
        }

        /// <summary>
        /// The line under the stepper, or NULL when there is nothing to say.
        ///
        /// It carries exactly what nothing else on screen carries:
        ///
        ///   - why there is no joint, when there is not one. The chips show a dash
        ///     and a dash is not a reason.
        ///   - THE FALLBACK-SEAT WARNING. When a joint's seat could not be
        ///     measured from a real wall it sits at the 3 mm default instead, and
        ///     that is worth checking by eye. This line is the only place it is
        ///     said. It used to have a Seat tile; when that tile was dropped the
        ///     signal was deliberately moved HERE rather than lost, and colouring
        ///     another chip to carry it was explicitly ruled out. Do not delete
        ///     this branch without giving the warning somewhere else to live.
        ///   - how many positions are already bolted, which changes what the
        ///     count means.
        ///
        /// It no longer repeats "{A} → {B}". The headline says that now, and
        /// saying it twice is what made this line look like filler.
        ///
        /// Returns null when a joint is ready and neither of the last two applies
        /// — so a clean run leaves the space under the stepper empty instead of
        /// filling it with a sentence that says nothing.
        ///
        /// Branches on MG2Shared.VerdictOf, the same classification the command's
        /// alerts use. One decision about which case we are in, two wordings.
        /// </summary>
        private WpfTextBlock? BuildStatusLine(MG2Shared.SelectionPlan plan)
        {
            MG2Shared.JointStats stats = plan.Stats;
            bool pair = plan.ElementCount == 2;

            string text;
            bool trouble = true;

            switch (MG2Shared.VerdictOf(plan))
            {
                case MG2Shared.PlanVerdict.NoGeometry:
                    text = pair
                        ? "One of the two has no usable solid geometry."
                        : "None of the selected elements has usable solid geometry.";
                    break;

                case MG2Shared.PlanVerdict.NothingTouching:
                    text = pair
                        ? "These two do not meet face to face within " +
                          $"{MG2Shared.GAP_TOLERANCE_MM:0.##} mm."
                        : $"None of the {plan.ElementCount} elements meet face to face within " +
                          $"{MG2Shared.GAP_TOLERANCE_MM:0.##} mm — {plan.PairsConsidered} pair(s) " +
                          "considered.";
                    break;

                case MG2Shared.PlanVerdict.AllTaken:
                    text = "Every position found is already bolted.";
                    break;

                case MG2Shared.PlanVerdict.NoBolts:
                    text = stats.NearMisses > 0
                        ? $"{plan.TouchingPairs} joint(s) touch, but no openings line up — " +
                          $"{stats.NearMisses} overlapped slightly and fell short, which usually " +
                          "means they are offset by part of a slot."
                        : $"{plan.TouchingPairs} joint(s) touch, but none of their openings line up.";
                    break;

                default:
                    trouble = false;

                    var parts = new List<string>();

                    // The protected signal. Worded for one joint or for several,
                    // but never omitted when it applies.
                    if (stats.FallbackSeatJoints > 0)
                    {
                        parts.Add(plan.Joints.Count == 1
                            ? $"Seated at the {MG2Shared.FALLBACK_SEAT_OFFSET_MM:0.##} mm fallback " +
                              "because no wall could be measured — check this one by eye."
                            : $"{stats.FallbackSeatJoints} joint(s) seated at the " +
                              $"{MG2Shared.FALLBACK_SEAT_OFFSET_MM:0.##} mm fallback because no wall " +
                              "could be measured — check those by eye.");

                        trouble = true;
                    }

                    if (plan.Ledger.AlreadyInDocument > 0)
                    {
                        parts.Add($"{plan.Ledger.AlreadyInDocument} position(s) here are already bolted.");
                    }

                    // Nothing worth saying. The chips and the headline have it
                    // covered, so the line is omitted rather than padded.
                    if (parts.Count == 0) return null;

                    text = string.Join("  ", parts);
                    break;
            }

            return new WpfTextBlock
            {
                Text = text,
                FontFamily = AvTokens.Ui,
                FontSize = 12.5,
                Foreground = AvTokens.B(trouble ? AvTokens.WarnFg : AvTokens.TextDim),
                TextWrapping = System.Windows.TextWrapping.Wrap,
                Margin = new WpfThickness(0, 14, 0, 0),
            };
        }

        private WpfButton StepButton(string glyph, int delta)
        {
            WpfButton button = AvControls.GhostButton(glyph);

            button.Width = 36;
            button.Height = 36;
            button.Padding = new WpfThickness(0);
            button.FontSize = 16;
            button.IsEnabled = false;

            button.Click += (_, __) => Step(delta);

            return button;
        }

        private WpfUIElement BuildStepperRow()
        {
            var row = new WpfStackPanel { Orientation = WpfOrientation.Horizontal };

            var frame = new WpfBorder
            {
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                BorderThickness = new WpfThickness(1),
                CornerRadius = new WpfCornerRadius(5),
                Padding = new WpfThickness(2),
            };

            var inner = new WpfStackPanel { Orientation = WpfOrientation.Horizontal };
            inner.Children.Add(_minusButton);
            inner.Children.Add(_countValue);
            inner.Children.Add(_plusButton);

            frame.Child = inner;

            row.Children.Add(frame);
            row.Children.Add(_countOfMax);

            return row;
        }

        private WpfUIElement BuildFooter()
        {
            // LastChildFill off and the stack docked Right: a DockPanel defaults
            // to Dock.Left, which puts the buttons on the wrong side.
            var inner = new WpfDockPanel { LastChildFill = false };

            var buttons = new WpfStackPanel { Orientation = WpfOrientation.Horizontal };
            WpfDockPanel.SetDock(buttons, WpfDock.Right);

            WpfButton cancelButton = AvControls.GhostButton("Cancel");
            cancelButton.Margin = new WpfThickness(0, 0, 10, 0);
            cancelButton.Click += (_, __) => DialogResult = false;

            buttons.Children.Add(cancelButton);
            buttons.Children.Add(_placeButton);

            inner.Children.Add(buttons);

            return AvControls.FooterStrip(inner);
        }

        // ── Section 1 ──────────────────────────────────────────────────────────

        /// <summary>
        /// Shows whichever bolt the command would already have defaulted to, so
        /// the field is never empty on open and the session's remembered choice
        /// carries into this window.
        /// </summary>
        private void LoadBolt()
        {
            _choice = Cmd_MG2.PreferredBolt(MG2Shared.LoadBoltChoices(_doc));

            AvControls.SetPickerValue(
                _boltButton, _choice?.DisplayLabel, "No bolt loaded in this document");

            RefreshPlaceButton();
        }

        /// <summary>
        /// Opens BoltPickerWindow modally over this one, owned by it — which is
        /// what makes that window's CenterOwner mean something.
        ///
        /// Goes through the command's own ChooseBolt rather than opening the
        /// picker directly, because that is where the session memory is written.
        /// </summary>
        private void OnPickBolt()
        {
            MG2Shared.BoltChoice? picked = Cmd_MG2.ChooseBolt(_doc, this);

            // Cancelled, or nothing to pick from — ChooseBolt has already said so.
            if (picked is null) return;

            _choice = picked;
            AvControls.SetPickerValue(_boltButton, picked.DisplayLabel, string.Empty);

            RefreshPlaceButton();
        }

        // ── Section 2 ──────────────────────────────────────────────────────────

        private void Step(int delta)
        {
            // Both guards are belt and braces — the buttons are already disabled
            // in either case — but a stepper that could move while its own caption
            // says the count is fixed is not a thing to leave to button state.
            if (_maxBolts <= 0 || _countLocked) return;

            _boltsPerJoint = Math.Min(_maxBolts, Math.Max(1, _boltsPerJoint + delta));
            ShowCount();
        }

        /// <summary>
        /// Writes the count and both step buttons' enabled state. The single place
        /// either is rendered, so the buttons cannot disagree with the number
        /// between them.
        /// </summary>
        private void ShowCount()
        {
            _countValue.Text = _boltsPerJoint.ToString();

            // Locked: the figure shown is the LARGEST joint's maximum, and a
            // smaller joint will take less than it. The caption has to say so, or
            // the number reads as a promise that every joint gets that many.
            _countOfMax.Text = _countLocked
                ? $"each joint takes its own maximum, {_maxBolts} at most"
                : $"of {_maxBolts} possible";

            _minusButton.IsEnabled = !_countLocked && _boltsPerJoint > 1;
            _plusButton.IsEnabled = !_countLocked && _boltsPerJoint < _maxBolts;

            RefreshPlaceButton();
        }

        /// <summary>
        /// The single place the Place button's enabled state is decided, from the
        /// two things that have to be true at once.
        /// </summary>
        private void RefreshPlaceButton()
        {
            _placeButton.IsEnabled = _maxBolts > 0 && _choice is not null;
        }

        /// <summary>
        /// Closes with a yes. The command placed nothing while this was open and
        /// places everything once it has closed.
        /// </summary>
        private void OnPlace()
        {
            DialogResult = true;
        }
    }
}
