using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ImacDisplay
{
    internal sealed class Options
    {
        public string Host;
        public int Port = 47101;
        /*
         The size only counts with --size (SizeGiven) or when LaptopScreen does not report its screen: otherwise the
         session picks the mode for the Mac (Program.ChooseMode). 200 %: Windows text is smaller than macOS text at
         the same logical size, 150 % reads too small on a Mac.
         */
        public int Width = 3840, Height = 2160, Refresh = 60, Scale = 200, Fps = 60, Bitrate = 80;
        /* the panel stays the main display while the lid is open: only there does Windows show the sign-in after a lock */
        public bool InternalPrimary = true;
        public bool SizeGiven, Repair, Test, Restore, Update;
        public bool ShareClipboard = true;
        /* "Laptop-Display ausschalten" in the window: with the lid open, too, the Mac display is the only one */
        public bool MacOnly;
        public string Code, UpdateZip;
        /* what a restart after an update passes on; code and clipboard choice are remembered anyway */
        public readonly List<string> Session = new List<string>();

        public const string Usage =
            "Optionen: --host <name|ip> --port <n> --size 3840x2160 --scale 200 --fps 60 --bitrate 80\r\n" +
            "          --code <kopplungscode> --repair --clipboard on|off --mac-primary\r\n" +
            "          --test --restore --update [zip]";

        /* null and the reason if an option is unknown or lacks its value */
        public static Options Parse(string[] args, out string error)
        {
            error = null;
            var o = new Options();
            Settings.Load(o);
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                string next = i + 1 < args.Length ? args[i + 1] : null;
                try
                {
                    switch (a)
                    {
                        case "--host": o.Host = Value(next); o.Keep(a, next); i++; break;
                        case "--port": o.Port = Number(next); o.Keep(a, next); i++; break;
                        case "--size":
                            string[] wh = Value(next).Split('x');
                            if (wh.Length != 2) throw new FormatException();
                            o.Width = Number(wh[0]);
                            o.Height = Number(wh[1]);
                            o.SizeGiven = true;
                            o.Keep(a, next);
                            i++;
                            break;
                        case "--scale": o.Scale = Number(next); o.Keep(a, next); i++; break;
                        case "--fps": o.Fps = Number(next); o.Keep(a, next); i++; break;
                        case "--bitrate": o.Bitrate = Number(next); o.Keep(a, next); i++; break;
                        case "--code": o.Code = Value(next); i++; break;
                        case "--clipboard":
                            /* remembered, so "--clipboard off" once is enough (the window has a switch for it, too) */
                            o.ShareClipboard = !string.Equals(Value(next), "off", StringComparison.OrdinalIgnoreCase);
                            Settings.Save("clipboard", o.ShareClipboard ? 1 : 0);
                            i++;
                            break;
                        case "--internal-primary": o.InternalPrimary = true; break;  // the default since 1.4.0
                        case "--mac-primary": o.InternalPrimary = false; o.Keep(a, null); break;
                        case "--repair": o.Repair = true; break;
                        case "--test": o.Test = true; break;
                        case "--restore": o.Restore = true; break;
                        case "--update":
                            o.Update = true;
                            if (next != null && IsZip(next))
                            {
                                o.UpdateZip = next;
                                i++;
                            }
                            break;
                        default:
                            if (IsZip(args[i]))
                            {
                                /* a zip dropped onto the exe */
                                o.Update = true;
                                o.UpdateZip = args[i];
                                break;
                            }
                            error = "Unbekannte Option: " + args[i];
                            return null;
                    }
                }
                catch (FormatException)
                {
                    error = "Fehlender oder ungültiger Wert für " + args[i] + ".";
                    return null;
                }
            }
            return o;
        }

        void Keep(string option, string value)
        {
            Session.Add(option);
            if (value != null) Session.Add(value);
        }

        static string Value(string value)
        {
            if (value == null) throw new FormatException();
            return value;
        }

        static int Number(string value)
        {
            int number;
            if (value == null || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) throw new FormatException();
            return number;
        }

        static bool IsZip(string path)
        {
            return path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        }
    }

    /*
     Remembered preferences (%APPDATA%\imac-display\settings.txt, lines "key=value"); command-line options win.
     "scale", "clipboard" and "maconly" hold numbers, "size.<Mac screen>" the mode the user chose for that Mac, e.g.
     "size.2880x1800=2560x1600".
     */
    internal static class Settings
    {
        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "imac-display", "settings.txt"); }
        }

        public static void Load(Options o)
        {
            foreach (KeyValuePair<string, string> pair in Read())
            {
                int value;
                if (!int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) continue;
                if (pair.Key == "scale") o.Scale = value;
                else if (pair.Key == "clipboard") o.ShareClipboard = value != 0;
                else if (pair.Key == "maconly") o.MacOnly = value != 0;
            }
        }

        /* null if not set */
        public static string Get(string key)
        {
            string value;
            return Read().TryGetValue(key, out value) ? value : null;
        }

        public static void Save(string key, int value)
        {
            Save(key, value.ToString(CultureInfo.InvariantCulture));
        }

        public static void Save(string key, string value)
        {
            Dictionary<string, string> all = Read();
            all[key] = value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, all.Select(pair => pair.Key + "=" + pair.Value));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static Dictionary<string, string> Read()
        {
            var result = new Dictionary<string, string>();
            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] pair = line.Split(new[] { '=' }, 2);
                    if (pair.Length == 2) result[pair[0].Trim()] = pair[1].Trim();
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return result;
        }
    }

    /* Stores the pairing code in the user profile (%APPDATA%\imac-display) */
    internal static class PairingStore
    {
        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "imac-display", "pairing.txt"); }
        }

        public static string Load()
        {
            try
            {
                string code = File.ReadAllText(FilePath).Trim();
                return code.Length > 0 ? code : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public static void Save(string code)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, code);
        }

        /* At least eight letters or digits, as the Mac shows them (e.g. ABCD-EFGH-JKLM) */
        public static bool LooksValid(string code)
        {
            return code != null && Crypto.Normalize(code).Length >= 8;
        }
    }

    /*
     Whether the agent can see and steer the laptop. The sign-in, UAC prompts and the screen saver run on another desktop than
     "Default". After locking, though, Windows 11 shows its lock screen on "Default" for a while, until someone dismisses it;
     only the session's own flag says it is locked then. In that phase programs may still switch displays.
     */
    internal static class Session
    {
        public static bool IsLocked()
        {
            return SessionLocked() || !InputDesktopIsDefault();
        }

        /* The session's lock flag (WTSINFOEX: Level, then 8-byte aligned SessionId, SessionState, SessionFlags 0 locked, 1 unlocked) */
        public static bool SessionLocked()
        {
            IntPtr buffer;
            int bytes;
            if (!Native.WTSQuerySessionInformation(IntPtr.Zero, Native.WTS_CURRENT_SESSION, Native.WTSSessionInfoEx, out buffer, out bytes)) return false;
            try
            {
                if (bytes < 20 || System.Runtime.InteropServices.Marshal.ReadInt32(buffer, 0) != 1) return false;
                return System.Runtime.InteropServices.Marshal.ReadInt32(buffer, 16) == 0;
            }
            finally { Native.WTSFreeMemory(buffer); }
        }

        /* The input desktop is the user's own: programs reach it with input and may switch displays */
        public static bool InputDesktopIsDefault()
        {
            IntPtr desktop = Native.OpenInputDesktop(0, false, 0x0001);  // DESKTOP_READOBJECTS
            if (desktop == IntPtr.Zero) return false;
            try
            {
                var name = new StringBuilder(256);
                int needed;
                if (!Native.GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out needed)) return true;  // UOI_NAME
                return string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
            }
            finally { Native.CloseDesktop(desktop); }
        }

        /* Seconds since the last input, on the laptop or from the Mac (SendInput counts like a keyboard) */
        public static int IdleSeconds()
        {
            var info = new Native.LASTINPUTINFO();
            info.cbSize = 8;
            if (!Native.GetLastInputInfo(ref info)) return 0;
            return (int)(unchecked((uint)Environment.TickCount - info.dwTime) / 1000);
        }

        /*
         After how many seconds without input Windows locks itself, 0 if never: the screen saver with sign-in on resume
         (a company policy may set it; SystemParametersInfo reports the result) or the machine inactivity limit
         */
        public static int LockTimeout()
        {
            int seconds = 0, active = 0, secure = 0, timeout = 0;
            if (Native.SystemParametersInfo(Native.SPI_GETSCREENSAVEACTIVE, 0, ref active, 0) && active != 0
                && Native.SystemParametersInfo(Native.SPI_GETSCREENSAVESECURE, 0, ref secure, 0) && secure != 0
                && Native.SystemParametersInfo(Native.SPI_GETSCREENSAVETIMEOUT, 0, ref timeout, 0) && timeout > 0
                && ScreenSaverChosen())
                seconds = timeout;
            object limit = Read(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "InactivityTimeoutSecs");
            if (limit is int && (int)limit > 0 && (seconds == 0 || (int)limit < seconds)) seconds = (int)limit;
            return seconds;
        }

        /* "(Kein)" leaves the screen saver switched on, but without a program to run */
        static bool ScreenSaverChosen()
        {
            return !string.IsNullOrEmpty(Read(@"HKEY_CURRENT_USER\Software\Policies\Microsoft\Windows\Control Panel\Desktop", "SCRNSAVE.EXE") as string)
                || !string.IsNullOrEmpty(Read(@"HKEY_CURRENT_USER\Control Panel\Desktop", "SCRNSAVE.EXE") as string);
        }

        static object Read(string key, string name)
        {
            try { return Microsoft.Win32.Registry.GetValue(key, name, null); }
            catch (System.Security.SecurityException) { return null; }
            catch (IOException) { return null; }
        }

        /* A program keeps the display on, e.g. a video or a call: then the screen saver does not start */
        public static bool DisplayHeld()
        {
            uint state;
            return Native.CallNtPowerInformation(16, IntPtr.Zero, 0, out state, 4) == 0 && (state & 0x2) != 0;
        }
    }

    /*
     Keeps Windows from sending the laptop to sleep after a while without input, as long as the picture runs: with
     the lid closed, the user may work on the Mac alone for a while, and only opening the lid would wake the laptop.
     Not while Windows is locked: nobody can work on the laptop from the Mac then, so it may sleep as usual. A power
     request rather than SetThreadExecutionState, so that "powercfg /requests" names the reason. Closing the lid and
     the power button still do what Windows is set to.
     */
    internal sealed class StayAwake : IDisposable
    {
        const string Reason = "iMac-Display überträgt den Bildschirm an den Mac.";
        static readonly IntPtr Invalid = new IntPtr(-1);
        readonly Action<string> log;
        IntPtr request = IntPtr.Zero;
        bool held, failed;

        public StayAwake(Action<string> log)
        {
            this.log = log;
        }

        public void Hold(bool on)
        {
            if (on == held || failed) return;
            if (request == IntPtr.Zero)
            {
                var context = new Native.REASON_CONTEXT
                {
                    Version = 0,
                    Flags = Native.POWER_REQUEST_CONTEXT_SIMPLE_STRING,
                    SimpleReasonString = Reason
                };
                IntPtr created = Native.PowerCreateRequest(ref context);
                if (created == Invalid || created == IntPtr.Zero)
                {
                    Fail();
                    return;
                }
                request = created;
            }
            bool done = on
                ? Native.PowerSetRequest(request, Native.PowerRequestSystemRequired)
                : Native.PowerClearRequest(request, Native.PowerRequestSystemRequired);
            if (done) held = on;
            else Fail();
        }

        /* Windows refused: say so once; the session runs on, the laptop just may fall asleep */
        void Fail()
        {
            failed = true;
            log("Windows lässt den Laptop nicht wach halten: " + new Win32Exception().Message);
        }

        public void Dispose()
        {
            if (request == IntPtr.Zero) return;
            if (held) Native.PowerClearRequest(request, Native.PowerRequestSystemRequired);
            Native.CloseHandle(request);
            request = IntPtr.Zero;
            held = false;
        }
    }

    /*
     Entry point and agent. The window (MainWindow) runs on the main thread; the agent loop runs on its
     own thread: find the Mac, handshake, then RunSession until the connection ends, the agent pauses
     itself (no ffmpeg, no pairing code; "Verbinden" resumes) or the window closes (stopping, also by
     "Trennen und beenden"). It reports to the window with ShowStatus and asks through it (pairing
     code, ffmpeg download).
     */
    internal static class Program
    {
        public static readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string ExePath = Assembly.GetExecutingAssembly().Location;
        public const string GuideUrl = "https://github.com/mb73/imac-display/blob/main/README.md";
        public const string LicenseUrl = "https://github.com/mb73/imac-display/blob/main/LICENSE";

        /* keepDisplay: an update hands the Mac display over to the new version instead of switching it off */
        static volatile bool stopping, paused, keepDisplay, shareClipboard, inSession, macAsleep, cableChecking;
        /* the window's "Laptop-Display ausschalten", and whether the agent switched the panel off with the lid open (Displays.PanelOff) */
        static volatile bool macOnly, panelSwitchedOff;
        /* the Mac on the direct cable, found in the background while a session runs over Wi-Fi */
        static volatile MacEndpoint cableFound;
        /* the Mac display's scaling, chosen in the window or adopted from Windows' settings */
        static volatile int scale;
        /* the Mac display's place next to the laptop panel, chosen in the window: offset of its top-left corner from the panel's */
        static readonly object placementLock = new object();
        static Point? placement;
        /* set by a session that ended to go over to the cable */
        static MacEndpoint switchTo;
        /* the session runs over the laptop's Wi-Fi: the light shows the Wi-Fi symbol instead of the check */
        static bool onWifi;
        /* a session in this run went over the cable: on Wi-Fi the window says the cable is coming */
        static bool cableSeen;
        static Mutex instance;
        static Thread agent;
        static MainWindow window;
        static readonly object cleanupLock = new object();
        static readonly object logLock = new object();
        static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "imac-display", "imac-display.log");
        /* this run's log lines for the log window, and the log window itself while it is open */
        static readonly Queue<string> recent = new Queue<string>();
        static Action<string> logListeners;
        static VideoSender video;
        static LidWatcher lid;
        static bool displayExtended;
        static string lastLogged;
        /* what the window shows while the agent has paused itself, and why */
        static AgentStatus pausedStatus;
        /* LaptopScreen's version in the current session; LaptopScreen before 1.2.0 never reports it */
        static string macVersion;
        static string reportedMacVersion;
        static bool warnedOldMac;
        /* the Mac's screen in pixels, as LaptopScreen 1.6.0 and newer report it; the Mac display's mode chosen for it */
        static Size? macScreen;
        static Size mode;
        static Size? announcedMode;

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            /* per-monitor v2, physical pixels everywhere; WinForms has done it already if imac-display.exe.config is there */
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }

            string error;
            Options options = Options.Parse(args, out error);
            if (options == null)
            {
                Report(error + "\r\n\r\n" + Options.Usage, MessageKind.Warning);
                return 2;
            }
            shareClipboard = options.ShareClipboard;
            macOnly = options.MacOnly;
            scale = options.Scale;
            if (options.Test)
            {
                if (OutputRedirected())
                {
                    using (TextWriter output = StandardOutput()) RunTest(options, output.WriteLine);
                }
                else
                {
                    var text = new StringBuilder();
                    RunTest(options, line => text.AppendLine(line));
                    TextDialog.ShowText("iMac-Display – Diagnose", text.ToString());
                }
                return 0;
            }
            if (options.Restore)
            {
                /* emergency exit: only the laptop panel, e.g. after a crash left the invisible display active */
                Displays.InternalOnly();
                Report("Nur noch der Laptop-Bildschirm ist aktiv.", MessageKind.Information);
                return 0;
            }
            if (RunningFromArchive())
            {
                Report("iMac-Display läuft hier direkt aus der Zip-Datei heraus. Bitte entpacke sie zuerst "
                    + "(Rechtsklick auf die Zip-Datei → „Alle extrahieren …“) und starte imac-display.exe dann aus dem entpackten Ordner.",
                    MessageKind.Warning);
                return 1;
            }
            string restarted = Environment.GetEnvironmentVariable(Updater.RestartedVariable);
            Environment.SetEnvironmentVariable(Updater.RestartedVariable, null);  // neither for ffmpeg nor for a later restart
            if (!AcquireInstance(restarted != null))
            {
                bool shown = ActivateOtherInstance();
                if (options.UpdateZip != null)
                    Report("iMac-Display läuft schon. Beende es und zieh die Zip-Datei dann noch einmal auf imac-display.exe.", MessageKind.Information);
                else if (!shown) Report("iMac-Display läuft schon.", MessageKind.Information);
                return 1;
            }
            Application.ThreadException += OnWindowError;
            AppDomain.CurrentDomain.UnhandledException += OnCrash;
            window = new MainWindow(options, restarted == "display");
            Application.Run(window);
            ReleaseInstance();
            return 0;
        }

        /* ---- what the window uses ---- */

        /* Starts the agent once the window is shown; displayHandedOver: the previous version left the Mac display on */
        public static void StartAgent(Options options, bool displayHandedOver)
        {
            agent = new Thread(delegate () { RunAgent(options, displayHandedOver); });
            agent.IsBackground = true;
            agent.Name = "agent";
            agent.Start();
        }

        public static bool Paused
        {
            get { return paused; }
        }

        /* Connected to the Mac right now (whether or not the picture runs) */
        public static bool InSession
        {
            get { return inSession; }
        }

        /* Lid closed: the Mac display is the laptop's only screen */
        public static bool LidClosed
        {
            get
            {
                LidWatcher watcher = lid;
                return watcher != null && watcher.IsOpen == false;
            }
        }

        /* "Verbinden" after the agent paused itself */
        public static void Resume()
        {
            paused = false;
        }

        /* The window's switch; a running session follows right away */
        public static bool ShareClipboard
        {
            get { return shareClipboard; }
            set
            {
                shareClipboard = value;
                Settings.Save("clipboard", value ? 1 : 0);
            }
        }

        /* The window's "Laptop-Display ausschalten": with the lid open, too, the Mac display is the only one; a running session follows right away */
        public static bool MacOnly
        {
            get { return macOnly; }
            set
            {
                macOnly = value;
                Settings.Save("maconly", value ? 1 : 0);
            }
        }

        /* The window's choice of scaling; a running session applies it right away */
        public static int Scale
        {
            get { return scale; }
            set
            {
                scale = value;
                Settings.Save("scale", value);
            }
        }

        /* The window's arrangement: the Mac display's top-left corner relative to the panel's; a running session applies it right away */
        public static void RequestPlacement(Point offset)
        {
            lock (placementLock) placement = offset;
        }

        static bool PlacementRequested
        {
            get { lock (placementLock) return placement.HasValue; }
        }

        static Point? TakePlacement()
        {
            lock (placementLock)
            {
                Point? requested = placement;
                placement = null;
                return requested;
            }
        }

        /* Ends the agent and stops the video; switches back to the laptop panel unless handOver keeps the display for a restart */
        public static void Shutdown(bool handOver)
        {
            lock (cleanupLock) keepDisplay = handOver && displayExtended;
            stopping = true;
            if (agent != null && !agent.Join(6000)) Log("Das Programm wartet nicht länger auf die Verbindung zum Mac.");
            Cleanup();
        }

        /* After an update: the new version starts in place of this one and takes over the Mac display; false if it did not start */
        public static bool RestartAfterUpdate(Options o)
        {
            Shutdown(true);
            ReleaseInstance();
            try
            {
                Updater.Restart(o.Session, keepDisplay);
                return true;
            }
            catch (Win32Exception ex)
            {
                Log("Die neue Version ließ sich nicht starten: " + ex.Message);
                keepDisplay = false;
                Cleanup();
                return false;
            }
        }

        /* The log window's "Diagnose": the read-only checks of --test, into the log */
        public static void Diagnose(Options o)
        {
            Log("Diagnose:");
            RunTest(o, delegate (string line) { Log("  " + line); });
        }

        public static string LogFile
        {
            get { return LogPath; }
        }

        /* The log window listens from now on; returns this run's lines so far, without gap or overlap */
        public static string[] ListenToLog(Action<string> listener)
        {
            lock (logLock)
            {
                logListeners += listener;
                return recent.ToArray();
            }
        }

        public static void StopListening(Action<string> listener)
        {
            lock (logLock) logListeners -= listener;
        }

        /* ---- the agent ---- */

        static bool Active
        {
            get { return !stopping && !paused; }
        }

        static void RunAgent(Options options, bool displayHandedOver)
        {
            try { Agent(options, displayHandedOver); }
            catch (Exception ex)
            {
                /* a bug: say so, and never leave the invisible display behind */
                Log("Unerwarteter Fehler: " + ex);
                keepDisplay = false;
                Cleanup();
                window.ShowStatus(new AgentStatus(Light.Problem, "Unerwarteter Fehler",
                    ex.Message + " Details stehen im Log. Bitte iMac-Display neu starten.", ThinBar.None));
            }
        }

        static void Agent(Options o, bool displayHandedOver)
        {
            RotateLog();
            Log("iMac-Display " + Updater.Version + " läuft.");
            lid = LidWatcher.Start();
            string ffmpeg = null;
            string code = o.Code ?? (o.Repair ? null : PairingStore.Load());
            if (o.Code != null) PairingStore.Save(o.Code);
            DateTime? keepDisplayUntil = null;
            MacEndpoint preferred = null;
            if (displayHandedOver)
            {
                /* the previous version left the Mac display on: keep it if the Mac comes back soon */
                lock (cleanupLock) displayExtended = true;
                keepDisplayUntil = DateTime.UtcNow.AddSeconds(30);
            }
            while (!stopping)
            {
                if (paused)
                {
                    keepDisplayUntil = null;
                    RevertDisplay("Getrennt: nur noch der Laptop-Bildschirm.");
                    window.ShowStatus(pausedStatus);
                    while (paused && !stopping) Thread.Sleep(100);
                    continue;
                }
                if (keepDisplayUntil.HasValue && DateTime.UtcNow > keepDisplayUntil.Value)
                {
                    keepDisplayUntil = null;
                    RevertDisplay("Der Mac ist nicht zurückgekommen: nur noch der Laptop-Bildschirm.");
                }
                if (ffmpeg == null)
                {
                    ffmpeg = FindFfmpeg() ?? Setup.InstallFfmpeg(window);
                    if (ffmpeg == null)
                    {
                        Pause(new AgentStatus(Light.Problem, "ffmpeg fehlt",
                            "Ohne ffmpeg kann iMac-Display kein Bild übertragen. „Verbinden“ versucht es noch einmal.", ThinBar.None));
                        continue;
                    }
                }
                if (code == null)
                {
                    /* first start: make the program easy to find again */
                    Setup.EnsureStartMenuEntry();
                    code = AskCode(false);
                    if (code == null) continue;
                }

                List<MacEndpoint> endpoints;
                if (preferred != null)
                {
                    endpoints = new List<MacEndpoint> { preferred };
                    preferred = null;
                }
                else endpoints = FindMac(o);
                if (!Active) continue;
                if (endpoints.Count == 0)
                {
                    if (macAsleep)
                    {
                        PanelBack();
                        ShowAsleep();
                        Wait(5000);
                        continue;
                    }
                    LogOnce("Suche den Mac … (läuft dort LaptopScreen?)");
                    window.ShowStatus(new AgentStatus(Light.Busy, "Suche den Mac …",
                        "Läuft dort LaptopScreen? Beide brauchen eine Verbindung per Kabel oder über dasselbe Netz.", ThinBar.None));
                    Wait(3000);
                    continue;
                }
                ControlClient control = null;
                MacEndpoint connected = null;
                string error = null;
                foreach (MacEndpoint endpoint in endpoints)
                {
                    control = ControlClient.Connect(endpoint, code, out error);
                    if (control != null)
                    {
                        connected = endpoint;
                        Log("Verbunden mit " + endpoint);
                        macAsleep = false;
                        break;
                    }
                    if (error == ControlClient.Denied || error == ControlClient.Asleep) break;
                }
                if (control == null)
                {
                    if (error == ControlClient.Asleep)
                    {
                        /* LaptopScreen answers so while the Mac's display sleeps, e.g. in a dark wake for maintenance */
                        if (!macAsleep) Log("Der Mac schläft. Sobald er aufwacht, verbinde ich mich wieder.");
                        macAsleep = true;
                        keepDisplayUntil = null;
                        PanelBack();
                        ShowAsleep();
                        Wait(5000);
                    }
                    else if (error == ControlClient.Denied)
                    {
                        /* the question has to appear on a visible screen */
                        keepDisplayUntil = null;
                        RevertDisplay(null);
                        Log("Der Mac hat den Kopplungscode abgelehnt.");
                        code = AskCode(true);
                    }
                    else
                    {
                        LogOnce("Verbindung zum Mac fehlgeschlagen: " + error);
                        window.ShowStatus(new AgentStatus(Light.Busy, "Suche den Mac …", "Gefunden, aber keine Verbindung: " + error, ThinBar.None));
                        Wait(3000);
                    }
                    continue;
                }
                RunSession(control, connected.Name, ffmpeg, o);
                if (!Active) continue;
                if (macAsleep)
                {
                    /* waking the Mac is up to the user; the display stays as it is meanwhile, only a switched-off panel comes back */
                    keepDisplayUntil = null;
                    PanelBack();
                    ShowAsleep();
                    Wait(5000);
                    continue;
                }
                /* keep the Mac display for a moment: LaptopScreen may just be restarting, e.g. after an update */
                keepDisplayUntil = DateTime.UtcNow.AddSeconds(15);
                if (switchTo != null)
                {
                    /* straight over to the cable */
                    preferred = switchTo;
                    switchTo = null;
                    continue;
                }
                window.ShowStatus(new AgentStatus(Light.Busy, "Verbindung unterbrochen",
                    "Suche den Mac wieder … Der Mac-Bildschirm bleibt noch einen Moment.", ThinBar.None));
                Wait(2000);
            }
            Cleanup();
            Log("Beendet.");
        }

        /* The pairing code from the window; null (and paused) if the user cancels */
        static string AskCode(bool denied)
        {
            string code = window.AskCode(denied);
            if (code == null)
            {
                Pause(new AgentStatus(Light.Off, "Nicht gekoppelt",
                    "„Verbinden“ fragt noch einmal nach dem Kopplungscode vom Mac.", ThinBar.None));
                return null;
            }
            PairingStore.Save(code);
            return code;
        }

        /* The agent stops by itself (no ffmpeg, no pairing code) until the user clicks "Verbinden" */
        static void Pause(AgentStatus reason)
        {
            pausedStatus = reason;
            paused = true;
        }

        static void RunSession(ControlClient control, string mac, string ffmpeg, Options o)
        {
            var injector = new Injector();
            var clipboard = new ClipboardSync();
            var awake = new StayAwake(Log);
            bool viaCable = Discovery.IsLinkLocal(control.Address);
            DateTime lastCableCheck = DateTime.UtcNow;
            cableFound = null;
            switchTo = null;
            mac += Discovery.Via(control.LocalAddress, out onWifi);
            if (viaCable) cableSeen = true;
            bool sharing = shareClipboard;
            clipboard.Enabled = sharing;
            DateTime started = DateTime.UtcNow;
            macVersion = null;
            macScreen = null;
            announcedMode = null;
            mode = new Size(o.Width, o.Height);
            inSession = true;
            try
            {
                control.Send("VERSION " + Updater.Version);
                control.Send(sharing ? "CLIPBOARD on" : "CLIPBOARD off");
                /* LaptopScreen 1.9.0 and newer mark the Dock icon like the taskbar button and show the way the laptop came */
                control.Send(onWifi ? "VIA wifi" : "VIA cable");
                if (!WaitForDummy(control, clipboard) || !Active) return;
                AwaitScreen(control, clipboard);
                Size? chosenFor = macScreen;

                bool lidOpen = LidIsOpen();
                string switching = !lidOpen ? "Der Deckel ist zu: Der Mac wird der einzige Bildschirm …"
                    : macOnly ? "Das Laptop-Display geht aus: Der Mac wird der einzige Bildschirm …" : "Schalte den Mac-Bildschirm zu …";
                Log(switching);
                window.ShowStatus(new AgentStatus(Light.Busy, "Verbunden mit " + mac, switching, ThinBar.Unknown));
                o.Scale = scale;  // chosen in the window just as the last session ended
                int lockTimeout = Session.LockTimeout();
                if (macOnly) NoteLockTimeout(lockTimeout);
                DisplayInfo external = Configure(o, lidOpen, macOnly && !LockSoon(lockTimeout));
                if (external == null)
                {
                    Log("Kein externer Bildschirm gefunden. Steckt der HDMI-Dummy-Stecker?");
                    window.ShowStatus(new AgentStatus(Light.Problem, "Kein Mac-Bildschirm",
                        "Windows hat den HDMI-Dummy-Stecker nicht als Bildschirm zugeschaltet. Steckt er richtig?", ThinBar.None));
                    Wait(5000);
                    return;
                }
                Log("Mac-Bildschirm: " + external);
                window.ShowScale(external);
                window.ShowArrangement(Displays.Internal(), external);
                injector.SetArea(external);
                int output = Displays.DxgiOutputIndex(external.GdiName);
                if (output < 0)
                {
                    Log("Den Bildschirm für die Aufnahme nicht gefunden.");
                    window.ShowStatus(new AgentStatus(Light.Problem, "Keine Aufnahme möglich",
                        "Den Mac-Bildschirm für die Aufnahme nicht gefunden. Details stehen im Log.", ThinBar.None));
                    return;
                }

                bool locked = Session.IsLocked();
                control.Send(locked ? "STATE locked" : "STATE unlocked");
                lock (cleanupLock)
                {
                    video = new VideoSender(ffmpeg, FfmpegArguments(output, control.Address, control.VideoPort, o));
                    video.Log += Log;
                    video.Pause(locked);
                    video.Start();
                }
                awake.Hold(!locked);
                Log("Übertragung läuft. Auf dem Mac LaptopScreen nach vorne holen.");
                ShowSession(mac, lidOpen, locked);

                string lastLayout = Displays.Describe();
                Log("Anzeige: " + lastLayout);
                DateTime lastHeard = DateTime.UtcNow, lastCheck = DateTime.UtcNow, lastReconfigure = DateTime.UtcNow, lastClipboard = DateTime.UtcNow,
                    lastTrayCheck = DateTime.UtcNow, panelRetry = DateTime.MinValue;
                bool crossed = false, spaceSeen = false;
                while (Active)
                {
                    string line = control.ReadLine(250);
                    DateTime now = DateTime.UtcNow;
                    if (line != null)
                    {
                        lastHeard = now;
                        if (!spaceSeen && line.StartsWith("C 32 1 ", StringComparison.Ordinal))
                        {
                            /* shows whether ctrl+space from the Mac comes as Ctrl (2) or still as Win (8) */
                            spaceSeen = true;
                            Log("Vom Mac kam ein Kürzel mit der Leertaste: " + line + " (Modifier 2 = Strg, 8 = Win).");
                        }
                        if (!locked && (line == LockDown || line == LockUp))
                        {
                            /* ctrl+L on the Mac, Win+L in Windows: synthetic input does not lock Windows, the agent does it itself */
                            if (line == LockDown) LockFromMac(control, injector, o, lidOpen);
                            continue;
                        }
                        string reply = (HandleControl(control, line, clipboard) || locked) ? null : injector.Handle(line);
                        if (macAsleep)
                        {
                            Log("Der Mac schläft ein. Sobald er aufwacht, verbinde ich mich wieder.");
                            break;
                        }
                        if (reply != null) control.Send(reply);
                        if (reply == "POINTER away" && !crossed)
                        {
                            crossed = true;
                            Log("Die Maus vom Mac ist über den Rand auf einen anderen Bildschirm gewechselt.");
                        }
                    }
                    else if ((now - lastHeard).TotalSeconds > 8)
                    {
                        Log("Der Mac antwortet nicht mehr.");
                        break;
                    }
                    if (sharing != shareClipboard)
                    {
                        sharing = shareClipboard;
                        clipboard.Enabled = sharing;
                        control.Send(sharing ? "CLIPBOARD on" : "CLIPBOARD off");
                        Log(sharing ? "Die Zwischenablage wird jetzt geteilt." : "Die Zwischenablage wird nicht mehr geteilt.");
                    }
                    if (sharing && !locked && (now - lastClipboard).TotalMilliseconds >= 250)
                    {
                        lastClipboard = now;
                        List<string> lines = clipboard.Poll();
                        if (lines != null) foreach (string part in lines) control.Send(part);
                    }
                    /* a place chosen in the window goes ahead right away, everything else once a second */
                    if (!PlacementRequested && (now - lastCheck).TotalMilliseconds < 1000) continue;
                    lastCheck = now;
                    Point? place = TakePlacement();

                    if (macVersion == null && !warnedOldMac && (now - started).TotalSeconds > 3)
                    {
                        warnedOldMac = true;
                        Log("LaptopScreen auf dem Mac ist älter als 1.2.0 und kann sich noch nicht selbst aktualisieren. "
                            + "Bitte dort einmal von Hand auf den neuen Stand bringen (README: Einrichtung auf dem Mac).");
                    }

                    bool isLocked = Session.IsLocked();
                    if (isLocked != locked)
                    {
                        locked = isLocked;
                        if (locked) injector.ReleaseAll();
                        if (locked) lockPendingUntil = DateTime.MinValue;
                        control.Send(locked ? "STATE locked" : "STATE unlocked");
                        video.Pause(locked);
                        awake.Hold(!locked);
                        Log(locked ? "Windows ist gesperrt, Übertragung pausiert." : "Windows entsperrt, Übertragung läuft wieder.");
                        ShowSession(mac, lidOpen, locked);
                    }
                    if (!viaCable && CableReady(injector, ref lastCableCheck))
                    {
                        switchTo = cableFound;
                        Log("Der Mac ist jetzt auch übers Kabel erreichbar: Ich wechsle vom WLAN aufs Kabel.");
                        /* LaptopScreen 1.7.5 and newer says so instead of flashing its waiting screen; */
                        /* the line has to arrive before the video connection closes */
                        control.Send("SWITCH cable");
                        Thread.Sleep(100);
                        break;
                    }
                    if (locked)
                    {
                        /* Windows 11 shows its lock screen on "Default" for a while: until someone dismisses it, displays may */
                        /* still be switched, and the panel goes on, so the lock screen and then the sign-in appear there */
                        if (panelSwitchedOff && LidIsOpen() && now >= panelRetry && Session.InputDesktopIsDefault())
                        {
                            LogOnce("Windows ist gesperrt: Laptop-Display an, damit die Anmeldung dort erscheint.");
                            DisplayInfo reconfigured = Configure(o, true, false);
                            lastReconfigure = DateTime.UtcNow;
                            panelRetry = panelSwitchedOff ? DateTime.UtcNow.AddSeconds(2) : DateTime.MinValue;
                            if (reconfigured != null)
                            {
                                /* for the picture and the input after unlocking, before the session switches the panel off again */
                                external = reconfigured;
                                injector.SetArea(external);
                                int capture = Displays.DxgiOutputIndex(external.GdiName);
                                if (capture >= 0 && capture != output)
                                {
                                    output = capture;
                                    video.SetArguments(FfmpegArguments(output, control.Address, control.VideoPort, o));
                                }
                            }
                        }
                        continue;
                    }
                    if (place.HasValue && lidOpen) PlaceMacDisplay(control, place.Value);
                    if ((now - lastTrayCheck).TotalSeconds >= 5)
                    {
                        lastTrayCheck = now;
                        RepairTaskbars();
                    }

                    if (scale != o.Scale)
                    {
                        /* chosen in the window; the layout check below sees the change as the program's own */
                        o.Scale = scale;
                        Log("Skalierung " + o.Scale + " % im Fenster gewählt.");
                        DisplayInfo scaled = Displays.External();
                        if (scaled != null)
                        {
                            try { window.ShowScale(Displays.EnsureMode(scaled, mode.Width, mode.Height, o.Refresh, o.Scale, !panelSwitchedOff) ?? scaled); }
                            catch (InvalidOperationException ex) { Log(ex.Message); }
                        }
                    }

                    bool open = LidIsOpen();
                    bool lidChanged = open != lidOpen;
                    if (lidChanged)
                    {
                        lidOpen = open;
                        Log(!lidOpen ? "Deckel zugeklappt: nur noch der Mac-Bildschirm."
                            : macOnly ? "Deckel aufgeklappt: Das Laptop-Display bleibt aus, wie im Fenster gewählt."
                            : "Deckel aufgeklappt: Laptop-Panel kommt wieder dazu.");
                        /* Windows switches by itself right now: LaptopScreen 1.10.3 and newer keep the last picture meanwhile */
                        control.Send("SWITCH display");
                        ShowSession(mac, lidOpen, false);
                        Thread.Sleep(1500);  // let Windows finish its own reconfiguration first
                        DisplayInfo reconfigured = Configure(o, lidOpen, macOnly && !LockSoon(lockTimeout));
                        if (reconfigured != null) external = reconfigured;
                        lastReconfigure = DateTime.UtcNow;
                    }

                    /*
                     The window's "Laptop-Display ausschalten", and the panel off again after Windows brought it back by itself, e.g.
                     when the lid closed and opened while Windows was locked: that is the way to the sign-in. Shortly before Windows
                     locks itself for lack of input, the panel goes on, so the sign-in appears there; the next input switches it off.
                     */
                    bool switched = false;
                    bool soon = macOnly && (LockSoon(lockTimeout) || now < lockPendingUntil), wantOff = macOnly && !soon;
                    if (lidOpen && !lidChanged && now >= panelRetry && (wantOff ? Displays.Internal() != null : panelSwitchedOff))
                    {
                        switched = true;
                        Log(wantOff ? "Laptop-Display aus: Der Mac ist jetzt der einzige Bildschirm."
                            : soon ? "Windows sperrt sich gleich, weil es so lange keine Eingabe gab: Laptop-Display an, damit die Anmeldung dort erscheint."
                            : "Laptop-Display wieder an.");
                        control.Send("SWITCH display");
                        ShowSession(mac, lidOpen, false);
                        DisplayInfo reconfigured = Configure(o, true, wantOff);
                        if (reconfigured != null) external = reconfigured;
                        lastReconfigure = DateTime.UtcNow;
                        /* failed (logged by Configure): try again a little later instead of every second */
                        bool done = wantOff ? panelSwitchedOff : Displays.Internal() != null;
                        panelRetry = done ? DateTime.MinValue : DateTime.UtcNow.AddSeconds(10);
                    }

                    string layout = Displays.Describe();
                    bool screenChanged = !Nullable.Equals(macScreen, chosenFor);
                    if (!lidChanged && !switched && !screenChanged && layout == lastLayout) continue;
                    if (layout != lastLayout)
                    {
                        Log("Anzeige: " + layout);
                        /* Explorer lays its taskbars out anew: the next look at them comes a second from now, not within five, */
                        /* and a repair starts over with the first notification, since the change may have undone the last one */
                        lastTrayCheck = now.AddSeconds(-4);
                        trayRepair = 0;
                    }
                    lastLayout = layout;

                    DisplayInfo current = Displays.External();
                    if (current == null) continue;
                    if (screenChanged)
                    {
                        /* LaptopScreen moved to another screen of the Mac, or that screen changed its resolution */
                        chosenFor = macScreen;
                        ChooseMode(o, current);
                        lastReconfigure = DateTime.UtcNow;
                        window.ShowTip(SharpnessKey, SharpnessTip());
                    }
                    /* long after any lid change, a change the program did not make is the user's choice in Windows' settings */
                    bool settled = (DateTime.UtcNow - lastReconfigure).TotalSeconds >= 10;
                    bool resized = current.Width != mode.Width || current.Height != mode.Height;
                    if (settled && resized && UserChoseMode(o, current, lidOpen))
                    {
                        mode = new Size(current.Width, current.Height);
                        Settings.Save(ModeKey(macScreen.Value), mode.Width + "x" + mode.Height);
                        Log("Auflösung " + mode.Width + "x" + mode.Height + " übernommen und für diesen Mac gemerkt.");
                        if (current.ScalePercent > 0 && current.ScalePercent != o.Scale) AdoptScale(o, current.ScalePercent);
                    }
                    else if (settled && !resized && current.ScalePercent > 0 && current.ScalePercent != o.Scale)
                    {
                        AdoptScale(o, current.ScalePercent);
                    }
                    else if (resized || current.ScalePercent != o.Scale)
                    {
                        Log("Stelle " + mode.Width + "x" + mode.Height + " bei " + o.Scale + " % wieder her.");
                        try { current = Displays.EnsureMode(current, mode.Width, mode.Height, o.Refresh, o.Scale, !panelSwitchedOff) ?? current; }
                        catch (InvalidOperationException ex) { Log(ex.Message); }
                    }
                    external = current;
                    injector.SetArea(external);
                    window.ShowScale(external);
                    window.ShowArrangement(Displays.Internal(), external);
                    int index = Displays.DxgiOutputIndex(external.GdiName);
                    if (index >= 0 && index != output)
                    {
                        output = index;
                        video.SetArguments(FfmpegArguments(output, control.Address, control.VideoPort, o));
                        Log("Aufnahme folgt dem Mac-Bildschirm (Ausgang " + output + ").");
                    }
                }
            }
            catch (IOException ex) { Log("Verbindung zum Mac getrennt: " + ex.Message); }
            catch (SocketException ex) { Log("Verbindung zum Mac getrennt: " + ex.Message); }
            catch (InvalidOperationException ex) { Log("Fehler: " + ex.Message); }
            finally
            {
                inSession = false;
                awake.Dispose();
                injector.ReleaseAll();
                /* video first: before the Mac sleeps, LaptopScreen waits only for the control connection to close */
                StopVideo();
                control.Dispose();
                window.ShowTip(SharpnessKey, null);
                window.ShowScale(null);
                window.ShowArrangement(null, null);
            }
        }

        /* 0 all taskbars whole, 1 settings notification sent, 2 display notification sent too, 3 neither helped */
        static int trayRepair;

        /*
         A taskbar drawn only in its lower part (TrayRepair): one nudge per check, five seconds apart, and the log says
         which one helped; after both it waits until the taskbar is whole again by other means
         */
        static void RepairTaskbars()
        {
            bool broken = TrayRepair.Broken().Count > 0;
            if (!broken)
            {
                if (trayRepair == 1) Log("Die Taskleiste ist wieder ganz gezeichnet: Die Einstellungs-Nachricht an Explorer hat geholfen.");
                else if (trayRepair == 2) Log("Die Taskleiste ist wieder ganz gezeichnet: Die Anzeige-Nachricht an Explorer hat geholfen.");
                trayRepair = 0;
                return;
            }
            if (trayRepair == 0)
            {
                Log("Eine Taskleiste ist nur unten gezeichnet, darüber scheint der Desktop durch. Ich schicke Explorer die Einstellungs-Nachricht.");
                TrayRepair.NudgeSettings();
                trayRepair = 1;
            }
            else if (trayRepair == 1)
            {
                Log("Die Taskleiste ist noch nicht ganz da. Ich schicke Explorer die Anzeige-Nachricht.");
                TrayRepair.NudgeDisplay();
                trayRepair = 2;
            }
            else if (trayRepair == 2)
            {
                Log("Beide Nachrichten haben nicht geholfen. Abhilfe: im Task-Manager den Windows-Explorer neu starten.");
                trayRepair = 3;
            }
        }

        /*
         The window's arrangement; the layout check that follows sees the change like any other and passes it on. Windows
         rebuilds its desktop even for a mere move, which breaks off the capture: LaptopScreen 1.10.3 and newer keep the
         last picture meanwhile, and ffmpeg starts again without the usual pause. A scaling change leaves the capture alone.
         */
        static void PlaceMacDisplay(ControlClient control, Point offset)
        {
            DisplayInfo panel = Displays.Internal(), external = Displays.External();
            if (panel == null || external == null) return;
            if (external.X - panel.X == offset.X && external.Y - panel.Y == offset.Y) return;
            control.Send("SWITCH display");
            video.RestartPromptly();
            try
            {
                Displays.Place(panel, external, offset);
                Log("Anordnung im Fenster gewählt: "
                    + ArrangementView.Side(new Size(panel.Width, panel.Height), new Size(external.Width, external.Height), offset) + ".");
            }
            catch (InvalidOperationException ex) { Log(ex.Message); }
        }

        static void ShowSession(string mac, bool lidOpen, bool locked)
        {
            if (locked)
                window.ShowStatus(new AgentStatus(Light.Paused, "Verbunden mit " + mac,
                    "Windows ist gesperrt. Die Übertragung ruht, bis du den Laptop entsperrst.", ThinBar.None));
            else
                window.ShowStatus(new AgentStatus(onWifi ? Light.OnWifi : Light.On, "Verbunden mit " + mac,
                    onWifi && cableSeen
                        ? "Das Kabel ist gerade nicht bereit, nach dem Aufwachen des Mac dauert das etwa eine Minute. "
                            + "Bis dahin läuft die Verbindung übers WLAN, danach wechselt sie von selbst aufs Kabel."
                        /* lid closed or laptop display switched off: the arrangement's box says so already */
                        : lidOpen && !macOnly ? "Der Mac ist dein zweiter Bildschirm.\nAuf dem Mac LaptopScreen nach vorne holen." : "",
                    ThinBar.None));
            window.ShowTip(SharpnessKey, SharpnessTip());
        }

        const string SharpnessKey = "tip.sharpness";

        /* The Mac draws LaptopScreen with fewer pixels than the laptop sends ("looks like 1600 x 900" on a 5K iMac): a setting */
        /* with more space makes the picture sharper, and Windows keeps its size there, since LaptopScreen fills the screen */
        static string SharpnessTip()
        {
            if (!macScreen.HasValue || macScreen.Value.Width >= mode.Width) return null;
            return "Tipp für ein schärferes Bild: Wähle auf dem Mac unter Systemeinstellungen → Displays eine Auflösung "
                + "mit mehr Platz, etwa „Standard“. In Windows bleibt dabei alles gleich groß, nur die Elemente der Mac-Oberfläche werden kleiner. "
                + "Gerade zeichnet der Mac mit " + macScreen.Value.Width + " × " + macScreen.Value.Height
                + " Pixeln, der Laptop schickt " + mode.Width + " × " + mode.Height + ".";
        }

        static void ShowAsleep()
        {
            window.ShowStatus(new AgentStatus(Light.Paused, "Der Mac schläft",
                "Sobald er aufwacht, verbindet sich iMac-Display von selbst wieder.", ThinBar.None));
        }

        /* ctrl+L on the Mac arrives as Win+L, pressed and released */
        const string LockDown = "C 108 1 8", LockUp = "C 108 0 8";

        /*
         Windows locks itself within the next minute for lack of input (Session.LockTimeout); not while a program keeps the
         display on, which holds off the screen saver
         */
        static bool LockSoon(int lockTimeout)
        {
            return lockTimeout > 0 && Session.IdleSeconds() >= Math.Max(lockTimeout / 2, lockTimeout - 60) && !Session.DisplayHeld();
        }

        static bool lockTimeoutNoted;
        /* LockWorkStation returns before Windows is locked: until then (at most 10 s) the panel must not go off again */
        static DateTime lockPendingUntil;

        /* once per run, so the log says when the panel will go on by itself */
        static void NoteLockTimeout(int lockTimeout)
        {
            if (lockTimeoutNoted) return;
            lockTimeoutNoted = true;
            if (lockTimeout <= 0) Log("Windows sperrt sich nicht von selbst, wenn es keine Eingabe gibt.");
            else Log("Windows sperrt sich nach " + (lockTimeout % 60 == 0 ? lockTimeout / 60 + " min" : lockTimeout + " s")
                + " ohne Eingabe. Kurz vorher schaltet iMac-Display das Laptop-Display ein, damit die Anmeldung dort erscheint.");
        }

        /*
         ctrl+L on the Mac: lock Windows. With the panel switched off, it goes on first, so the sign-in appears where the
         user can see it; once Windows is locked, it lets no program switch any more. After unlocking, the session switches
         the panel off again.
         */
        static void LockFromMac(ControlClient control, Injector injector, Options o, bool lidOpen)
        {
            injector.ReleaseAll();
            if (lidOpen && panelSwitchedOff)
            {
                Log("Sperre Windows (ctrl + L auf dem Mac). Das Laptop-Display geht vorher an, damit die Anmeldung dort erscheint.");
                control.Send("SWITCH display");
                Configure(o, true, false);
            }
            else Log("Sperre Windows (ctrl + L auf dem Mac).");
            lockPendingUntil = DateTime.UtcNow.AddSeconds(10);
            if (!Native.LockWorkStation()) Log("Windows ließ sich nicht sperren: " + new Win32Exception().Message);
        }

        /*
         The Mac sleeps: with the panel switched off, the laptop would show nothing anywhere until the Mac wakes up.
         Not once the sign-in has taken over the input (Windows lets no program switch then); the agent tries again a few seconds later.
         */
        static void PanelBack()
        {
            if (!panelSwitchedOff || !LidIsOpen() || !Session.InputDesktopIsDefault()) return;
            try
            {
                /* back already, e.g. the lid closed and opened meanwhile */
                if (Displays.Internal() == null)
                {
                    Displays.PanelOn();
                    Log("Der Mac schläft: Das Laptop-Display ist wieder an, bis er aufwacht.");
                }
                panelSwitchedOff = false;
            }
            catch (InvalidOperationException ex) { LogOnce("Das Laptop-Display ließ sich nicht wieder einschalten: " + ex.Message); }
        }

        /* While a session runs over Wi-Fi, looks for the Mac on the direct cable every 10 s in the background. True once it is */
        /* there and no key or button is held: switching interrupts the picture for about a second. */
        static bool CableReady(Injector injector, ref DateTime lastCheck)
        {
            if (cableFound != null) return injector.Idle;
            if (cableChecking || (DateTime.UtcNow - lastCheck).TotalSeconds < 10) return false;
            lastCheck = DateTime.UtcNow;
            cableChecking = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    foreach (MacEndpoint endpoint in Discovery.Find(1500))
                        if (Discovery.IsLinkLocal(endpoint.Address) && Accepts(endpoint))
                        {
                            cableFound = endpoint;
                            break;
                        }
                }
                catch (SocketException) { }
                finally { cableChecking = false; }
            });
            return false;
        }

        /* Right after the Mac wakes up, its cable answers the search before LaptopScreen accepts connections there */
        static bool Accepts(MacEndpoint endpoint)
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
            {
                try
                {
                    IAsyncResult pending = socket.BeginConnect(endpoint.Address, endpoint.Port, null, null);
                    if (!pending.AsyncWaitHandle.WaitOne(1000)) return false;
                    socket.EndConnect(pending);
                    return true;
                }
                catch (SocketException) { return false; }
            }
        }

        /* Lines from the Mac that are not input events. Returns true if the line was handled here. */
        static bool HandleControl(ControlClient control, string line, ClipboardSync clipboard)
        {
            if (line == "SLEEP")
            {
                /* the Mac is about to sleep: hang up while it is still awake, a hang-up after that would wake it again */
                macAsleep = true;
                return true;
            }
            if (line == "P")
            {
                control.Send("P");
                return true;
            }
            if (line.StartsWith("VERSION ", StringComparison.Ordinal))
            {
                NoteMacVersion(line.Substring(8).Trim());
                return true;
            }
            if (line.StartsWith("SCREEN ", StringComparison.Ordinal))
            {
                NoteMacScreen(line.Substring(7).Trim());
                return true;
            }
            if (line == "GETUPDATE")
            {
                SendMacUpdate(control);
                return true;
            }
            if (line.StartsWith("CLIP", StringComparison.Ordinal))
            {
                clipboard.Handle(line);
                return true;
            }
            if (line.StartsWith("FOCUS ", StringComparison.Ordinal))
            {
                clipboard.SetFocus(line == "FOCUS 1");
                return true;
            }
            return false;
        }

        static void NoteMacVersion(string version)
        {
            macVersion = version;
            if (version == reportedMacVersion) return;
            reportedMacVersion = version;
            int order = Updater.Compare(version, Updater.Version);
            if (order < 0)
                Log("LaptopScreen auf dem Mac hat Version " + version + ", dieses Programm " + Updater.Version + ": Der Mac bietet an, sich zu aktualisieren.");
            else if (order > 0)
            {
                Log("LaptopScreen auf dem Mac (" + version + ") ist neuer als dieses Programm (" + Updater.Version + "): Ich suche nach der neuen Version.");
                window.CheckForUpdatesSoon();
            }
        }

        /* "SCREEN 2880 1800": the pixels of the Mac screen LaptopScreen shows the laptop on, with the greeting and after changes */
        static void NoteMacScreen(string size)
        {
            Size screen = ParseSize(size.Replace(' ', 'x'));
            if (screen.Width < 320 || screen.Height < 200 || macScreen == screen) return;
            macScreen = screen;
            Log("LaptopScreen hat auf dem Mac " + screen.Width + "x" + screen.Height
                + " Pixel zur Verfügung (je nach Skalierung unter Systemeinstellungen > Displays).");
        }

        /* LaptopScreen 1.6.0 and newer report their screen together with the greeting; older ones never do */
        static void AwaitScreen(ControlClient control, ClipboardSync clipboard)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(500);
            while (!macScreen.HasValue && DateTime.UtcNow < until && Active)
            {
                string line = control.ReadLine(100);
                if (line != null) HandleControl(control, line, clipboard);
            }
        }

        /* LaptopScreen asked for the Mac sources this laptop carries (it builds and restarts itself) */
        static void SendMacUpdate(ControlClient control)
        {
            string version = null;
            byte[] archive = null;
            try { archive = MacUpdate.Build(BaseDirectory, out version); }
            catch (IOException ex) { Log("Mac-Dateien nicht lesbar: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { Log("Mac-Dateien nicht lesbar: " + ex.Message); }
            if (archive == null)
            {
                control.Send("NOUPDATE");
                Log("Der Mac möchte sich aktualisieren, aber hier fehlen die Mac-Quelltexte (Ordner mac).");
                return;
            }
            foreach (string line in MacUpdate.Frame(archive, version, control.UpdateProof)) control.Send(line);
            Log("LaptopScreen " + version + " an den Mac geschickt. Er baut es jetzt (etwa eine Minute) und startet neu.");
        }

        /* Without the HDMI dummy there is nothing to show: tell the Mac and wait until it is plugged in */
        static bool WaitForDummy(ControlClient control, ClipboardSync clipboard)
        {
            if (Displays.ExternalConnected()) return true;
            control.Send("STATE nodisplay");
            Log("Kein HDMI-Dummy-Stecker gefunden. Bitte in den HDMI-Anschluss des Laptops stecken.");
            window.ShowStatus(new AgentStatus(Light.Problem, "Kein HDMI-Dummy-Stecker",
                "Steck ihn in den HDMI-Anschluss des Laptops – das Bild kommt dann von selbst.", ThinBar.None));
            DateTime lastHeard = DateTime.UtcNow, lastCheck = DateTime.UtcNow;
            while (Active)
            {
                string line = control.ReadLine(250);
                DateTime now = DateTime.UtcNow;
                if (line != null)
                {
                    lastHeard = now;
                    HandleControl(control, line, clipboard);
                    if (macAsleep) return false;
                }
                else if ((now - lastHeard).TotalSeconds > 8)
                {
                    Log("Der Mac antwortet nicht mehr.");
                    return false;
                }
                if ((now - lastCheck).TotalMilliseconds < 1000) continue;
                lastCheck = now;
                if (Displays.ExternalConnected())
                {
                    Log("HDMI-Dummy-Stecker erkannt.");
                    return true;
                }
            }
            return false;
        }

        /*
         Lid open: extended desktop, or with panelOff ("Laptop-Display ausschalten" in the window, unless Windows locks itself
         soon) only the Mac display (Displays.PanelOff); lid closed: only the Mac display. All with the mode for the Mac (ChooseMode).
         */
        static DisplayInfo Configure(Options o, bool lidOpen, bool panelOff)
        {
            lock (cleanupLock) displayExtended = true;
            Func<DisplayInfo, Size> choose = delegate (DisplayInfo external) { return ChooseMode(o, external); };
            try
            {
                if (!lidOpen) return Displays.ExternalOnly(choose, o.Refresh, o.Scale);
                bool off = panelOff;
                DisplayInfo current = Displays.External();
                DisplayInfo panel = Displays.Internal();
                if (off && current != null && panel == null)
                {
                    /* off already, e.g. LaptopScreen just restarted or the connection went over to the cable */
                    panelSwitchedOff = true;
                    return Displays.PanelOff(choose, o.Refresh, o.Scale);
                }
                /* the main display sits at (0,0) */
                DisplayInfo primary = o.InternalPrimary ? panel : current;
                bool ready = current != null && panel != null && choose(current) == new Size(current.Width, current.Height)
                    && current.ScalePercent == o.Scale && primary.X == 0 && primary.Y == 0;  // e.g. LaptopScreen just restarted
                if (!ready) current = Displays.Extend(choose, o.Refresh, o.Scale, !o.InternalPrimary);
                panelSwitchedOff = false;
                if (!off || current == null) return current;
                /* from the extended desktop as Windows remembers it, with the panel as the main display: the way back to the sign-in */
                current = Displays.PanelOff(choose, o.Refresh, o.Scale);
                panelSwitchedOff = Displays.Internal() == null;
                return current;
            }
            catch (InvalidOperationException ex)
            {
                Log("Anzeige-Umschaltung fehlgeschlagen: " + ex.Message);
                return Displays.External();
            }
        }

        /*
         The Mac display's mode for this session: --size; else the one the user chose in Windows' settings for this
         Mac's screen; else the dummy's mode that fills the Mac's screen best (Displays.BestFit). LaptopScreen before
         1.6.0 does not report its screen: 3840 x 2160 as ever. Needs the dummy active, only then does it list its modes.
         */
        static Size ChooseMode(Options o, DisplayInfo external)
        {
            mode = new Size(o.Width, o.Height);
            if (o.SizeGiven || !macScreen.HasValue) return mode;
            List<Size> modes = Displays.Modes(external.GdiName, o.Refresh);
            Size remembered = ParseSize(Settings.Get(ModeKey(macScreen.Value)));
            bool own = modes.Contains(remembered);
            Size chosen = own ? remembered : Displays.BestFit(modes, macScreen.Value);
            if (chosen.IsEmpty) return mode;
            mode = chosen;
            if (announcedMode != mode)
            {
                announcedMode = mode;
                Log("Auflösung für den Mac: " + mode.Width + "x" + mode.Height + (own ? " (für diesen Mac gemerkt)." : " (passt am besten zu seinem Bildschirm)."));
            }
            return mode;
        }

        /*
         Whether a resolution the program did not set is the user's choice in Windows' settings: only with a known Mac
         screen and without --size, and only in the usual arrangement, because Win+P "Duplicate" changes it, too
         */
        static bool UserChoseMode(Options o, DisplayInfo external, bool lidOpen)
        {
            if (o.SizeGiven || !macScreen.HasValue) return false;
            DisplayInfo panel = Displays.Internal();
            bool panelExpected = lidOpen && !panelSwitchedOff;
            if (panelExpected != (panel != null)) return false;
            return panel == null || panel.X != external.X || panel.Y != external.Y;
        }

        /* The user changed the scaling in Windows' settings: keep it, for the next time too */
        static void AdoptScale(Options o, int percent)
        {
            o.Scale = percent;
            scale = percent;
            Settings.Save("scale", percent);
            Log("Skalierung " + percent + " % übernommen und für das nächste Mal gemerkt.");
        }

        /* The settings key of a mode chosen for a Mac screen, e.g. "size.2880x1800" */
        static string ModeKey(Size screen)
        {
            return "size." + screen.Width + "x" + screen.Height;
        }

        /* "2560x1600" as a size; Size.Empty if it is none */
        static Size ParseSize(string text)
        {
            string[] parts = text == null ? new string[0] : text.Split('x');
            int width, height;
            if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out height) && width > 0 && height > 0)
                return new Size(width, height);
            return Size.Empty;
        }

        static bool LidIsOpen()
        {
            return lid == null || lid.IsOpen != false;
        }

        static string FfmpegArguments(int output, IPAddress mac, int port, Options o)
        {
            string filter = string.Format(CultureInfo.InvariantCulture,
                "ddagrab=output_idx={0}:framerate={1}:draw_mouse=1,hwmap=derive_device=qsv,format=qsv," +
                "vpp_qsv=format=nv12:async_depth=1:out_range=tv:out_color_matrix=bt709:out_color_primaries=bt709:out_color_transfer=bt709[v]",
                output, o.Fps);
            return string.Format(CultureInfo.InvariantCulture,
                "-hide_banner -nostdin -loglevel error " +
                "-init_hw_device d3d11va=dx -init_hw_device qsv=qs@dx -filter_hw_device dx " +
                "-filter_complex \"{0}\" -map \"[v]\" " +
                "-c:v h264_qsv -profile:v high -low_power 1 -async_depth 1 -bf 0 -g {1} -scenario displayremoting -low_delay_brc 1 " +
                /* the buffer caps the size of a single frame: 4K keyframes need 0.7-0.9 MB, a smaller buffer truncates them */
                "-b:v {2}M -maxrate {2}M -bufsize 16M " +
                "-colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv " +
                "-flush_packets 1 -f flv -flvflags no_duration_filesize \"tcp://{3}:{4}?tcp_nodelay=1\"",
                filter, o.Fps * 2, o.Bitrate, mac, port);
        }

        static List<MacEndpoint> FindMac(Options o)
        {
            if (o.Host == null) return Discovery.Find(1500);
            var result = new List<MacEndpoint>();
            try
            {
                foreach (IPAddress address in Dns.GetHostAddresses(o.Host))
                {
                    if (address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var endpoint = new MacEndpoint();
                    endpoint.Name = o.Host;
                    endpoint.Address = address;
                    endpoint.Port = o.Port;
                    result.Add(endpoint);
                }
            }
            catch (SocketException) { }
            return result;
        }

        public static string FindFfmpeg()
        {
            string direct = Path.Combine(BaseDirectory, "ffmpeg.exe");
            if (File.Exists(direct)) return direct;
            string tools = Path.Combine(BaseDirectory, "tools");
            if (!Directory.Exists(tools)) return null;
            foreach (string folder in Directory.GetDirectories(tools))
            {
                /* not in ".unpack", where a download may have stopped halfway */
                if (Path.GetFileName(folder).StartsWith(".", StringComparison.Ordinal)) continue;
                string found = Directory.GetFiles(folder, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) return found;
            }
            return Directory.GetFiles(tools, "ffmpeg.exe").FirstOrDefault();
        }

        /* Read-only diagnostics: displays, capture index, dummy, Quick Sync, Mac discovery */
        static void RunTest(Options o, Action<string> line)
        {
            line("iMac-Display " + Updater.Version);
            line("Bildschirme:");
            foreach (DisplayInfo display in Displays.Active())
            {
                line("  " + display + "  -> ddagrab output_idx " + Displays.DxgiOutputIndex(display.GdiName));
                if (!display.Internal)
                    line("    Modi bei " + o.Refresh + " Hz: " + string.Join(", ", Displays.Modes(display.GdiName, o.Refresh).Select(s => s.Width + "x" + s.Height)));
            }
            line("HDMI-Dummy-Stecker: " + (Displays.ExternalConnected() ? "angeschlossen" : "nicht gefunden"));
            line("Windows gesperrt: " + (Session.IsLocked() ? "ja" : "nein"));
            LidWatcher watcher = lid ?? LidWatcher.Start();
            bool? open = watcher == null ? null : watcher.IsOpen;
            line("Deckel: " + (open == null ? "unbekannt" : open.Value ? "offen" : "zu"));
            string ffmpeg = FindFfmpeg();
            line("ffmpeg: " + (ffmpeg ?? "(nicht gefunden)"));
            if (ffmpeg != null)
            {
                string problem = Setup.CheckQuickSync(ffmpeg);
                line("Intel Quick Sync: " + (problem == null ? "funktioniert" : "geht nicht – " + problem));
            }
            line("Zwischenablage teilen: " + (shareClipboard ? "ja" : "nein"));
            line("Suche LaptopScreen per Bonjour …");
            List<MacEndpoint> found = Discovery.Find(1500);
            foreach (MacEndpoint endpoint in found) line("  " + endpoint);
            if (found.Count == 0) line("  (keinen gefunden)");
        }

        /* ---- start and end ---- */

        /* Explorer runs an exe inside a zip from %TEMP%\Temp1_<name>.zip\…, other archivers copy just the exe to %TEMP% */
        static bool RunningFromArchive()
        {
            if (!BaseDirectory.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) return false;
            return BaseDirectory.IndexOf(".zip\\", StringComparison.OrdinalIgnoreCase) >= 0
                || !File.Exists(Path.Combine(BaseDirectory, "VERSION"));
        }

        /* A message from a run without window (--restore, wrong options): on stdout for scripts, otherwise in a box */
        static void Report(string text, MessageKind kind)
        {
            if (OutputRedirected())
            {
                using (TextWriter output = StandardOutput()) output.WriteLine(text);
            }
            else MessageDialog.Show(null, text, kind);
        }

        /* True if stdout goes to a pipe or file, e.g. "imac-display.exe --test | Out-String"; a windowed program has no console */
        static bool OutputRedirected()
        {
            IntPtr handle = Native.GetStdHandle(Native.STD_OUTPUT_HANDLE);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
            int type = Native.GetFileType(handle);
            return type == Native.FILE_TYPE_DISK || type == Native.FILE_TYPE_PIPE;
        }

        /* UTF-8 like the log; there is no console whose code page could be set */
        static TextWriter StandardOutput()
        {
            var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            writer.AutoFlush = true;
            return writer;
        }

        /* One agent at a time; right after an update the previous process may still be finishing */
        static bool AcquireInstance(bool restarted)
        {
            bool created;
            instance = new Mutex(true, @"Local\imac-display", out created);
            if (created) return true;
            try
            {
                if (instance.WaitOne(restarted ? 8000 : 0)) return true;
            }
            catch (AbandonedMutexException) { return true; }
            instance.Dispose();
            instance = null;
            return false;
        }

        static void ReleaseInstance()
        {
            if (instance == null) return;
            instance.ReleaseMutex();
            instance.Dispose();
            instance = null;
        }

        /* Started a second time: bring the running instance's window to the front instead */
        static bool ActivateOtherInstance()
        {
            Process self = Process.GetCurrentProcess();
            foreach (Process other in Process.GetProcessesByName(self.ProcessName))
            {
                using (other)
                {
                    if (other.Id == self.Id || other.SessionId != self.SessionId) continue;
                    IntPtr handle = other.MainWindowHandle;
                    if (handle == IntPtr.Zero) continue;
                    if (Native.IsIconic(handle)) Native.ShowWindow(handle, Native.SW_RESTORE);
                    Native.SetForegroundWindow(handle);
                    return true;
                }
            }
            return false;
        }

        static void OnWindowError(object sender, ThreadExceptionEventArgs e)
        {
            Log("Fehler im Fenster: " + e.Exception);
            string text = e.Exception.Message + "\r\n\r\nDetails stehen im Log.";
            try { MessageDialog.Show(window != null && window.Visible ? window : null, text, MessageKind.Error); }
            catch (Exception ex)
            {
                /* the error may sit in the program's own windows: Windows' box still works */
                Log("Auch die Fehlermeldung scheiterte: " + ex.Message);
                MessageBox.Show(text, "iMac-Display", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /* The process is about to die: at least do not leave the invisible display behind */
        static void OnCrash(object sender, UnhandledExceptionEventArgs e)
        {
            Log("Absturz: " + e.ExceptionObject);
            keepDisplay = false;
            Cleanup();
        }

        static void StopVideo()
        {
            lock (cleanupLock)
            {
                if (video == null) return;
                video.Dispose();
                video = null;
            }
        }

        /*
         Back to the laptop panel alone; the invisible display must not outlive the connection for long.
         While Windows is locked this fails (access denied: only the lock screen's desktop may switch displays).
         */
        static void RevertDisplay(string message)
        {
            string failure = null;
            lock (cleanupLock)
            {
                if (!displayExtended) return;
                try
                {
                    Displays.InternalOnly();
                    panelSwitchedOff = false;
                }
                catch (InvalidOperationException ex) { failure = ex.Message; }
                displayExtended = false;
            }
            if (failure != null) Log("Zurück zum Laptop-Bildschirm ging nicht: " + failure);
            else if (message != null) Log(message);
        }

        static void Cleanup()
        {
            StopVideo();
            if (!keepDisplay) RevertDisplay(null);
        }

        static void Wait(int milliseconds)
        {
            for (int waited = 0; waited < milliseconds && Active; waited += 100) Thread.Sleep(100);
        }

        /* Logs a message only if it differs from the last one, e.g. while searching */
        static void LogOnce(string message)
        {
            if (message == lastLogged) return;
            Log(message);
        }

        public static void Log(string message)
        {
            lastLogged = message;
            DateTime now = DateTime.Now;
            string line = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message;
            lock (logLock)
            {
                recent.Enqueue(line);
                while (recent.Count > 2000) recent.Dequeue();
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    File.AppendAllText(LogPath, now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture) + message + Environment.NewLine, Encoding.UTF8);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                if (logListeners != null) logListeners(line);
            }
        }

        /* Keeps the log small: above 1 MB the current file becomes imac-display.log.old */
        static void RotateLog()
        {
            try
            {
                var file = new FileInfo(LogPath);
                if (!file.Exists || file.Length < 1024 * 1024) return;
                string old = LogPath + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(LogPath, old);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
