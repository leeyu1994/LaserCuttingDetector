using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using LaserCuttingDetector.Commons.LaserCuttingDetector.Commons;
using Legacy;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace LaserCuttingDetector.Commons
{
    namespace LaserCuttingDetector.Commons
    {
        // 用于封装流式传输结果的辅助类
        public class ProcessStreamedResult
        {
            public ProcessImageMetadata Metadata { get; set; }
            public Bitmap ResultImage { get; set; }
        }
    }
    /// <summary>
    /// 一个单例服务，用于封装所有与 gRPC 后台服务的通信。
    /// 现在包含完整的相机控制功能。
    /// </summary>
    public class GrpcClientService : IDisposable
    {
        private static readonly Lazy<GrpcClientService> _instance = new(() => new GrpcClientService());

        public static GrpcClientService Instance => _instance.Value;

        private readonly GrpcChannel _channel;
        private readonly LegacyVisionService.LegacyVisionServiceClient _client;

        // 相机状态事件
        public event Action<string> CameraLogReceived;
        public event Action<bool> CameraStatusChanged;

        private GrpcClientService()
        {
            var channelOptions = new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 200 * 1024 * 1024, // 200 MB
                MaxSendMessageSize = 200 * 1024 * 1024,    // 200 MB
            };
            _channel = GrpcChannel.ForAddress("http://localhost:50051", channelOptions);
            _client = new LegacyVisionService.LegacyVisionServiceClient(_channel);
        }

        #region 方案管理

        /// <summary>
        /// 调用服务端加载指定的视觉方案。
        /// </summary>
        public async Task<LoadSolutionResponse> LoadSolutionAsync(string solutionName)
        {
            var request = new LoadSolutionRequest { SolutionName = solutionName };
            var response = await _client.LoadSolutionAsync(request);

            OnCameraLogReceived($"方案加载结果: {response.Message}");
            return response;
        }

        #endregion

        #region 流式图像处理
        /// <summary>
        /// 流式处理本地图像文件
        /// </summary>
        public async Task<ProcessStreamedResult> ProcessImageFromPathStreamedAsync(string imagePath, string operationId = null)
        {
            var request = new ProcessImageFromPathRequest
            {
                ImagePath = imagePath,
                OperationId = operationId ?? Guid.NewGuid().ToString()
            };
            OnCameraLogReceived($"发送流式图像处理请求: {imagePath}");
            var call = _client.ProcessImageFromPathStreamed(request);
            return await ReassembleImageFromStream(call);
        }

        /// <summary>
        /// 流式采集并处理图像
        /// </summary>
        public async Task<ProcessStreamedResult> CaptureAndProcessStreamedAsync(bool saveImage = true, string savePath = null, int captureTimeoutMs = 10000)
        {
            var request = new CaptureAndProcessRequest
            {
                SaveImage = saveImage,
                SavePath = savePath ?? "",
                CaptureTimeoutMs = captureTimeoutMs,
                OperationId = Guid.NewGuid().ToString()
            };
            OnCameraLogReceived("开始流式采集并处理图像...");
            var call = _client.CaptureAndProcessStreamed(request);
            return await ReassembleImageFromStream(call);
        }

        /// <summary>
        /// 从服务器流中重组图像的核心逻辑
        /// </summary>
        private async Task<ProcessStreamedResult> ReassembleImageFromStream(Grpc.Core.AsyncServerStreamingCall<StreamedProcessImageResponse> call)
        {
            var result = new ProcessStreamedResult();

            // 使用 using 确保内存流被正确释放
            using (var memoryStream = new MemoryStream())
            {
                // 使用 IAsyncEnumerable 迭代流
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (response.ContentCase == StreamedProcessImageResponse.ContentOneofCase.Metadata)
                    {
                        // 这是第一条消息，包含元数据
                        result.Metadata = response.Metadata;

                        // 如果元数据表示失败，我们可能就不需要接收后续的图像块了
                        if (!result.Metadata.Success)
                        {
                            OnCameraLogReceived($"流式处理失败: {result.Metadata.Message}");
                            return result; // 提前返回
                        }
                    }
                    else if (response.ContentCase == StreamedProcessImageResponse.ContentOneofCase.Chunk)
                    {
                        // 这是图像数据块，写入内存流
                        memoryStream.Write(response.Chunk.Data.ToByteArray());
                    }
                }

                // 所有数据块接收完毕后，从内存流创建Bitmap
                if (memoryStream.Length > 0)
                {
                    memoryStream.Seek(0, SeekOrigin.Begin); // 重置流的位置
                    using var tempBitmap = new Bitmap(memoryStream);
                    result.ResultImage = new Bitmap(tempBitmap);
                }
            }

            OnCameraLogReceived($"图像流接收并重组完成。");
            return result;
        }


        #endregion
        #region 图像转换辅助方法

        /// <summary>
        /// 高效且线程安全地将 System.Drawing.Bitmap 转换为 System.Windows.Media.Imaging.BitmapSource
        /// </summary>
        /// <param name="bitmap">要转换的Bitmap对象</param>
        /// <returns>一个已冻结的、可跨线程使用的BitmapSource</returns>
        public static BitmapSource ConvertBitmapToBitmapSource(Bitmap bitmap)
        {
            // 如果输入为空，则返回null
            if (bitmap == null) return null;

            // 获取位图数据
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                bitmap.PixelFormat);

            // 创建BitmapSource
            var bitmapSource = BitmapSource.Create(
                bitmapData.Width,
                bitmapData.Height,
                bitmap.HorizontalResolution,
                bitmap.VerticalResolution,
                ConvertPixelFormat(bitmap.PixelFormat), // 转换像素格式
                null, // 无调色板
                bitmapData.Scan0, // 像素数据的内存地址
                bitmapData.Stride * bitmapData.Height, // 图像总大小
                bitmapData.Stride); // 每行字节数

            // 解锁位图数据
            bitmap.UnlockBits(bitmapData);

            // 【关键】冻结BitmapSource，使其成为只读，从而可以在任何线程上安全访问
            bitmapSource.Freeze();

            return bitmapSource;
        }

        /// <summary>
        /// 将 System.Drawing.Imaging.PixelFormat 转换为 System.Windows.Media.PixelFormat
        /// </summary>
        private static System.Windows.Media.PixelFormat ConvertPixelFormat(System.Drawing.Imaging.PixelFormat sourceFormat)
        {
            switch (sourceFormat)
            {
                case System.Drawing.Imaging.PixelFormat.Format24bppRgb:
                    return System.Windows.Media.PixelFormats.Bgr24;
                case System.Drawing.Imaging.PixelFormat.Format32bppRgb:
                    return System.Windows.Media.PixelFormats.Bgr32;
                case System.Drawing.Imaging.PixelFormat.Format32bppArgb:
                    return System.Windows.Media.PixelFormats.Bgra32;
                case System.Drawing.Imaging.PixelFormat.Format8bppIndexed:
                    return System.Windows.Media.PixelFormats.Gray8;
                // 可以根据需要添加更多格式的转换
                default:
                    // 默认使用最常见的格式之一
                    return System.Windows.Media.PixelFormats.Bgr24;
            }
        }
        #endregion
        #region 相机控制

        /// <summary>
        /// 初始化相机
        /// </summary>
        public async Task<CameraOperationResponse> InitializeCameraAsync(
            string configFilePath = "./ZKCP658.vlcf",
            int cameraType = 4, // ZKCP_658
            int baudrate = 115200)
        {
            var request = new InitializeCameraRequest
            {
                ConfigFilePath = configFilePath,
                CameraType = cameraType,
                Baudrate = baudrate,
                OperationId = Guid.NewGuid().ToString()
            };

            var response = await _client.InitializeCameraAsync(request);

            OnCameraLogReceived($"相机初始化: {response.Message}");
            OnCameraStatusChanged(response.Success);

            return response;
        }

        /// <summary>
        /// 设置相机参数
        /// </summary>
        public async Task<CameraOperationResponse> SetCameraParametersAsync(
            int? redLight = null,
            int? greenLight = null,
            int? blueLight = null,
            int? frameHeight = null,
            int? lineFrequency = null,
            int? lineSyncSource = null,
            bool? correctionEnabled = null,
            float? multiplier = null,
            int? shaftEncoderDirection = null,
            int? externalLineTrigger = null)
        {
            var request = new SetCameraParametersRequest
            {
                OperationId = Guid.NewGuid().ToString()
            };

            // 只设置非空的参数
            if (redLight.HasValue) request.RedLightIntensity = redLight.Value;
            if (greenLight.HasValue) request.GreenLightIntensity = greenLight.Value;
            if (blueLight.HasValue) request.BlueLightIntensity = blueLight.Value;
            if (frameHeight.HasValue) request.FrameHeight = frameHeight.Value;
            if (lineFrequency.HasValue) request.LineFrequency = lineFrequency.Value;
            if (lineSyncSource.HasValue) request.LineSyncSource = lineSyncSource.Value;
            if (correctionEnabled.HasValue) request.CorrectionEnabled = correctionEnabled.Value;
            if (multiplier.HasValue) request.Multiplier = multiplier.Value;
            if (shaftEncoderDirection.HasValue) request.ShaftEncoderDirection = shaftEncoderDirection.Value;
            if (externalLineTrigger.HasValue) request.ExternalLineTrigger = externalLineTrigger.Value;

            var response = await _client.SetCameraParametersAsync(request);

            OnCameraLogReceived($"相机参数设置: {response.Message}");

            return response;
        }

        /// <summary>
        /// 开始相机采集
        /// </summary>
        public async Task<CameraOperationResponse> StartCameraGrabAsync(bool continuousMode = true, int durationMs = 0)
        {
            var request = new StartCameraGrabRequest
            {
                ContinuousMode = continuousMode,
                DurationMs = durationMs,
                OperationId = Guid.NewGuid().ToString()
            };

            var response = await _client.StartCameraGrabAsync(request);

            OnCameraLogReceived($"开始采集: {response.Message}");

            return response;
        }

        /// <summary>
        /// 停止相机采集
        /// </summary>
        public async Task<CameraOperationResponse> StopCameraGrabAsync()
        {
            var request = new StopCameraGrabRequest
            {
                OperationId = Guid.NewGuid().ToString()
            };

            var response = await _client.StopCameraGrabAsync(request);

            OnCameraLogReceived($"停止采集: {response.Message}");

            return response;
        }


        /// <summary>
        /// 获取相机状态
        /// </summary>
        public async Task<GetCameraStatusResponse> GetCameraStatusAsync()
        {
            var request = new GetCameraStatusRequest
            {
                OperationId = Guid.NewGuid().ToString()
            };

            var response = await _client.GetCameraStatusAsync(request);

            return response;
        }

        /// <summary>
        /// 释放相机资源
        /// </summary>
        public async Task<CameraOperationResponse> ReleaseCameraAsync()
        {
            var request = new ReleaseCameraRequest
            {
                OperationId = Guid.NewGuid().ToString()
            };

            var response = await _client.ReleaseCameraAsync(request);

            OnCameraLogReceived($"相机资源释放: {response.Message}");
            OnCameraStatusChanged(false);

            return response;
        }

        #endregion


        #region 事件触发

        private void OnCameraLogReceived(string message)
        {
            CameraLogReceived?.Invoke(message);
        }

        private void OnCameraStatusChanged(bool isActive)
        {
            CameraStatusChanged?.Invoke(isActive);
        }

        #endregion

        public void Dispose()
        {
            _channel?.Dispose();
        }

        #region 辅助方法

        /// <summary>
        /// 从服务器返回的图像路径加载结果图像
        /// </summary>
        public static Bitmap LoadImageFromPath(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return null;

            // 安全地从文件加载图像
            using var fileStream = new FileStream(imagePath, FileMode.Open, FileAccess.Read);
            return new Bitmap(fileStream);
        }

        // --- 旧方法保持不变 ---
        public async Task StartGrabbingAndProcessingStreamedAsync(Action<ProcessStreamedResult> onResultReceived, CancellationToken cancellationToken)
        {
            // 为保持兼容，直接调用新的实现
            await StartTriggeredImageProcessingStreamAsync(onResultReceived, cancellationToken);
        }

        /// <summary>
        /// 【新增】启动对硬件触发图像的处理流。客户端调用此方法后，服务端将等待相机硬件触发。
        /// 每触发一次，服务端就会处理图像并将结果通过流发送回来，由 onResultReceived 回调进行处理。
        /// </summary>
        /// <param name="onResultReceived">每当一个完整的结果（元数据+图像）从服务端传来时触发的回调函数。</param>
        /// <param name="cancellationToken">用于取消监听的令牌。当此令牌被取消时，客户端将停止接收，服务端也将停止相机采集。</param>
        public async Task StartTriggeredImageProcessingStreamAsync(Action<ProcessStreamedResult> onResultReceived, CancellationToken cancellationToken)
        {
            var request = new StartGrabbingStreamRequest { OperationId = Guid.NewGuid().ToString() };
            OnCameraLogReceived("正在请求服务端开启硬件触发监听...");

            // 调用新的、语义更清晰的RPC方法
            var call = _client.ProcessTriggeredImagesStreamed(request, cancellationToken: cancellationToken);

            ProcessImageMetadata currentMetadata = null;
            // 使用 using 确保内存流在任何情况下都能被正确释放
            using var currentImageStream = new MemoryStream();

            // 异步迭代服务器发送过来的数据流
            await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
            {
                if (response.ContentCase == StreamedProcessImageResponse.ContentOneofCase.Metadata)
                {
                    // 收到元数据消息
                    // 如果我们已经有了一个处理中的图像（即 currentMetadata 不为 null），
                    // 这意味着上一个图像的数据块已经全部接收完毕。
                    if (currentMetadata != null && currentImageStream.Length > 0)
                    {
                        // 组装上一个图像的结果
                        currentImageStream.Seek(0, SeekOrigin.Begin);
                        using var tempBitmap = new Bitmap(currentImageStream);
                        var completedResult = new ProcessStreamedResult
                        {
                            Metadata = currentMetadata,
                            ResultImage = new Bitmap(tempBitmap)
                        };
                        // 通过回调通知调用方
                        onResultReceived(completedResult);
                    }

                    // 开始处理新的图像：保存新的元数据，并重置内存流
                    currentMetadata = response.Metadata;
                    currentImageStream.SetLength(0); // 清空流，准备接收新图像的块
                    currentImageStream.Seek(0, SeekOrigin.Begin);

                    // 如果元数据指示失败，直接通过回调通知并重置状态
                    if (!currentMetadata.Success)
                    {
                        onResultReceived(new ProcessStreamedResult { Metadata = currentMetadata });
                        currentMetadata = null; // 重置
                    }
                }
                else if (response.ContentCase == StreamedProcessImageResponse.ContentOneofCase.Chunk)
                {
                    // 收到图像块消息，将其写入内存流
                    if (currentMetadata != null) // 确保在有元数据上下文时才接收图像块
                    {
                        currentImageStream.Write(response.Chunk.Data.ToByteArray());
                    }
                }
            }

            // 循环结束后，处理最后一个可能存在的图像
            if (currentMetadata != null && currentImageStream.Length > 0)
            {
                currentImageStream.Seek(0, SeekOrigin.Begin);
                using var tempBitmap = new Bitmap(currentImageStream);
                var lastResult = new ProcessStreamedResult
                {
                    Metadata = currentMetadata,
                    ResultImage = new Bitmap(tempBitmap)
                };
                onResultReceived(lastResult);
            }
        }


        /// <summary>
        /// 【新增】将 gRPC 返回的 ByteString 转换为 Bitmap 对象
        /// </summary>
        public static Bitmap ConvertByteStringToBitmap(ByteString byteString)
        {
            // 检查输入数据是否有效
            if (byteString == null || byteString.IsEmpty)
            {
                return null;
            }

            // 从 ByteString 创建内存流
            using var memoryStream = new MemoryStream(byteString.ToByteArray());
            // 从内存流创建并返回一个新的 Bitmap 对象
            return new Bitmap(memoryStream);
        }

        #endregion
    }

}
