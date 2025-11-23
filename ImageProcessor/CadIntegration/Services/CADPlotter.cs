using System.Drawing;
using SkiaSharp;
using VisionLibrary.CadIntegration.Models;
using VisionLibrary.CadIntegration.Models.Utils;

namespace VisionLibrary.CadIntegration.Services
{
    /// <summary>
    /// CAD绘图器 - 支持动态边界和修正圆弧逻辑
    /// </summary>
    public class CADPlotter
    {
        private readonly float _marginMm;
        private readonly float _lineWidthPixels;
        private readonly List<CADElement> _elements = new();
        private readonly RectangleF _plotBounds; // 【新增】用于存储动态计算的绘图边界

        /// <summary>
        /// 构造函数，接收动态计算的绘图边界
        /// </summary>
        /// <param name="plotBounds">CAD所有元素的最小外接矩形</param>
        /// <param name="marginMm">图像边距（毫米）</param>
        public CADPlotter(RectangleF plotBounds, float marginMm = 0f)
        {
            _plotBounds = plotBounds; // 【修改】保存绘图边界
            _marginMm = marginMm;
            _lineWidthPixels = CADConfig.LINE_WIDTH_MM / CADConfig.PIXEL_SIZE;

            Console.WriteLine($"像素尺寸: {CADConfig.PIXEL_SIZE}mm/像素");
            Console.WriteLine($"计算DPI: {CADConfig.DPI:F2}");
            Console.WriteLine($"动态绘图区域: {_plotBounds.Width:F2}x{_plotBounds.Height:F2}mm (X: {_plotBounds.Left:F2} to {_plotBounds.Right:F2}, Y: {_plotBounds.Top:F2} to {_plotBounds.Bottom:F2})");
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
            // 【修改】根据动态边界计算总尺寸
            float totalWidth = _plotBounds.Width + 2 * _marginMm;
            float totalHeight = _plotBounds.Height + 2 * _marginMm;

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
        /// <summary>
        /// 绘制圆弧
        /// </summary>
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

            // ========================= 核心修正 =========================
            // 这是正确的逻辑。原因如下：
            // 1. Y轴翻转：MmToPixel方法将CAD的Y轴向上翻转为屏幕的Y轴向下。这个操作相当于对图形做了一次垂直镜像。
            // 2. 角度镜像：垂直镜像会导致角度的定义翻转。CAD中0度在右侧，90度在上方；屏幕坐标中0度在右侧，但90度在下方。因此，CAD的角度 `A` 对应屏幕角度 `-A`。
            // 3. 旋转方向镜像：CAD逆时针(CCW)为正角度；而SkiaSharp顺时针(CW)为正扫描角度。垂直镜像正好将CCW变成了CW。所以，CAD的总角度 `T` 也需要取反变成 `-T` 才能匹配SkiaSharp的扫描方向。
            // 综上，起始角和总角度都需要取反。
            float startAngle = -arc.StartAngle;
            float sweepAngle = -arc.TotalAngle;
            // ==========================================================

            canvas.DrawArc(rect, startAngle, sweepAngle, false, paint);
        }


        /// <summary>
        /// 【新增】绘制圆弧的诊断标记 (圆心、起点、终点)
        /// </summary>
        private void DrawArcDiagnostics(SKCanvas canvas, ArcElement arc, int canvasWidth, int canvasHeight)
        {
            // 计算CAD坐标系下的起点和终点
            float startRad = CADUtils.DegreesToRadians(arc.StartAngle);
            float endRad = CADUtils.DegreesToRadians(arc.StartAngle + arc.TotalAngle);

            PointF startPointCad = new PointF(arc.Center.X + arc.Radius * (float)Math.Cos(startRad), arc.Center.Y + arc.Radius * (float)Math.Sin(startRad));
            PointF endPointCad = new PointF(arc.Center.X + arc.Radius * (float)Math.Cos(endRad), arc.Center.Y + arc.Radius * (float)Math.Sin(endRad));

            // 将CAD坐标转换为画布像素坐标
            SKPoint centerPx = MmToPixel(arc.Center, canvasWidth, canvasHeight);
            SKPoint startPx = MmToPixel(startPointCad, canvasWidth, canvasHeight);
            SKPoint endPx = MmToPixel(endPointCad, canvasWidth, canvasHeight);

            // 绘制圆心 (红色圆圈)
            using (var centerPaint = new SKPaint { Color = SKColors.Red, Style = SKPaintStyle.Stroke, StrokeWidth = 2 })
            {
                canvas.DrawCircle(centerPx, 5, centerPaint);
            }
            // 绘制起点 (绿色方块)
            using (var startPaint = new SKPaint { Color = SKColors.Green, Style = SKPaintStyle.Stroke, StrokeWidth = 2 })
            {
                canvas.DrawRect(startPx.X - 4, startPx.Y - 4, 8, 8, startPaint);
            }
            // 绘制终点 (蓝色方块)
            using (var endPaint = new SKPaint { Color = SKColors.Blue, Style = SKPaintStyle.Stroke, StrokeWidth = 2 })
            {
                canvas.DrawRect(endPx.X - 3, endPx.Y - 3, 6, 6, endPaint);
            }
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

        // DrawSpline 和 DrawPolyline 方法无需修改，保持原样
        private void DrawSpline(SKCanvas canvas, SKPaint paint, SplineElement spline, int canvasWidth, int canvasHeight) { if (spline.InterpolatedPoints.Count < 2) return; using var path = new SKPath(); var startPoint = MmToPixel(spline.InterpolatedPoints[0], canvasWidth, canvasHeight); path.MoveTo(startPoint); for (int i = 1; i < spline.InterpolatedPoints.Count; i++) { var point = MmToPixel(spline.InterpolatedPoints[i], canvasWidth, canvasHeight); path.LineTo(point); } if (spline.IsClosed) { path.Close(); } canvas.DrawPath(path, paint); }
        private void DrawPolyline(SKCanvas canvas, SKPaint paint, PolylineElement polyline, int canvasWidth, int canvasHeight) { if (polyline.Points.Count < 2) return; using var path = new SKPath(); var startPoint = MmToPixel(polyline.Points[0], canvasWidth, canvasHeight); path.MoveTo(startPoint); for (int i = 1; i < polyline.Points.Count; i++) { var point = MmToPixel(polyline.Points[i], canvasWidth, canvasHeight); path.LineTo(point); } if (polyline.IsClosed) { path.Close(); } canvas.DrawPath(path, paint); }


        /// <summary>
        /// 将毫米坐标转换为像素坐标
        /// </summary>
        private SKPoint MmToPixel(PointF mmPoint, int canvasWidth, int canvasHeight)
        {
            // 【修改】使用动态边界的左上角作为坐标原点
            float x = (mmPoint.X - _plotBounds.Left + _marginMm) / CADConfig.PIXEL_SIZE;
            // Y轴翻转：SkiaSharp的Y轴向下，CAD的Y轴向上
            float y = canvasHeight - (mmPoint.Y - _plotBounds.Top + _marginMm) / CADConfig.PIXEL_SIZE;

            return new SKPoint(x, y);
        }
    }
}

