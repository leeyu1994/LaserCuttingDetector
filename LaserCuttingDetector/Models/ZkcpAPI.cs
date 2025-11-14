using System;
using System.Runtime.InteropServices;

namespace LaserCuttingDetector.Models
{
    // 枚举定义
    public enum CameraType
    {
        ZKCP_90,
        ZKCP_160,
        ZKCP_300,
        ZKCP_400,
        ZKCP_658,
        ZKCP_300B
    }

    public enum LightSourceColor
    {
        Colour_RGB,
        Colour_RED,
        Colour_GREEN,
        Colour_BLUE,
        Colour_Backup_1,
        Colour_Backup_2,
        Colour_Backup_3,
        Colour_Backup_4,
        Colour_Backup_5
    }

    public enum ScanMode
    {
        ScanMode_Invalid = 0,
        ScanMode_Default,
        ScanMode_ConditionalTrigger
    }

    public enum ADSectionIndex
    {
        ADSectionIndex_Zero = 0,
        ADSectionIndex_One,
        ADSectionIndex_Two,
        ADSectionIndex_Three,
        ADSectionIndex_Four,
        ADSectionIndex_Five,
        ADSectionIndex_Six
    }

    public enum ScanDPI
    {
        ScanDPI_Invalid = 0,
        ScanDPI_300,
        ScanDPI_600,
        ScanDPI_1200,
        ScanDPI_2400
    }

    public enum GenCorrectionType
    {
        Gen_WhiteFieldCorrection,
        Gen_BlackFieldCorrection
    }

    public enum ImageFormat
    {
        Format_Invalid = 0,
        Format_Index8 = 8,
        Format_RGB888 = 24
    }

    public enum BoardMode
    {
        BoardMode_Invalid = 0,
        BoardMode_Free,
        BoardMode_Fixed,
        BoardMode_Variable,
        BoardMode_Software
    }

    public enum LineSyncSource
    {
        InternalLineTrigger,
        ExternalLineTriggerInput1,
        ExternalLineTriggerInput2,
        ShaftEncoderInput,
        BoardSync1,
        BoardSync2
    }

    public enum ExternalLineTriggerSource
    {
        Auto = 0,
        PhaseA,
        PhaseB,
        PhaseA_PhaseB
    }

    public enum ShaftEncoderDirection
    {
        DirectionIgnored = 0,
        DirectionForward,
        DirectionReverse
    }

    public enum MsgNotify
    {
        Msg_None = 0x00000,
        Msg_NoHSyncPresent = 0x00001,
        Msg_HSyncPresent = 0x00002,
        Msg_NoVerticalSync = 0x00004,
        Msg_VerticalSync = 0x00008,
        Msg_StartOfFrame = 0x00010,
        Msg_EndOfFrame = 0x00020,
        Msg_PixelClk = 0x00040,
        Msg_NoPixelClk = 0x00080,
        Msg_FrameLost = 0x00100,
        Msg_DataOverflow = 0x00200,
        Msg_LineTriggerTooFast = 0x00400,
        Msg_LineTriggerTooSlow = 0x00800,
        Msg_GrabError = 0x01000,
        Msg_CardStatusIOA = 2000000,
        Msg_CardStatusIOB = 2000001,
        Msg_CardStatusIOC = 2000002,
        Msg_CardStatusIOD = 2000003
    }

    // 结构体定义
    [StructLayout(LayoutKind.Sequential)]
    public struct ImageDataInfo
    {
        public IntPtr bits;
        public int width;
        public int height;
        public int channel;
        public int depth;
        public int bytesPerLine;
        public float fps;
        public IntPtr context;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MsgNotifyInfo
    {
        public int msg;
        public IntPtr context;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct Server
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string serverName;
        public long snNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Servers
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public Server[] server;
        public int size;
    }

    // 委托定义
    public delegate void ImageDataCallback(IntPtr imageDataPtr);
    public delegate void MsgNotifyCallback(IntPtr msgNotifyPtr);


    public static class ZkcpAPI
    {
        private const string DLL_NAME = "libZkcpCore.dll"; // 根据实际DLL名称修改

        // 获取服务器列表
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_get_servers(IntPtr servers);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_get_servers2(IntPtr server, ref int size);

        // 驱动管理
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern long zkcp_create_drive();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void zkcp_release_drive(long handler);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_open(long handler, string fileName, int index, int type, int baudrate);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern void zkcp_close(long handler);

        // 采集控制
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_grab_start(long handler);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_grab_stop(long handler);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_snap_start(long handler);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_snap_stop(long handler);

        // 相机信息
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int zkcp_type(long handler);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int zkcp_dpi(long handler);

        // 光源控制
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_set_light_source(long handler, int value, int mode = (int)LightSourceColor.Colour_RGB);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int zkcp_light_source(long handler, int mode = (int)LightSourceColor.Colour_RGB);

        // 板卡模式
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_set_board(long handler, int mode);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int zkcp_board(long handler);

        // 行同步源
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_set_line_sync_source(long handler, int source);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int zkcp_line_sync_source(long handler);

        // 行频率
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_set_line_frequency(long handler, int value);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int zkcp_line_frequency(long handler);

        // 倍频器
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_set_multiplier(long handler, float value);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern float zkcp_multiplier(long handler);

        // 轴编码器方向
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_set_shaft_encoder_direction(long handler, int direction);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int zkcp_shaft_encoder_direction(long handler);

        // 外部行触发
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_set_external_line_trigger(long handler, int source);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int zkcp_external_line_trigger(long handler);

        // 校正
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_set_correction_enabled(long handler, bool enabled);

        // 回调函数
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_set_image_data_callback(long handler, ImageDataCallback callback, IntPtr context);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_set_msg_callback(long handler, MsgNotifyCallback callback, IntPtr context);

        // 帧尺寸
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int zkcp_frame_width(long handler);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int zkcp_frame_height(long handler);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool zkcp_set_frame_height(long handler, int height);

        // 时钟
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.Cdecl)]
        public static extern int zkcp_clock(long handler);
    }
}


