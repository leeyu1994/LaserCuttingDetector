// VisionLibrary/Models/ImageInspectionConfig.cs
using System.Collections.Generic;

namespace VisionLibrary.Models
{
    /// <summary>
    /// 存储所有图像检测项目的全局配置参数。
    /// </summary>
    public class ImageInspectionConfig
    {
        #region 通用与坐标系配置 (General & Coordinate System)

        /// <summary>
        /// 像素尺寸，即一个像素代表的物理长度 (单位: mm/pixel)。
        /// </summary>
        public double PixelSize { get; set; } = 0.042;

        /// <summary>
        /// 图像对齐后，电路板区域距离CAD画布边缘的宽度（单位：mm）。
        /// 这个值用于修正CAD坐标系与图像坐标系的偏差。
        /// </summary>
        public double CuttingFrame { get; set; } = 10.0;

        /// <summary>
        /// 电路板的物理宽度 (单位: mm)。
        /// </summary>
        public double TargetBoardWidthMm { get; set; } = 215.0;

        /// <summary>
        /// 电路板的物理高度 (单位: mm)。
        /// </summary>
        public double TargetBoardHeightMm { get; set; } = 290.0;

        /// <summary>
        /// 全局二值化阈值 (0-255)。
        /// 图像中灰度值低于此阈值的像素点将被视为目标（白色），高于则为背景（黑色）。
        /// </summary>
        public int GlobalBinaryThreshold { get; set; } = 85;

        #endregion

        #region 部件检测配置 (Component Detection)

        /// <summary>
        /// 组件实际位置与理论位置的最大允许偏移量 (单位: mm)。
        /// </summary>
        public double ComponentOffset { get; set; } = 2.0;

        /// <summary>
        /// 在理论位置周围搜索匹配轮廓的半径 (单位: mm)。
        /// </summary>
        public double ComponentSearchRadius { get; set; } = 5.0;

        /// <summary>
        /// 预处理时高斯模糊的核大小（必须为奇数）。
        /// </summary>
        public int BlurKernelSize { get; set; } = 5;

        /// <summary>
        /// 检测主要部件时，轮廓的最小面积阈值 (单位: 像素^2)。
        /// </summary>
        public double MinMainComponentArea { get; set; } = 20000;

        /// <summary>
        /// 检测子部件时，轮廓的最小面积阈值 (单位: 像素^2)。
        /// </summary>
        public double MinChildComponentArea { get; set; } = 2000;

        /// <summary>
        /// 需要在其内部检测子组件的父组件ID列表。
        /// </summary>
        public List<string> ParentComponentIds { get; set; } = new() { "A13", "A14" };

        #endregion

        #region 桥位检测配置 (Bridge Detection)

        /// <summary>
        /// 桥位检测时，在理论中心点周围提取的ROI区域的半径 (单位: 像素)。
        /// </summary>
        public int BridgeSearchRadius { get; set; } = 30;

        /// <summary>
        /// 桥位检测中，构成桥臂的路径轮廓的最小面积阈值 (单位: 像素^2)。
        /// </summary>
        public double MinBridgePathArea { get; set; } = 20;

        /// <summary>
        /// 桥位检测预处理时的形态学开运算核大小。
        /// </summary>
        public int BridgeMorphKernelSize { get; set; } = 3;

        /// <summary>
        /// 桥位两端是否对齐的垂直度偏移阈值 (单位: 像素)。
        /// </summary>
        public double BridgePlumbOffset { get; set; } = 1.0;

        /// <summary>
        /// 从CAD数据中自动寻找桥位时，两端点间的物理距离下限 (单位: mm)。
        /// </summary>
        public double MinBridgeDistance { get; set; } = 0.29;

        /// <summary>
        /// 从CAD数据中自动寻找桥位时，两端点间的物理距离上限 (单位: mm)。
        /// </summary>
        public double MaxBridgeDistance { get; set; } = 0.36;

        #endregion

        #region 宽度检测配置 (Width Detection)

        /// <summary>
        /// 在CAD图元上采样生成测量点的固定步长 (单位: mm)。
        /// </summary>
        public double WidthSampleStep { get; set; } = 0.42;

        /// <summary>
        /// 线路宽度合格的下限 (单位: mm)。
        /// </summary>
        public double MinQualifiedWidth { get; set; } = 0.14;

        /// <summary>
        /// 线路宽度合格的上限 (单位: mm)。
        /// </summary>
        public double MaxQualifiedWidth { get; set; } = 0.16;

        /// <summary>
        /// 线路中心点偏移合格的阈值 (单位: mm)。
        /// </summary>
        public double WidthOffsetThreshold { get; set; } = 0.52;

        /// <summary>
        /// 从采样点沿法线方向搜索边界的最大步数 (单位: 像素)。
        /// </summary>
        public int MaxSearchSteps { get; set; } = 15;

        /// <summary>
        /// 如果理论采样点不在有效像素上，在其周围搜索有效点的最大半径 (单位: 像素)。
        /// </summary>
        public int NearbySearchRadius { get; set; } = 8;

        #endregion

        #region 压痕检测配置 (Indentation Detection)

        /// <summary>
        /// 在计算压痕的平均灰度值时，用于采样绘制的线宽（单位：像素）。
        /// </summary>
        public int IndentationSampleLineWidth { get; set; } = 2;

        #endregion

        #region 不透检测配置 (Light Transmission Detection)

        /// <summary>
        /// 在CAD图元上为不透检测采样生成测量点的固定步长 (单位: mm)。
        /// 可以与宽度检测的步长(WidthSampleStep)设为相同值。
        /// </summary>
        public double TransmissionSampleStep { get; set; } = 0.42;

        /// <summary>
        /// 判断像素点为"有效透光"的最低灰度值 (0-255)。
        /// 在背光图像中，灰度值高于此阈值的点被认为是透光的。
        /// </summary>
        public int MinTransmissionGrayValue { get; set; } = 200;

        /// <summary>
        /// 在每个采样点，沿法线方向检查的宽度（单位：像素）。
        /// 例如，值为5表示以采样点为中心，向两侧各延伸2.5个像素进行检查。
        /// 这个值应该约等于或略大于实际切割缝隙的像素宽度。
        /// </summary>
        public int TransmissionCheckWidthPixels { get; set; } = 5;

        #endregion
    }
}
