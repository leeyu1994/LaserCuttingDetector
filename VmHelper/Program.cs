// VmHelper/Program.cs

using System;
using System.Threading; // 需要引入这个命名空间

namespace VmHelper
{
    class Program
    {
        // 定义与客户端完全相同的事件名称
        private const string ShutdownEventName = "Global\\LaserCuttingGrpcServerShutdownEvent";

        static void Main(string[] args)
        {
            // 创建一个命名的事件等待句柄，用于接收关闭信号
            // initial_state = false (非终止状态)
            // mode = EventResetMode.ManualReset (需要手动重置)
            using (var shutdownEvent = new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownEventName))
            {
                Console.WriteLine("准备启动 .NET Framework gRPC 服务端...");

                // 启动 gRPC 服务器
                Helper.Instance.Start();

                Console.WriteLine("服务已启动。正在等待关闭信号...");

                // 这里不再使用 Console.ReadKey()，而是无限期等待关闭事件被触发
                shutdownEvent.WaitOne();

                Console.WriteLine("收到关闭信号，正在准备关闭服务...");
                // 优雅地停止 gRPC 服务器
                Helper.Instance.StopAsync().GetAwaiter().GetResult();

                Console.WriteLine("服务已关闭，程序即将退出。");
            }
        }
    }
}