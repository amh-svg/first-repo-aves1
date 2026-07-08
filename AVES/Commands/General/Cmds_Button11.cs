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
        /// <summary>
        /// The actual member elements of this type, IN THIS ASSEMBLY, that are
        /// themselves directly tagged (i.e. their own Id was returned by some
        /// tag's GetTaggedLocalElementIds()) -- empty if none, even when Passed
        /// is true via a same-type element tagged elsewhere in the project.
        /// </summary>
        public List<ElementId> TaggedMembersWithinAssembly = new List<ElementId>();
        public ElementId ExampleUntaggedMemberId = ElementId.InvalidElementId;
    }

    /// <summary>Diagnostic result of a tag scan scoped to a specific set of views.</summary>
    internal class TaggedTypeScanResult
    {
        public HashSet<ElementId> TaggedElementIds = new HashSet<ElementId>();
        public HashSet<ElementId> TaggedTypeIds = new HashSet<ElementId>();
        /// <summary>Reverse lookup: tagged element id -> ids of every tag that tags it.</summary>
        public Dictionary<ElementId, List<ElementId>> TaggingTagsByElement = new Dictionary<ElementId, List<ElementId>>();
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
        /// Every IndependentTag in the document, fetched once and reused across
        /// assemblies (each assembly then filters this down to its own views by
        /// OwnerViewId, rather than re-querying the whole document per assembly).
        /// </summary>
        public static List<IndependentTag> GetAllIndependentTags(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>()
                .ToList();
        }

        /// <summary>
        /// The views generated specifically to document this assembly (3D,
        /// front/section, plan, sheet, etc.) — there is no direct "get all
        /// views for this assembly" API (AssemblyViewUtils only exposes
        /// creation methods), so this scans every View and filters by
        /// IsAssemblyView + AssociatedAssemblyInstanceId == assembly.Id. Note
        /// this is a View-specific property, distinct from the base
        /// Element.AssemblyInstanceId (which means "this element is a member
        /// of assembly X", not "this view documents assembly X").
        /// </summary>
        public static List<View> GetAssemblyOwnViews(Document doc, AssemblyInstance assembly)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v.IsAssemblyView && v.AssociatedAssemblyInstanceId == assembly.Id)
                .ToList();
        }

        /// <summary>
        /// Tagged-element/tagged-type sets built only from tags whose
        /// OwnerViewId falls within the given scope (an assembly's own views),
        /// not project-wide. Orphaned tags (IsOrphaned == true, host/reference
        /// lost) are skipped deterministically up front rather than relying on
        /// GetTaggedLocalElementIds throwing — a caught-and-swallowed exception
        /// was previously the only signal, hiding exactly how many tags were
        /// being dropped and why.
        /// </summary>
        public static TaggedTypeScanResult BuildTaggedTypeIdsForViews(
            Document doc, List<IndependentTag> allTags, HashSet<ElementId> scopeViewIds)
        {
            var result = new TaggedTypeScanResult();
            var scopedTags = allTags.Where(t => scopeViewIds.Contains(t.OwnerViewId)).ToList();
            result.TotalTagsScanned = scopedTags.Count;

            foreach (var tag in scopedTags)
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
                    if (!result.TaggingTagsByElement.TryGetValue(id, out var tagIds))
                    {
                        tagIds = new List<ElementId>();
                        result.TaggingTagsByElement[id] = tagIds;
                    }
                    tagIds.Add(tag.Id);

                    var typeId = el.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                        result.TaggedTypeIds.Add(typeId);
                }
            }

            return result;
        }

        /// <summary>
        /// Scans one assembly: groups its members by family type (dedup level
        /// per the confirmed spec), and reports every distinct family type's
        /// pass/fail status against a tag scan already scoped to that same
        /// assembly's own views. Distinguishes "tagged on this assembly's own
        /// member" from "passed only because some other element of the same
        /// type is tagged in one of the assembly's own views" (both count as
        /// passing, but the latter is worth a second look).
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
                var taggedMembers = kv.Value.Where(el => tagScan.TaggedElementIds.Contains(el.Id)).Select(el => el.Id).ToList();
                var untaggedMember = kv.Value.FirstOrDefault(el => !tagScan.TaggedElementIds.Contains(el.Id));
                var typeElem = doc.GetElement(kv.Key);
                result.Types.Add(new FamilyTypeScanRow
                {
                    TypeId = kv.Key,
                    TypeName = typeElem?.Name ?? "(unknown type)",
                    MemberCountInAssembly = kv.Value.Count,
                    Passed = passed,
                    TaggedMembersWithinAssembly = taggedMembers,
                    ExampleUntaggedMemberId = untaggedMember?.Id ?? ElementId.InvalidElementId
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

                var allTags = TagCompletenessScanner.GetAllIndependentTags(doc);

                var sb = new StringBuilder();
                sb.AppendLine($"View: {view.Name}");
                sb.AppendLine($"Assemblies detected: {assemblies.Count}");
                sb.AppendLine($"IndependentTag elements in document (unscoped): {allTags.Count}");
                sb.AppendLine();

                int totalFailing = 0;
                foreach (var assembly in assemblies.OrderBy(a => a.AssemblyTypeName).ThenBy(a => a.Id.IntegerValue))
                {
                    var assemblyViews = TagCompletenessScanner.GetAssemblyOwnViews(doc, assembly);
                    var assemblyViewIds = new HashSet<ElementId>(assemblyViews.Select(v => v.Id));

                    sb.AppendLine($"Assembly: {assembly.AssemblyTypeName}  [Id {assembly.Id.IntegerValue}]");
                    sb.AppendLine($"  Own views ({assemblyViews.Count}): {string.Join(", ", assemblyViews.Select(v => $"{v.Name} [{v.Id.IntegerValue}]"))}");

                    if (assemblyViews.Count == 0)
                    {
                        sb.AppendLine("  No assembly views found -- cannot determine tag completeness for this assembly (every member type will report FAIL).");
                        sb.AppendLine();
                        continue;
                    }

                    var tagScan = TagCompletenessScanner.BuildTaggedTypeIdsForViews(doc, allTags, assemblyViewIds);
                    sb.AppendLine($"  Tags scoped to these views: {tagScan.TotalTagsScanned}" +
                        $"  |  Orphaned skipped: {tagScan.OrphanedTagsSkipped}" +
                        $"  |  Exceptions: {tagScan.Exceptions.Count}" +
                        $"  |  Distinct tagged types (in-scope): {tagScan.TaggedTypeIds.Count}");
                    foreach (var (tagId, err) in tagScan.Exceptions)
                        sb.AppendLine($"      - Tag Id {tagId.IntegerValue}: {err}");

                    var scan = TagCompletenessScanner.ScanAssembly(doc, assembly, tagScan);
                    totalFailing += scan.Failing.Count;

                    sb.AppendLine($"  Distinct member family types: {scan.DistinctFamilyTypeCount}  |  Failing: {scan.Failing.Count}");
                    foreach (var t in scan.Types.OrderBy(t => t.TypeName))
                    {
                        if (t.TaggedMembersWithinAssembly.Count > 0)
                        {
                            sb.AppendLine($"    [PASS - tagged on this assembly's own member] {t.TypeName}  (members in assembly: {t.MemberCountInAssembly})");
                            foreach (var memberId in t.TaggedMembersWithinAssembly)
                            {
                                var tagIds = tagScan.TaggingTagsByElement.TryGetValue(memberId, out var ids)
                                    ? string.Join(", ", ids.Select(i => i.IntegerValue))
                                    : "(none found)";
                                sb.AppendLine($"        - member Id {memberId.IntegerValue} is tagged by Tag Id(s): {tagIds}");
                            }
                        }
                        else if (t.Passed)
                        {
                            sb.AppendLine($"    [PASS - tagged in an assembly view, but not on this assembly's own member] {t.TypeName}  (members in assembly: {t.MemberCountInAssembly}, " +
                                $"example untagged member Id: {t.ExampleUntaggedMemberId.IntegerValue})");
                        }
                        else
                        {
                            sb.AppendLine($"    [FAIL - untagged in this assembly's own views] {t.TypeName}  (members in assembly: {t.MemberCountInAssembly}, example untagged member Id: {t.ExampleUntaggedMemberId.IntegerValue})");
                        }
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
