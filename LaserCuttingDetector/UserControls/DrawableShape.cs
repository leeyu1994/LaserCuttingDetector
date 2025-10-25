using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace LaserCuttingDetector.UserControls
{
    /// <summary>
    /// 一个通用的、可在PictureViewer上绘制的形状模型
    /// </summary>
    public class DrawableShape
    {
        /// <summary>
        /// 构成形状的顶点集合（图像像素坐标）
        /// </summary>
        public PointCollection Points { get; set; }

        /// <summary>
        /// 形状是否被选中（用于高亮显示）
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public DrawableShape()
        {
            Points = new PointCollection();
        }
    }
}