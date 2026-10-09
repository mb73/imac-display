using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ImacDisplay
{
    /*
     "Wenn ich den Deckel schließe, wird mein PC" from Settings > System > Power & battery, plugged in and on battery,
     in the active power plan. A standard user may change it: the plans' security descriptor grants Users read and
     write access, and activating the plan again makes Windows apply the change at once.
     */
    internal static class LidAction
    {
        public const int Nothing = 0, Sleep = 1, Hibernate = 2, ShutDown = 3;

        static readonly Guid Buttons = new Guid("4f971e89-eebd-4455-a8de-9e59040e7347");  // GUID_SYSTEM_BUTTON_SUBGROUP
        static readonly Guid LidClose = new Guid("5ca83367-6e45-459f-a27b-476b1d01c936");  // GUID_LIDCLOSE_ACTION

        /* The names Windows 11 gives the choices */
        public static string Name(int action)
        {
            switch (action)
            {
                case Nothing: return "Keine Aktion ausführen";
                case Sleep: return "Standbymodus";
                case Hibernate: return "Ruhezustand";
                case ShutDown: return "Herunterfahren";
                default: return "Unbekannt (" + action + ")";
            }
        }

        /* The laptop has a lid; Windows offers the setting only then */
        public static bool LidPresent
        {
            get
            {
                byte[] capabilities = Capabilities();
                return capabilities == null || capabilities[2] != 0;
            }
        }

        public static bool BatteryPresent
        {
            get
            {
                byte[] capabilities = Capabilities();
                return capabilities == null || capabilities[30] != 0;
            }
        }

        /* Hibernation is switched on: only then does Windows offer "Ruhezustand" */
        public static bool HibernateAvailable
        {
            get
            {
                byte[] capabilities = Capabilities();
                return capabilities != null && capabilities[6] != 0 && capabilities[8] != 0;
            }
        }

        /* The action plugged in or on battery; -1 if Windows does not tell */
        public static int Read(bool battery)
        {
            Guid scheme;
            if (!ActiveScheme(out scheme)) return -1;
            Guid group = Buttons, setting = LidClose;
            uint value;
            uint rc = battery
                ? Native.PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref group, ref setting, out value)
                : Native.PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref group, ref setting, out value);
            return rc == 0 ? (int)value : -1;
        }

        /* Sets the action plugged in or on battery; null if Windows took it, else why not */
        public static string Write(bool battery, int action)
        {
            Guid scheme;
            if (!ActiveScheme(out scheme)) return "Windows nennt keinen aktiven Energiesparplan.";
            Guid group = Buttons, setting = LidClose;
            uint rc = battery
                ? Native.PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref group, ref setting, (uint)action)
                : Native.PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref group, ref setting, (uint)action);
            if (rc == 0) rc = Native.PowerSetActiveScheme(IntPtr.Zero, ref scheme);
            if (rc != 0) return new Win32Exception((int)rc).Message;
            /* a policy of the company may decide instead */
            if (Read(battery) != action) return "Windows hat die Einstellung nicht übernommen, vielleicht gibt die Firma sie vor.";
            return null;
        }

        static bool ActiveScheme(out Guid scheme)
        {
            scheme = Guid.Empty;
            IntPtr pointer;
            if (Native.PowerGetActiveScheme(IntPtr.Zero, out pointer) != 0 || pointer == IntPtr.Zero) return false;
            try { scheme = (Guid)Marshal.PtrToStructure(pointer, typeof(Guid)); }
            finally { Native.LocalFree(pointer); }
            return true;
        }

        /* SYSTEM_POWER_CAPABILITIES; null if Windows does not tell */
        static byte[] Capabilities()
        {
            var capabilities = new byte[76];
            try { return Native.GetPwrCapabilities(capabilities) ? capabilities : null; }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
        }
    }
}
