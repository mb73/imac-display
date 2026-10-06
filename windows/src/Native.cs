using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ImacDisplay
{
    /* Win32 declarations used by the agent; everything here works for a standard user */
    internal static class Native
    {
        /* ---- DPI and metrics ---- */

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        public const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        /* ---- display configuration ---- */

        [StructLayout(LayoutKind.Sequential)]
        public struct LUID { public uint LowPart; public int HighPart; }

        [StructLayout(LayoutKind.Sequential)]
        public struct PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RATIONAL { public uint Numerator; public uint Denominator; }

        [StructLayout(LayoutKind.Sequential)]
        public struct PATH_TARGET_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx; public int outputTechnology; public int rotation;
            public int scaling; public RATIONAL refreshRate; public int scanLineOrdering; public int targetAvailable; public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PATH_INFO { public PATH_SOURCE_INFO sourceInfo; public PATH_TARGET_INFO targetInfo; public uint flags; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MODE_INFO
        {
            public int infoType; public uint id; public LUID adapterId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVICE_INFO_HEADER { public int type; public uint size; public LUID adapterId; public uint id; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct SOURCE_NAME
        {
            public DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DPI_GET { public DEVICE_INFO_HEADER header; public int minScaleRel; public int curScaleRel; public int maxScaleRel; }

        [StructLayout(LayoutKind.Sequential)]
        public struct DPI_SET { public DEVICE_INFO_HEADER header; public int scaleRel; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll")]
        public static extern int SetDisplayConfig(uint numPaths, IntPtr paths, uint numModes, IntPtr modes, uint flags);

        [DllImport("user32.dll")]
        public static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

        [DllImport("user32.dll")]
        public static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths, ref uint numModes, [Out] MODE_INFO[] modes, IntPtr topologyId);

        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME request);

        [DllImport("user32.dll")]
        public static extern int DisplayConfigGetDeviceInfo(ref DPI_GET request);

        [DllImport("user32.dll")]
        public static extern int DisplayConfigSetDeviceInfo(ref DPI_SET request);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ChangeDisplaySettingsEx(string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int ChangeDisplaySettingsEx(string deviceName, IntPtr devMode, IntPtr hwnd, uint flags, IntPtr lParam);

        /* ---- synthetic input ---- */

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Explicit)]
        public struct INPUTUNION
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public INPUTUNION u; }

        public const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
        public const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
            MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_MIDDLEDOWN = 0x0020,
            MOUSEEVENTF_MIDDLEUP = 0x0040, MOUSEEVENTF_XDOWN = 0x0080, MOUSEEVENTF_XUP = 0x0100,
            MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000, MOUSEEVENTF_VIRTUALDESK = 0x4000,
            MOUSEEVENTF_ABSOLUTE = 0x8000;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        public static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern short VkKeyScanEx(char ch, IntPtr layout);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint threadId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

        /* ---- cursor and monitors (physical pixels: the process is per-monitor DPI aware) ---- */

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        public const uint MONITOR_DEFAULTTONULL = 0, MONITOR_DEFAULTTOPRIMARY = 1, MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT point, uint flags);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);

        [DllImport("user32.dll")]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        /* ---- job objects: ffmpeg dies together with the agent ---- */

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize; public UIntPtr MaximumWorkingSetSize; public uint ActiveProcessLimit;
            public UIntPtr Affinity; public uint PriorityClass; public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation; public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit; public UIntPtr JobMemoryLimit; public UIntPtr PeakProcessMemoryUsed; public UIntPtr PeakJobMemoryUsed;
        }

        public const int JobObjectExtendedLimitInformation = 9;
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll")]
        public static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);

        [DllImport("kernel32.dll")]
        public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        /* ---- desktop: detects the lock screen and UAC prompts ---- */

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);

        [DllImport("user32.dll")]
        public static extern bool CloseDesktop(IntPtr desktop);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetUserObjectInformation(IntPtr obj, int index, StringBuilder info, int length, out int needed);

        /* ---- lid switch notifications ---- */

        public const int WM_POWERBROADCAST = 0x0218, PBT_POWERSETTINGCHANGE = 0x8013;
        public static readonly Guid GUID_LIDSWITCH_STATE_CHANGE = new Guid("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSettingGuid, int flags);

        /* ---- power requests: the laptop does not fall asleep while the picture runs ---- */

        public const uint POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x1;
        public const int PowerRequestSystemRequired = 1;

        /* only the simple-string member of the union; Version is POWER_REQUEST_CONTEXT_VERSION (0) */
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct REASON_CONTEXT
        {
            public uint Version;
            public uint Flags;
            [MarshalAs(UnmanagedType.LPWStr)] public string SimpleReasonString;
        }

        /* INVALID_HANDLE_VALUE (-1) on failure */
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT context);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool PowerSetRequest(IntPtr request, int type);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool PowerClearRequest(IntPtr request, int type);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr handle);

        /* ---- windows: another instance, DPI, dark title bars, themed scroll bars, the close button ---- */

        public const int SW_RESTORE = 9;
        public const int WM_SETTINGCHANGE = 0x001A, WM_SYSCOLORCHANGE = 0x0015, WM_DPICHANGED = 0x02E0;
        public const int WM_SYSCOMMAND = 0x0112, WM_NCHITTEST = 0x0084, SC_CLOSE = 0xF060, HTCLOSE = 20;
        public const int WM_SIZE = 0x0005, SIZE_MINIMIZED = 1, WM_WINDOWPOSCHANGING = 0x0046;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hwnd, int command);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, ref RECT lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int RegisterWindowMessage(string name);

        /* 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (19 before Windows 10 20H1) */
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        /* ---- standard output: redirected to a pipe or file, e.g. "imac-display.exe --test | Out-String"? ---- */

        public const int STD_OUTPUT_HANDLE = -11, FILE_TYPE_DISK = 1, FILE_TYPE_PIPE = 3;

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetStdHandle(int which);

        [DllImport("kernel32.dll")]
        public static extern int GetFileType(IntPtr handle);

        /* ---- shell folders and clipboard ---- */

        public static readonly Guid FOLDERID_Downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        [DllImport("shell32.dll")]
        public static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

        /* changes with every clipboard change; cheap enough to poll */
        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();
    }
}
