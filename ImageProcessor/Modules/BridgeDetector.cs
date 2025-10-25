// VisionLibrary/Modules/BridgeDetector.cs
using OpenCvSharp;
using VisionLibrary.Models;

namespace VisionLibrary.Modules
{
    /// <summary>
    /// 负责在指定的局部区域内检测桥位及其几何参数。
    /// </summary>
    public class BridgeDetector
    {
        private readonly ImageInspectionConfig _config;
        private readonly Mat _morphKernel; // 优化点：在构造函数中创建一次，避免循环中重复创建

        /// <summary>
        /// 构造函数，初始化配置并预先创建形态学核。
        /// </summary>
        /// <param name="config">全局配置对象。</param>
        public BridgeDetector(ImageInspectionConfig config)
        {
            _config = config;
            _morphKernel = Cv2.GetStructuringElement(MorphShapes.Rect,
                new Size(_config.BridgeMorphKernelSize, _config.BridgeMorphKernelSize));
        }

        /// <summary>
        /// 对一系列给定的桥位理论中心点执行检测。
        /// </summary>
        /// <param name="binaryImage">全局二值化图像（目标为白色）。</param>
        /// <param name="bridgeCenterPoints">包含桥位理论中心点像素坐标(X, Y)和ID(Z)的列表。</param>
        /// <returns>检测到的所有有效桥位的结果列表。</returns>
        public List<BridgeResult> Detect(Mat binaryImage, List<Point3i> bridgeCenterPoints)
        {
            var results = new List<BridgeResult>();
            int radius = _config.BridgeSearchRadius;

            foreach (var point in bridgeCenterPoints)
            {
                // 1. 根据理论中心点和搜索半径，定义并裁剪感兴趣区域(ROI)
                Rect roiRect = new Rect(point.X - radius, point.Y - radius, radius * 2, radius * 2);
                // 确保ROI在图像范围内
                roiRect = roiRect.Intersect(new Rect(0, 0, binaryImage.Width, binaryImage.Height));

                if (roiRect.Width <= 0 || roiRect.Height <= 0) continue;

                using var region = new Mat(binaryImage, roiRect);

                // 2. 对ROI进行形态学开运算，消除小的噪声点
                using var processedRegion = new Mat();
                Cv2.MorphologyEx(region, processedRegion, MorphTypes.Open, _morphKernel);

                // 3. 在处理后的ROI中寻找轮廓
                Cv2.FindContours(processedRegion, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                // 4. 分析轮廓，只保留面积符合要求的，并用最小外接矩形拟合
                var validRects = contours
                    .Where(c => Cv2.ContourArea(c) > _config.MinBridgePathArea)
                    .Select(Cv2.MinAreaRect)
                    .ToList();

                // 5. 一个有效的桥位必须由两条路径构成，因此只处理检测到两个矩形的情况
                if (validRects.Count == 2)
                {
                    var bridgeInfo = ComputeBridgeInfo(validRects, point, roiRect.Location);
                    if (bridgeInfo != null)
                    {
                        results.Add(bridgeInfo);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// 根据拟合出的两个旋转矩形，计算桥位的各项几何参数。
        /// </summary>
        /// <param name="rects">包含两个旋转矩形的列表。</param>
        /// <param name="theoryCenterPoint">桥位的理论中心点（全局坐标）。</param>
        /// <param name="roiOrigin">ROI区域在全局图像中的左上角坐标。</param>
        /// <returns>如果计算成功，返回BridgeResult对象；否则返回null。</returns>
        private BridgeResult? ComputeBridgeInfo(List<RotatedRect> rects, Point3i theoryCenterPoint, Point roiOrigin)
        {
            var rect1 = rects[0];
            var rect2 = rects[1];

            // 1. 获取两个矩形各自短边的中点
            var midPoints1 = GetShortSideMidpoints(rect1);
            var midPoints2 = GetShortSideMidpoints(rect2);

            // 2. 遍历所有组合，找到两个矩形之间距离最近的一对点，它们是桥位的两个内侧端点
            float minDistance = float.MaxValue;
            Point2f nearPoint1 = new Point2f();
            Point2f nearPoint2 = new Point2f();
            int nearIndex1 = 0, nearIndex2 = 0;

            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    double dist = midPoints1[i].DistanceTo(midPoints2[j]);
                    if (dist < minDistance)
                    {
                        minDistance = (float)dist;
                        nearPoint1 = midPoints1[i];
                        nearPoint2 = midPoints2[j];
                        nearIndex1 = i;
                        nearIndex2 = j;
                    }
                }
            }

            // 3. 计算桥位长度
            double bridgeLengthPixels = minDistance;
            double bridgeLengthMm = bridgeLengthPixels * _config.PixelSize;

            // 4. 计算桥位中心点(局部坐标)，并转换为全局坐标
            Point2f bridgeMidLocal = new Point2f((nearPoint1.X + nearPoint2.X) / 2.0f, (nearPoint1.Y + nearPoint2.Y) / 2.0f);
            Point2f bridgeMidGlobal = new Point2f(bridgeMidLocal.X + roiOrigin.X, bridgeMidLocal.Y + roiOrigin.Y);

            // 5. 计算与理论点的偏移量和偏移角度
            double bridgeOffsetPixels = bridgeMidGlobal.DistanceTo(new Point2f(theoryCenterPoint.X, theoryCenterPoint.Y));
            double bridgeAngle = Math.Atan2(bridgeMidGlobal.Y - theoryCenterPoint.Y, bridgeMidGlobal.X - theoryCenterPoint.X) * (180.0 / Math.PI);

            // 6. 计算对称性（判断桥位是否“垂直”于两侧路径）
            Point2f farPoint1 = midPoints1[1 - nearIndex1];
            Point2f farPoint2 = midPoints2[1 - nearIndex2];
            var vector1 = farPoint1 - nearPoint1;
            var vector2 = farPoint2 - nearPoint2;
            var vecToMid1 = bridgeMidLocal - nearPoint1;
            var vecToMid2 = bridgeMidLocal - nearPoint2;

            double dotV1V1 = vector1.DotProduct(vector1);
            double dotV2V2 = vector2.DotProduct(vector2);
            double t1 = dotV1V1 == 0 ? 0 : vecToMid1.DotProduct(vector1) / dotV1V1;
            double t2 = dotV2V2 == 0 ? 0 : vecToMid2.DotProduct(vector2) / dotV2V2;

            // 标量乘法 Point2f * double
            Point2f plumbPoint1 = nearPoint1 + vector1*t1;
            Point2f plumbPoint2 = nearPoint2 + vector2*t2;
            
            double plumbDistance = plumbPoint1.DistanceTo(plumbPoint2);
            bool isSymmetric = plumbDistance < _config.BridgePlumbOffset;

            // 7. 组装结果对象
            return new BridgeResult
            {
                BrID = theoryCenterPoint.Z,
                BridgeWidth1 = Math.Round(Math.Min(rect1.Size.Width, rect1.Size.Height) * _config.PixelSize, 3),
                BridgeWidth2 = Math.Round(Math.Min(rect2.Size.Width, rect2.Size.Height) * _config.PixelSize, 3),
                BridgeLength = Math.Round(bridgeLengthMm, 3),
                BridgeOffset = Math.Round(bridgeOffsetPixels * _config.PixelSize, 3),
                BridgeAngle = Math.Round(bridgeAngle, 3),
                IsSymmetric = isSymmetric ? "T" : "F",
                PlumbOffset = Math.Round(plumbDistance, 1),
                BridgeCenterX = Math.Round(bridgeMidGlobal.X, 1),
                BridgeCenterY = Math.Round(bridgeMidGlobal.Y, 1),
                Result = isSymmetric ? "T" : "F" // 当前逻辑仅判断对称性
            };
        }

        /// <summary>
        /// 获取一个旋转矩形两个短边的中点。
        /// </summary>
        /// <param name="rect">输入的旋转矩形。</param>
        /// <returns>包含两个短边中点坐标的Point2f数组。</returns>
        private Point2f[] GetShortSideMidpoints(RotatedRect rect)
        {
            // rect.Points() 返回的顶点顺序: 0:左下, 1:左上, 2:右上, 3:右下
            Point2f[] points = rect.Points();
            Point2f mid1, mid2;

            if (rect.Size.Width < rect.Size.Height)
            {
                // Width是短边，对应的边是 (p0-p1) 和 (p2-p3)
                mid1 = new Point2f((points[0].X + points[1].X) / 2, (points[0].Y + points[1].Y) / 2);
                mid2 = new Point2f((points[2].X + points[3].X) / 2, (points[2].Y + points[3].Y) / 2);
            }
            else
            {
                // Height是短边，对应的边是 (p1-p2) 和 (p3-p0)
                mid1 = new Point2f((points[1].X + points[2].X) / 2, (points[1].Y + points[2].Y) / 2);
                mid2 = new Point2f((points[3].X + points[0].X) / 2, (points[3].Y + points[0].Y) / 2);
            }
            return [mid1, mid2];
        }
    }
}
