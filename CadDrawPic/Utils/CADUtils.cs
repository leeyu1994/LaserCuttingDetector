// Utils/CADUtils.cs (完整版本)
using System.Drawing;
using CadDrawPic.Config;
using CadDrawPic.Models;

namespace CadDrawPic.Utils
{
    /// <summary>
    /// CAD工具类 - 完整版本
    /// </summary>
    public static class CADUtils
    {
        /// <summary>
        /// 检查所有元素是否在绘图区域内
        /// </summary>
        public static List<OutOfBoundsInfo> CheckElementsInBounds(List<CADElement> elements)
        {
            var outOfBounds = new List<OutOfBoundsInfo>();

            for (int idx = 0; idx < elements.Count; idx++)
            {
                var element = elements[idx];
                var points = GetElementBoundaryPoints(element);

                foreach (var point in points)
                {
                    if (point.X < CADConfig.PLOT_X_MIN - CADConfig.PIXEL_SIZE ||
                        point.X > CADConfig.PLOT_X_MAX + CADConfig.PIXEL_SIZE ||
                        point.Y < CADConfig.PLOT_Y_MIN - CADConfig.PIXEL_SIZE ||
                        point.Y > CADConfig.PLOT_Y_MAX + CADConfig.PIXEL_SIZE)
                    {
                        outOfBounds.Add(new OutOfBoundsInfo
                        {
                            Index = idx,
                            Type = element.Type,
                            Point = point,
                            Bounds = new RectangleF(CADConfig.PLOT_X_MIN, CADConfig.PLOT_Y_MIN,
                                CADConfig.PLOT_WIDTH, CADConfig.PLOT_HEIGHT)
                        });
                    }
                }
            }

            return outOfBounds;
        }

        /// <summary>
        /// 获取元素边界点（支持所有类型）
        /// </summary>
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
        /// 获取圆弧边界点
        /// </summary>
        private static List<PointF> GetArcBoundaryPoints(ArcElement arc)
        {
            // 计算起点和终点
            float startAngleRad = (float)(arc.StartAngle * Math.PI / 180.0);
            float endAngleRad = (float)((arc.StartAngle + arc.TotalAngle) * Math.PI / 180.0);

            var start = new PointF(
                arc.Center.X + arc.Radius * (float)Math.Cos(startAngleRad),
                arc.Center.Y + arc.Radius * (float)Math.Sin(startAngleRad)
            );

            var end = new PointF(
                arc.Center.X + arc.Radius * (float)Math.Cos(endAngleRad),
                arc.Center.Y + arc.Radius * (float)Math.Sin(endAngleRad)
            );

            var boundaryPoints = new List<PointF> { start, end };

            // 检查圆弧是否跨越0°、90°、180°、270°等关键角度
            // 这些角度对应圆弧的极值点
            float[] keyAngles = { 0, 90, 180, 270 };

            foreach (float keyAngle in keyAngles)
            {
                if (IsAngleInArcRange(keyAngle, arc.StartAngle, arc.TotalAngle))
                {
                    float keyAngleRad = (float)(keyAngle * Math.PI / 180.0);
                    var extremePoint = new PointF(
                        arc.Center.X + arc.Radius * (float)Math.Cos(keyAngleRad),
                        arc.Center.Y + arc.Radius * (float)Math.Sin(keyAngleRad)
                    );
                    boundaryPoints.Add(extremePoint);
                }
            }

            return boundaryPoints;
        }

        /// <summary>
        /// 获取圆边界点
        /// </summary>
        private static List<PointF> GetCircleBoundaryPoints(CircleElement circle)
        {
            var cx = circle.Center.X;
            var cy = circle.Center.Y;
            var r = circle.Radius;

            return new List<PointF>
            {
                circle.Center,              // 圆心
                new PointF(cx + r, cy),     // 右
                new PointF(cx - r, cy),     // 左
                new PointF(cx, cy + r),     // 上
                new PointF(cx, cy - r)      // 下
            };
        }

        /// <summary>
        /// 获取样条曲线边界点
        /// </summary>
        private static List<PointF> GetSplineBoundaryPoints(SplineElement spline)
        {
            var boundaryPoints = new List<PointF>();

            // 添加控制点
            boundaryPoints.AddRange(spline.ControlPoints);

            // 添加插值点的极值点（用于更精确的边界检查）
            if (spline.InterpolatedPoints.Count > 0)
            {
                float minX = spline.InterpolatedPoints.Min(p => p.X);
                float maxX = spline.InterpolatedPoints.Max(p => p.X);
                float minY = spline.InterpolatedPoints.Min(p => p.Y);
                float maxY = spline.InterpolatedPoints.Max(p => p.Y);

                boundaryPoints.Add(new PointF(minX, minY)); // 左下角
                boundaryPoints.Add(new PointF(maxX, minY)); // 右下角
                boundaryPoints.Add(new PointF(minX, maxY)); // 左上角
                boundaryPoints.Add(new PointF(maxX, maxY)); // 右上角
            }

            return boundaryPoints;
        }

        /// <summary>
        /// 检查角度是否在圆弧范围内
        /// </summary>
        private static bool IsAngleInArcRange(float testAngle, float startAngle, float totalAngle)
        {
            // 标准化角度到0-360度
            float normalizedTest = NormalizeAngle(testAngle);
            float normalizedStart = NormalizeAngle(startAngle);
            float endAngle = NormalizeAngle(startAngle + totalAngle);

            if (totalAngle >= 360)
            {
                return true; // 完整圆或超过完整圆
            }

            // 处理跨越0度的情况
            if (normalizedStart <= endAngle)
            {
                // 正常情况：不跨越0度
                return normalizedTest >= normalizedStart && normalizedTest <= endAngle;
            }
            else
            {
                // 跨越0度的情况
                return normalizedTest >= normalizedStart || normalizedTest <= endAngle;
            }
        }

        /// <summary>
        /// 标准化角度到0-360度范围
        /// </summary>
        private static float NormalizeAngle(float angle)
        {
            while (angle < 0) angle += 360;
            while (angle >= 360) angle -= 360;
            return angle;
        }

        /// <summary>
        /// 计算两点之间的距离
        /// </summary>
        public static float CalculateDistance(PointF point1, PointF point2)
        {
            float dx = point2.X - point1.X;
            float dy = point2.Y - point1.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 计算线段长度
        /// </summary>
        public static float CalculateLineLength(LineElement line)
        {
            return CalculateDistance(line.Start, line.End);
        }

        /// <summary>
        /// 计算圆弧长度
        /// </summary>
        public static float CalculateArcLength(ArcElement arc)
        {
            return arc.Radius * (float)(Math.PI * Math.Abs(arc.TotalAngle) / 180.0);
        }

        /// <summary>
        /// 计算圆周长
        /// </summary>
        public static float CalculateCircleCircumference(CircleElement circle)
        {
            return 2 * (float)Math.PI * circle.Radius;
        }

        /// <summary>
        /// 检查点是否在矩形区域内
        /// </summary>
        public static bool IsPointInBounds(PointF point, RectangleF bounds)
        {
            return point.X >= bounds.Left && point.X <= bounds.Right &&
                   point.Y >= bounds.Top && point.Y <= bounds.Bottom;
        }

        /// <summary>
        /// 获取元素的边界矩形
        /// </summary>
        public static RectangleF GetElementBounds(CADElement element)
        {
            var points = GetElementBoundaryPoints(element);

            if (points.Count == 0)
                return RectangleF.Empty;

            float minX = points.Min(p => p.X);
            float maxX = points.Max(p => p.X);
            float minY = points.Min(p => p.Y);
            float maxY = points.Max(p => p.Y);

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// 获取所有元素的总边界矩形
        /// </summary>
        public static RectangleF GetTotalBounds(List<CADElement> elements)
        {
            if (elements.Count == 0)
                return RectangleF.Empty;

            var allPoints = new List<PointF>();

            foreach (var element in elements)
            {
                allPoints.AddRange(GetElementBoundaryPoints(element));
            }

            if (allPoints.Count == 0)
                return RectangleF.Empty;

            float minX = allPoints.Min(p => p.X);
            float maxX = allPoints.Max(p => p.X);
            float minY = allPoints.Min(p => p.Y);
            float maxY = allPoints.Max(p => p.Y);

            return new RectangleF(minX, minY, maxX - minX, maxY - minY);
        }

        /// <summary>
        /// 将角度转换为弧度
        /// </summary>
        public static float DegreesToRadians(float degrees)
        {
            return (float)(degrees * Math.PI / 180.0);
        }

        /// <summary>
        /// 将弧度转换为角度
        /// </summary>
        public static float RadiansToDegrees(float radians)
        {
            return (float)(radians * 180.0 / Math.PI);
        }

        /// <summary>
        /// 计算两个角度之间的差值（考虑360度周期性）
        /// </summary>
        public static float AngleDifference(float angle1, float angle2)
        {
            float diff = Math.Abs(angle1 - angle2);
            return Math.Min(diff, 360 - diff);
        }
    }

    /// <summary>
    /// 超出边界信息类
    /// </summary>
    public class OutOfBoundsInfo
    {
        public int Index { get; set; }
        public string Type { get; set; } = string.Empty;
        public PointF Point { get; set; }
        public RectangleF Bounds { get; set; }

        public override string ToString()
        {
            return $"元素{Index}({Type}) 点({Point.X:F2},{Point.Y:F2}) 超出边界";
        }
    }
}
