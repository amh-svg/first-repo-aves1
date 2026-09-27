// Autodesk
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
// System
using System.Linq;
using System.Text;

// TEMPORARY, read-only -- reports the currently selected Viewport's exact box
// centre, rotation and label state, so a before/after comparison across a
// real Vendor or Korea re-run is a number comparison, not an eyeballed one.
// Select a viewport's border on a sheet, then run this. Remove with
// Application.cs's matching ribbon button once Stage 2 is confirmed.
namespace AVES.Cmds_DiagViewportState;

[Transaction(TransactionMode.ReadOnly)]
public sealed class Cmd_DiagViewportState : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        var viewport = uiDoc.Selection.GetElementIds()
            .Select(id => doc.GetElement(id))
            .OfType<Viewport>()
            .FirstOrDefault();

        if (viewport is null)
        {
            TaskDialog.Show("AVES Diagnostic: Viewport State",
                "Select a viewport's border on a sheet first, then run this command.");
            return Result.Succeeded;
        }

        var view = doc.GetElement(viewport.ViewId) as View;
        var sheet = doc.GetElement(viewport.SheetId) as ViewSheet;

        var sb = new StringBuilder();
        sb.AppendLine($"Sheet: {sheet?.SheetNumber} - {sheet?.Name}");
        sb.AppendLine($"View: \"{view?.Name}\" (Id={viewport.ViewId})");
        sb.AppendLine($"Viewport Id: {viewport.Id}  Type: {doc.GetElement(viewport.GetTypeId())?.Name}");
        sb.AppendLine($"Box centre: {F(viewport.GetBoxCenter())}");
        sb.AppendLine($"Rotation: {viewport.Rotation}");
        sb.AppendLine($"LabelOffset: {F(viewport.LabelOffset)}");
        sb.AppendLine($"LabelLineLength: {viewport.LabelLineLength:0.######}");

        TaskDialog.Show("AVES Diagnostic: Viewport State", sb.ToString());
        return Result.Succeeded;
    }

    private static string F(XYZ p) => $"({p.X:0.####}, {p.Y:0.####}, {p.Z:0.####})";
}
