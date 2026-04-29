// Autodesk
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AVES.Cmds_Button
{
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
                const double LEADER_LENGTH = 0.75;
                const double MIN_TAG_SPACING = 0.1;
                const double RADIANS_45 = Math.PI / 4.0;

                var DIAGONAL_DIRECTIONS = new List<XYZ>()
                {
                    new XYZ(1, 1, 0).Normalize(),    // 45°
                    new XYZ(-1, 1, 0).Normalize(),   // 135°  
                    new XYZ(-1, -1, 0).Normalize(),  // 225°
                    new XYZ(1, -1, 0).Normalize()    // 315°
                };

                List<XYZ> placedTagPositions = new List<XYZ>();

                int assembliesProcessed = 0;
                int viewsFound = 0;
                int elementsWithComments = 0;
                int groupsFound = 0;
                string lastError = "";

                double GetRotationAngle(int directionIndex)
                {
                    int normalizedIndex = directionIndex % DIAGONAL_DIRECTIONS.Count;
                    return RADIANS_45 + (normalizedIndex * Math.PI / 2.0);
                }

                XYZ ResolveTagCollision(XYZ candidate, XYZ center, int dirIndex)
                {
                    // No collision resolution - tags are pre-spaced at different distances
                    // Just return the candidate position unchanged
                    return candidate;
                }

                XYZ GetElementCenter(Element el, View view)
                {
                    try
                    {
                        var bbox = el.get_BoundingBox(view);
                        if (bbox != null)
                            return (bbox.Min + bbox.Max) * 0.5;

                        if (el.Location is LocationPoint lp)
                            return lp.Point;

                        if (el.Location is LocationCurve lc)
                            return lc.Curve.Evaluate(0.5, true);
                    }
                    catch { }

                    return null;
                }

                XYZ CalculateDiagonalPosition(XYZ center, int index)
                {
                    var dir = DIAGONAL_DIRECTIONS[index % DIAGONAL_DIRECTIONS.Count];
                    return new XYZ(
                        center.X + dir.X * LEADER_LENGTH,
                        center.Y + dir.Y * LEADER_LENGTH,
                        center.Z
                    );
                }

                /// <summary>
                /// Find closest point on element geometry to the TAG POSITION
                /// </summary>
                XYZ FindClosestPoint(Element el, XYZ target, View view)
                {
                    try
                    {
                        Options opt = new Options
                        {
                            View = view,
                            ComputeReferences = true,
                            IncludeNonVisibleObjects = false
                        };

                        var geo = el.get_Geometry(opt);
                        if (geo == null) return null;

                        XYZ best = null;
                        double min = double.MaxValue;

                        void CheckPoint(XYZ p)
                        {
                            if (p == null) return;
                            double d = p.DistanceTo(target);
                            if (d < min)
                            {
                                min = d;
                                best = p;
                            }
                        }

                        foreach (var g in geo)
                        {
                            IEnumerable<GeometryObject> objects;

                            if (g is GeometryInstance gi)
                                objects = gi.GetInstanceGeometry().Cast<GeometryObject>();
                            else
                                objects = new List<GeometryObject> { g };

                            foreach (var obj in objects)
                            {
                                if (obj is Solid s)
                                {
                                    foreach (Face f in s.Faces)
                                    {
                                        try
                                        {
                                            var proj = f.Project(target);
                                            if (proj != null) CheckPoint(proj.XYZPoint);
                                        }
                                        catch { }
                                    }
                                }
                                else if (obj is Face f)
                                {
                                    try
                                    {
                                        var proj = f.Project(target);
                                        if (proj != null) CheckPoint(proj.XYZPoint);
                                    }
                                    catch { }
                                }
                                else if (obj is Curve c)
                                {
                                    try
                                    {
                                        CheckPoint(c.GetEndPoint(0));
                                        CheckPoint(c.GetEndPoint(1));
                                        CheckPoint(c.Evaluate(0.5, true));
                                    }
                                    catch { }
                                }
                            }
                        }

                        return best;
                    }
                    catch { }

                    return null;
                }

                /// <summary>
                /// FIXED: Get a proper taggable reference and the face reference for leader
                /// Returns tuple of (taggable reference, face reference for leader)
                /// </summary>
                Tuple<Reference, Reference> GetTaggableReference(Element el, View view)
                {
                    try
                    {
                        Options opt = new Options
                        {
                            ComputeReferences = true,
                            View = view,
                            IncludeNonVisibleObjects = false
                        };

                        var geo = el.get_Geometry(opt);
                        Reference faceRef = null;

                        if (geo != null)
                        {
                            foreach (var g in geo)
                            {
                                if (g is Solid solid && solid.Faces.Size > 0)
                                {
                                    foreach (Face face in solid.Faces)
                                    {
                                        if (face.Reference != null)
                                        {
                                            faceRef = face.Reference;
                                            break;
                                        }
                                    }
                                    if (faceRef != null) break;
                                }

                                if (g is GeometryInstance gi)
                                {
                                    var instGeo = gi.GetInstanceGeometry();
                                    foreach (var ig in instGeo)
                                    {
                                        if (ig is Solid solid2 && solid2.Faces.Size > 0)
                                        {
                                            foreach (Face face in solid2.Faces)
                                            {
                                                if (face.Reference != null)
                                                {
                                                    faceRef = face.Reference;
                                                    break;
                                                }
                                            }
                                            if (faceRef != null) break;
                                        }
                                    }
                                    if (faceRef != null) break;
                                }
                            }
                        }

                        // The element itself is the taggable reference
                        Reference elementRef = new Reference(el);

                        // Return both: element reference for tagging, face reference for leader
                        return new Tuple<Reference, Reference>(elementRef, faceRef ?? elementRef);
                    }
                    catch
                    {
                        Reference elementRef = new Reference(el);
                        return new Tuple<Reference, Reference>(elementRef, elementRef);
                    }
                }

                /// <summary>
                /// Find the "Mechanical Equipment Tag" family symbol
                /// </summary>
                ElementId FindMechanicalEquipmentTag()
                {
                    try
                    {
                        var tagSymbol = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .Cast<FamilySymbol>()
                            .FirstOrDefault(fs =>
                                fs.FamilyName.Equals("Mechanical Equipment Tag", StringComparison.OrdinalIgnoreCase) ||
                                fs.Family.Name.Equals("Mechanical Equipment Tag", StringComparison.OrdinalIgnoreCase));

                        if (tagSymbol != null)
                        {
                            if (!tagSymbol.IsActive)
                            {
                                using (Transaction activateTx = new Transaction(doc, "Activate Tag Symbol"))
                                {
                                    activateTx.Start();
                                    tagSymbol.Activate();
                                    activateTx.Commit();
                                }
                            }
                            return tagSymbol.Id;
                        }

                        tagSymbol = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilySymbol))
                            .Cast<FamilySymbol>()
                            .FirstOrDefault(fs =>
                                fs.FamilyName.Contains("Mechanical") &&
                                fs.FamilyName.Contains("Tag"));

                        if (tagSymbol != null)
                        {
                            if (!tagSymbol.IsActive)
                            {
                                using (Transaction activateTx = new Transaction(doc, "Activate Tag Symbol"))
                                {
                                    activateTx.Start();
                                    tagSymbol.Activate();
                                    activateTx.Commit();
                                }
                            }
                            return tagSymbol.Id;
                        }

                        return null;
                    }
                    catch
                    {
                        return null;
                    }
                }

                List<string> NaturalSort(IEnumerable<string> input)
                {
                    var digitPattern = new Regex(@"(\d+)");

                    return input
                        .OrderBy(s =>
                            digitPattern.Split(s)
                                .Select(x => int.TryParse(x, out int n) ? (object)n : x.ToLower())
                                .ToArray(),
                            Comparer<object[]>.Create((a, b) =>
                            {
                                int minLength = Math.Min(a.Length, b.Length);
                                for (int i = 0; i < minLength; i++)
                                {
                                    int comparison = Comparer<object>.Default.Compare(a[i], b[i]);
                                    if (comparison != 0) return comparison;
                                }
                                return a.Length.CompareTo(b.Length);
                            }))
                        .ToList();
                }

                // ========== MAIN EXECUTION ==========

                var assemblies = new FilteredElementCollector(doc)
                    .OfClass(typeof(AssemblyInstance))
                    .Cast<AssemblyInstance>()
                    .ToList();

                if (!assemblies.Any())
                {
                    TaskDialog.Show("Info", "No assemblies found in the project.");
                    return Result.Succeeded;
                }

                var mechanicalTagId = FindMechanicalEquipmentTag();
                if (mechanicalTagId == null || mechanicalTagId == ElementId.InvalidElementId)
                {
                    TaskDialog.Show("Error", "Could not find 'Mechanical Equipment Tag' family in the project.\n\nPlease load this family and try again.");
                    return Result.Succeeded;
                }

                int totalTagsCreated = 0;
                int failedTags = 0;

                using (TransactionGroup tg = new TransactionGroup(doc, "Tag All Assemblies - 45° Leaders"))
                {
                    tg.Start();

                    foreach (var assembly in assemblies)
                    {
                        try
                        {
                            assembliesProcessed++;
                            placedTagPositions.Clear();

                            var views3d = new FilteredElementCollector(doc)
                                .OfClass(typeof(View3D))
                                .Cast<View3D>()
                                .Where(v => !v.IsTemplate)
                                .ToList();

                            View3D view = null;

                            if (!string.IsNullOrEmpty(assembly.AssemblyTypeName))
                            {
                                view = views3d.FirstOrDefault(v => v.Name.Contains(assembly.AssemblyTypeName));
                            }

                            if (view == null)
                            {
                                view = views3d.FirstOrDefault(v => v.Name.Contains(assembly.Id.IntegerValue.ToString()));
                            }

                            if (view == null && doc.ActiveView is View3D activeView3d && !activeView3d.IsTemplate)
                            {
                                view = activeView3d;
                            }

                            if (view == null)
                            {
                                continue;
                            }

                            viewsFound++;

                            if (!view.IsLocked && !view.IsPerspective)
                            {
                                using (Transaction tLock = new Transaction(doc, "Lock 3D View"))
                                {
                                    tLock.Start();
                                    try { view.SaveOrientationAndLock(); }
                                    catch { }
                                    tLock.Commit();
                                }
                            }

                            var groupedByComment = new Dictionary<string, List<ElementId>>();

                            foreach (var memberId in assembly.GetMemberIds())
                            {
                                var element = doc.GetElement(memberId);
                                if (element == null) continue;

                                var commentParam = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)
                                                ?? element.LookupParameter("Comments");

                                if (commentParam != null && commentParam.HasValue)
                                {
                                    var commentValue = commentParam.AsString();
                                    if (!string.IsNullOrEmpty(commentValue))
                                    {
                                        elementsWithComments++;
                                        if (!groupedByComment.ContainsKey(commentValue))
                                            groupedByComment[commentValue] = new List<ElementId>();
                                        groupedByComment[commentValue].Add(memberId);
                                    }
                                }
                            }

                            if (!groupedByComment.Any())
                            {
                                continue;
                            }

                            groupsFound += groupedByComment.Count;

                            var sortedComments = NaturalSort(groupedByComment.Keys);

                            using (Transaction t = new Transaction(doc, $"Tag Assembly {assembly.Id.IntegerValue}"))
                            {
                                t.Start();

                                int tagIndex = 0; // Unique index for each tag created

                                for (int i = 0; i < sortedComments.Count; i++)
                                {
                                    try
                                    {
                                        var element = doc.GetElement(groupedByComment[sortedComments[i]].First());
                                        if (element == null)
                                        {
                                            failedTags++;
                                            continue;
                                        }

                                        if (element.IsHidden(view))
                                        {
                                            failedTags++;
                                            continue;
                                        }

                                        var elementCenter = GetElementCenter(element, view);
                                        if (elementCenter == null)
                                        {
                                            failedTags++;
                                            continue;
                                        }

                                        // Use tagIndex for direction and distance calculation
                                        int directionIndex = tagIndex % DIAGONAL_DIRECTIONS.Count;  // Which 45° angle (0-3)
                                        int distanceRing = tagIndex / DIAGONAL_DIRECTIONS.Count;     // How far out (0, 1, 2, ...)

                                        var dir = DIAGONAL_DIRECTIONS[directionIndex];
                                        double distance = LEADER_LENGTH * (1.0 + distanceRing * 0.4);  // Larger spacing for 50 tags

                                        XYZ initialTagPos = new XYZ(
                                            elementCenter.X + dir.X * distance,
                                            elementCenter.Y + dir.Y * distance,
                                            elementCenter.Z
                                        );

                                        // No collision resolution needed - positions are pre-calculated
                                        var finalTagPos = initialTagPos;

                                        // Get both taggable reference and face reference
                                        var references = GetTaggableReference(element, view);
                                        Reference taggableRef = references.Item1;
                                        Reference faceRef = references.Item2;

                                        // Create tag with TAGGABLE reference
                                        var tag = IndependentTag.Create(
                                            doc,
                                            view.Id,
                                            taggableRef,
                                            true,
                                            TagMode.TM_ADDBY_CATEGORY,
                                            TagOrientation.Horizontal,
                                            finalTagPos);

                                        if (tag == null)
                                        {
                                            failedTags++;
                                            continue;
                                        }

                                        try
                                        {
                                            // Change to Mechanical Equipment Tag type
                                            tag.ChangeTypeId(mechanicalTagId);

                                            // Set tag head position
                                            tag.TagHeadPosition = finalTagPos;

                                            // Configure leader
                                            tag.LeaderEndCondition = LeaderEndCondition.Free;

                                            // Find closest point on element to tag position
                                            var closestPoint = FindClosestPoint(element, finalTagPos, view) ?? elementCenter;

                                            // Set leader using face reference
                                            try
                                            {
                                                tag.SetLeaderEnd(faceRef, closestPoint);
                                            }
                                            catch
                                            {
                                                // Fallback to taggable reference
                                                tag.SetLeaderEnd(taggableRef, closestPoint);
                                            }

                                            // Tag head stays at 0° - no rotation applied
                                            // Leaders naturally angle at ~45° due to diagonal tag positioning

                                            placedTagPositions.Add(finalTagPos);
                                            totalTagsCreated++;
                                            tagIndex++; // Increment for next tag
                                        }
                                        catch (Exception ex)
                                        {
                                            failedTags++;
                                            lastError = $"Tag configuration failed: {ex.Message}";
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        failedTags++;
                                        lastError = $"Inner loop exception: {ex.Message}";
                                    }
                                }

                                t.Commit();
                            }
                        }
                        catch (Exception ex)
                        {
                            lastError = $"Assembly loop exception: {ex.Message}";
                        }
                    }

                    tg.Assimilate();
                }

                string diagnosticMsg = $"RESULTS:\n" +
                    $"Tags created: {totalTagsCreated}\n" +
                    $"Failed: {failedTags}\n\n" +
                    $"DIAGNOSTICS:\n" +
                    $"Assemblies found: {assemblies.Count}\n" +
                    $"Assemblies processed: {assembliesProcessed}\n" +
                    $"Views found: {viewsFound}\n" +
                    $"Elements with comments: {elementsWithComments}\n" +
                    $"Groups found: {groupsFound}\n\n" +
                    (string.IsNullOrEmpty(lastError) ? "" : $"Last error: {lastError}");

                TaskDialog.Show("Tagging Complete", diagnosticMsg);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", $"Unexpected error: {ex.Message}\n\nStack trace:\n{ex.StackTrace}");
                return Result.Failed;
            }
        }
    }
}