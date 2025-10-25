// VisionLibrary/Models/FastPixelData.cs
using OpenCvSharp;

namespace VisionLibrary.Models
{
    /// <summary>
    /// 提供对图像像素数据的快速访问。
    /// 此结构将二值图像的像素数据缓存到一个托管的二维字节数组中，
    /// 将目标像素（白色）表示为1，背景（黑色）表示为0，从而极大地提升后续像素有效性判断的速度。
    /// </summary>
    public class FastPixelData
    {
        private readonly byte[,] _pixelArray;
        private readonly int _width;
        private readonly int _height;

        /// <summary>
        /// 图像的宽度（像素）。
        /// </summary>
        public int Width => _width;

        /// <summary>
        /// 图像的高度（像素）。
        /// </summary>
        public int Height => _height;

        /// <summary>
        /// 构造函数，从一个二值化的OpenCvSharp Mat对象创建实例。
        /// </summary>
        /// <param name="binaryImage">二值化图像。图像中的目标路径应为非零值（如255），背景为0。</param>
        public FastPixelData(Mat binaryImage)
        {
            _width = binaryImage.Width;
            _height = binaryImage.Height;
            _pixelArray = new byte[_height, _width];

            // 使用索引器（Indexer）来提升访问Mat数据的性能，这比循环调用At<byte>()更快
            var indexer = binaryImage.GetGenericIndexer<byte>();

            // 将Mat数据复制到托管的二维数组中
            // 在此过程中，我们将非零值（代表目标像素）统一转换为1，零值（背景）保持为0。
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    _pixelArray[y, x] = indexer[y, x] > 0 ? (byte)1 : (byte)0;
                }
            }
        }

        /// <summary>
        /// 检查指定坐标的像素是否为有效的目标点（值为1）。
        /// 此方法经过了边界检查，可以安全调用。
        /// </summary>
        /// <param name="x">要检查的像素的X坐标。</param>
        /// <param name="y">要检查的像素的Y坐标。</param>
        /// <returns>如果坐标在图像范围内且像素值为1，则返回true；否则返回false。</returns>
        public bool IsValidPixel(int x, int y)
        {
            // 边界检查与值检查合并，利用了C#的短路求值特性
            return x >= 0 && x < _width && y >= 0 && y < _height && _pixelArray[y, x] == 1;
        }
    }
}