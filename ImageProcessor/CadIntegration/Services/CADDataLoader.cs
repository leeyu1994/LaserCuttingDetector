// Services/CADDataLoader.cs (修复变量初始化版本)

using System.Drawing;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using VisionLibrary.CadIntegration.Models;
using VisionLibrary.CadIntegration.Models.Utils;

namespace VisionLibrary.CadIntegration.Services
{
    /// <summary>
    /// CAD数据加载器 - 修复变量初始化版本
    /// </summary>
    public class CADDataLoader
    {
        /// <summary>
        /// 加载CAD数据CSV文件
        /// </summary>
        public List<Dictionary<string, string>> LoadCADData(string? csvPath = null)
        {
            string filePath = csvPath ?? Path.Combine(CADConfig.DataDir, CADConfig.CAD_DATA_FILE);

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"CAD数据文件不存在: {filePath}");
            }

            Console.WriteLine($"正在加载CAD数据文件: {filePath}");

            List<Dictionary<string, string>> records = new(); // 初始化变量

            // 按优先级尝试中文编码
            var encodings = new[]
            {
                Encoding.GetEncoding("GBK"),
                Encoding.GetEncoding("GB2312"),
                Encoding.GetEncoding("GB18030"),
                Encoding.Default,
                Encoding.UTF8
            };

            bool success = false;
            foreach (var encoding in encodings)
            {
                try
                {
                    if (TryReadCsvWithEncoding(filePath, encoding, out var tempRecords))
                    {
                        records = tempRecords; // 赋值成功读取的数据
                        Console.WriteLine($"使用{encoding.EncodingName}编码成功读取文件");
                        success = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"尝试{encoding.EncodingName}编码失败: {ex.Message}");
                }
            }

            if (!success)
            {
                throw new InvalidOperationException("无法使用任何编码正确解析CSV文件");
            }

            Console.WriteLine($"成功加载数据，共 {records.Count} 行");

            // 输出列名用于确认
            if (records.Count > 0)
            {
                Console.WriteLine("\n=== CSV文件列名 ===");
                var columnNames = records[0].Keys.ToList();
                for (int i = 0; i < columnNames.Count; i++)
                {
                    Console.WriteLine($"  列{i}: [{columnNames[i]}]");
                }

                // 输出第一行数据进行验证
                Console.WriteLine("\n=== 第一行数据示例 ===");
                var firstRecord = records[0];
                foreach (var kvp in firstRecord)
                {
                    if (!string.IsNullOrEmpty(kvp.Value))
                    {
                        Console.WriteLine($"  [{kvp.Key}] = [{kvp.Value}]");
                    }
                }
            }

            return records;
        }

        /// <summary>
        /// 尝试使用指定编码读取CSV文件并验证中文内容
        /// </summary>
        private bool TryReadCsvWithEncoding(string filePath, Encoding encoding, out List<Dictionary<string, string>> records)
        {
            records = new List<Dictionary<string, string>>();

            try
            {
                string content;
                using (var reader = new StreamReader(filePath, encoding))
                {
                    content = reader.ReadToEnd();
                }

                // 检查是否正确读取了中文字符
                if (!IsValidChineseContent(content))
                {
                    Console.WriteLine($"{encoding.EncodingName}: 中文字符识别失败");
                    return false;
                }

                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    Delimiter = ",",
                    BadDataFound = null,
                    MissingFieldFound = null
                };

                using var stringReader = new StringReader(content);
                using var csv = new CsvReader(stringReader, config);

                csv.Read();
                csv.ReadHeader();

                if (csv.HeaderRecord == null)
                    return false;

                var headers = csv.HeaderRecord;

                // 验证列名是否包含期望的中文字段
                bool hasValidHeaders = headers.Any(h => h.Contains("对象类型") || h.Contains("起点") || h.Contains("圆心"));
                if (!hasValidHeaders)
                {
                    Console.WriteLine($"{encoding.EncodingName}: 列名验证失败");
                    return false;
                }

                Console.WriteLine($"{encoding.EncodingName}: 读取的列名:");
                for (int i = 0; i < headers.Length; i++)
                {
                    Console.WriteLine($"  列{i}: [{headers[i]}]");
                }

                while (csv.Read())
                {
                    var record = new Dictionary<string, string>();
                    for (int i = 0; i < headers.Length; i++)
                    {
                        string value = csv.GetField(i) ?? string.Empty;
                        record[headers[i]] = value.Trim();
                    }
                    records.Add(record);
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取编码 {encoding.EncodingName} 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 检查内容是否包含有效的中文字符
        /// </summary>
        private bool IsValidChineseContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return false;

            // 检查是否包含预期的中文关键词
            string[] expectedChinese = { "对象类型", "起点", "终点", "圆心", "半径", "角度" };
            return expectedChinese.Any(word => content.Contains(word));
        }

        /// <summary>
        /// 提取基本元素
        /// </summary>
        public (List<CADElement> elements, List<float> allX, List<float> allY) ExtractBasicElements(
            List<Dictionary<string, string>> records)
        {
            var elements = new List<CADElement>();
            var allX = new List<float>();
            var allY = new List<float>();

            Console.WriteLine("开始提取基本元素...");
            Console.WriteLine($"总记录数: {records.Count}");

            if (records.Count == 0)
            {
                Console.WriteLine("警告: 没有数据记录");
                return (elements, allX, allY);
            }

            // 查找对象类型列名（支持不同的列名变体）
            string objectTypeColumn = FindObjectTypeColumn(records[0].Keys);
            string componentIdColumn = FindColumn(records[0], "部件ID", "组件ID", "component_id", "component");
            string objectHandleColumn = FindColumn(records[0], "对象句柄", "句柄", "handle", "object_handle", "id");
            if (string.IsNullOrEmpty(objectTypeColumn))
            {
                Console.WriteLine("错误: 无法找到对象类型列");
                Console.WriteLine("可用列名:");
                foreach (var key in records[0].Keys)
                {
                    Console.WriteLine($"  [{key}]");
                }
                return (elements, allX, allY);
            }

            Console.WriteLine($"使用对象类型列: [{objectTypeColumn}]");

            int lineCount = 0, arcCount = 0, circleCount = 0, unknownCount = 0;

            for (int idx = 0; idx < records.Count; idx++)
            {
                var record = records[idx];

                // 获取对象类型
                if (!record.ContainsKey(objectTypeColumn) || string.IsNullOrEmpty(record[objectTypeColumn]))
                {
                    unknownCount++;
                    continue;
                }

                string elemType = record[objectTypeColumn].Trim().ToUpper();

                if (idx < 5) // 显示前5行的处理情况
                {
                    Console.WriteLine($"第{idx + 1}行: 对象类型=[{elemType}]");
                }

                string componentId = GetValue(record, componentIdColumn);
                string handle = GetValue(record, objectHandleColumn);

                if (elemType == "LINE")
                {
                    var line = ExtractLineElement(record, idx + 1);
                    if (line != null)
                    {
                        line.ComponentId = componentId;
                        line.Id = handle;
                        elements.Add(line);
                        allX.AddRange(new[] { line.Start.X, line.End.X });
                        allY.AddRange(new[] { line.Start.Y, line.End.Y });
                        lineCount++;
                    }
                }
                else if (elemType == "ARC")
                {
                    var arc = ExtractArcElement(record, idx + 1);
                    if (arc != null)
                    {
                        arc.ComponentId = componentId;
                        arc.Id = handle;
                        elements.Add(arc);
                        allX.Add(arc.Center.X);
                        allY.Add(arc.Center.Y);
                        arcCount++;
                    }
                }
                else if (elemType == "CIRCLE")
                {
                    var circle = ExtractCircleElement(record, idx + 1);
                    if (circle != null)
                    {
                        circle.ComponentId = componentId;
                        circle.Id = handle;
                        elements.Add(circle);
                        allX.Add(circle.Center.X);
                        allY.Add(circle.Center.Y);
                        circleCount++;
                    }
                }
                else
                {
                    if (idx < 10) // 只显示前10行的未知类型
                        Console.WriteLine($"第{idx + 1}行: 未知元素类型 [{elemType}]");
                    unknownCount++;
                }
            }

            Console.WriteLine($"\n=== 元素提取统计 ===");
            Console.WriteLine($"直线: {lineCount}");
            Console.WriteLine($"圆弧: {arcCount}");
            Console.WriteLine($"圆: {circleCount}");
            Console.WriteLine($"未知类型: {unknownCount}");
            Console.WriteLine($"总计: {elements.Count} 个基本元素");

            return (elements, allX, allY);
        }

        /// <summary>
        /// 查找对象类型列名
        /// </summary>
        private string FindObjectTypeColumn(IEnumerable<string> columnNames)
        {
            var columns = columnNames.ToList();

            // 可能的对象类型列名
            string[] possibleNames = {
                "对象类型",
                "类型",
                "对象",
                "元素类型",
                "图元类型",
                "entity_type",
                "object_type",
                "type",
                "entity"
            };

            foreach (var possibleName in possibleNames)
            {
                var found = columns.FirstOrDefault(c =>
                    c.Equals(possibleName, StringComparison.OrdinalIgnoreCase) ||
                    c.Contains(possibleName));
                if (!string.IsNullOrEmpty(found))
                {
                    return found;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 查找列名（支持模糊匹配）
        /// </summary>
        private string FindColumn(Dictionary<string, string> record, params string[] possibleNames)
        {
            foreach (var possibleName in possibleNames)
            {
                var found = record.Keys.FirstOrDefault(k =>
                    k.Equals(possibleName, StringComparison.OrdinalIgnoreCase) ||
                    k.Contains(possibleName));
                if (!string.IsNullOrEmpty(found))
                {
                    return found;
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// 提取直线元素
        /// </summary>
        private LineElement? ExtractLineElement(Dictionary<string, string> record, int rowNumber)
        {
            string startXCol = FindColumn(record, "起点X", "起始X", "startx", "start_x", "x1");
            string startYCol = FindColumn(record, "起点Y", "起始Y", "starty", "start_y", "y1");
            string endXCol = FindColumn(record, "终点X", "端点X", "endx", "end_x", "x2");
            string endYCol = FindColumn(record, "终点Y", "端点Y", "endy", "end_y", "y2");

            if (string.IsNullOrEmpty(startXCol) || string.IsNullOrEmpty(startYCol) ||
                string.IsNullOrEmpty(endXCol) || string.IsNullOrEmpty(endYCol))
            {
                if (rowNumber <= 5)
                    Console.WriteLine($"第{rowNumber}行: 直线元素缺少坐标列");
                return null;
            }

            if (!TryParseFloat(record, startXCol, out float startX) ||
                !TryParseFloat(record, startYCol, out float startY) ||
                !TryParseFloat(record, endXCol, out float endX) ||
                !TryParseFloat(record, endYCol, out float endY))
            {
                if (rowNumber <= 5)
                    Console.WriteLine($"第{rowNumber}行: 直线元素坐标数据解析失败");
                return null;
            }

            var line = new LineElement
            {
                Start = new PointF(startX, startY),
                End = new PointF(endX, endY)
            };

            line.Length = line.CalculateLength();
            if (rowNumber <= 3)
                Console.WriteLine($"第{rowNumber}行: 提取直线: ({startX:F2},{startY:F2}) -> ({endX:F2},{endY:F2})");
            return line;
        }

        /// <summary>
        /// 提取圆弧元素
        /// </summary>
        private ArcElement? ExtractArcElement(Dictionary<string, string> record, int rowNumber)
        {
            string centerXCol = FindColumn(record, "圆心X", "中心X", "centerx", "center_x");
            string centerYCol = FindColumn(record, "圆心Y", "中心Y", "centery", "center_y");
            string radiusCol = FindColumn(record, "半径", "radius", "r");
            string startAngleCol = FindColumn(record, "起始角度(°)", "起始角度", "startangle", "start_angle");
            string totalAngleCol = FindColumn(record, "总角度(°)", "总角度", "sweepangle", "sweep_angle");

            if (string.IsNullOrEmpty(centerXCol) || string.IsNullOrEmpty(centerYCol) ||
                string.IsNullOrEmpty(radiusCol) || string.IsNullOrEmpty(startAngleCol) ||
                string.IsNullOrEmpty(totalAngleCol))
            {
                if (rowNumber <= 5)
                    Console.WriteLine($"第{rowNumber}行: 圆弧元素缺少必要的列");
                return null;
            }

            if (!TryParseFloat(record, centerXCol, out float centerX) ||
                !TryParseFloat(record, centerYCol, out float centerY) ||
                !TryParseFloat(record, radiusCol, out float radius) ||
                !TryParseFloat(record, startAngleCol, out float startAngle) ||
                !TryParseFloat(record, totalAngleCol, out float totalAngle))
            {
                if (rowNumber <= 5)
                    Console.WriteLine($"第{rowNumber}行: 圆弧元素数据解析失败");
                return null;
            }

            var arc = new ArcElement
            {
                Center = new PointF(centerX, centerY),
                Radius = radius,
                StartAngle = startAngle, // 直接使用原始数据
                TotalAngle = totalAngle  // 直接使用原始数据，保留方向
            };

            arc.Length = arc.CalculateLength();
            if (rowNumber <= 3)
                Console.WriteLine($"第{rowNumber}行: 提取圆弧: 中心({centerX:F2},{centerY:F2}), 半径: {radius:F2}, 起始角度: {startAngle:F2}, 总角度: {totalAngle:F2}");
            return arc;
        }

        /// <summary>
        /// 提取圆元素
        /// </summary>
        private CircleElement? ExtractCircleElement(Dictionary<string, string> record, int rowNumber)
        {
            string centerXCol = FindColumn(record, "圆心X", "中心X", "centerx", "center_x");
            string centerYCol = FindColumn(record, "圆心Y", "中心Y", "centery", "center_y");
            string radiusCol = FindColumn(record, "半径", "radius", "r");

            if (string.IsNullOrEmpty(centerXCol) || string.IsNullOrEmpty(centerYCol) ||
                string.IsNullOrEmpty(radiusCol))
            {
                if (rowNumber <= 5)
                    Console.WriteLine($"第{rowNumber}行: 圆元素缺少必要的列");
                return null;
            }

            if (!TryParseFloat(record, centerXCol, out float centerX) ||
                !TryParseFloat(record, centerYCol, out float centerY) ||
                !TryParseFloat(record, radiusCol, out float radius))
            {
                if (rowNumber <= 5)
                    Console.WriteLine($"第{rowNumber}行: 圆元素数据解析失败");
                return null;
            }

            var circle = new CircleElement
            {
                Center = new PointF(centerX, centerY),
                Radius = radius
            };

            circle.Circumference = circle.CalculateLength();
            if (rowNumber <= 3)
                Console.WriteLine($"第{rowNumber}行: 提取圆: 中心({centerX:F2},{centerY:F2}), 半径: {radius:F2}");
            return circle;
        }

        /// <summary>
        /// 尝试解析浮点数
        /// </summary>
        private bool TryParseFloat(Dictionary<string, string> record, string key, out float value)
        {
            value = 0f;
            if (!record.ContainsKey(key) || string.IsNullOrEmpty(record[key]))
            {
                return false;
            }

            string valueStr = record[key].Trim();
            return float.TryParse(valueStr, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// 安全获取指定列的值，缺失时返回空字符串。
        /// </summary>
        private string GetValue(Dictionary<string, string> record, string? columnName)
        {
            if (string.IsNullOrEmpty(columnName)) return string.Empty;
            return record.TryGetValue(columnName, out var value) ? value.Trim() : string.Empty;
        }
    }
}
