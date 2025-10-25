// VisionLibrary/Modules/ComponentDetector.cs
using OpenCvSharp;
using VisionLibrary.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionLibrary.Modules
{
    /// <summary>
    /// 负责从图像中检测各个部件。
    /// 采用多阶段策略：首先检测主要的大部件，然后处理可能粘连的部件，
    /// 接着在特定父部件内检测子部件，最后对遗漏的部件进行毯式搜索。
    /// </summary>
    public class ComponentDetector
    {
        private readonly ImageInspectionConfig _config;
        private readonly double _offsetNormPixels;
        private readonly double _searchRadiusPixels;

        // 优化点: 预先创建形态学核
        private readonly Mat _kernelOpen;
        private readonly Mat _kernelClose;
        private readonly Mat _childKernelOpen;

        // 使用空间哈希字典进行快速查找，键为网格坐标，值为理论组件信息列表
        private Dictionary<(int, int), List<ComponentInfo>> _spatialLookup = new();

        // 用于特殊处理粘连组件(A11, A12)的聚合信息
        private ComponentInfo? _divideComponentInfo = null;

        /// <summary>
        /// 构造函数，初始化配置并计算常用参数。
        /// </summary>
        /// <param name="config">全局配置对象。</param>
        public ComponentDetector(ImageInspectionConfig config)
        {
            _config = config;
            // 将配置中的物理单位(mm)预先转换为像素单位，便于后续计算
            _offsetNormPixels = _config.ComponentOffset / _config.PixelSize;
            _searchRadiusPixels = _config.ComponentSearchRadius / _config.PixelSize;

            // 预创建形态学核
            _kernelOpen = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            _kernelClose = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 7));
            _childKernelOpen = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
        }

        /// <summary>
        /// 执行部件检测的主方法。
        /// </summary>
        /// <param name="binaryImage">输入的二值化图像（目标为白色）。</param>
        /// <param name="allComponentInfos">所有部件的理论位置信息列表。</param>
        /// <returns>检测到的所有部件的结果列表。</returns>
        public List<ComponentResult> Detect(Mat binaryImage, List<ComponentInfo> allComponentInfos)
        {
            // 1. 构建空间查找字典，用于快速匹配轮廓和理论组件
            BuildSpatialLookup(allComponentInfos);

            var detectedComponents = new List<ComponentResult>();
            var detectedIds = new HashSet<string>();

            // 2. 对二值图进行形态学处理，平滑轮廓并消除噪声
            using var processedBinary = new Mat();
            Cv2.MorphologyEx(binaryImage, processedBinary, MorphTypes.Open, _kernelOpen);
            Cv2.MorphologyEx(processedBinary, processedBinary, MorphTypes.Close, _kernelClose);
            processedBinary.SaveImage("D:\\1.bmp");
            // 3. 查找最外层轮廓
            Cv2.FindContours(processedBinary, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            // 4. 第一轮：处理主要的大轮廓
            foreach (var contour in contours)
            {
                if (Cv2.ContourArea(contour) < _config.MinMainComponentArea) continue;

                Rect box = Cv2.BoundingRect(contour);

                var component = FindMatchAndCreateResult(box, detectedIds, contour);
                if (component != null)
                {
                    detectedComponents.Add(component);
                }
                // 特殊处理：如果轮廓未匹配成功，但其位置接近A11和A12的聚合区域，则尝试分割
                else if (_divideComponentInfo != null)
                {
                    var boxCenter = new Point2f(box.X + box.Width / 2.0f, box.Y + box.Height / 2.0f);
                    if (boxCenter.DistanceTo(_divideComponentInfo.Center) < _searchRadiusPixels * 1.5)
                    {
                        var dividedComponents = DivideAndProcessComponents(binaryImage, box, detectedIds);
                        detectedComponents.AddRange(dividedComponents);
                    }
                }
            }

            // 5. 第二轮：在特定父组件内检测子组件
            foreach (var parent in detectedComponents.ToList())
            {
                if (_config.ParentComponentIds.Contains(parent.CmpID))
                {
                    var childComponents = DetectChildComponents(binaryImage, parent, detectedIds);
                    detectedComponents.AddRange(childComponents);
                }
            }

            // 6. 第三轮：毯式搜索未被找到的剩余组件
            var restComponents = DetectRestComponents(binaryImage, allComponentInfos, detectedIds);
            detectedComponents.AddRange(restComponents);

            return detectedComponents;
        }

        /// <summary>
        /// 将部件理论数据构建成空间哈希字典以便快速查找。
        /// </summary>
        private void BuildSpatialLookup(List<ComponentInfo> allComponentInfos)
        {
            _spatialLookup.Clear();
            ComponentInfo? a11 = null, a12 = null;
            int cellSize = (int)_searchRadiusPixels;
            if (cellSize <= 0) cellSize = 100; // 防止除零错误

            foreach (var info in allComponentInfos)
            {
                int keyX = (int)(info.Center.X / cellSize);
                int keyY = (int)(info.Center.Y / cellSize);

                // 将组件信息加入其中心点所在的网格
                if (!_spatialLookup.ContainsKey((keyX, keyY)))
                {
                    _spatialLookup[(keyX, keyY)] = new List<ComponentInfo>();
                }
                _spatialLookup[(keyX, keyY)].Add(info);

                if (info.Id == "A11") a11 = info;
                if (info.Id == "A12") a12 = info;
            }

            // 特殊处理：为A11和A12创建一个聚合信息，用于识别它们粘连的情况
            if (a11 != null && a12 != null)
            {
                Rect combinedBox = a11.BoundingBox.Union(a12.BoundingBox);
                _divideComponentInfo = new ComponentInfo
                {
                    Id = "A11_A12_DIVIDE",
                    Center = new Point2f(combinedBox.X + combinedBox.Width / 2.0f, combinedBox.Y + combinedBox.Height / 2.0f),
                    BoundingBox = combinedBox
                };
            }
        }

        /// <summary>
        /// 当两个组件粘连成一个大轮廓时，通过垂直投影法找到最薄弱的连接处并进行分割。
        /// </summary>
        private List<ComponentResult> DivideAndProcessComponents(Mat binaryImage, Rect box, HashSet<string> detectedIds)
        {
            var newComponents = new List<ComponentResult>();
            using var roi = new Mat(binaryImage, box);

            // 1. 垂直投影：计算ROI中每一列的白色像素数量。连接处（黑色）的像素和会形成波谷。
            using var projection = new Mat();
            Cv2.Reduce(roi, projection, ReduceDimension.Row, ReduceTypes.Sum, MatType.CV_32S);

            // 2. 在ROI的中间区域(20%-80%)寻找最深的波谷（即像素和最小的位置）作为分割线
            int start = (int)(roi.Width * 0.2);
            int end = (int)(roi.Width * 0.8);
            if (start >= end) return newComponents;

            int minVal = int.MaxValue;
            int splitX = -1;
            for (int i = start; i < end; i++)
            {
                int val = projection.At<int>(0, i);
                if (val < minVal)
                {
                    minVal = val;
                    splitX = i;
                }
            }

            // 3. 如果找到了有效的分割线，则将ROI分割为左右两部分并分别处理
            if (splitX != -1)
            {
                // 处理左半部分
                Rect leftRect = new Rect(0, 0, splitX, roi.Height);
                ProcessDividedRoi(new Mat(roi, leftRect), box.TopLeft, detectedIds, newComponents);

                // 处理右半部分 (注意原点坐标需要偏移)
                Point rightOrigin = new Point(box.TopLeft.X + splitX, box.TopLeft.Y);
                Rect rightRect = new Rect(splitX, 0, roi.Width - splitX, roi.Height);
                ProcessDividedRoi(new Mat(roi, rightRect), rightOrigin, detectedIds, newComponents);
            }

            return newComponents;
        }

        /// <summary>
        /// 处理被分割后的子区域：在其中找到最大轮廓，匹配理论组件并创建结果。
        /// </summary>
        private void ProcessDividedRoi(Mat subRoi, Point originInGlobal, HashSet<string> detectedIds, List<ComponentResult> results)
        {
            using (subRoi) // 确保传入的Mat在此方法结束时被释放
            {
                if (subRoi.CountNonZero() < 5000) return;

                Cv2.FindContours(subRoi, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                if (contours.Length > 0)
                {
                    var largestContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();

                    // 将轮廓的包围盒和点坐标从ROI局部坐标系转换到全局图像坐标系
                    Rect box = Cv2.BoundingRect(largestContour);
                    box.X += originInGlobal.X;
                    box.Y += originInGlobal.Y;
                    var globalContour = largestContour.Select(p => new Point(p.X + originInGlobal.X, p.Y + originInGlobal.Y)).ToArray();

                    var component = FindMatchAndCreateResult(box, detectedIds, globalContour);
                    if (component != null)
                    {
                        results.Add(component);
                    }
                }
            }
        }

        /// <summary>
        /// 在指定的父组件区域内检测子组件。
        /// </summary>
        private List<ComponentResult> DetectChildComponents(Mat binaryImage, ComponentResult parent, HashSet<string> detectedIds)
        {
            var childComponents = new List<ComponentResult>();
            Rect roiRect = new Rect(parent.XMin, parent.YMin, parent.Width, parent.Height);

            // 确保ROI在图像范围内
            roiRect = roiRect.Intersect(new Rect(0, 0, binaryImage.Width, binaryImage.Height));
            if (roiRect.Width <= 0 || roiRect.Height <= 0) return childComponents;

            using var region = new Mat(binaryImage, roiRect);
            using var processedRegion = new Mat();

            // 子组件检测使用更小的核进行开运算
            Cv2.MorphologyEx(region, processedRegion, MorphTypes.Open, _childKernelOpen);
            Cv2.FindContours(processedRegion, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            foreach (var contour in contours)
            {
                if (Cv2.ContourArea(contour) < _config.MinChildComponentArea) continue;

                // 将轮廓的包围盒和点坐标从ROI局部坐标系转换到全局图像坐标系
                Rect box = Cv2.BoundingRect(contour);
                box.X += parent.XMin;
                box.Y += parent.YMin;
                var globalContour = contour.Select(p => new Point(p.X + parent.XMin, p.Y + parent.YMin)).ToArray();

                var child = FindMatchAndCreateResult(box, detectedIds, globalContour);
                if (child != null)
                {
                    childComponents.Add(child);
                }
            }
            return childComponents;
        }

        /// <summary>
        /// 使用"毯式搜索"来检测那些在主流程中未被找到的组件。
        /// 遍历所有未匹配的理论组件，在其理论位置附近的小范围内进行独立的轮廓查找。
        /// </summary>
        private List<ComponentResult> DetectRestComponents(Mat binaryImage, List<ComponentInfo> allComponentInfos, HashSet<string> detectedIds)
        {
            var restComponents = new List<ComponentResult>();
            var missingInfos = allComponentInfos.Where(info => !detectedIds.Contains(info.Id));

            foreach (var info in missingInfos)
            {
                // 1. 定义搜索区域（理论包围盒 + 搜索半径）并与图像边界求交集
                Rect searchBox = info.BoundingBox;
                searchBox.Inflate((int)_searchRadiusPixels, (int)_searchRadiusPixels);
                searchBox = searchBox.Intersect(new Rect(0, 0, binaryImage.Width, binaryImage.Height));
                if (searchBox.Width <= 0 || searchBox.Height <= 0) continue;

                using var region = new Mat(binaryImage, searchBox);

                // 2. 在小区域内查找轮廓
                Cv2.FindContours(region, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                if (contours.Length == 0) continue;

                // 3. 找到最大轮廓并进行匹配
                var largestContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
                if (Cv2.ContourArea(largestContour) < 1000) continue;

                Rect box = Cv2.BoundingRect(largestContour);
                box.X += searchBox.X;
                box.Y += searchBox.Y;
                var globalContour = largestContour.Select(p => new Point(p.X + searchBox.X, p.Y + searchBox.Y)).ToArray();

                // 4. 计算与理论中心的距离并创建结果
                var center = new Point2f(box.X + box.Width / 2.0f, box.Y + box.Height / 2.0f);
                double distance = center.DistanceTo(info.Center);

                // 再次确认ID未被添加，且距离在容忍范围内
                if (!detectedIds.Contains(info.Id) && distance < _searchRadiusPixels)
                {
                    restComponents.Add(CreateComponentResult(info, box, center, distance, globalContour));
                    detectedIds.Add(info.Id);
                }
            }
            return restComponents;
        }

        /// <summary>
        /// 根据检测到的包围盒，在空间查找字典中寻找最佳匹配的理论组件。
        /// </summary>
        private ComponentResult? FindMatchAndCreateResult(Rect box, HashSet<string> detectedIds, Point[] contour)
        {
            var center = new Point2f(box.X + box.Width / 2.0f, box.Y + box.Height / 2.0f);
            int keyX = (int)(center.X / _searchRadiusPixels);
            int keyY = (int)(center.Y / _searchRadiusPixels);

            ComponentInfo? bestMatch = null;
            double minDistance = _searchRadiusPixels; // 只有小于搜索半径的才算匹配

            // 搜索中心网格及其周围的8个网格，以应对组件中心在网格边界的情况
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (_spatialLookup.TryGetValue((keyX + dx, keyY + dy), out var candidates))
                    {
                        foreach (var info in candidates)
                        {
                            if (detectedIds.Contains(info.Id)) continue; // 跳过已匹配的

                            double distance = center.DistanceTo(info.Center);
                            if (distance < minDistance)
                            {
                                minDistance = distance;
                                bestMatch = info;
                            }
                        }
                    }
                }
            }

            if (bestMatch != null)
            {
                detectedIds.Add(bestMatch.Id);
                return CreateComponentResult(bestMatch, box, center, minDistance, contour);
            }

            return null;
        }

        /// <summary>
        /// 辅助方法，用于创建并填充ComponentResult对象。
        /// </summary>
        private ComponentResult CreateComponentResult(ComponentInfo info, Rect box, Point2f center, double distancePixels, Point[] contour)
        {
            return new ComponentResult
            {
                CmpID = info.Id,
                CenterX = (int)center.X,
                CenterY = (int)center.Y,
                XMin = box.X,
                YMin = box.Y,
                XMax = box.Right,
                YMax = box.Bottom,
                Width = box.Width,
                Height = box.Height,
                Area = Cv2.ContourArea(contour), // 使用轮廓面积比包围盒面积更精确
                Offset = Math.Round(distancePixels * _config.PixelSize, 3),
                Result = distancePixels <= _offsetNormPixels ? "T" : "F",
                Contour = contour
            };
        }
    }
}
