using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevExpress.Mvvm;
using System.IO;
using System.Text.Json;
using VisionLibrary.Models;
namespace LaserCuttingDetector.ViewModels
{
    public partial class ParamsSetViewModel : ObservableObject, ISupportServices
    {
        #region 服务
        // MVVM服务容器，用于获取ICurrentWindowService来关闭窗口
        private IServiceContainer _serviceContainer;
        protected IServiceContainer ServiceContainer => _serviceContainer ??= new ServiceContainer(this);
        IServiceContainer ISupportServices.ServiceContainer => ServiceContainer;

        // 获取当前窗口服务，以便在ViewModel中关闭对话框
        ICurrentWindowService CurrentWindowService => ServiceContainer.GetService<ICurrentWindowService>();
        #endregion

        #region 字段
        // 保存当前产品配置文件的完整路径
        private readonly string _configFilePath;
        #endregion

        #region 属性
        /// <summary>
        /// 用于数据绑定的配置对象
        /// </summary>
        [ObservableProperty]
        private ImageInspectionConfig _config;
        #endregion

        #region 构造函数
        public ParamsSetViewModel(string configFilePath)
        {
            _configFilePath = configFilePath;
            LoadConfig();
        }
        #endregion

        #region 命令
        /// <summary>
        /// 保存当前修改的参数并关闭对话框
        /// </summary>
        [RelayCommand]
        private void SaveAndClose()
        {
            // 设置JSON序列化选项，使其格式化输出（美观）
            var options = new JsonSerializerOptions { WriteIndented = true };
            // 将Config对象序列化为JSON字符串
            string jsonString = JsonSerializer.Serialize(Config, options);
            // 将JSON字符串写入文件
            File.WriteAllText(_configFilePath, jsonString);

            // 使用服务关闭当前窗口
            CurrentWindowService?.Close();
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 从指定路径加载配置文件
        /// </summary>
        private void LoadConfig()
        {
            // 检查文件是否存在
            if (!File.Exists(_configFilePath))
            {
                // 如果文件不存在，就创建一个新的默认配置对象
                // 注意：这里不保存，让用户在第一次设置后点击保存按钮来创建文件
                Config = new ImageInspectionConfig();
                return;
            }

            // 读取JSON文件内容
            string jsonString = File.ReadAllText(_configFilePath);
            // 反序列化JSON到Config对象，如果失败则创建一个新的默认对象
            Config = JsonSerializer.Deserialize<ImageInspectionConfig>(jsonString) ?? new ImageInspectionConfig();
        }
        #endregion
    }
}
