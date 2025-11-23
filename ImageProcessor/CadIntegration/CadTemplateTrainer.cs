using System.Drawing;
using System.IO;
using System.Linq;
using HalconDotNet;
using SkiaSharp;
using VisionLibrary.CadIntegration.Config;
using VisionLibrary.CadIntegration.Models;
using VisionLibrary.CadIntegration.Models.Utils;
using VisionLibrary.CadIntegration.Services;

namespace VisionLibrary.CadIntegration;

/// <summary>
/// 从 CAD 数据训练 Halcon 定位模板。
/// </summary>
public class CadTemplateTrainer : IDisposable
{
    private readonly CadTemplateConfig _config;

    public CadTemplateTrainer(CadTemplateConfig config)
    {
        _config = config;
    }

    public HShapeModel? CoarseModel { get; private set; }
    public HShapeModel? FineModel { get; private set; }
    public List<PointF> TheoreticalCorners { get; private set; } = new();
    public PointF ImageCenter { get; private set; }
    public RectangleF CadBoundsMm { get; private set; }
    public List<PointF> CrossCentersMm { get; private set; } = new();

    /// <summary>
    /// 主流程：加载 CAD -> 绘图 -> 生成 Halcon 模板。
    /// </summary>
    public void GenerateTemplates(string csvPath, string? templateOutputPath = null, string? invertedTemplateOutputPath = null)
    {
        DisposeModels();

        var loader = new CADDataLoader();
        var rawData = loader.LoadCADData(csvPath);
        var (allElements, _, _) = loader.ExtractBasicElements(rawData);

        var verificationLines = allElements
            .OfType<LineElement>()
            .Where(e => string.Equals(e.ComponentId, _config.VerificationComponentId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!verificationLines.Any())
            throw new InvalidOperationException($"未在 CAD 数据中找到部件ID为“{_config.VerificationComponentId}”的十字线。");

        CadBoundsMm = CalculateBoundsFromVerificationLines(verificationLines);
        CrossCentersMm = OrderCorners(CalculateCrossCenters(verificationLines));

        var plotter = new CADPlotter(
            CadBoundsMm,
            marginMm: _config.CanvasMarginMm,
            pixelSizeOverride: _config.PixelSizeMm,
            lineWidthOverrideMm: _config.LineWidthMm);

        var elementsToDraw = _config.DrawOnlyVerificationLines
            ? verificationLines.Cast<CADElement>().ToList()
            : allElements;
        plotter.DrawElements(elementsToDraw);

        using var skImage = plotter.GetRenderedImage();
        if (!string.IsNullOrEmpty(templateOutputPath))
        {
            using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
            using var fs = File.Open(templateOutputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            data.SaveTo(fs);
        }
        if (!string.IsNullOrEmpty(invertedTemplateOutputPath))
        {
            using var inverted = CreateInvertedAndFlippedImage(skImage);
            using var data = inverted.Encode(SKEncodedImageFormat.Png, 100);
            using var fs = File.Open(invertedTemplateOutputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            data.SaveTo(fs);
        }
        using var hImage = SkiaToHalcon(skImage);

        TrainHalconModels(hImage);
    }

    private RectangleF CalculateBoundsFromVerificationLines(List<LineElement> lines)
    {
        var xs = lines.SelectMany(l => new[] { l.Start.X, l.End.X }).ToList();
        var ys = lines.SelectMany(l => new[] { l.Start.Y, l.End.Y }).ToList();

        float minX = xs.Min();
        float maxX = xs.Max();
        float minY = ys.Min();
        float maxY = ys.Max();

        return new RectangleF(minX, minY, maxX - minX, maxY - minY);
    }

    private List<PointF> CalculateCrossCenters(List<LineElement> lines)
    {
        float tolerance = Math.Max(_config.CrossMergeToleranceMm, 0.1f);
        var centers = lines.Select(l => new PointF(
            (l.Start.X + l.End.X) / 2f,
            (l.Start.Y + l.End.Y) / 2f)).ToList();

        var clusters = new List<List<PointF>>();
        foreach (var c in centers)
        {
            var cluster = clusters.FirstOrDefault(g => Distance(g[0], c) <= tolerance);
            if (cluster == null)
            {
                clusters.Add(new List<PointF> { c });
            }
            else
            {
                cluster.Add(c);
            }
        }

        return clusters.Select(GetCentroid).Where(p => p != PointF.Empty).ToList();
    }

    private static float Distance(PointF a, PointF b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    private void TrainHalconModels(HImage image)
    {
        image.GetImageSize(out int width, out int height);
        ImageCenter = new PointF(width / 2f, height / 2f);

        TheoreticalCorners = OrderCorners(
            CrossCentersMm.Select(mm => MmToPixel(mm, CadBoundsMm, height)).ToList());

        if (TheoreticalCorners.Count != 4)
            throw new InvalidOperationException($"预期找到4个角点，实际计算出 {TheoreticalCorners.Count} 个");

        TrainCoarseModel(image);
        TrainFineModel(image);
    }

    private void TrainCoarseModel(HImage image)
    {
        double halfSide = _config.CoarseRegionSizeMm / _config.PixelSizeMm / 2.0;

        using var region = new HRegion();
        region.GenRectangle1(
            ImageCenter.Y - halfSide,
            ImageCenter.X - halfSide,
            ImageCenter.Y + halfSide,
            ImageCenter.X + halfSide);

        using var reduced = image.ReduceDomain(region);

        CoarseModel = new HShapeModel();
        CoarseModel.CreateShapeModel(
            reduced,
            "auto",
            -_config.AngleSearchRangeRad,
            _config.AngleSearchRangeRad * 2,
            "auto",
            "auto",
            "use_polarity",
            "auto",
            "auto");
    }

    private void TrainFineModel(HImage image)
    {
        double halfSide = _config.FineRegionSizeMm / _config.PixelSizeMm / 2.0;
        var crossCenter = TheoreticalCorners[0]; // 左上

        using var region = new HRegion();
        region.GenRectangle1(
            crossCenter.Y - halfSide,
            crossCenter.X - halfSide,
            crossCenter.Y + halfSide,
            crossCenter.X + halfSide);

        using var reduced = image.ReduceDomain(region);

        FineModel = new HShapeModel();
        FineModel.CreateShapeModel(
            reduced,
            "auto",
            -_config.AngleSearchRangeRad,
            _config.AngleSearchRangeRad * 2,
            "auto",
            "auto",
            "use_polarity",
            "auto",
            "auto");
    }

    private PointF MmToPixel(PointF mmPoint, RectangleF boundsMm, int imageHeight)
    {
        float px = (mmPoint.X - boundsMm.Left + _config.CanvasMarginMm) / _config.PixelSizeMm;
        float py = imageHeight - (mmPoint.Y - boundsMm.Top + _config.CanvasMarginMm) / _config.PixelSizeMm;
        return new PointF(px, py);
    }

    private SKImage CreateInvertedAndFlippedImage(SKImage source)
    {
        var info = source.Info;
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        canvas.Translate(0, info.Height);
        canvas.Scale(1, -1);

        using var paint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateColorMatrix(new[]
            {
                -1f, 0, 0, 0, 255f,
                0, -1f, 0, 0, 255f,
                0, 0, -1f, 0, 255f,
                0, 0, 0, 1, 0
            })
        };

        canvas.DrawImage(source, 0, 0, paint);
        canvas.Flush();
        return surface.Snapshot();
    }

    private List<PointF> OrderCorners(List<PointF> centers)
    {
        var ordered = centers.OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        if (ordered.Count != 4) return ordered;
        // 排序为：左上、右上、右下、左下
        return new List<PointF>
        {
            ordered[0],
            ordered[1],
            ordered[3],
            ordered[2]
        };
    }

    private PointF GetCentroid(List<PointF> pts)
    {
        if (!pts.Any()) return PointF.Empty;
        return new PointF(pts.Average(p => p.X), pts.Average(p => p.Y));
    }

    private HImage SkiaToHalcon(SKImage skImage)
    {
        using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        var tempFile = Path.GetTempFileName() + ".png";
        File.WriteAllBytes(tempFile, stream.ToArray());

        var hImage = new HImage(tempFile);
        File.Delete(tempFile);
        return hImage;
    }

    private void DisposeModels()
    {
        CoarseModel?.Dispose();
        FineModel?.Dispose();
        CoarseModel = null;
        FineModel = null;
    }

    public void Dispose()
    {
        DisposeModels();
    }
}
