using VisionLibrary.CadIntegration.Models.Utils;

namespace VisionLibrary.CadIntegration.Config;

/// <summary>
/// 配置 CAD 模板生成与 Halcon 定位的参数。
/// </summary>
public class CadTemplateConfig
{
    /// <summary>CAD 像素尺寸，单位 mm/px。</summary>
    public float PixelSizeMm { get; set; } = CADConfig.PIXEL_SIZE;

    /// <summary>绘制 CAD 线宽，单位 mm。</summary>
    public float LineWidthMm { get; set; } = CADConfig.LINE_WIDTH_MM;

    /// <summary>绘图边距，单位 mm。</summary>
    public float CanvasMarginMm { get; set; } = CADConfig.MARGIN_MM;

    /// <summary>用于构成四个角十字线的部件 ID。</summary>
    public string VerificationComponentId { get; set; } = "最后验证";

    /// <summary>聚合同一十字线两条线段的距离容差，单位 mm。</summary>
    public float CrossMergeToleranceMm { get; set; } = 0.8f;

    /// <summary>粗定位模板矩形边长，单位 mm。</summary>
    public float CoarseRegionSizeMm { get; set; } = 24f;

    /// <summary>精定位模板矩形边长，单位 mm。</summary>
    public float FineRegionSizeMm { get; set; } = 14f;

    /// <summary>精定位搜索窗口的额外边距，单位 mm。</summary>
    public float FineSearchMarginMm { get; set; } = 8f;

    /// <summary>粗定位模板匹配的最小得分。</summary>
    public double CoarseMinScore { get; set; } = 0.8;

    /// <summary>精定位模板匹配的最小得分。</summary>
    public double FineMinScore { get; set; } = 0.65;

    /// <summary>粗定位角度搜索范围（弧度），会在正负区间内搜索。</summary>
    public double AngleSearchRangeRad { get; set; } = 0.5;

    /// <summary>生成模板时是否仅绘制验证十字线，避免干扰。</summary>
    public bool DrawOnlyVerificationLines { get; set; } = true;
}
