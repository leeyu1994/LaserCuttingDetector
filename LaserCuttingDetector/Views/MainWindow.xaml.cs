using DevExpress.Mvvm;
using DevExpress.Xpf.Core;
using HslCommunication.LogNet;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace LaserCuttingDetector.Views
{
    // 在你的类外面或者里面添加这个辅助类
    public static class NativeMethods
    {
        // 声明从 kernel32.dll 导入的 LoadLibrary 函数
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string lpFileName);
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : ThemedWindow
    {
        public MainWindow()
        {
            CheckDllLoading();
            InitializeComponent();
            Messenger.Default.Register<HslMessageItem>(this, "Log", msg =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (msg == null) return;
                    LogBox.Items.Add(msg);
                    if (LogBox.Items.Count > 1000)
                    {
                        LogBox.Items.RemoveAt(0);
                    }
                    LogBox.ScrollIntoView(msg);
                });
            });
        }

        private void CheckDllLoading()
        {
            // 拼接 DLL 的完整路径
            string dllPath = Path.Combine(AppContext.BaseDirectory, "libZkcpCore.dll");
            if (!File.Exists(dllPath))
            {
                MessageBox.Show($"DLL 文件不存在: {dllPath}");
                return;
            }

            // 尝试加载库
            IntPtr handle = NativeMethods.LoadLibrary(dllPath);
            if (handle == IntPtr.Zero)
            {
                // 如果加载失败，获取详细的错误码
                int errorCode = Marshal.GetLastWin32Error();
                MessageBox.Show($"无法加载 libZkcpCore.dll。\n路径: {dllPath}\nWin32 错误码: {errorCode}");
                // 你可以根据这个错误码在网上搜索具体原因
                // 常见的错误码:
                // 126: 找不到指定的模块 (通常是依赖项缺失)
                // 193: %1 不是有效的 Win32 应用程序 (通常是 x86/x64 架构不匹配)
            }
        }
    }
}
