using CsvHelper;
using OpenCvSharp;
using System.Globalization;
using System.Text;
using VisionLibrary.Models;
using VisionLibrary.Utils;

namespace VisionLibrary.Modules
{
    /// <summary>
    /// 负责解析CAD数据文件，并提供生成用于检测的理论数据的方法。
    /// 此类还包含自动从CAD图元中识别桥位的逻辑。
    /// </summary>
    public class DataParser
    {
        public List<CadRawData> CADDataCache { get; private set; } = new();
        private List<Bridge> _absoluteBridgesCache = new();
        private readonly ImageInspectionConfig _config;

        // 内部数据结构，用于桥位查找
        private record CadPoint(string ComponentId, string Handle, Point2d Coord);
        private record Bridge(int BridgeId, string ComponentId, Point2d Point1, Point2d Point2);

        public DataParser(ImageInspectionConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// 加载原始CAD的CSV文件。此方法会尝试不同的编码和分隔符来健壮地解析文件。
        /// </summary>
        /// <returns>如果成功加载数据，则返回true。</returns>
        public bool LoadRawCadData(string cadFilePath)
        {
            // 检查文件是否存在
            if (!File.Exists(cadFilePath)) return false;

            // 注册编码提供程序以支持GBK等编码
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // 定义要尝试的编码和分隔符数组
            var encodingsToTry = new[] { Encoding.GetEncoding("gbk"), Encoding.UTF8 };
            var delimitersToTry = new[] { ",", "\t" };

            // 循环尝试所有编码和分隔符的组合
            foreach (var encoding in encodingsToTry)
            {
                foreach (var delimiter in delimitersToTry)
                {
                    // 注意：此处的try-catch是必需的，因为它允许程序在尝试一种错误的解析方式失败后，
                    // 能继续尝试下一种组合，而不是直接崩溃。这是该功能的核心逻辑。
                    try
                    {
                        var config = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
                        {
                            Encoding = encoding,
                            Delimiter = delimiter,
                            HasHeaderRecord = true, // 文件包含表头
                        };
                        using var reader = new StreamReader(cadFilePath, encoding);
                        using var csv = new CsvReader(reader, config);
                        var records = csv.GetRecords<CadRawData>().ToList();

                        // 验证是否成功解析出有效数据
                        if (records.Count > 0 && !string.IsNullOrEmpty(records[0].ComponentId))
                        {
                            CADDataCache = records;
                            // 成功加载数据后，立即查找桥位并缓存结果
                            _absoluteBridgesCache = FindBridgesFromCad();
                            return true; // 成功加载，立即返回
                        }
                    }
                    catch
                    {
                        // 忽略解析异常并继续尝试下一种组合
                    }
                }
            }
            // 所有组合都失败了
            return false;
        }

        /// <summary>
        /// 从缓存的CAD数据生成部件的物理边界信息 (单位: mm)。
        /// </summary>
        public List<ComponentExtremes> GenerateComponentExtremesFromCad()
        {
            return CADDataCache
                .Where(row => !string.IsNullOrEmpty(row.ComponentId)) // 过滤掉没有部件ID的行
                .GroupBy(row => row.ComponentId) // 按部件ID分组
                .Select(group =>
                {
                    // 解析组内所有图元的关键点
                    var allPoints = group.SelectMany(ParseGeometricEntity).ToList();
                    if (!allPoints.Any()) return null; // 如果没有解析出任何点，则忽略该部件
                    // 根据所有关键点计算边界
                    return new ComponentExtremes
                    {
                        ComponentId = group.Key,
                        MinX = allPoints.Min(p => p.X),
                        MinY = allPoints.Min(p => p.Y),
                        MaxX = allPoints.Max(p => p.X),
                        MaxY = allPoints.Max(p => p.Y)
                    };
                })
                .Where(e => e != null) // 过滤掉为null的项
                .Select(e => e!)
                .ToList();
        }

        /// <summary>
        /// 将物理边界信息列表转换为用于检测的ComponentInfo列表（像素单位）。
        /// </summary>
        /// <param name="componentExtremes">部件的物理边界列表。</param>
        /// <param name="converter">用于坐标转换的转换器实例。</param>
        public List<ComponentInfo> ConvertExtremesToComponentInfo(List<ComponentExtremes> componentExtremes, CoordinateConverter converter)
        {
            return componentExtremes.Select(item => {
                // 使用坐标转换器将毫米单位的矩形转换为像素单位的矩形
                Rect pixelRect = converter.ToPixelRect(item);
                return new ComponentInfo
                {
                    Id = item.ComponentId,
                    Center = new Point2f(pixelRect.X + pixelRect.Width / 2.0f, pixelRect.Y + pixelRect.Height / 2.0f),
                    BoundingBox = pixelRect
                };
            }).ToList();
        }

        /// <summary>
        /// 根据CAD数据计算桥位的理论中心点像素坐标。
        /// </summary>
        /// <param name="converter">用于坐标转换的转换器实例。</param>
        /// <returns>包含桥位中心像素坐标(X, Y)和桥位ID(Z)的列表。</returns>
        public List<Point3i> GenerateBridgeCenterPoints(CoordinateConverter converter)
        {
            return _absoluteBridgesCache.Select(bridge => {
                // 计算桥位中心的物理坐标 (mm)
                var centerMm = new Point2d((bridge.Point1.X + bridge.Point2.X) / 2.0, (bridge.Point1.Y + bridge.Point2.Y) / 2.0);
                // 将物理坐标转换为像素坐标
                Point centerPx = converter.ToPixel(centerMm);
                // 创建包含ID的Point3i对象
                return new Point3i(centerPx.X, centerPx.Y, bridge.BridgeId);
            }).ToList();
        }

        #region Private Helper Methods

        /// <summary>
        /// 核心逻辑：从CAD数据中自动识别桥位。
        /// </summary>
        private List<Bridge> FindBridgesFromCad()
        {
            var bridges = new List<Bridge>();
            int bridgeIdCounter = 1;

            // 筛选出包含有效端点信息的 LINE 和 ARC
            var validEntities = CADDataCache.Where(row =>
                (row.ObjectType == "LINE" || row.ObjectType == "ARC") &&
                 row.StartX.HasValue && row.StartY.HasValue &&
                 row.EndX.HasValue && row.EndY.HasValue).ToList();

            // 按部件ID分组处理
            foreach (var group in validEntities.GroupBy(e => e.ComponentId))
            {
                // 步骤 1: 提取所有图元的端点
                var endpoints = group.SelectMany(entity => new[]
                {
                    new CadPoint(entity.ComponentId, entity.ObjectHandle, new Point2d(entity.StartX!.Value, entity.StartY!.Value)),
                    new CadPoint(entity.ComponentId, entity.ObjectHandle, new Point2d(entity.EndX!.Value, entity.EndY!.Value))
                }).ToList();

                // 步骤 2: 识别并排除连接点（多个不同图元共享的坐标点）
                var connectionCoords = endpoints
                    .GroupBy(p => p.Coord) // 按坐标分组
                    .Where(g => g.Select(p => p.Handle).Distinct().Count() > 1) // 筛选出连接点
                    .Select(g => g.Key) // 只获取坐标
                    .ToHashSet();

                // 步骤 3: 只保留“悬空”的端点
                var validEndpoints = endpoints.Where(p => !connectionCoords.Contains(p.Coord)).ToList();
                if (validEndpoints.Count < 2) continue; // 端点少于2个，无法形成桥位

                // 步骤 4: 贪心算法：找出所有符合距离要求的候选对
                var candidatePairs = new List<(int i, int j, double distance)>();
                for (int i = 0; i < validEndpoints.Count; i++)
                {
                    for (int j = i + 1; j < validEndpoints.Count; j++)
                    {
                        // 确保端点来自不同的图元
                        if (validEndpoints[i].Handle == validEndpoints[j].Handle) continue;
                        double dist = validEndpoints[i].Coord.DistanceTo(validEndpoints[j].Coord);
                        // 检查距离是否在配置的有效范围内
                        if (dist > _config.MinBridgeDistance && dist < _config.MaxBridgeDistance)
                        {
                            candidatePairs.Add((i, j, dist));
                        }
                    }
                }

                // 步骤 5: 选择最终的桥位对，按距离排序，优先选择最近的，并确保每个端点只被使用一次
                var usedIndices = new HashSet<int>();
                foreach (var pair in candidatePairs.OrderBy(p => p.distance)) // 按距离升序排序
                {
                    if (!usedIndices.Contains(pair.i) && !usedIndices.Contains(pair.j))
                    {
                        bridges.Add(new Bridge(bridgeIdCounter++, group.Key, validEndpoints[pair.i].Coord, validEndpoints[pair.j].Coord));
                        // 标记这两个端点已被使用
                        usedIndices.Add(pair.i);
                        usedIndices.Add(pair.j);
                    }
                }
            }
            return bridges;
        }

        /// <summary>
        /// 解析单个CAD图元，返回其用于确定边界框的关键坐标点。
        /// </summary>
        private IEnumerable<Point2d> ParseGeometricEntity(CadRawData row)
        {
            // 规范化对象类型字符串
            string objType = (row.ObjectType ?? "").Trim().ToUpper();
            if (objType == "LINE")
            {
                if (row.StartX.HasValue && row.StartY.HasValue) yield return new Point2d(row.StartX.Value, row.StartY.Value);
                if (row.EndX.HasValue && row.EndY.HasValue) yield return new Point2d(row.EndX.Value, row.EndY.Value);
            }
            else if (objType == "CIRCLE" && row.CenterX.HasValue && row.CenterY.HasValue && row.Radius.HasValue)
            {
                // 返回圆的四个极值点（上、下、左、右）来正确确定其边界
                double cx = row.CenterX.Value, cy = row.CenterY.Value, r = row.Radius.Value;
                yield return new Point2d(cx - r, cy); // 最左点
                yield return new Point2d(cx + r, cy); // 最右点
                yield return new Point2d(cx, cy - r); // 最下点 (注意CAD坐标系Y轴向上)
                yield return new Point2d(cx, cy + r); // 最上点
            }
            else if (objType == "ARC" && row.CenterX.HasValue && row.CenterY.HasValue && row.Radius.HasValue && row.StartAngle.HasValue && row.TotalAngle.HasValue)
            {
                // 使用完整的圆弧关键点计算逻辑
                foreach (var p in GetArcKeyPoints(row.CenterX.Value, row.CenterY.Value, row.Radius.Value, row.StartAngle.Value, row.TotalAngle.Value))
                    yield return p;
            }
        }

        /// <summary>
        /// 获取圆弧的关键点，用于精确计算其边界框。
        /// 关键点包括起点、终点，以及圆弧可能经过的0、90、180、270度坐标轴上的点。
        /// </summary>
        private IEnumerable<Point2d> GetArcKeyPoints(double cx, double cy, double r, double startAngle, double totalAngle)
        {
            double startRad = startAngle * Math.PI / 180.0;
            double endRad = (startAngle + totalAngle) * Math.PI / 180.0;

            // 返回圆弧的起点
            yield return new Point2d(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
            // 返回圆弧的终点
            yield return new Point2d(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));

            // 检查圆弧是否跨越了坐标轴（0, 90, 180, 270度）
            // 这是找到圆弧真实边界框(min/max)的关键
            for (int angleDeg = 0; angleDeg < 360; angleDeg += 90)
            {
                if (IsAngleInArc(startAngle, totalAngle, angleDeg))
                {
                    double rad = angleDeg * Math.PI / 180.0;
                    yield return new Point2d(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
                }
            }
        }

        /// <summary>
        /// 判断一个给定的角度是否位于圆弧的扫描范围内。
        /// </summary>
        private bool IsAngleInArc(double start, double total, double angle)
        {
            double end = start + total;
            // total > 0 表示逆时针圆弧
            if (total > 0)
            {
                // 将待测角度调整到与起始角度在同一个周期内
                while (angle < start) angle += 360;
                return angle <= end;
            }
            // total < 0 表示顺时针圆弧
            else
            {
                // 将待测角度调整到与起始角度在同一个周期内
                while (angle > start) angle -= 360;
                return angle >= end;
            }
        }

        #endregion
    }
}
