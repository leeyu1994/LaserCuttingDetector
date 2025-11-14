using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevExpress.Mvvm;
using System.IO;
using System.Text.Encodings.Web; // 新增: 用于JSON编码
using System.Text.Json;
using VisionLibrary.Models;

namespace LaserCuttingDetector.ViewModels
{
    public partial class ParamsSetViewModel : ObservableObject, ISupportServices
    {
        #region 服务
        private IServiceContainer _serviceContainer;
        protected IServiceContainer ServiceContainer => _serviceContainer ??= new ServiceContainer(this);
        IServiceContainer ISupportServices.ServiceContainer => ServiceContainer;

        ICurrentWindowService CurrentWindowService => ServiceContainer.GetService<ICurrentWindowService>();
        // 建议：添加一个消息框服务用于显示错误信息
        IMessageBoxService MessageBoxService => ServiceContainer.GetService<IMessageBoxService>();
        #endregion

        #region 字段
        private readonly string _configFilePath;
        #endregion

        #region 属性
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
        [RelayCommand]
        private void SaveAndClose()
        {
            // 捕获潜在的文件写入异常
            try
            {
                // 设置JSON序列化选项
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true, // 格式化输出（美观）
                    // 使用不转义任何字符的编码器，确保中文等字符正确显示
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                string jsonString = JsonSerializer.Serialize(Config, options);
                File.WriteAllText(_configFilePath, jsonString);

                // 仅在成功保存后关闭窗口
                CurrentWindowService?.Close();
            }
            catch (System.Exception ex)
            {
                // 如果保存失败，向用户显示错误信息
                MessageBoxService?.ShowMessage($"保存配置文件失败: {ex.Message}", "错误", MessageButton.OK,MessageIcon.Error);
            }
        }
        #endregion

        #region 辅助方法
        private void LoadConfig()
        {
            if (!File.Exists(_configFilePath))
            {
                Config = new ImageInspectionConfig();
                return;
            }

            // 捕获潜在的文件读取或解析异常
            try
            {
                string jsonString = File.ReadAllText(_configFilePath);
                Config = JsonSerializer.Deserialize<ImageInspectionConfig>(jsonString) ?? new ImageInspectionConfig();
            }
            catch (System.Exception ex)
            {
                // 如果加载失败，创建一个默认配置并通知用户
                Config = new ImageInspectionConfig();
                MessageBoxService?.ShowMessage($"加载配置文件失败: {ex.Message}\n已为您创建默认配置。", "警告", MessageButton.OK, MessageIcon.Warning);
            }
        }
        #endregion
    }
}
