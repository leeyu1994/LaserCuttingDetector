using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevExpress.Mvvm;
using LaserCuttingDetector.Commons;
using LaserCuttingDetector.Dialogs;
using LaserCuttingDetector.Models;
using LaserCuttingDetector.UserControls;
using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using VisionLibrary;
using VisionLibrary.Models;
using ImageFormat = System.Drawing.Imaging.ImageFormat;
using LogManager = LaserCuttingDetector.Models.LogManager;
using Point = System.Windows.Point;

namespace LaserCuttingDetector.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject, ISupportServices
    {
        private IServiceContainer _serviceContainer;
        protected IServiceContainer ServiceContainer => _serviceContainer ??= new ServiceContainer(this);
        IServiceContainer ISupportServices.ServiceContainer => ServiceContainer;

        private ImageInspectionConfig _currentConfig;

        #region 服务
        // 服务部分保持不变，它们与MVVM框架的实现无关
        IMessageBoxService MessageBoxService => ServiceContainer.GetService<IMessageBoxService>();
        ISplashScreenService SplashScreenService => ServiceContainer.GetService<ISplashScreenService>();
        #endregion

        #region 字段
        private Bitmap _sourceImage; // 用于暂存从相机接收的最新图像
        private readonly Lock _imageLock = new(); // 用于线程安全地访问 _sourceImage

        private CancellationTokenSource _grabbingCts; // 用于控制持续采集流的取消
        // 添加相机状态相关属性
        [ObservableProperty]
        private bool _isCameraInitialized;

        [ObservableProperty]
        private bool _isCameraGrabbing;

        [ObservableProperty]
        private string _cameraStatusText = "相机未初始化";

        [ObservableProperty]
        private ObservableCollection<AggregatedWidthDefect> _widthDefects;

        // 【新增】本地相机控制器实例
        private CameraController _cameraController;
        // 【新增】用于在停止时能正确取消订阅的事件处理器委托实例
        private Action<Bitmap> _imageReceivedHandler;


        private ImageInspectionLibrary _imageInspector;
        private double _pixelSize;
        private Image _diffImage;
        private Image _regionImage;
        private Image _originImage;

        // 定义一个常量用于控制选中项的放大倍数
        private const double DefaultZoomFactor = 2.5;
        // 定义一个常量用于控制红色定位框的大小（像素）
        private const double LocatorBoxSize = 150;
        #endregion

        #region 构造函数
        public MainWindowViewModel()
        {
            // 初始化缺陷数据和用于绘制矩形框的集合
            BridgeDefects = new ObservableCollection<BridgeResult>();
            WidthDefects = new ObservableCollection<AggregatedWidthDefect>();
            MisalignmentDefects = new ObservableCollection<ComponentResult>();
            IndentationDefects = new ObservableCollection<IndentationResult>(); // 【新增】初始化压痕缺陷集合
            Shapes = new ObservableCollection<DrawableShape>();

        }
        #endregion

        #region 相机事件处理

        private void OnCameraLogReceived(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                LogManager.Instance.Info($"[相机] {message}");
            });
        }

        private void OnCameraStatusChanged(bool isActive)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsCameraInitialized = isActive;
                CameraStatusText = isActive ? "相机已就绪" : "相机未初始化";
            });
        }

        #endregion

        #region 核心数据绑定属性 (使用 CommunityToolkit.Mvvm 重构)

        [ObservableProperty]
        private Image _mainImage;

        [ObservableProperty]
        private bool _shouldResetImageView = true;

        [ObservableProperty]
        private bool _isWaitIndicatorVisible;

        [ObservableProperty]
        private string _waitIndicatorText;

        [ObservableProperty]
        private ObservableCollection<BridgeResult> _bridgeDefects;


        [ObservableProperty]
        private ObservableCollection<ComponentResult> _misalignmentDefects;

        // 【新增】压痕缺陷的数据集合，用于绑定UI
        [ObservableProperty]
        private ObservableCollection<IndentationResult> _indentationDefects;

        #endregion

        #region 列表选中项与图像交互 (使用 CommunityToolkit.Mvvm 重构)

        // 控制 PictureViewer 居中显示的点坐标
        [ObservableProperty]
        private Point _centerPoint;

        // 控制 PictureViewer 的缩放级别
        [ObservableProperty]
        private double _zoomFactor;

        // 用于在图像上绘制矩形框的集合
        [ObservableProperty]
        private ObservableCollection<DrawableShape> _shapes;

        // 当在UI上选中的桥位缺陷行改变时，将触发 OnSelectedBridgeDefectChanged 方法
        [ObservableProperty]
        private BridgeResult _selectedBridgeDefect;

        // 当在UI上选中的宽度缺陷行改变时，将触发 OnSelectedWidthDefectChanged 方法
        [ObservableProperty]
        private AggregatedWidthDefect _selectedWidthDefect;

        // 当在UI上选中的错位缺陷行改变时，将触发 OnSelectedMisalignmentDefectChanged 方法
        [ObservableProperty]
        private ComponentResult _selectedMisalignmentDefect;

        // 当在UI上选中的压痕缺陷行改变时，将触发 OnSelectedIndentationDefectChanged 方法
        [ObservableProperty]
        private IndentationResult _selectedIndentationDefect;


        /// <summary>
        /// 当选中的桥位缺陷变化时执行的逻辑
        /// </summary>
        /// <param name="value">新的选中项</param>
        partial void OnSelectedBridgeDefectChanged(BridgeResult value)
        {
            // 清除旧形状
            Shapes.Clear();

            if (value != null)
            {
                // 1. 定义中心点和要绘制的方框大小
                var center = new Point(value.BridgeCenterX, value.BridgeCenterY);
                double halfSize = LocatorBoxSize / 2.0;

                // 2. 创建方框的四个顶点
                var points = new PointCollection
                {
                    new Point(center.X - halfSize, center.Y - halfSize), // 左上
                    new Point(center.X + halfSize, center.Y - halfSize), // 右上
                    new Point(center.X + halfSize, center.Y + halfSize), // 右下
                    new Point(center.X - halfSize, center.Y + halfSize)  // 左下
                };

                // 3. 创建 DrawableShape 并添加到集合中
                var shape = new DrawableShape
                {
                    Points = points,
                    IsSelected = true
                };
                Shapes.Add(shape);

                // 4. 更新UI视图，使其居中放大
                CenterPoint = center;
                ZoomFactor = DefaultZoomFactor;
            }
        }



        /// <summary>
        /// 当选中的宽度缺陷变化时执行的逻辑
        /// </summary>
        partial void OnSelectedWidthDefectChanged(AggregatedWidthDefect value)
        {

            UpdateShape(value, (defect) =>
            {
                // defect.DefectShapePoints 是 OpenCvSharp.Point[]
                // 需要转换为 System.Windows.Media.PointCollection
                var wpfPoints = new PointCollection(
                    defect.DefectShapePoints.Select(p => new System.Windows.Point(p.X, p.Y))
                );
                return (new System.Windows.Point(defect.CenterPoint.X, defect.CenterPoint.Y), wpfPoints);
            });
        }

        /// <summary>
        /// 当选中的错位缺陷变化时执行的逻辑
        /// </summary>
        partial void OnSelectedMisalignmentDefectChanged(ComponentResult value)
        {
            UpdateShape(value, (defect) =>
            {
                if (defect.Contour == null || defect.Contour.Length == 0) return null;

                var wpfPoints = new PointCollection(
                    defect.Contour.Select(p => new System.Windows.Point(p.X, p.Y))
                );

                // 计算轮廓的中心点用于定位
                double centerX = defect.Contour.Average(p => p.X);
                double centerY = defect.Contour.Average(p => p.Y);

                return (new System.Windows.Point(centerX, centerY), wpfPoints);
            });
        }

        // 【新增】当选中的压痕缺陷变化时执行的逻辑
        partial void OnSelectedIndentationDefectChanged(IndentationResult value)
        {
            Shapes.Clear(); // 清除旧的形状

            if (value != null && value.CenterX_Result.HasValue && value.CenterY_Result.HasValue)
            {
                // 压痕缺陷没有轮廓，我们像桥位一样，在它的中心点绘制一个定位框
                var center = new Point(value.CenterX_Result.Value, value.CenterY_Result.Value);
                double halfSize = LocatorBoxSize / 2.0;

                var points = new PointCollection
                {
                    new Point(center.X - halfSize, center.Y - halfSize),
                    new Point(center.X + halfSize, center.Y - halfSize),
                    new Point(center.X + halfSize, center.Y + halfSize),
                    new Point(center.X - halfSize, center.Y + halfSize)
                };

                var shape = new DrawableShape
                {
                    Points = points,
                    IsSelected = true
                };
                Shapes.Add(shape);

                // 更新UI视图，使其居中放大
                CenterPoint = center;
                ZoomFactor = DefaultZoomFactor;
            }
        }


        // 创建一个通用的 UpdateShape 方法，替换 UpdateLocator
        private void UpdateShape<T>(T defect, Func<T, (System.Windows.Point center, PointCollection points)?> getShapeFunc) where T : class
        {
            Shapes.Clear(); // 每次选择都清除旧的形状

            if (defect != null)
            {
                var shapeData = getShapeFunc(defect);
                if (shapeData.HasValue)
                {
                    var (center, points) = shapeData.Value;

                    // 更新绑定属性，触发UI居中和放大
                    CenterPoint = center;
                    ZoomFactor = DefaultZoomFactor;

                    // 创建新的 DrawableShape 并添加到集合中
                    var shape = new DrawableShape
                    {
                        Points = points,
                        IsSelected = true
                    };
                    Shapes.Add(shape);
                }
            }
        }

        #endregion

        #region 核心命令 (使用 CommunityToolkit.Mvvm 重构)

        [RelayCommand]
        private async void ViewLoadAsync()
        {
            StartGrpcServer();

            // 【修改】初始化本地相机
            _cameraController = new CameraController();
            // 订阅日志事件，直接在UI上显示相机日志
            _cameraController.LogMessage += (msg) =>
            {
                Application.Current.Dispatcher.Invoke(() => LogManager.Instance.Info($"[相机] {msg}"));
            };

            InitializeCamera(); // 调用新的本地初始化方法
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Solutions");
            Products = new ObservableCollection<string>(Directory.GetDirectories(path)
                .Select(Path.GetFileName)
                .ToList());
            SelectedProduct = Products.FirstOrDefault();
        }

        [RelayCommand]
        private async Task ViewClosedAsync()
        {
            // 确保停止采集
            if (IsCameraGrabbing)
            {
                await StopAsync();
            }

            // 释放本地相机资源
            _cameraController?.Dispose();
            _cameraController = null;

            // 停止 gRPC 服务器
            StopGrpcServer();

            // 释放客户端资源
            GrpcClientService.Instance.Dispose();

            // 清理本地图像资源
            lock (_imageLock)
            {
                _sourceImage?.Dispose();
                _sourceImage = null;
            }

            // 【修改】现在我们可以安全地释放这些Bitmap对象了
            DisposeImageSafely(ref _regionImage);
            DisposeImageSafely(ref _diffImage);
            DisposeImageSafely(ref _originImage);

            // MainImage 也是 Image 类型，也需要释放
            if (_mainImage is IDisposable disposableImage)
            {
                disposableImage.Dispose();
            }
            _mainImage = null;

            _imageInspector?.Dispose();
        }

        private void DisposeImageSafely(ref Image image)
        {
            if (image != null)
            {
                image.Dispose();
                image = null;
            }
        }
        #endregion

        #region 其他属性和命令 (使用 CommunityToolkit.Mvvm 重构)


        [ObservableProperty] private ObservableCollection<string> _products;

        // 对于 setter 中有逻辑的属性，手动实现
        private string _selectedProduct;
        public string SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                if (SetProperty(ref _selectedProduct, value) && value != null)
                {
                    _ = LoadProduct(value);
                }
            }
        }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoginCommand))] // 当 LoginStatus 变化时，通知 LoginCommand 的 CanExecute 需要重新评估
        [NotifyCanExecuteChangedFor(nameof(LogoutCommand))]// 同上，通知 LogoutCommand
        private bool _loginStatus;

        /// <summary>
        /// 登录命令的实现
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanLogin))]
        private void Login()
        {

            // 创建登录对话框实例
            var loginDialog = new LoginDialog();

            // 以模态方式显示登录对话框
            bool? dialogResult = loginDialog.ShowDialog();

            // 检查用户是否点击了确定按钮且密码验证成功
            if (dialogResult == true)
            {
                // 登录成功，更新登录状态
                LoginStatus = true;

                // 根据当前权限级别显示相应的欢迎信息
                string roleText = ConfigManager.Instance.CurrentPermissionLevel == PermissionLevel.Engineer ? "工程师" : "管理员";
                MessageBoxService.ShowMessage($"欢迎，{roleText}！", "登录成功", MessageButton.OK, MessageIcon.Information);

                // 记录日志
                LogManager.Instance.Info($"用户以{roleText}身份登录成功");
            }
            else
            {
                // 用户取消登录或密码验证失败
                LogManager.Instance.Info("用户取消登录或密码验证失败");
            }
        }

        /// <summary>
        /// 判断是否可以执行登录命令
        /// </summary>
        /// <returns>当前未登录时返回true</returns>
        private bool CanLogin() => !LoginStatus;

        /// <summary>
        /// 登出命令的实现
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanLogout))]
        private void Logout()
        {
            // 显示确认对话框
            var result = MessageBoxService.ShowMessage(
                "确定要登出吗？",
                "确认登出",
                MessageButton.YesNo,
                MessageIcon.Question);

            // 用户确认登出
            if (result == MessageResult.Yes)
            {
                // 重置权限级别为默认值（无权限）
                ConfigManager.Instance.CurrentPermissionLevel = PermissionLevel.User;

                // 更新登录状态
                LoginStatus = false;

                // 显示登出成功信息
                MessageBoxService.ShowMessage("已成功登出", "登出", MessageButton.OK, MessageIcon.Information);

                // 记录日志
                LogManager.Instance.Info("用户已登出");
            }
            else
            {
                // 用户取消登出操作
                LogManager.Instance.Info("用户取消登出操作");
            }
        }

        /// <summary>
        /// 判断是否可以执行登出命令
        /// </summary>
        /// <returns>当前已登录时返回true</returns>
        private bool CanLogout() => LoginStatus;
        [RelayCommand]
        private async Task StartGrabAsync()
        {
            if (!IsCameraInitialized)
            {
                MessageBoxService.ShowMessage("请先初始化相机", "提示", MessageButton.OK, MessageIcon.Warning);
                return;
            }

            if (IsCameraGrabbing) // 防止重复点击
            {
                return;
            }

            // 【核心修改】
            // 1. 定义当相机接收到图像时要执行的操作
            _imageReceivedHandler = async (receivedBitmap) =>
            {
                // 克隆图像，因为原始 bitmap 很快会被相机SDK回收
                using var imageClone = (Bitmap)receivedBitmap.Clone();

                // 将 Bitmap 转换为 byte[]
                byte[] imageBytes;
                using (var ms = new MemoryStream())
                {
                    // 使用 Bmp 格式，因为它无损且快速
                    imageClone.Save(ms, ImageFormat.Bmp);
                    imageBytes = ms.ToArray();
                }

                // 通过 gRPC 将字节数据发送到服务器进行处理
                var result = await GrpcClientService.Instance.ProcessImageFromBytesStreamedAsync(imageBytes);

                // 在UI线程上处理返回的结果
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await ProcessVisionResultAsync(result);
                });
            };

            // 2. 订阅事件
            _cameraController.ImageReceived += _imageReceivedHandler;

            // 3. 启动相机硬件采集
            if (_cameraController.StartGrab(true))
            {
                IsCameraGrabbing = true;
                CameraStatusText = "正在采集...";
                LogManager.Instance.Info("相机已启动，等待硬件触发...");
            }
            else
            {
                // 启动失败，取消订阅
                _cameraController.ImageReceived -= _imageReceivedHandler;
                MessageBoxService.ShowMessage("启动相机采集失败！", "错误", MessageButton.OK, MessageIcon.Error);
            }
        }
        [RelayCommand]
        private async Task StopAsync()
        {
            if (!IsCameraGrabbing)
            {
                return;
            }

            // 【核心修改】
            // 1. 停止相机硬件
            _cameraController.StopGrab();

            // 2. 取消订阅事件，防止内存泄漏和意外调用
            if (_imageReceivedHandler != null)
            {
                _cameraController.ImageReceived -= _imageReceivedHandler;
                _imageReceivedHandler = null; // 清空引用
            }

            // 3. 更新UI状态
            IsCameraGrabbing = false;
            CameraStatusText = IsCameraInitialized ? "相机已就绪" : "相机未初始化";
            LogManager.Instance.Info("相机采集已停止。");

            // 这里返回一个完成的任务以匹配 async Task 签名
            await Task.CompletedTask;
        }

        [RelayCommand]
        private void ShowDiff()
        {
            if (_diffImage != null)
            {
                // 切换时不重置视图
                ShouldResetImageView = false;
                MainImage = _diffImage;
            }
        }

        [RelayCommand]
        private void ShowRegion()
        {
            if (_regionImage != null)
            {
                // 切换时不重置视图
                ShouldResetImageView = false;
                MainImage = _regionImage;
            }
        }

        [RelayCommand]
        private void ShowOrigin()
        {
            if (_originImage != null)
            {
                // 切换时不重置视图
                ShouldResetImageView = false;
                MainImage = _originImage;
            }
        }
        [RelayCommand] private void ShowHideRect() { }

        [RelayCommand]
        private async Task LocalImageTestAsync()
        {
            // 创建文件选择对话框
            var openFileDialog = new OpenFileDialog
            {
                Title = "请选择一个图像文件进行测试",
                Filter = "图像文件 (*.bmp;*.jpg;*.jpeg;*.png;*.tiff)|*.bmp;*.jpg;*.jpeg;*.png;*.tiff|" +
                         "所有文件 (*.*)|*.*",
                FilterIndex = 1,
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() != true)
            {
                LogManager.Instance.Info("用户取消了本地图片测试操作。");
                return;
            }

            string selectedFilePath = openFileDialog.FileName;
            LogManager.Instance.Info($"用户选择了本地图片：{selectedFilePath}");

            IsWaitIndicatorVisible = true;
            WaitIndicatorText = "处理本地图像中，请稍候...";

            // 2. 调用新的gRPC流式方法，直接传递图像的字节数据
            // 1. 将文件完整读取到字节数组中
            byte[] imageBytes = await File.ReadAllBytesAsync(selectedFilePath);
            var result = await GrpcClientService.Instance.ProcessImageFromBytesStreamedAsync(imageBytes);

            // 调用更新后的 ProcessVisionResultAsync
            await ProcessVisionResultAsync(result);

            IsWaitIndicatorVisible = false;
        }
        /// <summary>
        /// 获取支持的图像文件扩展名
        /// </summary>
        /// <returns>支持的扩展名列表</returns>
        private static readonly string[] SupportedImageExtensions =
        [
            ".bmp", ".jpg", ".jpeg", ".png", ".tiff", ".tif"
        ];

        /// <summary>
        /// 处理视觉检测结果的通用方法（已适配流式传输）
        /// </summary>
        /// <param name="result">包含元数据和结果图像的流式处理结果对象</param>
        private async Task ProcessVisionResultAsync(ProcessStreamedResult result)
        {
            // 步骤 1: 基本有效性检查
            if (result?.Metadata == null)
            {
                // 如果结果或元数据为空，则无法继续，释放可能存在的图像资源
                result?.ResultImage?.Dispose();
                LogManager.Instance.Error("ProcessVisionResultAsync 收到无效的 null 结果或元数据。");
                return;
            }

            // 从结果对象中提取元数据和待分析的图像
            var metadata = result.Metadata;
            var analysisBitmap = result.ResultImage;

            // 将所有耗时操作放入后台线程，避免UI卡顿
            await Task.Run(() =>
            {
                // 步骤 2: 检查服务端（VM流程）是否成功执行
                if (!metadata.Success)
                {
                    // 如果服务端失败，在UI线程显示错误信息并终止
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBoxService.ShowMessage($"服务器端处理失败: {metadata.Message}", "错误", MessageButton.OK, MessageIcon.Error);
                        LogManager.Instance.Error($"服务器端视觉处理失败: {metadata.Message}");
                    });
                    // 确保释放图像资源，防止内存泄漏
                    analysisBitmap?.Dispose();
                    return;
                }

                // 步骤 3: 检查在客户端进行详细检测所需的所有前提条件
                if (_imageInspector == null || analysisBitmap == null || metadata.CornerPoints.Count < 4)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        string errorMsg = "执行本地检测失败：\n";
                        if (_imageInspector == null)
                            errorMsg += "- 本地检测库未初始化，请先选择产品。\n";
                        if (analysisBitmap == null)
                            errorMsg += "- 服务器未返回有效的图像数据。\n";
                        if (metadata.CornerPoints.Count < 4)
                            errorMsg += $"- 服务器返回的角点数量不足 ({metadata.CornerPoints.Count})。";

                        MessageBoxService.ShowMessage(errorMsg, "前提条件不足", MessageButton.OK, MessageIcon.Error);
                        LogManager.Instance.Error(errorMsg.Replace("\n", " "));
                    });
                    analysisBitmap?.Dispose();
                    return;
                }

                // 步骤 4: 执行核心检测算法，并正确管理资源
                // 使用 using 语句确保 analysisBitmap 和转换后的 sourceMat 在使用后被自动释放
                using (analysisBitmap)
                using (var sourceMat = analysisBitmap.ToMat())
                {
                    var cornerPoints = metadata.CornerPoints
                                               .Select(p => new Point2f(p.X, p.Y))
                                               .ToArray();

                    // 4.1 运行本地检测
                    InspectionResult localInspectionResult = _imageInspector.Run(sourceMat, cornerPoints);

                    // ########## 最终修正点: 增强筛选逻辑 ##########
                    // 使用 StringComparison.OrdinalIgnoreCase 来忽略大小写，更加健壮。
                    // 只有被视觉库判定为不合格(NG)的结果才会被添加到缺陷列表。

                    // 筛选桥位缺陷：Result 属性为 "F"
                    var bridgeDefectsToAdd = localInspectionResult?.DetectedBridges
                        .Where(b => "F".Equals(b.Result, StringComparison.OrdinalIgnoreCase))
                        .ToList() ?? new List<BridgeResult>();

                    // 【修改】获取聚合后的宽度缺陷
                    var widthDefectsToAdd = localInspectionResult?.AggregatedWidthDefects ?? new List<AggregatedWidthDefect>();

                    // ########## 在这里添加新的处理逻辑 ##########
                    // 遍历所有宽度缺陷，计算并填充新增的属性
                    foreach (var defect in widthDefectsToAdd)
                    {
                        // 检查点集是否有效，防止后续操作出错
                        if (defect.DefectShapePoints != null && defect.DefectShapePoints.Length > 0)
                        {
                            // 2. 计算缺陷的物理长度（毫米）
                            double pixelLength = 0;
                            // 遍历点集，累加每两个相邻点之间的距离
                            for (int i = 0; i < defect.DefectShapePoints.Length - 1; i++)
                            {
                                // 使用OpenCvSharp的Point类内置的DistanceTo方法计算欧氏距离
                                pixelLength += defect.DefectShapePoints[i].DistanceTo(defect.DefectShapePoints[i + 1]);
                            }
                            // 将总像素长度乘以像素尺寸，得到物理长度
                            defect.LengthMm = pixelLength * _pixelSize;
                        }
                    }

                    // 筛选错位缺陷：Result 属性为 "F"
                    var componentDefectsToAdd = localInspectionResult?.DetectedComponents
                        .Where(c => "F".Equals(c.Result, StringComparison.OrdinalIgnoreCase))
                        .ToList() ?? new List<ComponentResult>();

                    // 【新增】筛选压痕缺陷：Result 属性为 "F"
                    var indentationDefectsToAdd = localInspectionResult?.DetectedIndentations
                        .Where(i => "F".Equals(i.Result, StringComparison.OrdinalIgnoreCase))
                        .ToList() ?? new List<IndentationResult>();

                    // 4.2 将Bitmap转换为线程安全的Bitmap对象用于UI显示
                    var displayImage = (Bitmap)localInspectionResult.AlignedImage.ToBitmap().Clone();


                    // 步骤 5: 切换到UI线程，进行纯粹的UI更新操作
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // 5.1 更新图像
                        // 首先释放旧的图像资源
                        if (MainImage is IDisposable disposableOldImage)
                        {
                            disposableOldImage.Dispose();
                        }

                        // 然后赋新值。PictureViewer会接收到这个新的Bitmap对象。
                        ShouldResetImageView = true;
                        MainImage = displayImage;
                        _regionImage = (Bitmap)displayImage.Clone();

                        // 5.2 修正表格绑定并更新
                        // 清空旧的缺陷列表
                        BridgeDefects.Clear();
                        WidthDefects.Clear();
                        MisalignmentDefects.Clear();
                        IndentationDefects.Clear(); // 【新增】清空压痕缺陷列表
                        Shapes.Clear();

                        // 添加筛选后的缺陷列表
                        foreach (var bridge in bridgeDefectsToAdd) BridgeDefects.Add(bridge);
                        foreach (var width in widthDefectsToAdd) WidthDefects.Add(width);
                        foreach (var component in componentDefectsToAdd) MisalignmentDefects.Add(component);
                        foreach (var indentation in indentationDefectsToAdd) IndentationDefects.Add(indentation); // 【新增】添加压痕缺陷

                        // 5.4 显示报告
                        string report = $"检测完成!\n" +
                                    $"服务端耗时: {metadata.ProcessTimeSeconds:F2} 秒\n" +
                                    $"客户端耗时: {localInspectionResult?.ProcessTimeSeconds ?? 0:F2} 秒\n" +
                                    $"------ 客户端检测结果 ------\n" +
                                    $"桥位缺陷: {bridgeDefectsToAdd.Count} 处\n" +
                                    $"宽度缺陷: {widthDefectsToAdd.Count} 段\n" +
                                    $"错位缺陷: {componentDefectsToAdd.Count} 处\n" + // 【修改】在末尾添加换行符
                                    $"压痕缺陷: {indentationDefectsToAdd.Count} 处"; // 【新增】在报告中显示压痕缺陷数量

                        MessageBoxService.ShowMessage(report, "检测报告");
                        LogManager.Instance.Info($"视觉检测流程全部完成。服务端消息: {metadata.Message}");
                    });
                }
            });
        }        /// <summary>
                 /// 检查文件是否为支持的图像格式
                 /// </summary>
                 /// <param name="filePath">文件路径</param>
                 /// <returns>如果是支持的格式返回true</returns>
        private bool IsSupportedImageFormat(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            return SupportedImageExtensions.Contains(extension);
        }

        /// <summary>
        /// 安全地加载图像文件 - 方案3（最安全）
        /// </summary>
        /// <param name="filePath">图像文件路径</param>
        /// <returns>加载的Bitmap对象，失败时返回null</returns>
        private Bitmap SafeLoadImage(string filePath)
        {
            if (!File.Exists(filePath))
            {
                LogManager.Instance.Error($"图像文件不存在：{filePath}");
                return null;
            }

            if (!IsSupportedImageFormat(filePath))
            {
                LogManager.Instance.Error($"不支持的图像格式：{filePath}");
                return null;
            }

            // 先将文件完全读取到内存
            byte[] imageBytes = File.ReadAllBytes(filePath);

            // 从内存流创建 Bitmap
            using var memoryStream = new MemoryStream(imageBytes);
            return new Bitmap(memoryStream);
        }

        #endregion

        #region 辅助方法 (这部分方法与MVVM框架无关，无需改动)

        private async Task LoadProduct(string name)
        {
            // ########## 第一部分：加载 .sol 方案文件 (与原来相同) ##########
            var solutionBasePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Solutions");
            var productPath = Path.Combine(solutionBasePath, name);
            var solPath = Path.Combine(productPath, "Demo.sol");

            if (!File.Exists(solPath))
            {
                MessageBoxService.ShowMessage("方案文件(.sol)不存在，请核对后重试");
                return;
            }

            var result = await GrpcClientService.Instance.LoadSolutionAsync(solPath);
            if (!result.Success)
            {
                MessageBoxService.ShowMessage($"方案加载失败: {result.Message}", "错误", MessageButton.OK, MessageIcon.Error);
                // 即使方案加载失败，也可能需要继续加载检测配置，所以不在此处返回
            }
            else
            {
                LogManager.Instance.Info($"方案 '{name}' 加载成功。");
            }

            // ########## 第二部分：加载图像检测库相关配置 (新逻辑) ##########

            // 释放上一个产品的检测库实例
            _imageInspector?.Dispose();
            _imageInspector = null;

            // 定义产品目录下的配置文件路径
            string configPath = Path.Combine(productPath, "config.json");
            string cadCsvPath = Path.Combine(productPath, "cad_data.csv");
            string cadTemplateImagePath = Path.Combine(productPath, "cad_template.jpg");
            _originImage = new Bitmap(cadTemplateImagePath);
            // 检查 config.json 是否存在，如果不存在则自动生成一个默认的
            if (!File.Exists(configPath))
            {
                LogManager.Instance.Warn($"在产品 '{name}' 目录中未找到 config.json，将自动生成默认配置。");
                // 创建一个带有默认值的配置对象
                var defaultConfig = new ImageInspectionConfig();
                // 设置JSON序列化选项，使其格式化输出（美观）
                var options = new JsonSerializerOptions { WriteIndented = true };
                // 将对象序列化为JSON字符串
                var jsonString = JsonSerializer.Serialize(defaultConfig, options);
                // 将JSON字符串写入文件
                await File.WriteAllTextAsync(configPath, jsonString);
                LogManager.Instance.Info($"已成功在路径 '{configPath}' 创建默认的 config.json。");
            }

            // 检查其他必要文件是否存在
            if (!File.Exists(cadCsvPath) || !File.Exists(cadTemplateImagePath))
            {
                var msg = $"初始化产品 '{name}' 失败：缺少 'cad_data.csv' 或 'cad_template.jpg' 文件。\n请确保它们位于产品目录下：\n{productPath}";
                LogManager.Instance.Error(msg);
                MessageBoxService.ShowMessage(msg, "配置错误", MessageButton.OK, MessageIcon.Error);
                return; // 缺少关键文件，无法继续初始化
            }

            // 读取并解析配置文件
            string configJsonString = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<ImageInspectionConfig>(configJsonString);
            if (config == null)
            {
                var msg = $"初始化产品 '{name}' 失败：无法解析配置文件 config.json。";
                LogManager.Instance.Error(msg);
                MessageBoxService.ShowMessage(msg, "配置错误", MessageButton.OK, MessageIcon.Error);
                return;
            }
            _currentConfig = config;
            // 更新像素尺寸
            _pixelSize = config.PixelSize;

            // 初始化图像检测库
            _imageInspector = new ImageInspectionLibrary();
            _imageInspector.Init(configPath, cadCsvPath, cadTemplateImagePath);
            LogManager.Instance.Info($"产品 '{name}' 的图像检测库初始化成功。");
        }


        private void InitializeCamera()
        {
            if (_cameraController == null) return;

            bool success = _cameraController.Initialize(); // 使用默认参数

            IsCameraInitialized = success;
            CameraStatusText = success ? "相机已就绪" : "相机初始化失败";

            if (!success)
            {
                MessageBoxService.ShowMessage($"相机初始化失败，请检查连接和驱动。", "错误", MessageButton.OK, MessageIcon.Error);
            }
            else
            {
                LogManager.Instance.Info("本地相机初始化成功。");
            }
        }
        #endregion

        #region 辅助方法 (这部分方法与MVVM框架无关，无需改动)

        // 新增：启动 gRPC 服务进程的方法

        private Process _grpcServerProcess; // 新增：用于持有 gRPC 服务进程的引用

        // 定义与服务端完全相同的事件名称
        private const string ShutdownEventName = "Global\\LaserCuttingGrpcServerShutdownEvent";

        private void StartGrpcServer()
        {
            const string serverExeName = "VmHelper.exe"; // 你的gRPC服务端exe文件名
            // 假设服务端exe放在主程序目录下的 "grpc_server" 文件夹中
            string serverPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "grpc_server", serverExeName);

            if (!File.Exists(serverPath))
            {
                MessageBoxService.ShowMessage($"无法找到gRPC服务程序: {serverPath}\n请确保它存在。", "错误", MessageButton.OK, MessageIcon.Error);
                return;
            }

            // 检查进程是否已在运行 (以防上次未正常关闭)
            var existingProcess = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(serverExeName)).FirstOrDefault();
            if (existingProcess != null)
            {
                // 如果已在运行，先尝试正常关闭它
                StopGrpcServer(existingProcess);
            }

            var startInfo = new ProcessStartInfo(serverPath)
            {
                // 以下设置为后台运行所必须
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = false
            };

            _grpcServerProcess = Process.Start(startInfo);
            LogManager.Instance.Info($"已启动 gRPC 服务进程，ID: {_grpcServerProcess.Id}");
        }

        // 新增：停止 gRPC 服务进程的方法
        private void StopGrpcServer()
        {
            StopGrpcServer(_grpcServerProcess); // 调用重载方法
        }



        // 新增：停止 gRPC 服务进程的重载方法
        private void StopGrpcServer(Process process)
        {
            if (process == null || process.HasExited)
            {
                return;
            }

            // 使用 EventWaitHandle 发送关闭信号
            if (EventWaitHandle.TryOpenExisting(ShutdownEventName, out EventWaitHandle shutdownEvent))
            {
                using (shutdownEvent)
                {
                    LogManager.Instance.Info($"正在向 gRPC 服务进程 (ID: {process.Id}) 发送关闭信号...");
                    shutdownEvent.Set(); // 触发事件
                }

                // 等待进程退出，设置一个超时时间（例如5秒）
                if (process.WaitForExit(5000))
                {
                    LogManager.Instance.Info("gRPC 服务进程已成功关闭。");
                }
                else
                {
                    LogManager.Instance.Warn("gRPC 服务进程在超时后仍未关闭，将强制终止。");
                    process.Kill(); // 如果无法正常关闭，则强制终止
                }
            }
            else
            {
                LogManager.Instance.Warn($"无法找到名为 '{ShutdownEventName}' 的关闭事件句柄，将直接终止进程。");
                process.Kill();
            }

            process.Dispose();
        }

        #endregion

        /// <summary>
        /// 打开参数设置对话框的命令
        /// </summary>
        [RelayCommand]
        private async Task OpenParamSetDialogAsync()
        {
            // 检查是否已选择产品
            if (string.IsNullOrEmpty(SelectedProduct))
            {
                MessageBoxService.ShowMessage("请先选择一个产品。", "提示", MessageButton.OK, MessageIcon.Information);
                return;
            }

            // 构建当前选中产品的配置文件路径
            var productPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Solutions", SelectedProduct);
            var configPath = Path.Combine(productPath, "config.json");

            // 创建对话框和其ViewModel
            var dialog = new ParamsSetDialog();
            var viewModel = new ParamsSetViewModel(configPath);
            dialog.DataContext = viewModel;
            dialog.Title = $"{SelectedProduct} - 参数设置"; // 设置窗口标题

            // 以模态方式显示对话框，代码会在此处等待直到对话框关闭
            dialog.ShowDialog();

            // 对话框关闭后，重新加载产品配置，以确保所有更改都已生效
            LogManager.Instance.Info("参数设置窗口已关闭，重新加载产品配置以应用更改...");
            await LoadProduct(SelectedProduct);
            LogManager.Instance.Info("产品配置已刷新。");
        }
    }
}
