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
        /// <param name="doc">当前CAD文档</param>
        /// <param name="outputCsvPath">输出CSV文件路径</param>
        /// <param name="selectedLayers">需要提取的图层列表</param>
        /// <param name="userOrigin">用户坐标系原点</param>
        /// <param name="unitScale">单位缩放因子</param>
        /// <returns>是否成功</returns>
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

                // 步骤 1: 【已验证正确】手动遍历所有几何点，计算100%可靠的边界框
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

                // 步骤 2: 【最终修正】如果自动适配，则计算居中和缩放参数
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

                        // 计算缩放后的尺寸
                        double scaledWidth = sourceWidth * autoFitScale;
                        double scaledHeight = sourceHeight * autoFitScale;

                        // 计算居中所需的偏移量
                        double offsetX = (targetWidth - scaledWidth) / 2.0;
                        double offsetY = (targetHeight - scaledHeight) / 2.0;
                        centeringOffset = new Vector3d(offsetX, offsetY, 0);
                    }
                }

                double totalScale = unitScale * autoFitScale;

                // 步骤 3: 处理实体，传入所有变换参数
                foreach (var entityInfo in basicEntities)
                {
                    var record = ProcessEntity(entityInfo.Entity, entityInfo.LayerName, origin, totalScale, centeringOffset);
                    if (record != null)
                    {
                        extractedData.Add(record);
                    }
                }

                trans.Abort();
            }

            return WriteToCSV(extractedData, outputCsvPath);
        }
        /// <summary>
        /// 辅助方法：获取单个实体的边界并更新总边界
        /// </summary>
        private void GetEntityBounds(Entity entity, ref double minX, ref double minY, ref double maxX, ref double maxY)
        {
            switch (entity)
            {
                case Line line:
                    minX = Math.Min(minX, line.StartPoint.X); maxX = Math.Max(maxX, line.StartPoint.X);
                    minY = Math.Min(minY, line.StartPoint.Y); maxY = Math.Max(maxY, line.StartPoint.Y);
                    minX = Math.Min(minX, line.EndPoint.X); maxX = Math.Max(maxX, line.EndPoint.X);
                    minY = Math.Min(minY, line.EndPoint.Y); maxY = Math.Max(maxY, line.EndPoint.Y);
                    break;
                case Arc arc:
                    minX = Math.Min(minX, arc.Center.X - arc.Radius); maxX = Math.Max(maxX, arc.Center.X + arc.Radius);
                    minY = Math.Min(minY, arc.Center.Y - arc.Radius); maxY = Math.Max(maxY, arc.Center.Y + arc.Radius);
                    break;
                case Circle circle:
                    minX = Math.Min(minX, circle.Center.X - circle.Radius); maxX = Math.Max(maxX, circle.Center.X + circle.Radius);
                    minY = Math.Min(minY, circle.Center.Y - circle.Radius); maxY = Math.Max(maxY, circle.Center.Y + circle.Radius);
                    break;
            }
        }
        /// <summary>
        /// 准备图层：确保目标图层是打开、解冻、未锁定的。
        /// </summary>
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
        /// <summary>
        /// 循环处理所有复杂对象，直到模型空间中只剩下基本几何体。
        /// 此方法恢复了对样条曲线的转换和对其他对象分解的区分处理。
        /// </summary>
        private void ProcessComplexObjects(Database db, Transaction trans, HashSet<string> targetLayers)
        {
            var modelSpace = (BlockTableRecord)trans.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            bool changedInLoop;

            do
            {
                changedInLoop = false;
                var splinesToConvert = new List<ObjectId>();
                var entitiesToExplode = new List<ObjectId>();

                // 步骤 2.1: 遍历并分类所有需要处理的对象
                foreach (ObjectId objId in modelSpace)
                {
                    var entity = trans.GetObject(objId, OpenMode.ForRead) as Entity;
                    if (entity == null || entity.IsErased || !targetLayers.Contains(entity.Layer)) continue;

                    // 类别 A: 样条曲线 (需要转换，而不是分解)
                    if (entity is Spline)
                    {
                        splinesToConvert.Add(objId);
                        continue;
                    }

                    // 类别 B: 可分解的对象 (块、多段线等)
                    if (entity is BlockReference || entity is Polyline || entity is MInsertBlock || entity is Polyline2d)
                    {
                        // 安全检查：跳过外部参照块，以避免 eNotApplicable 异常
                        if (entity is BlockReference blockRef)
                        {
                            var blockDef = trans.GetObject(blockRef.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
                            if (blockDef != null && (blockDef.IsFromExternalReference || blockDef.IsFromOverlayReference))
                            {
                                continue; // 是外部参照，跳过
                            }
                        }
                        entitiesToExplode.Add(objId);
                    }
                }

                // 步骤 2.2: 如果找到样条曲线，则将其转换为多段线
                if (splinesToConvert.Count > 0)
                {
                    changedInLoop = true;
                    modelSpace.UpgradeOpen();
                    foreach (var splineId in splinesToConvert)
                    {
                        var spline = trans.GetObject(splineId, OpenMode.ForWrite) as Spline;
                        if (spline == null || spline.IsErased) continue;

                        var polyline = ConvertSplineToPolyline(spline); // 调用转换辅助函数
                        if (polyline != null)
                        {
                            polyline.Layer = spline.Layer;
                            modelSpace.AppendEntity(polyline);
                            trans.AddNewlyCreatedDBObject(polyline, true);
                        }
                        spline.Erase();
                    }
                    modelSpace.DowngradeOpen();
                }

                // 步骤 2.3: 如果找到可分解对象，则分解它们
                if (entitiesToExplode.Count > 0)
                {
                    changedInLoop = true;
                    modelSpace.UpgradeOpen();
                    foreach (var entityId in entitiesToExplode)
                    {
                        var entity = trans.GetObject(entityId, OpenMode.ForWrite) as Entity;
                        if (entity == null || entity.IsErased) continue;

                        var explodedEntities = new DBObjectCollection();
                        entity.Explode(explodedEntities);

                        foreach (Entity explodedEntity in explodedEntities)
                        {
                            explodedEntity.Layer = entity.Layer;
                            modelSpace.AppendEntity(explodedEntity);
                            trans.AddNewlyCreatedDBObject(explodedEntity, true);
                        }
                        entity.Erase();
                    }
                    modelSpace.DowngradeOpen();
                }

            } while (changedInLoop); // 只要本轮有修改，就再循环一次，确保全部分解
        }

        /// <summary>
        /// 将样条曲线转换为多段线 (保留您原始代码中的逻辑)
        /// </summary>
        private Polyline ConvertSplineToPolyline(Spline spline, int precision = 20)
        {
            // 对于一个给定的样条曲线，20-100的精度通常足够
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

        /// <summary>
        /// 实体信息结构
        /// </summary>
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
            }
            return result;
        }

        /// <summary>
        /// 处理单个实体并转换为数据记录 (最终修正版: 处理反转法线导致的镜像问题)
        /// </summary>

        private CADDataRecord ProcessEntity(Entity entity, string layerName, Point3d origin, double totalScale, Vector3d offset)
        {
            var record = new CADDataRecord { 部件ID = layerName, 对象句柄 = entity.Handle.ToString() };

            Point3d TransformPoint(Point3d point)
            {
                // 1. 平移到(0,0)
                var translatedPoint = point - origin.GetAsVector();
                // 2. 缩放
                var scaledPoint = translatedPoint * totalScale;
                // 3. 居中平移
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

                    // --- 【最终修正】: 彻底放弃API角度，完全基于几何反算 ---
                    Vector3d startVec = arc.StartPoint - arc.Center;
                    Vector3d endVec = arc.EndPoint - arc.Center;

                    double startAngleRad = Math.Atan2(startVec.Y, startVec.X);
                    double endAngleRad = Math.Atan2(endVec.Y, endVec.X);

                    double startAngleDeg = startAngleRad * 180.0 / Math.PI;
                    double endAngleDeg = endAngleRad * 180.0 / Math.PI;

                    while (startAngleDeg < 0) startAngleDeg += 360.0;
                    while (endAngleDeg < 0) endAngleDeg += 360.0;

                    double totalAngleDeg = endAngleDeg - startAngleDeg;
                    if (totalAngleDeg <= 0) totalAngleDeg += 360.0;

                    // 如果是镜像的，说明应该取长弧
                    if (arc.Normal.Z < 0)
                    {
                        totalAngleDeg = 360.0 - totalAngleDeg;
                    }

                    record.起始角度 = Math.Round(startAngleDeg, 6);
                    record.总角度 = Math.Round(totalAngleDeg, 6);
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
        /// 将数据写入CSV文件
        /// </summary>
        private bool WriteToCSV(List<CADDataRecord> data, string outputPath)
        {
            using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
            {
                writer.WriteLine("部件ID,对象类型,对象句柄,起点X,起点Y,终点X,终点Y,圆心X,圆心Y,半径,起始角度(°),总角度(°)");
                foreach (var line in data.Select(record => $"{record.部件ID},{record.对象类型},'{record.对象句柄}'," +
                                                           $"{FormatDouble(record.起点X)},{FormatDouble(record.起点Y)}," +
                                                           $"{FormatDouble(record.终点X)},{FormatDouble(record.终点Y)}," +
                                                           $"{FormatDouble(record.圆心X)},{FormatDouble(record.圆心Y)}," +
                                                           $"{FormatDouble(record.半径)}," +
                                                           $"{FormatDouble(record.起始角度)},{FormatDouble(record.总角度)}"))
                {
                    writer.WriteLine(line);
                }
            }
            return true;
        }

        /// <summary>
        /// 格式化双精度数值
        /// </summary>
        private string FormatDouble(double value)
        {
            return Math.Abs(value) < 1e-10 ? "" : value.ToString("F6");
        }
    }
}
