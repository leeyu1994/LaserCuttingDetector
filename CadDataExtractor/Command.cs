using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System.Windows; // 引入WPF命名空间
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using MessageBox = System.Windows.MessageBox;

namespace CadDataExtractor
{
    public class Commands
    {
        /// <summary>
        /// CAD数据提取命令
        /// </summary>
        [CommandMethod("EXTRACTDATA")]
        public void ExtractCADData()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                // 修改: 使用WPF的枚举 MessageBoxButton 和 MessageBoxImage
                MessageBox.Show("没有打开的CAD文档！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Editor ed = doc.Editor;

            // 显示WPF图层选择和输出设置对话框
            var dialog = new LayerSelectionDialog();
            // Application.ShowModalWindow是推荐的在AutoCAD中显示模态WPF窗口的方法
            bool? dialogResult = Application.ShowModalWindow(dialog);

            if (dialogResult == true)
            {
                var selectedLayers = dialog.SelectedLayers;
                var outputPath = dialog.OutputPath;
                var userOrigin = dialog.UserOrigin;
                var unitScale = dialog.UnitScale;

                // 后续逻辑与之前完全相同...
                var extractor = new CADDataExtractor();
                ed.WriteMessage($"\n开始提取数据...");
                bool success = extractor.ExtractDataFromCurrentDocument(doc, outputPath, selectedLayers, userOrigin, unitScale);

                if (success)
                {
                    ed.WriteMessage($"\n数据提取完成！输出文件: {outputPath}");
                    // 修改: 使用WPF的枚举 MessageBoxButton 和 MessageBoxImage
                    MessageBox.Show($"数据提取完成！\n输出文件: {outputPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    ed.WriteMessage("\n数据提取失败！");
                    // 修改: 使用WPF的枚举 MessageBoxButton 和 MessageBoxImage
                    MessageBox.Show("数据提取失败！请检查文件权限和路径。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 显示帮助信息
        /// </summary>
        [CommandMethod("EXTRACTHELP")]
        public void ShowHelp()
        {
            var helpText = @"CAD数据提取工具使用说明：

命令: EXTRACTDATA
功能: 提取CAD图形中的直线、圆弧、圆等基础几何对象数据

使用步骤:
1. 打开需要处理的CAD文件
2. 输入命令 EXTRACTDATA
3. 在弹出的对话框中选择要提取的图层
4. 设置输出CSV文件路径
5. 可选设置用户坐标系原点和单位缩放
6. 点击确定开始提取

注意事项:
- 复杂对象（多段线、样条曲线等）会被自动分解为基础对象
- 文字、标注等非几何对象会被忽略
- 输出的CSV文件包含对象的详细几何信息";

            // 修改: 使用WPF的枚举 MessageBoxButton 和 MessageBoxImage
            MessageBox.Show(helpText, "帮助", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
