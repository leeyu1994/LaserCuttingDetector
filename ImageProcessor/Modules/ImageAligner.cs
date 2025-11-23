using OpenCvSharp;
using VisionLibrary.Models;

namespace VisionLibrary.Modules;

/// <summary>
///     提供静态方法，用于对实际拍摄的图像进行校正、拉伸和对齐。
/// </summary>
public static class ImageAligner
{
    /// <summary>
    ///     执行完整的对齐流程：透视校正 -> 尺寸拉伸 -> CAD图对齐。
    /// </summary>
    /// <param name="boardMat">待处理的原始图像Mat对象。</param>
    /// <param name="cadTemplateMat">CAD模板图的Mat对象。</param>
    /// <param name="sourceCorners">原始图像上电路板的四个角点坐标 (顺序: 左上, 右上, 右下, 左下)。</param>
    /// <param name="config">包含目标物理尺寸、像素大小等参数的配置对象。</param>
    /// <returns>返回最终对齐完成的 Mat 对象。调用者有责任释放此对象！</returns>
    public static Mat ExecuteAlignmentPipeline(Mat boardMat, Mat cadTemplateMat, Point2f[] sourceCorners,
        ImageInspectionConfig config)
    {
        // 步骤1: 对图像进行透视校正，使其从任意四边形变为完美的矩形
        using var correctedBoard = CorrectPerspective(boardMat, sourceCorners);
        // 步骤2: 将校正后的图像拉伸到由标准物理尺寸和像素尺寸定义的目标像素大小
        using var stretchedBoard = StretchImage(correctedBoard, config);
        // 步骤3: 将拉伸后的图像对齐并嵌入到CAD设计模板图中，同时清理背景
        var finalAlignedImage = AlignToCad(stretchedBoard, cadTemplateMat, config);

        return finalAlignedImage;
    }

    /// <summary>
    ///     步骤1：根据四个角点对图像进行透视校正。
    /// </summary>
    private static Mat CorrectPerspective(Mat inputImage, Point2f[] corners)
    {
        // corners 顺序: 0:左上, 1:右上, 2:右下, 3:左下
        var width = Math.Max(corners[1].DistanceTo(corners[0]), corners[2].DistanceTo(corners[3]));
        var height = Math.Max(corners[3].DistanceTo(corners[0]), corners[2].DistanceTo(corners[1]));
        var maxWidth = (int)Math.Round(width);
        var maxHeight = (int)Math.Round(height);

        // 定义目标矩形的四个角点
        Point2f[] destinationPoints =
        {
            new(0, 0),
            new(maxWidth - 1, 0),
            new(maxWidth - 1, maxHeight - 1),
            new(0, maxHeight - 1)
        };

        // 计算透视变换矩阵并应用
        using var matrix = Cv2.GetPerspectiveTransform(corners, destinationPoints);
        var result = new Mat();
        Cv2.WarpPerspective(inputImage, result, matrix, new Size(maxWidth, maxHeight));
        return result;
    }

    /// <summary>
    ///     步骤2：将校正后的图像拉伸到由物理尺寸定义的目标像素尺寸。
    /// </summary>
    private static Mat StretchImage(Mat inputImage, ImageInspectionConfig config)
    {
        var targetWidthPx = (int)Math.Round(config.TargetBoardWidthMm / config.PixelSize);
        var targetHeightPx = (int)Math.Round(config.TargetBoardHeightMm / config.PixelSize);

        var result = new Mat();
        Cv2.Resize(inputImage, result, new Size(targetWidthPx, targetHeightPx));
        return result;
    }

    /// <summary>
    ///     步骤3：将处理好的电路板图像对齐并嵌入到CAD模板图中。
    /// </summary>
    private static Mat AlignToCad(Mat actualImage, Mat cadImage, ImageInspectionConfig config)
    {
        // 1. 计算CAD模板图的毫米到像素的转换比例
        var cadTotalWidthMm = (float)(config.TargetBoardWidthMm + 2 * config.CuttingFrame);
        var cadTotalHeightMm = (float)(config.TargetBoardHeightMm + 2 * config.CuttingFrame);
        var pxPerMmX = cadImage.Width / cadTotalWidthMm;
        var pxPerMmY = cadImage.Height / cadTotalHeightMm;

        // 2. 定义电路板在CAD物理坐标系中的四个角点（左下角为(0,0)）
        var boardCornersMm = new Point2f[]
        {
            new(0, 0), // 左下
            new((float)config.TargetBoardWidthMm, 0), // 右下
            new(0, (float)config.TargetBoardHeightMm), // 左上
            new((float)config.TargetBoardWidthMm, (float)config.TargetBoardHeightMm) // 右上
        };

        // 3. 将物理角点(mm)转换为CAD模板图上的像素坐标(px)
        //    注意Y轴翻转：图像(0,0)在左上，CAD物理(0,0)在左下
        var cadDestinationPoints = boardCornersMm.Select(p => new Point2f(
            (float)(p.X + config.CuttingFrame) * pxPerMmX,
            cadImage.Height - (float)(p.Y + config.CuttingFrame) * pxPerMmY
        )).ToArray();

        // 4. 定义源点（即待嵌入的实际图像的四个角点）
        Point2f[] actualSourcePoints =
        {
            new(0, 0),
            new(actualImage.Width - 1, 0),
            new(actualImage.Width - 1, actualImage.Height - 1),
            new(0, actualImage.Height - 1)
        };

        // 目标点顺序必须是：左上, 右上, 右下, 左下
        Point2f[] orderedCadPoints =
            { cadDestinationPoints[2], cadDestinationPoints[3], cadDestinationPoints[1], cadDestinationPoints[0] };

        // 5. 计算变换矩阵，将实际图像warp到CAD模板的目标区域
        using var matrix = Cv2.GetPerspectiveTransform(actualSourcePoints, orderedCadPoints);
        using var warpedActual = new Mat();
        Cv2.WarpPerspective(actualImage, warpedActual, matrix, cadImage.Size());

        // 6. 创建掩码，精确地将变换后的图像粘贴到CAD背景上
        using var mask = new Mat(cadImage.Size(), MatType.CV_8UC1, Scalar.Black);
        var cadPointsInt = orderedCadPoints.Select(p => new Point((int)p.X, (int)p.Y));
        Cv2.FillPoly(mask, new[] { cadPointsInt }, Scalar.White);

        var finalImage = cadImage.Clone();
        warpedActual.CopyTo(finalImage, mask);

        // 7. 清理背景：将掩码外的所有区域（CAD模板的边框和标记）设置为纯白色，以防干扰后续检测
        using var inverseMask = new Mat();
        Cv2.BitwiseNot(mask, inverseMask);
        finalImage.SetTo(Scalar.White, inverseMask); // Scalar.White 对应 (255,255,255)

        return finalImage;
    }
}