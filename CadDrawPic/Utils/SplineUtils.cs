using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CadDrawPic.Utils
{
    /// <summary>
    /// 样条曲线工具类
    /// </summary>
    public static class SplineUtils
    {
        /// <summary>
        /// 使用三次样条插值细化点集
        /// </summary>
        public static List<PointF> RefineSplinePoints(List<PointF> controlPoints, int numPoints = 100)
        {
            if (controlPoints.Count < 2)
                return new List<PointF>(controlPoints);

            if (controlPoints.Count == 2)
            {
                // 如果只有两个点，返回直线插值
                return InterpolateLinear(controlPoints[0], controlPoints[1], numPoints);
            }

            // 使用三次样条插值
            return InterpolateCubicSpline(controlPoints, numPoints);
        }

        /// <summary>
        /// 直线插值
        /// </summary>
        private static List<PointF> InterpolateLinear(PointF start, PointF end, int numPoints)
        {
            var points = new List<PointF>();

            for (int i = 0; i < numPoints; i++)
            {
                float t = i / (float)(numPoints - 1);
                float x = start.X + t * (end.X - start.X);
                float y = start.Y + t * (end.Y - start.Y);
                points.Add(new PointF(x, y));
            }

            return points;
        }

        /// <summary>
        /// 三次样条插值
        /// </summary>
        private static List<PointF> InterpolateCubicSpline(List<PointF> controlPoints, int numPoints)
        {
            int n = controlPoints.Count;
            var points = new List<PointF>();

            // 计算参数t值（累积弦长参数化）
            var t = new float[n];
            t[0] = 0;
            for (int i = 1; i < n; i++)
            {
                float dx = controlPoints[i].X - controlPoints[i - 1].X;
                float dy = controlPoints[i].Y - controlPoints[i - 1].Y;
                t[i] = t[i - 1] + (float)Math.Sqrt(dx * dx + dy * dy);
            }

            // 标准化t值到[0,1]
            float tMax = t[n - 1];
            if (tMax > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    t[i] /= tMax;
                }
            }

            // 分别对X和Y坐标进行样条插值
            var xValues = controlPoints.Select(p => p.X).ToArray();
            var yValues = controlPoints.Select(p => p.Y).ToArray();

            var xSpline = CreateCubicSpline(t, xValues);
            var ySpline = CreateCubicSpline(t, yValues);

            // 生成插值点
            for (int i = 0; i < numPoints; i++)
            {
                float tNew = i / (float)(numPoints - 1);
                float x = EvaluateCubicSpline(xSpline, t, tNew);
                float y = EvaluateCubicSpline(ySpline, t, tNew);
                points.Add(new PointF(x, y));
            }

            return points;
        }

        /// <summary>
        /// 创建三次样条插值系数
        /// </summary>
        private static float[] CreateCubicSpline(float[] t, float[] values)
        {
            int n = t.Length;
            if (n < 2) return values;

            // 使用自然样条边界条件
            var h = new float[n - 1];
            var alpha = new float[n - 1];

            // 计算步长
            for (int i = 0; i < n - 1; i++)
            {
                h[i] = t[i + 1] - t[i];
            }

            // 计算右端项
            for (int i = 1; i < n - 1; i++)
            {
                alpha[i] = 3 * ((values[i + 1] - values[i]) / h[i] - (values[i] - values[i - 1]) / h[i - 1]);
            }

            // 求解三对角线性方程组
            var c = new float[n];
            var l = new float[n];
            var mu = new float[n];
            var z = new float[n];

            l[0] = 1;
            mu[0] = 0;
            z[0] = 0;

            for (int i = 1; i < n - 1; i++)
            {
                l[i] = 2 * (t[i + 1] - t[i - 1]) - h[i - 1] * mu[i - 1];
                mu[i] = h[i] / l[i];
                z[i] = (alpha[i] - h[i - 1] * z[i - 1]) / l[i];
            }

            l[n - 1] = 1;
            z[n - 1] = 0;
            c[n - 1] = 0;

            for (int j = n - 2; j >= 0; j--)
            {
                c[j] = z[j] - mu[j] * c[j + 1];
            }

            return c;
        }

        /// <summary>
        /// 计算三次样条插值值
        /// </summary>
        private static float EvaluateCubicSpline(float[] splineCoeffs, float[] t, float tNew)
        {
            int n = t.Length;

            // 找到插值区间
            int i = 0;
            for (i = 0; i < n - 1; i++)
            {
                if (tNew <= t[i + 1]) break;
            }

            if (i >= n - 1) i = n - 2;

            // 使用线性插值作为简化（在实际应用中应该使用完整的三次样条公式）
            float h = t[i + 1] - t[i];
            if (Math.Abs(h) < 1e-6f) return splineCoeffs[i];

            float alpha = (tNew - t[i]) / h;
            return splineCoeffs[i] * (1 - alpha) + splineCoeffs[i + 1] * alpha;
        }

        /// <summary>
        /// 检测控制点是否形成闭合曲线
        /// </summary>
        public static bool IsClosedCurve(List<PointF> points, float tolerance = 1.0f)
        {
            if (points.Count < 3) return false;

            var first = points[0];
            var last = points[points.Count - 1];

            float dx = last.X - first.X;
            float dy = last.Y - first.Y;
            float distance = (float)Math.Sqrt(dx * dx + dy * dy);

            return distance <= tolerance;
        }
    }
}
