using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CadDrawPic.Config
{
    // <summary>
    /// CAD处理配置类
    /// </summary>
    public static class CADConfig
    {
        // ====================== 项目路径配置 ======================
        public static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string DataDir = Path.Combine(BaseDir, "data");
        public static readonly string OutputDir = Path.Combine(BaseDir, "output");

        // ====================== 文件名配置 ======================
        public const string CAD_DATA_FILE = "PROJECT_QJ_1AMK63_001_v17_C.csv";
        public const string ACCURACY_REPORT = "accuracy_report.txt";
        public const string OUTPUT_IMAGE_NO_MARGIN = "cad_redraw_0.png";
        public const string OUTPUT_IMAGE_WITH_MARGIN = "cad_redraw_10.png";

        // ====================== 绘图参数配置 ======================
        public const float PIXEL_SIZE = 0.042f;           // 每个像素的物理尺寸（毫米）
        public const float LINE_WIDTH_MM = 0.1f;          // 线宽（毫米）
        public const float MARGIN_MM = 10f;               // 边距大小（毫米）
        public static readonly float DPI = 25.4f / PIXEL_SIZE; // 图像分辨率

        // ====================== 固定绘图区域 ======================
        public const float PLOT_X_MIN = 0f;
        public const float PLOT_Y_MIN = 0f;
        public const float PLOT_X_MAX = 215f;
        public const float PLOT_Y_MAX = 290f;
        public static readonly float PLOT_WIDTH = PLOT_X_MAX - PLOT_X_MIN;
        public static readonly float PLOT_HEIGHT = PLOT_Y_MAX - PLOT_Y_MIN;

        // ====================== 验证参数配置 ======================
        public const float POSITION_TOLERANCE_MM = 0.5f;      // 位置误差容差（毫米）
        public const float LENGTH_TOLERANCE_RATIO = 0.01f;    // 长度误差容差（1%）
        public const float ANGLE_TOLERANCE_DEG = 0.5f;        // 角度误差容差（度）

        // ====================== 列名映射配置 ======================
        public static readonly Dictionary<string, string> COLUMN_MAPPING = new()
        {
            ["名称"] = "对象类型",
            ["起点X"] = "起点X",
            ["起点Y"] = "起点Y",
            ["端点X"] = "终点X",
            ["端点Y"] = "终点Y",
            ["中心X"] = "圆心X",
            ["中心Y"] = "圆心Y",
            ["半径"] = "半径",
            ["起点角度"] = "起始角度(°)",
            ["总角度"] = "总角度(°)"
        };
    }
}
