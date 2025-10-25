using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using ImageSourceModuleCs;
using Legacy;
using System;
using System.Collections.Concurrent; // 引入 BlockingCollection 命名空间
using System.Drawing;
using System.IO;
// using System.Threading.Channels; // 不再需要这个
using System.Threading.Tasks;
using VM.Core;
using VM.PlatformSDKCS;

namespace VmHelper
{
    /// <summary>
    /// 实现了 legacy_vision.proto 文件中定义的 LegacyVisionService 服务。
    /// </summary>
    public class LegacyVisionServiceImpl : LegacyVisionService.LegacyVisionServiceBase, IDisposable
    {
        private CameraControllerServer _cameraController;
        private readonly string _outputImageDirectory;
        private bool _disposed = false;

        public LegacyVisionServiceImpl()
        {
            // 创建输出图像目录
            _outputImageDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OutputImages");
            if (!Directory.Exists(_outputImageDirectory))
            {
                Directory.CreateDirectory(_outputImageDirectory);
            }

            Console.WriteLine($"[服务端] 输出图像目录: {_outputImageDirectory}");
            Console.WriteLine("[服务端] LegacyVisionServiceImpl 已初始化");
        }

        #region 方案管理

        public override Task<LoadSolutionResponse> LoadSolution(LoadSolutionRequest request, ServerCallContext context)
        {
            Console.WriteLine($"[RPC] 收到 LoadSolution 请求, 准备加载方案: '{request.SolutionName}'");

            var path = request.SolutionName;
            if (!File.Exists(path))
            {
                var errorMessage = $"方案文件不存在: {path}";
                Console.WriteLine($"[错误] {errorMessage}");
                return Task.FromResult(new LoadSolutionResponse
                {
                    Success = false,
                    Message = errorMessage
                });
            }

            VmSolution.Load(path);
            Console.WriteLine($"[方案] '{request.SolutionName}' 加载成功。");

            var response = new LoadSolutionResponse
            {
                Success = true,
                Message = $"方案 '{request.SolutionName}' 已成功加载。"
            };

            return Task.FromResult(response);
        }

        #endregion

        #region 流式图像处理
        public override async Task ProcessImageFromPathStreamed(ProcessImageFromPathRequest request, IServerStreamWriter<StreamedProcessImageResponse> responseStream, ServerCallContext context)
        {
            Console.WriteLine($"[RPC-Stream] 收到 ProcessImageFromPathStreamed 请求: {request.ImagePath}");

            if (VmSolution.Instance == null)
            {
                var metadataMsg = new ProcessImageMetadata { Success = false, Message = "处理失败: 没有加载任何视觉方案。" };
                await responseStream.WriteAsync(new StreamedProcessImageResponse { Metadata = metadataMsg });
                return;
            }

            if (!File.Exists(request.ImagePath))
            {
                var metadataMsg = new ProcessImageMetadata { Success = false, Message = $"图像文件不存在: {request.ImagePath}" };
                await responseStream.WriteAsync(new StreamedProcessImageResponse { Metadata = metadataMsg });
                return;
            }

            Bitmap sourceImage;
            byte[] imageBytes = File.ReadAllBytes(request.ImagePath);
            using (var memoryStream = new MemoryStream(imageBytes))
            {
                sourceImage = new Bitmap(memoryStream);
            }
            using (sourceImage)
            {
                await RunVmProcedureAndStreamResponse(sourceImage, request.OperationId, responseStream, context, request.ImagePath);
            }
        }

        public override async Task CaptureAndProcessStreamed(CaptureAndProcessRequest request, IServerStreamWriter<StreamedProcessImageResponse> responseStream, ServerCallContext context)
        {
            Console.WriteLine($"[RPC-Stream] 收到 CaptureAndProcessStreamed 请求");

            if (VmSolution.Instance == null)
            {
                var metadataMsg = new ProcessImageMetadata { Success = false, Message = "处理失败: 没有加载任何视觉方案。" };
                await responseStream.WriteAsync(new StreamedProcessImageResponse { Metadata = metadataMsg });
                return;
            }
            if (_cameraController == null || !_cameraController.IsInitialized)
            {
                var metadataMsg = new ProcessImageMetadata { Success = false, Message = "相机未初始化" };
                await responseStream.WriteAsync(new StreamedProcessImageResponse { Metadata = metadataMsg });
                return;
            }

            var capturedImage = await _cameraController.CaptureImageAsync(request.CaptureTimeoutMs);
            if (capturedImage == null)
            {
                var metadataMsg = new ProcessImageMetadata { Success = false, Message = "图像采集失败或超时" };
                await responseStream.WriteAsync(new StreamedProcessImageResponse { Metadata = metadataMsg });
                return;
            }

            string sourceImagePath = null;
            if (request.SaveImage)
            {
                sourceImagePath = string.IsNullOrEmpty(request.SavePath)
                    ? Path.Combine(_outputImageDirectory, $"source_{request.OperationId}_{DateTime.Now:yyyyMMdd_HHmmss}.bmp")
                    : request.SavePath;
                capturedImage.Save(sourceImagePath, System.Drawing.Imaging.ImageFormat.Bmp);
            }

            using (capturedImage)
            {
                await RunVmProcedureAndStreamResponse(capturedImage, request.OperationId, responseStream, context, sourceImagePath);
            }
        }

        private async Task RunVmProcedureAndStreamResponse(Bitmap sourceImage, string operationId, IServerStreamWriter<StreamedProcessImageResponse> responseStream, ServerCallContext context, string sourceImagePath = null)
        {
            var startTime = DateTime.Now;

            var procedure = (VmProcedure)VmSolution.Instance["主流程"];
            var imageSourceTool = (ImageSourceModuleTool)VmSolution.Instance["主流程.图像源"];
            imageSourceTool.ModuParams.ImageSourceType = ImageSourceParam.ImageSourceTypeEnum.SDK;
            imageSourceTool.SetImageData(new ImageBaseData(sourceImage));
            procedure.Run();

            var metadata = new ProcessImageMetadata
            {
                Success = true,
                Message = "处理成功",
                OperationId = operationId,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                ProcessTimeSeconds = (DateTime.Now - startTime).TotalSeconds,
                SourceImagePath = sourceImagePath ?? ""
            };

            var pointNames = new[] { "左上", "右上", "右下", "左下" };
            foreach (var name in pointNames)
            {
                var outputPoints = procedure.ModuResult?.GetOutputPointArray(name);
                if (outputPoints == null || outputPoints.Count == 0) { metadata.Success = false; break; }
                var vmPoint = outputPoints[0];
                metadata.CornerPoints.Add(new Point2D { X = vmPoint.X, Y = vmPoint.Y });
            }

            var imageResult = procedure.ModuResult.GetOutputImageV2("图像");
            if (imageResult.ImageData == null)
            {
                metadata.Success = false;
                metadata.Message = "处理失败: VM流程未能生成输出图像。";
            }

            await responseStream.WriteAsync(new StreamedProcessImageResponse { Metadata = metadata });

            if (!metadata.Success)
            {
                Console.WriteLine($"[VM-Stream] 处理失败: {metadata.Message}");
                return;
            }

            using (var resultImage = imageResult.ToBitmap())
            using (var memoryStream = new MemoryStream())
            {
                resultImage.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                var imageBytes = memoryStream.ToArray();
                const int chunkSize = 1 * 1024 * 1024;
                int bytesSent = 0;
                while (bytesSent < imageBytes.Length)
                {
                    int lengthToSend = Math.Min(chunkSize, imageBytes.Length - bytesSent);
                    var chunkData = new byte[lengthToSend];
                    Array.Copy(imageBytes, bytesSent, chunkData, 0, lengthToSend);
                    var imageChunk = new ImageChunk { Data = ByteString.CopyFrom(chunkData) };
                    await responseStream.WriteAsync(new StreamedProcessImageResponse { Chunk = imageChunk });
                    bytesSent += lengthToSend;
                }
            }

            Console.WriteLine($"[VM-Stream] 图像数据流式传输完成。");
        }

        public override async Task StartGrabbingAndProcessingStreamed(StartGrabbingStreamRequest request, IServerStreamWriter<StreamedProcessImageResponse> responseStream, ServerCallContext context)
        {
            await ProcessTriggeredImagesStreamed(request, responseStream, context);
        }

        /// <summary>
        /// 【最终修正版】使用内置的 BlockingCollection 解决依赖冲突问题。
        /// </summary>
        public override async Task ProcessTriggeredImagesStreamed(StartGrabbingStreamRequest request, IServerStreamWriter<StreamedProcessImageResponse> responseStream, ServerCallContext context)
        {
            Console.WriteLine($"[RPC-Stream] 收到 ProcessTriggeredImagesStreamed 请求, OperationId: {request.OperationId}");

            if (VmSolution.Instance == null)
            {
                var metadataMsg = new ProcessImageMetadata { Success = false, Message = "处理失败: 没有加载任何视觉方案。" };
                await responseStream.WriteAsync(new StreamedProcessImageResponse { Metadata = metadataMsg });
                return;
            }
            if (_cameraController == null || !_cameraController.IsInitialized)
            {
                var metadataMsg = new ProcessImageMetadata { Success = false, Message = "相机未初始化" };
                await responseStream.WriteAsync(new StreamedProcessImageResponse { Metadata = metadataMsg });
                return;
            }

            // 使用 BlockingCollection 作为线程安全的队列，它内置于 .NET Framework，没有外部依赖
            using (var imageQueue = new BlockingCollection<Bitmap>())
            {
                // 定义相机回调（生产者）
                Action<Bitmap> imageReceivedHandler = (image) =>
                {
                    var imageClone = (Bitmap)image.Clone();
                    // 将克隆的图像添加到队列中
                    if (!imageQueue.IsAddingCompleted)
                    {
                        imageQueue.Add(imageClone);
                    }
                    else
                    {
                        // 如果队列已经关闭，则释放图像
                        imageClone.Dispose();
                    }
                };

                // 注册回调
                _cameraController.ImageReceived += imageReceivedHandler;
                Console.WriteLine("[相机] 已订阅 ImageReceived 事件，等待外部硬件触发...");

                // 注册客户端断开连接时的清理逻辑
                context.CancellationToken.Register(() =>
                {
                    Console.WriteLine($"[RPC-Stream] 客户端已断开连接 (OperationId: {request.OperationId})，正在停止监听...");
                    _cameraController.ImageReceived -= imageReceivedHandler;
                    _cameraController.StopGrab();
                    imageQueue.CompleteAdding(); // 告知队列不会再有新项目，这将解除消费者的阻塞
                    Console.WriteLine("[相机] 已取消订阅并停止采集。");
                });

                // 启动相机
                if (!_cameraController.IsGrabbing)
                {
                    _cameraController.StartGrab(true);
                }

                // 在后台线程中运行消费者循环，这样它就不会阻塞当前的 gRPC 请求线程
                var consumerTask = Task.Run(async () =>
                {
                    // GetConsumingEnumerable 会阻塞直到有新项目或集合被标记为完成
                    // CancellationToken 会在客户端断开时抛出异常，从而终止循环
                    foreach (var receivedImage in imageQueue.GetConsumingEnumerable(context.CancellationToken))
                    {
                        using (receivedImage)
                        {
                            Console.WriteLine($"[gRPC线程] 从队列中获取到图像，开始处理, OperationId: {request.OperationId}");
                            // 在正确的 gRPC 线程上下文中调用处理和流式发送方法
                            await RunVmProcedureAndStreamResponse(receivedImage, request.OperationId, responseStream, context);
                        }
                    }
                });

                // 等待消费者任务完成（通常是因为 CancellationToken 被取消）
                await consumerTask;
            }
        }
        #endregion

        #region 相机控制

        public override Task<CameraOperationResponse> InitializeCamera(InitializeCameraRequest request, ServerCallContext context)
        {
            Console.WriteLine($"[RPC] 收到 InitializeCamera 请求");

            if (_cameraController != null)
            {
                Console.WriteLine("[相机] 释放现有相机控制器");
                _cameraController.Dispose();
                _cameraController = null;
            }

            _cameraController = new CameraControllerServer();
            _cameraController.LogMessage += (msg) => Console.WriteLine(msg);

            bool success = _cameraController.Initialize(
                request.ConfigFilePath,
                request.CameraType,
                request.Baudrate);

            return Task.FromResult(new CameraOperationResponse
            {
                Success = success,
                Message = success ? "相机初始化成功" : "相机初始化失败",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                OperationId = request.OperationId
            });
        }

        public override Task<CameraOperationResponse> SetCameraParameters(SetCameraParametersRequest request, ServerCallContext context)
        {
            Console.WriteLine($"[RPC] 收到 SetCameraParameters 请求");

            if (_cameraController == null || !_cameraController.IsInitialized)
            {
                return Task.FromResult(new CameraOperationResponse
                {
                    Success = false,
                    Message = "相机未初始化",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                    OperationId = request.OperationId
                });
            }

            bool success = _cameraController.SetParameters(
                redLight: request.HasRedLightIntensity ? request.RedLightIntensity : (int?)null,
                greenLight: request.HasGreenLightIntensity ? request.GreenLightIntensity : (int?)null,
                blueLight: request.HasBlueLightIntensity ? request.BlueLightIntensity : (int?)null,
                frameHeight: request.HasFrameHeight ? request.FrameHeight : (int?)null,
                lineFrequency: request.HasLineFrequency ? request.LineFrequency : (int?)null,
                lineSyncSource: request.HasLineSyncSource ? request.LineSyncSource : (int?)null,
                correctionEnabled: request.HasCorrectionEnabled ? request.CorrectionEnabled : (bool?)null,
                multiplier: request.HasMultiplier ? request.Multiplier : (float?)null,
                shaftEncoderDirection: request.HasShaftEncoderDirection ? request.ShaftEncoderDirection : (int?)null,
                externalLineTrigger: request.HasExternalLineTrigger ? request.ExternalLineTrigger : (int?)null
            );

            return Task.FromResult(new CameraOperationResponse
            {
                Success = success,
                Message = success ? "相机参数设置成功" : "相机参数设置失败",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                OperationId = request.OperationId
            });
        }

        public override Task<CameraOperationResponse> StartCameraGrab(StartCameraGrabRequest request, ServerCallContext context)
        {
            Console.WriteLine($"[RPC] 收到 StartCameraGrab 请求");

            if (_cameraController == null || !_cameraController.IsInitialized)
            {
                return Task.FromResult(new CameraOperationResponse
                {
                    Success = false,
                    Message = "相机未初始化",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                    OperationId = request.OperationId
                });
            }

            bool success = _cameraController.StartGrab(request.ContinuousMode);

            return Task.FromResult(new CameraOperationResponse
            {
                Success = success,
                Message = success ? "开始采集" : "开始采集失败",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                OperationId = request.OperationId
            });
        }

        public override Task<CameraOperationResponse> StopCameraGrab(StopCameraGrabRequest request, ServerCallContext context)
        {
            Console.WriteLine($"[RPC] 收到 StopCameraGrab 请求");

            if (_cameraController == null)
            {
                return Task.FromResult(new CameraOperationResponse
                {
                    Success = false,
                    Message = "相机控制器未初始化",
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                    OperationId = request.OperationId
                });
            }

            bool success = _cameraController.StopGrab();

            return Task.FromResult(new CameraOperationResponse
            {
                Success = success,
                Message = success ? "停止采集" : "停止采集失败",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                OperationId = request.OperationId
            });
        }

        public override Task<GetCameraStatusResponse> GetCameraStatus(GetCameraStatusRequest request, ServerCallContext context)
        {
            Console.WriteLine($"[RPC] 收到 GetCameraStatus 请求");

            if (_cameraController == null)
            {
                return Task.FromResult(new GetCameraStatusResponse
                {
                    Success = false,
                    Message = "相机控制器未初始化",
                    IsInitialized = false,
                    IsGrabbing = false
                });
            }

            var cameraInfo = new
            {
                FrameWidth = _cameraController.FrameWidth,
                FrameHeight = _cameraController.FrameHeight,
                DPI = _cameraController.DPI,
                RedLight = _cameraController.RedLightIntensity,
                GreenLight = _cameraController.GreenLightIntensity,
                BlueLight = _cameraController.BlueLightIntensity
            };

            return Task.FromResult(new GetCameraStatusResponse
            {
                Success = true,
                Message = "获取状态成功",
                IsInitialized = _cameraController.IsInitialized,
                IsGrabbing = _cameraController.IsGrabbing,
                FrameWidth = _cameraController.FrameWidth,
                FrameHeight = _cameraController.FrameHeight,
                Dpi = _cameraController.DPI,
                RedLightIntensity = _cameraController.RedLightIntensity,
                GreenLightIntensity = _cameraController.GreenLightIntensity,
                BlueLightIntensity = _cameraController.BlueLightIntensity,
                CameraInfo = System.Text.Json.JsonSerializer.Serialize(cameraInfo)
            });
        }

        public override Task<CameraOperationResponse> ReleaseCamera(ReleaseCameraRequest request, ServerCallContext context)
        {
            Console.WriteLine($"[RPC] 收到 ReleaseCamera 请求");

            _cameraController?.Dispose();
            _cameraController = null;

            return Task.FromResult(new CameraOperationResponse
            {
                Success = true,
                Message = "相机资源已释放",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                OperationId = request.OperationId
            });
        }

        #endregion

        #region 核心处理逻辑

        private ProcessImageResponse RunVmProcedure(Bitmap sourceImage, string operationId, string sourceImagePath = null)
        {
            var startTime = DateTime.Now;
            Console.WriteLine($"[VM] 开始处理图像: {sourceImage.Width}x{sourceImage.Height}");

            var procedure = (VmProcedure)VmSolution.Instance["主流程"];
            var imageSourceTool = (ImageSourceModuleTool)VmSolution.Instance["主流程.图像源"];
            imageSourceTool.ModuParams.ImageSourceType = ImageSourceParam.ImageSourceTypeEnum.SDK;
            imageSourceTool.SetImageData(new ImageBaseData(sourceImage));
            procedure.Run();
            Console.WriteLine("[VM] 主流程运行完毕。");

            var processingTime = (DateTime.Now - startTime).TotalSeconds;

            var response = new ProcessImageResponse
            {
                Success = true,
                Message = "处理成功",
                OperationId = operationId,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                ProcessTimeSeconds = processingTime,
                SourceImagePath = sourceImagePath ?? ""
            };

            var pointNames = new[] { "左上", "右上", "右下", "左下" };
            foreach (var name in pointNames)
            {
                var outputPoints = procedure.ModuResult?.GetOutputPointArray(name);
                if (outputPoints == null || outputPoints.Count == 0)
                {
                    Console.WriteLine($"[警告] 未获取到有效的角点数据: 输出点 '{name}' 为 null 或空数组。");
                    response.CornerPoints.Add(new Point2D { X = 0, Y = 0 });
                    response.Success = false;
                    continue;
                }

                var vmPoint = outputPoints[0];
                response.CornerPoints.Add(new Point2D { X = vmPoint.X, Y = vmPoint.Y });
                Console.WriteLine($"[VM] 成功提取角点 '{name}': X={vmPoint.X}, Y={vmPoint.Y}");
            }

            Console.WriteLine($"[VM] 共提取到 {response.CornerPoints.Count} 个角点坐标。");

            var imageResult = procedure.ModuResult.GetOutputImageV2("图像");
            if (imageResult.ImageData != null)
            {
                using (var resultImage = imageResult.ToBitmap())
                {
                    if (resultImage != null)
                    {
                        using (var memoryStream = new MemoryStream())
                        {
                            resultImage.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Bmp);
                            response.OutputImageData = ByteString.CopyFrom(memoryStream.ToArray());
                            Console.WriteLine($"[VM] 输出图像已编码为 byte stream ({response.OutputImageData.Length} bytes) 并附加到响应中。");
                        }
                    }
                }
            }
            else
            {
                response.Success = false;
                response.Message = "处理失败：VM流程未能生成输出图像。";
            }
            return response;
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cameraController?.Dispose();
                    _cameraController = null;
                    Console.WriteLine("[服务端] LegacyVisionServiceImpl 资源已释放");
                }
                _disposed = true;
            }
        }

        ~LegacyVisionServiceImpl()
        {
            Dispose(false);
        }

        #endregion
    }
}
