using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using OpenCvSharp;
using VisionLibrary.Models;
using VisionLibrary.Utils;

namespace VisionLibrary.Modules;

/// <summary>
///     负责解析CAD数据文件，并提供生成用于检测的理论数据的方法。
///     此类还包含自动从CAD图元中识别桥位的逻辑。
/// </summary>
public class DataParser(ImageInspectionConfig config)
{
    private List<Bridge> _absoluteBridgesCache = new();
    public List<CadRawData> CADDataCache { get; private set; } = new();

    /// <summary>
    ///     加载原始CAD的CSV文件。此方法会尝试不同的编码和分隔符来健壮地解析文件。
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
        foreach (var delimiter in delimitersToTry)
            // 注意：此处的try-catch是必需的，因为它允许程序在尝试一种错误的解析方式失败后，
            // 能继续尝试下一种组合，而不是直接崩溃。这是该功能的核心逻辑。
            try
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    Encoding = encoding,
                    Delimiter = delimiter,
                    HasHeaderRecord = true // 文件包含表头
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

        // 所有组合都失败了
        return false;
    }

    /// <summary>
    ///     从缓存的CAD数据生成部件的物理边界信息 (单位: mm)。
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
    ///     为“宽度检测”准备CAD数据：
    ///     只保留 ComponentId 为 "0" 或 "先切" 的 LINE/CIRCLE/ARC，
    ///     并剔除与 ComponentId == "可掉落" 重合的几何图元。
    /// </summary>
    public List<CadRawData> GetCadForWidthDetection()
    {
        // 1. 先取 0 + 先切 的线/圆/弧
        var candidateForWidth = CADDataCache
            .Where(r =>
                (r.ObjectType == "LINE" || r.ObjectType == "CIRCLE" || r.ObjectType == "ARC") &&
                (r.ComponentId == "0" || r.ComponentId == "先切"))
            .ToList();

        // 2. 取 可掉落 的线/圆/弧
        var droppableEntities = CADDataCache
            .Where(r =>
                (r.ObjectType == "LINE" || r.ObjectType == "CIRCLE" || r.ObjectType == "ARC") &&
                r.ComponentId == "可掉落")
            .ToList();

        // 3. 把“可掉落”的几何做成签名集合，用于几何重合判断
        var droppableGeomSet = new HashSet<string>(
            droppableEntities.Select(BuildGeometrySignatureForDedup)
        );

        // 4. 最终用于宽度检测的 CAD：
        //    只保留 “不在可掉落几何集合中的 0/先切 图元”
        var widthCad = candidateForWidth
            .Where(r => !droppableGeomSet.Contains(BuildGeometrySignatureForDedup(r)))
            .ToList();

        return widthCad;
    }

    /// <summary>
    ///     为“宽度检测”去重生成的几何签名。
    ///     注意：只用于同一套CAD数据内部比较，不适合作为全局唯一ID。
    /// </summary>
    private static string BuildGeometrySignatureForDedup(CadRawData row)
    {
        var type = (row.ObjectType ?? string.Empty).Trim().ToUpperInvariant();

        switch (type)
        {
            case "LINE":
            {
                // 线段：考虑起终点交换的情况，做一个排序，避免 A->B 和 B->A 被当成不同线
                var x1 = row.StartX ?? 0;
                var y1 = row.StartY ?? 0;
                var x2 = row.EndX ?? 0;
                var y2 = row.EndY ?? 0;

                // 保证 (x1,y1) 是“较小”的那个点
                if (x1 > x2 || (Math.Abs(x1 - x2) < 1e-6 && y1 > y2))
                {
                    (x1, x2) = (x2, x1);
                    (y1, y2) = (y2, y1);
                }

                return $"{type}|{x1:F3},{y1:F3}->{x2:F3},{y2:F3}";
            }

            case "CIRCLE":
            {
                var cx = row.CenterX ?? 0;
                var cy = row.CenterY ?? 0;
                var r = row.Radius ?? 0;
                return $"{type}|C=({cx:F3},{cy:F3}),R={r:F3}";
            }

            case "ARC":
            {
                var cx = row.CenterX ?? 0;
                var cy = row.CenterY ?? 0;
                var r = row.Radius ?? 0;
                var sa = row.StartAngle ?? 0;
                var ta = row.TotalAngle ?? 0;
                return $"{type}|C=({cx:F3},{cy:F3}),R={r:F3},A=({sa:F3},{ta:F3})";
            }

            default:
                return type;
        }
    }


    /// <summary>
    ///     将物理边界信息列表转换为用于检测的ComponentInfo列表（像素单位）。
    /// </summary>
    /// <param name="componentExtremes">部件的物理边界列表。</param>
    /// <param name="converter">用于坐标转换的转换器实例。</param>
    public List<ComponentInfo> ConvertExtremesToComponentInfo(List<ComponentExtremes> componentExtremes,
        CoordinateConverter converter)
    {
        return componentExtremes.Select(item =>
        {
            // 使用坐标转换器将毫米单位的矩形转换为像素单位的矩形
            var pixelRect = converter.ToPixelRect(item);
            return new ComponentInfo
            {
                Id = item.ComponentId,
                Center = new Point2f(pixelRect.X + pixelRect.Width / 2.0f, pixelRect.Y + pixelRect.Height / 2.0f),
                BoundingBox = pixelRect
            };
        }).ToList();
    }

    /// <summary>
    ///     根据CAD数据计算桥位的理论中心点像素坐标。
    /// </summary>
    /// <param name="converter">用于坐标转换的转换器实例。</param>
    /// <returns>包含桥位中心像素坐标(X, Y)和桥位ID(Z)的列表。</returns>
    public List<Point3i> GenerateBridgeCenterPoints(CoordinateConverter converter)
    {
        return _absoluteBridgesCache.Select(bridge =>
        {
            // 计算桥位中心的物理坐标 (mm)
            var centerMm = new Point2d((bridge.Point1.X + bridge.Point2.X) / 2.0,
                (bridge.Point1.Y + bridge.Point2.Y) / 2.0);
            // 将物理坐标转换为像素坐标
            var centerPx = converter.ToPixel(centerMm);
            // 创建包含ID的Point3i对象
            return new Point3i(centerPx.X, centerPx.Y, bridge.BridgeId);
        }).ToList();
    }

    /// <summary>
    ///     把坐标量化成字符串key，用于做字典 / 去重（避免直接用double比较）
    ///     精度：0.001mm 级别
    /// </summary>
    private static string QuantizePointKey(Point2d p, int decimals = 3)
    {
        // 修复方案1：使用显式格式化和不变区域性
        var format = "F" + decimals;
        return
            $"{Math.Round(p.X, decimals).ToString(format, CultureInfo.InvariantCulture)},{Math.Round(p.Y, decimals).ToString(format, CultureInfo.InvariantCulture)}";
    }


    /// <summary>
    ///     从 ComponentId 为 "0" / "先切" 的图元中，
    ///     根据几何连通关系 + 桥位，把它们自动分成多个“逻辑组件”。
    ///     一个逻辑组件大致对应你原来手动分的 A1、A2、D1 这种拼装块。
    /// </summary>
    public List<LogicalComponent> BuildLogicalComponentsFromMainCuts()
    {
        var logicalComponents = new List<LogicalComponent>();

        // 1. 只取 "0" 和 "先切" 的 LINE / ARC 图元
        var mainCutEntities = CADDataCache
            .Where(r =>
                (r.ObjectType == "LINE" || r.ObjectType == "ARC") &&
                (r.ComponentId == "0" || r.ComponentId == "先切") &&
                r.StartX.HasValue && r.StartY.HasValue &&
                r.EndX.HasValue && r.EndY.HasValue)
            .ToList();

        if (mainCutEntities.Count == 0)
            return logicalComponents;

        // 2. 建立“点 -> 节点索引”的映射
        var nodeKeyToIndex = new Dictionary<string, int>();
        var nodes = new List<Point2d>();

        int GetNodeIndex(Point2d p)
        {
            var key = QuantizePointKey(p);
            if (!nodeKeyToIndex.TryGetValue(key, out var idx))
            {
                idx = nodes.Count;
                nodes.Add(p);
                nodeKeyToIndex[key] = idx;
            }

            return idx;
        }

        // 3. 边列表（每条边对应一条图元或桥位）
        //    edge.NodeA / NodeB 是两个端点索引，Entity 为空表示这是桥位虚拟边
        var edges = new List<(int NodeA, int NodeB, CadRawData? Entity)>();

        // 3.1 把所有 LINE / ARC 作为边加入图
        foreach (var entity in mainCutEntities)
        {
            var p1 = new Point2d(entity.StartX!.Value, entity.StartY!.Value);
            var p2 = new Point2d(entity.EndX!.Value, entity.EndY!.Value);

            var n1 = GetNodeIndex(p1);
            var n2 = GetNodeIndex(p2);

            edges.Add((n1, n2, entity));
        }

        // 3.2 把桥位作为“虚拟边”加入图，只为连通，不对应具体 CadRawData
        foreach (var bridge in _absoluteBridgesCache)
        {
            // 只考虑 0 / 先切 的桥位
            if (bridge.ComponentId != "0" && bridge.ComponentId != "先切")
                continue;

            var n1 = GetNodeIndex(bridge.Point1);
            var n2 = GetNodeIndex(bridge.Point2);

            edges.Add((n1, n2, null));
        }

        // 4. 建立邻接表：节点 -> 边索引列表
        var adj = new List<List<int>>(new List<int>[nodes.Count]);
        for (var i = 0; i < nodes.Count; i++)
            adj[i] = new List<int>();

        for (var eIdx = 0; eIdx < edges.Count; eIdx++)
        {
            var (a, b, _) = edges[eIdx];
            adj[a].Add(eIdx);
            adj[b].Add(eIdx);
        }

        // 5. 在“点-边图”上做连通域，每个连通域 = 一个逻辑组件
        var visitedNode = new bool[nodes.Count];
        var componentSeq0 = 0;
        var componentSeqFirstCut = 0;

        for (var startNode = 0; startNode < nodes.Count; startNode++)
        {
            if (visitedNode[startNode])
                continue;

            var queue = new Queue<int>();
            var nodeSet = new HashSet<int>();
            var edgeSet = new HashSet<int>();

            queue.Enqueue(startNode);
            visitedNode[startNode] = true;

            while (queue.Count > 0)
            {
                var n = queue.Dequeue();
                nodeSet.Add(n);

                foreach (var eIdx in adj[n])
                {
                    edgeSet.Add(eIdx);
                    var edge = edges[eIdx];
                    var other = edge.NodeA == n ? edge.NodeB : edge.NodeA;

                    if (!visitedNode[other])
                    {
                        visitedNode[other] = true;
                        queue.Enqueue(other);
                    }
                }
            }

            // 通过 edgeSet 收集这个连通域里的所有 CadRawData 图元
            var entities = edgeSet
                .Select(idx => edges[idx].Entity)
                .Where(e => e != null)
                .Cast<CadRawData>()
                .Distinct()
                .ToList();

            if (entities.Count == 0)
                continue;

            // 这个逻辑组件来源的 ComponentId，大概率全一样，取第一个即可
            var sourceId = entities[0].ComponentId ?? string.Empty;

            // 自动生成逻辑组件ID：例如 "0_1", "0_2", "先切_1" ...
            int seq;
            if (sourceId == "0")
                seq = ++componentSeq0;
            else if (sourceId == "先切")
                seq = ++componentSeqFirstCut;
            else
                seq = 1;

            var logicalId = $"{sourceId}_{seq}";

            // 计算这个逻辑组件的物理边界
            var allPoints = entities.SelectMany(ParseGeometricEntity).ToList();
            ComponentExtremes? extremes = null;
            if (allPoints.Count > 0)
                extremes = new ComponentExtremes
                {
                    ComponentId = logicalId,
                    MinX = allPoints.Min(p => p.X),
                    MinY = allPoints.Min(p => p.Y),
                    MaxX = allPoints.Max(p => p.X),
                    MaxY = allPoints.Max(p => p.Y)
                };

            logicalComponents.Add(new LogicalComponent
            {
                Id = logicalId,
                SourceComponentId = sourceId,
                Entities = entities,
                Extremes = extremes
            });
        }

        return logicalComponents;
    }

    // 内部数据结构，用于桥位查找
    private record CadPoint(string ComponentId, string Handle, Point2d Coord);

    private record Bridge(int BridgeId, string ComponentId, Point2d Point1, Point2d Point2);

    #region Private Helper Methods

    /// <summary>
    ///     核心逻辑：从CAD数据中自动识别桥位。
    /// </summary>
    private List<Bridge> FindBridgesFromCad()
    {
        var bridges = new List<Bridge>();
        var bridgeIdCounter = 1;

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
                new CadPoint(entity.ComponentId, entity.ObjectHandle,
                    new Point2d(entity.StartX!.Value, entity.StartY!.Value)),
                new CadPoint(entity.ComponentId, entity.ObjectHandle,
                    new Point2d(entity.EndX!.Value, entity.EndY!.Value))
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
            for (var i = 0; i < validEndpoints.Count; i++)
            for (var j = i + 1; j < validEndpoints.Count; j++)
            {
                // 确保端点来自不同的图元
                if (validEndpoints[i].Handle == validEndpoints[j].Handle) continue;
                var dist = validEndpoints[i].Coord.DistanceTo(validEndpoints[j].Coord);
                // 检查距离是否在配置的有效范围内
                if (dist > config.MinBridgeDistance && dist < config.MaxBridgeDistance)
                    candidatePairs.Add((i, j, dist));
            }

            // 步骤 5: 选择最终的桥位对，按距离排序，优先选择最近的，并确保每个端点只被使用一次
            var usedIndices = new HashSet<int>();
            foreach (var pair in candidatePairs.OrderBy(p => p.distance)) // 按距离升序排序
                if (!usedIndices.Contains(pair.i) && !usedIndices.Contains(pair.j))
                {
                    bridges.Add(new Bridge(bridgeIdCounter++, group.Key, validEndpoints[pair.i].Coord,
                        validEndpoints[pair.j].Coord));
                    // 标记这两个端点已被使用
                    usedIndices.Add(pair.i);
                    usedIndices.Add(pair.j);
                }
        }

        return bridges;
    }

    /// <summary>
    ///     解析单个CAD图元，返回其用于确定边界框的关键坐标点。
    /// </summary>
    private IEnumerable<Point2d> ParseGeometricEntity(CadRawData row)
    {
        // 规范化对象类型字符串
        var objType = (row.ObjectType ?? "").Trim().ToUpper();
        if (objType == "LINE")
        {
            if (row.StartX.HasValue && row.StartY.HasValue)
                yield return new Point2d(row.StartX.Value, row.StartY.Value);
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
        else if (objType == "ARC" && row.CenterX.HasValue && row.CenterY.HasValue && row.Radius.HasValue &&
                 row.StartAngle.HasValue && row.TotalAngle.HasValue)
        {
            // 使用完整的圆弧关键点计算逻辑
            foreach (var p in GetArcKeyPoints(row.CenterX.Value, row.CenterY.Value, row.Radius.Value,
                         row.StartAngle.Value, row.TotalAngle.Value))
                yield return p;
        }
    }

    /// <summary>
    ///     获取圆弧的关键点，用于精确计算其边界框。
    ///     关键点包括起点、终点，以及圆弧可能经过的0、90、180、270度坐标轴上的点。
    /// </summary>
    private IEnumerable<Point2d> GetArcKeyPoints(double cx, double cy, double r, double startAngle, double totalAngle)
    {
        var startRad = startAngle * Math.PI / 180.0;
        var endRad = (startAngle + totalAngle) * Math.PI / 180.0;

        // 返回圆弧的起点
        yield return new Point2d(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
        // 返回圆弧的终点
        yield return new Point2d(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));

        // 检查圆弧是否跨越了坐标轴（0, 90, 180, 270度）
        // 这是找到圆弧真实边界框(min/max)的关键
        for (var angleDeg = 0; angleDeg < 360; angleDeg += 90)
            if (IsAngleInArc(startAngle, totalAngle, angleDeg))
            {
                var rad = angleDeg * Math.PI / 180.0;
                yield return new Point2d(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
            }
    }

    /// <summary>
    ///     判断一个给定的角度是否位于圆弧的扫描范围内。
    /// </summary>
    private bool IsAngleInArc(double start, double total, double angle)
    {
        var end = start + total;
        // total > 0 表示逆时针圆弧
        if (total > 0)
        {
            // 将待测角度调整到与起始角度在同一个周期内
            while (angle < start) angle += 360;
            return angle <= end;
        }
        // total < 0 表示顺时针圆弧

        // 将待测角度调整到与起始角度在同一个周期内
        while (angle > start) angle -= 360;
        return angle >= end;
    }

    #endregion
}