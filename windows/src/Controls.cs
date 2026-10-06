using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ImacDisplay
{
    /* Connection states as the window's status light and the taskbar badge show them */
    internal enum Light { Off, Busy, Paused, On, Problem }

    /* Draws the status lights; the taskbar badges are the same drawing with a white ring */
    internal static class Badges
    {
        static readonly Dictionary<Light, IntPtr> icons = new Dictionary<Light, IntPtr>();

        /* white marks stay readable on all of them, in both themes */
        public static Color ColorOf(Light light, bool dark)
        {
            switch (light)
            {
                case Light.On: return dark ? Color.FromArgb(0x2E, 0xA0, 0x43) : Color.FromArgb(0x10, 0x7C, 0x10);
                case Light.Busy:
                case Light.Paused: return dark ? Color.FromArgb(0xE3, 0x9A, 0x1B) : Color.FromArgb(0xE0, 0x8A, 0x00);
                case Light.Problem: return dark ? Color.FromArgb(0xE5, 0x53, 0x4B) : Color.FromArgb(0xD9, 0x2D, 0x2D);
                default: return dark ? Color.FromArgb(0x78, 0x78, 0x78) : Color.FromArgb(0xA6, 0xA6, 0xA6);
            }
        }

        /*
         A round light with a white mark: a check for On (proportions as in one-click-vpn's taskbar badge),
         two bars for Paused, an exclamation mark for Problem. As a taskbar badge it gets a white ring.
         */
        public static void Draw(Graphics g, RectangleF r, Light light, bool dark, bool ring)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(ColorOf(light, dark))) g.FillEllipse(brush, r);
            if (ring)
                using (var pen = new Pen(Color.White, r.Width * 0.086f)) g.DrawEllipse(pen, r);
            float w = r.Width, h = r.Height;
            if (light == Light.On)
            {
                using (var tick = new Pen(Color.White, w * 0.138f))
                {
                    tick.StartCap = LineCap.Round;
                    tick.EndCap = LineCap.Round;
                    tick.LineJoin = LineJoin.Round;
                    g.DrawLines(tick, new[]
                    {
                        new PointF(r.X + w * 0.259f, r.Y + h * 0.534f),
                        new PointF(r.X + w * 0.431f, r.Y + h * 0.707f),
                        new PointF(r.X + w * 0.776f, r.Y + h * 0.328f)
                    });
                }
            }
            else if (light == Light.Paused)
            {
                g.FillRectangle(Brushes.White, r.X + w * 0.30f, r.Y + h * 0.28f, w * 0.14f, h * 0.44f);
                g.FillRectangle(Brushes.White, r.X + w * 0.56f, r.Y + h * 0.28f, w * 0.14f, h * 0.44f);
            }
            else if (light == Light.Problem)
            {
                g.FillRectangle(Brushes.White, r.X + w * 0.43f, r.Y + h * 0.20f, w * 0.14f, h * 0.38f);
                g.FillEllipse(Brushes.White, r.X + w * 0.42f, r.Y + h * 0.65f, w * 0.16f, h * 0.16f);
            }
        }

        /* Icon handle for the taskbar badge, IntPtr.Zero for Off (no badge); each made once and kept */
        public static IntPtr TaskbarIcon(Light light)
        {
            if (light == Light.Off) return IntPtr.Zero;
            IntPtr icon;
            if (icons.TryGetValue(light, out icon)) return icon;
            using (var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    Draw(g, new RectangleF(1.5f, 1.5f, 28.5f, 28.5f), light, false, true);
                }
                icon = bitmap.GetHicon();
            }
            icons[light] = icon;
            return icon;
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(2 * radius, Math.Min(r.Width, r.Height));
            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /* The round status light in front of the headline */
    internal sealed class StatusLight : Control
    {
        Light light = Light.Off;

        public StatusLight()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
        }

        public Light Light
        {
            get { return light; }
            set
            {
                if (light == value) return;
                light = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            float side = Math.Min(ClientSize.Width, ClientSize.Height) - 1;
            if (side <= 0) return;
            Badges.Draw(e.Graphics, new RectangleF((ClientSize.Width - side) / 2f, (ClientSize.Height - side) / 2f, side, side),
                light, Theme.Current.Dark, false);
        }
    }

    /* Slim progress bar in the theme's colors: a percentage, a moving segment while the end is unknown, or nothing */
    internal sealed class ThinBar : Control
    {
        public const int None = -1, Unknown = -2;

        int value = None;
        float phase;
        readonly Timer timer = new Timer();

        public ThinBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = false;
            AccessibleRole = AccessibleRole.ProgressBar;
            timer.Interval = 30;
            timer.Tick += delegate
            {
                phase = (phase + 0.012f) % 1.4f;
                Invalidate();
            };
        }

        /* None, Unknown or a percentage */
        public int Value
        {
            get { return value; }
            set
            {
                int next = value == Unknown ? Unknown : value < 0 ? None : Math.Min(100, value);
                if (next == this.value) return;
                this.value = next;
                timer.Enabled = next == Unknown;
                AccessibleDescription = next >= 0 ? next + " %" : null;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (value == None || Width <= 0 || Height <= 0) return;
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new RectangleF(0, 0, Width, Height);
            float radius = Height / 2f;
            using (GraphicsPath path = Badges.Rounded(track, radius))
            using (var brush = new SolidBrush(t.Track))
                g.FillPath(brush, path);
            RectangleF fill;
            if (value == Unknown)
            {
                /* a third of the width, running from left to right and around again */
                fill = new RectangleF((phase - 0.4f) * Width, 0, Width * 0.4f, Height);
                fill.Intersect(track);
            }
            else fill = new RectangleF(0, 0, Width * value / 100f, Height);
            if (fill.Width <= 0) return;
            using (GraphicsPath path = Badges.Rounded(fill, radius))
            using (var brush = new SolidBrush(t.Accent))
                g.FillPath(brush, path);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    /* CheckBox that draws itself in dark mode; WinForms' own drawing knows only light check boxes */
    internal sealed class ThemedCheckBox : CheckBox
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            if (!t.Dark)
            {
                base.OnPaint(e);
                return;
            }
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int side = LogicalToDeviceUnits(13);
            var box = new RectangleF(0.5f, (Height - side) / 2 + 0.5f, side - 1, side - 1);
            using (GraphicsPath path = Badges.Rounded(box, LogicalToDeviceUnits(3)))
            {
                using (var fill = new SolidBrush(Checked ? t.Accent : t.Field)) g.FillPath(fill, path);
                using (var edge = new Pen(Checked ? t.Accent : t.Muted)) g.DrawPath(edge, path);
            }
            if (Checked)
            {
                using (var pen = new Pen(t.OnAccent, Math.Max(1.5f, side / 7f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    g.DrawLines(pen, new[]
                    {
                        new PointF(box.X + box.Width * 0.24f, box.Y + box.Height * 0.52f),
                        new PointF(box.X + box.Width * 0.43f, box.Y + box.Height * 0.71f),
                        new PointF(box.X + box.Width * 0.77f, box.Y + box.Height * 0.31f)
                    });
                }
            }
            int left = side + LogicalToDeviceUnits(4);
            var text = new Rectangle(left, 0, Width - left, Height);
            TextRenderer.DrawText(g, Text, Font, text, Enabled ? t.Text : t.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                Size size = TextRenderer.MeasureText(g, Text, Font, text.Size, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                ControlPaint.DrawFocusRectangle(g, new Rectangle(left - 1, (Height - size.Height) / 2, size.Width + 2, size.Height), t.Text, BackColor);
            }
        }
    }
}
