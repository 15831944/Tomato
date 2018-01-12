﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LS.MapClean.Addin.Algorithms;
using LS.MapClean.Addin.Utils;

namespace LS.MapClean.Addin.MapClean
{
    public class BreakCrossingObjectsAction : MapCleanActionBase
    {
        public BreakCrossingObjectsAction(Document document)
            : base(document)
        {
        }

        public override ActionType ActionType
        {
            get { return ActionType.BreakCrossing; }
        }

        public override bool Hasparameters
        {
            get { return false; }
        }

        protected override IEnumerable<CheckResult> CheckImpl(IEnumerable<ObjectId> selectedObjectIds)
        {
            //var results = new List<CrossingCheckResult>();
            //var editor = Document.Editor;
            //var crossingObjects = new BreakCrossingObjects(editor, Tolerance);
            //crossingObjects.Check();
            //if (crossingObjects.CrossingPoints == null)
            //    return results;

            //foreach (var crossingPoint in crossingObjects.CrossingPoints)
            //{
            //    var checkResult = new CrossingCheckResult(crossingPoint);
            //    results.Add(checkResult);
            //}

            //return results;

            var editor = Document.Editor;
            var algorithm = new BreakCrossingObjectsBsp(editor, Tolerance);
            algorithm.Check(selectedObjectIds);
            var result = new List<CrossingCheckResult>();
            foreach (var crossingInfo in algorithm.CrossingInfos)
            {
                var checkResult = new CrossingCheckResult(crossingInfo);
                result.Add(checkResult);
            }
            return result;
        }

        protected override Status FixImpl(CheckResult checkResult, out List<ObjectId> resultIds)
        {
            resultIds = new List<ObjectId>();
            var crossingCheckResult = checkResult as CrossingCheckResult;
            if (crossingCheckResult == null)
                return Status.Rejected;

            var crossingInfo = crossingCheckResult.CrossingInfo;
            var distinctIds = new HashSet<ObjectId>();
            distinctIds.Add(crossingInfo.SourceId);
            distinctIds.Add(crossingInfo.TargetId);

            using (var transaction = Document.Database.TransactionManager.StartTransaction())
            {
                //// distinctIds == 1说明是自交线
                bool isSelfIntersection = (distinctIds.Count == 1);

                var modelSpace = (BlockTableRecord)transaction.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(Document.Database), OpenMode.ForWrite);
                foreach (var sourceId in distinctIds)
                {
                    var sourceCurve = transaction.GetObject(sourceId, OpenMode.ForWrite) as Curve;
                    DBObjectCollection allSplitCurves = null;
                    if (isSelfIntersection)
                    {
                        allSplitCurves = CurveUtils.SplitSelfIntersectCurve(sourceCurve, crossingInfo.IntersectPoints, transaction);
                    }
                    else // Use CurveUtils.SplitCurve take less time.
                    {
                        allSplitCurves = CurveUtils.SplitCurve(sourceCurve, crossingInfo.IntersectPoints);
                    }

                    // The splitted curves has the same layer with original curve, 
                    // so we needn't set its layer explicitly.
                    foreach (Curve splitCurve in allSplitCurves)
                    {
                        var curveId = modelSpace.AppendEntity(splitCurve);
                        transaction.AddNewlyCreatedDBObject(splitCurve, true);
                        // Add splited curve to resultIds.
                        resultIds.Add(curveId);
                    }

                    if (allSplitCurves.Count > 0)
                    {
                        // Erase the old one
                        sourceCurve.Erase();
                    }
                }
                transaction.Commit();
            }
            return Status.Fixed;
        }

        protected override bool CheckAndFixAllImpl(IEnumerable<ObjectId> ids)
        {
            var editor = Document.Editor;
            editor.WriteMessage("\n开始检查交叉对象...");
            var algorithm = new BreakCrossingObjectsBsp(editor, 0.0);
            algorithm.Check(ids);
            var count = algorithm.CrossingInfos.Count();
            if (count == 0)
            {
                editor.WriteMessage("\n检测到0处交叉，无需修复\n");
                return true;
            }

            var message = String.Format("\n检测到{0}处交叉，是否打断？", count);

            if (!AcadPromptUtil.AskContinue(message, editor))
                return true;

            editor.WriteMessage("\n开始打断...");
            algorithm.Fix(eraseOld:true);
            editor.WriteMessage("\n打断所有交叉线成功！\n");
            return true;
        }
    }
}
