using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace ImacDisplay
{
    internal sealed class DisplayInfo
    {
        public string GdiName;
        public bool Internal;
        public int X, Y, Width, Height, Refresh, ScalePercent;
        public Native.LUID Adapter;
        public uint SourceId;

        public override string ToString()
        {
            return string.Format("{0} ({1}) {2}x{3} @ {4} Hz bei ({5},{6}), Skalierung {7} %",
                GdiName, Internal ? "intern" : "extern", Width, Height, Refresh, X, Y, ScalePercent);
        }
    }

    /*
     Switches between "laptop panel only" and an extended desktop on the external (dummy) display.
     Display settings are per user, so none of this needs admin rights.
     */
    internal static class Displays
    {
        const uint SDC_TOPOLOGY_INTERNAL = 0x1, SDC_TOPOLOGY_EXTEND = 0x4, SDC_TOPOLOGY_EXTERNAL = 0x8, SDC_APPLY = 0x80;
        const uint QDC_ALL_PATHS = 0x1, QDC_ONLY_ACTIVE_PATHS = 0x2;
        const int OUTPUT_TECHNOLOGY_INTERNAL = unchecked((int)0x80000000);
        const int DM_POSITION = 0x20, DM_PELSWIDTH = 0x80000, DM_PELSHEIGHT = 0x100000, DM_DISPLAYFREQUENCY = 0x400000;
        const uint CDS_UPDATEREGISTRY = 0x1, CDS_SET_PRIMARY = 0x10, CDS_NORESET = 0x10000000;

        /* Windows' DPI scaling steps; the undocumented DPI API works with indices relative to the recommended step */
        static readonly int[] Scales = { 100, 125, 150, 175, 200, 225, 250, 300, 350, 400, 450, 500 };

        public static List<DisplayInfo> Active()
        {
            uint numPaths, numModes;
            if (Native.GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out numPaths, out numModes) != 0)
                throw new InvalidOperationException("GetDisplayConfigBufferSizes fehlgeschlagen");
            var paths = new Native.PATH_INFO[numPaths];
            var modes = new Native.MODE_INFO[numModes];
            if (Native.QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero) != 0)
                throw new InvalidOperationException("QueryDisplayConfig fehlgeschlagen");

            var result = new List<DisplayInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < numPaths; i++)
            {
                var name = new Native.SOURCE_NAME();
                name.header.type = 1;  // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
                name.header.size = (uint)Marshal.SizeOf(typeof(Native.SOURCE_NAME));
                name.header.adapterId = paths[i].sourceInfo.adapterId;
                name.header.id = paths[i].sourceInfo.id;
                if (Native.DisplayConfigGetDeviceInfo(ref name) != 0 || !seen.Add(name.viewGdiDeviceName)) continue;

                var display = new DisplayInfo();
                display.GdiName = name.viewGdiDeviceName;
                display.Internal = paths[i].targetInfo.outputTechnology == OUTPUT_TECHNOLOGY_INTERNAL;
                display.Adapter = paths[i].sourceInfo.adapterId;
                display.SourceId = paths[i].sourceInfo.id;
                var mode = NewDevMode();
                if (Native.EnumDisplaySettings(display.GdiName, -1, ref mode))
                {
                    display.X = mode.dmPositionX;
                    display.Y = mode.dmPositionY;
                    display.Width = mode.dmPelsWidth;
                    display.Height = mode.dmPelsHeight;
                    display.Refresh = mode.dmDisplayFrequency;
                }
                display.ScalePercent = GetScale(display);
                result.Add(display);
            }
            return result;
        }

        public static DisplayInfo External()
        {
            foreach (var display in Active()) if (!display.Internal) return display;
            return null;
        }

        public static DisplayInfo Internal()
        {
            foreach (var display in Active()) if (display.Internal) return display;
            return null;
        }

        public static bool InternalActive()
        {
            return Internal() != null;
        }

        /* True if an external display (the HDMI dummy) is plugged in, even while Windows does not use it */
        public static bool ExternalConnected()
        {
            uint numPaths, numModes;
            if (Native.GetDisplayConfigBufferSizes(QDC_ALL_PATHS, out numPaths, out numModes) != 0) return false;
            var paths = new Native.PATH_INFO[numPaths];
            var modes = new Native.MODE_INFO[numModes];
            if (Native.QueryDisplayConfig(QDC_ALL_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero) != 0) return false;
            for (int i = 0; i < numPaths; i++)
            {
                if (paths[i].targetInfo.targetAvailable != 0 && paths[i].targetInfo.outputTechnology != OUTPUT_TECHNOLOGY_INTERNAL)
                    return true;
            }
            return false;
        }

        public static void InternalOnly()
        {
            SetTopology(SDC_TOPOLOGY_INTERNAL);
        }

        /*
         Extends the desktop onto the external display and applies resolution, primary display and scaling.
         SDC_TOPOLOGY_EXTEND restores the arrangement Windows remembers for the extended desktop, i.e. the one
         last chosen under Settings > System > Display; Arrange keeps it. The mode is chosen once the external
         display is active: only then does it list its modes.
         */
        public static DisplayInfo Extend(Func<DisplayInfo, Size> mode, int refresh, int scale, bool externalPrimary)
        {
            SetTopology(SDC_TOPOLOGY_EXTEND);
            Thread.Sleep(1500);
            DisplayInfo internalDisplay = Internal();
            DisplayInfo externalDisplay = External();
            if (externalDisplay == null) return null;
            if (internalDisplay != null)
            {
                Size size = mode(externalDisplay);
                Arrange(internalDisplay, externalDisplay, size.Width, size.Height, refresh, externalPrimary);
                Thread.Sleep(1000);
                externalDisplay = External();
            }
            if (externalDisplay != null && externalDisplay.ScalePercent != scale)
            {
                SetScale(externalDisplay, scale);
                Thread.Sleep(500);
                externalDisplay = External();
            }
            return externalDisplay;
        }

        /* Lid closed: the external display alone, as primary, with the requested mode and scaling */
        public static DisplayInfo ExternalOnly(Func<DisplayInfo, Size> mode, int refresh, int scale)
        {
            /* when the lid closes, Windows usually drops the panel by itself; only switch if it is still active */
            if (Internal() != null)
            {
                SetTopology(SDC_TOPOLOGY_EXTERNAL);
                Thread.Sleep(1500);
            }
            DisplayInfo external = External();
            if (external == null) return null;
            Size size = mode(external);
            return EnsureMode(external, size.Width, size.Height, refresh, scale);
        }

        /* The display's sizes at the given refresh rate that H.264 can carry (at most 4096 x 2304), largest first */
        public static List<Size> Modes(string gdiName, int refresh)
        {
            var result = new List<Size>();
            for (int i = 0; ; i++)
            {
                var mode = NewDevMode();
                if (!Native.EnumDisplaySettings(gdiName, i, ref mode)) break;
                var size = new Size(mode.dmPelsWidth, mode.dmPelsHeight);
                if (mode.dmBitsPerPel == 32 && mode.dmDisplayFrequency == refresh && size.Width <= 4096 && size.Height <= 2304
                    && !result.Contains(size)) result.Add(size);
            }
            result.Sort((a, b) => (b.Width * b.Height).CompareTo(a.Width * a.Height));
            return result;
        }

        /*
         The mode that fills a screen best: shown as large as possible without distortion, it covers the most of
         the screen; among nearly equal ones the largest, for the sharpest picture. Enlarged more than 1.5 times, a
         mode gets blurry and does not count (else a 21:9 screen would get 1280 x 600); screens beyond 6K, where all
         modes would be, get the largest. A 16:9 iMac gets 3840 x 2160, a 16:10 MacBook 2560 x 1600 (the FUERAN dummy
         offers neither 2880 x 1800 nor 3072 x 1920). Size.Empty if there are no modes.
         */
        public static Size BestFit(List<Size> modes, Size screen)
        {
            var sharp = modes.FindAll(mode => Factor(mode, screen) <= 1.5);
            if (sharp.Count == 0) return Largest(modes, 0, screen);
            double best = 0;
            foreach (Size mode in sharp) best = Math.Max(best, Coverage(mode, screen));
            return Largest(sharp, best - 0.02, screen);
        }

        /* The largest of the modes that cover at least the given share of the screen */
        static Size Largest(List<Size> modes, double coverage, Size screen)
        {
            Size chosen = Size.Empty;
            foreach (Size mode in modes)
            {
                if (Coverage(mode, screen) >= coverage && mode.Width * mode.Height > chosen.Width * chosen.Height) chosen = mode;
            }
            return chosen;
        }

        /* How much the mode is enlarged (above 1) or reduced to fit the screen */
        static double Factor(Size mode, Size screen)
        {
            return Math.Min((double)screen.Width / mode.Width, (double)screen.Height / mode.Height);
        }

        /* The share of the screen the mode covers, scaled to fit */
        static double Coverage(Size mode, Size screen)
        {
            double factor = Factor(mode, screen);
            return mode.Width * factor * mode.Height * factor / ((double)screen.Width * screen.Height);
        }

        /* Re-applies resolution and scaling if Windows changed them, e.g. after the lid was closed */
        public static DisplayInfo EnsureMode(DisplayInfo external, int width, int height, int refresh, int scale)
        {
            if (external.Width != width || external.Height != height)
            {
                DisplayInfo internalDisplay = Internal();
                if (internalDisplay != null)
                {
                    /* extended desktop: keep the arrangement and the primary display (the one at 0,0) */
                    Arrange(internalDisplay, external, width, height, refresh, external.X == 0 && external.Y == 0);
                }
                else
                {
                    var mode = NewDevMode();
                    mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
                    mode.dmPelsWidth = width;
                    mode.dmPelsHeight = height;
                    mode.dmDisplayFrequency = refresh;
                    Check(Native.ChangeDisplaySettingsEx(external.GdiName, ref mode, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero), "Auflösung");
                }
                Thread.Sleep(1000);
                external = External();
                if (external == null) return null;
            }
            if (external.ScalePercent != scale)
            {
                SetScale(external, scale);
                Thread.Sleep(500);
                external = External();
            }
            return external;
        }

        /* Compact description of all active displays, for the log */
        public static string Describe()
        {
            var parts = new List<string>();
            foreach (var display in Active()) parts.Add(display.ToString());
            return parts.Count == 0 ? "(keine)" : string.Join(" | ", parts);
        }

        static void SetTopology(uint topology)
        {
            int rc = Native.SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, SDC_APPLY | topology);
            if (rc != 0) throw new InvalidOperationException("SetDisplayConfig fehlgeschlagen: " + rc);
        }

        /*
         Gives the external display the requested mode and puts the requested primary display at (0,0), keeping
         the two displays where they are relative to each other. If the external display changes size, its edge
         facing the laptop panel stays in place, so the two still touch.
         */
        static void Arrange(DisplayInfo internalDisplay, DisplayInfo external, int width, int height, int refresh, bool externalPrimary)
        {
            /* top-left corner of the external display relative to the panel's, at the new size */
            int dx = external.X - internalDisplay.X, dy = external.Y - internalDisplay.Y;
            if (external.X + external.Width <= internalDisplay.X) dx -= width - external.Width;  // left of the panel
            if (external.Y + external.Height <= internalDisplay.Y) dy -= height - external.Height;  // above the panel
            DisplayInfo primary = externalPrimary ? external : internalDisplay;
            DisplayInfo secondary = externalPrimary ? internalDisplay : external;

            var mode = NewDevMode();
            mode.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
            mode.dmPelsWidth = width;
            mode.dmPelsHeight = height;
            mode.dmDisplayFrequency = refresh;
            Check(Native.ChangeDisplaySettingsEx(external.GdiName, ref mode, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero), "Auflösung");

            var first = NewDevMode();
            first.dmFields = DM_POSITION;
            Check(Native.ChangeDisplaySettingsEx(primary.GdiName, ref first, IntPtr.Zero, CDS_SET_PRIMARY | CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero), "Hauptbildschirm");

            var second = NewDevMode();
            second.dmFields = DM_POSITION;
            second.dmPositionX = externalPrimary ? -dx : dx;
            second.dmPositionY = externalPrimary ? -dy : dy;
            Check(Native.ChangeDisplaySettingsEx(secondary.GdiName, ref second, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero), "Anordnung");

            Check(Native.ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero), "Übernehmen");
        }

        static void Check(int rc, string step)
        {
            if (rc != 0) throw new InvalidOperationException("Anzeige-Einstellung (" + step + ") fehlgeschlagen: " + rc);
        }

        static Native.DEVMODE NewDevMode()
        {
            var mode = new Native.DEVMODE();
            mode.dmSize = (short)Marshal.SizeOf(typeof(Native.DEVMODE));
            return mode;
        }

        static int GetScale(DisplayInfo display)
        {
            var request = new Native.DPI_GET();
            request.header.type = -3;  // undocumented: get DPI scale
            request.header.size = (uint)Marshal.SizeOf(typeof(Native.DPI_GET));
            request.header.adapterId = display.Adapter;
            request.header.id = display.SourceId;
            if (Native.DisplayConfigGetDeviceInfo(ref request) != 0) return -1;
            int index = -request.minScaleRel + request.curScaleRel;
            return index >= 0 && index < Scales.Length ? Scales[index] : -1;
        }

        static void SetScale(DisplayInfo display, int percent)
        {
            int target = Array.IndexOf(Scales, percent);
            if (target < 0) return;
            var current = new Native.DPI_GET();
            current.header.type = -3;
            current.header.size = (uint)Marshal.SizeOf(typeof(Native.DPI_GET));
            current.header.adapterId = display.Adapter;
            current.header.id = display.SourceId;
            if (Native.DisplayConfigGetDeviceInfo(ref current) != 0) return;
            int relative = Math.Max(current.minScaleRel, Math.Min(current.maxScaleRel, target + current.minScaleRel));

            var request = new Native.DPI_SET();
            request.header.type = -4;  // undocumented: set DPI scale
            request.header.size = (uint)Marshal.SizeOf(typeof(Native.DPI_SET));
            request.header.adapterId = display.Adapter;
            request.header.id = display.SourceId;
            request.scaleRel = relative;
            Native.DisplayConfigSetDeviceInfo(ref request);
        }

        /* ---- DXGI: which output index does ddagrab use for a given display? ---- */

        [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDXGIFactory1
        {
            void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
            void EnumAdapters(); void MakeWindowAssociation(); void GetWindowAssociation(); void CreateSwapChain(); void CreateSoftwareAdapter();
            [PreserveSig] int EnumAdapters1(uint index, [MarshalAs(UnmanagedType.Interface)] out IDXGIAdapter1 adapter);
        }

        [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDXGIAdapter1
        {
            void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
            [PreserveSig] int EnumOutputs(uint index, [MarshalAs(UnmanagedType.Interface)] out IDXGIOutput output);
        }

        [ComImport, Guid("ae02eedb-c735-4690-8d52-5a8dc20213aa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDXGIOutput
        {
            void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();
            [PreserveSig] int GetDesc(out DXGI_OUTPUT_DESC desc);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DXGI_OUTPUT_DESC
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            public int Left, Top, Right, Bottom;
            public int AttachedToDesktop;
            public int Rotation;
            public IntPtr Monitor;
        }

        [DllImport("dxgi.dll")]
        static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

        /* ddagrab enumerates the outputs of the default adapter; returns -1 if the display is not found */
        public static int DxgiOutputIndex(string gdiName)
        {
            Guid iid = typeof(IDXGIFactory1).GUID;
            IntPtr pointer;
            if (CreateDXGIFactory1(ref iid, out pointer) != 0) return -1;
            var factory = (IDXGIFactory1)Marshal.GetObjectForIUnknown(pointer);
            Marshal.Release(pointer);
            try
            {
                IDXGIAdapter1 adapter;
                if (factory.EnumAdapters1(0, out adapter) != 0) return -1;
                try
                {
                    for (uint i = 0; i < 16; i++)
                    {
                        IDXGIOutput output;
                        if (adapter.EnumOutputs(i, out output) != 0) return -1;
                        DXGI_OUTPUT_DESC desc;
                        int hr = output.GetDesc(out desc);
                        Marshal.ReleaseComObject(output);
                        if (hr == 0 && string.Equals(desc.DeviceName, gdiName, StringComparison.OrdinalIgnoreCase)) return (int)i;
                    }
                    return -1;
                }
                finally { Marshal.ReleaseComObject(adapter); }
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
    }
}
