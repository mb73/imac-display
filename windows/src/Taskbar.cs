using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ImacDisplay
{
    /*
     Explorer sometimes draws a taskbar too low after a display change: its window keeps the right height, but its
     content fills only the lower part, and the desktop shows above it (seen 2026-10-10 on the Mac display at 200 %,
     with the panel at 125 %). A hit test finds it whatever the desktop shows, a color, a picture or a slideshow: near
     the top edge the point belongs to the desktop's window, near the bottom edge to the taskbar. A notification makes Explorer lay its taskbars out again, in milliseconds, without
     restarting: first the one Windows' settings send after a taskbar option changed, then the one after a display change.
     */
    internal static class TrayRepair
    {
        /* Taskbars whose upper part shows the desktop; hidden or covered ones do not count */
        public static List<IntPtr> Broken()
        {
            var broken = new List<IntPtr>();
            foreach (IntPtr tray in Taskbars())
            {
                Native.RECT r;
                if (!Native.GetWindowRect(tray, out r) || r.Bottom - r.Top < 20) continue;
                int x = r.Left + Math.Min(300, (r.Right - r.Left) / 4);
                if (RootAt(x, r.Bottom - 6) != tray) continue;
                string top = ClassOf(RootAt(x, r.Top + 6));
                if (top == "Progman" || top == "WorkerW") broken.Add(tray);
            }
            return broken;
        }

        /* What Windows' settings send after a taskbar option changed */
        public static void NudgeSettings()
        {
            IntPtr tray = Native.FindWindow("Shell_TrayWnd", null);
            IntPtr result;
            if (tray != IntPtr.Zero)
                Native.SendMessageTimeout(tray, Native.WM_SETTINGCHANGE, IntPtr.Zero, "TraySettings", Native.SMTO_ABORTIFHUNG, 1000, out result);
        }

        /* What Windows sends after a display change; posted, since it carries no pointers */
        public static void NudgeDisplay()
        {
            int width = Native.GetSystemMetrics(0), height = Native.GetSystemMetrics(1);  // SM_CXSCREEN, SM_CYSCREEN
            var size = new IntPtr((height << 16) | (width & 0xFFFF));
            foreach (IntPtr tray in Taskbars()) Native.PostMessage(tray, Native.WM_DISPLAYCHANGE, new IntPtr(32), size);
        }

        static List<IntPtr> Taskbars()
        {
            var found = new List<IntPtr>();
            Native.EnumWindows(delegate (IntPtr hwnd, IntPtr param)
            {
                string name = ClassOf(hwnd);
                if (name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd") found.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            return found;
        }

        static IntPtr RootAt(int x, int y)
        {
            var point = new Native.POINT();
            point.X = x;
            point.Y = y;
            return Native.GetAncestor(Native.WindowFromPoint(point), Native.GA_ROOT);
        }

        static string ClassOf(IntPtr hwnd)
        {
            var name = new StringBuilder(256);
            return Native.GetClassName(hwnd, name, name.Capacity) > 0 ? name.ToString() : "";
        }
    }

    /*
     The program's taskbar button: a badge (overlay icon) for the connection state and a progress bar.
     Windows creates the button some time after the window and announces it with the registered message
     "TaskbarButtonCreated"; anything set before is lost, and the same happens when Explorer restarts, so
     the window applies its state again on every such message.
     */
    internal sealed class Taskbar
    {
        public const int NoProgress = 0, Indeterminate = 1, Normal = 2, Error = 4, Paused = 8;
        public static readonly int ButtonCreatedMessage = Native.RegisterWindowMessage("TaskbarButtonCreated");

        /* up to SetOverlayIcon, in vtable order; a wrong IID shows up as E_NOINTERFACE in the log */
        [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITaskbarList3
        {
            /* ITaskbarList */
            [PreserveSig] int HrInit();
            [PreserveSig] int AddTab(IntPtr hwnd);
            [PreserveSig] int DeleteTab(IntPtr hwnd);
            [PreserveSig] int ActivateTab(IntPtr hwnd);
            [PreserveSig] int SetActiveAlt(IntPtr hwnd);
            /* ITaskbarList2 */
            [PreserveSig] int MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
            /* ITaskbarList3 */
            [PreserveSig] int SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
            [PreserveSig] int SetProgressState(IntPtr hwnd, int flags);
            [PreserveSig] int RegisterTab(IntPtr tab, IntPtr mdi);
            [PreserveSig] int UnregisterTab(IntPtr tab);
            [PreserveSig] int SetTabOrder(IntPtr tab, IntPtr insertBefore);
            [PreserveSig] int SetTabActive(IntPtr tab, IntPtr mdi, uint reserved);
            [PreserveSig] int ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
            [PreserveSig] int ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
            [PreserveSig] int ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
            [PreserveSig] int SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
        }

        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
        class TaskbarList { }

        readonly ITaskbarList3 list;
        readonly IntPtr window;

        Taskbar(ITaskbarList3 list, IntPtr window)
        {
            this.list = list;
            this.window = window;
        }

        /* Null if the shell offers no taskbar list, e.g. while Explorer is not running */
        public static Taskbar For(IntPtr window)
        {
            try
            {
                var list = (ITaskbarList3)new TaskbarList();
                int hr = list.HrInit();
                if (hr == 0) return new Taskbar(list, window);
                Program.Log("Taskleiste nicht verfügbar: HrInit 0x" + hr.ToString("X8"));
            }
            catch (COMException ex) { Program.Log("Taskleiste nicht verfügbar: " + ex.Message); }
            catch (InvalidCastException ex) { Program.Log("Taskleiste nicht verfügbar: " + ex.Message); }
            return null;
        }

        /* icon: a small icon handle, IntPtr.Zero removes the badge; the description is read out by screen readers */
        public void SetBadge(IntPtr icon, string description)
        {
            list.SetOverlayIcon(window, icon, description);
        }

        /* state: NoProgress, Indeterminate, Normal, Error or Paused; percent counts for the last three */
        public void SetProgress(int state, int percent)
        {
            list.SetProgressState(window, state);
            if (state == Normal || state == Error || state == Paused)
                list.SetProgressValue(window, (ulong)Math.Max(0, Math.Min(100, percent)), 100);
        }
    }
}
