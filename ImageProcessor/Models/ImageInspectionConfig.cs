// VisionLibrary/Models/ImageInspectionConfig.cs
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VisionLibrary.Models
{
    /// <summary>
    /// 存储所有图像检测所需的可配置参数。
    /// 这个类可以被序列化为JSON文件，方便用户进行配置。
    /// </summary>
    public class ImageInspectionConfig
    {
        // =================================================================
        // 全局与图像对齐设置 (Global & Image Alignment Settings)
        // =================================================================

        /// <summary>
        /// 像素大小，即一个像素代表的物理尺寸。单位：毫米/像素 (mm/px)。
        /// 这是所有物理单位(mm)和像素单位(px)转换的基础。
        /// </summary>
        public double PixelSize { get; set; } = 0.01;

        /// <summary>
        /// 电路板目标的理论物理宽度。单位：毫米 (mm)。
        /// </summary>
        public double TargetBoardWidthMm { get; set; } = 200.0;

        /// <summary>
        /// 电路板目标的理论物理高度。单位：毫米 (mm)。
        /// </summary>
        public double TargetBoardHeightMm { get; set; } = 150.0;

        /// <summary>
        /// CAD模板图相比实际电路板多出的切割边框宽度。单位：毫米 (mm)。
        /// </summary>
        public double CuttingFrame { get; set; } = 5.0;

        /// <summary>
        /// 全局二值化阈值。灰度值低于此值的像素被视为目标（黑色），高于此值的为背景（白色）。
        /// 注意：代码中使用了 BinaryInv，所以实际效果是高于阈值的为目标。
        /// </summary>
        public int GlobalBinaryThreshold { get; set; } = 128;


        // =================================================================
        // 部件检测设置 (Component Detection Settings)
        // =================================================================

        /// <summary>
        /// 部件中心点的最大允许偏移量，超出此范围则判定为不合格。单位：毫米 (mm)。
        /// </summary>
        public double ComponentOffset { get; set; } = 0.5;

        /// <summary>
        /// 在理论位置附近搜索实际部件的最大半径。单位：毫米 (mm)。
        /// </summary>
        public double ComponentSearchRadius { get; set; } = 2.0;

        /// <summary>
        /// 识别为主要部件所需的最小轮廓面积。单位：像素² (px²)。
        /// </summary>
        public double MinMainComponentArea { get; set; } = 5000;

        /// <summary>
        /// 识别为子部件所需的最小轮廓面积。单位：像素² (px²)。
        /// </summary>
        public double MinChildComponentArea { get; set; } = 1000;

        /// <summary>
        /// 需要在其内部进一步检测子部件的父部件ID列表。
        /// </summary>
        public List<string> ParentComponentIds { get; set; } = new();

        /// <summary>
        ///  部件检测时，用于消除噪点的形态学开运算核的边长。单位：像素 (px)。
        /// 通常为奇数，如3, 5。
        /// </summary>
        public int ComponentMorphOpenKernelSize { get; set; } = 3;

        /// <summary>
        ///  部件检测时，用于连接邻近区域的形态学闭运算核的边长。单位：像素 (px)。
        /// 通常为奇数，如7, 9。
        /// </summary>
        public int ComponentMorphCloseKernelSize { get; set; } = 7;

        /// <summary>
        ///  子部件检测时，用于消除噪点的形态学开运算核的边长。单位：像素 (px)。
        /// </summary>
        public int ChildComponentMorphOpenKernelSize { get; set; } = 2;


        // =================================================================
        // 线宽与偏移检测设置 (Width & Offset Detection Settings)
        // =================================================================

        /// <summary>
        /// 沿CAD路径进行宽度采样的步长。单位：毫米 (mm)。值越小，采样越密集，计算量越大。
        /// </summary>
        public double WidthSampleStep { get; set; } = 0.1;

        /// <summary>
        /// 当理论采样点不在实际线路上时，向外搜索有效像素点的最大半径。单位：像素 (px)。
        /// </summary>
        public int NearbySearchRadius { get; set; } = 10;

        /// <summary>
        /// 从采样点沿法线方向搜索线路边界的最大步数。单位：像素 (px)。
        /// 用于防止在开阔区域无限搜索。
        /// </summary>
        public int MaxSearchSteps { get; set; } = 100;

        /// <summary>
        /// 线路宽度的最小合格值。单位：毫米 (mm)。
        /// </summary>
        public double MinQualifiedWidth { get; set; } = 0.08;

        /// <summary>
        /// 线路宽度的最大合格值。单位：毫米 (mm)。
        /// </summary>
        public double MaxQualifiedWidth { get; set; } = 0.12;

        /// <summary>
        /// 线路中心点偏移量的最大允许值。单位：毫米 (mm)。
        /// </summary>
        public double WidthOffsetThreshold { get; set; } = 0.05;


        // =================================================================
        // 桥位检测设置 (Bridge Detection Settings)
        // =================================================================

        /// <summary>
        /// 从CAD数据中自动识别桥位时，两个端点间的最小物理距离。单位：毫米 (mm)。
        /// </summary>
        public double MinBridgeDistance { get; set; } = 0.5;

        /// <summary>
        /// 从CAD数据中自动识别桥位时，两个端点间的最大物理距离。单位：毫米 (mm)。
        /// </summary>
        public double MaxBridgeDistance { get; set; } = 2.0;

        /// <summary>
        /// 在图像上以理论中心点为圆心，搜索桥位轮廓的半径。单位：像素 (px)。
        /// </summary>
        public int BridgeSearchRadius { get; set; } = 50;

        /// <summary>
        /// 桥位ROI区域内进行形态学开运算的核边长，用于消除噪点。单位：像素 (px)。
        /// </summary>
        public int BridgeMorphKernelSize { get; set; } = 3;

        /// <summary>
        /// 构成桥位的单边线路所需的最小轮廓面积。单位：像素² (px²)。
        /// </summary>
        public double MinBridgePathArea { get; set; } = 100;

        /// <summary>
        /// 判断桥位对称性时，两条线路中心连线的最大允许投影偏差。单位：像素 (px)。
        /// </summary>
        public double BridgePlumbOffset { get; set; } = 10.0;

        /// <summary>
        ///  桥位长度的最小合格值。单位：毫米 (mm)。
        /// </summary>
        public double MinBridgeLengthMm { get; set; } = 0.8;

        /// <summary>
        ///  桥位长度的最大合格值。单位：毫米 (mm)。
        /// </summary>
        public double MaxBridgeLengthMm { get; set; } = 1.2;

        /// <summary>
        ///  桥位两侧线路宽度的最小合格值。单位：毫米 (mm)。
        /// </summary>
        public double MinBridgePathWidthMm { get; set; } = 0.08;

        /// <summary>
        ///  桥位两侧线路宽度的最大合格值。单位：毫米 (mm)。
        /// </summary>
        public double MaxBridgePathWidthMm { get; set; } = 0.12;

        /// <summary>
        /// 连续宽度缺陷被统计所需满足的最小长度。单位：毫米 (mm)。
        /// 长度小于此值的缺陷段将被忽略。
        /// </summary>
        public double MinDefectLengthMm { get; set; } = 0.5;

        // =================================================================
        // 压痕检测设置 (Indentation Detection Settings)
        // =================================================================

        /// <summary>
        /// 用于压痕检测的CAD数据所在的图层名称。
        /// </summary>
        public string IndentationLayerName { get; set; } = "压痕";

        /// <summary>
        /// 在图像上绘制压痕路径以提取灰度值时使用的线条宽度。单位：像素 (px)。
        /// </summary>
        public int IndentationSampleLineWidth { get; set; } = 5;

        /// <summary>
        ///  压痕区域平均灰度值的最小合格值。范围 0-255。
        /// </summary>
        public int MinIndentationGrayValue { get; set; } = 50;

        /// <summary>
        ///  压痕区域平均灰度值的最大合格值。范围 0-255。
        /// </summary>
        public int MaxIndentationGrayValue { get; set; } = 150;


        // =================================================================
        // 不透光检测设置 (Light Transmission Detection Settings)
        // =================================================================

        /// <summary>
        /// 沿CAD路径进行不透光检测的采样步长。单位：毫米 (mm)。
        /// </summary>
        public double TransmissionSampleStep { get; set; } = 0.1;

        /// <summary>
        /// 在每个采样点，沿法线方向检查像素灰度值的总宽度。单位：像素 (px)。
        /// </summary>
        public int TransmissionCheckWidthPixels { get; set; } = 10;

        /// <summary>
        /// 判断线路为“透光”所需的最低灰度值。横截面内所有像素的灰度值都必须大于等于此值。范围 0-255。
        /// </summary>
        public int MinTransmissionGrayValue { get; set; } = 200;
    }
}
