// VisionLibrary/Utils/CoordinateConverter.cs
using OpenCvSharp;
using VisionLibrary.Models;

namespace VisionLibrary.Utils
{
    /// <summary>
    /// 提供统一的坐标转换服务。
    /// 封装了从CAD物理坐标(mm)到图像像素坐标(px)的所有转换逻辑，
    /// 确保在整个应用程序中的坐标转换规则保持一致。
    /// </summary>
    public class CoordinateConverter
    {
        private readonly double _pixelSize;
        private readonly double _cuttingFrame;
        private readonly double _cadTotalHeightMm;

        /// <summary>
        /// 构造函数，初始化转换器所需的配置参数。
        /// </summary>
        /// <param name="config">包含PixelSize, CuttingFrame等参数的全局配置。</param>
        public CoordinateConverter(ImageInspectionConfig config)
        {
            _pixelSize = config.PixelSize;
            _cuttingFrame = config.CuttingFrame;
            // Y轴翻转的基准高度
            _cadTotalHeightMm = config.TargetBoardHeightMm + 2 * config.CuttingFrame;
        }

        /// <summary>
        /// 将单个CAD物理坐标点(mm)转换为图像像素坐标点(px)。
        /// </summary>
        /// <param name="cadPointMm">CAD图纸上的物理坐标点 (单位: mm)。</param>
        /// <returns>对应的图像像素坐标点。</returns>
        public Point ToPixel(Point2d cadPointMm)
        {
            // 步骤 1: 将CAD的相对坐标(相对于电路板左下角)转换为绝对物理坐标(相对于整个画布左下角)。
            double absoluteX_mm = cadPointMm.X + _cuttingFrame;
            double absoluteY_mm = cadPointMm.Y + _cuttingFrame;

            // 步骤 2: 将绝对物理坐标(mm)转换为像素坐标(px)。
            int pxX = (int)Math.Round(absoluteX_mm / _pixelSize);

            // 步骤 3: Y轴需要翻转以匹配图像坐标系(原点在左上角)。
            // (总物理高度 - 绝对Y物理坐标) / 像素尺寸
            int pxY = (int)Math.Round((_cadTotalHeightMm - absoluteY_mm) / _pixelSize);

            return new Point(pxX, pxY);
        }

        /// <summary>
        /// 将CAD物理坐标表示的包围盒(mm)转换为图像像素坐标表示的包围盒(px)。
        /// </summary>
        /// <param name="extremes">包含Min/Max物理坐标的ComponentExtremes对象。</param>
        /// <returns>对应的图像像素坐标Rect。</returns>
        public Rect ToPixelRect(ComponentExtremes extremes)
        {
            // 物理坐标的MinX, MaxY对应像素坐标的左上角
            Point pxTopLeft = ToPixel(new Point2d(extremes.MinX, extremes.MaxY));
            // 物理坐标的MaxX, MinY对应像素坐标的右下角
            Point pxBottomRight = ToPixel(new Point2d(extremes.MaxX, extremes.MinY));

            return new Rect(pxTopLeft.X, pxTopLeft.Y, pxBottomRight.X - pxTopLeft.X, pxBottomRight.Y - pxTopLeft.Y);
        }
    }
}
