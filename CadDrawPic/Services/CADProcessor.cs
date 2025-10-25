using CadDrawPic.Config;
using CadDrawPic.Utils;

namespace CadDrawPic.Services
{
    /// <summary>
    /// CAD处理器 - 主要业务逻辑
    /// </summary>
    public class CADProcessor
    {
        public void ProcessCADData()
        {
            Console.WriteLine("开始处理CAD数据...");
            Console.WriteLine($"固定绘图区域: X({CADConfig.PLOT_X_MIN}-{CADConfig.PLOT_X_MAX}mm), Y({CADConfig.PLOT_Y_MIN}-{CADConfig.PLOT_Y_MAX}mm)");

            // 确保目录存在
            Directory.CreateDirectory(CADConfig.DataDir);
            Directory.CreateDirectory(CADConfig.OutputDir);

            var dataLoader = new CADDataLoader();

            // 加载数据
            var records = dataLoader.LoadCADData();
            Console.WriteLine($"成功加载数据，共 {records.Count} 行");

            // 打印列名用于调试
            if (records.Count > 0)
            {
                Console.WriteLine($"CSV文件列名: {string.Join(", ", records[0].Keys)}");
            }

            // 检查空数据
            if (records.Count == 0)
            {
                Console.WriteLine("错误：CSV文件为空");
                return;
            }

            // 提取基本元素
            var (elements, allX, allY) = dataLoader.ExtractBasicElements(records);
            Console.WriteLine($"提取到 {elements.Count} 个基本元素 (直线/圆弧/圆)");

            // 检查是否有可绘制元素
            if (elements.Count == 0)
            {
                Console.WriteLine("错误：没有找到可绘制的元素");
                return;
            }

            // 检查元素是否在固定区域内
            var outOfBounds = CADUtils.CheckElementsInBounds(elements);
            if (outOfBounds.Count > 0)
            {
                Console.WriteLine($"警告: {outOfBounds.Count}个点超出固定绘图区域");
                for (int i = 0; i < Math.Min(3, outOfBounds.Count); i++) // 只显示前3个
                {
                    var item = outOfBounds[i];
                    Console.WriteLine($"元素 {item.Index} ({item.Type}) 点({item.Point.X:F2}, {item.Point.Y:F2}) " +
                        $"超出区域({item.Bounds.Left}-{item.Bounds.Right}, {item.Bounds.Top}-{item.Bounds.Bottom})");
                }
            }

            // 创建无边距绘图器并绘制
            var noMarginPlotter = new CADPlotter(marginMm: 0f);
            noMarginPlotter.DrawElements(elements);

            // 创建带边距绘图器并绘制
            var marginPlotter = new CADPlotter(marginMm: CADConfig.MARGIN_MM);
            marginPlotter.DrawElements(elements);

            // 验证还原度（只需验证一次）
            var validator = new CADValidator();
            var (_, accuracy) = validator.Validate(elements, elements);

            // 保存验证报告
            string reportPath = Path.Combine(CADConfig.OutputDir, CADConfig.ACCURACY_REPORT);
            validator.SaveReport(reportPath);
            Console.WriteLine($"还原度验证报告已保存至: {reportPath}");
            Console.WriteLine($"系统还原度: {accuracy:F2}%");

            // 保存无边距图像
            string outputNoMargin = Path.Combine(CADConfig.OutputDir, CADConfig.OUTPUT_IMAGE_NO_MARGIN);
            noMarginPlotter.SavePlot(outputNoMargin);
            Console.WriteLine($"无边距CAD图纸已保存至: {outputNoMargin}");

            // 保存带边距图像
            string outputWithMargin = Path.Combine(CADConfig.OutputDir, CADConfig.OUTPUT_IMAGE_WITH_MARGIN);
            marginPlotter.SavePlot(outputWithMargin);
            Console.WriteLine($"带边距CAD图纸已保存至: {outputWithMargin}");

            // 验证实际图像尺寸（只验证无边距图像）
            ValidateImageSize(outputNoMargin);

            // 显示最终结果
            Console.WriteLine("\n处理完成! 运行结果:");
            Console.WriteLine($"- 无边距CAD图纸路径: {outputNoMargin}");
            Console.WriteLine($"- 带边距CAD图纸路径: {outputWithMargin}");
            Console.WriteLine($"- 验证报告路径: {reportPath}");
            Console.WriteLine($"- 还原度: {accuracy:F2}%");
        }

        /// <summary>
        /// 验证图像尺寸
        /// </summary>
        private void ValidateImageSize(string imagePath)
        {
            if (!File.Exists(imagePath))
            {
                Console.WriteLine("警告: 图像文件不存在，无法验证尺寸");
                return;
            }

            using var codec = SkiaSharp.SKCodec.Create(imagePath);
            if (codec == null)
            {
                Console.WriteLine("警告: 无法读取图像文件");
                return;
            }

            int actualWidth = codec.Info.Width;
            int actualHeight = codec.Info.Height;
            int expectedWidthPx = (int)Math.Round(CADConfig.PLOT_WIDTH / CADConfig.PIXEL_SIZE);
            int expectedHeightPx = (int)Math.Round(CADConfig.PLOT_HEIGHT / CADConfig.PIXEL_SIZE);

            Console.WriteLine($"预期图像尺寸: {expectedWidthPx}x{expectedHeightPx}像素");
            Console.WriteLine($"实际图像尺寸: {actualWidth}x{actualHeight}像素");

            int widthDiff = Math.Abs(actualWidth - expectedWidthPx);
            int heightDiff = Math.Abs(actualHeight - expectedHeightPx);

            if (widthDiff > 1 || heightDiff > 1)
            {
                Console.WriteLine($"尺寸不匹配! 差异: 宽度差={widthDiff}px, 高度差={heightDiff}px");
                Console.WriteLine($"精度损失: {widthDiff / (float)expectedWidthPx * 100:F2}%宽度, " +
                    $"{heightDiff / (float)expectedHeightPx * 100:F2}%高度");
            }
            else
            {
                Console.WriteLine("图像尺寸完全匹配预期值");
            }
        }
    }
}
