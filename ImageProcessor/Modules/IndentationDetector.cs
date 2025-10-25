// VisionLibrary/Modules/IndentationDetector.cs
using OpenCvSharp;
using VisionLibrary.Models;

namespace VisionLibrary.Modules
{
    /// <summary>
    /// 负责检测指定的压痕（线段和圆弧），并计算其几何中心和平均灰度值。
    /// </summary>
    public class IndentationDetector
    {
        private readonly ImageInspectionConfig _config;

        /// <summary>
        /// 构造函数，初始化配置。
        /// </summary>
        /// <param name="config">全局配置对象。</param>
        public IndentationDetector(ImageInspectionConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// 执行压痕检测的主方法。
        /// </summary>
        /// <param name="image">待检测的原始图像（可以是彩色或灰度）。</param>
        /// <param name="indentationData">包含压痕几何信息的CAD数据列表。</param>
        /// <returns>包含所有压痕检测结果的列表。</returns>
        public List<IndentationResult> Detect(Mat image, List<CadRawData> indentationData)
        {
            // 1. 准备灰度图像
            // 使用 using 语句确保 Mat 对象在使用后被正确释放
            using var grayImage = new Mat();
            if (image.Channels() == 3)
            {
                Cv2.CvtColor(image, grayImage, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                image.CopyTo(grayImage);
            }

            var results = new List<IndentationResult>();
            int lineWidth = _config.IndentationSampleLineWidth;

            // 2. 遍历所有CAD数据项
            foreach (var row in indentationData)
            {
                var result = IndentationResult.FromCadData(row);
                string objType = (row.ObjectType ?? "").Trim().ToUpper();

                if (objType == "LINE")
                {
                    // 确保坐标存在，避免空引用
                    if (row.StartX.HasValue && row.StartY.HasValue && row.EndX.HasValue && row.EndY.HasValue)
                    {
                        var startPoint = new Point((int)row.StartX.Value, (int)row.StartY.Value);
                        var endPoint = new Point((int)row.EndX.Value, (int)row.EndY.Value);

                        // 计算中心点
                        var center = CalculateLineCenterPoint(startPoint, endPoint);
                        result.CenterX_Result = Math.Round(center.X, 2);
                        result.CenterY_Result = Math.Round(center.Y, 2);

                        // 计算平均灰度
                        result.AverageGray = CalculateShapeAverageGray(grayImage,
                            (mask) => Cv2.Line(mask, startPoint, endPoint, Scalar.White, lineWidth));
                    }
                }
                else if (objType == "ARC")
                {
                    // 确保圆弧参数存在
                    if (row.CenterX.HasValue && row.CenterY.HasValue && row.Radius.HasValue &&
                        row.StartAngle.HasValue && row.TotalAngle.HasValue)
                    {
                        var circleCenter = new Point((int)row.CenterX.Value, (int)row.CenterY.Value);
                        var radius = (int)row.Radius.Value;
                        double startAngle = row.StartAngle.Value;
                        double totalAngle = row.TotalAngle.Value;
                        double endAngle = startAngle + totalAngle;

                        // 计算圆弧中点（Python代码中未实现，此处进行了补充）
                        var center = CalculateArcCenterPoint(circleCenter, radius, startAngle, totalAngle);
                        result.CenterX_Result = Math.Round(center.X, 2);
                        result.CenterY_Result = Math.Round(center.Y, 2);

                        // 计算平均灰度
                        var axes = new Size(radius, radius);
                        result.AverageGray = CalculateShapeAverageGray(grayImage,
                            (mask) => Cv2.Ellipse(mask, circleCenter, axes, 0, startAngle, endAngle, Scalar.White, lineWidth));
                    }
                }

                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// 计算线段的中点。
        /// </summary>
        /// <param name="startPoint">线段起点。</param>
        /// <param name="endPoint">线段终点。</param>
        /// <returns>中点坐标。</returns>
        private Point2d CalculateLineCenterPoint(Point startPoint, Point endPoint)
        {
            double x = (startPoint.X + endPoint.X) / 2.0;
            double y = (startPoint.Y + endPoint.Y) / 2.0;
            return new Point2d(x, y);
        }

        /// <summary>
        /// 计算圆弧的几何中点。
        /// </summary>
        /// <param name="center">圆心。</param>
        /// <param name="radius">半径。</param>
        /// <param name="startAngle">起始角度。</param>
        /// <param name="totalAngle">总角度。</param>
        /// <returns>圆弧的中点坐标。</returns>
        private Point2d CalculateArcCenterPoint(Point center, int radius, double startAngle, double totalAngle)
        {
            // 计算圆弧的中间角度
            double midAngleRad = (startAngle + totalAngle / 2.0) * Math.PI / 180.0;
            // 计算中点坐标
            double x = center.X + radius * Math.Cos(midAngleRad);
            double y = center.Y + radius * Math.Sin(midAngleRad);
            return new Point2d(x, y);
        }

        /// <summary>
        /// 通用的形状平均灰度计算方法。
        /// 它创建一个掩码，使用传入的委托在掩码上绘制形状，然后计算该形状区域在原图上的平均灰度值。
        /// </summary>
        /// <param name="grayImage">灰度图像。</param>
        /// <param name="drawAction">一个在Mat（掩码）上执行绘制操作的委托。</param>
        /// <returns>计算出的平均灰度值，如果区域无效则返回0。</returns>
        private double CalculateShapeAverageGray(Mat grayImage, Action<Mat> drawAction)
        {
            // 创建一个与原始图像相同大小的黑色掩码
            using var mask = new Mat(grayImage.Size(), MatType.CV_8UC1, Scalar.Black);

            // 执行传入的绘制动作（画线、画圆弧等）
            drawAction(mask);

            // 检查掩码上是否有任何白色像素，防止除以零
            if (Cv2.CountNonZero(mask) == 0)
            {
                return 0.0;
            }

            // 使用掩码计算指定区域的平均值
            Scalar averageScalar = Cv2.Mean(grayImage, mask);

            // 返回灰度值（第一个通道的值），并保留两位小数
            return Math.Round(averageScalar.Val0, 2);
        }
    }
}
