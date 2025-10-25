// Services/CADPlotter.cs (完整版本)

using CadDrawPic.Config;
using CadDrawPic.Models;
using SkiaSharp;
using System.Drawing;

namespace CadDrawPic.Services
{
    /// <summary>
    /// CAD绘图器 - 完整版本
    /// </summary>
    public class CADPlotter
    {
        private readonly float _marginMm;
        private readonly float _lineWidthPixels;
        private readonly List<CADElement> _elements = new();

        public CADPlotter(float marginMm = 0f)
        {
            _marginMm = marginMm;
            _lineWidthPixels = CADConfig.LINE_WIDTH_MM / CADConfig.PIXEL_SIZE;

            Console.WriteLine($"像素尺寸: {CADConfig.PIXEL_SIZE}mm/像素");
            Console.WriteLine($"计算DPI: {CADConfig.DPI:F2}");
            Console.WriteLine($"绘图区域: {CADConfig.PLOT_WIDTH}x{CADConfig.PLOT_HEIGHT}mm");
            Console.WriteLine($"线宽设置: {CADConfig.LINE_WIDTH_MM}mm ({_lineWidthPixels:F2}像素)");
        }

        /// <summary>
        /// 绘制所有元素
        /// </summary>
        public void DrawElements(List<CADElement> elements)
        {
            Console.WriteLine($"开始绘制{elements.Count}个元素...");
            _elements.Clear();
            _elements.AddRange(elements);
            Console.WriteLine("所有元素绘制完成");
        }

        /// <summary>
        /// 保存绘图为PNG文件
        /// </summary>
        public void SavePlot(string outputPath)
        {
            // 计算包含边距的总尺寸
            float totalWidth = CADConfig.PLOT_WIDTH + 2 * _marginMm;
            float totalHeight = CADConfig.PLOT_HEIGHT + 2 * _marginMm;

            // 计算精确像素尺寸
            int widthPx = (int)Math.Round(totalWidth / CADConfig.PIXEL_SIZE);
            int heightPx = (int)Math.Round(totalHeight / CADConfig.PIXEL_SIZE);

            // 创建SkiaSharp画布
            using var surface = SKSurface.Create(new SKImageInfo(widthPx, heightPx, SKColorType.Rgb888x));
            var canvas = surface.Canvas;

            // 设置白色背景
            canvas.Clear(SKColors.White);

            // 创建画笔
            using var paint = new SKPaint
            {
                Color = SKColors.Black,
                StrokeWidth = _lineWidthPixels,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };

            Console.WriteLine($"重新绘制元素到精确尺寸画布...");

            // 绘制所有元素
            foreach (var element in _elements)
            {
                DrawElement(canvas, paint, element, widthPx, heightPx);
            }

            // 保存图像
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(outputPath);
            data.SaveTo(stream);

            Console.WriteLine($"图像精确保存: {widthPx}x{heightPx}像素 (DPI={CADConfig.DPI:F2})");
            Console.WriteLine($"图像物理尺寸: {totalWidth:F1}x{totalHeight:F1}mm (包含{_marginMm}mm边距)");
        }

        /// <summary>
        /// 绘制单个元素
        /// </summary>
        private void DrawElement(SKCanvas canvas, SKPaint paint, CADElement element, int canvasWidth, int canvasHeight)
        {
            switch (element)
            {
                case LineElement line:
                    DrawLine(canvas, paint, line, canvasWidth, canvasHeight);
                    break;
                case ArcElement arc:
                    DrawArc(canvas, paint, arc, canvasWidth, canvasHeight);
                    break;
                case CircleElement circle:
                    DrawCircle(canvas, paint, circle, canvasWidth, canvasHeight);
                    break;
                case SplineElement spline:
                    DrawSpline(canvas, paint, spline, canvasWidth, canvasHeight);
                    break;
                case PolylineElement polyline:
                    DrawPolyline(canvas, paint, polyline, canvasWidth, canvasHeight);
                    break;
            }
        }

        /// <summary>
        /// 绘制直线
        /// </summary>
        private void DrawLine(SKCanvas canvas, SKPaint paint, LineElement line, int canvasWidth, int canvasHeight)
        {
            var start = MmToPixel(line.Start, canvasWidth, canvasHeight);
            var end = MmToPixel(line.End, canvasWidth, canvasHeight);
            canvas.DrawLine(start, end, paint);
        }

        /// <summary>
        /// 绘制圆弧
        /// </summary>
        private void DrawArc(SKCanvas canvas, SKPaint paint, ArcElement arc, int canvasWidth, int canvasHeight)
        {
            var center = MmToPixel(arc.Center, canvasWidth, canvasHeight);
            float radiusPixels = arc.Radius / CADConfig.PIXEL_SIZE;

            var rect = new SKRect(
                center.X - radiusPixels,
                center.Y - radiusPixels,
                center.X + radiusPixels,
                center.Y + radiusPixels
            );

            // SkiaSharp使用顺时针角度，需要调整Y轴翻转
            float startAngle = -arc.StartAngle; // Y轴翻转
            float sweepAngle = -arc.TotalAngle; // Y轴翻转

            canvas.DrawArc(rect, startAngle, sweepAngle, false, paint);
        }

        /// <summary>
        /// 绘制圆
        /// </summary>
        private void DrawCircle(SKCanvas canvas, SKPaint paint, CircleElement circle, int canvasWidth, int canvasHeight)
        {
            var center = MmToPixel(circle.Center, canvasWidth, canvasHeight);
            float radiusPixels = circle.Radius / CADConfig.PIXEL_SIZE;
            canvas.DrawCircle(center, radiusPixels, paint);
        }

        /// <summary>
        /// 绘制样条曲线
        /// </summary>
        private void DrawSpline(SKCanvas canvas, SKPaint paint, SplineElement spline, int canvasWidth, int canvasHeight)
        {
            if (spline.InterpolatedPoints.Count < 2) return;

            // 创建路径
            using var path = new SKPath();

            // 移动到起点
            var startPoint = MmToPixel(spline.InterpolatedPoints[0], canvasWidth, canvasHeight);
            path.MoveTo(startPoint);

            // 添加曲线段
            for (int i = 1; i < spline.InterpolatedPoints.Count; i++)
            {
                var point = MmToPixel(spline.InterpolatedPoints[i], canvasWidth, canvasHeight);
                path.LineTo(point);
            }

            // 如果是闭合曲线，闭合路径
            if (spline.IsClosed)
            {
                path.Close();
            }

            // 绘制路径
            canvas.DrawPath(path, paint);
        }

        /// <summary>
        /// 绘制多段线
        /// </summary>
        private void DrawPolyline(SKCanvas canvas, SKPaint paint, PolylineElement polyline, int canvasWidth, int canvasHeight)
        {
            if (polyline.Points.Count < 2) return;

            // 创建路径
            using var path = new SKPath();

            // 移动到起点
            var startPoint = MmToPixel(polyline.Points[0], canvasWidth, canvasHeight);
            path.MoveTo(startPoint);

            // 添加线段
            for (int i = 1; i < polyline.Points.Count; i++)
            {
                var point = MmToPixel(polyline.Points[i], canvasWidth, canvasHeight);
                path.LineTo(point);
            }

            // 如果是闭合多段线，闭合路径
            if (polyline.IsClosed)
            {
                path.Close();
            }

            // 绘制路径
            canvas.DrawPath(path, paint);
        }

        /// <summary>
        /// 将毫米坐标转换为像素坐标
        /// </summary>
        private SKPoint MmToPixel(PointF mmPoint, int canvasWidth, int canvasHeight)
        {
            // 考虑边距和坐标系转换
            float x = (mmPoint.X - CADConfig.PLOT_X_MIN + _marginMm) / CADConfig.PIXEL_SIZE;
            // Y轴翻转：SkiaSharp的Y轴向下，CAD的Y轴向上
            float y = canvasHeight - (mmPoint.Y - CADConfig.PLOT_Y_MIN + _marginMm) / CADConfig.PIXEL_SIZE;

            return new SKPoint(x, y);
        }
    }
}
