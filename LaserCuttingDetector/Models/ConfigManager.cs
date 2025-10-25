using System;
using System.IO;
using System.Xml.Serialization;
using DevExpress.Mvvm;
using HslCommunication;

namespace LaserCuttingDetector.Models
{

    public enum PermissionLevel
    {
        Admin,
        Engineer,
        User
    }

    public partial class ConfigManager:ViewModelBase
    {
        // ReSharper disable once InconsistentNaming
        private static readonly Lazy<ConfigManager> _instance = new Lazy<ConfigManager>(() => new ConfigManager());

        public static ConfigManager Instance => _instance.Value;

        private ConfigManager()
        {
            // Init your configuration settings here
            // For example, load settings from a file or database
        }

        private const string ConfigFilePath = "Configuration\\config.xml"; // Path to your config file

        public OperateResult Init()
        {
            return Load();
        }

        private OperateResult Load()
        {
            if (!File.Exists(ConfigFilePath))
            {
                Directory.CreateDirectory("Configuration");
                // 获取文件夹信息
                var dirInfo = new DirectoryInfo("Configuration");

                // 添加隐藏属性（保留原有属性）
                dirInfo.Attributes |= FileAttributes.Hidden;
                return Save();
            }
            // Load config from file
            using (var fs = new FileStream(ConfigFilePath, FileMode.Open))
            {
                var serializer = new XmlSerializer(typeof(Configuration));
                Config = (Configuration)serializer.Deserialize(fs);
            }
            return OperateResult.CreateSuccessResult();
        }

        public OperateResult Save()
        {
            using (var fs = new FileStream(ConfigFilePath, FileMode.Create))
            {
                var serializer = new XmlSerializer(typeof(Configuration));
                serializer.Serialize(fs, Config);
            }
            return OperateResult.CreateSuccessResult();
        }

        public PermissionLevel CurrentPermissionLevel
        {
            get => GetValue<PermissionLevel>();
            set => SetValue(value);
        }

        #region 参数

        public Configuration Config { get; set; } = new Configuration();

        public class Configuration
        {
            /// <summary>
            /// 管理员密码
            /// </summary>
            [XmlElement(ElementName = "管理员密码")] public string AdminPassword { get; set; } = "admin";
            /// <summary>
            /// 工程师密码
            /// </summary>
            [XmlElement(ElementName = "工程师密码")] public string EngineerPassword { get; set; } = "engineer";
            /// <summary>
            /// 上次左工位产品名称
            /// </summary>
            [XmlElement(ElementName = "上次左产品名称")] public string LastLeftProductName { get; set; } = "DemoProduct";
            /// <summary>
            /// 上次右工位产品名称
            /// </summary>
            [XmlElement(ElementName = "上次右产品名称")] public string LastRightProductName { get; set; } = "DemoProduct";
            /// <summary>
            /// 开机自启动
            /// </summary>
            [XmlElement(ElementName = "开机自启动")] public bool AutoStart { get; set; }
            /// <summary>
            /// 相机品牌
            /// </summary>
            [XmlElement(ElementName = "相机品牌")] public string CameraModel { get; set; } = "海康";
            /// <summary>
            /// 相机接口类型
            /// </summary>
            [XmlElement(ElementName = "相机接口类型")] public string CameraType { get; set; } = "GigE";
            /// <summary>
            /// 是否彩色图像
            /// </summary>
            [XmlElement(ElementName = "是否彩色图像")] public bool IsColorImage { get; set; }

            /// <summary>
            /// 光源控制器品牌
            /// </summary>
            [XmlElement(ElementName = "光源控制器品牌")] public string LightControlModel { get; set; } = "康视达";

            /// <summary>
            /// X方向像素比例 (像素/毫米)
            /// </summary>
            [XmlElement(ElementName = "X方向像素比例")] public double XPixelRatio { get; set; } = 1.0;

            /// <summary>
            /// Y方向像素比例 (像素/毫米)
            /// </summary>
            [XmlElement(ElementName = "Y方向像素比例")] public double YPixelRatio { get; set; } = 1.0;

            /// <summary>
            /// 图像旋转角度 (弧度)
            /// </summary>
            [XmlElement(ElementName = "图像旋转角度")] public double ImageRotationAngle { get; set; }

            /// <summary>
            /// 图像宽度
            /// </summary>
            [XmlElement(ElementName = "图像宽度")] public int ImageWidth { get; set; } = 1920;

            /// <summary>
            /// 图像高度
            /// </summary>
            [XmlElement(ElementName = "图像高度")] public int ImageHeight { get; set; } = 1080;
        }


        #endregion

    }
}
