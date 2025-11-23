using System.Drawing;

namespace VisionLibrary.CadIntegration.Models
{
    /// <summary>
    /// CAD元素基类
    /// </summary>
    public abstract class CADElement
    {
        public string Type { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string ComponentId { get; set; } = string.Empty;
        public abstract float CalculateLength();
    }

    /// <summary>
    /// 直线元素
    /// </summary>
    public class LineElement : CADElement
    {
        public PointF Start { get; set; }
        public PointF End { get; set; }
        public float Length { get; set; }

        public LineElement()
        {
            Type = "line";
        }

        public override float CalculateLength()
        {
            float dx = End.X - Start.X;
            float dy = End.Y - Start.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// 圆弧元素
    /// </summary>
    public class ArcElement : CADElement
    {
        public PointF Center { get; set; }
        public float Radius { get; set; }
        public float StartAngle { get; set; }   // 起始角度（度）
        public float TotalAngle { get; set; }   // 总角度（度）
        public float Length { get; set; }

        public ArcElement()
        {
            Type = "arc";
        }

        public override float CalculateLength()
        {
            return Radius * (float)(Math.PI * Math.Abs(TotalAngle) / 180.0);
        }
    }

    /// <summary>
    /// 圆元素
    /// </summary>
    public class CircleElement : CADElement
    {
        public PointF Center { get; set; }
        public float Radius { get; set; }
        public float Circumference { get; set; }

        public CircleElement()
        {
            Type = "circle";
        }

        public override float CalculateLength()
        {
            return 2 * (float)Math.PI * Radius;
        }
    }

    /// <summary>
    /// 样条曲线元素
    /// </summary>
    public class SplineElement : CADElement
    {
        public List<PointF> ControlPoints { get; set; } = new();
        public List<PointF> InterpolatedPoints { get; set; } = new();
        public bool IsClosed { get; set; } = false;
        public float Length { get; set; }

        public SplineElement()
        {
            Type = "spline";
        }

        public override float CalculateLength()
        {
            if (InterpolatedPoints.Count < 2) return 0f;

            float totalLength = 0f;
            for (int i = 1; i < InterpolatedPoints.Count; i++)
            {
                float dx = InterpolatedPoints[i].X - InterpolatedPoints[i - 1].X;
                float dy = InterpolatedPoints[i].Y - InterpolatedPoints[i - 1].Y;
                totalLength += (float)Math.Sqrt(dx * dx + dy * dy);
            }

            return totalLength;
        }
    }

    /// <summary>
    /// 多段线元素
    /// </summary>
    public class PolylineElement : CADElement
    {
        public List<PointF> Points { get; set; } = new();
        public bool IsClosed { get; set; } = false;
        public float Length { get; set; }

        public PolylineElement()
        {
            Type = "polyline";
        }

        public override float CalculateLength()
        {
            if (Points.Count < 2) return 0f;

            float totalLength = 0f;
            for (int i = 1; i < Points.Count; i++)
            {
                float dx = Points[i].X - Points[i - 1].X;
                float dy = Points[i].Y - Points[i - 1].Y;
                totalLength += (float)Math.Sqrt(dx * dx + dy * dy);
            }

            // 如果是闭合的，添加最后一段到起点的距离
            if (IsClosed && Points.Count > 2)
            {
                float dx = Points[0].X - Points[Points.Count - 1].X;
                float dy = Points[0].Y - Points[Points.Count - 1].Y;
                totalLength += (float)Math.Sqrt(dx * dx + dy * dy);
            }

            return totalLength;
        }
    }
}
