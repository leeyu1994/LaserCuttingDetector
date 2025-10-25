using CsvHelper.Configuration.Attributes;
using OpenCvSharp;

namespace VisionLibrary.Models
{
    // =================================================================
    // 输入数据模型 (Input Data Models)
    // =================================================================

    /// <summary>
    /// 表示从原始CAD的CSV文件中直接读取的一行数据。
    /// </summary>
    public class CadRawData
    {
        [Name("部件ID")]
        public string ComponentId { get; set; } = string.Empty;

        [Name("对象类型")]
        public string ObjectType { get; set; } = string.Empty;

        [Name("对象句柄")]
        public string ObjectHandle { get; set; } = string.Empty;

        [Name("起点X")]
        public double? StartX { get; set; }

        [Name("起点Y")]
        public double? StartY { get; set; }

        [Name("终点X")]
        public double? EndX { get; set; }

        [Name("终点Y")]
        public double? EndY { get; set; }

        [Name("圆心X")]
        public double? CenterX { get; set; }

        [Name("圆心Y")]
        public double? CenterY { get; set; }

        [Name("半径")]
        public double? Radius { get; set; }

        [Name("起始角度(°)")]
        public double? StartAngle { get; set; }

        [Name("总角度(°)")]
        public double? TotalAngle { get; set; }
    }

    /// <summary>
    /// 表示从CAD数据中解析出的单个部件的理论物理边界信息。
    /// 所有坐标单位均为毫米(mm)。
    /// </summary>
    public class ComponentExtremes
    {
        public string ComponentId { get; set; } = string.Empty;
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
    }

    /// <summary>
    /// 表示经过处理后，可直接用于检测的部件信息。
    /// 所有坐标单位已从毫米(mm)转换为像素(px)。
    /// </summary>
    public class ComponentInfo
    {
        public string Id { get; set; } = string.Empty;
        /// <summary>
        /// 部件的理论中心点坐标 (像素单位)。
        /// </summary>
        public Point2f Center { get; set; }
        /// <summary>
        /// 部件的理论包围盒 (像素单位)。
        /// </summary>
        public Rect BoundingBox { get; set; }
    }


    // =================================================================
    // 结果数据模型 (Result Models)
    // =================================================================

    /// <summary>
    /// 包含单个部件的完整检测结果。
    /// </summary>
    public class ComponentResult
    {
        [Name("id")]
        public string CmpID { get; set; } = string.Empty;
        [Name("x_min")]
        public int XMin { get; set; }
        [Name("x_max")]
        public int XMax { get; set; }
        [Name("y_min")]
        public int YMin { get; set; }
        [Name("y_max")]
        public int YMax { get; set; }
        [Name("center_x")]
        public int CenterX { get; set; }
        [Name("center_y")]
        public int CenterY { get; set; }
        [Name("width")]
        public int Width { get; set; }
        [Name("height")]
        public int Height { get; set; }
        [Name("offset")]
        public double Offset { get; set; }
        [Name("area")]
        public double Area { get; set; }
        [Name("result")]
        public string Result { get; set; } = "F"; // 默认为不合格 (Fail)

        /// <summary>
        /// 检测到的实际轮廓点集，不写入CSV。
        /// </summary>
        [Ignore]
        public Point[] Contour { get; set; } = [];
    }

    /// <summary>
    /// 包含单个桥位的完整检测结果。
    /// </summary>
    public class BridgeResult
    {
        [Name("BrID")]
        public int BrID { get; set; }
        [Name("bridge_width1")]
        public double BridgeWidth1 { get; set; }
        [Name("bridge_width2")]
        public double BridgeWidth2 { get; set; }
        [Name("bridge_length")]
        public double BridgeLength { get; set; }
        [Name("bridge_offset")]
        public double BridgeOffset { get; set; }
        [Name("bridge_angle")]
        public double BridgeAngle { get; set; }
        [Name("is_symmertric")]
        public string IsSymmetric { get; set; } = "F";
        [Name("offset")]
        public double PlumbOffset { get; set; }
        [Name("bridge_center_x")]
        public double BridgeCenterX { get; set; }
        [Name("bridge_center_y")]
        public double BridgeCenterY { get; set; }
        [Name("result")]
        public string Result { get; set; } = "F";
    }

    /// <summary>
    /// 表示宽度检测中单个采样点的详细结果。
    /// </summary>
    public class WidthSampleResult
    {
        public int CurveId { get; set; }
        public string CurveType { get; set; } = string.Empty;
        public double DesignX { get; set; } // 理论坐标 (mm)
        public double DesignY { get; set; } // 理论坐标 (mm)
        public int FinalMappedX { get; set; } // 最终匹配到的像素X
        public int FinalMappedY { get; set; } // 最终匹配到的像素Y
        public bool IsValid { get; set; } // 采样点是否有效
        public double? WidthMm { get; set; } // 检测到的宽度 (mm)
        public string WidthQualified { get; set; } = "F"; // 宽度是否合格 (T/F)
        public string OffsetQualified { get; set; } = "F"; // 偏移是否合格 (T/F)
        public double? MidX { get; set; } // 实际中心点像素X
        public double? MidY { get; set; } // 实际中心点像素Y
        public double? OffsetDistanceMm { get; set; } // 偏移距离 (mm)
    }

    /// <summary>
    /// 表示聚合后的连续宽度/偏移缺陷段，用于UI展示。
    /// </summary>
    public class AggregatedWidthDefect
    {
        public int CurveId { get; set; }
        public string CurveType { get; set; } = string.Empty;
        public int StartIndex { get; set; } // 缺陷在原始采样点列表中的起始索引
        public int EndIndex { get; set; }   // 缺陷在原始采样点列表中的结束索引
        public int PointCount { get; set; } // 这段缺陷包含的采样点数量
        public double AverageWidthMm { get; set; }
        public double MaxOffsetMm { get; set; }

        /// <summary>
        /// 用于在UI上绘制缺陷轮廓的点集 (像素坐标)。
        /// </summary>
        public Point[] DefectShapePoints { get; set; } = [];

        /// <summary>
        /// 缺陷段的中心点，用于UI定位 (像素坐标)。
        /// </summary>
        public Point2f CenterPoint { get; set; }

        /// <summary>
        /// 缺陷线段的物理长度（单位：毫米）。
        /// </summary>
        public double LengthMm { get; set; }
    }

    /// <summary>
    /// 包含单次图像检测所有结果的顶层容器。
    /// </summary>
    public class InspectionResult
    {
        public List<ComponentResult> DetectedComponents { get; set; } = new();
        public List<BridgeResult> DetectedBridges { get; set; } = new();
        public List<WidthSampleResult> WidthSampleResults { get; set; } = new();
        public List<AggregatedWidthDefect> AggregatedWidthDefects { get; set; } = new();

        /// <summary>
        /// 对齐后的图像，可用于UI显示或进一步分析。
        /// 注意：调用者在用完后有责任调用 .Dispose() 方法释放此Mat对象！
        /// </summary>
        public Mat? AlignedImage { get; set; }

        /// <summary>
        /// 总处理时间（秒）。
        /// </summary>
        public double ProcessTimeSeconds { get; set; }
    }

    /// <summary>
    /// 包含单个压痕（线段或圆弧）的完整检测结果。
    /// 此模型结合了原始CAD数据和检测到的图像特征。
    /// </summary>
    public class IndentationResult
    {
        // --- 原始CAD数据字段 ---
        [Name("部件ID")]
        public string ComponentId { get; set; } = string.Empty;

        [Name("对象类型")]
        public string ObjectType { get; set; } = string.Empty;

        [Name("对象句柄")]
        public string ObjectHandle { get; set; } = string.Empty;

        [Name("起点X")]
        public double? StartX { get; set; }

        [Name("起点Y")]
        public double? StartY { get; set; }

        [Name("终点X")]
        public double? EndX { get; set; }

        [Name("终点Y")]
        public double? EndY { get; set; }

        [Name("圆心X")]
        public double? CenterX_Cad { get; set; }

        [Name("圆心Y")]
        public double? CenterY_Cad { get; set; }

        [Name("半径")]
        public double? Radius { get; set; }

        [Name("起始角度(°)")]
        public double? StartAngle { get; set; }

        [Name("总角度(°)")]
        public double? TotalAngle { get; set; }

        // --- 新增的检测结果字段 ---
        [Name("中心点X")]
        public double? CenterX_Result { get; set; }

        [Name("中心点Y")]
        public double? CenterY_Result { get; set; }

        [Name("平均灰度值")]
        public double AverageGray { get; set; }

        /// <summary>
        /// 辅助方法，用于从原始CadRawData对象填充字段。
        /// </summary>
        public static IndentationResult FromCadData(CadRawData raw)
        {
            return new IndentationResult
            {
                ComponentId = raw.ComponentId,
                ObjectType = raw.ObjectType,
                ObjectHandle = raw.ObjectHandle,
                StartX = raw.StartX,
                StartY = raw.StartY,
                EndX = raw.EndX,
                EndY = raw.EndY,
                CenterX_Cad = raw.CenterX,
                CenterY_Cad = raw.CenterY,
                Radius = raw.Radius,
                StartAngle = raw.StartAngle,
                TotalAngle = raw.TotalAngle
            };
        }
    }

    /// <summary>
    /// 表示不透检测中，单个采样点的详细结果。
    /// </summary>
    public class LightTransmissionResult
    {
        [Name("曲线ID")]
        public int CurveId { get; set; }

        [Name("曲线类型")]
        public string CurveType { get; set; } = string.Empty;

        [Name("理论X(mm)")]
        public double DesignX { get; set; }

        [Name("理论Y(mm)")]
        public double DesignY { get; set; }

        [Name("映射像素X")]
        public int MappedX { get; set; }

        [Name("映射像素Y")]
        public int MappedY { get; set; }

        [Name("是否透光")]
        public string IsTransmitted { get; set; } = "F"; // 默认为不透光 (Fail)

        [Name("平均灰度值")]
        public double AverageGrayValue { get; set; }

        [Name("最小灰度值")]
        public int MinGrayValue { get; set; }
    }
}
