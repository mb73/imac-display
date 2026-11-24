using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ImacDisplay
{
    /*
     Remote Desktop (mstsc) in full screen on the Mac display does not survive the program's display switches. When the
     panel comes on as the main display, before Windows locks itself or for ctrl+L, it takes over (0,0), where the Mac
     display was; Windows leaves the windows at their coordinates, which then lie on the panel. Remote Desktop spreads its
     full screen over both displays, then leaves it for its last window size and place, and stays a window, often on the
     panel: a strip of 117 x 1140 pixels, or 1920 x 1200 with its session fitted to the panel (2026-11-21, watched every
     250 ms). Minimized, it restored itself for that (the same day). When the panel goes off again, Remote Desktop in full
     screen on the Mac display leaves full screen and comes back by itself; one Windows minimizes because it lay on the
     panel comes back as a window without client area, 26 x 71 pixels, that the taskbar cannot bring back (2026-11-13
     three times, 2026-11-20). The same after the Mac display went off and on again.

     So the session notes, while the displays stay as they are, which Remote Desktop windows lie in full screen on the Mac
     display (Note), and after a change puts them back into full screen there (Keep), the way that held on 2026-11-13:
     restore, a window as large as the Mac display, then the "Vollbild" entry of its window menu. Straight from the
     collapsed state, full screen remembers 26 x 71 as the window size and falls back to it the next time it leaves full
     screen. A thread of its own does it, since it waits for Remote Desktop between the steps.

     Minimized from full screen, it may break as well and show it only when restored: after the switches of 2026-11-13
     its window still looked like full screen until the click in the taskbar, and on 2026-11-21, after three switches,
     it came back as a strip of 93 x 1305 pixels. So the session notes minimized ones, too. One that stays minimized at a
     change waits and is repaired once the user restores it (Note); one that restored itself is repaired at once and
     minimized again, as the user had it.
     */
    internal static class RemoteDesktop
    {
        const string WindowClass = "TscShellContainerClass";
        /* the "Vollbild" (full screen) entry of mstsc's window menu, a toggle */
        const int FullScreenCommand = 0x9D;

        static readonly object sync = new object();
        /* while the displays stay as they are: the windows in full screen on the Mac display, and those minimized from there; */
        /* when that was, since only a recent look tells what a switch changed */
        static List<IntPtr> shown = new List<IntPtr>(), minimized = new List<IntPtr>();
        static DateTime notedAt = DateTime.MinValue;
        /* minimized when the displays changed: repaired once the user restores them */
        static readonly List<IntPtr> waiting = new List<IntPtr>();
        /* minimized by the user, restored by Remote Desktop itself at a change: minimized again after the repair */
        static readonly List<IntPtr> putAway = new List<IntPtr>();
        /* for the repair thread: the windows to repair, and where the Mac display is now, the latest one asked for */
        static readonly List<IntPtr> queue = new List<IntPtr>();
        static Rectangle target;
        static uint targetDpi;
        static Thread repairing;

        /* While the displays stay as they are: the Remote Desktop windows in full screen on the Mac display, or minimized from there */
        public static void Note(DisplayInfo mac)
        {
            Rectangle bounds = Bounds(mac);
            var full = new List<IntPtr>();
            var iconic = new List<IntPtr>();
            foreach (IntPtr window in Windows())
            {
                if (IsFullScreenOn(window, bounds)) full.Add(window);
                else if (IsMinimizedFrom(window, bounds)) iconic.Add(window);
            }
            lock (sync)
            {
                /* not while a repair runs: the windows it repairs are not in full screen yet, and one it minimizes again */
                /* would look restored for a moment */
                if (repairing != null) return;
                /*
                 Restored since the last look, minimized from full screen before: only now Remote Desktop shows whether it
                 broke, however long ago the last switch was. On 2026-11-23 it was in full screen and worked after two
                 switches at night, was minimized in the morning, and came back as a strip of 47 x 1258 pixels.
                 */
                var before = new List<IntPtr>(waiting);
                foreach (IntPtr window in minimized) if (!before.Contains(window)) before.Add(window);
                var restored = new List<IntPtr>();
                foreach (IntPtr window in before)
                {
                    if (Native.IsWindow(window) && Native.IsIconic(window)) continue;
                    waiting.Remove(window);
                    if (Native.IsWindow(window)) restored.Add(window);
                }
                shown = full;
                minimized = iconic;
                notedAt = DateTime.UtcNow;
                if (restored.Count > 0) Schedule(restored, mac);
            }
        }

        /* While Windows is locked: nobody can touch Remote Desktop meanwhile, so what Note saw before still holds */
        public static void Locked()
        {
            lock (sync) notedAt = DateTime.UtcNow;
        }

        /* After a display change: the windows in full screen before go back there, minimized ones stay so */
        public static void Keep(DisplayInfo mac)
        {
            bool put = false;
            lock (sync)
            {
                /* not at the start of a session after a long break, e.g. while the Mac slept: meanwhile the user may have */
                /* restored and used it on the laptop (a lock does not count, Locked) */
                bool recent = (DateTime.UtcNow - notedAt).TotalSeconds < 15;
                var repair = new List<IntPtr>(shown);
                var before = new List<IntPtr>(minimized);
                foreach (IntPtr window in waiting) if (!before.Contains(window)) before.Add(window);
                foreach (IntPtr window in before)
                {
                    if (!Native.IsWindow(window)) continue;
                    if (Native.IsIconic(window))
                    {
                        if (waiting.Contains(window)) continue;
                        waiting.Add(window);
                        put = true;
                    }
                    else
                    {
                        /* restored by itself during the switch, unless it is in front: then the user clicked it, e.g. on */
                        /* coming back to the laptop, where the first input switches the panel off */
                        waiting.Remove(window);
                        repair.Add(window);
                        if (recent && window != Native.GetForegroundWindow() && !putAway.Contains(window)) putAway.Add(window);
                    }
                }
                if (repair.Count > 0) Schedule(repair, mac);
            }
            if (put) Program.Log("Remotedesktop ist minimiert. Kommt es beim Wiederherstellen nicht im Vollbild zurück, hole ich es dorthin.");
        }

        /* Hands windows to the repair thread, which starts if need be; the caller holds sync */
        static void Schedule(List<IntPtr> windows, DisplayInfo mac)
        {
            foreach (IntPtr window in windows) if (!queue.Contains(window)) queue.Add(window);
            target = Bounds(mac);
            targetDpi = (uint)(96 * (mac.ScalePercent > 0 ? mac.ScalePercent : 100) / 100);
            if (repairing != null) return;
            repairing = new Thread(RepairAll);
            repairing.IsBackground = true;
            repairing.Name = "remote desktop";
            repairing.Start();
        }

        static void RepairAll()
        {
            try
            {
                while (true)
                {
                    Rectangle mac;
                    uint dpi;
                    List<IntPtr> windows;
                    lock (sync)
                    {
                        if (queue.Count == 0)
                        {
                            repairing = null;
                            return;
                        }
                        mac = target;
                        dpi = targetDpi;
                        windows = new List<IntPtr>(queue);
                        queue.Clear();
                    }
                    /* right after a change or a restore, Windows and Remote Desktop may still be moving the window */
                    Thread.Sleep(500);
                    foreach (IntPtr window in windows)
                    {
                        bool full = Repair(window, mac, dpi);
                        bool away;
                        lock (sync) away = putAway.Remove(window);
                        if (away && full) PutAway(window);
                    }
                }
            }
            catch (Exception ex)
            {
                /* a worker thread must not take the program down */
                Program.Log("Remotedesktop ließ sich nicht zurückholen: " + ex.Message);
                lock (sync)
                {
                    queue.Clear();
                    putAway.Clear();
                    repairing = null;
                }
            }
        }

        /* Back into full screen on the Mac display; whether it is there now */
        static bool Repair(IntPtr window, Rectangle mac, uint dpi)
        {
            if (!Native.IsWindow(window)) return false;
            if (IsFullScreenOn(window, mac)) return true;
            if (Native.GetMenuState(Native.GetSystemMenu(window, false), FullScreenCommand, Native.MF_BYCOMMAND) == Native.MENU_ITEM_MISSING)
            {
                Program.Log("Remotedesktop hat das Vollbild verlassen, sein Fenstermenü kennt aber kein „Vollbild“ wie bisher: Ich lasse es, wie es ist.");
                return false;
            }
            Program.Log("Remotedesktop ist nach der Umschaltung nicht mehr im Vollbild auf dem Mac-Bildschirm: Ich hole es zurück.");
            if (Native.IsIconic(window))
            {
                Command(window, Native.SC_RESTORE);
                Until(delegate { return !Native.IsIconic(window); }, 1500);
                if (IsFullScreenOn(window, mac))
                {
                    Program.Log("Remotedesktop ist wieder im Vollbild.");
                    return true;
                }
            }
            if (!HasCaption(window))
            {
                /* in full screen elsewhere, e.g. on the panel: out of it first */
                Command(window, FullScreenCommand);
                if (!Until(delegate { return HasCaption(window); }, 2000))
                {
                    Failed(window);
                    return false;
                }
            }
            /* a window as large as the Mac display: full screen remembers it as the size to come back to */
            var frame = new Native.RECT { Left = mac.Left, Top = mac.Top, Right = mac.Right, Bottom = mac.Bottom };
            Native.AdjustWindowRectExForDpi(ref frame, Style(window, Native.GWL_STYLE), false, Style(window, Native.GWL_EXSTYLE), dpi);
            Native.SetWindowPos(window, IntPtr.Zero, frame.Left, frame.Top, frame.Right - frame.Left, frame.Bottom - frame.Top,
                Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Thread.Sleep(500);
            /* Remote Desktop keeps the window no larger than its session, which may have fitted itself to the panel meanwhile, */
            /* and keeps the right edge where it was (on 2026-11-21 that left the window beside the Mac display): centered on */
            /* the Mac display, full screen goes there */
            Rectangle now = WindowBounds(window);
            if (!mac.Contains(new Point(now.X + now.Width / 2, now.Y + now.Height / 2)))
            {
                Native.SetWindowPos(window, IntPtr.Zero, mac.X + (mac.Width - now.Width) / 2, mac.Y + (mac.Height - now.Height) / 2, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                Thread.Sleep(300);
            }
            Command(window, FullScreenCommand);
            if (Until(delegate { return IsFullScreenOn(window, mac); }, 3000))
            {
                Program.Log("Remotedesktop ist wieder im Vollbild.");
                return true;
            }
            Failed(window);
            return false;
        }

        /* Minimized again, as the user had it before the switch */
        static void PutAway(IntPtr window)
        {
            /* right after going into full screen, Remote Desktop let a minimize pass once (2026-11-23): a moment later, twice */
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Thread.Sleep(1000);
                if (window == Native.GetForegroundWindow())
                {
                    Program.Log("Remotedesktop bleibt offen: Es ist jetzt im Vordergrund.");
                    return;
                }
                Command(window, Native.SC_MINIMIZE);
                if (Until(delegate { return Native.IsIconic(window); }, 1500))
                {
                    Program.Log("Remotedesktop ist wieder minimiert, wie vor der Umschaltung.");
                    return;
                }
            }
            Program.Log("Remotedesktop ließ sich nicht wieder minimieren.");
        }

        static void Failed(IntPtr window)
        {
            Rectangle bounds = WindowBounds(window);
            Program.Log(string.Format("Remotedesktop ließ sich nicht zurück ins Vollbild holen (Fenster bei ({0},{1}), {2} × {3}, {4}).",
                bounds.X, bounds.Y, bounds.Width, bounds.Height, Native.IsIconic(window) ? "minimiert" : HasCaption(window) ? "mit Rahmen" : "ohne Rahmen"));
        }

        static List<IntPtr> Windows()
        {
            var found = new List<IntPtr>();
            var name = new StringBuilder(64);
            Native.EnumWindows(delegate (IntPtr window, IntPtr param)
            {
                name.Length = 0;
                if (Native.GetClassName(window, name, name.Capacity) > 0 && name.ToString() == WindowClass) found.Add(window);
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /* Full screen: no frame, exactly over the display */
        static bool IsFullScreenOn(IntPtr window, Rectangle mac)
        {
            return Native.IsWindowVisible(window) && !Native.IsIconic(window) && !HasCaption(window) && WindowBounds(window) == mac;
        }

        /* Minimized from full screen there: still without frame, and its place to come back to has the display's size */
        static bool IsMinimizedFrom(IntPtr window, Rectangle mac)
        {
            if (!Native.IsWindowVisible(window) || !Native.IsIconic(window) || HasCaption(window)) return false;
            var placement = new Native.WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
            if (!Native.GetWindowPlacement(window, ref placement)) return false;
            /* in workspace coordinates, which a taskbar at the top or left shifts: so only the size and roughly the place */
            Native.RECT normal = placement.normalPosition;
            Rectangle back = Rectangle.FromLTRB(normal.Left, normal.Top, normal.Right, normal.Bottom);
            return back.Size == mac.Size && mac.Contains(back.X + back.Width / 2, back.Y + back.Height / 2);
        }

        static bool HasCaption(IntPtr window)
        {
            return (Style(window, Native.GWL_STYLE) & Native.WS_CAPTION) == Native.WS_CAPTION;
        }

        /* a 32-bit style, however GetWindowLongPtr extends it */
        static uint Style(IntPtr window, int index)
        {
            return (uint)(Native.GetWindowLongPtr(window, index).ToInt64() & 0xFFFFFFFF);
        }

        static Rectangle WindowBounds(IntPtr window)
        {
            Native.RECT rect;
            if (!Native.GetWindowRect(window, out rect)) return Rectangle.Empty;
            return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }

        static Rectangle Bounds(DisplayInfo display)
        {
            return new Rectangle(display.X, display.Y, display.Width, display.Height);
        }

        static void Command(IntPtr window, int command)
        {
            Native.PostMessage(window, Native.WM_SYSCOMMAND, new IntPtr(command), IntPtr.Zero);
        }

        /* Waits up to timeout milliseconds for the condition */
        static bool Until(Func<bool> condition, int timeout)
        {
            for (int waited = 0; ; waited += 100)
            {
                if (condition()) return true;
                if (waited >= timeout) return false;
                Thread.Sleep(100);
            }
        }
    }
}
