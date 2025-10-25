// VisionLibrary/Modules/WidthDetector.cs
using OpenCvSharp;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System;
using VisionLibrary.Models;
using VisionLibrary.Utils;

namespace VisionLibrary.Modules
{
    /// <summary>
    /// 负责根据CAD理论路径，在图像上采样并检测线路的宽度和偏移。
    /// </summary>
    public class WidthDetector
    {
        private readonly ImageInspectionConfig _config;
        private readonly CoordinateConverter _converter;

        public WidthDetector(ImageInspectionConfig config, CoordinateConverter converter)
        {
            _config = config;
            _converter = converter;
        }

        /// <summary>
        /// 执行宽度检测的核心方法。
        /// </summary>
        /// <param name="binaryImage">二值化后的图像（目标线路为白色）。</param>
        /// <param name="cadData">原始CAD数据列表。</param>
        /// <returns>包含所有采样点结果的列表。</returns>
        public List<WidthSampleResult> Detect(Mat binaryImage, List<CadRawData> cadData)
        {
            var fastPixelData = new FastPixelData(binaryImage);

            var validEntities = cadData
                .Where(row => (row.ObjectType == "LINE" || row.ObjectType == "CIRCLE" || row.ObjectType == "ARC") && row.StartX.HasValue)
                .ToList();

            if (!validEntities.Any()) return new List<WidthSampleResult>();

            // 使用并行处理来加速对大量图元的采样和计算
            var results = new ConcurrentBag<WidthSampleResult>();
            Parallel.For(0, validEntities.Count, i =>
            {
                var csvRow = validEntities[i];
                var (samples, normals) = SampleEntity(csvRow, _config.WidthSampleStep);

                for (int j = 0; j < samples.Count; j++)
                {
                    var sampleResult = ProcessSamplePoint(fastPixelData, samples[j], normals[j], i + 1, csvRow.ObjectType);
                    results.Add(sampleResult);
                }
            });

            return results.ToList();
        }

        /// <summary>
        /// 处理单个采样点，计算其宽度和偏移。
        /// </summary>
        private WidthSampleResult ProcessSamplePoint(FastPixelData pixelData, Point2d designPointMm, (double nx, double ny) normal, int curveId, string curveType)
        {
            // 1. 将理论物理坐标(mm)转换为像素坐标(px)
            Point mappedPixel = _converter.ToPixel(designPointMm);
            int finalX = mappedPixel.X;
            int finalY = mappedPixel.Y;

            // 2. 检查理论点是否落在实际线路上，如果不在，则在附近搜索最近的有效点
            bool isValid = pixelData.IsValidPixel(finalX, finalY);
            if (!isValid)
            {
                var nearbyPoint = FindNearbyValidPoint(pixelData, finalX, finalY, _config.NearbySearchRadius);
                if (nearbyPoint.HasValue)
                {
                    finalX = nearbyPoint.Value.X;
                    finalY = nearbyPoint.Value.Y;
                    isValid = true;
                }
            }

            var result = new WidthSampleResult
            {
                CurveId = curveId,
                CurveType = curveType,
                DesignX = designPointMm.X,
                DesignY = designPointMm.Y,
                FinalMappedX = finalX,
                FinalMappedY = finalY,
                IsValid = isValid,
            };

            if (!isValid) return result;

            // 3. 从有效点出发，沿法线正反两个方向搜索线路边界
            var posBoundary = SearchBoundary(pixelData, finalX, finalY, normal.nx, normal.ny, _config.MaxSearchSteps);
            var negBoundary = SearchBoundary(pixelData, finalX, finalY, -normal.nx, -normal.ny, _config.MaxSearchSteps);

            // 4. 根据找到的边界点计算宽度和中心点
            if (posBoundary.HasValue && negBoundary.HasValue)
            {
                double widthPixel = posBoundary.Value.DistanceTo(negBoundary.Value);
                result.WidthMm = widthPixel * _config.PixelSize;
                result.MidX = (posBoundary.Value.X + negBoundary.Value.X) / 2.0;
                result.MidY = (posBoundary.Value.Y + negBoundary.Value.Y) / 2.0;
            }

            // 5. 计算偏移
            if (result.MidX.HasValue) // 如果能确定中心点
            {
                double xDev = result.MidX.Value - mappedPixel.X;
                double yDev = result.MidY.Value - mappedPixel.Y;
                result.OffsetDistanceMm = Math.Sqrt(xDev * xDev + yDev * yDev) * _config.PixelSize;
                result.OffsetQualified = result.OffsetDistanceMm <= _config.WidthOffsetThreshold ? "T" : "F";
            }

            // 6. 判断宽度是否合格
            if (result.WidthMm.HasValue)
            {
                result.WidthQualified = (result.WidthMm >= _config.MinQualifiedWidth && result.WidthMm <= _config.MaxQualifiedWidth) ? "T" : "F";
            }

            return result;
        }

        /// <summary>
        /// 沿指定方向搜索像素值为0的边界点。
        /// </summary>
        private Point? SearchBoundary(FastPixelData pixelData, int startX, int startY, double dirX, double dirY, int maxSearchSteps)
        {
            double currentX = startX;
            double currentY = startY;
            int lastValidX = startX;
            int lastValidY = startY;

            for (int i = 0; i < maxSearchSteps; i++)
            {
                currentX += dirX;
                currentY += dirY;
                int nextX = (int)Math.Round(currentX);
                int nextY = (int)Math.Round(currentY);

                if (nextX == lastValidX && nextY == lastValidY) continue; // 避免因四舍五入停在原地

                if (!pixelData.IsValidPixel(nextX, nextY))
                {
                    return new Point(lastValidX, lastValidY); // 返回最后一个有效点
                }
                lastValidX = nextX;
                lastValidY = nextY;
            }
            return null; // 未在最大步数内找到边界
        }

        /// <summary>
        /// 在指定点周围的圆形区域内搜索最近的有效像素点。
        /// </summary>
        private Point? FindNearbyValidPoint(FastPixelData pixelData, int x, int y, int searchRadius)
        {
            double minDistanceSq = double.MaxValue;
            Point? closestPoint = null;

            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int cx = x + dx;
                    int cy = y + dy;

                    if (pixelData.IsValidPixel(cx, cy))
                    {
                        double distSq = dx * dx + dy * dy;
                        if (distSq < minDistanceSq)
                        {
                            minDistanceSq = distSq;
                            closestPoint = new Point(cx, cy);
                        }
                    }
                }
            }
            return closestPoint;
        }

        /// <summary>
        /// 根据CAD图元数据，按固定步长生成采样点及其法向量。
        /// </summary>
        private (List<Point2d> samples, List<(double nx, double ny)> normals) SampleEntity(CadRawData csvRow, double step)
        {
            var samples = new List<Point2d>();
            var normals = new List<(double nx, double ny)>();
            string objectType = csvRow.ObjectType?.Trim().ToUpper() ?? string.Empty;

            if (objectType == "LINE")
            {
                if (!csvRow.StartX.HasValue || !csvRow.StartY.HasValue || !csvRow.EndX.HasValue || !csvRow.EndY.HasValue) return (samples, normals);
                double x1 = csvRow.StartX.Value, y1 = csvRow.StartY.Value;
                double x2 = csvRow.EndX.Value, y2 = csvRow.EndY.Value;
                double dx = x2 - x1, dy = y2 - y1;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-6) return (samples, normals);

                double nx = -dy / length, ny = dx / length;
                int numSamples = Math.Max(1, (int)Math.Round(length / step));
                for (int i = 0; i <= numSamples; i++)
                {
                    double t = (numSamples > 0) ? (double)i / numSamples : 0;
                    samples.Add(new Point2d(x1 + t * dx, y1 + t * dy));
                    normals.Add((nx, ny));
                }
            }
            else if (objectType == "CIRCLE" || objectType == "ARC")
            {
                if (!csvRow.CenterX.HasValue || !csvRow.CenterY.HasValue || !csvRow.Radius.HasValue) return (samples, normals);
                double cx = csvRow.CenterX.Value, cy = csvRow.CenterY.Value, r = csvRow.Radius.Value;
                if (r < 1e-6) return (samples, normals);

                double startAngle = csvRow.StartAngle ?? 0;
                double totalAngle = (objectType == "ARC") ? (csvRow.TotalAngle ?? 360) : 360;
                double startRad = startAngle * Math.PI / 180.0;
                double totalRad = totalAngle * Math.PI / 180.0;
                double arcLength = Math.Abs(totalRad) * r;
                int numSamples = Math.Max(1, (int)Math.Round(arcLength / step));

                for (int i = 0; i <= numSamples; i++)
                {
                    double t = (numSamples > 0) ? (double)i / numSamples : 0;
                    double angle = startRad + t * totalRad;
                    double x = cx + r * Math.Cos(angle);
                    double y = cy + r * Math.Sin(angle);
                    samples.Add(new Point2d(x, y));
                    normals.Add(((x - cx) / r, (y - cy) / r));
                }
            }
            return (samples, normals);
        }
    }
}
