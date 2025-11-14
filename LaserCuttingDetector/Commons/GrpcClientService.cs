using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Legacy;
using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
namespace LaserCuttingDetector.Commons
{
    // 用于封装流式传输结果的辅助类
    public class ProcessStreamedResult
    {
        public ProcessImageMetadata Metadata { get; set; }
        public Bitmap ResultImage { get; set; }
    }
    public class GrpcClientService : IDisposable
    {
        private static readonly Lazy<GrpcClientService> _instance = new(() => new GrpcClientService());
        public static GrpcClientService Instance => _instance.Value;

        private readonly GrpcChannel _channel;
        private readonly LegacyVisionService.LegacyVisionServiceClient _client;

        // 移除所有相机相关的事件和方法

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
        public async Task<LoadSolutionResponse> LoadSolutionAsync(string solutionName)
        {
            var request = new LoadSolutionRequest { SolutionName = solutionName };
            return await _client.LoadSolutionAsync(request);
        }
        #endregion

        #region 流式图像处理

        // 此方法不变，用于本地文件测试
        public async Task<ProcessStreamedResult> ProcessImageFromPathStreamedAsync(string imagePath, string operationId = null)
        {
            var request = new ProcessImageFromPathRequest
            {
                ImagePath = imagePath,
                OperationId = operationId ?? Guid.NewGuid().ToString()
            };
            var call = _client.ProcessImageFromPathStreamed(request);
            return await ReassembleImageFromStream(call);
        }

        // 【新增】调用新 RPC 的方法
        public async Task<ProcessStreamedResult> ProcessImageFromBytesStreamedAsync(byte[] imageData, string operationId = null)
        {
            var request = new ProcessImageFromBytesRequest
            {
                ImageData = ByteString.CopyFrom(imageData),
                OperationId = operationId ?? Guid.NewGuid().ToString()
            };
            var call = _client.ProcessImageFromBytesStreamed(request);
            return await ReassembleImageFromStream(call);
        }

        // 核心的图像重组逻辑，保持不变
        private async Task<ProcessStreamedResult> ReassembleImageFromStream(AsyncServerStreamingCall<StreamedProcessImageResponse> call)
        {
            var result = new ProcessStreamedResult();
            using (var memoryStream = new MemoryStream())
            {
                await foreach (var response in call.ResponseStream.ReadAllAsync())
                {
                    if (response.ContentCase == StreamedProcessImageResponse.ContentOneofCase.Metadata)
                    {
                        result.Metadata = response.Metadata;
                        if (!result.Metadata.Success)
                        {
                            return result; // 提前返回
                        }
                    }
                    else if (response.ContentCase == StreamedProcessImageResponse.ContentOneofCase.Chunk)
                    {
                        memoryStream.Write(response.Chunk.Data.ToByteArray());
                    }
                }

                if (memoryStream.Length > 0)
                {
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    using var tempBitmap = new Bitmap(memoryStream);
                    result.ResultImage = new Bitmap(tempBitmap);
                }
            }
            return result;
        }
        #endregion

        // 图像转换辅助方法 ConvertBitmapToBitmapSource 等保持不变

        public void Dispose()
        {
            _channel?.Dispose();
        }
    }
}
