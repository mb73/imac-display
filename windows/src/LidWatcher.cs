using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ImacDisplay
{
    /*
     Reports whether the laptop lid is open. Windows sends the current state right after the
     registration and then every change (WM_POWERBROADCAST with GUID_LIDSWITCH_STATE_CHANGE).
     A message-only window on its own STA thread receives the notifications.
     */
    internal sealed class LidWatcher : NativeWindow
    {
        volatile int state = -1;  // -1 unknown, 0 closed, 1 open

        public bool? IsOpen
        {
            get
            {
                int current = state;
                if (current < 0) return null;
                return current == 1;
            }
        }

        public static LidWatcher Start()
        {
            LidWatcher watcher = null;
            var ready = new ManualResetEvent(false);
            var thread = new Thread(() =>
            {
                watcher = new LidWatcher();
                ready.Set();
                Application.Run();
            });
            thread.IsBackground = true;
            thread.Name = "lid";
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.WaitOne(3000);
            /* give Windows a moment to deliver the initial state */
            for (int i = 0; i < 20 && watcher != null && watcher.IsOpen == null; i++) Thread.Sleep(50);
            return watcher;
        }

        LidWatcher()
        {
            var parameters = new CreateParams();
            parameters.Parent = new IntPtr(-3);  // HWND_MESSAGE: message-only window
            CreateHandle(parameters);
            Guid lid = Native.GUID_LIDSWITCH_STATE_CHANGE;
            Native.RegisterPowerSettingNotification(Handle, ref lid, 0);  // DEVICE_NOTIFY_WINDOW_HANDLE
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_POWERBROADCAST && m.WParam.ToInt64() == Native.PBT_POWERSETTINGCHANGE)
            {
                /* POWERBROADCAST_SETTING: GUID (16 bytes), DataLength (4 bytes), Data */
                var setting = (Guid)Marshal.PtrToStructure(m.LParam, typeof(Guid));
                if (setting == Native.GUID_LIDSWITCH_STATE_CHANGE) state = Marshal.ReadByte(m.LParam, 20) != 0 ? 1 : 0;
                m.Result = new IntPtr(1);
                return;
            }
            base.WndProc(ref m);
        }
    }
}
