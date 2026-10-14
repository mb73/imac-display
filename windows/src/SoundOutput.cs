using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace ImacDisplay
{
    /*
     The HDMI dummy plays no sound, yet Windows makes its sound output the default once the agent switches it on (seen
     2026-10-14 for all three roles: "4k60-1080@240 (HD Audio Driver for Display Audio)"), and the laptop stays silent as
     long as the Mac display is on. While the agent uses the dummy, a look once a second puts the sound back: on the
     output that was the default before, else on headphones or the speakers. The dummy's sound output shares its device
     container (Displays.ExternalContainer); another display's, e.g. a monitor with speakers, stays as it is. Setting the
     default takes IPolicyConfig, which Windows' own sound settings use: undocumented, unchanged since Windows 7, and open
     to a standard user (measured 2026-10-14).
     */
    internal static class SoundOutput
    {
        /* eRender, DEVICE_STATE_ACTIVE, STGM_READ; ERole runs from eConsole (0) over eMultimedia to eCommunications (2) */
        const int Render = 0, StateActive = 1, StgmRead = 0, Roles = 3;
        /* EndpointFormFactor */
        const int Speakers = 1, Headphones = 3, Headset = 5, DigitalAudioDisplayDevice = 9;

        static readonly PropertyKey PKEY_Device_FriendlyName = new PropertyKey("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
        static readonly PropertyKey PKEY_AudioEndpoint_FormFactor = new PropertyKey("1da5d803-d492-4edd-8c23-e0c0ffee7f0e", 0);
        static readonly PropertyKey PKEY_Device_ContainerId = new PropertyKey("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c", 2);

        /* per role, the last default output that was not the dummy's */
        static readonly string[] home = new string[Roles];
        static Func<bool> dummyInUse;
        static Timer timer;
        /* Windows refused to switch: the next try */
        static DateTime retry;
        static string lastProblem;

        /* Looks once a second from now on; inUse: the agent has the Mac display (the dummy) switched on */
        public static void Watch(Func<bool> inUse)
        {
            dummyInUse = inUse;
            timer = new Timer(Tick, null, 1000, Timeout.Infinite);
        }

        /* For the diagnosis: where Windows sends the sound right now */
        public static string Describe()
        {
            try
            {
                var devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                try
                {
                    Endpoint current = Default(devices, 0);
                    if (current == null) return "kein Ausgang";
                    Guid? dummy = Displays.ExternalContainer();
                    bool onDummy = current.FormFactor == DigitalAudioDisplayDevice && dummy.HasValue && current.Container == dummy.Value;
                    return "„" + current.Name + "“" + (onDummy ? " – das ist der HDMI-Dummy-Stecker, er spielt nichts ab" : "");
                }
                finally { Marshal.ReleaseComObject(devices); }
            }
            catch (COMException ex) { return "unbekannt (" + ex.Message + ")"; }
            catch (InvalidCastException ex) { return "unbekannt (" + ex.Message + ")"; }
        }

        /* one look at a time: the next one is due a second after this one */
        static void Tick(object state)
        {
            try { Check(); }
            catch (Exception ex) { Problem("Den Ton-Ausgang konnte ich nicht prüfen: " + ex.Message); }  // a timer thread must not take the program down
            timer.Change(1000, Timeout.Infinite);
        }

        static void Check()
        {
            bool inUse = dummyInUse();
            var devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            try
            {
                Guid? dummy = null;
                IPolicyConfig policy = null;
                string from = null;
                var to = new List<string>();
                for (int role = 0; role < Roles; role++)
                {
                    Endpoint current = Default(devices, role);
                    if (current == null) continue;
                    /* only a display's sound output may be the dummy's; which display that is, the agent looks up just then */
                    if (current.FormFactor != DigitalAudioDisplayDevice)
                    {
                        home[role] = current.Id;
                        continue;
                    }
                    if (!inUse) continue;
                    if (dummy == null) dummy = Displays.ExternalContainer();
                    if (dummy == null) continue;
                    if (current.Container != dummy.Value)
                    {
                        home[role] = current.Id;
                        continue;
                    }
                    if (DateTime.UtcNow < retry) continue;
                    Endpoint target = Replacement(devices, home[role], dummy.Value);
                    if (target == null)
                    {
                        Problem("Windows gibt den Ton über den HDMI-Dummy-Stecker aus, der nichts abspielt, und hat gerade keinen anderen Ausgang.");
                        continue;
                    }
                    if (policy == null) policy = (IPolicyConfig)new PolicyConfigClient();
                    int hr = policy.SetDefaultEndpoint(target.Id, role);
                    if (hr != 0)
                    {
                        retry = DateTime.UtcNow.AddSeconds(30);
                        Problem("Windows gibt den Ton über den HDMI-Dummy-Stecker aus, der nichts abspielt. Zurück auf „" + target.Name
                            + "“ ging nicht (Fehler 0x" + hr.ToString("X8") + "). Abhilfe: in den Windows-Einstellungen unter System → Sound einen anderen Ausgang wählen.");
                        break;
                    }
                    from = current.Name;
                    if (!to.Contains(target.Name)) to.Add(target.Name);
                }
                if (policy != null) Marshal.ReleaseComObject(policy);
                if (to.Count == 0) return;
                lastProblem = null;
                Program.Log("Windows hat den Ton auf den HDMI-Dummy-Stecker gelegt („" + from + "“), der nichts abspielt. Er kommt wieder aus „"
                    + string.Join("“ und „", to) + "“.");
            }
            finally { Marshal.ReleaseComObject(devices); }
        }

        /* Where the sound goes instead: the output that was the default before, if it is still there, else the likeliest one */
        static Endpoint Replacement(IMMDeviceEnumerator devices, string before, Guid dummy)
        {
            IMMDevice device;
            if (before != null && devices.GetDevice(before, out device) == 0)
            {
                Endpoint previous = Endpoint.Of(device);
                if (previous.State == StateActive && previous.Container != dummy) return previous;
            }
            IMMDeviceCollection active;
            if (devices.EnumAudioEndpoints(Render, StateActive, out active) != 0) return null;
            Endpoint best = null;
            try
            {
                int count;
                active.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    if (active.Item(i, out device) != 0) continue;
                    Endpoint candidate = Endpoint.Of(device);
                    if (candidate.Container != dummy && (best == null || Rank(candidate) < Rank(best))) best = candidate;
                }
            }
            finally { Marshal.ReleaseComObject(active); }
            return best;
        }

        /* headphones plugged in are meant to play, then the speakers; another display's sound last */
        static int Rank(Endpoint endpoint)
        {
            if (endpoint.FormFactor == Headphones || endpoint.FormFactor == Headset) return 0;
            if (endpoint.FormFactor == Speakers) return 1;
            return endpoint.FormFactor == DigitalAudioDisplayDevice ? 3 : 2;
        }

        /* null without any active output (E_NOTFOUND) */
        static Endpoint Default(IMMDeviceEnumerator devices, int role)
        {
            IMMDevice device;
            return devices.GetDefaultAudioEndpoint(Render, role, out device) == 0 ? Endpoint.Of(device) : null;
        }

        /* once per problem, not every second */
        static void Problem(string message)
        {
            if (message == lastProblem) return;
            lastProblem = message;
            Program.Log(message);
        }

        sealed class Endpoint
        {
            public string Id, Name;
            public int State, FormFactor = -1;
            public Guid Container;

            /* reads what matters here and lets go of the device */
            public static Endpoint Of(IMMDevice device)
            {
                var endpoint = new Endpoint();
                try
                {
                    device.GetId(out endpoint.Id);
                    device.GetState(out endpoint.State);
                    IPropertyStore properties;
                    if (device.OpenPropertyStore(StgmRead, out properties) == 0)
                    {
                        endpoint.Name = Property(properties, PKEY_Device_FriendlyName) as string;
                        object form = Property(properties, PKEY_AudioEndpoint_FormFactor);
                        if (form is uint) endpoint.FormFactor = (int)(uint)form;
                        object container = Property(properties, PKEY_Device_ContainerId);
                        if (container is Guid) endpoint.Container = (Guid)container;
                        Marshal.ReleaseComObject(properties);
                    }
                }
                finally { Marshal.ReleaseComObject(device); }
                if (endpoint.Name == null) endpoint.Name = endpoint.Id;
                return endpoint;
            }
        }

        /* a number (VT_UI4), a string (VT_LPWSTR) or a GUID (VT_CLSID); null for anything else */
        static object Property(IPropertyStore properties, PropertyKey key)
        {
            PropVariant value;
            if (properties.GetValue(ref key, out value) != 0) return null;
            try
            {
                switch (value.Type)
                {
                    case 19: return (uint)value.Data.ToInt64();
                    case 31: return Marshal.PtrToStringUni(value.Data);
                    case 72: return (Guid)Marshal.PtrToStructure(value.Data, typeof(Guid));
                    default: return null;
                }
            }
            finally { PropVariantClear(ref value); }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PropertyKey
        {
            public Guid Format;
            public int Id;

            public PropertyKey(string format, int id)
            {
                Format = new Guid(format);
                Id = id;
            }
        }

        /* a PROPVARIANT: its type, three reserved words and a union of 16 bytes (x64) */
        [StructLayout(LayoutKind.Sequential)]
        struct PropVariant
        {
            public ushort Type, Reserved1, Reserved2, Reserved3;
            public IntPtr Data, Data2;
        }

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropVariant value);

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumerator { }

        /* up to GetDevice, in vtable order */
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, out IntPtr result);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore properties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        /* up to GetValue, in vtable order */
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        }

        /* undocumented; only SetDefaultEndpoint is called, the methods before it just hold their places in the vtable */
        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat();
            [PreserveSig] int GetDeviceFormat();
            [PreserveSig] int ResetDeviceFormat();
            [PreserveSig] int SetDeviceFormat();
            [PreserveSig] int GetProcessingPeriod();
            [PreserveSig] int SetProcessingPeriod();
            [PreserveSig] int GetShareMode();
            [PreserveSig] int SetShareMode();
            [PreserveSig] int GetPropertyValue();
            [PreserveSig] int SetPropertyValue();
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        }

        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        class PolicyConfigClient { }
    }
}
