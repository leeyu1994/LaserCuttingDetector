using System.Collections.Concurrent;
using OpenCvSharp;
using VisionLibrary.Models;
using VisionLibrary.Utils;

namespace VisionLibrary.Modules;

/// <summary>
///     负责检测CAD理论路径在背光图像上是否透光。
///     通过在路径上采样，并检查每个采样点横截面上的像素灰度值来判断。
/// </summary>
public class LightTransmissionDetector(ImageInspectionConfig config, CoordinateConverter converter)
{
    /// <summary>
    ///     执行透光性检测的核心方法。
    /// </summary>
    /// <param name="backlitImage">背光拍摄的图像（目标线路为白色）。可以是彩色或灰度图。</param>
    /// <param name="cadData">原始CAD数据列表。</param>
    /// <returns>包含所有采样点检测结果的列表。</returns>
    public List<LightTransmissionResult> Detect(Mat backlitImage, List<CadRawData> cadData)
    {
        // 准备灰度图像，后续所有操作都在灰度图上进行
        using var grayImage = new Mat();
        if (backlitImage.Channels() > 1)
            Cv2.CvtColor(backlitImage, grayImage, ColorConversionCodes.BGR2GRAY);
        else
            backlitImage.CopyTo(grayImage);

        var validEntities = cadData
            .Where(row => (row.ObjectType == "LINE" || row.ObjectType == "CIRCLE" || row.ObjectType == "ARC") &&
                          row.StartX.HasValue)
            .ToList();

        if (!validEntities.Any()) return new List<LightTransmissionResult>();

        var results = new ConcurrentBag<LightTransmissionResult>();
        // 使用并行处理来加速
        Parallel.For(0, validEntities.Count, i =>
        {
            var csvRow = validEntities[i];
            var (samples, normals) = SampleEntity(csvRow, config.TransmissionSampleStep);

            for (var j = 0; j < samples.Count; j++)
            {
                var sampleResult =
                    ProcessTransmissionSamplePoint(grayImage, samples[j], normals[j], i + 1, csvRow.ObjectType);
                results.Add(sampleResult);
            }
        });

        return results.ToList();
    }

    /// <summary>
    ///     处理单个采样点，检查其横截面的透光性。
    /// </summary>
    private LightTransmissionResult ProcessTransmissionSamplePoint(Mat grayImage, Point2d designPointMm,
        (double nx, double ny) normal, int curveId, string curveType)
    {
        // 1. 将理论物理坐标(mm)转换为像素坐标(px)
        var mappedPixel = converter.ToPixel(designPointMm);

        var result = new LightTransmissionResult
        {
            CurveId = curveId,
            CurveType = curveType,
            DesignX = Math.Round(designPointMm.X, 4),
            DesignY = Math.Round(designPointMm.Y, 4),
            MappedX = mappedPixel.X,
            MappedY = mappedPixel.Y
        };

        // 2. 沿法线方向，在定义的检查宽度内，采集所有像素的灰度值
        var grayValues = new List<byte>();
        var checkHalfWidth = config.TransmissionCheckWidthPixels / 2.0;

        // 迭代步长可以小于1，以确保在倾斜时不会跳过像素
        for (var step = -checkHalfWidth; step <= checkHalfWidth; step += 0.5)
        {
            var currentX = mappedPixel.X + step * normal.nx;
            var currentY = mappedPixel.Y + step * normal.ny;

            var x = (int)Math.Round(currentX);
            var y = (int)Math.Round(currentY);

            // 边界检查，确保坐标在图像范围内
            if (x >= 0 && x < grayImage.Width && y >= 0 && y < grayImage.Height)
                grayValues.Add(grayImage.At<byte>(y, x));
        }

        // 3. 分析采集到的灰度值
        if (grayValues.Count == 0)
        {
            // 如果没有采集到任何点（例如，理论路径在图像外），则标记为不透光
            result.IsTransmitted = "F";
            result.AverageGrayValue = 0;
            result.MinGrayValue = 0;
            return result;
        }

        // 去重，因为四舍五入可能导致同一个像素被多次采样
        var distinctGrayValues = grayValues.Distinct().ToList();

        result.AverageGrayValue = Math.Round(distinctGrayValues.Average(v => v), 2);
        result.MinGrayValue = distinctGrayValues.Min();

        // 4. 根据最小灰度值进行最终判断
        // **核心逻辑**：只要横截面中存在一个灰度值低于阈值的点，就认为该点存在遮挡，即不透光。
        // 这种方法比判断平均值更严格、更可靠。
        if (result.MinGrayValue >= config.MinTransmissionGrayValue)
            result.IsTransmitted = "T";
        else
            result.IsTransmitted = "F";

        return result;
    }

    /// <summary>
    ///     根据CAD图元数据，按固定步长生成采样点及其法向量。
    ///     (此方法与WidthDetector中的完全相同，直接复用)
    /// </summary>
    private (List<Point2d> samples, List<(double nx, double ny)> normals) SampleEntity(CadRawData csvRow, double step)
    {
        var samples = new List<Point2d>();
        var normals = new List<(double nx, double ny)>();
        var objectType = csvRow.ObjectType?.Trim().ToUpper() ?? string.Empty;

        switch (objectType)
        {
            case "LINE" when !csvRow.StartX.HasValue || !csvRow.StartY.HasValue || !csvRow.EndX.HasValue || !csvRow.EndY.HasValue:
                break;
            case "LINE":
            {
                double x1 = csvRow.StartX.Value, y1 = csvRow.StartY.Value;
                double x2 = csvRow.EndX.Value, y2 = csvRow.EndY.Value;
                double dx = x2 - x1, dy = y2 - y1;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-6) return (samples, normals);

                double nx = -dy / length, ny = dx / length;
                var numSamples = Math.Max(1, (int)Math.Round(length / step));
                for (var i = 0; i <= numSamples; i++)
                {
                    var t = (double)i / numSamples;
                    samples.Add(new Point2d(x1 + t * dx, y1 + t * dy));
                    normals.Add((nx, ny));
                }

                break;
            }
            case "CIRCLE":
            case "ARC":
            {
                if (!csvRow.CenterX.HasValue || !csvRow.CenterY.HasValue || !csvRow.Radius.HasValue)
                    return (samples, normals);
                double cx = csvRow.CenterX.Value, cy = csvRow.CenterY.Value, r = csvRow.Radius.Value;
                if (r < 1e-6) return (samples, normals);

                var startAngle = csvRow.StartAngle ?? 0;
                var totalAngle = objectType == "ARC" ? csvRow.TotalAngle ?? 360 : 360;
                var startRad = startAngle * Math.PI / 180.0;
                var totalRad = totalAngle * Math.PI / 180.0;
                var arcLength = Math.Abs(totalRad) * r;
                var numSamples = Math.Max(1, (int)Math.Round(arcLength / step));

                for (var i = 0; i <= numSamples; i++)
                {
                    var t = (double)i / numSamples;
                    var angle = startRad + t * totalRad;
                    var x = cx + r * Math.Cos(angle);
                    var y = cy + r * Math.Sin(angle);
                    samples.Add(new Point2d(x, y));
                    normals.Add(((x - cx) / r, (y - cy) / r));
                }

                break;
            }
        }

        return (samples, normals);
    }
}