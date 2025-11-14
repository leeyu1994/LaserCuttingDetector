using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CadDataExtractor
{
    /// <summary>
    /// CAD数据提取器 - 针对当前文档进行数据提取
    /// </summary>
    public class CADDataExtractor
    {
        /// <summary>
        /// 数据记录结构
        /// </summary>
        public class CADDataRecord
        {
            public string 部件ID { get; set; }
            public string 对象类型 { get; set; }
            public string 对象句柄 { get; set; }
            public double 起点X { get; set; }
            public double 起点Y { get; set; }
            public double 终点X { get; set; }
            public double 终点Y { get; set; }
            public double 圆心X { get; set; }
            public double 圆心Y { get; set; }
            public double 半径 { get; set; }
            public double 起始角度 { get; set; }
            public double 总角度 { get; set; }
        }

        /// <summary>
        /// 从当前文档提取数据
        /// </summary>
        public bool ExtractDataFromCurrentDocument(Document doc, string outputCsvPath,
                    List<string> selectedLayers, Point3d? userOrigin = null, double unitScale = 1.0)
        {
            if (doc == null || selectedLayers == null || selectedLayers.Count == 0) return false;

            var extractedData = new List<CADDataRecord>();
            Database db = doc.Database;

            using (var trans = db.TransactionManager.StartTransaction())
            {
                var targetLayersSet = new HashSet<string>(selectedLayers, StringComparer.OrdinalIgnoreCase);

                PrepareLayers(db, trans, targetLayersSet);
                ProcessComplexObjects(db, trans, targetLayersSet);

                var basicEntities = GetAllBasicEntities(db, trans, targetLayersSet);
                if (basicEntities.Count == 0)
                {
                    trans.Abort();
                    return WriteToCSV(extractedData, outputCsvPath);
                }

                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (var info in basicEntities)
                {
                    GetEntityBounds(info.Entity, ref minX, ref minY, ref maxX, ref maxY);
                }

                bool hasValidBounds = minX != double.MaxValue;
                Point3d origin = userOrigin.HasValue ? userOrigin.Value : (hasValidBounds ? new Point3d(minX, minY, 0) : Point3d.Origin);
                double autoFitScale = 1.0;
                Vector3d centeringOffset = Vector3d.ZAxis;

                if (!userOrigin.HasValue && hasValidBounds)
                {
                    double sourceWidth = maxX - minX;
                    double sourceHeight = maxY - minY;
                    const double targetWidth = 215.0;
                    const double targetHeight = 290.0;

                    if (sourceWidth > 1e-6 && sourceHeight > 1e-6)
                    {
                        double scaleX = targetWidth / sourceWidth;
                        double scaleY = targetHeight / sourceHeight;
                        autoFitScale = Math.Min(scaleX, scaleY);

                        double scaledWidth = sourceWidth * autoFitScale;
                        double scaledHeight = sourceHeight * autoFitScale;

                        double offsetX = (targetWidth - scaledWidth) / 2.0;
                        double offsetY = (targetHeight - scaledHeight) / 2.0;
                        centeringOffset = new Vector3d(offsetX, offsetY, 0);
                    }
                }

                double totalScale = unitScale * autoFitScale;

                foreach (var entityInfo in basicEntities)
                {
                    var record = ProcessEntity(entityInfo.Entity, entityInfo.LayerName, origin, totalScale, centeringOffset);
                    if (record != null)
                    {
                        extractedData.Add(record);
                    }
                }

                foreach (var entityInfo in basicEntities)
                {
                    if (!entityInfo.Entity.ObjectId.IsValid)
                    {
                        entityInfo.Entity.Dispose();
                    }
                }

                trans.Abort();
            }

            return WriteToCSV(extractedData, outputCsvPath);
        }

        /// <summary>
        /// 【编译错误修正】辅助方法：获取单个实体的边界并更新总边界
        /// </summary>
        private void GetEntityBounds(Entity entity, ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            // 使用 GeometricExtents 属性是获取实体精确边界框的最可靠、最标准的方法。
            // 这个属性由 AutoCAD 内部计算，能正确处理所有类型的几何体，包括圆弧的精确边界。
            if (entity.Bounds.HasValue) // 首先检查边界是否存在
            {
                Extents3d extents = entity.GeometricExtents;
                Point3d minPoint = extents.MinPoint;
                Point3d maxPoint = extents.MaxPoint;

                minX = Math.Min(minX, minPoint.X);
                minY = Math.Min(minY, minPoint.Y);
                maxX = Math.Max(maxX, maxPoint.X);
                maxY = Math.Max(maxY, maxPoint.Y);
            }
        }

        private void PrepareLayers(Database db, Transaction trans, HashSet<string> targetLayers)
        {
            var layerTable = (LayerTable)trans.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId layerId in layerTable)
            {
                var layer = (LayerTableRecord)trans.GetObject(layerId, OpenMode.ForRead);
                if (targetLayers.Contains(layer.Name))
                {
                    if (layer.IsOff || layer.IsFrozen || layer.IsLocked)
                    {
                        layer.UpgradeOpen();
                        layer.IsOff = false;
                        layer.IsFrozen = false;
                        layer.IsLocked = false;
                        layer.DowngradeOpen();
                    }
                }
            }
        }

        private void ProcessComplexObjects(Database db, Transaction trans, HashSet<string> targetLayers)
        {
            var modelSpace = (BlockTableRecord)trans.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            bool changedInLoop;

            do
            {
                changedInLoop = false;
                var objectsToProcess = new List<ObjectId>();

                foreach (ObjectId objId in modelSpace)
                {
                    var entity = trans.GetObject(objId, OpenMode.ForRead) as Entity;
                    if (entity == null || entity.IsErased || !targetLayers.Contains(entity.Layer)) continue;

                    if (entity is BlockReference || entity is MInsertBlock || entity is Spline)
                    {
                        if (entity is BlockReference blockRef)
                        {
                            var blockDef = trans.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                            if (blockDef != null && (blockDef.IsFromExternalReference || blockDef.IsFromOverlayReference))
                            {
                                continue;
                            }
                        }
                        objectsToProcess.Add(objId);
                    }
                }

                if (objectsToProcess.Count > 0)
                {
                    changedInLoop = true;
                    modelSpace.UpgradeOpen();
                    foreach (var entityId in objectsToProcess)
                    {
                        var entity = trans.GetObject(entityId, OpenMode.ForWrite) as Entity;
                        if (entity == null || entity.IsErased) continue;

                        if (entity is Spline spline)
                        {
                            var polyline = ConvertSplineToPolyline(spline);
                            if (polyline != null)
                            {
                                polyline.Layer = spline.Layer;
                                modelSpace.AppendEntity(polyline);
                                trans.AddNewlyCreatedDBObject(polyline, true);
                            }
                        }
                        else
                        {
                            var explodedEntities = new DBObjectCollection();
                            entity.Explode(explodedEntities);

                            foreach (Entity explodedEntity in explodedEntities)
                            {
                                explodedEntity.Layer = entity.Layer;
                                modelSpace.AppendEntity(explodedEntity);
                                trans.AddNewlyCreatedDBObject(explodedEntity, true);
                            }
                        }
                        entity.Erase();
                    }
                    modelSpace.DowngradeOpen();
                }
            } while (changedInLoop);
        }

        private Polyline ConvertSplineToPolyline(Spline spline, int precision = 20)
        {
            var polyline = new Polyline();
            double startParam = spline.StartParam;
            double endParam = spline.EndParam;
            double step = (endParam - startParam) / precision;

            for (int i = 0; i <= precision; i++)
            {
                double param = startParam + i * step;
                Point3d pt = spline.GetPointAtParameter(param > endParam ? endParam : param);
                polyline.AddVertexAt(i, new Point2d(pt.X, pt.Y), 0, 0, 0);
            }
            return polyline;
        }

        private class EntityInfo
        {
            public Entity Entity { get; set; }
            public string LayerName { get; set; }
        }

        private List<EntityInfo> GetAllBasicEntities(Database db, Transaction trans, HashSet<string> targetLayers)
        {
            var result = new List<EntityInfo>();
            var modelSpace = (BlockTableRecord)trans.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

            foreach (ObjectId objId in modelSpace)
            {
                var entity = trans.GetObject(objId, OpenMode.ForRead) as Entity;
                if (entity == null || entity.IsErased || !targetLayers.Contains(entity.Layer)) continue;

                if (entity is Line || entity is Arc || entity is Circle)
                {
                    result.Add(new EntityInfo { Entity = entity, LayerName = entity.Layer });
                }
                else if (entity is Curve curve && (curve is Polyline || curve is Polyline2d))
                {
                    var explodedObjects = new DBObjectCollection();
                    curve.Explode(explodedObjects);

                    foreach (Entity explodedEntity in explodedObjects)
                    {
                        if (explodedEntity is Line || explodedEntity is Arc)
                        {
                            explodedEntity.Layer = entity.Layer;
                            result.Add(new EntityInfo { Entity = explodedEntity, LayerName = entity.Layer });
                        }
                        else
                        {
                            explodedEntity.Dispose();
                        }
                    }
                }
            }
            return result;
        }

        private CADDataRecord ProcessEntity(Entity entity, string layerName, Point3d origin, double totalScale, Vector3d offset)
        {
            var record = new CADDataRecord { 部件ID = layerName, 对象句柄 = entity.Handle.ToString() };

            Point3d TransformPoint(Point3d point)
            {
                var translatedPoint = point - origin.GetAsVector();
                var scaledPoint = translatedPoint * totalScale;
                return scaledPoint + offset;
            }

            switch (entity)
            {
                case Line line:
                    record.对象类型 = "LINE";
                    var startPt = TransformPoint(line.StartPoint);
                    var endPt = TransformPoint(line.EndPoint);
                    record.起点X = Math.Round(startPt.X, 6); record.起点Y = Math.Round(startPt.Y, 6);
                    record.终点X = Math.Round(endPt.X, 6); record.终点Y = Math.Round(endPt.Y, 6);
                    break;
                case Arc arc:
                    record.对象类型 = "ARC";
                    var arcCenter = TransformPoint(arc.Center);
                    record.圆心X = Math.Round(arcCenter.X, 6); record.圆心Y = Math.Round(arcCenter.Y, 6);
                    record.半径 = Math.Round(arc.Radius * totalScale, 6);

                    Vector3d startVec = arc.StartPoint - arc.Center;
                    Vector3d endVec = arc.EndPoint - arc.Center;
                    double startAngleRad = Math.Atan2(startVec.Y, startVec.X);
                    double endAngleRad = Math.Atan2(endVec.Y, endVec.X);

                    double startAngleDeg = startAngleRad * 180.0 / Math.PI;
                    double endAngleDeg = endAngleRad * 180.0 / Math.PI;
                    while (startAngleDeg < 0) startAngleDeg += 360.0;
                    while (endAngleDeg < 0) endAngleDeg += 360.0;
                    startAngleDeg %= 360.0;
                    endAngleDeg %= 360.0;

                    double totalAngleDeg = endAngleDeg - startAngleDeg;
                    if (totalAngleDeg <= 1e-9)
                    {
                        totalAngleDeg += 360.0;
                    }

                    if (arc.Normal.Z < 0)
                    {
                        record.起始角度 = Math.Round(endAngleDeg, 6);
                        record.总角度 = Math.Round(360.0 - totalAngleDeg, 6);
                    }
                    else
                    {
                        record.起始角度 = Math.Round(startAngleDeg, 6);
                        record.总角度 = Math.Round(totalAngleDeg, 6);
                    }
                    break;

                case Circle circle:
                    record.对象类型 = "CIRCLE";
                    var circleCenter = TransformPoint(circle.Center);
                    record.圆心X = Math.Round(circleCenter.X, 6); record.圆心Y = Math.Round(circleCenter.Y, 6);
                    record.半径 = Math.Round(circle.Radius * totalScale, 6);
                    break;

                default:
                    return null;
            }
            return record;
        }

        private bool WriteToCSV(List<CADDataRecord> data, string outputPath)
        {
            using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
            {
                writer.WriteLine("部件ID,对象类型,对象句柄,起点X,起点Y,终点X,终点Y,圆心X,圆心Y,半径,起始角度(°),总角度(°)");
                foreach (var record in data)
                {
                    // 对坐标和半径使用FormatDouble，但对角度使用新的FormatAngle
                    var line = $"{record.部件ID},{record.对象类型},'{record.对象句柄}'," +
                               $"{FormatDouble(record.起点X)},{FormatDouble(record.起点Y)}," +
                               $"{FormatDouble(record.终点X)},{FormatDouble(record.终点Y)}," +
                               $"{FormatDouble(record.圆心X)},{FormatDouble(record.圆心Y)}," +
                               $"{FormatDouble(record.半径)}," +
                               $"{FormatAngle(record.起始角度)},{FormatAngle(record.总角度)}"; // <-- 使用新方法
                    writer.WriteLine(line);
                }
            }
            return true;
        }

        private string FormatDouble(double value)
        {
            return value.ToString("F6");
        }


        /// <summary>
        /// 新增：专门用于格式化角度的方法，确保0度不会变为空白
        /// </summary>
        private string FormatAngle(double value)
        {
            return value.ToString("F6");
        }
    }
}
