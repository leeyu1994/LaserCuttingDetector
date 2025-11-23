using System.Collections.Concurrent;
using OpenCvSharp;
using VisionLibrary.Models;
using VisionLibrary.Utils;

namespace VisionLibrary.Modules;

/// <summary>
///     针对 ComponentId == "可掉落" 的路径，
///     只检测“单边边界是否存在 & 位置偏移”，不检测宽度。
///     适用于“掉了也行、不掉也行”的开窗区域。
/// </summary>
public class DroppableEdgeDetector(ImageInspectionConfig config, CoordinateConverter converter)
{
    /// <summary>
    ///     执行可掉落区域的边界检测。
    ///     binaryImage: 二值化图像，目标边界为白色（与 FastPixelData 约定一致）。
    ///     droppableCad: 仅包含 ComponentId == "可掉落" 的 LINE/CIRCLE/ARC。
    /// </summary>
    public List<DroppableEdgeResult> Detect(Mat binaryImage, List<CadRawData> droppableCad)
    {
        var fastPixelData = new FastPixelData(binaryImage);

        var validEntities = droppableCad
            .Where(row => (row.ObjectType == "LINE" || row.ObjectType == "CIRCLE" || row.ObjectType == "ARC")
                          && row.StartX.HasValue)
            .ToList();

        if (!validEntities.Any()) return new List<DroppableEdgeResult>();

        var results = new ConcurrentBag<DroppableEdgeResult>();

        Parallel.For(0, validEntities.Count, i =>
        {
            var csvRow = validEntities[i];
            var (samples, normals) = SampleEntity(csvRow, config.WidthSampleStep);

            for (var j = 0; j < samples.Count; j++)
            {
                var sampleResult = ProcessDroppableSamplePoint(
                    fastPixelData,
                    samples[j],
                    normals[j],
                    i + 1,
                    csvRow.ObjectType
                );
                results.Add(sampleResult);
            }
        });

        return results.ToList();
    }

    /// <summary>
    ///     针对单个可掉落采样点，只做“单边边界 + 位置偏移”检测。
    /// </summary>
    private DroppableEdgeResult ProcessDroppableSamplePoint(
        FastPixelData pixelData,
        Point2d designPointMm,
        (double nx, double ny) normal,
        int curveId,
        string curveType)
    {
        // 1. 设计点 mm -> 像素
        var mappedPixel = converter.ToPixel(designPointMm);
        var startX = mappedPixel.X;
        var startY = mappedPixel.Y;

        // 2. 在附近找最近的有效点（避免起点刚好落在缝里）
        var nearbyPoint = FindNearbyValidPoint(pixelData, startX, startY, config.NearbySearchRadius);
        var isValid = nearbyPoint.HasValue;
        var finalX = isValid ? nearbyPoint!.Value.X : startX;
        var finalY = isValid ? nearbyPoint!.Value.Y : startY;

        var result = new DroppableEdgeResult
        {
            CurveId = curveId,
            CurveType = curveType,
            DesignX = designPointMm.X,
            DesignY = designPointMm.Y,
            FinalMappedX = finalX,
            FinalMappedY = finalY,
            IsValid = false
        };

        if (!isValid)
            return result;

        // 3. 沿正反两个方向各搜索一次边界（谁先/谁近用谁）
        var posBoundary = SearchBoundary(pixelData, finalX, finalY, normal.nx, normal.ny, config.MaxSearchSteps);
        var negBoundary = SearchBoundary(pixelData, finalX, finalY, -normal.nx, -normal.ny, config.MaxSearchSteps);

        Point? chosen = null;

        if (posBoundary.HasValue && negBoundary.HasValue)
        {
            var dPos = DistanceSquared(posBoundary.Value, mappedPixel);
            var dNeg = DistanceSquared(negBoundary.Value, mappedPixel);
            chosen = dPos <= dNeg ? posBoundary : negBoundary;
        }
        else if (posBoundary.HasValue)
        {
            chosen = posBoundary;
        }
        else if (negBoundary.HasValue)
        {
            chosen = negBoundary;
        }

        if (!chosen.HasValue)
        {
            // 单边也找不到 → 认为此处没有明显白-黑边界
            result.IsValid = false;
            return result;
        }

        // 4. 计算单边偏移
        var edge = chosen.Value;
        result.EdgeX = edge.X;
        result.EdgeY = edge.Y;

        double dx = edge.X - mappedPixel.X;
        double dy = edge.Y - mappedPixel.Y;
        result.OffsetDistanceMm = Math.Sqrt(dx * dx + dy * dy) * config.PixelSize;

        // 判定是否合格：使用专门的可掉落偏移容差（你可以在配置里加一个 DroppableOffsetThreshold）
        var threshold = config.WidthOffsetThreshold; // 暂时复用，如果你愿意可以再加一个字段
        result.OffsetQualified = result.OffsetDistanceMm <= threshold ? "T" : "F";
        result.IsValid = true;

        return result;
    }

    private static double DistanceSquared(Point p, Point center)
    {
        double dx = p.X - center.X;
        double dy = p.Y - center.Y;
        return dx * dx + dy * dy;
    }

    #region 与 WidthDetector 类似的工具函数（可抽成公共 Util）

    private Point? SearchBoundary(FastPixelData pixelData, int startX, int startY, double dirX, double dirY,
        int maxSearchSteps)
    {
        double currentX = startX;
        double currentY = startY;
        var lastValidX = startX;
        var lastValidY = startY;

        for (var i = 0; i < maxSearchSteps; i++)
        {
            currentX += dirX;
            currentY += dirY;
            var nextX = (int)Math.Round(currentX);
            var nextY = (int)Math.Round(currentY);

            if (nextX == lastValidX && nextY == lastValidY) continue;

            if (!pixelData.IsValidPixel(nextX, nextY)) return new Point(lastValidX, lastValidY);

            lastValidX = nextX;
            lastValidY = nextY;
        }

        return null;
    }

    private Point? FindNearbyValidPoint(FastPixelData pixelData, int x, int y, int searchRadius)
    {
        var minDistanceSq = double.MaxValue;
        Point? closestPoint = null;

        for (var dy = -searchRadius; dy <= searchRadius; dy++)
        for (var dx = -searchRadius; dx <= searchRadius; dx++)
        {
            if (dx == 0 && dy == 0) continue;

            var cx = x + dx;
            var cy = y + dy;

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

        return closestPoint;
    }

    private (List<Point2d> samples, List<(double nx, double ny)> normals) SampleEntity(CadRawData csvRow, double step)
    {
        var samples = new List<Point2d>();
        var normals = new List<(double nx, double ny)>();
        var objectType = (csvRow.ObjectType ?? "").Trim().ToUpperInvariant();

        if (objectType == "LINE")
        {
            if (!csvRow.StartX.HasValue || !csvRow.StartY.HasValue || !csvRow.EndX.HasValue || !csvRow.EndY.HasValue)
                return (samples, normals);

            double x1 = csvRow.StartX.Value, y1 = csvRow.StartY.Value;
            double x2 = csvRow.EndX.Value, y2 = csvRow.EndY.Value;
            double dx = x2 - x1, dy = y2 - y1;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 1e-6) return (samples, normals);

            double nx = -dy / length, ny = dx / length;
            var numSamples = Math.Max(1, (int)Math.Round(length / step));
            for (var i = 0; i <= numSamples; i++)
            {
                var t = numSamples > 0 ? (double)i / numSamples : 0;
                var sx = x1 + dx * t;
                var sy = y1 + dy * t;
                samples.Add(new Point2d(sx, sy));
                normals.Add((nx, ny));
            }
        }

        // TODO: CIRCLE / ARC 的采样可以参考 WidthDetector 中的实现，这里略。
        return (samples, normals);
    }

    #endregion
}