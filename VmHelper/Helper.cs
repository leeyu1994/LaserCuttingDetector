// VmHelper/Helper.cs

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Core;

namespace VmHelper
{
    /// <summary>
    /// gRPC 服务帮助类，管理服务器的启动和停止
    /// </summary>
    public class Helper
    {
        private static readonly Lazy<Helper> _instance = new Lazy<Helper>(() => new Helper());
        public static Helper Instance => _instance.Value;

        private Grpc.Core.Server _server;
        private LegacyVisionServiceImpl _serviceImpl;

        private Helper() { }

        /// <summary>
        /// 启动 gRPC 服务器
        /// </summary>
        public void Start()
        {
            try
            {
                // 创建服务实现实例
                _serviceImpl = new LegacyVisionServiceImpl();

                // 配置服务器
                _server = new Grpc.Core.Server(new List<ChannelOption>
                {
                    // 设置接收和发送的最大消息大小为 200MB，以防万一
                    // 这对流式传输的“块”大小也有效
                    new ChannelOption(ChannelOptions.MaxReceiveMessageLength, 200 * 1024 * 1024),
                    new ChannelOption(ChannelOptions.MaxSendMessageLength, 200 * 1024 * 1024)
                })
                {
                    Ports = { new Grpc.Core.ServerPort("localhost", 50051, Grpc.Core.ServerCredentials.Insecure) }
                };

                // 绑定服务
                _server.Services.Add(Legacy.LegacyVisionService.BindService(_serviceImpl));

                // 启动服务器
                _server.Start();

                Console.WriteLine("gRPC 服务器已在端口 50051 上启动");
                Console.WriteLine("支持的服务方法:");
                Console.WriteLine("- LoadSolution: 加载视觉方案");
                Console.WriteLine("- ProcessImageFromPath: 处理本地图像文件");
                Console.WriteLine("- InitializeCamera: 初始化相机");
                Console.WriteLine("- SetCameraParameters: 设置相机参数");
                Console.WriteLine("- StartCameraGrab: 开始相机采集");
                Console.WriteLine("- StopCameraGrab: 停止相机采集");
                Console.WriteLine("- CaptureAndProcess: 采集并处理图像");
                Console.WriteLine("- GetCameraStatus: 获取相机状态");
                Console.WriteLine("- ReleaseCamera: 释放相机资源");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动 gRPC 服务器时发生错误: {ex.Message}");
                Console.WriteLine($"错误详情: {ex}");
                throw;
            }
        }

        /// <summary>
        /// 异步停止 gRPC 服务器
        /// </summary>
        public async Task StopAsync()
        {
            try
            {
                var server = _server; // 本地变量避免空引用
                if (server != null)
                {
                    Console.WriteLine("正在停止 gRPC 服务器...");

                    // 释放服务实现的资源
                    var serviceImpl = _serviceImpl;
                    serviceImpl?.Dispose();

                    // 优雅地关闭服务器
                    await server.ShutdownAsync();
                    Console.WriteLine("gRPC 服务器已停止");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"停止 gRPC 服务器时发生错误: {ex.Message}");

                // 如果优雅关闭失败，强制杀死服务器
                try
                {
                    var server = _server;
                    if (server != null)
                    {
                        await server.KillAsync();
                        Console.WriteLine("gRPC 服务器已强制停止");
                    }
                }
                catch (Exception killEx)
                {
                    Console.WriteLine($"强制停止 gRPC 服务器失败: {killEx.Message}");
                }
            }
            finally
            {
                _server = null;
                _serviceImpl = null;
            }
        }
    }
}
