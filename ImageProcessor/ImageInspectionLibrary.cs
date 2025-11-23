using OpenCvSharp;
using System.Diagnostics;
using System.Text.Json;
using VisionLibrary.Models;
using VisionLibrary.Modules;
using VisionLibrary.Utils;

namespace VisionLibrary
{
    /// <summary>
    /// 图像检测库的主类，封装了从初始化到执行检测再到资源释放的完整生命周期。
    /// </summary>
    public class ImageInspectionLibrary : IDisposable
    {
        private ImageInspectionConfig _config = new();
        private DataParser? _dataParser;
        private ComponentDetector? _componentDetector;
        private BridgeDetector? _bridgeDetector;
        private Mat? _cadTemplateImage;

        private bool _isInitialized;
        private bool _disposed;

        /// <summary>
        /// 一个辅助类，用于在处理时将 WidthSampleResult 与其原始索引绑定在一起。
        /// </summary>
        private class SampleWithIndex
        {
            public WidthSampleResult Sample { get; }
            public int Index { get; }

            public SampleWithIndex(WidthSampleResult sample, int index)
            {
                Sample = sample;
                Index = index;
            }
        }

        public void Init(string configPath, string cadDataPath, string cadTemplateImagePath)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ImageInspectionLibrary));
            if (_isInitialized) return;

            if (File.Exists(configPath))
            {
                string jsonString = File.ReadAllText(configPath);
                _config = JsonSerializer.Deserialize<ImageInspectionConfig>(jsonString) ?? new ImageInspectionConfig();
            }

            if (!File.Exists(cadTemplateImagePath)) throw new FileNotFoundException("CAD模板图像未找到", cadTemplateImagePath);
            _cadTemplateImage = new Mat(cadTemplateImagePath, ImreadModes.Color);

            _dataParser = new DataParser(_config);
            if (!_dataParser.LoadRawCadData(cadDataPath))
            {
                throw new InvalidDataException($"无法加载或解析CAD数据文件: {cadDataPath}");
            }

            _componentDetector = new ComponentDetector(_config);
            _bridgeDetector = new BridgeDetector(_config);

            _isInitialized = true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="image"></param>
        /// <param name="cornerPoints"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public InspectionResult Run(Mat image, Point2f[] cornerPoints)
        {
            if (!_isInitialized || _disposed) throw new InvalidOperationException("库未初始化或已被释放。");

            var stopwatch = Stopwatch.StartNew();
            var finalResult = new InspectionResult();

            using Mat alignedImage = ImageAligner.ExecuteAlignmentPipeline(image, _cadTemplateImage!, cornerPoints, _config);
            finalResult.AlignedImage = alignedImage.Clone();

            ProcessImage(alignedImage, finalResult);

            stopwatch.Stop();
            finalResult.ProcessTimeSeconds = stopwatch.Elapsed.TotalSeconds;

            return finalResult;
        }

        public InspectionResult Run(Mat alignedImage)
        {
            if (!_isInitialized || _disposed) throw new InvalidOperationException("库未初始化或已被释放。");

            var stopwatch = Stopwatch.StartNew();
            var finalResult = new InspectionResult();

            ProcessImage(alignedImage, finalResult);

            stopwatch.Stop();
            finalResult.ProcessTimeSeconds = stopwatch.Elapsed.TotalSeconds;

            return finalResult;
        }

        private void ProcessImage(Mat sourceImage, InspectionResult resultContainer)
        {
            using var grayImage = new Mat();
            if (sourceImage.Channels() == 3) Cv2.CvtColor(sourceImage, grayImage, ColorConversionCodes.BGR2GRAY);
            else if (sourceImage.Channels() == 4) Cv2.CvtColor(sourceImage, grayImage, ColorConversionCodes.BGRA2GRAY);
            else sourceImage.CopyTo(grayImage);

            using var binaryImage = new Mat();
            Cv2.Threshold(grayImage, binaryImage, _config.GlobalBinaryThreshold, 255, ThresholdTypes.BinaryInv);
            var converter = new CoordinateConverter(_config);

            var componentExtremes = _dataParser!.GenerateComponentExtremesFromCad();
            var allComponentInfos = _dataParser.ConvertExtremesToComponentInfo(componentExtremes, converter);
            var bridgeLocations = _dataParser.GenerateBridgeCenterPoints(converter);

            var allCad = _dataParser.CADDataCache;

            // 1. 先取 0 + 先切 的线/圆/弧
            var candidateForWidth = allCad
                .Where(r =>
                    (r.ObjectType == "LINE" || r.ObjectType == "CIRCLE" || r.ObjectType == "ARC") &&
                    (r.ComponentId == "0" || r.ComponentId == "先切"))
                .ToList();

            // 2. 取 可掉落 的线/圆/弧
            var droppableEntities = allCad
                .Where(r =>
                    (r.ObjectType == "LINE" || r.ObjectType == "CIRCLE" || r.ObjectType == "ARC") &&
                    r.ComponentId == "可掉落")
                .ToList();

            // 3. 做一个“几何签名”，判断重合
            string GetGeomKey(CadRawData row)
            {
                var type = (row.ObjectType ?? "").Trim().ToUpper();

                return type switch
                {
                    "LINE" => $"{type}|{row.StartX:F3},{row.StartY:F3}->{row.EndX:F3},{row.EndY:F3}",
                    "CIRCLE" => $"{type}|C=({row.CenterX:F3},{row.CenterY:F3}),R={row.Radius:F3}",
                    "ARC" =>
                        $"{type}|C=({row.CenterX:F3},{row.CenterY:F3}),R={row.Radius:F3},A=({row.StartAngle:F3},{row.TotalAngle:F3})",
                    _ => type
                };
            }

            // 4. 把可掉落几何做成一个 HashSet，方便查重
            var droppableGeomSet = new HashSet<string>(
                droppableEntities.Select(GetGeomKey)
            );

            // 5. 最终用于宽度检测的 CAD：
            //    只保留 “不在可掉落几何集合中的 0/先切 图元”
            var widthCad = candidateForWidth
                .Where(r => !droppableGeomSet.Contains(GetGeomKey(r)))
                .ToList();


            var componentTask = Task.Run(() => _componentDetector!.Detect(binaryImage, allComponentInfos));
            var bridgeTask = Task.Run(() => _bridgeDetector!.Detect(binaryImage, bridgeLocations));
            var widthTask = Task.Run(() => {
                var widthDetector = new WidthDetector(_config, converter);
                return widthDetector.Detect(binaryImage, widthCad);
            });

            Task.WaitAll(componentTask, bridgeTask, widthTask);

            resultContainer.DetectedComponents = componentTask.Result;
            resultContainer.DetectedBridges = bridgeTask.Result;
            resultContainer.WidthSampleResults = widthTask.Result;
            resultContainer.AggregatedWidthDefects = AggregateWidthDefects(resultContainer.WidthSampleResults);
        }

        private List<AggregatedWidthDefect> AggregateWidthDefects(List<WidthSampleResult> allSamples)
        {
            var aggregatedDefects = new List<AggregatedWidthDefect>();

            var ngSamplesWithIndex = allSamples
                .Select((sample, index) => new SampleWithIndex(sample, index))
                .Where(x => !x.Sample.IsValid || x.Sample.WidthQualified == "F" || x.Sample.OffsetQualified == "F")
                .ToList();

            if (!ngSamplesWithIndex.Any()) return aggregatedDefects;

            foreach (var group in ngSamplesWithIndex.GroupBy(x => x.Sample.CurveId))
            {
                var sortedSamples = group.OrderBy(x => x.Index).ToList();
                if (!sortedSamples.Any()) continue;

                int segmentStart = 0;
                for (int i = 1; i < sortedSamples.Count; i++)
                {
                    if (sortedSamples[i].Index != sortedSamples[i - 1].Index + 1)
                    {
                        AddSegment(sortedSamples.GetRange(segmentStart, i - segmentStart));
                        segmentStart = i;
                    }
                }
                AddSegment(sortedSamples.GetRange(segmentStart, sortedSamples.Count - segmentStart));
            }

            void AddSegment(List<SampleWithIndex> segment)
            {
                if (segment.Count < 2) return;

                var firstSampleData = segment.First();
                var lastSampleData = segment.Last();

                var validPoints = segment
                    .Where(s => s.Sample.MidX.HasValue && s.Sample.MidY.HasValue)
                    .Select(s => new Point((int)s.Sample.MidX!.Value, (int)s.Sample.MidY!.Value))
                    .ToArray();

                if (validPoints.Length == 0) return;

                var avgWidth = segment.Where(s => s.Sample.WidthMm.HasValue).DefaultIfEmpty().Average(s => s?.Sample.WidthMm ?? 0);
                var maxOffset = segment.Where(s => s.Sample.OffsetDistanceMm.HasValue).DefaultIfEmpty().Max(s => s?.Sample.OffsetDistanceMm ?? 0);
                var box = Cv2.BoundingRect(validPoints);

                aggregatedDefects.Add(new AggregatedWidthDefect
                {
                    CurveId = firstSampleData.Sample.CurveId,
                    CurveType = firstSampleData.Sample.CurveType,
                    StartIndex = firstSampleData.Index,
                    EndIndex = lastSampleData.Index,
                    PointCount = segment.Count,
                    AverageWidthMm = avgWidth,
                    MaxOffsetMm = maxOffset,
                    DefectShapePoints = validPoints,
                    CenterPoint = new Point2f(box.X + box.Width / 2f, box.Y + box.Height / 2f)
                });
            }

            return aggregatedDefects;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cadTemplateImage?.Dispose();
                }
                _isInitialized = false;
                _disposed = true;
            }
        }
    }
}
