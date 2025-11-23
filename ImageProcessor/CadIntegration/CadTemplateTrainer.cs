// VisionLibrary/CadIntegration/CadTemplateTrainer.cs
using HalconDotNet;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using VisionLibrary.CadIntegration.Models;
using VisionLibrary.CadIntegration.Models.Utils;
using VisionLibrary.CadIntegration.Services; // 假设 Plotter 和 DataLoader 在这里

namespace VisionLibrary.CadIntegration
{
    /// <summary>
    /// CAD 视觉模板训练器
    /// </summary>
    public class CadTemplateTrainer : IDisposable
    {
        private readonly CadGeneratorConfig _config;

        // 产出物
        public HShapeModel CoarseModel { get; private set; } // 粗定位模板
        public HShapeModel FineModel { get; private set; }   // 精定位模板 (十字线)
        public List<PointF> TheoreticalCorners { get; private set; } // 四个角的理论像素坐标
        public PointF ImageCenter { get; private set; }      // 图像中心

        public CadTemplateTrainer(CadGeneratorConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// 主流程：加载CSV -> 绘图 -> 生成Halcon模板
        /// </summary>
        /// <param name="csvPath">CSV文件路径</param>
        public void GenerateTemplates(string csvPath)
        {
            // 1. 加载 CAD 数据
            var loader = new CADDataLoader(); // 需确保 DataLoader 能读取 PartId
            var rawData = loader.LoadCADData(csvPath);
            var (allElements, _, _) = loader.ExtractBasicElements(rawData);

            // 2. 筛选 "最后验证" 的8条线
            var verificationElements = allElements
                .Where(e => e.PartId == "最后验证")
                .ToList();

            if (verificationElements.Count == 0)
                throw new Exception("未在CAD数据中找到 '最后验证' 部件");

            // 3. 计算边界 (这是你的要求：CAD图长宽 = 十字线组成的区域)
            var bounds = CADUtils.GetTotalBounds(verificationElements);

            // 4. 在内存中绘制 CAD 图像
            // 使用我们改造后的 CADPlotter，只画 verificationElements 或者画全部
            // 为了定位纯净，建议只画验证元素，或者画全部但确保验证元素在最上层
            using var skImage = DrawCadToImage(verificationElements, bounds);

            // 5. 转为 Halcon Image
            using var hImage = SkiaToHalcon(skImage);

            // 6. Halcon 训练逻辑
            TrainHalconModels(hImage, verificationElements, bounds);
        }

        private SKImage DrawCadToImage(List<CADElement> elements, System.Drawing.RectangleF bounds)
        {
            // 实例化你的 CADPlotter
            var plotter = new CADPlotter(bounds, marginMm: 0); // 0边距
            plotter.DrawElements(elements);

            // 这里需要修改 Plotter 让它支持返回 SKImage 而不是直接存盘
            // 或者临时存盘再读取（简单但慢），这里演示内存操作逻辑：
            return plotter.GetRenderedImage();
        }

        private HImage SkiaToHalcon(SKImage skImage)
        {
            using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            byte[] bytes = stream.ToArray();

            // 即使是内存流，Halcon有些版本也需要特定处理，
            // 最稳妥的跨平台方式还是生成临时文件，或者使用指针拷贝(如果对性能要求极高)
            // 这里为了代码简洁，演示临时文件方式，生产环境可优化为指针拷贝
            string tempFile = Path.GetTempFileName() + ".png";
            File.WriteAllBytes(tempFile, bytes);

            HImage hImage = new HImage(tempFile);
            File.Delete(tempFile);
            return hImage;
        }

        private void TrainHalconModels(HImage image, List<CADElement> elements, System.Drawing.RectangleF boundsMm)
        {
            image.GetImageSize(out int width, out int height);
            ImageCenter = new PointF(width / 2f, height / 2f);

            // --- A. 训练粗定位 (中心区域) ---
            HRegion centerRegion = new HRegion();
            centerRegion.GenRectangle1(
                (double)(ImageCenter.Y - _config.CoarseRegionSize / 2),
                (double)(ImageCenter.X - _config.CoarseRegionSize / 2),
                (double)(ImageCenter.Y + _config.CoarseRegionSize / 2),
                (double)(ImageCenter.X + _config.CoarseRegionSize / 2)
            );

            HImage coarseImage = image.ReduceDomain(centerRegion);
            CoarseModel = new HShapeModel();
            // 创建基于形状的模板
            CoarseModel.CreateShapeModel(coarseImage, "auto", -0.39, 0.79, "auto", "auto", "use_polarity", "auto", "auto");

            // --- B. 计算四个角的理论像素坐标 ---
            // 需要解析 elements (8条线) 得到4个交点。
            // 逻辑：将毫米坐标转为像素坐标 (利用 boundsMm 和 Config.PixelSize)
            TheoreticalCorners = CalculateCornerPixels(elements, boundsMm, height);

            if (TheoreticalCorners.Count != 4)
                throw new Exception($"预期找到4个角点，实际计算出 {TheoreticalCorners.Count} 个");

            // --- C. 训练精定位 (取左上角作为十字线模板) ---
            // 假设第0个是左上角 (需要 CalculateCornerPixels 保证顺序)
            PointF crossCenter = TheoreticalCorners[0];

            HRegion crossRegion = new HRegion();
            crossRegion.GenRectangle1(
                (double)(crossCenter.Y - _config.FineRegionSize / 2),
                (double)(crossCenter.X - _config.FineRegionSize / 2),
                (double)(crossCenter.Y + _config.FineRegionSize / 2),
                (double)(crossCenter.X + _config.FineRegionSize / 2)
            );

            HImage crossImageReduced = image.ReduceDomain(crossRegion);
            FineModel = new HShapeModel();
            FineModel.CreateShapeModel(crossImageReduced, "auto", -0.39, 0.79, "auto", "auto", "use_polarity", "auto", "auto");
        }

        private List<PointF> CalculateCornerPixels(List<CADElement> elements, System.Drawing.RectangleF bounds, int imgHeight)
        {
            // 1. 提取所有端点 (毫米)
            var points = new List<PointF>();
            foreach (var e in elements)
            {
                if (e is LineElement l) { points.Add(l.Start); points.Add(l.End); }
            }

            // 2. 聚类找到4个中心 (简单按照象限划分)
            float midX = bounds.Left + bounds.Width / 2;
            float midY = bounds.Top + bounds.Height / 2;

            var cornersMm = new List<PointF>
            {
                GetCentroid(points.Where(p => p.X < midX && p.Y > midY).ToList()), // 左上 (CAD Y向上)
                GetCentroid(points.Where(p => p.X > midX && p.Y > midY).ToList()), // 右上
                GetCentroid(points.Where(p => p.X > midX && p.Y < midY).ToList()), // 右下
                GetCentroid(points.Where(p => p.X < midX && p.Y < midY).ToList())  // 左下
            };

            // 3. 转像素
            var pixels = new List<PointF>();
            foreach (var p in cornersMm)
            {
                // X = (x_mm - min_x) / pixel_size
                float px = (p.X - bounds.Left) / _config.PixelSize;
                // Y = height - (y_mm - min_y) / pixel_size (翻转Y轴)
                float py = imgHeight - (p.Y - bounds.Top) / _config.PixelSize;
                pixels.Add(new PointF(px, py));
            }

            // Halcon顺序通常按行扫描：左上，右上，左下，右下
            // 我们需要对 pixels 进行排序以匹配这个顺序
            // 先按Y排序(小到大)，再按X排序
            return pixels.OrderBy(p => p.Y).ThenBy(p => p.X).ToList();
        }

        private PointF GetCentroid(List<PointF> pts)
        {
            if (!pts.Any()) return PointF.Empty;
            return new PointF(pts.Average(p => p.X), pts.Average(p => p.Y));
        }

        public void Dispose()
        {
            CoarseModel?.Dispose();
            FineModel?.Dispose();
        }
    }
}
