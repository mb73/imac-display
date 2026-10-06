using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace ImacDisplay
{
    /*
     Turns the Mac's input lines into SendInput calls (works for a standard user; only windows
     running elevated and the secure desktop are out of reach).
       M x y                   absolute move, 0..65535 across the external display
       B button down x y mods  1 left, 2 right, 3 middle, 4/5 back/forward
       W dy dx mods            wheel in Windows units (120 per notch)
       K vk down mods ext      virtual key
       C codepoint down mods   shortcut by character, mapped with the current keyboard layout
       T utf16hex              text, typed as Unicode independent of the layout
       S mods                  modifier state (Shift/Ctrl follow; Alt/Win are only released)
       R                       release everything
     Modifier bits: 1 Shift, 2 Ctrl, 4 Alt, 8 Win. Alt and Win are pressed only together with a
     key or click, so Windows never sees them alone (no menu activation, no Start menu).
     */
    internal sealed class Injector
    {
        const int Shift = 1, Ctrl = 2, Alt = 4, Win = 8;
        static readonly ushort[] ModifierKeys = { 0xA0, 0xA2, 0xA4, 0x5B };  // LShift, LCtrl, LAlt, LWin

        int held;
        readonly Dictionary<ushort, bool> pressedKeys = new Dictionary<ushort, bool>();
        readonly HashSet<int> pressedButtons = new HashSet<int>();
        int areaX, areaY, areaWidth = 1, areaHeight = 1;

        public void SetArea(DisplayInfo display)
        {
            areaX = display.X;
            areaY = display.Y;
            areaWidth = Math.Max(1, display.Width);
            areaHeight = Math.Max(1, display.Height);
        }

        public void Handle(string line)
        {
            string[] p = line.Split(' ');
            switch (p[0])
            {
                case "M": Move(Int(p, 1), Int(p, 2)); break;
                case "B": Button(Int(p, 1), Int(p, 2) == 1, Int(p, 3), Int(p, 4), Int(p, 5)); break;
                case "W": Wheel(Int(p, 1), Int(p, 2), Int(p, 3)); break;
                case "K": Key((ushort)Int(p, 1), Int(p, 2) == 1, Int(p, 3), Int(p, 4) == 1); break;
                case "C": Character(Int(p, 1), Int(p, 2) == 1, Int(p, 3)); break;
                case "T": Text(p.Length > 1 ? p[1] : ""); break;
                case "S": Sync(Int(p, 1)); break;
                case "R": ReleaseAll(); break;
            }
        }

        public void ReleaseAll()
        {
            foreach (var key in pressedKeys.ToList()) SendKey(key.Key, false, key.Value);
            pressedKeys.Clear();
            foreach (int button in pressedButtons.ToList()) SendButton(button, false);
            pressedButtons.Clear();
            ApplyModifiers(0);
        }

        static int Int(string[] parts, int index)
        {
            int value;
            return index < parts.Length && int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
        }

        void Move(int x, int y)
        {
            double px = areaX + x / 65535.0 * (areaWidth - 1);
            double py = areaY + y / 65535.0 * (areaHeight - 1);
            int vx = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
            int vy = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
            int vw = Math.Max(2, Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN));
            int vh = Math.Max(2, Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));
            int nx = (int)Math.Round((px - vx) * 65535.0 / (vw - 1));
            int ny = (int)Math.Round((py - vy) * 65535.0 / (vh - 1));
            Send(Mouse(nx, ny, 0, Native.MOUSEEVENTF_MOVE | Native.MOUSEEVENTF_ABSOLUTE | Native.MOUSEEVENTF_VIRTUALDESK));
        }

        void Button(int button, bool down, int x, int y, int mods)
        {
            Move(x, y);
            if (down) ApplyModifiers(mods);
            SendButton(button, down);
            if (down) pressedButtons.Add(button);
            else pressedButtons.Remove(button);
        }

        static void SendButton(int button, bool down)
        {
            uint flags;
            uint data = 0;
            switch (button)
            {
                case 1: flags = down ? Native.MOUSEEVENTF_LEFTDOWN : Native.MOUSEEVENTF_LEFTUP; break;
                case 2: flags = down ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_RIGHTUP; break;
                case 3: flags = down ? Native.MOUSEEVENTF_MIDDLEDOWN : Native.MOUSEEVENTF_MIDDLEUP; break;
                case 4: flags = down ? Native.MOUSEEVENTF_XDOWN : Native.MOUSEEVENTF_XUP; data = 1; break;
                case 5: flags = down ? Native.MOUSEEVENTF_XDOWN : Native.MOUSEEVENTF_XUP; data = 2; break;
                default: return;
            }
            Send(Mouse(0, 0, data, flags));
        }

        void Wheel(int dy, int dx, int mods)
        {
            /* only Shift/Ctrl matter for the wheel (Ctrl+wheel = zoom); never press Alt/Win here */
            ApplyModifiers((mods & (Shift | Ctrl)) | (held & mods & (Alt | Win)));
            if (dy != 0) Send(Mouse(0, 0, unchecked((uint)dy), Native.MOUSEEVENTF_WHEEL));
            if (dx != 0) Send(Mouse(0, 0, unchecked((uint)dx), Native.MOUSEEVENTF_HWHEEL));
        }

        void Key(ushort vk, bool down, int mods, bool extended)
        {
            if (down) ApplyModifiers(mods);
            SendKey(vk, down, extended);
            if (down) pressedKeys[vk] = extended;
            else pressedKeys.Remove(vk);
        }

        void Character(int codepoint, bool down, int mods)
        {
            if (codepoint <= 0 || codepoint > 0xFFFF) return;
            IntPtr layout = Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero));
            short scan = Native.VkKeyScanEx((char)codepoint, layout);
            if (scan == -1) return;
            Key((ushort)(scan & 0xFF), down, mods, false);
        }

        void Text(string hex)
        {
            ApplyModifiers(0);
            var inputs = new List<Native.INPUT>();
            for (int i = 0; i + 4 <= hex.Length; i += 4)
            {
                ushort unit;
                if (!ushort.TryParse(hex.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out unit)) continue;
                inputs.Add(Keyboard(0, unit, Native.KEYEVENTF_UNICODE));
                inputs.Add(Keyboard(0, unit, Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP));
            }
            if (inputs.Count > 0) Send(inputs.ToArray());
        }

        void Sync(int mods)
        {
            /* Shift and Ctrl follow the Mac directly; Alt and Win may only be released here */
            ApplyModifiers((mods & (Shift | Ctrl)) | (held & mods & (Alt | Win)));
        }

        void ApplyModifiers(int wanted)
        {
            for (int bit = 0; bit < 4; bit++)
            {
                int mask = 1 << bit;
                bool isDown = (held & mask) != 0;
                bool shouldBeDown = (wanted & mask) != 0;
                if (isDown == shouldBeDown) continue;
                SendKey(ModifierKeys[bit], shouldBeDown, bit == 3);
                if (shouldBeDown) held |= mask;
                else held &= ~mask;
            }
        }

        static void SendKey(ushort vk, bool down, bool extended)
        {
            ushort scan = (ushort)Native.MapVirtualKey(vk, 0);
            uint flags = (down ? 0u : Native.KEYEVENTF_KEYUP) | (extended ? Native.KEYEVENTF_EXTENDEDKEY : 0u);
            Send(Keyboard(vk, scan, flags));
        }

        static Native.INPUT Mouse(int dx, int dy, uint data, uint flags)
        {
            var input = new Native.INPUT();
            input.type = Native.INPUT_MOUSE;
            input.u.mi.dx = dx;
            input.u.mi.dy = dy;
            input.u.mi.mouseData = data;
            input.u.mi.dwFlags = flags;
            return input;
        }

        static Native.INPUT Keyboard(ushort vk, ushort scan, uint flags)
        {
            var input = new Native.INPUT();
            input.type = Native.INPUT_KEYBOARD;
            input.u.ki.wVk = vk;
            input.u.ki.wScan = scan;
            input.u.ki.dwFlags = flags;
            return input;
        }

        static void Send(params Native.INPUT[] inputs)
        {
            Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        }
    }
}
