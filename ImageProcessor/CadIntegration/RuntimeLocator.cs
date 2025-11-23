using System;
using System.Drawing;
using System.Linq;
using HalconDotNet;
using VisionLibrary.CadIntegration.Config;

namespace VisionLibrary.CadIntegration;

public class RuntimeLocator
{
    private readonly CadTemplateTrainer _trainer;
    private readonly CadTemplateConfig _config;

    public RuntimeLocator(CadTemplateTrainer trainer, CadTemplateConfig config)
    {
        _trainer = trainer;
        _config = config;
    }

    /// <summary>
    /// 对真实图片进行定位，返回四角坐标（顺序：左上、右上、右下、左下）。
    /// </summary>
    public TemplateMatchResult Locate(HImage sceneImage)
    {
        if (_trainer.CoarseModel is null || _trainer.FineModel is null)
            throw new InvalidOperationException("请先调用 CadTemplateTrainer.GenerateTemplates 生成模板。");

        var result = new TemplateMatchResult();

        _trainer.CoarseModel.FindShapeModel(
            sceneImage,
            -_config.AngleSearchRangeRad,
            _config.AngleSearchRangeRad,
            0.5,
            1,
            _config.CoarseMinScore,
            "least_squares",
            0,
            _config.CoarseMinScore,
            out HTuple row,
            out HTuple col,
            out HTuple angle,
            out HTuple score);

        if (score.Length == 0)
        {
            return result;
        }

        result.CoarseScore = score.D;

        var mat = new HHomMat2D();
        mat.VectorAngleToRigid(
            new HTuple(_trainer.ImageCenter.Y),
            new HTuple(_trainer.ImageCenter.X),
            new HTuple(0.0),
            row,
            col,
            angle);

        var expectedCorners = _trainer.TheoreticalCorners
            .Select(corner =>
            {
                HTuple r = mat.AffineTransPoint2d(corner.Y, corner.X, out HTuple c);
                return new PointF((float)c.D, (float)r.D);
            })
            .ToList();

        var searchHalf = (_config.FineRegionSizeMm / _config.PixelSizeMm) / 2.0 +
                         _config.FineSearchMarginMm / _config.PixelSizeMm;

        var fineScores = new List<double>();
        var finalCorners = new List<PointF>();

        foreach (var expected in expectedCorners)
        {
            using var region = new HRegion();
            region.GenRectangle1(
                expected.Y - searchHalf,
                expected.X - searchHalf,
                expected.Y + searchHalf,
                expected.X + searchHalf);

            using var reduced = sceneImage.ReduceDomain(region);

            _trainer.FineModel.FindShapeModel(
                reduced,
                -_config.AngleSearchRangeRad,
                _config.AngleSearchRangeRad,
                0.0,
                1,
                _config.FineMinScore,
                "least_squares",
                0,
                _config.FineMinScore,
                out HTuple cRow,
                out HTuple cCol,
                out HTuple _,
                out HTuple cScore);

            if (cScore.Length > 0)
            {
                finalCorners.Add(new PointF((float)cCol.D, (float)cRow.D));
                fineScores.Add(cScore.D);
            }
            else
            {
                finalCorners.Add(expected);
                fineScores.Add(0);
            }
        }

        result.Corners = OrderCorners(finalCorners).ToArray();
        result.FineScores = fineScores;
        result.Transform = mat;
        return result;
    }

    private List<PointF> OrderCorners(List<PointF> centers)
    {
        var ordered = centers.OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        if (ordered.Count != 4) return ordered;
        return new List<PointF>
        {
            ordered[0],
            ordered[1],
            ordered[3],
            ordered[2]
        };
    }
}

public class TemplateMatchResult
{
    public PointF[] Corners { get; set; } = Array.Empty<PointF>();
    public HHomMat2D? Transform { get; set; }
    public double CoarseScore { get; set; }
    public List<double> FineScores { get; set; } = new();
}
