using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace CadDataExtractor
{
    public partial class LayerSelectionDialog : Window
    {
        // 公共属性，用于在关闭对话框后从外部获取数据
        public List<string> SelectedLayers { get; private set; }
        public string OutputPath { get; private set; }
        public Point3d? UserOrigin { get; private set; }
        public double UnitScale { get; private set; }

        private LayerSelectionViewModel _viewModel;

        public LayerSelectionDialog()
        {
            InitializeComponent();
            // 创建并设置ViewModel
            _viewModel = new LayerSelectionViewModel();
            this.DataContext = _viewModel;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. 从ViewModel获取选中的图层
            SelectedLayers = _viewModel.Layers.Where(l => l.IsSelected).Select(l => l.Name).ToList();
            if (SelectedLayers.Count == 0)
            {
                MessageBox.Show("请至少选择一个图层！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. 获取输出路径
            OutputPath = _viewModel.OutputPath;
            if (string.IsNullOrEmpty(OutputPath))
            {
                MessageBox.Show("请指定输出文件路径！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. 获取原点设置
            if (_viewModel.UseCustomOrigin)
            {
                if (double.TryParse(_viewModel.OriginX, out double x) &&
                    double.TryParse(_viewModel.OriginY, out double y))
                {
                    UserOrigin = new Point3d(x, y, 0);
                }
                else
                {
                    MessageBox.Show("原点坐标格式不正确！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // 4. 获取单位缩放
            switch (_viewModel.SelectedUnitScaleIndex)
            {
                case 0: UnitScale = 1.0; break;
                case 1: UnitScale = 0.1; break;
                case 2: UnitScale = 0.001; break;
                case 3: UnitScale = 25.4; break;
                default: UnitScale = 1.0; break;
            }

            // 所有验证通过，设置DialogResult为true并关闭窗口
            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
