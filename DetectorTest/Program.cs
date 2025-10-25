using System.Globalization;
using CsvHelper;
using OpenCvSharp;
using VisionLibrary;
using VisionLibrary.Models;

namespace DetectorTest;

internal class Program
{
    static void Main(string[] args)
    {
        // --- 1. 定义文件路径 ---
        string basePath = "./test_data/";
        string configPath = Path.Combine(basePath, "config.json");
        string imagePath = Path.Combine(basePath, "test_image.png");
        string cadImagePath = Path.Combine(basePath, "test_cadImage.png");
        // 只需要一个原始CAD文件
        string cadDataPath = Path.Combine(basePath, "PROJECT_QJ_1AMK63_001_v17_C.csv");
        string outputDir = Path.Combine(basePath, "output");

        if (!Directory.Exists(outputDir)) Directory.CreateDirectory(outputDir);

        // --- 2. 准备配置文件 ---
        if (!File.Exists(configPath))
        {
            var defaultConfig = new ImageInspectionConfig();
            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            string jsonString = System.Text.Json.JsonSerializer.Serialize(defaultConfig, options);
            File.WriteAllText(configPath, jsonString);
        }

        // --- 3. 初始化并运行检测库 ---
        using var library = new ImageInspectionLibrary();
        // Init方法现在只需要config和原始CAD文件路径
        library.Init(configPath, cadDataPath, cadImagePath);

        using Mat image = new Mat(imagePath);
        if (image.Empty())
        {
            Console.WriteLine($"无法加载图像: {imagePath}");
            return;
        }

        InspectionResult result = library.Run(image);

        // --- 4. 处理并保存结果 ---
        Console.WriteLine("\n--- 检测结果摘要 ---");
        Console.WriteLine($"检测到 {result.DetectedComponents.Count} 个部件。");
        Console.WriteLine($"检测到 {result.DetectedBridges.Count} 个桥位。");
        Console.WriteLine($"检测到 {result.WidthSampleResults.Count(s => s.IsValid)} 个有效宽度采样点。");
        Console.WriteLine($"总耗时: {result.ProcessTimeSeconds:F2} 秒。");

        // 保存部件检测结果
        SaveResultsToCsv(result.DetectedComponents, Path.Combine(outputDir, "components_result.csv"));

        // 保存桥位检测结果
        SaveResultsToCsv(result.DetectedBridges, Path.Combine(outputDir, "bridges_result.csv"));

        // 保存宽度检测结果
        SaveResultsToCsv(result.WidthSampleResults, Path.Combine(outputDir, "width_result.csv"));

        Console.WriteLine($"所有检测结果已保存到目录: {outputDir}");

        // --- 5. (可选) 可视化结果 ---
        Mat resultImage = image.Clone();
        VisualizeResults(resultImage, result);
        string outputImagePath = Path.Combine(outputDir, "detection_result.png");
        Cv2.ImWrite(outputImagePath, resultImage);

        resultImage.Dispose();
    }

    // ... (SaveResultsToCsv 和 VisualizeResults 方法保持不变) ...
    public static void SaveResultsToCsv<T>(IEnumerable<T> records, string filePath)
    {
        using var writer = new StreamWriter(filePath);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csv.WriteRecords(records);
    }

    public static void VisualizeResults(Mat image, InspectionResult result)
    {
        // 绘制部件
        foreach (var comp in result.DetectedComponents)
        {
            Scalar color = comp.Result == "T" ? new Scalar(0, 255, 0) : new Scalar(0, 0, 255);
            Rect box = new Rect(comp.XMin, comp.YMin, comp.Width, comp.Height);
            Cv2.Rectangle(image, box, color, 2);
            Cv2.PutText(image, comp.CmpID, new Point(comp.XMin, comp.YMin - 5), HersheyFonts.HersheySimplex, 0.8, color, 2);
        }

        // 绘制桥位
        foreach (var bridge in result.DetectedBridges)
        {
            Scalar color = bridge.Result == "T" ? new Scalar(255, 0, 0) : new Scalar(0, 255, 255);
            Point center = new Point((int)bridge.BridgeCenterX, (int)bridge.BridgeCenterY);
            Cv2.Circle(image, center, 10, color, -1);
            Cv2.PutText(image, $"B{bridge.BrID}", new Point(center.X + 15, center.Y + 5), HersheyFonts.HersheySimplex, 0.6, color, 2);
        }

        // 抽样绘制宽度检测点
        var widthSamplesToDraw = result.WidthSampleResults.Where(s => s.IsValid).Take(200); // 只画前200个点避免画面杂乱
        foreach (var sample in widthSamplesToDraw)
        {
            Scalar color = sample.WidthQualified == "T" && sample.OffsetQualified == "T" ? new Scalar(255, 255, 0) : new Scalar(128, 0, 255); // 合格为青色，不合格为紫色
            Point center = new Point(sample.FinalMappedX, sample.FinalMappedY);
            Cv2.Circle(image, center, 2, color, -1);
        }
    }
}