// Utils/AdvancedSplineUtils.cs

using System.Drawing;

namespace VisionLibrary.CadIntegration.Models.Utils
{
    /// <summary>
    /// 高级样条曲线工具类
    /// </summary>
    public static class AdvancedSplineUtils
    {
        /// <summary>
        /// 使用Catmull-Rom样条插值
        /// </summary>
        public static List<PointF> CatmullRomSpline(List<PointF> controlPoints, int numSegments = 50)
        {
            if (controlPoints.Count < 2)
                return new List<PointF>(controlPoints);

            var result = new List<PointF>();

            // 为端点添加虚拟控制点
            var extendedPoints = new List<PointF>();

            if (controlPoints.Count >= 2)
            {
                // 起始虚拟点
                var p0 = controlPoints[0];
                var p1 = controlPoints[1];
                var virtualStart = new PointF(2 * p0.X - p1.X, 2 * p0.Y - p1.Y);
                extendedPoints.Add(virtualStart);
            }

            extendedPoints.AddRange(controlPoints);

            if (controlPoints.Count >= 2)
            {
                // 结束虚拟点
                var pn1 = controlPoints[controlPoints.Count - 2];
                var pn = controlPoints[controlPoints.Count - 1];
                var virtualEnd = new PointF(2 * pn.X - pn1.X, 2 * pn.Y - pn1.Y);
                extendedPoints.Add(virtualEnd);
            }

            // 生成样条段
            for (int i = 1; i < extendedPoints.Count - 2; i++)
            {
                var p0 = extendedPoints[i - 1];
                var p1 = extendedPoints[i];
                var p2 = extendedPoints[i + 1];
                var p3 = extendedPoints[i + 2];

                for (int j = 0; j < numSegments; j++)
                {
                    float t = j / (float)numSegments;
                    var point = CatmullRomInterpolate(p0, p1, p2, p3, t);
                    result.Add(point);
                }
            }

            // 添加最后一个点
            if (controlPoints.Count > 0)
            {
                result.Add(controlPoints[controlPoints.Count - 1]);
            }

            return result;
        }

        /// <summary>
        /// Catmull-Rom插值计算
        /// </summary>
        private static PointF CatmullRomInterpolate(PointF p0, PointF p1, PointF p2, PointF p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            float x = 0.5f * (
                (2 * p1.X) +
                (-p0.X + p2.X) * t +
                (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 +
                (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3
            );

            float y = 0.5f * (
                (2 * p1.Y) +
                (-p0.Y + p2.Y) * t +
                (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 +
                (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3
            );

            return new PointF(x, y);
        }

        /// <summary>
        /// 贝塞尔曲线插值
        /// </summary>
        public static List<PointF> BezierSpline(List<PointF> controlPoints, int numPoints = 100)
        {
            if (controlPoints.Count < 2)
                return new List<PointF>(controlPoints);

            var result = new List<PointF>();

            for (int i = 0; i < numPoints; i++)
            {
                float t = i / (float)(numPoints - 1);
                var point = DeCasteljau(controlPoints, t);
                result.Add(point);
            }

            return result;
        }

        /// <summary>
        /// De Casteljau算法计算贝塞尔曲线点
        /// </summary>
        private static PointF DeCasteljau(List<PointF> points, float t)
        {
            var tempPoints = new List<PointF>(points);

            while (tempPoints.Count > 1)
            {
                var newPoints = new List<PointF>();
                for (int i = 0; i < tempPoints.Count - 1; i++)
                {
                    float x = (1 - t) * tempPoints[i].X + t * tempPoints[i + 1].X;
                    float y = (1 - t) * tempPoints[i].Y + t * tempPoints[i + 1].Y;
                    newPoints.Add(new PointF(x, y));
                }
                tempPoints = newPoints;
            }

            return tempPoints[0];
        }

        /// <summary>
        /// 自适应样条插值（根据曲率调整点密度）
        /// </summary>
        public static List<PointF> AdaptiveSpline(List<PointF> controlPoints, float tolerance = 0.1f)
        {
            if (controlPoints.Count < 3)
                return SplineUtils.RefineSplinePoints(controlPoints, 50);

            var result = new List<PointF>();
            result.Add(controlPoints[0]);

            for (int i = 1; i < controlPoints.Count - 1; i++)
            {
                var p1 = controlPoints[i - 1];
                var p2 = controlPoints[i];
                var p3 = controlPoints[i + 1];

                // 计算曲率
                float curvature = CalculateCurvature(p1, p2, p3);

                // 根据曲率确定插值点数量
                int segmentPoints = Math.Max(2, (int)(curvature * 100 / tolerance));
                segmentPoints = Math.Min(segmentPoints, 50); // 限制最大点数

                // 在当前段内插值
                for (int j = 1; j <= segmentPoints; j++)
                {
                    float t = j / (float)segmentPoints;
                    float x = (1 - t) * p1.X + t * p2.X;
                    float y = (1 - t) * p1.Y + t * p2.Y;
                    result.Add(new PointF(x, y));
                }
            }

            result.Add(controlPoints[controlPoints.Count - 1]);
            return result;
        }

        /// <summary>
        /// 计算三点曲率
        /// </summary>
        private static float CalculateCurvature(PointF p1, PointF p2, PointF p3)
        {
            // 使用三点计算曲率的近似公式
            float a = CalculateDistance(p1, p2);
            float b = CalculateDistance(p2, p3);
            float c = CalculateDistance(p1, p3);

            if (a < 1e-6f || b < 1e-6f || c < 1e-6f)
                return 0f;

            // 使用面积公式计算曲率
            float area = Math.Abs((p2.X - p1.X) * (p3.Y - p1.Y) - (p3.X - p1.X) * (p2.Y - p1.Y)) / 2f;
            float curvature = 4 * area / (a * b * c);

            return curvature;
        }

        /// <summary>
        /// 计算两点距离
        /// </summary>
        private static float CalculateDistance(PointF p1, PointF p2)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
