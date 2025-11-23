using SkiaSharp;
using System.Drawing;
using VisionLibrary.CadIntegration.Models;
using VisionLibrary.CadIntegration.Models.Utils;

namespace VisionLibrary.CadIntegration.Services;

/// <summary>
/// CAD 绘图器：支持将 CAD 基本元素绘制到内存图像或文件。
/// </summary>
public class CADPlotter
{
    private readonly float _marginMm;
    private readonly float _lineWidthPixels;
    private readonly float _pixelSize;
    private readonly RectangleF _plotBounds;
    private readonly List<CADElement> _elements = new();

    public CADPlotter(
        RectangleF plotBounds,
        float marginMm = 0f,
        float? pixelSizeOverride = null,
        float? lineWidthOverrideMm = null)
    {
        _plotBounds = plotBounds;
        _marginMm = marginMm;
        _pixelSize = pixelSizeOverride ?? CADConfig.PIXEL_SIZE;
        var lineWidthMm = lineWidthOverrideMm ?? CADConfig.LINE_WIDTH_MM;
        _lineWidthPixels = lineWidthMm / _pixelSize;

        Console.WriteLine($"动态绘图区域: {_plotBounds.Width:F2}x{_plotBounds.Height:F2}mm");
        Console.WriteLine($"像素尺寸: {_pixelSize}mm/px, 线宽: {lineWidthMm}mm({_lineWidthPixels:F2}px)");
    }

    public void DrawElements(IEnumerable<CADElement> elements)
    {
        _elements.Clear();
        _elements.AddRange(elements);
    }

    public void SavePlot(string outputPath)
    {
        using var image = RenderToImage();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write);
        data.SaveTo(stream);
    }

    /// <summary>
    /// 返回绘制好的 SKImage，调用方负责释放。
    /// </summary>
    public SKImage GetRenderedImage()
    {
        return RenderToImage();
    }

    private SKImage RenderToImage()
    {
        float totalWidth = _plotBounds.Width + 2 * _marginMm;
        float totalHeight = _plotBounds.Height + 2 * _marginMm;

        int widthPx = (int)Math.Round(totalWidth / _pixelSize);
        int heightPx = (int)Math.Round(totalHeight / _pixelSize);

        var info = new SKImageInfo(widthPx, heightPx, SKColorType.Rgba8888);
        using var surface = SKSurface.Create(info);
        var canvas = surface.Canvas;

        canvas.Clear(SKColors.White);

        using var paint = new SKPaint
        {
            Color = SKColors.Black,
            StrokeWidth = _lineWidthPixels,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };

        foreach (var element in _elements)
        {
            DrawElement(canvas, paint, element, heightPx);
        }

        return surface.Snapshot();
    }

    private void DrawElement(SKCanvas canvas, SKPaint paint, CADElement element, int canvasHeight)
    {
        switch (element)
        {
            case LineElement line:
                DrawLine(canvas, paint, line, canvasHeight);
                break;
            case ArcElement arc:
                DrawArc(canvas, paint, arc, canvasHeight);
                break;
            case CircleElement circle:
                DrawCircle(canvas, paint, circle, canvasHeight);
                break;
            case SplineElement spline:
                DrawSpline(canvas, paint, spline, canvasHeight);
                break;
            case PolylineElement polyline:
                DrawPolyline(canvas, paint, polyline, canvasHeight);
                break;
        }
    }

    private void DrawLine(SKCanvas canvas, SKPaint paint, LineElement line, int canvasHeight)
    {
        var start = MmToPixel(line.Start, canvasHeight);
        var end = MmToPixel(line.End, canvasHeight);
        canvas.DrawLine(start, end, paint);
    }

    private void DrawArc(SKCanvas canvas, SKPaint paint, ArcElement arc, int canvasHeight)
    {
        var center = MmToPixel(arc.Center, canvasHeight);
        float radiusPixels = arc.Radius / _pixelSize;

        var rect = new SKRect(
            center.X - radiusPixels,
            center.Y - radiusPixels,
            center.X + radiusPixels,
            center.Y + radiusPixels
        );

        float startAngle = -arc.StartAngle;
        float sweepAngle = -arc.TotalAngle;

        canvas.DrawArc(rect, startAngle, sweepAngle, false, paint);
    }

    private void DrawCircle(SKCanvas canvas, SKPaint paint, CircleElement circle, int canvasHeight)
    {
        var center = MmToPixel(circle.Center, canvasHeight);
        float radiusPixels = circle.Radius / _pixelSize;
        canvas.DrawCircle(center, radiusPixels, paint);
    }

    private void DrawSpline(SKCanvas canvas, SKPaint paint, SplineElement spline, int canvasHeight)
    {
        if (spline.InterpolatedPoints.Count < 2) return;
        using var path = new SKPath();
        var startPoint = MmToPixel(spline.InterpolatedPoints[0], canvasHeight);
        path.MoveTo(startPoint);

        for (int i = 1; i < spline.InterpolatedPoints.Count; i++)
        {
            var point = MmToPixel(spline.InterpolatedPoints[i], canvasHeight);
            path.LineTo(point);
        }

        if (spline.IsClosed) path.Close();
        canvas.DrawPath(path, paint);
    }

    private void DrawPolyline(SKCanvas canvas, SKPaint paint, PolylineElement polyline, int canvasHeight)
    {
        if (polyline.Points.Count < 2) return;
        using var path = new SKPath();
        var startPoint = MmToPixel(polyline.Points[0], canvasHeight);
        path.MoveTo(startPoint);

        for (int i = 1; i < polyline.Points.Count; i++)
        {
            var point = MmToPixel(polyline.Points[i], canvasHeight);
            path.LineTo(point);
        }

        if (polyline.IsClosed) path.Close();
        canvas.DrawPath(path, paint);
    }

    private SKPoint MmToPixel(PointF mmPoint, int canvasHeight)
    {
        float x = (mmPoint.X - _plotBounds.Left + _marginMm) / _pixelSize;
        float y = canvasHeight - (mmPoint.Y - _plotBounds.Top + _marginMm) / _pixelSize;
        return new SKPoint(x, y);
    }
}
