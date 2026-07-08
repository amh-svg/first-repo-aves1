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
    /// <summary>One distinct member family type within one assembly, pass or fail.</summary>
    internal class FamilyTypeScanRow
    {
        public ElementId TypeId = ElementId.InvalidElementId;
        public string TypeName = "";
        public int MemberCountInAssembly;
        public bool Passed;
        /// <summary>True if at least one of THIS assembly's own members of this type is tagged (as opposed to only some other, unrelated element of the same type elsewhere in the project).</summary>
        public bool PassedWithinAssembly;
        public ElementId ExampleElementId = ElementId.InvalidElementId;
    }

    /// <summary>Diagnostic result of the project-wide tag scan.</summary>
    internal class TaggedTypeScanResult
    {
        public HashSet<ElementId> TaggedElementIds = new HashSet<ElementId>();
        public HashSet<ElementId> TaggedTypeIds = new HashSet<ElementId>();
        public int TotalTagsScanned;
        public int OrphanedTagsSkipped;
        public List<(ElementId TagId, string Error)> Exceptions = new List<(ElementId, string)>();
    }

    /// <summary>One assembly's scan result within one view.</summary>
    internal class AssemblyScanResult
    {
        public AssemblyInstance Assembly = null!;
        public List<FamilyTypeScanRow> Types = new List<FamilyTypeScanRow>();
        public int DistinctFamilyTypeCount => Types.Count;
        public List<FamilyTypeScanRow> Failing => Types.Where(t => !t.Passed).ToList();
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
        ///
        /// Orphaned tags (IsOrphaned == true, host/reference lost) are skipped
        /// deterministically up front rather than relying on GetTaggedLocalElementIds
        /// throwing — a caught-and-swallowed exception here was previously the
        /// only signal, which hid exactly how many tags were being dropped and
        /// why, making the resulting set look flaky run to run.
        /// </summary>
        public static TaggedTypeScanResult BuildProjectWideTaggedTypeIds(Document doc)
        {
            var result = new TaggedTypeScanResult();
            var tags = new FilteredElementCollector(doc)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();

            result.TotalTagsScanned = tags.Count;

            foreach (var tag in tags)
            {
                if (tag.IsOrphaned)
                {
                    result.OrphanedTagsSkipped++;
                    continue;
                }

                ICollection<ElementId> taggedIds;
                try
                {
                    taggedIds = tag.GetTaggedLocalElementIds();
                }
                catch (Exception ex)
                {
                    result.Exceptions.Add((tag.Id, ex.Message));
                    continue;
                }

                foreach (var id in taggedIds)
                {
                    var el = doc.GetElement(id);
                    if (el == null) continue;
                    result.TaggedElementIds.Add(id);
                    var typeId = el.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                        result.TaggedTypeIds.Add(typeId);
                }
            }

            return result;
        }

        /// <summary>
        /// Scans one assembly: groups its members by family type (dedup level
        /// per the confirmed spec), and reports every distinct family type with
        /// its pass/fail status, distinguishing "tagged within this assembly"
        /// from "passed only because some other element of the same type is
        /// tagged elsewhere in the project" (both count as passing per spec).
        /// </summary>
        public static AssemblyScanResult ScanAssembly(Document doc, AssemblyInstance assembly, TaggedTypeScanResult tagScan)
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

            var result = new AssemblyScanResult { Assembly = assembly };

            foreach (var kv in membersByType)
            {
                bool passed = tagScan.TaggedTypeIds.Contains(kv.Key);
                bool passedWithinAssembly = kv.Value.Any(el => tagScan.TaggedElementIds.Contains(el.Id));
                var typeElem = doc.GetElement(kv.Key);
                result.Types.Add(new FamilyTypeScanRow
                {
                    TypeId = kv.Key,
                    TypeName = typeElem?.Name ?? "(unknown type)",
                    MemberCountInAssembly = kv.Value.Count,
                    Passed = passed,
                    PassedWithinAssembly = passedWithinAssembly,
                    ExampleElementId = kv.Value[0].Id
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

                var tagScan = TagCompletenessScanner.BuildProjectWideTaggedTypeIds(doc);

                var sb = new StringBuilder();
                sb.AppendLine($"View: {view.Name}");
                sb.AppendLine($"Assemblies detected: {assemblies.Count}");
                sb.AppendLine();
                sb.AppendLine("--- Project-wide tag scan ---");
                sb.AppendLine($"IndependentTag elements found: {tagScan.TotalTagsScanned}");
                sb.AppendLine($"Orphaned tags skipped (IsOrphaned): {tagScan.OrphanedTagsSkipped}");
                sb.AppendLine($"Tags that threw resolving GetTaggedLocalElementIds: {tagScan.Exceptions.Count}");
                foreach (var (tagId, err) in tagScan.Exceptions)
                    sb.AppendLine($"    - Tag Id {tagId.IntegerValue}: {err}");
                sb.AppendLine($"Distinct tagged type count: {tagScan.TaggedTypeIds.Count}");
                sb.AppendLine();

                int totalFailing = 0;
                foreach (var assembly in assemblies.OrderBy(a => a.AssemblyTypeName).ThenBy(a => a.Id.IntegerValue))
                {
                    var scan = TagCompletenessScanner.ScanAssembly(doc, assembly, tagScan);
                    totalFailing += scan.Failing.Count;

                    sb.AppendLine($"Assembly: {assembly.AssemblyTypeName}  [Id {assembly.Id.IntegerValue}]");
                    sb.AppendLine($"  Distinct member family types: {scan.DistinctFamilyTypeCount}  |  Failing: {scan.Failing.Count}");
                    foreach (var t in scan.Types.OrderBy(t => t.TypeName))
                    {
                        string status = t.Passed
                            ? (t.PassedWithinAssembly ? "PASS (tagged within this assembly)" : "PASS (tagged elsewhere in project only)")
                            : "FAIL (untagged anywhere)";
                        sb.AppendLine($"    [{status}] {t.TypeName}  (members in assembly: {t.MemberCountInAssembly}, example element Id: {t.ExampleElementId.IntegerValue})");
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
