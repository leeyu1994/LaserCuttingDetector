// 文件: VmHelper/LegacyVisionServiceImpl.cs

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using ImageSourceModuleCs;
using Legacy;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using VM.Core;
using VM.PlatformSDKCS;

namespace VmHelper
{
    /// <summary>
    /// 实现了 legacy_vision.proto 文件中定义的 LegacyVisionService 服务。
    /// 服务端现在不包含任何相机控制逻辑。
    /// </summary>
    public class LegacyVisionServiceImpl : LegacyVisionService.LegacyVisionServiceBase, IDisposable
    {
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

            return Task.FromResult(new LoadSolutionResponse
            {
                Success = true,
                Message = $"方案 '{request.SolutionName}' 已成功加载。"
            });
        }
        #endregion

        #region 流式图像处理

        //用于本地文件测试
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

            // 使用 using 语句确保资源被释放
            using (var sourceImage = new Bitmap(request.ImagePath))
            {
                await RunVmProcedureAndStreamResponse(sourceImage, request.OperationId, responseStream, context, request.ImagePath);
            }
        }

        // 实现新的 ProcessImageFromBytesStreamed RPC
        public override async Task ProcessImageFromBytesStreamed(ProcessImageFromBytesRequest request, IServerStreamWriter<StreamedProcessImageResponse> responseStream, ServerCallContext context)
        {
            Console.WriteLine($"[RPC-Stream] 收到 ProcessImageFromBytesStreamed 请求, OperationId: {request.OperationId}");

            if (VmSolution.Instance == null)
            {
                var metadataMsg = new ProcessImageMetadata { Success = false, Message = "处理失败: 没有加载任何视觉方案。" };
                await responseStream.WriteAsync(new StreamedProcessImageResponse { Metadata = metadataMsg });
                return;
            }

            if (request.ImageData == null || request.ImageData.IsEmpty)
            {
                var metadataMsg = new ProcessImageMetadata { Success = false, Message = "处理失败: 接收到的图像数据为空。" };
                await responseStream.WriteAsync(new StreamedProcessImageResponse { Metadata = metadataMsg });
                return;
            }

            // 从客户端发送的字节数据创建 Bitmap
            // 使用 using 语句确保资源被释放
            using (var memoryStream = new MemoryStream(request.ImageData.ToByteArray()))
            using (var sourceImage = new Bitmap(memoryStream))
            {
                // 复用现有的处理和流式响应逻辑
                await RunVmProcedureAndStreamResponse(sourceImage, request.OperationId, responseStream, context);
            }
        }

        // 核心处理逻辑，保持不变，供以上两个方法调用
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
                // 建议使用 PNG，因为它通常比 BMP 小，传输更快
                resultImage.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
                var imageBytes = memoryStream.ToArray();
                const int chunkSize = 1 * 1024 * 1024; // 1 MB
                int bytesSent = 0;
                while (bytesSent < imageBytes.Length)
                {
                    int lengthToSend = Math.Min(chunkSize, imageBytes.Length - bytesSent);
                    var chunkData = ByteString.CopyFrom(imageBytes, bytesSent, lengthToSend);
                    var imageChunk = new ImageChunk { Data = chunkData };
                    await responseStream.WriteAsync(new StreamedProcessImageResponse { Chunk = imageChunk });
                    bytesSent += lengthToSend;
                }
            }
            Console.WriteLine($"[VM-Stream] 图像数据流式传输完成。");
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
                    // 没有需要释放的托管资源了
                }
                _disposed = true;
                Console.WriteLine("[服务端] LegacyVisionServiceImpl 资源已释放");
            }
        }

        ~LegacyVisionServiceImpl()
        {
            Dispose(false);
        }
        #endregion
    }
}
