// CameraControllerServer.cs
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace VmHelper
{
    /// <summary>
    /// 服务端的相机控制器，移植自客户端的CameraController
    /// </summary>
    public class CameraControllerServer : IDisposable
    {
        private long _camera; // 相机句柄
        private readonly ImageDataCallback _imageCallback;
        private readonly MsgNotifyCallback _msgCallback;
        private Bitmap _latestImage; // 存储最新采集到的图像
        private readonly object _imageLock = new object();
        private bool _isGrabbing = false;
        private bool _isInitialized = false;

        // 事件
        public event Action<Bitmap> ImageReceived;
        public event Action<string> LogMessage;

        public CameraControllerServer()
        {
            _imageCallback = OnImageDataCallback;
            _msgCallback = OnMsgCallback;
        }

        #region 相机基本属性

        public bool IsInitialized => _isInitialized;
        public bool IsGrabbing => _isGrabbing;

        public int FrameWidth => _camera != 0 ? ZkcpAPI.zkcp_frame_width(_camera) : 0;
        public int FrameHeight => _camera != 0 ? ZkcpAPI.zkcp_frame_height(_camera) : 0;
        public int DPI => _camera != 0 ? ZkcpAPI.zkcp_dpi(_camera) : 0;
        public int RedLightIntensity => _camera != 0 ? ZkcpAPI.zkcp_light_source(_camera, (int)LightSourceColor.Colour_RED) : 0;
        public int GreenLightIntensity => _camera != 0 ? ZkcpAPI.zkcp_light_source(_camera, (int)LightSourceColor.Colour_GREEN) : 0;
        public int BlueLightIntensity => _camera != 0 ? ZkcpAPI.zkcp_light_source(_camera, (int)LightSourceColor.Colour_BLUE) : 0;

        #endregion

        #region 相机回调处理

        private void OnImageDataCallback(IntPtr imageDataPtr)
        {
            if (!_isGrabbing) return;

            ImageDataInfo imageData = Marshal.PtrToStructure<ImageDataInfo>(imageDataPtr);
            Bitmap bitmap = ImageConverter.ConvertToBitmap(imageData);

            if (bitmap != null)
            {
                lock (_imageLock)
                {
                    _latestImage?.Dispose();
                    _latestImage = (Bitmap)bitmap.Clone();
                }

                // 触发事件
                ImageReceived?.Invoke(bitmap);
                LogMessage?.Invoke($"[相机] 接收到图像: {bitmap.Width}x{bitmap.Height}, 格式: {bitmap.PixelFormat}");
            }
        }

        private void OnMsgCallback(IntPtr msgNotifyPtr)
        {
            MsgNotifyInfo msgInfo = Marshal.PtrToStructure<MsgNotifyInfo>(msgNotifyPtr);
            MsgNotify msgType = (MsgNotify)msgInfo.msg;
            LogMessage?.Invoke($"[相机] 收到消息: {msgType}");
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 初始化相机
        /// </summary>
        public bool Initialize(string configFilePath = "./ZKCP658.vlcf", int cameraType = 4, int baudrate = 115200)
        {
            if (_isInitialized)
            {
                LogMessage?.Invoke("[相机] 相机已初始化，跳过重复初始化");
                return true;
            }

            // 获取服务器列表
            Servers servers = new Servers();
            servers.server = new Server[10];

            IntPtr serversPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Servers>());
            Marshal.StructureToPtr(servers, serversPtr, false);

            bool result = ZkcpAPI.zkcp_get_servers(serversPtr);
            servers = Marshal.PtrToStructure<Servers>(serversPtr);
            Marshal.FreeHGlobal(serversPtr);

            if (!result)
            {
                LogMessage?.Invoke("[相机] 未找到可用的服务器");
                return false;
            }

            LogMessage?.Invoke($"[相机] 找到 {servers.size} 个服务器");

            // 创建驱动
            _camera = ZkcpAPI.zkcp_create_drive();
            if (_camera == 0)
            {
                LogMessage?.Invoke("[相机] 创建相机驱动失败");
                return false;
            }

            // 打开采集卡
            if (!ZkcpAPI.zkcp_open(_camera, configFilePath, 0, cameraType, baudrate))
            {
                LogMessage?.Invoke("[相机] 采集卡打开失败");
                return false;
            }

            // 设置回调函数
            if (!ZkcpAPI.zkcp_set_image_data_callback(_camera, _imageCallback, IntPtr.Zero))
            {
                LogMessage?.Invoke("[相机] 设置图像数据回调函数失败");
                return false;
            }

            if (!ZkcpAPI.zkcp_set_msg_callback(_camera, _msgCallback, IntPtr.Zero))
            {
                LogMessage?.Invoke("[相机] 设置消息回调函数失败");
                return false;
            }

            _isInitialized = true;
            LogMessage?.Invoke("[相机] 相机初始化成功");

            // 输出相机参数
            LogMessage?.Invoke($"[相机] 帧宽度: {FrameWidth}, 帧高度: {FrameHeight}, DPI: {DPI}");
            LogMessage?.Invoke($"[相机] 光源强度 - 红: {RedLightIntensity}, 绿: {GreenLightIntensity}, 蓝: {BlueLightIntensity}");

            return true;
        }

        /// <summary>
        /// 设置相机参数
        /// </summary>
        public bool SetParameters(
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
            if (!_isInitialized)
            {
                LogMessage?.Invoke("[相机] 相机未初始化，无法设置参数");
                return false;
            }

            bool allSuccess = true;

            // 设置光源
            if (redLight.HasValue && !ZkcpAPI.zkcp_set_light_source(_camera, redLight.Value, (int)LightSourceColor.Colour_RED))
            {
                LogMessage?.Invoke($"[相机] 设置红光强度失败: {redLight.Value}");
                allSuccess = false;
            }

            if (greenLight.HasValue && !ZkcpAPI.zkcp_set_light_source(_camera, greenLight.Value, (int)LightSourceColor.Colour_GREEN))
            {
                LogMessage?.Invoke($"[相机] 设置绿光强度失败: {greenLight.Value}");
                allSuccess = false;
            }

            if (blueLight.HasValue && !ZkcpAPI.zkcp_set_light_source(_camera, blueLight.Value, (int)LightSourceColor.Colour_BLUE))
            {
                LogMessage?.Invoke($"[相机] 设置蓝光强度失败: {blueLight.Value}");
                allSuccess = false;
            }

            // 设置帧高度
            if (frameHeight.HasValue && !ZkcpAPI.zkcp_set_frame_height(_camera, frameHeight.Value))
            {
                LogMessage?.Invoke($"[相机] 设置帧高度失败: {frameHeight.Value}");
                allSuccess = false;
            }

            // 设置行频率
            if (lineFrequency.HasValue && !ZkcpAPI.zkcp_set_line_frequency(_camera, lineFrequency.Value))
            {
                LogMessage?.Invoke($"[相机] 设置行频率失败: {lineFrequency.Value}");
                allSuccess = false;
            }

            // 设置行同步源
            if (lineSyncSource.HasValue && !ZkcpAPI.zkcp_set_line_sync_source(_camera, lineSyncSource.Value))
            {
                LogMessage?.Invoke($"[相机] 设置行同步源失败: {lineSyncSource.Value}");
                allSuccess = false;
            }

            // 设置校正使能
            if (correctionEnabled.HasValue && !ZkcpAPI.zkcp_set_correction_enabled(_camera, correctionEnabled.Value))
            {
                LogMessage?.Invoke($"[相机] 设置校正使能失败: {correctionEnabled.Value}");
                allSuccess = false;
            }

            // 设置倍频器
            if (multiplier.HasValue && !ZkcpAPI.zkcp_set_multiplier(_camera, multiplier.Value))
            {
                LogMessage?.Invoke($"[相机] 设置倍频器失败: {multiplier.Value}");
                allSuccess = false;
            }

            // 设置轴编码器方向
            if (shaftEncoderDirection.HasValue && !ZkcpAPI.zkcp_set_shaft_encoder_direction(_camera, shaftEncoderDirection.Value))
            {
                LogMessage?.Invoke($"[相机] 设置轴编码器方向失败: {shaftEncoderDirection.Value}");
                allSuccess = false;
            }

            // 设置外部行触发
            if (externalLineTrigger.HasValue && !ZkcpAPI.zkcp_set_external_line_trigger(_camera, externalLineTrigger.Value))
            {
                LogMessage?.Invoke($"[相机] 设置外部行触发失败: {externalLineTrigger.Value}");
                allSuccess = false;
            }

            if (allSuccess)
            {
                LogMessage?.Invoke("[相机] 参数设置成功");
            }

            return allSuccess;
        }

        /// <summary>
        /// 开始采集
        /// </summary>
        public bool StartGrab(bool continuousMode = true)
        {
            if (!_isInitialized)
            {
                LogMessage?.Invoke("[相机] 相机未初始化，无法开始采集");
                return false;
            }

            if (_isGrabbing)
            {
                LogMessage?.Invoke("[相机] 相机已在采集中");
                return true;
            }

            if (!ZkcpAPI.zkcp_grab_start(_camera))
            {
                LogMessage?.Invoke("[相机] 开始采集失败");
                return false;
            }

            _isGrabbing = true;
            LogMessage?.Invoke("[相机] 开始采集图像...");
            return true;
        }

        /// <summary>
        /// 停止采集
        /// </summary>
        public bool StopGrab()
        {
            if (!_isGrabbing)
            {
                LogMessage?.Invoke("[相机] 相机未在采集中");
                return true;
            }

            ZkcpAPI.zkcp_grab_stop(_camera);
            _isGrabbing = false;
            LogMessage?.Invoke("[相机] 停止采集");
            return true;
        }

        /// <summary>
        /// 采集一张图像（带超时）
        /// </summary>
        public async Task<Bitmap> CaptureImageAsync(int timeoutMs = 10000)
        {
            if (!_isInitialized)
            {
                LogMessage?.Invoke("[相机] 相机未初始化，无法采集图像");
                return null;
            }

            Bitmap capturedImage = null;
            bool imageReceived = false;

            // 临时事件处理器
            Action<Bitmap> tempHandler = (bitmap) =>
            {
                if (!imageReceived)
                {
                    capturedImage = (Bitmap)bitmap.Clone();
                    imageReceived = true;
                }
            };

            try
            {
                // 订阅图像接收事件
                ImageReceived += tempHandler;

                // 如果没有在采集，临时开始采集
                bool wasGrabbing = _isGrabbing;
                if (!wasGrabbing)
                {
                    if (!StartGrab())
                    {
                        return null;
                    }
                }

                // 等待图像接收或超时
                var startTime = DateTime.Now;
                while (!imageReceived && (DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
                {
                    await Task.Delay(50);
                }

                // 如果之前没在采集，停止采集
                if (!wasGrabbing)
                {
                    StopGrab();
                }

                if (imageReceived)
                {
                    LogMessage?.Invoke($"[相机] 成功采集图像: {capturedImage?.Width}x{capturedImage?.Height}");
                }
                else
                {
                    LogMessage?.Invoke($"[相机] 采集图像超时 ({timeoutMs}ms)");
                }

                return capturedImage;
            }
            finally
            {
                // 取消订阅
                ImageReceived -= tempHandler;
            }
        }

        /// <summary>
        /// 获取最新的图像（如果有的话）
        /// </summary>
        public Bitmap GetLatestImage()
        {
            lock (_imageLock)
            {
                return _latestImage != null ? (Bitmap)_latestImage.Clone() : null;
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_isGrabbing)
            {
                StopGrab();
            }

            if (_camera != 0)
            {
                ZkcpAPI.zkcp_close(_camera);
                ZkcpAPI.zkcp_release_drive(_camera);
                _camera = 0;
            }

            lock (_imageLock)
            {
                _latestImage?.Dispose();
                _latestImage = null;
            }

            _isInitialized = false;
            LogMessage?.Invoke("[相机] 相机资源已释放");
        }

        #endregion
    }
}
