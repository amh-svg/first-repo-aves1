// Autodesk
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
// AVES
using AVES.Cmds_PullDownCreateSheets;
// System
using System.Collections.Generic;
using System.Linq;
using System.Text;

// TEMPORARY -- constructs a genuinely unstamped Vendor package for testing
// Stage 3's name-matching fallback (Step B) against. No package in this
// project currently lacks a ViewKey stamp (AssemblyOrientationStorage.Stamp
// has run unconditionally in Build() since well before this Vendor re-run
// work started), so Step B has never actually executed -- only Step A has.
// This tool removes the stamp from a selected sheet's 2D views so the next
// re-run of that package genuinely has to fall back to name matching.
// COMMITS, does not roll back -- the change must persist for the next re-run
// to see it. Remove this file and its ribbon button (Application.cs)
// alongside Cmd_DiagViewportState once Step B is confirmed.
namespace AVES.Cmds_DiagStripViewKeyStamp;

[Transaction(TransactionMode.Manual)]
public sealed class Cmd_DiagStripViewKeyStamp : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var uiDoc = commandData.Application.ActiveUIDocument;
        var doc = uiDoc.Document;

        var sheet = uiDoc.Selection.GetElementIds()
            .Select(id => doc.GetElement(id))
            .OfType<ViewSheet>()
            .FirstOrDefault();

        if (sheet is null)
        {
            TaskDialog.Show("AVES Diagnostic: Strip ViewKey Stamp",
                "Select a sheet first -- its row in the Project Browser, or its title block on " +
                "the sheet itself -- then run this command.");
            return Result.Succeeded;
        }

        var touched = new List<string>();

        using (var tx = new Transaction(doc, "AVES diagnostic: strip ViewKey stamp"))
        {
            tx.Start();

            foreach (var vpId in sheet.GetAllViewports())
            {
                if (doc.GetElement(vpId) is not Viewport viewport) continue;
                if (doc.GetElement(viewport.ViewId) is not View view) continue;
                if (!AssemblyOrientationStorage.TryRead(view, out _, out _, out var key)) continue;

                AssemblyOrientationStorage.ClearStamp(view);
                touched.Add($"\"{view.Name}\" (view Id={view.Id}, was key \"{key}\")");
            }

            tx.Commit();
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Sheet: {sheet.SheetNumber} - {sheet.Name}");
        sb.AppendLine("This change is COMMITTED, not rolled back -- it will still be there on " +
                      "your next re-run.");
        sb.AppendLine();

        if (touched.Count == 0)
        {
            sb.AppendLine("No viewport on this sheet pointed to a view carrying the " +
                          "AssemblyOrientationStorage stamp -- nothing changed.");
        }
        else
        {
            sb.AppendLine($"Stripped the stamp from {touched.Count} view(s):");
            foreach (var t in touched) sb.AppendLine($"  {t}");
        }

        TaskDialog.Show("AVES Diagnostic: Strip ViewKey Stamp", sb.ToString());
        return Result.Succeeded;
    }
}
