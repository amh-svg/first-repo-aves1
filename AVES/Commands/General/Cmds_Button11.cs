// ─────────────────────────────────────────────────────────────────────────────
//  Cmds_Button11.cs
//  Assembly Tag Completeness Checker.
//
//  DIAGNOSTIC STAGE (not yet the final tool): this file currently contains
//  only the core detection algorithm plus a temporary trace-dump Execute(),
//  so it can be run once in Revit against a known assembly (with known
//  tagged/untagged members) to confirm the detection + dedup-by-family-type
//  logic before the view picker and WPF report window are built around it.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace AVES.Cmds_Button11
{
    /// <summary>One failing family type within one assembly.</summary>
    internal class FailingFamilyType
    {
        public ElementId TypeId = ElementId.InvalidElementId;
        public string TypeName = "";
        public int MemberCount;
        public ElementId ExampleUntaggedElementId = ElementId.InvalidElementId;
    }

    /// <summary>One assembly's scan result within one view.</summary>
    internal class AssemblyScanResult
    {
        public AssemblyInstance Assembly = null!;
        public int DistinctFamilyTypeCount;
        public List<FailingFamilyType> Failing = new List<FailingFamilyType>();
    }

    internal static class TagCompletenessScanner
    {
        /// <summary>
        /// Assemblies whose member elements are visible in the given view, per
        /// the confirmed detection approach: collect elements the view's own
        /// FilteredElementCollector would show, then group by AssemblyInstanceId.
        /// </summary>
        public static List<AssemblyInstance> GetAssembliesInView(Document doc, View view)
        {
            var elems = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .ToElements();

            var assemblyIds = new HashSet<ElementId>();
            foreach (var el in elems)
            {
                var aid = el.AssemblyInstanceId;
                if (aid != ElementId.InvalidElementId)
                    assemblyIds.Add(aid);
            }

            return assemblyIds
                .Select(id => doc.GetElement(id) as AssemblyInstance)
                .Where(a => a != null)
                .Select(a => a!)
                .ToList();
        }

        /// <summary>
        /// Project-wide set of type ids that have at least one tagged instance
        /// anywhere in the document, built from a single pass over every
        /// IndependentTag (scope is project-wide, not limited to a view or an
        /// assembly, per the confirmed spec).
        /// </summary>
        public static HashSet<ElementId> BuildProjectWideTaggedTypeIds(Document doc)
        {
            var taggedTypeIds = new HashSet<ElementId>();
            var tags = new FilteredElementCollector(doc)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>();

            foreach (var tag in tags)
            {
                ICollection<ElementId> taggedIds;
                try { taggedIds = tag.GetTaggedLocalElementIds(); }
                catch { continue; }

                foreach (var id in taggedIds)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    var typeId = el.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                        taggedTypeIds.Add(typeId);
                }
            }

            return taggedTypeIds;
        }

        /// <summary>
        /// Scans one assembly: groups its members by family type (dedup level
        /// per the confirmed spec), and reports which distinct family types have
        /// zero tagged instances anywhere in the project.
        /// </summary>
        public static AssemblyScanResult ScanAssembly(Document doc, AssemblyInstance assembly, HashSet<ElementId> taggedTypeIds)
        {
            var membersByType = new Dictionary<ElementId, List<Element>>();
            foreach (var id in assembly.GetMemberIds())
            {
                var el = doc.GetElement(id);
                if (el == null) continue;
                var typeId = el.GetTypeId();
                if (typeId == ElementId.InvalidElementId) continue;

                if (!membersByType.TryGetValue(typeId, out var list))
                {
                    list = new List<Element>();
                    membersByType[typeId] = list;
                }
                list.Add(el);
            }

            var result = new AssemblyScanResult
            {
                Assembly = assembly,
                DistinctFamilyTypeCount = membersByType.Count
            };

            foreach (var kv in membersByType)
            {
                if (taggedTypeIds.Contains(kv.Key)) continue; // passes: tagged somewhere

                var typeElem = doc.GetElement(kv.Key);
                result.Failing.Add(new FailingFamilyType
                {
                    TypeId = kv.Key,
                    TypeName = typeElem?.Name ?? "(unknown type)",
                    MemberCount = kv.Value.Count,
                    ExampleUntaggedElementId = kv.Value[0].Id
                });
            }

            return result;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  TEMPORARY DIAGNOSTIC COMMAND
    //  Runs the scan against the active view only and dumps a full trace via
    //  TaskDialog, so the algorithm can be manually verified against a known
    //  assembly before the real view-picker + report-window UI is built.
    // ─────────────────────────────────────────────────────────────────────────

    [Transaction(TransactionMode.Manual)]
    public class Cmd_Button : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            var doc = uiDoc.Document;

            try
            {
                var view = doc.ActiveView;
                if (view == null)
                {
                    TaskDialog.Show("Tag Completeness — Diagnostic", "No active view.");
                    return Result.Cancelled;
                }

                var assemblies = TagCompletenessScanner.GetAssembliesInView(doc, view);
                if (assemblies.Count == 0)
                {
                    TaskDialog.Show("Tag Completeness — Diagnostic",
                        $"View: {view.Name}\n\nNo assemblies detected in this view " +
                        "(no visible element has a non-invalid AssemblyInstanceId).");
                    return Result.Succeeded;
                }

                var taggedTypeIds = TagCompletenessScanner.BuildProjectWideTaggedTypeIds(doc);

                var sb = new StringBuilder();
                sb.AppendLine($"View: {view.Name}");
                sb.AppendLine($"Assemblies detected: {assemblies.Count}");
                sb.AppendLine($"Project-wide tagged type count: {taggedTypeIds.Count}");
                sb.AppendLine();

                int totalFailing = 0;
                foreach (var assembly in assemblies.OrderBy(a => a.AssemblyTypeName).ThenBy(a => a.Id.IntegerValue))
                {
                    var scan = TagCompletenessScanner.ScanAssembly(doc, assembly, taggedTypeIds);
                    totalFailing += scan.Failing.Count;

                    sb.AppendLine($"Assembly: {assembly.AssemblyTypeName}  [Id {assembly.Id.IntegerValue}]");
                    sb.AppendLine($"  Distinct member family types: {scan.DistinctFamilyTypeCount}");
                    sb.AppendLine($"  Failing (untagged anywhere): {scan.Failing.Count}");
                    foreach (var f in scan.Failing)
                    {
                        sb.AppendLine($"    - {f.TypeName}  (members in assembly: {f.MemberCount}, example untagged Id: {f.ExampleUntaggedElementId.IntegerValue})");
                    }
                    sb.AppendLine();
                }

                var dialog = new TaskDialog("Tag Completeness — Diagnostic")
                {
                    MainInstruction = $"{assemblies.Count} assembl{(assemblies.Count == 1 ? "y" : "ies")} scanned, {totalFailing} failing family type(s) total",
                    MainContent = "Full per-assembly trace is in the details below. Compare this against your known tagged/untagged members before the real UI is built.",
                    ExpandedContent = sb.ToString()
                };
                dialog.Show();

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Tag Completeness — Diagnostic — Error", ex.ToString());
                return Result.Failed;
            }
        }
    }
}
