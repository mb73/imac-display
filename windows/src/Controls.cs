using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace ImacDisplay
{
    /* Connection states as the window's status light and the taskbar badge show them */
    internal enum Light { Off, Busy, Paused, On, OnWifi, Problem }

    /* Draws the status lights; the taskbar badges are the same drawing with a white ring */
    internal static class Badges
    {
        static readonly Dictionary<Light, IntPtr> icons = new Dictionary<Light, IntPtr>();

        /* white marks stay readable on all of them, in both themes */
        public static Color ColorOf(Light light, bool dark)
        {
            switch (light)
            {
                case Light.On:
                case Light.OnWifi: return dark ? Color.FromArgb(0x2E, 0xA0, 0x43) : Color.FromArgb(0x10, 0x7C, 0x10);
                case Light.Busy:
                case Light.Paused: return dark ? Color.FromArgb(0xE3, 0x9A, 0x1B) : Color.FromArgb(0xE0, 0x8A, 0x00);
                case Light.Problem: return dark ? Color.FromArgb(0xE5, 0x53, 0x4B) : Color.FromArgb(0xD9, 0x2D, 0x2D);
                default: return dark ? Color.FromArgb(0x78, 0x78, 0x78) : Color.FromArgb(0xA6, 0xA6, 0xA6);
            }
        }

        /*
         A round light with a white mark: a check for On (proportions as in one-click-vpn's taskbar badge),
         the Wi-Fi symbol for OnWifi, two bars for Paused, an exclamation mark for Problem. As a taskbar
         badge it gets a white ring.
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
            else if (light == Light.OnWifi)
            {
                /* a dot and two arcs above it */
                float cx = r.X + w * 0.5f, cy = r.Y + h * 0.69f;
                using (var arc = new Pen(Color.White, w * 0.105f))
                {
                    arc.StartCap = LineCap.Round;
                    arc.EndCap = LineCap.Round;
                    foreach (float radius in new[] { 0.20f, 0.36f })
                        g.DrawArc(arc, cx - w * radius, cy - h * radius, w * radius * 2, h * radius * 2, 225, 90);
                }
                g.FillEllipse(Brushes.White, cx - w * 0.075f, cy - h * 0.075f, w * 0.15f, h * 0.15f);
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

        /* The symbol of a message, like the lights: an exclamation mark on amber (warning) or red (error), an i on blue */
        public static void DrawSymbol(Graphics g, RectangleF r, MessageKind kind, bool dark)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = kind == MessageKind.Error ? ColorOf(Light.Problem, dark)
                : kind == MessageKind.Warning ? ColorOf(Light.Busy, dark)
                : dark ? Color.FromArgb(0x2F, 0x80, 0xDD) : Color.FromArgb(0x1D, 0x6C, 0xE0);
            using (var brush = new SolidBrush(fill)) g.FillEllipse(brush, r);
            /* the i is the exclamation mark upside down */
            bool info = kind == MessageKind.Information;
            float w = r.Width, h = r.Height, x = r.X + w / 2f;
            float from = info ? 0.48f : 0.25f, to = info ? 0.75f : 0.52f, dot = info ? 0.28f : 0.72f;
            using (var bar = new Pen(Color.White, w * 0.12f))
            {
                bar.StartCap = LineCap.Round;
                bar.EndCap = LineCap.Round;
                g.DrawLine(bar, x, r.Y + h * from, x, r.Y + h * to);
            }
            float d = w * 0.15f;
            g.FillEllipse(Brushes.White, x - d / 2f, r.Y + h * dot - d / 2f, d, d);
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

    /* The symbol in front of a message (MessageDialog) */
    internal sealed class MessageIcon : Control
    {
        readonly MessageKind kind;

        public MessageIcon(MessageKind kind)
        {
            this.kind = kind;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
            AccessibleName = kind == MessageKind.Error ? "Fehler" : kind == MessageKind.Warning ? "Warnung" : "Information";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            float side = Math.Min(ClientSize.Width, ClientSize.Height) - 1;
            if (side <= 0) return;
            Badges.DrawSymbol(e.Graphics, new RectangleF((ClientSize.Width - side) / 2f, (ClientSize.Height - side) / 2f, side, side),
                kind, Theme.Current.Dark);
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

    /*
     The laptop panel and the Mac display as rectangles in their real proportions, like Settings > System > Display.
     The Mac display stays in the middle (a Mac usually stands still, the laptop moves around it), with room for the
     laptop on every side, so nothing moves or rescales while dragging. Dropped, the laptop goes to the nearest edge
     of the Mac display, with at least a quarter of the shorter side in contact, and lines up with its edges or middle
     when close to them; a dashed outline shows that place while dragging. The arrow keys put it on a side. Placed
     tells the Mac display's new offset from the panel, its top-left corner relative to the panel's, as Windows needs
     it. Without both displays the box shows a note.
     */
    internal sealed class ArrangementView : Control
    {
        Size panel, mac;
        /* the Mac display's top-left corner relative to the panel's, in desktop pixels */
        Point offset;
        bool available;
        string note = "";
        /* while dragging: where the laptop is, relative to the Mac display, and where on it the mouse holds it (view pixels) */
        bool dragging;
        Point dragged;
        Size grab;
        /* view = origin + desktop * scale, desktop relative to the Mac display's top-left corner */
        float scale = 1;
        PointF origin;
        /* a little pointer shows where the mouse goes over: it rests on the laptop, wanders to the Mac display, rests there and comes back */
        const int RestMs = 3000, MoveMs = 600;
        readonly Timer animation = new Timer();
        /* 0 resting on the laptop, 1 on its way to the Mac display, 2 resting there, 3 on its way back */
        int phase, phaseStart;

        /* an arrow like the mouse pointer of Windows, tip at the top left, in fractions of its width and height */
        static readonly PointF[] Arrow =
        {
            new PointF(0f, 0f), new PointF(0f, 0.889f), new PointF(0.364f, 0.667f), new PointF(0.636f, 1f),
            new PointF(0.818f, 0.944f), new PointF(0.545f, 0.611f), new PointF(1f, 0.611f)
        };

        public event Action<Point> Placed;

        public ArrangementView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.Diagram;
            AccessibleName = "Anordnung der Bildschirme";
            animation.Tick += delegate { Animate(); };
        }

        /* Both displays, the Mac display at offset from the panel */
        public void ShowDisplays(Size panelSize, Size macSize, Point macOffset)
        {
            bool changed = !available || panel != panelSize || mac != macSize || (!dragging && offset != macOffset);
            available = true;
            panel = panelSize;
            mac = macSize;
            if (!dragging) offset = macOffset;
            Fit();
            AccessibleDescription = Side(panel, mac, offset);
            Cursor = Cursors.Default;
            if (changed && !dragging) RestartPointer();
            Invalidate();
        }

        public void ShowNote(string text)
        {
            available = false;
            dragging = false;
            animation.Enabled = false;
            note = text;
            AccessibleDescription = text;
            Cursor = Cursors.Default;
            Invalidate();
        }

        /* "Laptop links neben dem Mac", "Laptop unter dem Mac" …, for the log and screen readers */
        public static string Side(Size panel, Size mac, Point offset)
        {
            if (offset.X >= panel.Width) return "Laptop links neben dem Mac";
            if (offset.X + mac.Width <= 0) return "Laptop rechts neben dem Mac";
            if (offset.Y + mac.Height <= 0) return "Laptop unter dem Mac";
            if (offset.Y >= panel.Height) return "Laptop über dem Mac";
            return "Laptop auf dem Mac";  // duplicated, e.g. after Win+P
        }

        /*
         The nearest place where one display (moving) touches an edge of another (anchor), relative to the anchor's
         top-left corner, with at least a quarter of the shorter side in contact; within tolerance of the anchor's
         start, end or middle along that edge it lines up with it
         */
        public static Point Snap(Size anchor, Size moving, Point at, int tolerance)
        {
            int overlapX = Math.Max(1, Math.Min(anchor.Width, moving.Width) / 4);
            int overlapY = Math.Max(1, Math.Min(anchor.Height, moving.Height) / 4);
            int alongX = Clamp(at.X, overlapX - moving.Width, anchor.Width - overlapX);
            int alongY = Clamp(at.Y, overlapY - moving.Height, anchor.Height - overlapY);
            /* right, left, below, above */
            var sides = new[]
            {
                new Point(anchor.Width, alongY), new Point(-moving.Width, alongY),
                new Point(alongX, anchor.Height), new Point(alongX, -moving.Height)
            };
            int best = 0;
            long nearest = long.MaxValue;
            for (int i = 0; i < sides.Length; i++)
            {
                long dx = sides[i].X - at.X, dy = sides[i].Y - at.Y;
                if (dx * dx + dy * dy < nearest)
                {
                    nearest = dx * dx + dy * dy;
                    best = i;
                }
            }
            Point place = sides[best];
            if (best < 2) place.Y = Align(place.Y, anchor.Height, moving.Height, tolerance);
            else place.X = Align(place.X, anchor.Width, moving.Width, tolerance);
            return place;
        }

        static int Align(int position, int anchorLength, int movingLength, int tolerance)
        {
            foreach (int target in new[] { 0, anchorLength - movingLength, (anchorLength - movingLength) / 2 })
                if (Math.Abs(position - target) <= tolerance) return target;
            return position;
        }

        static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        /* The Mac display in the middle, with room for the laptop on every side of it */
        void Fit()
        {
            float pad = LogicalToDeviceUnits(8);
            float width = Math.Max(1, ClientSize.Width - 2 * pad), height = Math.Max(1, ClientSize.Height - 2 * pad);
            scale = Math.Min(width / Math.Max(1, mac.Width + 2 * panel.Width), height / Math.Max(1, mac.Height + 2 * panel.Height));
            origin = new PointF((ClientSize.Width - mac.Width * scale) / 2f, (ClientSize.Height - mac.Height * scale) / 2f);
        }

        /* close enough to the Mac display's edges or middle to line up with them: a few pixels on the screen */
        int Tolerance
        {
            get { return (int)(LogicalToDeviceUnits(8) / scale); }
        }

        /* where the laptop is, relative to the Mac display */
        Point LaptopAt
        {
            get { return dragging ? dragged : new Point(-offset.X, -offset.Y); }
        }

        RectangleF ToView(Point at, Size size)
        {
            return new RectangleF(origin.X + at.X * scale, origin.Y + at.Y * scale, size.Width * scale, size.Height * scale);
        }

        void Place(Point to)
        {
            bool moved = to != offset;
            offset = to;
            AccessibleDescription = Side(panel, mac, offset);
            RestartPointer();
            Invalidate();
            if (moved && Placed != null) Placed(to);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (available) Fit();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!available || e.Button != MouseButtons.Left) return;
            Focus();
            RectangleF r = ToView(LaptopAt, panel);
            if (!r.Contains(e.Location)) return;
            dragged = LaptopAt;
            dragging = true;
            grab = new Size(e.X - (int)r.X, e.Y - (int)r.Y);
            /* no second pointer next to the one that drags */
            animation.Enabled = false;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging)
            {
                Cursor = available && ToView(LaptopAt, panel).Contains(e.Location) ? Cursors.SizeAll : Cursors.Default;
                return;
            }
            dragged = new Point((int)Math.Round((e.X - grab.Width - origin.X) / scale), (int)Math.Round((e.Y - grab.Height - origin.Y) / scale));
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            Point snapped = Snap(mac, panel, dragged, Tolerance);
            Place(new Point(-snapped.X, -snapped.Y));
        }

        /* the drag ends without a drop, e.g. when another window takes the mouse */
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!dragging) return;
            dragging = false;
            RestartPointer();
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!available || dragging) return;
            /* the laptop goes to that side of the Mac display, so the Mac display lies on the other side of it */
            int middleX = (panel.Width - mac.Width) / 2, middleY = (panel.Height - mac.Height) / 2;
            Point to;
            if (e.KeyCode == Keys.Left) to = new Point(panel.Width, middleY);
            else if (e.KeyCode == Keys.Right) to = new Point(-mac.Width, middleY);
            else if (e.KeyCode == Keys.Up) to = new Point(middleX, panel.Height);
            else if (e.KeyCode == Keys.Down) to = new Point(middleX, -mac.Height);
            else return;
            e.Handled = true;
            Place(to);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme t = Theme.Current;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : t.Back);
            using (GraphicsPath box = Badges.Rounded(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), LogicalToDeviceUnits(6)))
            {
                using (var fill = new SolidBrush(t.Field)) g.FillPath(fill, box);
                using (var edge = new Pen(t.Edge)) g.DrawPath(edge, box);
            }
            if (!available)
            {
                int inset = LogicalToDeviceUnits(14);
                TextRenderer.DrawText(g, note, Font, Rectangle.Inflate(ClientRectangle, -inset, -inset), t.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            RectangleF laptop = ToView(LaptopAt, panel), macDisplay = ToView(Point.Empty, mac);
            /* a hairline gap where the two touch, as in Windows */
            laptop.Inflate(-1, -1);
            macDisplay.Inflate(-1, -1);
            RectangleF laptopName, macName;
            string laptopLabel = Label(g, laptop, "Laptop", panel, out laptopName);
            string macLabel = Label(g, macDisplay, "Mac", mac, out macName);
            DrawDisplay(g, t, macDisplay, macLabel, false, 255);
            if (dragging)
            {
                /* where it will go */
                RectangleF target = ToView(Snap(mac, panel, dragged, Tolerance), panel);
                target.Inflate(-1, -1);
                using (GraphicsPath path = Badges.Rounded(target, LogicalToDeviceUnits(3)))
                using (var pen = new Pen(t.Accent, LogicalToDeviceUnits(1)))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawPath(pen, path);
                }
            }
            DrawDisplay(g, t, laptop, laptopLabel, true, dragging ? 200 : 255);
            int shared = animation.Enabled && !dragging ? SharedEdge() : -1;
            if (shared >= 0) DrawPointer(g, PointerTip(shared, laptop, laptopName, macDisplay, macName));
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -3, -3), t.Text, t.Field);
        }

        /* The pointer starts over on the laptop; it shows only where the displays share an edge, and not while dragging */
        void RestartPointer()
        {
            phase = 0;
            phaseStart = Environment.TickCount;
            animation.Interval = RestMs;
            animation.Enabled = available && !dragging && SharedEdge() >= 0;
        }

        void Animate()
        {
            int length = phase % 2 == 0 ? RestMs : MoveMs;
            int elapsed = unchecked(Environment.TickCount - phaseStart);
            if (elapsed >= length)
            {
                phase = (phase + 1) % 4;
                phaseStart = Environment.TickCount;
                elapsed = 0;
                length = phase % 2 == 0 ? RestMs : MoveMs;
            }
            animation.Interval = phase % 2 == 0 ? Math.Max(15, length - elapsed) : 15;
            Invalidate();
        }

        /* The panel's edge the Mac display touches: 0 right, 1 left, 2 top, 3 bottom; -1 if they touch nowhere */
        int SharedEdge()
        {
            bool besideY = offset.Y < panel.Height && offset.Y + mac.Height > 0;
            bool besideX = offset.X < panel.Width && offset.X + mac.Width > 0;
            if (besideY && offset.X == panel.Width) return 0;
            if (besideY && offset.X + mac.Width == 0) return 1;
            if (besideX && offset.Y + mac.Height == 0) return 2;
            if (besideX && offset.Y == panel.Height) return 3;
            return -1;
        }

        float PointerHeight
        {
            get { return LogicalToDeviceUnits(9); }
        }

        float PointerWidth
        {
            get { return PointerHeight * 0.61f; }
        }

        float PointerGap
        {
            get { return LogicalToDeviceUnits(2); }
        }

        /*
         Where the pointer rests in r: at the far side (0 right, 1 left, 2 top, 3 bottom), level with the middle of the
         shared edge. Where it would touch the name there, it goes beside the name along that side, toward the middle of
         the shared edge; only if there is no room either, it stays at the far side.
         */
        PointF Rest(RectangleF r, RectangleF name, int side, PointF middle)
        {
            float w = PointerWidth, h = PointerHeight, gap = PointerGap;
            float x = Math.Max(r.Left + gap, Math.Min(r.Right - gap - w, middle.X - w / 2));
            float y = Math.Max(r.Top + gap, Math.Min(r.Bottom - gap - h, middle.Y - h / 2));
            if (side == 0) x = r.Right - gap - w;
            else if (side == 1) x = r.Left + gap;
            else if (side == 2) y = r.Top + gap;
            else y = r.Bottom - gap - h;
            if (!new RectangleF(x, y, w, h).IntersectsWith(name)) return new PointF(x, y);
            if (side <= 1)
            {
                float above = name.Top - gap - h, below = name.Bottom + gap;
                bool fitsAbove = above >= r.Top + gap, fitsBelow = below + h <= r.Bottom - gap;
                if (fitsAbove && (!fitsBelow || middle.Y < name.Top + name.Height / 2)) return new PointF(x, above);
                if (fitsBelow) return new PointF(x, below);
            }
            else
            {
                float left = name.Left - gap - w, right = name.Right + gap;
                bool fitsLeft = left >= r.Left + gap, fitsRight = right + w <= r.Right - gap;
                if (fitsLeft && (!fitsRight || middle.X < name.Left + name.Width / 2)) return new PointF(left, y);
                if (fitsRight) return new PointF(right, y);
            }
            return new PointF(x, y);
        }

        /*
         The pointer's tip now. It rests at the far side of each display, beside the names, and on its way crosses
         the shared edge in the middle of the part where the two touch, where the real mouse goes over.
         */
        PointF PointerTip(int edge, RectangleF laptop, RectangleF laptopName, RectangleF macDisplay, RectangleF macName)
        {
            PointF middle = edge <= 1
                ? new PointF(edge == 0 ? laptop.Right : laptop.Left,
                    (Math.Max(laptop.Top, macDisplay.Top) + Math.Min(laptop.Bottom, macDisplay.Bottom)) / 2)
                : new PointF((Math.Max(laptop.Left, macDisplay.Left) + Math.Min(laptop.Right, macDisplay.Right)) / 2,
                    edge == 2 ? laptop.Top : laptop.Bottom);
            PointF onLaptop = Rest(laptop, laptopName, edge ^ 1, middle), onMac = Rest(macDisplay, macName, edge, middle);
            if (phase == 0) return onLaptop;
            if (phase == 2) return onMac;
            float progress = Math.Min(1f, unchecked(Environment.TickCount - phaseStart) / (float)MoveMs);
            progress = progress * progress * (3 - 2 * progress);
            PointF from = phase == 1 ? onLaptop : onMac, to = phase == 1 ? onMac : onLaptop;
            return new PointF(from.X + (to.X - from.X) * progress, from.Y + (to.Y - from.Y) * progress);
        }

        /* white with a black edge, like the real one, in light and dark mode alike */
        void DrawPointer(Graphics g, PointF tip)
        {
            float w = PointerWidth, h = PointerHeight;
            var outline = new PointF[Arrow.Length];
            for (int i = 0; i < Arrow.Length; i++) outline[i] = new PointF(tip.X + Arrow[i].X * w, tip.Y + Arrow[i].Y * h);
            g.FillPolygon(Brushes.White, outline);
            using (var pen = new Pen(Color.Black, Math.Max(1f, h / 10f)))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawPolygon(pen, outline);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) animation.Dispose();
            base.Dispose(disposing);
        }

        const TextFormatFlags LabelFlags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        /* The display's name, with its size below where both fit, and the box the text takes in the middle of r */
        string Label(Graphics g, RectangleF r, string name, Size size, out RectangleF box)
        {
            var room = new Size(Math.Max(1, (int)r.Width), int.MaxValue);
            string label = name + "\n" + size.Width + " × " + size.Height;
            Size needed = TextRenderer.MeasureText(g, label, Font, room, LabelFlags);
            if (needed.Height > r.Height || needed.Width > r.Width)
            {
                label = name;
                needed = TextRenderer.MeasureText(g, label, Font, room, LabelFlags);
            }
            box = new RectangleF(r.X + (r.Width - needed.Width) / 2f, r.Y + (r.Height - needed.Height) / 2f, needed.Width, needed.Height);
            return label;
        }

        /* the laptop in the accent color: it is the one to move */
        void DrawDisplay(Graphics g, Theme t, RectangleF r, string label, bool movable, int alpha)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath path = Badges.Rounded(r, LogicalToDeviceUnits(3)))
            {
                using (var fill = new SolidBrush(Color.FromArgb(alpha, movable ? t.Accent : t.Face))) g.FillPath(fill, path);
                using (var edge = new Pen(Color.FromArgb(alpha, movable ? t.Accent : t.Edge))) g.DrawPath(edge, path);
            }
            TextRenderer.DrawText(g, label, Font, Rectangle.Round(r), movable ? t.OnAccent : t.Text, LabelFlags);
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
