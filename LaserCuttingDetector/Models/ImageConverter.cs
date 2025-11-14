using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace LaserCuttingDetector.Models
{
    public static class ImageConverter
    {
        /// <summary>
        /// 将ImageDataInfo转换为Bitmap（带调试信息）
        /// </summary>
        /// <param name="imageData">图像数据信息</param>
        /// <returns>转换后的Bitmap对象</returns>
        public static Bitmap ConvertToBitmap(ImageDataInfo imageData)
        {
            if (imageData.bits == IntPtr.Zero || imageData.width <= 0 || imageData.height <= 0)
            {
                System.Diagnostics.Debug.WriteLine("图像数据无效");
                return null;
            }

            try
            {
                // 打印调试信息
                System.Diagnostics.Debug.WriteLine($"图像信息: Width={imageData.width}, Height={imageData.height}");
                System.Diagnostics.Debug.WriteLine($"Channel={imageData.channel}, Depth={imageData.depth}");
                System.Diagnostics.Debug.WriteLine($"BytesPerLine={imageData.bytesPerLine}");
                System.Diagnostics.Debug.WriteLine($"Bits指针: {imageData.bits}");

                // 先尝试最简单的8位灰度格式
                int bytesPerPixel = 1;
                PixelFormat pixelFormat = PixelFormat.Format8bppIndexed;

                // 计算实际的字节步长
                int actualBytesPerLine = imageData.bytesPerLine > 0
                    ? imageData.bytesPerLine
                    : imageData.width * bytesPerPixel;

                System.Diagnostics.Debug.WriteLine($"计算的BytesPerLine: {actualBytesPerLine}");

                // 计算总数据大小
                int totalSize = actualBytesPerLine * imageData.height;
                System.Diagnostics.Debug.WriteLine($"总数据大小: {totalSize} bytes");

                // 复制数据并检查内容
                byte[] imageBytes = new byte[totalSize];
                Marshal.Copy(imageData.bits, imageBytes, 0, totalSize);

                // 检查数据内容
                CheckImageData(imageBytes, imageData.width, imageData.height, actualBytesPerLine);

                // 创建位图
                Bitmap bitmap = new Bitmap(imageData.width, imageData.height, pixelFormat);

                // 设置灰度调色板
                SetGrayscalePalette(bitmap);

                // 锁定位图数据
                BitmapData bitmapData = bitmap.LockBits(
                    new Rectangle(0, 0, imageData.width, imageData.height),
                    ImageLockMode.WriteOnly,
                    pixelFormat);

                try
                {
                    IntPtr ptr = bitmapData.Scan0;
                    int bitmapStride = bitmapData.Stride;

                    System.Diagnostics.Debug.WriteLine($"Bitmap Stride: {bitmapStride}");

                    // 逐行复制数据
                    for (int y = 0; y < imageData.height; y++)
                    {
                        int sourceOffset = y * actualBytesPerLine;
                        IntPtr destPtr = new IntPtr(ptr.ToInt64() + y * bitmapStride);

                        // 复制一行数据
                        int bytesToCopy = Math.Min(imageData.width * bytesPerPixel, bitmapStride);
                        Marshal.Copy(imageBytes, sourceOffset, destPtr, bytesToCopy);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(bitmapData);
                }

                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"图像转换异常: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// 检查图像数据内容
        /// </summary>
        private static void CheckImageData(byte[] data, int width, int height, int bytesPerLine)
        {
            if (data == null || data.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("数据为空");
                return;
            }

            // 检查前几个字节
            System.Diagnostics.Debug.Write("前16个字节: ");
            for (int i = 0; i < Math.Min(16, data.Length); i++)
            {
                System.Diagnostics.Debug.Write($"{data[i]:X2} ");
            }
            System.Diagnostics.Debug.WriteLine("");

            // 统计像素值分布
            int[] histogram = new int[256];
            int sampleSize = Math.Min(data.Length, width * height);

            for (int i = 0; i < sampleSize; i++)
            {
                histogram[data[i]]++;
            }

            // 找出非零像素值
            int nonZeroCount = 0;
            int minValue = 255, maxValue = 0;
            for (int i = 0; i < 256; i++)
            {
                if (histogram[i] > 0)
                {
                    nonZeroCount++;
                    if (i < minValue) minValue = i;
                    if (i > maxValue) maxValue = i;
                }
            }

            System.Diagnostics.Debug.WriteLine($"像素值统计: 非零值数量={nonZeroCount}, 最小值={minValue}, 最大值={maxValue}");
            System.Diagnostics.Debug.WriteLine($"零值像素数量: {histogram[0]}, 占比: {(double)histogram[0] / sampleSize * 100:F2}%");

            // 如果全是零，可能是数据格式问题
            if (histogram[0] == sampleSize)
            {
                System.Diagnostics.Debug.WriteLine("警告: 所有像素都是0，可能是数据格式错误");
            }
        }

        /// <summary>
        /// 尝试不同的格式转换
        /// </summary>
        public static Bitmap ConvertToBitmapWithDifferentFormats(ImageDataInfo imageData)
        {
            if (imageData.bits == IntPtr.Zero || imageData.width <= 0 || imageData.height <= 0)
            {
                return null;
            }

            // 尝试不同的格式
            var formats = new[]
            {
                new { Format = PixelFormat.Format8bppIndexed, BytesPerPixel = 1, Name = "8位灰度" },
                new { Format = PixelFormat.Format24bppRgb, BytesPerPixel = 3, Name = "24位RGB" },
                new { Format = PixelFormat.Format32bppArgb, BytesPerPixel = 4, Name = "32位ARGB" }
            };

            foreach (var format in formats)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"尝试格式: {format.Name}");

                    int bytesPerLine = imageData.bytesPerLine > 0
                        ? imageData.bytesPerLine
                        : imageData.width * format.BytesPerPixel;

                    int totalSize = bytesPerLine * imageData.height;
                    byte[] imageBytes = new byte[totalSize];
                    Marshal.Copy(imageData.bits, imageBytes, 0, totalSize);

                    // 检查这种格式下的数据
                    CheckImageData(imageBytes, imageData.width, imageData.height, bytesPerLine);

                    Bitmap bitmap = new Bitmap(imageData.width, imageData.height, format.Format);

                    if (format.Format == PixelFormat.Format8bppIndexed)
                    {
                        SetGrayscalePalette(bitmap);
                    }

                    BitmapData bitmapData = bitmap.LockBits(
                        new Rectangle(0, 0, imageData.width, imageData.height),
                        ImageLockMode.WriteOnly,
                        format.Format);

                    try
                    {
                        for (int y = 0; y < imageData.height; y++)
                        {
                            int sourceOffset = y * bytesPerLine;
                            IntPtr destPtr = new IntPtr(bitmapData.Scan0.ToInt64() + y * bitmapData.Stride);
                            int bytesToCopy = Math.Min(imageData.width * format.BytesPerPixel, bitmapData.Stride);
                            Marshal.Copy(imageBytes, sourceOffset, destPtr, bytesToCopy);
                        }
                    }
                    finally
                    {
                        bitmap.UnlockBits(bitmapData);
                    }

                    // 保存测试图像
                    string testPath = $"test_{format.Name.Replace("位", "bit")}.bmp";
                    bitmap.Save(testPath, System.Drawing.Imaging.ImageFormat.Bmp);
                    System.Diagnostics.Debug.WriteLine($"测试图像已保存: {testPath}");

                    return bitmap; // 返回第一个成功的格式
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"格式 {format.Name} 转换失败: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// 原始数据转储（用于调试）
        /// </summary>
        public static void DumpRawData(ImageDataInfo imageData, string filePath)
        {
            if (imageData.bits == IntPtr.Zero)
                return;

            try
            {
                int totalSize = imageData.bytesPerLine > 0
                    ? imageData.bytesPerLine * imageData.height
                    : imageData.width * imageData.height; // 假设1字节每像素

                byte[] data = new byte[totalSize];
                Marshal.Copy(imageData.bits, data, 0, totalSize);

                File.WriteAllBytes(filePath, data);
                System.Diagnostics.Debug.WriteLine($"原始数据已保存到: {filePath}");
                System.Diagnostics.Debug.WriteLine($"数据大小: {data.Length} bytes");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存原始数据失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 为8位灰度图像设置调色板
        /// </summary>
        private static void SetGrayscalePalette(Bitmap bitmap)
        {
            try
            {
                ColorPalette palette = bitmap.Palette;
                for (int i = 0; i < 256; i++)
                {
                    palette.Entries[i] = Color.FromArgb(i, i, i);
                }
                bitmap.Palette = palette;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置调色板异常: {ex.Message}");
            }
        }

    }
}
