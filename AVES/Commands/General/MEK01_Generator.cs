// -----------------------------------------------------------------------
// Title: Mekano Generator
// Summary: Lets the user pick one existing Starter adaptive component and
//          generates a Mekano adaptive component offset from it, tilted to
//          follow the Starter's own orientation.
// Key logic: The Starter's rotation parameters compose as Rz(XY) * Rx(YZ)
//            * Ry(XZ), confirmed empirically against solid geometry. That
//            matrix rotates the local placement offset into world space and
//            is composed with the Mekano's fixed vertical pose to give the
//            target orientation, from which the Mekano's own three rotation
//            parameters are extracted. All three are Angle-type parameters
//            stored in radians.
// -----------------------------------------------------------------------
// .NET
using System;
using System.Collections.Generic;
using System.Linq;
// Autodesk
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
// AVES
using AVES.Core;
using static AVES.Commands.MekanoCommon;

// Associate with pushbutton commands
namespace AVES.Commands
{
    /// <summary>
    /// Shared configuration and rotation helpers for the Mekano generator.
    /// </summary>
    internal static class MEK01Shared
    {
        // ── Families and parameters ────────────────────────────────────────────

        internal const string STARTER_FAMILY_NAME = "93441 _ Starter Bracket CH100-2T";
        internal const string MEKANO_FAMILY_NAME = "1372182 _ Mekano Channel 50-2T-2";

        internal const string PARAM_ROTATE_XY = "Rotate XY";
        internal const string PARAM_ROTATE_XZ = "Rotate XZ";
        internal const string PARAM_ROTATE_YZ = "Rotate YZ";

        // ── Tolerances ─────────────────────────────────────────────────────────

        // How close |sin(YZ)| must get to 1 before the XY/XZ split becomes
        // undetermined and the gimbal-locked branch takes over.
        internal const double GIMBAL_TOLERANCE = 1e-6;

        internal const string DIALOG_TITLE = "Mekano Generator";

        // ── Selection filter ───────────────────────────────────────────────────

        /// <summary>
        /// Restricts picking to family instances of the Starter family.
        /// </summary>
        internal sealed class StarterFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem)
            {
                return elem is FamilyInstance fi
                    && string.Equals(
                        fi.Symbol?.Family?.Name,
                        STARTER_FAMILY_NAME,
                        StringComparison.Ordinal);
            }

            public bool AllowReference(Reference reference, XYZ position) => false;
        }

        // ── Reference point access ─────────────────────────────────────────────

        /// <summary>
        /// Resolves the single adaptive placement point of an instance.
        /// Reports the actual count when it is not exactly one, rather than guessing.
        /// </summary>
        internal static bool TryGetSinglePlacementPoint(
            Document doc,
            FamilyInstance instance,
            out ReferencePoint? point,
            out string error)
        {
            point = null;
            error = string.Empty;

            IList<ElementId> pointIds =
                AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(instance);

            int count = pointIds?.Count ?? 0;

            if (count != 1)
            {
                error = $"expected exactly 1 adaptive placement point but found {count}.";
                return false;
            }

            if (doc.GetElement(pointIds![0]) is not ReferencePoint resolved)
            {
                error = "its adaptive placement point could not be resolved to a reference point.";
                return false;
            }

            point = resolved;
            return true;
        }

        // ── Rotation parameters ────────────────────────────────────────────────

        /// <summary>
        /// Reads the three rotation parameters in radians, which is how Revit
        /// stores Angle-type parameters internally. Names the specific missing
        /// parameter on failure.
        /// </summary>
        internal static bool TryReadRotations(
            FamilyInstance instance,
            out double rotateXy,
            out double rotateXz,
            out double rotateYz,
            out string error)
        {
            rotateXy = rotateXz = rotateYz = 0.0;
            error = string.Empty;

            Parameter? paramXy = instance.LookupParameter(PARAM_ROTATE_XY);
            Parameter? paramXz = instance.LookupParameter(PARAM_ROTATE_XZ);
            Parameter? paramYz = instance.LookupParameter(PARAM_ROTATE_YZ);

            string? missing =
                paramXy is null ? PARAM_ROTATE_XY :
                paramXz is null ? PARAM_ROTATE_XZ :
                paramYz is null ? PARAM_ROTATE_YZ : null;

            if (missing is not null)
            {
                error = $"the parameter \"{missing}\" was not found.";
                return false;
            }

            rotateXy = paramXy!.AsDouble();
            rotateXz = paramXz!.AsDouble();
            rotateYz = paramYz!.AsDouble();
            return true;
        }

        // ── Rotation composition and extraction ────────────────────────────────

        /// <summary>
        /// Builds the rotation the three parameters describe, as Rz(XY) * Rx(YZ)
        /// * Ry(XZ). In application order that is XZ first, then YZ, then XY.
        /// Angles are in radians.
        /// </summary>
        internal static Transform Compose(double rotateXy, double rotateXz, double rotateYz)
        {
            return Transform.CreateRotation(XYZ.BasisZ, rotateXy)
                .Multiply(Transform.CreateRotation(XYZ.BasisX, rotateYz))
                .Multiply(Transform.CreateRotation(XYZ.BasisY, rotateXz));
        }

        /// <summary>
        /// Recovers the three parameter values from a rotation built by Compose,
        /// inverting that composition. Angles come back in radians.
        ///
        /// Working the product out by hand gives, in row-major terms,
        ///   R[2][1] =  sin(YZ)
        ///   R[0][1] = -sin(XY)cos(YZ),  R[1][1] = cos(XY)cos(YZ)
        ///   R[2][0] = -cos(YZ)sin(XZ),  R[2][2] = cos(YZ)cos(XZ)
        /// Revit's BasisX/Y/Z are the matrix columns, so R[2][0] is BasisX.Z,
        /// not BasisZ.X.
        ///
        /// When cos(YZ) reaches zero the second and third rows collapse and only
        /// the sum of XY and XZ survives, so the caller is told the split was
        /// gimbal locked.
        /// </summary>
        internal static void ExtractRotations(
            Transform rotation,
            out double rotateXy,
            out double rotateXz,
            out double rotateYz,
            out bool gimbalLocked)
        {
            double sinYz = Math.Max(-1.0, Math.Min(1.0, rotation.BasisY.Z));

            rotateYz = Math.Asin(sinYz);
            gimbalLocked = Math.Abs(sinYz) > 1.0 - GIMBAL_TOLERANCE;

            if (gimbalLocked)
            {
                // cos(YZ) is zero, so XY and XZ are not separable. Pin XZ at zero
                // and fold the whole turn into XY. That is the standard resolution,
                // and it reproduces the vertical pose exactly for an unrotated
                // Starter.
                rotateXz = 0.0;
                rotateXy = Math.Atan2(rotation.BasisX.Y, rotation.BasisX.X);
                return;
            }

            rotateXy = Math.Atan2(-rotation.BasisY.X, rotation.BasisY.Y);
            rotateXz = Math.Atan2(-rotation.BasisX.Z, rotation.BasisZ.Z);
        }
    }

    /// <summary>
    /// Places a Mekano adaptive component offset from the adaptive placement
    /// point of a user-picked Starter adaptive component. Both the offset
    /// direction and the Mekano's orientation follow the Starter's own rotation,
    /// composed with the Mekano's fixed vertical pose.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class Cmd_MEK01_Generator : IExternalCommand
    {
        // ── Configuration ──────────────────────────────────────────────────────

        // The Mekano's vertical pose, in degrees, expressed in the same parameter
        // convention as the Starter's. This is composed with the Starter's own
        // rotation rather than being written out directly, so it is not what the
        // new instance ends up carrying.
        private const double VERTICAL_POSE_XY_DEG = 90.0;
        private const double VERTICAL_POSE_XZ_DEG = 0.0;
        private const double VERTICAL_POSE_YZ_DEG = 90.0;

        // Offset from the Starter's adaptive point to the Mekano's, in millimetres,
        // expressed in the Starter's own local frame and rotated into world
        // coordinates through the Starter's orientation.
        private const double OFFSET_X_MM = 48.0;
        private const double OFFSET_Y_MM = 48.0;
        private const double OFFSET_Z_MM = 72.5;

        // ── Unit helpers ───────────────────────────────────────────────────────

        /// <summary>Degrees to radians, the internal unit for Angle parameters.</summary>
        private static double Rad(double degrees)
            => UnitUtils.ConvertToInternalUnits(degrees, UnitTypeId.Degrees);

        /// <summary>Radians back to degrees, formatted for the success message.</summary>
        private static string Deg(double radians)
            => UnitUtils.ConvertFromInternalUnits(radians, UnitTypeId.Degrees).ToString("0.###");

        // ── Command ────────────────────────────────────────────────────────────

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiDoc = commandData.Application.ActiveUIDocument;

            if (uiDoc is null)
            {
                AvControls.ShowAlert(
                    MEK01Shared.DIALOG_TITLE,
                    "No active document.\n\nOpen a project and re-run.",
                    AlertSeverity.Warning);
                return Result.Cancelled;
            }

            var doc = uiDoc.Document;

            try
            {
                // ── 1. Pick the Starter instance ───────────────────────────────
                Reference pickedRef;

                try
                {
                    pickedRef = uiDoc.Selection.PickObject(
                        ObjectType.Element,
                        new MEK01Shared.StarterFilter(),
                        $"Select a \"{MEK01Shared.STARTER_FAMILY_NAME}\" instance.");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    // User pressed Escape. Not an error.
                    return Result.Cancelled;
                }

                if (doc.GetElement(pickedRef) is not FamilyInstance starter)
                {
                    AvControls.ShowAlert(
                        MEK01Shared.DIALOG_TITLE,
                        "The picked element is not a family instance.\n\nPick a Starter instance and re-run.",
                        AlertSeverity.Warning);
                    return Result.Cancelled;
                }

                // ── 2. Validate the Starter instance ───────────────────────────
                var starterFamilyName = starter.Symbol?.Family?.Name ?? "(unknown)";

                if (!string.Equals(starterFamilyName, MEK01Shared.STARTER_FAMILY_NAME, StringComparison.Ordinal))
                {
                    AvControls.ShowAlert(
                        MEK01Shared.DIALOG_TITLE,
                        $"The picked element belongs to family \"{starterFamilyName}\".\n\n" +
                        $"Expected \"{MEK01Shared.STARTER_FAMILY_NAME}\".",
                        AlertSeverity.Warning);
                    return Result.Cancelled;
                }

                if (!AdaptiveComponentInstanceUtils.IsAdaptiveComponentInstance(starter))
                {
                    AvControls.ShowAlert(
                        MEK01Shared.DIALOG_TITLE,
                        $"\"{starterFamilyName}\" is not an adaptive component instance.\n\n" +
                        "This command only works with adaptive components.",
                        AlertSeverity.Warning);
                    return Result.Cancelled;
                }

                // ── 3. Read the Starter placement point ────────────────────────
                if (!MEK01Shared.TryGetSinglePlacementPoint(doc, starter, out ReferencePoint? starterPoint, out string pointError))
                {
                    AvControls.ShowAlert(MEK01Shared.DIALOG_TITLE, $"Starter instance: {pointError}", AlertSeverity.Error);
                    return Result.Failed;
                }

                XYZ starterPosition = starterPoint!.Position;

                // ── 4. Build the Starter's rotation ────────────────────────────
                if (!MEK01Shared.TryReadRotations(
                        starter,
                        out double starterXy, out double starterXz, out double starterYz,
                        out string rotationError))
                {
                    AvControls.ShowAlert(MEK01Shared.DIALOG_TITLE, $"Starter instance: {rotationError}", AlertSeverity.Error);
                    return Result.Failed;
                }

                Transform starterRotation = MEK01Shared.Compose(starterXy, starterXz, starterYz);

                // ── 5. Rotate the offset into world coordinates ────────────────
                var localOffset = new XYZ(
                    Ft(OFFSET_X_MM),
                    Ft(OFFSET_Y_MM),
                    Ft(OFFSET_Z_MM));

                XYZ worldOffset = starterRotation.OfVector(localOffset);
                XYZ mekanoPosition = starterPosition + worldOffset;

                // ── 6. Compose the Mekano's target orientation ─────────────────
                Transform verticalPose = MEK01Shared.Compose(
                    Rad(VERTICAL_POSE_XY_DEG),
                    Rad(VERTICAL_POSE_XZ_DEG),
                    Rad(VERTICAL_POSE_YZ_DEG));

                Transform targetRotation = starterRotation.Multiply(verticalPose);

                MEK01Shared.ExtractRotations(
                    targetRotation,
                    out double targetXy, out double targetXz, out double targetYz,
                    out bool gimbalLocked);

                // ── 7. Find the Mekano symbol ──────────────────────────────────
                var mekanoSymbol = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilySymbol))
                    .Cast<FamilySymbol>()
                    .FirstOrDefault(s => string.Equals(s.Family?.Name, MEK01Shared.MEKANO_FAMILY_NAME, StringComparison.Ordinal));

                if (mekanoSymbol is null)
                {
                    AvControls.ShowAlert(
                        MEK01Shared.DIALOG_TITLE,
                        $"The family \"{MEK01Shared.MEKANO_FAMILY_NAME}\" is not loaded in this document.\n\n" +
                        "Load it and re-run.",
                        AlertSeverity.Error);
                    return Result.Failed;
                }

                if (!AdaptiveComponentInstanceUtils.IsAdaptiveFamilySymbol(mekanoSymbol))
                {
                    AvControls.ShowAlert(
                        MEK01Shared.DIALOG_TITLE,
                        $"\"{MEK01Shared.MEKANO_FAMILY_NAME}\" is not an adaptive family.\n\n" +
                        "This command can only place adaptive components.",
                        AlertSeverity.Error);
                    return Result.Failed;
                }

                // ── 8. Create and align the Mekano instance ────────────────────
                using (var tx = new Transaction(doc, "Generate Mekano"))
                {
                    try
                    {
                        tx.Start();

                        if (!mekanoSymbol.IsActive)
                        {
                            mekanoSymbol.Activate();
                            doc.Regenerate();
                        }

                        FamilyInstance mekano =
                            AdaptiveComponentInstanceUtils.CreateAdaptiveComponentInstance(doc, mekanoSymbol);

                        doc.Regenerate();

                        if (!MEK01Shared.TryGetSinglePlacementPoint(doc, mekano, out ReferencePoint? mekanoPoint, out string mekanoPointError))
                        {
                            if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                            AvControls.ShowAlert(MEK01Shared.DIALOG_TITLE, $"New Mekano instance: {mekanoPointError}", AlertSeverity.Error);
                            return Result.Failed;
                        }

                        mekanoPoint!.Position = mekanoPosition;
                        doc.Regenerate();

                        Parameter? targetXyParam = mekano.LookupParameter(MEK01Shared.PARAM_ROTATE_XY);
                        Parameter? targetXzParam = mekano.LookupParameter(MEK01Shared.PARAM_ROTATE_XZ);
                        Parameter? targetYzParam = mekano.LookupParameter(MEK01Shared.PARAM_ROTATE_YZ);

                        string? missing =
                            targetXyParam is null ? MEK01Shared.PARAM_ROTATE_XY :
                            targetXzParam is null ? MEK01Shared.PARAM_ROTATE_XZ :
                            targetYzParam is null ? MEK01Shared.PARAM_ROTATE_YZ : null;

                        if (missing is not null)
                        {
                            if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                            AvControls.ShowAlert(
                                MEK01Shared.DIALOG_TITLE,
                                $"New Mekano instance: the parameter \"{missing}\" was not found.",
                                AlertSeverity.Error);
                            return Result.Failed;
                        }

                        // Already radians, straight from the extraction.
                        targetXyParam!.Set(targetXy);
                        targetXzParam!.Set(targetXz);
                        targetYzParam!.Set(targetYz);

                        doc.Regenerate();

                        tx.Commit();
                    }
                    catch (Exception txEx)
                    {
                        if (tx.HasStarted() && !tx.HasEnded()) tx.RollBack();
                        AvControls.ShowAlert(
                            MEK01Shared.DIALOG_TITLE,
                            $"The Mekano instance could not be generated.\n\n{txEx.Message}",
                            AlertSeverity.Error);
                        return Result.Failed;
                    }
                }

                // ── 9. Report success ──────────────────────────────────────────
                AvControls.ShowAlert(
                    MEK01Shared.DIALOG_TITLE,
                    $"A \"{MEK01Shared.MEKANO_FAMILY_NAME}\" instance was generated.\n\n" +
                    $"Starter rotation: XY {Deg(starterXy)}, XZ {Deg(starterXz)}, YZ {Deg(starterYz)}\n" +
                    $"Mekano rotation:  XY {Deg(targetXy)}, XZ {Deg(targetXz)}, YZ {Deg(targetYz)}" +
                    (gimbalLocked
                        ? "\n\nThe target orientation sits at the YZ singularity, so XZ was pinned " +
                          "at 0 and the turn folded into XY. The resulting orientation is exact."
                        : string.Empty),
                    AlertSeverity.Success);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                AvControls.ShowAlert(
                    MEK01Shared.DIALOG_TITLE,
                    $"The command stopped unexpectedly.\n\n{ex.Message}",
                    AlertSeverity.Error);
                return Result.Failed;
            }
        }
    }
}
