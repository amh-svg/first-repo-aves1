using Autodesk.Revit.DB;

namespace AVES.Core
{
    /// <summary>
    /// Shared reader for the "Combo Item" Yes/No shared parameter that marks a
    /// combo/host family (a container for the real pieces). Used by DOC01_FillSchedules
    /// (numbering) and DOC08_AddNested (report counts) so both apply one rule.
    /// </summary>
    internal static class ComboItemHelper
    {
        /// <summary>
        /// True when the element is a combo/host family: its "Combo Item" Yes/No
        /// shared parameter is set to 1. Instance is checked first, then type; the
        /// first one that exists and holds a value decides. Real pieces leave it
        /// empty, and an element without the parameter is a normal piece.
        /// Name lookup is correct here: it is a shared parameter, not a built-in.
        /// </summary>
        public static bool IsComboItem(Document doc, Element el)
        {
            var onInstance = el.LookupParameter("Combo Item");
            if (HasComboValue(onInstance)) return onInstance!.AsInteger() == 1;

            var symbol = el is FamilyInstance fi ? fi.Symbol as Element : null;
            var typeEl = symbol ?? doc.GetElement(el.GetTypeId());
            var onType = typeEl?.LookupParameter("Combo Item");
            return HasComboValue(onType) && onType!.AsInteger() == 1;
        }

        private static bool HasComboValue(Parameter? p)
            => p != null && p.StorageType == StorageType.Integer && p.HasValue;
    }
}
