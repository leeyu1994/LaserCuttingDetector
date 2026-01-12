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

    private class TemplateModelSet : IDisposable
    {
        public HShapeModel CoarseModel { get; init; }
        public HShapeModel FineModel { get; init; }
        public List<PointF> TheoreticalCorners { get; init; } = new();
        public PointF ImageCenter { get; init; }

        public void Dispose()
        {
            CoarseModel?.Dispose();
            FineModel?.Dispose();
        }
    }

    public HShapeModel? CoarseModel { get; private set; }
    public HShapeModel? FineModel { get; private set; }
    public List<PointF> TheoreticalCorners { get; private set; } = new();
    public PointF ImageCenter { get; private set; }
    public RectangleF CadBoundsMm { get; private set; }
    public List<PointF> CrossCentersMm { get; private set; } = new();

    private TemplateModelSet? _frontModelSet;
    private TemplateModelSet? _backlitModelSet;

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

        using var hImage = SkiaToHalcon(skImage);
        _frontModelSet = TrainHalconModels(hImage, flipVertical: false);
        CoarseModel = _frontModelSet.CoarseModel;
        FineModel = _frontModelSet.FineModel;
        TheoreticalCorners = _frontModelSet.TheoreticalCorners;
        ImageCenter = _frontModelSet.ImageCenter;

        if (!string.IsNullOrEmpty(invertedTemplateOutputPath))
        {
            using var inverted = CreateInvertedAndFlippedImage(skImage);
            using var data = inverted.Encode(SKEncodedImageFormat.Png, 100);
            using var fs = File.Open(invertedTemplateOutputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            data.SaveTo(fs);

            using var invertedHImage = SkiaToHalcon(inverted);
            _backlitModelSet = TrainHalconModels(invertedHImage, flipVertical: true);
        }
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

    private TemplateModelSet TrainHalconModels(HImage image, bool flipVertical)
    {
        image.GetImageSize(out int width, out int height);
        var imageCenter = new PointF(width / 2f, height / 2f);

        var theoreticalCorners = OrderCorners(
            CrossCentersMm.Select(mm => MmToPixel(mm, CadBoundsMm, height, flipVertical)).ToList());

        if (theoreticalCorners.Count != 4)
            throw new InvalidOperationException($"预期找到4个角点，实际计算出 {theoreticalCorners.Count} 个");

        var coarseModel = TrainCoarseModel(image, imageCenter);
        var fineModel = TrainFineModel(image, imageCenter, theoreticalCorners);

        return new TemplateModelSet
        {
            CoarseModel = coarseModel,
            FineModel = fineModel,
            TheoreticalCorners = theoreticalCorners,
            ImageCenter = imageCenter
        };
    }

    private HShapeModel TrainCoarseModel(HImage image, PointF imageCenter)
    {
        double halfSide = _config.CoarseRegionSizeMm / _config.PixelSizeMm / 2.0;

        using var region = new HRegion();
        region.GenRectangle1(
            imageCenter.Y - halfSide,
            imageCenter.X - halfSide,
            imageCenter.Y + halfSide,
            imageCenter.X + halfSide);

        using var reduced = image.ReduceDomain(region);

        var coarseModel = new HShapeModel();
        coarseModel.CreateShapeModel(
            reduced,
            "auto",
            -_config.AngleSearchRangeRad,
            _config.AngleSearchRangeRad * 2,
            "auto",
            "auto",
            "use_polarity",
            "auto",
            "auto");

        return coarseModel;
    }

    private HShapeModel TrainFineModel(HImage image, PointF imageCenter, List<PointF> theoreticalCorners)
    {
        double halfSide = _config.FineRegionSizeMm / _config.PixelSizeMm / 2.0;
        var crossCenter = theoreticalCorners[0]; // 左上

        using var region = new HRegion();
        region.GenRectangle1(
            crossCenter.Y - halfSide,
            crossCenter.X - halfSide,
            crossCenter.Y + halfSide,
            crossCenter.X + halfSide);

        using var reduced = image.ReduceDomain(region);

        var fineModel = new HShapeModel();
        fineModel.CreateShapeModel(
            reduced,
            "auto",
            -_config.AngleSearchRangeRad,
            _config.AngleSearchRangeRad * 2,
            "auto",
            "auto",
            "use_polarity",
            "auto",
            "auto");

        return fineModel;
    }

    private PointF MmToPixel(PointF mmPoint, RectangleF boundsMm, int imageHeight, bool flipVertical)
    {
        float px = (mmPoint.X - boundsMm.Left + _config.CanvasMarginMm) / _config.PixelSizeMm;
        float py = imageHeight - (mmPoint.Y - boundsMm.Top + _config.CanvasMarginMm) / _config.PixelSizeMm;
        if (flipVertical)
        {
            py = imageHeight - py;
        }
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

    internal (HShapeModel Coarse, HShapeModel Fine, List<PointF> Corners, PointF Center) GetModelSet(bool useBacklight)
    {
        var selected = useBacklight && _backlitModelSet != null
            ? _backlitModelSet
            : _frontModelSet;

        if (selected == null)
            throw new InvalidOperationException("请先调用 GenerateTemplates 生成定位模板。");

        return (selected.CoarseModel, selected.FineModel, selected.TheoreticalCorners, selected.ImageCenter);
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
        _frontModelSet?.Dispose();
        _backlitModelSet?.Dispose();
        CoarseModel = null;
        FineModel = null;
        _frontModelSet = null;
        _backlitModelSet = null;
    }

    public void Dispose()
    {
        DisposeModels();
    }
}
