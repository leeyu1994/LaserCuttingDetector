// Services/CADValidator.cs (完整版本)

using System.Drawing;
using System.Text;
using CadDrawPic.Config;
using CadDrawPic.Models;

namespace CadDrawPic.Services
{
    /// <summary>
    /// CAD验证器 - 完整版本
    /// </summary>
    public class CADValidator
    {
        private readonly List<string> _report = new();
        private int _totalElements = 0;
        private int _validElements = 0;

        /// <summary>
        /// 验证所有元素还原度
        /// </summary>
        public (Dictionary<int, List<string>> validationResults, float accuracy) Validate(
            List<CADElement> originalElements, List<CADElement> drawnElements)
        {
            var validationResults = new Dictionary<int, List<string>>();

            // 确保相同数量的元素
            if (originalElements.Count != drawnElements.Count)
            {
                _report.Add($"警告: 原始元素数({originalElements.Count})与绘制元素数({drawnElements.Count})不一致");
            }

            // 验证每个元素
            for (int idx = 0; idx < Math.Min(originalElements.Count, drawnElements.Count); idx++)
            {
                var orig = originalElements[idx];
                var drawn = drawnElements[idx];

                if (orig.Type != drawn.Type)
                {
                    _report.Add($"错误: 元素 {idx + 1} 类型不一致 ({orig.Type} vs {drawn.Type})");
                    continue;
                }

                List<string> results = orig.Type switch
                {
                    "line" => ValidateLine((LineElement)orig, (LineElement)drawn),
                    "arc" => ValidateArc((ArcElement)orig, (ArcElement)drawn),
                    "circle" => ValidateCircle((CircleElement)orig, (CircleElement)drawn),
                    "spline" => ValidateSpline((SplineElement)orig, (SplineElement)drawn),
                    "polyline" => ValidatePolyline((PolylineElement)orig, (PolylineElement)drawn),
                    _ => new List<string>()
                };

                validationResults[idx] = results;
                _report.Add($"\n验证元素 {idx + 1} ({orig.Type}):");
                _report.AddRange(results);
            }

            // 添加总结报告
            float accuracy = _totalElements > 0 ? (_validElements / (float)_totalElements) * 100 : 0;
            _report.Add("\n===== 验证总结 =====");
            _report.Add($"总元素数: {_totalElements}");
            _report.Add($"有效元素数: {_validElements}");
            _report.Add($"还原度: {accuracy:F2}%");

            return (validationResults, accuracy);
        }

        /// <summary>
        /// 验证直线元素
        /// </summary>
        private List<string> ValidateLine(LineElement original, LineElement drawn)
        {
            var results = new List<string>();
            bool valid = true;

            // 验证起点位置
            float startDist = CalculateDistance(original.Start, drawn.Start);
            bool startValid = startDist <= CADConfig.POSITION_TOLERANCE_MM;
            results.Add($"起点位置误差: {startDist:F4}mm ({(startValid ? "通过" : "不通过")})");
            if (!startValid) valid = false;

            // 验证终点位置
            float endDist = CalculateDistance(original.End, drawn.End);
            bool endValid = endDist <= CADConfig.POSITION_TOLERANCE_MM;
            results.Add($"终点位置误差: {endDist:F4}mm ({(endValid ? "通过" : "不通过")})");
            if (!endValid) valid = false;

            // 验证长度
            float lengthDiff = Math.Abs(original.Length - drawn.Length);
            bool lengthValid = lengthDiff <= (original.Length * CADConfig.LENGTH_TOLERANCE_RATIO);
            results.Add($"长度误差: {lengthDiff:F4}mm ({(lengthValid ? "通过" : "不通过")})");
            if (!lengthValid) valid = false;

            // 更新统计
            _totalElements++;
            if (valid) _validElements++;

            return results;
        }

        /// <summary>
        /// 验证圆弧元素
        /// </summary>
        private List<string> ValidateArc(ArcElement original, ArcElement drawn)
        {
            var results = new List<string>();
            bool valid = true;

            // 验证圆心位置
            float centerDist = CalculateDistance(original.Center, drawn.Center);
            bool centerValid = centerDist <= CADConfig.POSITION_TOLERANCE_MM;
            results.Add($"圆心位置误差: {centerDist:F4}mm ({(centerValid ? "通过" : "不通过")})");
            if (!centerValid) valid = false;

            // 验证半径
            float radiusDiff = Math.Abs(original.Radius - drawn.Radius);
            bool radiusValid = radiusDiff <= (original.Radius * CADConfig.LENGTH_TOLERANCE_RATIO);
            results.Add($"半径误差: {radiusDiff:F4}mm ({(radiusValid ? "通过" : "不通过")})");
            if (!radiusValid) valid = false;

            // 验证圆弧端点
            var (origStart, origEnd) = CalculateArcEndpoints(original.Center, original.Radius,
                original.StartAngle, original.TotalAngle);
            var (drawnStart, drawnEnd) = CalculateArcEndpoints(drawn.Center, drawn.Radius,
                drawn.StartAngle, drawn.TotalAngle);

            // 验证起点位置
            float startDist = CalculateDistance(origStart, drawnStart);
            bool startValid = startDist <= CADConfig.POSITION_TOLERANCE_MM;
            results.Add($"起点位置误差: {startDist:F4}mm ({(startValid ? "通过" : "不通过")})");
            if (!startValid) valid = false;

            // 验证终点位置
            float endDist = CalculateDistance(origEnd, drawnEnd);
            bool endValid = endDist <= CADConfig.POSITION_TOLERANCE_MM;
            results.Add($"终点位置误差: {endDist:F4}mm ({(endValid ? "通过" : "不通过")})");
            if (!endValid) valid = false;

            // 验证角度（处理周期性）
            float startAngleDiff = Math.Abs(original.StartAngle - drawn.StartAngle);
            startAngleDiff = Math.Min(startAngleDiff, 360 - startAngleDiff);
            bool startAngleValid = startAngleDiff <= CADConfig.ANGLE_TOLERANCE_DEG;
            results.Add($"起始角度误差: {startAngleDiff:F4}度 ({(startAngleValid ? "通过" : "不通过")})");
            if (!startAngleValid) valid = false;

            float totalAngleDiff = Math.Abs(original.TotalAngle - drawn.TotalAngle);
            bool totalAngleValid = totalAngleDiff <= CADConfig.ANGLE_TOLERANCE_DEG;
            results.Add($"总角度误差: {totalAngleDiff:F4}度 ({(totalAngleValid ? "通过" : "不通过")})");
            if (!totalAngleValid) valid = false;

            // 更新统计
            _totalElements++;
            if (valid) _validElements++;

            return results;
        }

        /// <summary>
        /// 验证圆元素
        /// </summary>
        private List<string> ValidateCircle(CircleElement original, CircleElement drawn)
        {
            var results = new List<string>();
            bool valid = true;

            // 验证圆心位置
            float centerDist = CalculateDistance(original.Center, drawn.Center);
            bool centerValid = centerDist <= CADConfig.POSITION_TOLERANCE_MM;
            results.Add($"圆心位置误差: {centerDist:F4}mm ({(centerValid ? "通过" : "不通过")})");
            if (!centerValid) valid = false;

            // 验证半径
            float radiusDiff = Math.Abs(original.Radius - drawn.Radius);
            bool radiusValid = radiusDiff <= (original.Radius * CADConfig.LENGTH_TOLERANCE_RATIO);
            results.Add($"半径误差: {radiusDiff:F4}mm ({(radiusValid ? "通过" : "不通过")})");
            if (!radiusValid) valid = false;

            // 更新统计
            _totalElements++;
            if (valid) _validElements++;

            return results;
        }

        /// <summary>
        /// 验证样条曲线元素
        /// </summary>
        private List<string> ValidateSpline(SplineElement original, SplineElement drawn)
        {
            var results = new List<string>();
            bool valid = true;

            // 验证控制点数量
            bool pointCountValid = original.ControlPoints.Count == drawn.ControlPoints.Count;
            results.Add($"控制点数量: 原始{original.ControlPoints.Count} vs 绘制{drawn.ControlPoints.Count} ({(pointCountValid ? "通过" : "不通过")})");
            if (!pointCountValid) valid = false;

            // 验证控制点位置
            int validPointCount = 0;
            int minPointCount = Math.Min(original.ControlPoints.Count, drawn.ControlPoints.Count);

            for (int i = 0; i < minPointCount; i++)
            {
                float dist = CalculateDistance(original.ControlPoints[i], drawn.ControlPoints[i]);
                if (dist <= CADConfig.POSITION_TOLERANCE_MM)
                {
                    validPointCount++;
                }
            }

            float pointAccuracy = minPointCount > 0 ? (validPointCount / (float)minPointCount) * 100 : 0;
            results.Add($"控制点位置精度: {pointAccuracy:F1}% ({validPointCount}/{minPointCount})");

            bool pointPositionValid = pointAccuracy >= 80; // 80%以上
            if (!pointPositionValid) valid = false;

            // 验证曲线长度
            float lengthDiff = Math.Abs(original.Length - drawn.Length);
            bool lengthValid = lengthDiff <= (original.Length * CADConfig.LENGTH_TOLERANCE_RATIO);
            results.Add($"曲线长度误差: {lengthDiff:F4}mm ({(lengthValid ? "通过" : "不通过")})");
            if (!lengthValid) valid = false;

            // 验证闭合状态
            bool closedValid = original.IsClosed == drawn.IsClosed;
            results.Add($"闭合状态: 原始{original.IsClosed} vs 绘制{drawn.IsClosed} ({(closedValid ? "通过" : "不通过")})");
            if (!closedValid) valid = false;

            // 更新统计
            _totalElements++;
            if (valid) _validElements++;

            return results;
        }

        /// <summary>
        /// 验证多段线元素
        /// </summary>
        private List<string> ValidatePolyline(PolylineElement original, PolylineElement drawn)
        {
            var results = new List<string>();
            bool valid = true;

            // 验证顶点数量
            bool pointCountValid = original.Points.Count == drawn.Points.Count;
            results.Add($"顶点数量: 原始{original.Points.Count} vs 绘制{drawn.Points.Count} ({(pointCountValid ? "通过" : "不通过")})");
            if (!pointCountValid) valid = false;

            // 验证顶点位置
            int validPointCount = 0;
            int minPointCount = Math.Min(original.Points.Count, drawn.Points.Count);

            for (int i = 0; i < minPointCount; i++)
            {
                float dist = CalculateDistance(original.Points[i], drawn.Points[i]);
                if (dist <= CADConfig.POSITION_TOLERANCE_MM)
                {
                    validPointCount++;
                }
            }

            float pointAccuracy = minPointCount > 0 ? (validPointCount / (float)minPointCount) * 100 : 0;
            results.Add($"顶点位置精度: {pointAccuracy:F1}% ({validPointCount}/{minPointCount})");

            bool pointPositionValid = pointAccuracy >= 90; // 90%以上
            if (!pointPositionValid) valid = false;

            // 验证多段线长度
            float lengthDiff = Math.Abs(original.Length - drawn.Length);
            bool lengthValid = lengthDiff <= (original.Length * CADConfig.LENGTH_TOLERANCE_RATIO);
            results.Add($"多段线长度误差: {lengthDiff:F4}mm ({(lengthValid ? "通过" : "不通过")})");
            if (!lengthValid) valid = false;

            // 验证闭合状态
            bool closedValid = original.IsClosed == drawn.IsClosed;
            results.Add($"闭合状态: 原始{original.IsClosed} vs 绘制{drawn.IsClosed} ({(closedValid ? "通过" : "不通过")})");
            if (!closedValid) valid = false;

            // 更新统计
            _totalElements++;
            if (valid) _validElements++;

            return results;
        }

        /// <summary>
        /// 计算两点之间的距离
        /// </summary>
        private float CalculateDistance(PointF point1, PointF point2)
        {
            float dx = point2.X - point1.X;
            float dy = point2.Y - point1.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// 计算圆弧的起点和终点坐标
        /// </summary>
        private (PointF start, PointF end) CalculateArcEndpoints(PointF center, float radius,
            float startAngle, float totalAngle)
        {
            // 起点计算
            float startAngleRad = (float)(startAngle * Math.PI / 180.0);
            float startX = center.X + radius * (float)Math.Cos(startAngleRad);
            float startY = center.Y + radius * (float)Math.Sin(startAngleRad);

            // 终点计算
            float endAngleRad = (float)((startAngle + totalAngle) * Math.PI / 180.0);
            float endX = center.X + radius * (float)Math.Cos(endAngleRad);
            float endY = center.Y + radius * (float)Math.Sin(endAngleRad);

            return (new PointF(startX, startY), new PointF(endX, endY));
        }

        /// <summary>
        /// 保存验证报告
        /// </summary>
        public void SaveReport(string filePath)
        {
            File.WriteAllLines(filePath, _report, Encoding.UTF8);
        }
    }
}
