using System;
using System.Runtime.InteropServices;

namespace ImacDisplay
{
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
