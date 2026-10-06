using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ImacDisplay
{
    /*
     Colors for the program's windows. Light mode uses the system colors, so high contrast keeps
     working. Dark mode follows Windows' app mode (Settings > Personalization > Colors), which WinForms
     does not know by itself: the title bar comes from DWM, scroll bars from the "DarkMode_Explorer"
     theme, buttons are drawn flat in these colors, and check boxes and progress bars draw themselves.
     */
    internal sealed class Theme
    {
        public bool Dark, HighContrast;
        public Color Back, Text, Muted, Bar, Banner, Link, Field, Face, Edge, Hover, Pressed, Track, Accent, OnAccent;

        static Theme current;

        public static Theme Current
        {
            get
            {
                if (current == null) current = Read();
                return current;
            }
        }

        /* Reads the Windows setting again; true if light/dark or high contrast changed */
        public static bool Refresh()
        {
            Theme before = Current;
            current = Read();
            return current.Dark != before.Dark || current.HighContrast != before.HighContrast;
        }

        static Theme Read()
        {
            bool highContrast = SystemInformation.HighContrast;
            return Create(!highContrast && AppsUseDarkMode(), highContrast);
        }

        static Theme Create(bool dark, bool highContrast)
        {
            var t = new Theme();
            t.HighContrast = highContrast;
            t.Dark = dark;
            if (t.Dark)
            {
                t.Back = Color.FromArgb(0x20, 0x20, 0x20);
                t.Text = Color.White;
                t.Muted = Color.FromArgb(0xA8, 0xA8, 0xA8);
                t.Bar = Color.FromArgb(0x19, 0x19, 0x19);
                t.Banner = Color.FromArgb(0x1C, 0x2D, 0x45);
                t.Link = Color.FromArgb(0x6C, 0xB8, 0xFF);
                t.Field = Color.FromArgb(0x2D, 0x2D, 0x2D);
                t.Face = Color.FromArgb(0x37, 0x37, 0x37);
                t.Edge = Color.FromArgb(0x50, 0x50, 0x50);
                t.Hover = Color.FromArgb(0x42, 0x42, 0x42);
                t.Pressed = Color.FromArgb(0x30, 0x30, 0x30);
                t.Track = Color.FromArgb(0x45, 0x45, 0x45);
                t.Accent = Color.FromArgb(0x4C, 0xA6, 0xFF);
                t.OnAccent = Color.Black;
            }
            else
            {
                t.Back = SystemColors.Window;
                t.Text = SystemColors.WindowText;
                t.Muted = SystemColors.GrayText;
                t.Bar = SystemColors.Control;
                t.Banner = t.HighContrast ? SystemColors.Window : Color.FromArgb(0xE6, 0xF0, 0xFC);
                t.Link = SystemColors.HotTrack;
                t.Field = SystemColors.Window;
                t.Face = SystemColors.Control;
                t.Edge = SystemColors.ControlDark;
                t.Hover = SystemColors.ControlLight;
                t.Pressed = SystemColors.ControlDark;
                t.Track = t.HighContrast ? SystemColors.ControlDark : Color.FromArgb(0xDA, 0xDA, 0xDA);
                t.Accent = t.HighContrast ? SystemColors.Highlight : Color.FromArgb(0x1D, 0x6C, 0xE0);
                t.OnAccent = t.HighContrast ? SystemColors.HighlightText : Color.White;
            }
            return t;
        }

        static bool AppsUseDarkMode()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch (SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }

        /* Dark or light title bar; Windows 10 before 20H1 knows the attribute as 19 instead of 20 */
        public void StyleTitleBar(IntPtr window)
        {
            int dark = Dark ? 1 : 0;
            try
            {
                if (Native.DwmSetWindowAttribute(window, 20, ref dark, 4) != 0) Native.DwmSetWindowAttribute(window, 19, ref dark, 4);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        public void Style(Button button)
        {
            if (Dark)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = Face;
                button.ForeColor = Text;
                button.FlatAppearance.BorderColor = Edge;
                button.FlatAppearance.MouseOverBackColor = Hover;
                button.FlatAppearance.MouseDownBackColor = Pressed;
            }
            else
            {
                button.FlatStyle = FlatStyle.Standard;
                button.ResetBackColor();
                button.ResetForeColor();
                button.UseVisualStyleBackColor = true;
            }
        }

        public void Style(LinkLabel link)
        {
            link.LinkColor = Link;
            link.ActiveLinkColor = Link;
            link.VisitedLinkColor = Link;
        }

        /* input fields stand out, read-only text (log, update notes) sits on the window's color */
        public void Style(TextBox box)
        {
            box.BackColor = box.ReadOnly ? Back : Field;
            box.ForeColor = Text;
            /* dark scroll bars; "Explorer" gives the light ones of current Windows */
            if (box.IsHandleCreated) Native.SetWindowTheme(box.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null);
        }
    }

    /*
     Base for the program's windows, built like designer code: controls in 96-DPI units, scaled by
     WinForms (per-monitor v2, switched on in imac-display.exe.config) to the system DPI and again
     whenever a window moves to a monitor with another DPI. A window that opens on a monitor whose DPI
     differs from the system DPI (fixed at sign-in) gets no such message and would keep the system
     DPI's size, so OnLoad replays it. Fonts other than the window's own are derived from it in
     UpdateFonts, which runs after every change, so they scale along.
     */
    internal class ThemedForm : Form
    {
        static Icon appIcon;
        /* minimized since the last WM_SIZE: the next move is the restore (see WndProc) */
        bool minimized;

        protected ThemedForm()
        {
            SuspendLayout();
            Font = new Font("Segoe UI", 9F);
            Icon = AppIcon;
        }

        /* The program icon in all its sizes (embedded by build.cmd), for title bars and the taskbar */
        public static Icon AppIcon
        {
            get
            {
                if (appIcon == null)
                {
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("imac-display.ico"))
                        appIcon = stream != null ? new Icon(stream) : Icon.ExtractAssociatedIcon(Program.ExePath);
                }
                return appIcon;
            }
        }

        /* End of the constructor: from 96 DPI to the system DPI, like InitializeComponent does it */
        protected void EndLayout()
        {
            GrowAndShrink(this);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            UpdateFonts();
            ApplyTheme();
            ResumeLayout(false);
            PerformLayout();
        }

        /*
         AutoSize buttons and panels measure their text at the real DPI while their coordinates are still in
         96-DPI units; in WinForms' default mode "grow only" they would keep that size and get scaled once more
         */
        static void GrowAndShrink(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                var button = control as Button;
                var panel = control as Panel;
                if (button != null && button.AutoSize) button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                else if (panel != null && panel.AutoSize) panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                GrowAndShrink(control);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            /* before the window shows, so a dark window does not flash a white title bar */
            Theme.Current.StyleTitleBar(Handle);
        }

        protected override void OnLoad(EventArgs e)
        {
            if (MatchMonitorDpi() && !Modal)
            {
                if (StartPosition == FormStartPosition.CenterScreen) CenterToScreen();
                else if (StartPosition == FormStartPosition.CenterParent && Owner != null) CenterToParent();
            }
            ApplyTheme();
            base.OnLoad(e);
        }

        /* Replays the DPI change Windows did not send; true if the window had to be rescaled */
        bool MatchMonitorDpi()
        {
            int dpi;
            try { dpi = (int)Native.GetDpiForWindow(Handle); }
            catch (EntryPointNotFoundException) { return false; }  // before Windows 10 1607
            if (dpi <= 0 || dpi == DeviceDpi) return false;
            double factor = (double)dpi / DeviceDpi;
            Rectangle bounds = Bounds;
            var suggested = new Native.RECT();
            suggested.Left = bounds.Left;
            suggested.Top = bounds.Top;
            suggested.Right = bounds.Left + (int)Math.Round(bounds.Width * factor);
            suggested.Bottom = bounds.Top + (int)Math.Round(bounds.Height * factor);
            Native.SendMessage(Handle, Native.WM_DPICHANGED, new IntPtr((dpi << 16) | dpi), ref suggested);
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == Native.WM_SIZE)
            {
                bool restored = minimized && (long)m.WParam != Native.SIZE_MINIMIZED;
                minimized = (long)m.WParam == Native.SIZE_MINIMIZED;
                /* WinForms may move the window once more after the restore, to where it remembers it */
                if (restored) Post(KeepOnScreen);
            }
            else if (m.Msg == Native.WM_WINDOWPOSCHANGING && minimized) KeepReachable(m.LParam);
        }

        void KeepOnScreen()
        {
            if (WindowState != FormWindowState.Normal) return;
            Rectangle bounds = Bounds;
            if (Reachable(new Native.RECT { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom })) return;
            Location = Middle(bounds.Width, bounds.Height);
        }

        /*
         The restore of a minimized window: Windows moves windows along when a screen moves or goes, but not the place
         a minimized one returns to. After the lid opened, the main window came back where the Mac display had been,
         beside every screen. Such a return goes to the middle of the screen with the mouse pointer instead.
         */
        static void KeepReachable(IntPtr windowPos)
        {
            var pos = (Native.WINDOWPOS)Marshal.PtrToStructure(windowPos, typeof(Native.WINDOWPOS));
            /* -32000 is where Windows parks minimized windows */
            if ((pos.flags & (Native.SWP_NOMOVE | Native.SWP_NOSIZE)) != 0 || pos.x <= -30000 || pos.y <= -30000) return;
            var target = new Native.RECT { Left = pos.x, Top = pos.y, Right = pos.x + pos.cx, Bottom = pos.y + pos.cy };
            if (Reachable(target)) return;
            Point middle = Middle(pos.cx, pos.cy);
            pos.x = middle.X;
            pos.y = middle.Y;
            Marshal.StructureToPtr(pos, windowPos, false);
        }

        /* Top left corner for a window in the middle of the screen with the mouse pointer */
        static Point Middle(int width, int height)
        {
            Native.POINT cursor;
            if (!Native.GetCursorPos(out cursor)) cursor = new Native.POINT();  // e.g. while locked: the main display
            var info = new Native.MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
            Native.GetMonitorInfo(Native.MonitorFromPoint(cursor, Native.MONITOR_DEFAULTTONEAREST), ref info);
            Native.RECT work = info.rcWork;
            return new Point(work.Left + Math.Max(0, (work.Right - work.Left - width) / 2), work.Top + Math.Max(0, (work.Bottom - work.Top - height) / 2));
        }

        /* A good piece of the title bar lies in a screen's work area, where the mouse can grab it */
        static bool Reachable(Native.RECT window)
        {
            Native.RECT caption = window;
            caption.Bottom = Math.Min(window.Bottom, window.Top + 24);
            IntPtr monitor = Native.MonitorFromRect(ref caption, Native.MONITOR_DEFAULTTONULL);
            if (monitor == IntPtr.Zero) return false;
            var info = new Native.MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO));
            if (!Native.GetMonitorInfo(monitor, ref info)) return true;
            int width = Math.Min(caption.Right, info.rcWork.Right) - Math.Max(caption.Left, info.rcWork.Left);
            int height = Math.Min(caption.Bottom, info.rcWork.Bottom) - Math.Max(caption.Top, info.rcWork.Top);
            return width >= Math.Min(caption.Right - caption.Left, 100) && height > 0;
        }

        /* Runs action on the window's thread later; does nothing once the window is gone */
        protected void Post(MethodInvoker action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
            }
            catch (InvalidOperationException) { }  // closed meanwhile
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            UpdateFonts();
        }

        /* Derives the fonts of single controls (headlines, the log) from the window's font */
        protected virtual void UpdateFonts() { }

        /* Colors for the current light or dark mode; again after every change in Windows */
        public void ApplyTheme()
        {
            Theme t = Theme.Current;
            BackColor = t.Back;
            ForeColor = t.Text;
            Style(this, t);
            if (IsHandleCreated)
            {
                t.StyleTitleBar(Handle);
                /* repaints the title bar right away */
                Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_FRAMECHANGED);
            }
            Invalidate(true);
        }

        /* Roles in Tag: "bar" (buttons at the bottom), "banner" (update offer), "muted" (secondary text) */
        static void Style(Control parent, Theme t)
        {
            foreach (Control control in parent.Controls)
            {
                var button = control as Button;
                var link = control as LinkLabel;
                var box = control as TextBox;
                if (button != null) t.Style(button);
                else if (link != null) t.Style(link);
                else if (box != null) t.Style(box);
                string role = control.Tag as string;
                if (role == "bar") control.BackColor = t.Bar;
                else if (role == "banner") control.BackColor = t.Banner;
                else if (role == "muted") control.ForeColor = t.Muted;
                Style(control, t);
            }
        }
    }
}
