using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadDataExtractor
{
    // 主ViewModel，掌管对话框所有的数据和逻辑
    public class LayerSelectionViewModel : INotifyPropertyChanged
    {
        // 属性
        public ObservableCollection<LayerItemViewModel> Layers { get; set; }
        public string OutputPath { get; set; }
        public bool UseCustomOrigin { get; set; }
        public string OriginX { get; set; } = "0.0";
        public string OriginY { get; set; } = "0.0";
        public List<string> UnitScaleOptions { get; } = new List<string> { "1.0 (毫米)", "0.1 (厘米)", "0.001 (米)", "25.4 (英寸)" };
        public int SelectedUnitScaleIndex { get; set; } = 0;

        // 命令 (WPF中推荐使用命令来处理UI事件，而不是传统的Click事件处理器)
        public ICommand SelectAllCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand BrowseCommand { get; }

        public LayerSelectionViewModel()
        {
            Layers = new ObservableCollection<LayerItemViewModel>();
            LoadLayers();

            // 初始化命令
            SelectAllCommand = new RelayCommand(p => SelectAll(true));
            ClearAllCommand = new RelayCommand(p => SelectAll(false));
            BrowseCommand = new RelayCommand(p => BrowseFile());
        }

        private void LoadLayers()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Database db = doc.Database;
            using (var trans = db.TransactionManager.StartTransaction())
            {
                var layerTable = (LayerTable)trans.GetObject(db.LayerTableId, OpenMode.ForRead);
                foreach (ObjectId layerId in layerTable)
                {
                    var layer = (LayerTableRecord)trans.GetObject(layerId, OpenMode.ForRead);
                    Layers.Add(new LayerItemViewModel { Name = layer.Name, IsSelected = true }); // 默认全选
                }
                trans.Commit();
            }
        }

        private void SelectAll(bool select)
        {
            foreach (var layer in Layers)
            {
                layer.IsSelected = select;
            }
        }

        private void BrowseFile()
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "CSV文件|*.csv|所有文件|*.*",
                DefaultExt = "csv",
                FileName = "CAD_Data_Export.csv"
            };

            if (saveDialog.ShowDialog() == true)
            {
                OutputPath = saveDialog.FileName;
                OnPropertyChanged(nameof(OutputPath)); // 通知UI更新路径文本框
            }
        }

        // INotifyPropertyChanged 实现
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // 一个简单的RelayCommand实现，用于将方法绑定到UI命令
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        public RelayCommand(Action<object> execute) { _execute = execute; }
        public bool CanExecute(object parameter) => true;
        public void Execute(object parameter) => _execute(parameter);
        public event EventHandler CanExecuteChanged { add { } remove { } }
    }
}
