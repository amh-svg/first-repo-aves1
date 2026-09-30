// -----------------------------------------------------------------------
// Title: Cable Tray Armour Generator
// Summary: Cmd_MG3. The user hand-places one armour set (two adaptive
//          Generic Model halves plus every bolt and nut) on one cable tray,
//          picks that same tray as the reference, and this reads the set as
//          its own in-memory template — no file, no JSON — and repeats it
//          along every other selected tray of the same family + type, at a
//          user-chosen spacing and end clearance. On the reference tray
//          itself, new sets are anchored to the sample's own position; the
//          sample is never moved, replaced or duplicated.
//
// Locked findings (each confirmed empirically before being relied on here):
//   - Tray frame: instance transform x MG1Shared.Compose(Rotate XY/XZ/YZ),
//     read from the tray's own parameters at runtime, never hardcoded.
//     See MG3Shared.TryGetTrayFrame.
//   - The frame's origin sits at the centre of the run: local X spans
//     -L/2 to +L/2.
//   - Fasteners: NewFamilyInstance's reference direction is built from
//     HandOrientationLocal. FacingOrientationLocal was measured 90 degrees
//     off on every one of 8 fasteners tested — it is not read at all.
//   - Armour halves are ONE-point adaptive families: position alone cannot
//     express their rotation. Both ReferencePoint.SetCoordinateSystem AND a
//     copy of the captured Rotate XY/XZ/YZ onto the new instance are
//     required — the instance transform alone cannot see the family's
//     internal geometry rotation, so dropping the parameter copy silently
//     leaves halves misoriented.
//   - The reference tray is always picked by the user, never auto-detected.
//     The safety check right after picking requires the sample's centroid to
//     fall STRICTLY within [-L/2, L/2] on local X — no tolerance, because a
//     sample sitting near a joint between two trays must not pass against
//     the wrong neighbour. REFERENCE_TRAY_TOLERANCE_MM applies to the Y/Z
//     cross-section check only.
//
// Outstanding (not acted on, listed for later):
//   - Icons MG3_MG316.png / MG3_MG332.png do not exist yet; the ribbon
//     button renders label-only.
//   - Tooltip text for the ribbon button.
//   - MG3Shared depends on MG1Shared (Compose, Rotate parameter names) and
//     MG2Shared (SolidsOf, LabelOf) by reference. Whether any of that moves
//     to a common helper file, shared with MG1/MG2, is undecided.
//   - MG3Shared.ComputeArmourReference reads the armour's captured
//     placement points only, not the halves' solid geometry, to find its
//     own centre and thickness. Passed every test run so far; geometry
//     could in principle disagree with points for some future family.
//   - A tray is recognised by MG3Shared.IsTray — has a B+R frame and a
//     Length parameter — not by family name. If an armour family ever
//     gains its own Length parameter, this stops discriminating and the
//     check would need to switch to matching exact tray family names.
// -----------------------------------------------------------------------
// .NET
using System;
using System.Collections.Generic;
using System.Globalization;
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
// WPF, for the spacing/clearance and sample-confirmation dialogs. Grid
// collides with Autodesk.Revit.DB and TextBox with Autodesk.Revit.UI (a
// ribbon control) — both need an alias; nothing else here does.
using System.Windows;
using System.Windows.Controls;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfVisibility = System.Windows.Visibility;
// Reused rather than duplicated: solid-collection and labelling helpers are
// generic geometry code, not Mekano-specific, and MG2Shared already has them
// well exercised. Referenced only — MG2's file itself is never touched.
using MG2 = AVES.Cmds_MG2.MG2Shared;
// Same reuse principle: the tray family (and the armour halves) share
// Mekano's own Rotate XY/XZ/YZ convention and composition order.
// MG1Shared.Compose already implements R = Rz(XY)*Rx(YZ)*Ry(XZ) correctly —
// referenced only, never modified.
using MG1 = AVES.Cmds_Button_MG1.MG1Shared;

namespace AVES.Cmds_MG3
{
    /// <summary>Frame math, in-memory armour-set capture and placement helpers shared by Cmd_MG3.</summary>
    internal static class MG3Shared
    {
        // ── Identity ───────────────────────────────────────────────────────────

        internal const string DIALOG_TITLE = "Cable Tray Armour Generator";

        // Exact parameter name this reads. Anything not spelled exactly like
        // this, on either the instance or its type, is reported as not found
        // rather than guessed at — see the hard rule on exact-name lookups.
        // Width/Height are deliberately not looked up: the tray family
        // exposes no such parameters — its width comes from the type, not
        // an instance value.
        internal const string PARAM_LENGTH = "Length";

        internal const double AXIS_EPSILON = 1e-9;

        // How far a tray's local X may tilt from true horizontal (as the sine
        // of the angle, i.e. axisX.Z of a unit axis) and still count as a
        // straight horizontal run. Production only ever handles horizontal
        // segments, so this is a real gate, not a display rounding.
        internal const double MAX_HORIZONTAL_TILT_DEG = 1.0;

        // How far outside the reference tray's own bare extents — its own
        // centreline in Y/Z only, NEVER its Length along X — the sample's
        // centroid may still land and count as "on this tray". The safety
        // check right after the user picks it (Cmd_MG3.Execute) applies this
        // to Y/Z alone; X must fall strictly within [-L/2, L/2], no
        // tolerance, or a sample sitting near a joint between two trays could
        // pass against the wrong neighbour. NOT measured from the armour's
        // real cross-section either: the armour hugs the tray so it extends
        // a little past the tray's own bare solid. Confirmed by measurement:
        // the sample sits within a few mm of the tray's own faces, so this is
        // deliberately tight.
        internal const double REFERENCE_TRAY_TOLERANCE_MM = 50.0;

        // The round-trip self-check (rebuild world from what was just read,
        // compare) should read ~0 — it is pure floating-point arithmetic on
        // data read a moment ago, not a measurement of anything physical.
        // Cmd_MG3's sample capture has no human reviewing the raw numbers
        // before placement, so these are real gates: a deviation above either
        // one means something is wrong with the frame or the points read,
        // not that the tolerance needs loosening.
        internal const double SAMPLE_SELF_CHECK_MAX_POSITION_DEVIATION_MM = 1.0;
        internal const double SAMPLE_SELF_CHECK_MAX_ANGLE_DEVIATION_DEG = 0.5;

        // How close two candidate work planes' origins may sit, measured
        // along the shared normal, and still count as the same plane. NOT a
        // physical tolerance — these planes come from the same captured
        // sample point, so any real difference is floating-point noise.
        internal const double SKETCH_PLANE_COINCIDENCE_MM = 0.5;

        // ── Unit helpers ───────────────────────────────────────────────────────

        internal static double Ft(double millimetres)
            => UnitUtils.ConvertToInternalUnits(millimetres, UnitTypeId.Millimeters);

        internal static double Mm(double feet)
            => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);

        /// <summary>Angle between two vectors, in degrees. Zero if either has collapsed to nothing.</summary>
        internal static double AngleDegreesBetween(XYZ a, XYZ b)
        {
            double lengthA = a.GetLength();
            double lengthB = b.GetLength();

            if (lengthA < AXIS_EPSILON || lengthB < AXIS_EPSILON) return 0.0;

            double cos = a.DotProduct(b) / (lengthA * lengthB);
            cos = Math.Max(-1.0, Math.Min(1.0, cos));

            return Math.Acos(cos) * 180.0 / Math.PI;
        }

        // ── Tray identification and frame ───────────────────────────────────────

        /// <summary>Reads a named Double parameter (exact name, instance first, then type) as millimetres.</summary>
        internal static (bool Found, double ValueMm) ReadLengthMm(Element element, string name)
        {
            Parameter? param = element.LookupParameter(name);

            if (param is null && element is FamilyInstance fi)
            {
                param = fi.Symbol?.LookupParameter(name);
            }

            if (param is null || !param.HasValue || param.StorageType != StorageType.Double)
            {
                return (false, 0.0);
            }

            return (true, Mm(param.AsDouble()));
        }

        /// <summary>
        /// Rotate XY/XZ/YZ read off an element, MG1's own convention (radians,
        /// as Revit stores them). Found is false, every value zero, when the
        /// element does not carry all three as numeric parameters.
        /// </summary>
        internal sealed class RotationParams
        {
            internal bool Found { get; set; }
            internal double RotateXYRad { get; set; }
            internal double RotateXZRad { get; set; }
            internal double RotateYZRad { get; set; }
        }

        /// <summary>
        /// Reads Rotate XY/XZ/YZ off an element (exact names, MG1's own
        /// constants), if all three are present as numeric parameters. Used
        /// for both the tray (frame method B+R) and each armour half — the
        /// same convention, MG1Shared.Compose, referenced and never
        /// rederived.
        /// </summary>
        internal static RotationParams ReadRotationParams(Element element)
        {
            Parameter? paramXy = element.LookupParameter(MG1.PARAM_ROTATE_XY);
            Parameter? paramXz = element.LookupParameter(MG1.PARAM_ROTATE_XZ);
            Parameter? paramYz = element.LookupParameter(MG1.PARAM_ROTATE_YZ);

            bool allNumeric =
                paramXy is not null && paramXy.HasValue && paramXy.StorageType == StorageType.Double
                && paramXz is not null && paramXz.HasValue && paramXz.StorageType == StorageType.Double
                && paramYz is not null && paramYz.HasValue && paramYz.StorageType == StorageType.Double;

            if (!allNumeric) return new RotationParams { Found = false };

            return new RotationParams
            {
                Found = true,
                RotateXYRad = paramXy!.AsDouble(),
                RotateXZRad = paramXz!.AsDouble(),
                RotateYZRad = paramYz!.AsDouble(),
            };
        }

        /// <summary>
        /// A right-handed frame along one tray: X is the run axis, Z is
        /// FORCED to world up regardless of the tray's own local Z, and
        /// Y = Z × X. This is what keeps the frame right-handed and
        /// consistent across every tray regardless of how that tray was
        /// drawn. See TryGetTrayFrame for how X and the origin are found.
        /// </summary>
        internal sealed class TrayFrame
        {
            internal XYZ Origin { get; }
            internal XYZ AxisX { get; }
            internal XYZ AxisY { get; }
            internal XYZ AxisZ { get; }
            internal Transform Transform { get; }

            /// <summary>
            /// <paramref name="axisX"/> need not be a unit vector; it is
            /// normalized here. Throws only if it has collapsed to nothing —
            /// callers are expected to have already rejected that case via
            /// TryGetTrayFrame, which is the only place this is constructed.
            /// </summary>
            internal TrayFrame(XYZ origin, XYZ axisX)
            {
                Origin = origin;
                AxisX = axisX.Normalize();
                AxisZ = XYZ.BasisZ;
                AxisY = AxisZ.CrossProduct(AxisX).Normalize();

                Transform = Transform.Identity;
                Transform.Origin = Origin;
                Transform.BasisX = AxisX;
                Transform.BasisY = AxisY;
                Transform.BasisZ = AxisZ;
            }

            internal XYZ ToLocalPoint(XYZ world) => Transform.Inverse.OfPoint(world);
            internal XYZ ToWorldPoint(XYZ local) => Transform.OfPoint(local);
            internal XYZ ToLocalVector(XYZ world) => Transform.Inverse.OfVector(world);
            internal XYZ ToWorldVector(XYZ local) => Transform.OfVector(local);
        }

        /// <summary>
        /// Builds a tray's local frame — LOCKED FINDING: instance transform x
        /// R, where R = Rz(XY) * Rx(YZ) * Ry(XZ) is built from the tray's own
        /// Rotate XY/XZ/YZ parameters (MG1Shared.Compose, referenced not
        /// rederived), read fresh from the tray every time this is called,
        /// never hardcoded. The plain instance transform alone is measurably
        /// wrong: its local axes do not line up with the tray's true run
        /// direction until this rotation is applied. The frame's origin
        /// therefore sits at the centre of the run (local X spans -L/2 to
        /// +L/2) — confirmed empirically, not assumed.
        ///
        /// Also returns the tray's Length (exact-name parameter): every
        /// caller needs both together, and a frame without a Length is not
        /// usable for distribution anyway, so this fails outright rather
        /// than making the caller ask twice.
        ///
        /// Fails when the element is not a FamilyInstance, when Rotate
        /// XY/XZ/YZ are not all present as numeric parameters, when the
        /// resulting run axis is degenerate or parallel to world Z (no
        /// horizontal frame to build), or when Length is missing.
        /// </summary>
        internal static bool TryGetTrayFrame(Element tray, out TrayFrame? frame, out double lengthMm)
        {
            frame = null;
            lengthMm = 0.0;

            if (tray is not FamilyInstance familyInstance) return false;

            RotationParams rotation = ReadRotationParams(familyInstance);
            if (!rotation.Found) return false;

            Transform instanceTransform = familyInstance.GetTransform();
            Transform rotationTransform =
                MG1.Compose(rotation.RotateXYRad, rotation.RotateXZRad, rotation.RotateYZRad);
            Transform combined = instanceTransform.Multiply(rotationTransform);

            XYZ axisX = combined.BasisX;
            if (axisX.GetLength() <= AXIS_EPSILON) return false;

            XYZ axisXNormalized = axisX.Normalize();

            // A run parallel to world Z has no horizontal frame — Z × X collapses.
            if (XYZ.BasisZ.CrossProduct(axisXNormalized).GetLength() < AXIS_EPSILON) return false;

            (bool lengthFound, double lm) = ReadLengthMm(tray, PARAM_LENGTH);
            if (!lengthFound) return false;

            frame = new TrayFrame(combined.Origin, axisXNormalized);
            lengthMm = lm;
            return true;
        }

        /// <summary>
        /// Whether an element plausibly IS a tray, as opposed to something
        /// that merely shares a placement mechanism with one. This matters
        /// because the tray family is itself a Generic Model Adaptive family
        /// — the same placement type as an armour half — so "is adaptive" or
        /// "not a native cable tray category" cannot tell the two apart on
        /// their own. What actually distinguishes a tray is that
        /// TryGetTrayFrame can build its frame AND it carries a Length
        /// parameter; an armour half has neither. Used both for tray
        /// selection (TrayElementFilter) and to keep a tray out of a
        /// hand-placed armour sample (ArmourSampleFilter).
        /// </summary>
        internal static bool IsTray(Element elem)
        {
            Category? category = elem.Category;
            if (category is null) return false;

            try
            {
                BuiltInCategory bic = category.BuiltInCategory;
                if (bic == BuiltInCategory.OST_CableTray || bic == BuiltInCategory.OST_CableTrayFitting) return false;
            }
            catch { }

            return TryGetTrayFrame(elem, out _, out _);
        }

        /// <summary>Accepts a tray, and only a tray — see IsTray. Used for both reference-tray and target-tray picking.</summary>
        internal sealed class TrayElementFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => IsTray(elem);

            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        /// <summary>
        /// Accepts only what Cmd_MG3's sample can use: an adaptive component
        /// instance (an armour half) or a WorkPlaneBased family instance (a
        /// bolt or nut) — excluding anything IsTray identifies as a tray,
        /// since the tray family is itself Generic Model Adaptive and would
        /// otherwise pass the adaptive check too.
        /// </summary>
        internal sealed class ArmourSampleFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                if (elem is not FamilyInstance fi) return false;
                if (IsTray(fi)) return false;

                if (AdaptiveComponentInstanceUtils.IsAdaptiveComponentInstance(fi)) return true;
                return fi.Symbol?.Family?.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased;
            }

            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        /// <summary>
        /// The tray's own solid geometry, measured in its local frame — the
        /// verification that the frame's X really is the run axis, done by
        /// evidence rather than assumed because that is what X is supposed to
        /// mean. Reports which axis's span comes closest to the tray's own
        /// Length.
        /// </summary>
        internal sealed class LocalExtent
        {
            internal double MinXMm { get; set; }
            internal double MaxXMm { get; set; }
            internal double MinYMm { get; set; }
            internal double MaxYMm { get; set; }
            internal double MinZMm { get; set; }
            internal double MaxZMm { get; set; }

            /// <summary>Which axis's span came closest to the tray's own Length. Should read "X".</summary>
            internal string LengthMatchedAxis { get; set; } = string.Empty;
            internal bool LengthMatchesX { get; set; }
        }

        /// <summary>Measures a tray's solids in <paramref name="frame"/> and compares each axis's span against <paramref name="lengthMm"/>.</summary>
        internal static LocalExtent ComputeLocalExtents(List<Solid> solids, TrayFrame frame, double lengthMm)
        {
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            double minZ = double.MaxValue, maxZ = double.MinValue;

            foreach (Solid solid in solids)
            {
                foreach (Edge edge in solid.Edges)
                {
                    IList<XYZ> points;
                    try { points = edge.Tessellate(); }
                    catch { continue; }

                    foreach (XYZ worldPoint in points)
                    {
                        XYZ local = frame.ToLocalPoint(worldPoint);
                        minX = Math.Min(minX, local.X); maxX = Math.Max(maxX, local.X);
                        minY = Math.Min(minY, local.Y); maxY = Math.Max(maxY, local.Y);
                        minZ = Math.Min(minZ, local.Z); maxZ = Math.Max(maxZ, local.Z);
                    }
                }
            }

            if (minX > maxX)
            {
                return new LocalExtent { LengthMatchedAxis = "(no geometry)" };
            }

            double spanXMm = Mm(maxX - minX);
            double spanYMm = Mm(maxY - minY);
            double spanZMm = Mm(maxZ - minZ);

            double diffX = Math.Abs(spanXMm - lengthMm);
            double diffY = Math.Abs(spanYMm - lengthMm);
            double diffZ = Math.Abs(spanZMm - lengthMm);

            string matchedAxis = diffX <= diffY && diffX <= diffZ ? "X" : diffY <= diffZ ? "Y" : "Z";

            return new LocalExtent
            {
                MinXMm = Mm(minX),
                MaxXMm = Mm(maxX),
                MinYMm = Mm(minY),
                MaxYMm = Mm(maxY),
                MinZMm = Mm(minZ),
                MaxZMm = Mm(maxZ),
                LengthMatchedAxis = matchedAxis,
                LengthMatchesX = matchedAxis == "X",
            };
        }

        // ── Armour-set capture (in memory — no file) ────────────────────────────

        /// <summary>A plain, mutable XYZ carrier, deliberately a different type from Revit's own XYZ so a local-frame value (millimetres, or a unit direction) is never accidentally passed where a world-space, feet-valued XYZ is expected.</summary>
        internal sealed class Vec3
        {
            internal double X { get; set; }
            internal double Y { get; set; }
            internal double Z { get; set; }

            internal Vec3() { }

            internal Vec3(XYZ v)
            {
                X = v.X;
                Y = v.Y;
                Z = v.Z;
            }

            internal XYZ ToXYZ() => new(X, Y, Z);
        }

        /// <summary>Converts a local point already in feet to a millimetre Vec3.</summary>
        internal static Vec3 MmVec3(XYZ localFt) => new(new XYZ(Mm(localFt.X), Mm(localFt.Y), Mm(localFt.Z)));

        /// <summary>
        /// A world coordinate system (origin + three basis vectors), captured
        /// in local coordinates. One point's Position alone cannot express a
        /// one-point adaptive family's rotation — this can, coming from
        /// ReferencePoint.GetCoordinateSystem().
        /// </summary>
        internal sealed class CoordinateSystemData
        {
            internal Vec3 OriginLocalMm { get; set; } = new();
            internal Vec3 BasisXLocal { get; set; } = new();
            internal Vec3 BasisYLocal { get; set; } = new();
            internal Vec3 BasisZLocal { get; set; } = new();
        }

        /// <summary>
        /// One adaptive Generic Model armour half. Points are in captured
        /// (adaptive) order; PointCoordinateSystems is the same order, one
        /// entry per point that yielded a readable coordinate system (may be
        /// shorter than PointsLocalMm if one point's system could not be
        /// read — see the capture warnings, never silently padded).
        /// </summary>
        internal sealed class ArmourHalf
        {
            internal string Family { get; set; } = string.Empty;
            internal string Type { get; set; } = string.Empty;
            internal List<Vec3> PointsLocalMm { get; set; } = new();
            internal List<CoordinateSystemData> PointCoordinateSystems { get; set; } = new();

            /// <summary>Rotate XY/XZ/YZ, if this half's family carries them — see ReadRotationParams.</summary>
            internal RotationParams RotationParams { get; set; } = new();
        }

        /// <summary>One WorkPlaneBased bolt or nut.</summary>
        internal sealed class Fastener
        {
            internal string Family { get; set; } = string.Empty;
            internal string Type { get; set; } = string.Empty;
            internal Vec3 OriginLocalMm { get; set; } = new();
            internal Vec3 HandOrientationLocal { get; set; } = new();
            internal Vec3 WorkPlaneNormalLocal { get; set; } = new();
            internal Vec3 WorkPlaneOriginLocalMm { get; set; } = new();
        }

        /// <summary>
        /// Reads one armour half's placement points, in adaptive order, into
        /// the tray's local frame. A point that cannot be read is warned
        /// about and skipped rather than silently dropped or substituted.
        ///
        /// Position alone does not describe a ONE-point adaptive family's
        /// rotation, so each point's own coordinate system
        /// (GetCoordinateSystem) is captured alongside it, and the instance's
        /// own transform (GetTransform) feeds the round-trip self-check the
        /// same way positions do (see vectorChecks) without being stored —
        /// nothing downstream reads it.
        /// </summary>
        internal static void CaptureArmourHalf(
            Document doc,
            FamilyInstance fi,
            TrayFrame frame,
            List<ArmourHalf> sink,
            List<string> warnings,
            List<(XYZ World, XYZ LocalFt)> pointChecks,
            List<(XYZ World, XYZ LocalUnit)> vectorChecks)
        {
            string typeName = fi.Symbol?.Name ?? "Unknown";

            var half = new ArmourHalf
            {
                Family = fi.Symbol?.Family?.Name ?? "Unknown",
                Type = typeName,
            };

            IList<ElementId> pointIds;
            try
            {
                pointIds = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(fi);
            }
            catch (Exception ex)
            {
                warnings.Add($"Armour half \"{typeName}\" ({fi.Id}): could not read its placement points — {ex.Message}");
                return;
            }

            foreach (ElementId id in pointIds)
            {
                if (doc.GetElement(id) is not ReferencePoint referencePoint)
                {
                    warnings.Add(
                        $"Armour half \"{typeName}\" ({fi.Id}): placement point {id} did not resolve to a " +
                        "ReferencePoint.");
                    continue;
                }

                XYZ world;
                try
                {
                    world = referencePoint.Position;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Armour half \"{typeName}\" ({fi.Id}): could not read point {id} — {ex.Message}");
                    continue;
                }

                XYZ localFt = frame.ToLocalPoint(world);
                half.PointsLocalMm.Add(MmVec3(localFt));
                pointChecks.Add((world, localFt));

                try
                {
                    Transform pointCs = referencePoint.GetCoordinateSystem();

                    XYZ csOriginLocalFt = frame.ToLocalPoint(pointCs.Origin);
                    XYZ basisXLocal = frame.ToLocalVector(pointCs.BasisX).Normalize();
                    XYZ basisYLocal = frame.ToLocalVector(pointCs.BasisY).Normalize();
                    XYZ basisZLocal = frame.ToLocalVector(pointCs.BasisZ).Normalize();

                    half.PointCoordinateSystems.Add(new CoordinateSystemData
                    {
                        OriginLocalMm = MmVec3(csOriginLocalFt),
                        BasisXLocal = new Vec3(basisXLocal),
                        BasisYLocal = new Vec3(basisYLocal),
                        BasisZLocal = new Vec3(basisZLocal),
                    });

                    pointChecks.Add((pointCs.Origin, csOriginLocalFt));
                    vectorChecks.Add((pointCs.BasisX, basisXLocal));
                    vectorChecks.Add((pointCs.BasisY, basisYLocal));
                    vectorChecks.Add((pointCs.BasisZ, basisZLocal));
                }
                catch (Exception ex)
                {
                    warnings.Add(
                        $"Armour half \"{typeName}\" ({fi.Id}): could not read point {id}'s coordinate " +
                        $"system — {ex.Message}. Position was still captured.");
                }
            }

            if (half.PointsLocalMm.Count == 0)
            {
                warnings.Add($"Armour half \"{typeName}\" ({fi.Id}): no points could be read; skipped.");
                return;
            }

            try
            {
                Transform instanceTransform = fi.GetTransform();

                XYZ instOriginLocalFt = frame.ToLocalPoint(instanceTransform.Origin);
                XYZ instBasisXLocal = frame.ToLocalVector(instanceTransform.BasisX).Normalize();
                XYZ instBasisYLocal = frame.ToLocalVector(instanceTransform.BasisY).Normalize();
                XYZ instBasisZLocal = frame.ToLocalVector(instanceTransform.BasisZ).Normalize();

                pointChecks.Add((instanceTransform.Origin, instOriginLocalFt));
                vectorChecks.Add((instanceTransform.BasisX, instBasisXLocal));
                vectorChecks.Add((instanceTransform.BasisY, instBasisYLocal));
                vectorChecks.Add((instanceTransform.BasisZ, instBasisZLocal));
            }
            catch (Exception ex)
            {
                warnings.Add($"Armour half \"{typeName}\" ({fi.Id}): could not read its instance transform — {ex.Message}.");
            }

            half.RotationParams = ReadRotationParams(fi);

            sink.Add(half);
        }

        /// <summary>
        /// Reads one bolt or nut's pose into the tray's local frame: origin,
        /// HandOrientation and the work-plane normal — the reference
        /// direction placement uses, per the locked finding that
        /// FacingOrientation is 90 degrees off and is therefore never read.
        /// </summary>
        internal static void CaptureFastener(
            FamilyInstance fi,
            TrayFrame frame,
            List<Fastener> sink,
            List<string> warnings,
            List<(XYZ World, XYZ LocalFt)> pointChecks,
            List<(XYZ World, XYZ LocalUnit)> vectorChecks)
        {
            string typeName = fi.Symbol?.Name ?? "Unknown";

            Transform instanceTransform;
            try
            {
                instanceTransform = fi.GetTransform();
            }
            catch (Exception ex)
            {
                warnings.Add($"Fastener \"{typeName}\" ({fi.Id}): could not read its transform — {ex.Message}");
                return;
            }

            XYZ worldOrigin = instanceTransform.Origin;
            XYZ worldNormal = instanceTransform.BasisZ;

            XYZ worldHand;
            try
            {
                worldHand = fi.HandOrientation;
            }
            catch (Exception ex)
            {
                warnings.Add(
                    $"Fastener \"{typeName}\" ({fi.Id}): could not read HandOrientation — {ex.Message}; " +
                    "falling back to the instance transform's Y axis.");
                worldHand = instanceTransform.BasisY;
            }

            XYZ localOriginFt = frame.ToLocalPoint(worldOrigin);
            XYZ localNormal = frame.ToLocalVector(worldNormal).Normalize();
            XYZ localHand = frame.ToLocalVector(worldHand).Normalize();

            var originMm = new Vec3(
                new XYZ(Mm(localOriginFt.X), Mm(localOriginFt.Y), Mm(localOriginFt.Z)));

            sink.Add(new Fastener
            {
                Family = fi.Symbol?.Family?.Name ?? "Unknown",
                Type = typeName,
                OriginLocalMm = originMm,
                HandOrientationLocal = new Vec3(localHand),
                WorkPlaneNormalLocal = new Vec3(localNormal),
                // The work plane passes through the placement origin for a
                // WorkPlaneBased instance placed via the sketch-plane
                // overload (the only overload this family type can use) —
                // same point, kept as its own field because placement builds
                // the sketch plane from it independently of where the
                // instance itself ends up.
                WorkPlaneOriginLocalMm = originMm,
            });

            pointChecks.Add((worldOrigin, localOriginFt));
            vectorChecks.Add((worldNormal, localNormal));
            vectorChecks.Add((worldHand, localHand));
        }

        /// <summary>One hand-placed armour set, captured in memory — no file, no tray identity, just the set itself.</summary>
        internal sealed class ArmourSet
        {
            internal List<ArmourHalf> Halves { get; } = new();
            internal List<Fastener> Fasteners { get; } = new();
            internal List<string> Warnings { get; } = new();
            internal double SelfCheckMaxPositionDeviationMm { get; set; }
            internal double SelfCheckMaxAngleDeviationDeg { get; set; }
        }

        /// <summary>
        /// Reads a hand-placed armour sample into the reference tray's local
        /// frame, entirely in memory. Classifies every element as an armour
        /// half (adaptive) or a fastener (WorkPlaneBased); anything else is
        /// skipped and warned about. The round-trip self-check (rebuild world
        /// from what was just read, compare) feeds
        /// SelfCheckMaxPositionDeviationMm/SelfCheckMaxAngleDeviationDeg,
        /// which Cmd_MG3 gates on — see SAMPLE_SELF_CHECK_MAX_*.
        /// </summary>
        internal static ArmourSet CaptureSample(Document doc, TrayFrame frame, List<Element> sampleElements)
        {
            var set = new ArmourSet();
            var pointChecks = new List<(XYZ World, XYZ LocalFt)>();
            var vectorChecks = new List<(XYZ World, XYZ LocalUnit)>();

            foreach (Element el in sampleElements)
            {
                if (el is not FamilyInstance fi)
                {
                    set.Warnings.Add($"Skipped {MG2.LabelOf(el)} ({el.Id}): not a family instance.");
                    continue;
                }

                if (AdaptiveComponentInstanceUtils.IsAdaptiveComponentInstance(fi))
                {
                    CaptureArmourHalf(doc, fi, frame, set.Halves, set.Warnings, pointChecks, vectorChecks);
                }
                else if (fi.Symbol?.Family?.FamilyPlacementType == FamilyPlacementType.WorkPlaneBased)
                {
                    CaptureFastener(fi, frame, set.Fasteners, set.Warnings, pointChecks, vectorChecks);
                }
                else
                {
                    set.Warnings.Add(
                        $"Skipped {MG2.LabelOf(el)} ({el.Id}): placement type " +
                        $"{fi.Symbol?.Family?.FamilyPlacementType} is neither adaptive nor WorkPlaneBased.");
                }
            }

            foreach ((XYZ world, XYZ localFt) in pointChecks)
            {
                XYZ rebuilt = frame.ToWorldPoint(localFt);
                set.SelfCheckMaxPositionDeviationMm =
                    Math.Max(set.SelfCheckMaxPositionDeviationMm, Mm(world.DistanceTo(rebuilt)));
            }

            foreach ((XYZ world, XYZ localUnit) in vectorChecks)
            {
                XYZ rebuilt = frame.ToWorldVector(localUnit);
                set.SelfCheckMaxAngleDeviationDeg =
                    Math.Max(set.SelfCheckMaxAngleDeviationDeg, AngleDegreesBetween(world, rebuilt));
            }

            return set;
        }

        /// <summary>
        /// The armour set's own centre and thickness along local X, measured
        /// across every captured element (every half's adaptive points, every
        /// fastener's origin) in the reference tray's local frame, exactly as
        /// captured. Placement shifts every element relative to this centre,
        /// not its captured X — that is what lets the same set repeat at any
        /// position on any tray, instead of only ever reproducing itself at
        /// the one spot it was captured from.
        /// </summary>
        internal static (double CentreMm, double ThicknessMm) ComputeArmourReference(ArmourSet set)
        {
            var xs = new List<double>();

            foreach (ArmourHalf half in set.Halves)
            {
                foreach (Vec3 point in half.PointsLocalMm) xs.Add(point.X);
            }

            foreach (Fastener fastener in set.Fasteners)
            {
                xs.Add(fastener.OriginLocalMm.X);
            }

            if (xs.Count == 0) return (0.0, 0.0);

            double min = xs.Min();
            double max = xs.Max();

            return ((min + max) / 2.0, max - min);
        }

        // ── Placement helpers ────────────────────────────────────────────────────

        /// <summary>
        /// The loaded FamilySymbol matching an exact family + type name, or
        /// null. Ordinal comparison throughout, matching the hard rule on
        /// family lookups — a symbol with a similar name is not a match.
        /// </summary>
        internal static FamilySymbol? FindFamilySymbol(Document doc, string familyName, string typeName)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s =>
                    string.Equals(s.Family?.Name, familyName, StringComparison.Ordinal)
                    && string.Equals(s.Name, typeName, StringComparison.Ordinal));
        }

        /// <summary>
        /// Reuses one SketchPlane for every fastener that shares it, instead
        /// of creating one per fastener. Two candidates are the same plane
        /// when their normals point the same way and their origins fall
        /// within SKETCH_PLANE_COINCIDENCE_MM of each other along that
        /// normal.
        /// </summary>
        internal sealed class SketchPlaneCache
        {
            private readonly List<(XYZ Normal, XYZ Origin, SketchPlane Plane)> _planes = new();
            private readonly double _toleranceFt;

            internal SketchPlaneCache(double toleranceMm) => _toleranceFt = Ft(toleranceMm);

            internal SketchPlane GetOrCreate(Document doc, XYZ worldNormal, XYZ worldOrigin)
            {
                foreach ((XYZ normal, XYZ origin, SketchPlane plane) in _planes)
                {
                    if (normal.DotProduct(worldNormal) > 0.999999
                        && Math.Abs((worldOrigin - origin).DotProduct(normal)) < _toleranceFt)
                    {
                        return plane;
                    }
                }

                SketchPlane created =
                    SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(worldNormal, worldOrigin));
                _planes.Add((worldNormal, worldOrigin, created));
                return created;
            }
        }
    }

    /// <summary>
    /// A small labelled numeric entry: label, text box, unit suffix. Its own
    /// minimal copy rather than a reuse of Cmds_PullDown2.cs's NumericRow —
    /// that file backs protected commands (Vendor/Assembly/MT Drawings) and
    /// is referenced by nothing here, matching the hard rule.
    /// </summary>
    internal sealed class SimpleNumericField
    {
        internal StackPanel Container { get; }
        internal event Action? ValueChanged;

        private readonly WpfTextBox _textBox;

        internal SimpleNumericField(string label, string suffix)
        {
            Container = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 6, 0, 6),
            };

            var labelBlock = new TextBlock
            {
                Text = label,
                Width = 170,
                FontFamily = AvTokens.Ui,
                FontSize = 12,
                Foreground = AvTokens.B(AvTokens.TextDim),
                VerticalAlignment = VerticalAlignment.Center,
            };

            _textBox = new WpfTextBox
            {
                Width = 90,
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                Foreground = AvTokens.B(AvTokens.Text),
                Background = AvTokens.B(AvTokens.Surface),
                BorderBrush = AvTokens.B(AvTokens.BorderStrong),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
                HorizontalContentAlignment = HorizontalAlignment.Right,
            };
            _textBox.TextChanged += (_, __) => ValueChanged?.Invoke();

            var suffixBlock = new TextBlock
            {
                Text = " " + suffix,
                FontFamily = AvTokens.Ui,
                FontSize = 12,
                Foreground = AvTokens.B(AvTokens.TextFaint),
                VerticalAlignment = VerticalAlignment.Center,
            };

            Container.Children.Add(labelBlock);
            Container.Children.Add(_textBox);
            Container.Children.Add(suffixBlock);
        }

        internal bool IsValid => TryParse(out _);

        internal double Value => TryParse(out double v) ? v : 0.0;

        internal void SetValue(double value) => _textBox.Text = value.ToString("0.###", CultureInfo.InvariantCulture);

        private bool TryParse(out double value)
        {
            string normalized = (_textBox.Text ?? string.Empty).Replace(',', '.');
            return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }

    /// <summary>
    /// Spacing and end-clearance entry, AVES style. Modal: ShowDialog blocks
    /// inside Cmd_MG3.Execute, so placement below runs in the same API
    /// context the command started in.
    /// </summary>
    internal sealed class SpacingInputWindow : Window
    {
        internal double SpacingMm => _spacing.Value;
        internal double EndClearanceMm => _clearance.Value;

        private readonly SimpleNumericField _spacing;
        private readonly SimpleNumericField _clearance;
        private readonly Button _placeButton;

        internal SpacingInputWindow()
        {
            Title = MG3Shared.DIALOG_TITLE;
            Width = 380;
            Height = 260;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = AvTokens.B(AvTokens.Surface);
            FontFamily = AvTokens.Ui;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var root = new WpfGrid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            Border header = AvControls.TitleBar("Cable Tray Armour Generator");
            WpfGrid.SetRow(header, 0);
            root.Children.Add(header);

            var body = new StackPanel { Margin = new Thickness(20) };
            WpfGrid.SetRow(body, 1);
            root.Children.Add(body);

            _spacing = new SimpleNumericField("Spacing (s)", "mm");
            _clearance = new SimpleNumericField("End clearance (c)", "mm");
            _clearance.SetValue(0);

            body.Children.Add(_spacing.Container);
            body.Children.Add(_clearance.Container);

            var validationText = new TextBlock
            {
                Foreground = AvTokens.B(AvTokens.FailFg),
                FontFamily = AvTokens.Ui,
                FontSize = 11.5,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Visibility = WpfVisibility.Collapsed,
            };
            body.Children.Add(validationText);

            _placeButton = AvControls.PrimaryButton("Place armour");
            _placeButton.Click += (_, __) => { DialogResult = true; Close(); };

            Button cancelButton = AvControls.SecondaryButton("Cancel");
            cancelButton.Click += (_, __) => { DialogResult = false; Close(); };

            var footerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            footerRow.Children.Add(cancelButton);
            footerRow.Children.Add(_placeButton);

            Border footer = AvControls.FooterStrip(footerRow);
            WpfGrid.SetRow(footer, 2);
            root.Children.Add(footer);

            void Revalidate()
            {
                bool spacingOk = _spacing.IsValid && _spacing.Value > 0.0;
                bool clearanceOk = _clearance.IsValid && _clearance.Value >= 0.0;

                _placeButton.IsEnabled = spacingOk && clearanceOk;
                validationText.Visibility = _placeButton.IsEnabled ? WpfVisibility.Collapsed : WpfVisibility.Visible;
                validationText.Text = !spacingOk
                    ? "Spacing must be a positive number of millimetres."
                    : !clearanceOk
                        ? "End clearance must be zero or a positive number of millimetres."
                        : string.Empty;
            }

            _spacing.ValueChanged += Revalidate;
            _clearance.ValueChanged += Revalidate;
            Revalidate();
        }
    }

    /// <summary>
    /// Shows the sample's counts per family and asks whether to proceed —
    /// Cmd_MG3's sample-driven flow has nothing else to confirm against
    /// before it goes looking for the reference tray, so this is the one
    /// chance to catch a wrong click before it does.
    /// </summary>
    internal sealed class SampleConfirmationWindow : Window
    {
        private readonly Button _continueButton;

        internal SampleConfirmationWindow(string summaryText)
        {
            Title = MG3Shared.DIALOG_TITLE;
            Width = 420;
            Height = 340;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = AvTokens.B(AvTokens.Surface);
            FontFamily = AvTokens.Ui;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            var root = new WpfGrid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            Border header = AvControls.TitleBar("Cable Tray Armour Generator");
            WpfGrid.SetRow(header, 0);
            root.Children.Add(header);

            var body = new StackPanel { Margin = new Thickness(20) };

            var bodyScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = body,
            };
            WpfGrid.SetRow(bodyScroll, 1);
            root.Children.Add(bodyScroll);

            body.Children.Add(new TextBlock
            {
                Text = "Use this as the sample armour set?",
                FontFamily = AvTokens.Ui,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = AvTokens.B(AvTokens.Text),
                Margin = new Thickness(0, 0, 0, 10),
            });

            body.Children.Add(new TextBlock
            {
                Text = summaryText,
                FontFamily = AvTokens.Ui,
                FontSize = 12,
                Foreground = AvTokens.B(AvTokens.TextDim),
                TextWrapping = TextWrapping.Wrap,
            });

            _continueButton = AvControls.PrimaryButton("Continue");
            _continueButton.Click += (_, __) => { DialogResult = true; Close(); };

            Button cancelButton = AvControls.SecondaryButton("Cancel");
            cancelButton.Click += (_, __) => { DialogResult = false; Close(); };

            var footerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            footerRow.Children.Add(cancelButton);
            footerRow.Children.Add(_continueButton);

            Border footer = AvControls.FooterStrip(footerRow);
            WpfGrid.SetRow(footer, 2);
            root.Children.Add(footer);
        }
    }

    /// <summary>
    /// Cable Tray Armour Generator — production command. Every placement
    /// decision here is a LOCKED FINDING, verified empirically, not a fresh
    /// guess — see the file banner. This class only orchestrates: picking,
    /// validating, distributing and placing; the geometry and capture logic
    /// all live in MG3Shared.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class Cmd_MG3 : IExternalCommand
    {
        /// <summary>One selected tray that passed every gate: frame + Length (via MG3Shared.IsTray), horizontal, and — for a non-reference tray — family/type match with the reference.</summary>
        private sealed class ValidTray
        {
            internal Element Tray { get; }
            internal MG3Shared.TrayFrame Frame { get; }
            internal double LengthMm { get; }
            internal bool IsReferenceTray { get; }

            internal ValidTray(Element tray, MG3Shared.TrayFrame frame, double lengthMm, bool isReferenceTray)
            {
                Tray = tray;
                Frame = frame;
                LengthMm = lengthMm;
                IsReferenceTray = isReferenceTray;
            }
        }

        /// <summary>
        /// One valid tray's distribution: how many sets, where, and what came
        /// of placing them. SetCentresMm holds only NEW sets — for the
        /// reference tray, the hand-placed sample itself is never in this
        /// list, since it is never re-placed.
        /// </summary>
        private sealed class TrayPlan
        {
            internal ValidTray Tray { get; }
            internal double ArmourCentreMm { get; }
            internal double ArmourThicknessMm { get; }
            internal List<double> SetCentresMm { get; }

            /// <summary>Generic (non-reference) trays only.</summary>
            internal double? ActualEndGapMm { get; init; }

            /// <summary>Reference tray only.</summary>
            internal int? SetsPositiveSide { get; init; }
            internal int? SetsNegativeSide { get; init; }
            internal double? GapPositiveEndMm { get; init; }
            internal double? GapNegativeEndMm { get; init; }
            internal bool? SampleWithinClearance { get; init; }

            internal int SetsPlaced { get; set; }
            internal int ElementsPlaced { get; set; }

            internal TrayPlan(
                ValidTray tray, double armourCentreMm, double armourThicknessMm, List<double> setCentresMm)
            {
                Tray = tray;
                ArmourCentreMm = armourCentreMm;
                ArmourThicknessMm = armourThicknessMm;
                SetCentresMm = setCentresMm;
            }
        }

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument? uiDoc = commandData.Application.ActiveUIDocument;

            if (uiDoc is null)
            {
                AvControls.ShowAlert(
                    MG3Shared.DIALOG_TITLE, "No active document.\n\nOpen a project and re-run.", AlertSeverity.Warning);
                return Result.Cancelled;
            }

            Document doc = uiDoc.Document;

            try
            {
                // ── 1. The sample ────────────────────────────────────────────────
                IList<Reference> sampleRefs;
                try
                {
                    sampleRefs = uiDoc.Selection.PickObjects(
                        ObjectType.Element,
                        new MG3Shared.ArmourSampleFilter(),
                        "Select one hand-placed armour set — both halves, every bolt and nut (click and " +
                        "box selection), then finish.");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                var seenSample = new HashSet<ElementId>();
                var sampleElements = new List<Element>();

                foreach (Reference reference in sampleRefs)
                {
                    Element? el = doc.GetElement(reference);
                    if (el is not null && seenSample.Add(el.Id)) sampleElements.Add(el);
                }

                if (sampleElements.Count == 0)
                {
                    AvControls.ShowAlert(MG3Shared.DIALOG_TITLE, "No armour elements were selected.", AlertSeverity.Warning);
                    return Result.Cancelled;
                }

                // Second check, in addition to the pick filter: the tray
                // family is itself Generic Model Adaptive, so a tray can in
                // principle slip past a filter that only tests placement
                // type. A tray is never counted as an armour half.
                int trayLikeCount = sampleElements.Count(MG3Shared.IsTray);
                if (trayLikeCount > 0)
                {
                    sampleElements = sampleElements.Where(el => !MG3Shared.IsTray(el)).ToList();
                }

                if (sampleElements.Count == 0)
                {
                    AvControls.ShowAlert(
                        MG3Shared.DIALOG_TITLE,
                        "Everything selected looked like a tray, not an armour part.",
                        AlertSeverity.Error);
                    return Result.Failed;
                }

                int sampleHalfCount = sampleElements.OfType<FamilyInstance>()
                    .Count(AdaptiveComponentInstanceUtils.IsAdaptiveComponentInstance);
                int sampleFastenerCount = sampleElements.Count - sampleHalfCount;

                if (sampleHalfCount == 0 || sampleFastenerCount == 0)
                {
                    AvControls.ShowAlert(
                        MG3Shared.DIALOG_TITLE,
                        "The sample must include at least one armour half and at least one fastener.\n\n" +
                        $"Found {sampleHalfCount} half(s), {sampleFastenerCount} fastener(s)" +
                        (trayLikeCount > 0 ? $"; {trayLikeCount} tray-like element(s) were ignored." : "."),
                        AlertSeverity.Error);
                    return Result.Failed;
                }

                var sampleCounts = sampleElements
                    .OfType<FamilyInstance>()
                    .GroupBy(fi => (
                        Kind: AdaptiveComponentInstanceUtils.IsAdaptiveComponentInstance(fi) ? "half" : "fastener",
                        Family: fi.Symbol?.Family?.Name ?? "Unknown",
                        Type: fi.Symbol?.Name ?? "Unknown"))
                    .OrderBy(g => g.Key.Kind)
                    .Select(g => $"{g.Count()} x \"{g.Key.Family}\" / \"{g.Key.Type}\" ({g.Key.Kind}s)")
                    .ToList();

                var confirmation = new SampleConfirmationWindow(
                    $"{sampleHalfCount} half(s), {sampleFastenerCount} fastener(s):\n\n" +
                    string.Join("\n", sampleCounts) +
                    (trayLikeCount > 0 ? $"\n\n({trayLikeCount} tray-like element(s) ignored.)" : string.Empty));

                if (confirmation.ShowDialog() != true) return Result.Cancelled;

                // ── 2. The reference tray — always picked manually ───────────────
                List<XYZ> sampleWorldPoints = CollectSampleWorldPoints(doc, sampleElements);

                if (sampleWorldPoints.Count == 0)
                {
                    AvControls.ShowAlert(
                        MG3Shared.DIALOG_TITLE, "Could not read any position from the sample.", AlertSeverity.Error);
                    return Result.Failed;
                }

                XYZ sampleCentroid = new(
                    sampleWorldPoints.Average(p => p.X),
                    sampleWorldPoints.Average(p => p.Y),
                    sampleWorldPoints.Average(p => p.Z));

                Element referenceTray;
                MG3Shared.TrayFrame referenceFrame;
                double referenceLengthMm;
                MG3Shared.LocalExtent referenceExtents;

                const string pickPrompt = "Select the cable tray the sample armour is on, then click to finish.";

                while (true)
                {
                    Reference pickedRef;
                    try
                    {
                        pickedRef = uiDoc.Selection.PickObject(
                            ObjectType.Element, new MG3Shared.TrayElementFilter(), pickPrompt);
                    }
                    catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                    {
                        return Result.Cancelled;
                    }

                    Element? picked = doc.GetElement(pickedRef);

                    if (picked is null)
                    {
                        AvControls.ShowAlert(
                            MG3Shared.DIALOG_TITLE, "The selected tray could not be resolved.", AlertSeverity.Error);
                        return Result.Failed;
                    }

                    if (!MG3Shared.TryGetTrayFrame(picked, out MG3Shared.TrayFrame? pickedFrame, out double pickedLengthMm)
                        || pickedFrame is null)
                    {
                        AvControls.ShowAlert(
                            MG3Shared.DIALOG_TITLE,
                            $"Could not build a frame for the selected tray, or its \"{MG3Shared.PARAM_LENGTH}\" " +
                            "parameter is missing. Select the tray the sample armour is on.",
                            AlertSeverity.Warning);
                        continue;
                    }

                    List<Solid> pickedSolids = MG2.SolidsOf(picked);
                    MG3Shared.LocalExtent pickedExtents =
                        MG3Shared.ComputeLocalExtents(pickedSolids, pickedFrame, pickedLengthMm);

                    // Safety check: does the sample actually sit on THIS tray?
                    // Never continues with one that fails it — a wrong pick
                    // means picking again, not a hard command failure.
                    //
                    // X gets NO tolerance: it must fall strictly within
                    // [-L/2, L/2]. A sample sitting ~35 mm from a joint
                    // between two trays would pass a tolerant X test against
                    // the WRONG neighbouring tray — REFERENCE_TRAY_TOLERANCE_MM
                    // is for the Y/Z cross-section check only.
                    XYZ localCentroid = pickedFrame.ToLocalPoint(sampleCentroid);
                    double halfLengthFt = MG3Shared.Ft(pickedLengthMm / 2.0);
                    double localCentroidYmm = MG3Shared.Mm(localCentroid.Y);
                    double localCentroidZmm = MG3Shared.Mm(localCentroid.Z);

                    bool sampleOnThisTray =
                        localCentroid.X >= -halfLengthFt
                        && localCentroid.X <= halfLengthFt
                        && localCentroidYmm >= pickedExtents.MinYMm - MG3Shared.REFERENCE_TRAY_TOLERANCE_MM
                        && localCentroidYmm <= pickedExtents.MaxYMm + MG3Shared.REFERENCE_TRAY_TOLERANCE_MM
                        && localCentroidZmm >= pickedExtents.MinZMm - MG3Shared.REFERENCE_TRAY_TOLERANCE_MM
                        && localCentroidZmm <= pickedExtents.MaxZMm + MG3Shared.REFERENCE_TRAY_TOLERANCE_MM;

                    if (!sampleOnThisTray)
                    {
                        AvControls.ShowAlert(
                            MG3Shared.DIALOG_TITLE,
                            "The sample doesn't sit on this tray. Select the tray the sample armour is on.",
                            AlertSeverity.Warning);
                        continue;
                    }

                    referenceTray = picked;
                    referenceFrame = pickedFrame;
                    referenceLengthMm = pickedLengthMm;
                    referenceExtents = pickedExtents;
                    break;
                }

                // ── 3. Build the in-memory armour set from the sample ─────────────
                MG3Shared.ArmourSet armourSet = MG3Shared.CaptureSample(doc, referenceFrame, sampleElements);

                if (armourSet.Halves.Count == 0 && armourSet.Fasteners.Count == 0)
                {
                    AvControls.ShowAlert(
                        MG3Shared.DIALOG_TITLE,
                        "Nothing usable was captured from the sample.\n\n" + string.Join("\n", armourSet.Warnings),
                        AlertSeverity.Error);
                    return Result.Failed;
                }

                // referenceExtents was already measured against this same
                // Length while picking the tray, in step 2 — no need to
                // measure it again.
                if (!referenceExtents.LengthMatchesX)
                {
                    AvControls.ShowAlert(
                        MG3Shared.DIALOG_TITLE,
                        "Frame verification failed: the reference tray's own Length does not match its " +
                        "local X extent, measured from its solid geometry.\n\n" +
                        $"Length: {referenceLengthMm:0.##} mm\n" +
                        $"Local X extent: {referenceExtents.MaxXMm - referenceExtents.MinXMm:0.##} mm\n" +
                        $"Local Y extent: {referenceExtents.MaxYMm - referenceExtents.MinYMm:0.##} mm\n" +
                        $"Local Z extent: {referenceExtents.MaxZMm - referenceExtents.MinZMm:0.##} mm\n" +
                        $"Closest-matching axis: {referenceExtents.LengthMatchedAxis}\n\n" +
                        "Nothing was placed.",
                        AlertSeverity.Error);
                    return Result.Failed;
                }

                if (armourSet.SelfCheckMaxPositionDeviationMm > MG3Shared.SAMPLE_SELF_CHECK_MAX_POSITION_DEVIATION_MM
                    || armourSet.SelfCheckMaxAngleDeviationDeg > MG3Shared.SAMPLE_SELF_CHECK_MAX_ANGLE_DEVIATION_DEG)
                {
                    AvControls.ShowAlert(
                        MG3Shared.DIALOG_TITLE,
                        "Self-check failed: the sample could not be reproduced from its own captured data " +
                        "within tolerance.\n\n" +
                        $"Max position deviation: {armourSet.SelfCheckMaxPositionDeviationMm:0.###} mm " +
                        $"(limit {MG3Shared.SAMPLE_SELF_CHECK_MAX_POSITION_DEVIATION_MM:0.##} mm)\n" +
                        $"Max orientation deviation: {armourSet.SelfCheckMaxAngleDeviationDeg:0.###} deg " +
                        $"(limit {MG3Shared.SAMPLE_SELF_CHECK_MAX_ANGLE_DEVIATION_DEG:0.##} deg)\n\n" +
                        "Nothing was placed.",
                        AlertSeverity.Error);
                    return Result.Failed;
                }

                FamilyInstance? referenceInstance = referenceTray as FamilyInstance;
                string referenceTrayFamily = referenceInstance?.Symbol?.Family?.Name ?? "Unknown";
                string referenceTrayType = referenceInstance?.Symbol?.Name ?? "Unknown";

                (double armourCentreMm, double armourThicknessMm) = MG3Shared.ComputeArmourReference(armourSet);

                // ── 4. Target trays — the reference tray is always included ──────
                IList<Reference> targetRefs;
                try
                {
                    targetRefs = uiDoc.Selection.PickObjects(
                        ObjectType.Element,
                        new MG3Shared.TrayElementFilter(),
                        "Select the trays to receive armour (click and box selection). The reference " +
                        "tray is included automatically. Then finish.");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }

                var seenTargets = new HashSet<ElementId>();
                var targetTrayElements = new List<Element>();

                foreach (Reference reference in targetRefs)
                {
                    Element? el = doc.GetElement(reference);
                    if (el is not null && seenTargets.Add(el.Id)) targetTrayElements.Add(el);
                }

                if (seenTargets.Add(referenceTray.Id)) targetTrayElements.Add(referenceTray);

                // ── 5. Validate every target tray ────────────────────────────────
                var validTrays = new List<ValidTray>();
                var skipped = new List<string>();

                foreach (Element tray in targetTrayElements)
                {
                    string label = $"{MG2.LabelOf(tray)} ({tray.Id})";
                    bool isReference = tray.Id == referenceTray.Id;

                    if (isReference)
                    {
                        validTrays.Add(new ValidTray(tray, referenceFrame, referenceLengthMm, true));
                        continue;
                    }

                    if (!MG3Shared.TryGetTrayFrame(tray, out MG3Shared.TrayFrame? trayFrame, out double lengthMm)
                        || trayFrame is null)
                    {
                        skipped.Add(
                            $"{label}: no usable frame could be built, or its \"{MG3Shared.PARAM_LENGTH}\" " +
                            "parameter is missing.");
                        continue;
                    }

                    double tiltDeg = Math.Asin(Math.Clamp(Math.Abs(trayFrame.AxisX.Z), -1.0, 1.0)) * 180.0 / Math.PI;

                    if (tiltDeg > MG3Shared.MAX_HORIZONTAL_TILT_DEG)
                    {
                        skipped.Add(
                            $"{label}: not horizontal — local X tilts {tiltDeg:0.##} deg from level " +
                            $"(limit {MG3Shared.MAX_HORIZONTAL_TILT_DEG:0.##} deg).");
                        continue;
                    }

                    FamilyInstance? candidateInstance = tray as FamilyInstance;
                    string candidateFamily = candidateInstance?.Symbol?.Family?.Name ?? tray.Category?.Name ?? "Unknown";
                    string candidateType = candidateInstance?.Symbol?.Name ?? "Unknown";

                    if (!string.Equals(candidateFamily, referenceTrayFamily, StringComparison.Ordinal)
                        || !string.Equals(candidateType, referenceTrayType, StringComparison.Ordinal))
                    {
                        skipped.Add(
                            $"{label}: type differs from sample — \"{candidateFamily}\" / \"{candidateType}\", " +
                            $"expected \"{referenceTrayFamily}\" / \"{referenceTrayType}\".");
                        continue;
                    }

                    validTrays.Add(new ValidTray(tray, trayFrame, lengthMm, false));
                }

                if (validTrays.Count == 0)
                {
                    AvControls.ShowAlert(
                        MG3Shared.DIALOG_TITLE,
                        "None of the selected trays could be processed.\n\n" + string.Join("\n", skipped),
                        AlertSeverity.Warning);
                    return Result.Failed;
                }

                // ── 6. Spacing / end clearance ────────────────────────────────────
                var dialog = new SpacingInputWindow();
                if (dialog.ShowDialog() != true) return Result.Cancelled;

                double spacingMm = dialog.SpacingMm;
                double clearanceMm = dialog.EndClearanceMm;

                // ── 7. Distribution, per valid tray ───────────────────────────────
                var plans = new List<TrayPlan>();

                foreach (ValidTray validTray in validTrays)
                {
                    if (validTray.IsReferenceTray)
                    {
                        // Anchored to the sample: new sets step out from the
                        // sample's own captured centre, never at it — the
                        // sample stays exactly where it was hand-placed.
                        double halfExtentMm = validTray.LengthMm / 2.0 - clearanceMm - armourThicknessMm / 2.0;
                        bool sampleWithinClearance = Math.Abs(armourCentreMm) <= halfExtentMm;

                        var newCentresMm = new List<double>();

                        for (int k = 1; ; k++)
                        {
                            double x = armourCentreMm + k * spacingMm;
                            if (x > halfExtentMm) break;
                            newCentresMm.Add(x);
                        }

                        for (int k = 1; ; k++)
                        {
                            double x = armourCentreMm - k * spacingMm;
                            if (x < -halfExtentMm) break;
                            newCentresMm.Add(x);
                        }

                        double outermostPositiveMm =
                            newCentresMm.Where(c => c > armourCentreMm).DefaultIfEmpty(armourCentreMm).Max();
                        double outermostNegativeMm =
                            newCentresMm.Where(c => c < armourCentreMm).DefaultIfEmpty(armourCentreMm).Min();

                        double gapPositiveEndMm = validTray.LengthMm / 2.0 - (outermostPositiveMm + armourThicknessMm / 2.0);
                        double gapNegativeEndMm = validTray.LengthMm / 2.0 + (outermostNegativeMm - armourThicknessMm / 2.0);

                        plans.Add(new TrayPlan(validTray, armourCentreMm, armourThicknessMm, newCentresMm)
                        {
                            SetsPositiveSide = newCentresMm.Count(c => c > armourCentreMm),
                            SetsNegativeSide = newCentresMm.Count(c => c < armourCentreMm),
                            GapPositiveEndMm = gapPositiveEndMm,
                            GapNegativeEndMm = gapNegativeEndMm,
                            SampleWithinClearance = sampleWithinClearance,
                        });
                    }
                    else
                    {
                        double usableMm = validTray.LengthMm - 2.0 * clearanceMm - armourThicknessMm;

                        if (usableMm < 0.0)
                        {
                            skipped.Add(
                                $"{MG2.LabelOf(validTray.Tray)} ({validTray.Tray.Id}): insufficient usable " +
                                $"length — L={validTray.LengthMm:0.##} mm, 2c={2.0 * clearanceMm:0.##} mm, " +
                                $"armour thickness={armourThicknessMm:0.##} mm, usable={usableMm:0.##} mm.");
                            continue;
                        }

                        int setCount = (int)Math.Floor(usableMm / spacingMm) + 1;

                        var setCentresMm = new List<double>(setCount);
                        for (int i = 0; i < setCount; i++)
                        {
                            setCentresMm.Add(-(setCount - 1) * spacingMm / 2.0 + i * spacingMm);
                        }

                        double actualEndGapMm = clearanceMm + (usableMm - (setCount - 1) * spacingMm) / 2.0;

                        plans.Add(new TrayPlan(validTray, armourCentreMm, armourThicknessMm, setCentresMm)
                        {
                            ActualEndGapMm = actualEndGapMm,
                        });
                    }
                }

                if (plans.Count == 0)
                {
                    AvControls.ShowAlert(
                        MG3Shared.DIALOG_TITLE,
                        "No tray had enough usable length for even one set.\n\n" + string.Join("\n", skipped),
                        AlertSeverity.Warning);
                    return Result.Failed;
                }

                // ── 8. Placement — one TransactionGroup for the whole run ────────
                var elementIssues = new List<string>();

                TransactionGroup? group = null;
                Transaction? currentTx = null;

                try
                {
                    group = new TransactionGroup(doc, "MG3 Armour Placement");
                    group.Start();

                    foreach (TrayPlan plan in plans)
                    {
                        currentTx = new Transaction(doc, $"MG3 Armour — {MG2.LabelOf(plan.Tray.Tray)}");
                        currentTx.Start();

                        foreach (double setCentreMm in plan.SetCentresMm)
                        {
                            // Fresh per set, per the hard rule: coplanar fasteners
                            // are reused WITHIN a set, not carried across sets or
                            // across trays.
                            var planeCache = new MG3Shared.SketchPlaneCache(MG3Shared.SKETCH_PLANE_COINCIDENCE_MM);

                            int placedInSet = PlaceOneSet(
                                doc, plan.Tray.Frame, armourSet, plan.ArmourCentreMm,
                                setCentreMm, planeCache, elementIssues);

                            if (placedInSet > 0) plan.SetsPlaced++;
                            plan.ElementsPlaced += placedInSet;
                        }

                        currentTx.Commit();
                        currentTx.Dispose();
                        currentTx = null;
                    }

                    group.Assimilate();
                }
                catch (Exception ex)
                {
                    if (currentTx is not null && currentTx.HasStarted() && !currentTx.HasEnded())
                    {
                        currentTx.RollBack();
                    }

                    if (group is not null && group.HasStarted() && !group.HasEnded())
                    {
                        group.RollBack();
                    }

                    AvControls.ShowAlert(
                        MG3Shared.DIALOG_TITLE,
                        $"Placement failed and the whole run was rolled back.\n\n{ex.Message}",
                        AlertSeverity.Error);
                    return Result.Failed;
                }
                finally
                {
                    currentTx?.Dispose();
                    group?.Dispose();
                }

                // ── 9. Report ────────────────────────────────────────────────────
                int traysProcessed = plans.Count(p => p.SetsPlaced > 0);
                int setsPlaced = plans.Sum(p => p.SetsPlaced);
                int elementsPlaced = plans.Sum(p => p.ElementsPlaced);

                AvControls.ShowAlert(
                    MG3Shared.DIALOG_TITLE,
                    BuildRunSummary(
                        traysProcessed, setsPlaced, elementsPlaced, spacingMm, clearanceMm,
                        referenceTray, referenceTrayFamily, referenceTrayType, sampleHalfCount, sampleFastenerCount,
                        plans, skipped, elementIssues, armourSet.Warnings),
                    (skipped.Count > 0 || elementIssues.Count > 0 || armourSet.Warnings.Count > 0)
                        ? AlertSeverity.Warning
                        : AlertSeverity.Success);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AvControls.ShowAlert(
                    MG3Shared.DIALOG_TITLE, $"The command stopped unexpectedly.\n\n{ex.Message}", AlertSeverity.Error);
                return Result.Failed;
            }
        }

        /// <summary>Every world position the sample offers to test tray containment with — every adaptive point, every fastener's origin.</summary>
        private static List<XYZ> CollectSampleWorldPoints(Document doc, List<Element> sampleElements)
        {
            var points = new List<XYZ>();

            foreach (Element el in sampleElements)
            {
                if (el is not FamilyInstance fi) continue;

                if (AdaptiveComponentInstanceUtils.IsAdaptiveComponentInstance(fi))
                {
                    IList<ElementId> pointIds;
                    try
                    {
                        pointIds = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(fi);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (ElementId id in pointIds)
                    {
                        if (doc.GetElement(id) is ReferencePoint rp)
                        {
                            try { points.Add(rp.Position); }
                            catch { }
                        }
                    }
                }
                else
                {
                    try { points.Add(fi.GetTransform().Origin); }
                    catch { }
                }
            }

            return points;
        }

        private static XYZ LocalFt(MG3Shared.Vec3 localMm)
            => new(MG3Shared.Ft(localMm.X), MG3Shared.Ft(localMm.Y), MG3Shared.Ft(localMm.Z));

        private static XYZ OffsetLocal(XYZ localFt, double shiftFt) => localFt + new XYZ(shiftFt, 0.0, 0.0);

        private static Transform WorldCoordinateSystem(
            MG3Shared.TrayFrame frame, MG3Shared.CoordinateSystemData cs, double shiftFt)
        {
            Transform t = Transform.Identity;
            t.Origin = frame.ToWorldPoint(OffsetLocal(LocalFt(cs.OriginLocalMm), shiftFt));
            t.BasisX = frame.ToWorldVector(cs.BasisXLocal.ToXYZ()).Normalize();
            t.BasisY = frame.ToWorldVector(cs.BasisYLocal.ToXYZ()).Normalize();
            t.BasisZ = frame.ToWorldVector(cs.BasisZLocal.ToXYZ()).Normalize();
            return t;
        }

        /// <summary>
        /// Places one full armour set — both halves, every fastener — shifted
        /// from the armour's own captured centre to <paramref name="setCentreMm"/>
        /// on this tray. Never aborts for one element's failure: each half and
        /// each fastener is its own try, logged and skipped on failure.
        /// Returns how many elements were actually created.
        /// </summary>
        private static int PlaceOneSet(
            Document doc,
            MG3Shared.TrayFrame frame,
            MG3Shared.ArmourSet armourSet,
            double armourCentreMm,
            double setCentreMm,
            MG3Shared.SketchPlaneCache planeCache,
            List<string> issues)
        {
            int placed = 0;
            double shiftFt = MG3Shared.Ft(setCentreMm - armourCentreMm);

            foreach (MG3Shared.ArmourHalf half in armourSet.Halves)
            {
                if (PlaceArmourHalf(doc, frame, half, shiftFt, issues)) placed++;
            }

            foreach (MG3Shared.Fastener fastener in armourSet.Fasteners)
            {
                if (PlaceFastener(doc, frame, fastener, shiftFt, planeCache, issues)) placed++;
            }

            return placed;
        }

        /// <summary>
        /// Creates one adaptive half and sets every point's full coordinate
        /// system, then copies Rotate XY/XZ/YZ onto it too — both, per the
        /// locked finding that the instance transform alone cannot see this
        /// family's internal geometry rotation, so the parameter copy is not
        /// optional. A point or parameter that cannot be set is logged and
        /// left as it landed, never retried.
        /// </summary>
        private static bool PlaceArmourHalf(
            Document doc,
            MG3Shared.TrayFrame frame,
            MG3Shared.ArmourHalf half,
            double shiftFt,
            List<string> issues)
        {
            FamilySymbol? symbol = MG3Shared.FindFamilySymbol(doc, half.Family, half.Type);

            if (symbol is null)
            {
                issues.Add($"Armour half \"{half.Type}\": family/type not found in this document — not placed.");
                return false;
            }

            if (!AdaptiveComponentInstanceUtils.IsAdaptiveFamilySymbol(symbol))
            {
                issues.Add($"Armour half \"{half.Type}\": symbol is not an adaptive component family — not placed.");
                return false;
            }

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            FamilyInstance instance;
            try
            {
                instance = AdaptiveComponentInstanceUtils.CreateAdaptiveComponentInstance(doc, symbol);
            }
            catch (Exception ex)
            {
                issues.Add($"Armour half \"{half.Type}\": could not create an instance — {ex.Message}. Not placed.");
                return false;
            }

            IList<ElementId> pointIds =
                AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(instance);
            int settable = Math.Min(pointIds.Count, half.PointCoordinateSystems.Count);

            if (pointIds.Count != half.PointCoordinateSystems.Count)
            {
                issues.Add(
                    $"Armour half \"{half.Type}\" ({instance.Id}): captured " +
                    $"{half.PointCoordinateSystems.Count} coordinate system(s) but the placed instance has " +
                    $"{pointIds.Count} point(s).");
            }

            for (int i = 0; i < settable; i++)
            {
                if (doc.GetElement(pointIds[i]) is not ReferencePoint referencePoint)
                {
                    issues.Add(
                        $"Armour half \"{half.Type}\" ({instance.Id}): point {i} did not resolve to a " +
                        "ReferencePoint.");
                    continue;
                }

                Transform target = WorldCoordinateSystem(frame, half.PointCoordinateSystems[i], shiftFt);

                try
                {
                    referencePoint.SetCoordinateSystem(target);
                }
                catch (Exception ex)
                {
                    issues.Add(
                        $"Armour half \"{half.Type}\" ({instance.Id}): point {i} could not have its " +
                        $"coordinate system set — {ex.Message}.");
                }
            }

            if (half.RotationParams.Found)
            {
                Parameter? paramXy = instance.LookupParameter(MG1.PARAM_ROTATE_XY);
                Parameter? paramXz = instance.LookupParameter(MG1.PARAM_ROTATE_XZ);
                Parameter? paramYz = instance.LookupParameter(MG1.PARAM_ROTATE_YZ);

                if (paramXy is null || paramXz is null || paramYz is null)
                {
                    issues.Add(
                        $"Armour half \"{half.Type}\" ({instance.Id}): Rotate XY/XZ/YZ were captured but not " +
                        "all found on the new instance — not set.");
                }
                else
                {
                    try
                    {
                        paramXy.Set(half.RotationParams.RotateXYRad);
                        paramXz.Set(half.RotationParams.RotateXZRad);
                        paramYz.Set(half.RotationParams.RotateYZRad);
                    }
                    catch (Exception ex)
                    {
                        issues.Add(
                            $"Armour half \"{half.Type}\" ({instance.Id}): could not set Rotate XY/XZ/YZ — " +
                            $"{ex.Message}.");
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Creates one bolt or nut, reference direction from HandOrientationLocal
        /// — the locked finding, verified on all 8 fasteners, FacingOrientationLocal
        /// was 90 degrees off on every one. Reuses the set's SketchPlaneCache for
        /// any other fastener sharing its work plane.
        /// </summary>
        private static bool PlaceFastener(
            Document doc,
            MG3Shared.TrayFrame frame,
            MG3Shared.Fastener fastener,
            double shiftFt,
            MG3Shared.SketchPlaneCache planeCache,
            List<string> issues)
        {
            FamilySymbol? symbol = MG3Shared.FindFamilySymbol(doc, fastener.Family, fastener.Type);

            if (symbol is null)
            {
                issues.Add($"Fastener \"{fastener.Type}\": family/type not found in this document — not placed.");
                return false;
            }

            if (!symbol.IsActive)
            {
                symbol.Activate();
                doc.Regenerate();
            }

            XYZ worldOrigin = frame.ToWorldPoint(OffsetLocal(LocalFt(fastener.OriginLocalMm), shiftFt));

            XYZ worldPlaneOrigin =
                frame.ToWorldPoint(OffsetLocal(LocalFt(fastener.WorkPlaneOriginLocalMm), shiftFt));
            XYZ worldPlaneNormal = frame.ToWorldVector(fastener.WorkPlaneNormalLocal.ToXYZ());
            SketchPlane sketchPlane = planeCache.GetOrCreate(doc, worldPlaneNormal, worldPlaneOrigin);

            XYZ worldReference = frame.ToWorldVector(fastener.HandOrientationLocal.ToXYZ());

            try
            {
                doc.Create.NewFamilyInstance(
                    worldOrigin, symbol, worldReference, sketchPlane, StructuralType.NonStructural);
                return true;
            }
            catch (Exception ex)
            {
                issues.Add($"Fastener \"{fastener.Type}\": placement failed — {ex.Message}. Not placed.");
                return false;
            }
        }

        private static string BuildRunSummary(
            int traysProcessed, int setsPlaced, int elementsPlaced,
            double spacingMm, double clearanceMm,
            Element referenceTray, string referenceTrayFamily, string referenceTrayType,
            int sampleHalfCount, int sampleFastenerCount,
            List<TrayPlan> plans, List<string> skipped, List<string> elementIssues, List<string> captureWarnings)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Sample: {sampleHalfCount} half(s), {sampleFastenerCount} fastener(s)");
            sb.AppendLine(
                $"Reference tray (picked by the user): {MG2.LabelOf(referenceTray)} ({referenceTray.Id}) — " +
                $"\"{referenceTrayFamily}\" / \"{referenceTrayType}\"");
            sb.AppendLine($"Spacing: {spacingMm:0.##} mm, end clearance: {clearanceMm:0.##} mm");
            sb.AppendLine();
            sb.AppendLine($"Trays processed: {traysProcessed}, sets placed: {setsPlaced}, elements placed: {elementsPlaced}");

            sb.AppendLine();
            sb.AppendLine("Per tray:");
            foreach (TrayPlan plan in plans)
            {
                string trayLabel = $"  {MG2.LabelOf(plan.Tray.Tray)} ({plan.Tray.Tray.Id})";

                if (plan.Tray.IsReferenceTray)
                {
                    sb.AppendLine($"{trayLabel} [REFERENCE] — L={plan.Tray.LengthMm:0.##} mm");
                    sb.AppendLine(
                        $"    sample kept in place; {plan.SetsPositiveSide} new set(s) on the positive side, " +
                        $"{plan.SetsNegativeSide} on the negative side");
                    sb.AppendLine(
                        $"    end gaps: positive {plan.GapPositiveEndMm:0.##} mm, " +
                        $"negative {plan.GapNegativeEndMm:0.##} mm");

                    if (plan.SampleWithinClearance == false)
                    {
                        sb.AppendLine(
                            "    NOTE: the sample itself sits closer than the end clearance to one end " +
                            "— kept as placed; only the new sets respect the clearance.");
                    }

                    sb.AppendLine(
                        $"    placed {plan.SetsPlaced}/{plan.SetCentresMm.Count} new set(s), " +
                        $"{plan.ElementsPlaced} element(s)");
                }
                else
                {
                    sb.AppendLine(
                        $"{trayLabel}: L={plan.Tray.LengthMm:0.##} mm, n={plan.SetCentresMm.Count}, " +
                        $"end gap={plan.ActualEndGapMm:0.##} mm, placed {plan.SetsPlaced}/{plan.SetCentresMm.Count} " +
                        $"set(s), {plan.ElementsPlaced} element(s)");
                }
            }

            if (skipped.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Trays skipped:");
                foreach (string reason in skipped) sb.AppendLine($"  - {reason}");
            }

            if (captureWarnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Sample capture warnings:");
                foreach (string warning in captureWarnings) sb.AppendLine($"  - {warning}");
            }

            if (elementIssues.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Element issues:");
                foreach (string issue in elementIssues) sb.AppendLine($"  - {issue}");
            }

            return sb.ToString();
        }
    }
}
