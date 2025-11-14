// Utils/CADUtils.cs (最终修正版本)
using System.Drawing;
using CadDrawPic.Models;

namespace CadDrawPic.Utils
{
    /// <summary>
    /// CAD工具类 - 最终修正版本
    /// </summary>
    public static class CADUtils
    {
        private static List<PointF> GetElementBoundaryPoints(CADElement element)
        {
            return element switch
            {
                LineElement line => new List<PointF> { line.Start, line.End },
                ArcElement arc => GetArcBoundaryPoints(arc),
                CircleElement circle => GetCircleBoundaryPoints(circle),
                SplineElement spline => GetSplineBoundaryPoints(spline),
                PolylineElement polyline => new List<PointF>(polyline.Points),
                _ => new List<PointF>()
            };
        }

        /// <summary>
        /// 【核心修正】获取圆弧边界点的健壮算法
        /// </summary>
        private static List<PointF> GetArcBoundaryPoints(ArcElement arc)
        {
            var boundaryPoints = new List<PointF>();

            // 1. 总是包含起点和终点
            float startRad = DegreesToRadians(arc.StartAngle);
            float endRad = DegreesToRadians(arc.StartAngle + arc.TotalAngle);
            boundaryPoints.Add(new PointF(arc.Center.X + arc.Radius * (float)Math.Cos(startRad), arc.Center.Y + arc.Radius * (float)Math.Sin(startRad)));
            boundaryPoints.Add(new PointF(arc.Center.X + arc.Radius * (float)Math.Cos(endRad), arc.Center.Y + arc.Radius * (float)Math.Sin(endRad)));

            // 2. 检查圆弧是否跨越了四个正交点 (0°, 90°, 180°, 270°)
            //    这些点是边界的最大/最小值所在的位置
            float start = NormalizeAngle(arc.StartAngle);
            float sweep = arc.TotalAngle;

            for (int i = 0; i < 4; i++)
            {
                float cardinalAngle = i * 90;

                // 检查这个正交点是否在圆弧的扫描范围内
                if (IsAngleOnArc(cardinalAngle, start, sweep))
                {
                    float rad = DegreesToRadians(cardinalAngle);
                    boundaryPoints.Add(new PointF(arc.Center.X + arc.Radius * (float)Math.Cos(rad), arc.Center.Y + arc.Radius * (float)Math.Sin(rad)));
                }
            }
            return boundaryPoints;
        }

        /// <summary>
        /// 【新增辅助方法】判断一个角度是否在一个圆弧的扫描范围内
        /// </summary>
        private static bool IsAngleOnArc(float angle, float startAngle, float sweepAngle)
        {
            // 将所有角度标准化到 [0, 360) 范围
            angle = NormalizeAngle(angle);
            startAngle = NormalizeAngle(startAngle);

            if (sweepAngle >= 360 || sweepAngle <= -360) return true; // 如果是整圆或更大，所有点都在上面

            float endAngle = NormalizeAngle(startAngle + sweepAngle);

            if (sweepAngle > 0) // 逆时针
            {
                if (startAngle < endAngle)
                    return angle >= startAngle && angle <= endAngle;
                else // 跨越了0度
                    return angle >= startAngle || angle <= endAngle;
            }
            else // 顺时针
            {
                if (startAngle > endAngle)
                    return angle <= startAngle && angle >= endAngle;
                else // 跨越了0度
                    return angle <= startAngle || angle >= endAngle;
            }
        }

        private static List<PointF> GetCircleBoundaryPoints(CircleElement circle)
        {
            var cx = circle.Center.X; var cy = circle.Center.Y; var r = circle.Radius;
            return new List<PointF> { new PointF(cx + r, cy), new PointF(cx - r, cy), new PointF(cx, cy + r), new PointF(cx, cy - r) };
        }

        private static List<PointF> GetSplineBoundaryPoints(SplineElement spline)
        {
            if (spline.InterpolatedPoints.Count > 0) { return new List<PointF>(spline.InterpolatedPoints); }
            return new List<PointF>(spline.ControlPoints);
        }

        private static float NormalizeAngle(float angle) { float result = angle % 360; if (result < 0) { result += 360; } return result; }

        public static RectangleF GetTotalBounds(List<CADElement> elements)
        {
            if (elements.Count == 0) return RectangleF.Empty;
            var allPoints = new List<PointF>();
            foreach (var element in elements) { allPoints.AddRange(GetElementBoundaryPoints(element)); }
            if (allPoints.Count == 0) return RectangleF.Empty;
            float minX = allPoints.Min(p => p.X), maxX = allPoints.Max(p => p.X), minY = allPoints.Min(p => p.Y), maxY = allPoints.Max(p => p.Y);
            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        public static float DegreesToRadians(float degrees) => (float)(degrees * Math.PI / 180.0);
    }
}
