// -----------------------------------------------------------------------
// Title: Mekano Common
// Summary: Helpers shared by the Mekano commands (MEK01, MEK02, MEK03):
//          millimetre/feet conversion, solid extraction and labelling of
//          arbitrary elements, and reading a parameter as text.
// Scope:   Moved here unchanged from MEK02Shared, where MEK03 was already
//          borrowing them, and from the per-command unit helpers. Nothing in
//          here knows about bolts, trays or channels.
// -----------------------------------------------------------------------
// Autodesk
using Autodesk.Revit.DB;

namespace AVES.Commands
{
    internal static class MekanoCommon
    {
        // ── Geometry constants ─────────────────────────────────────────────────

        // Solids below this volume are noise, not geometry.
        internal const double MIN_SOLID_VOLUME = 1e-9;

        // Nested families can sit several GeometryInstance levels down.
        internal const int MAX_NESTING_DEPTH = 8;

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

        // ── Parameters ─────────────────────────────────────────────────────────

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
    }
}
