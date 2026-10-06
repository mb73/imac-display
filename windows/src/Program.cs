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
     "scale" and "clipboard" hold numbers, "size.<Mac screen>" the mode the user chose for that Mac, e.g.
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

    /* The lock screen, UAC prompts and the screen saver run on another desktop than "Default" */
    internal static class Session
    {
        public static bool IsLocked()
        {
            IntPtr desktop = Native.OpenInputDesktop(0, false, 0x0001);  // DESKTOP_READOBJECTS
            if (desktop == IntPtr.Zero) return true;
            try
            {
                var name = new StringBuilder(256);
                int needed;
                if (!Native.GetUserObjectInformation(desktop, 2, name, name.Capacity * 2, out needed)) return false;  // UOI_NAME
                return !string.Equals(name.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
            }
            finally { Native.CloseDesktop(desktop); }
        }
    }

    /*
     Entry point and agent. The window (MainWindow) runs on the main thread; the agent loop runs on its
     own thread: find the Mac, handshake, then RunSession until the connection ends, the user
     disconnects ("Verbindung trennen" sets paused) or the window closes (stopping). It reports to the
     window with ShowStatus and asks through it (pairing code, ffmpeg download).
     */
    internal static class Program
    {
        public static readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string ExePath = Assembly.GetExecutingAssembly().Location;
        public const string GuideUrl = "https://github.com/mb73/imac-display/blob/main/README.md";

        /* keepDisplay: an update hands the Mac display over to the new version instead of switching it off */
        static volatile bool stopping, paused, keepDisplay, shareClipboard, inSession;
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
        /* what the window shows while paused: the user's "Verbindung trennen" or the agent's own reason */
        static AgentStatus pausedStatus;
        /* LaptopScreen's version in the current session; LaptopScreen before 1.2.0 never reports it */
        static string macVersion;
        static string reportedMacVersion;
        static bool warnedOldMac;
        /* the Mac's screen in pixels, as LaptopScreen 1.6.0 and newer report it; the Mac display's mode chosen for it */
        static Size? macScreen;
        static Size mode;
        static Size? announcedMode;

        static readonly AgentStatus Disconnected = new AgentStatus(Light.Off, "Getrennt",
            "Der Mac ist nicht mehr dein Bildschirm. „Verbinden“ holt ihn zurück.", ThinBar.None);

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

        /* "Verbindung trennen" (true) and "Verbinden" (false) */
        public static void SetPaused(bool value)
        {
            if (value) pausedStatus = Disconnected;
            paused = value;
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
                    window.ShowStatus(pausedStatus ?? Disconnected);
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

                List<MacEndpoint> endpoints = FindMac(o);
                if (!Active) continue;
                if (endpoints.Count == 0)
                {
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
                        break;
                    }
                    if (error == ControlClient.Denied) break;
                }
                if (control == null)
                {
                    if (error == ControlClient.Denied)
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
                /* keep the Mac display for a moment: LaptopScreen may just be restarting, e.g. after an update */
                keepDisplayUntil = DateTime.UtcNow.AddSeconds(15);
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
                if (!WaitForDummy(control, clipboard) || !Active) return;
                AwaitScreen(control, clipboard);
                Size? chosenFor = macScreen;

                bool lidOpen = LidIsOpen();
                Log(lidOpen ? "Schalte den Mac-Bildschirm zu …" : "Deckel ist zu: Mac-Bildschirm wird der einzige Bildschirm …");
                window.ShowStatus(new AgentStatus(Light.Busy, "Verbunden mit " + mac,
                    lidOpen ? "Schalte den Mac-Bildschirm zu …" : "Der Deckel ist zu: Der Mac wird der einzige Bildschirm …", ThinBar.Unknown));
                DisplayInfo external = Configure(o, lidOpen);
                if (external == null)
                {
                    Log("Kein externer Bildschirm gefunden. Steckt der HDMI-Dummy-Stecker?");
                    window.ShowStatus(new AgentStatus(Light.Problem, "Kein Mac-Bildschirm",
                        "Windows hat den HDMI-Dummy-Stecker nicht als Bildschirm zugeschaltet. Steckt er richtig?", ThinBar.None));
                    Wait(5000);
                    return;
                }
                Log("Mac-Bildschirm: " + external);
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
                Log("Übertragung läuft. Auf dem Mac LaptopScreen nach vorne holen.");
                ShowSession(mac, lidOpen, locked);

                string lastLayout = Displays.Describe();
                Log("Anzeige: " + lastLayout);
                DateTime lastHeard = DateTime.UtcNow, lastCheck = DateTime.UtcNow, lastReconfigure = DateTime.UtcNow, lastClipboard = DateTime.UtcNow;
                bool crossed = false;
                while (Active)
                {
                    string line = control.ReadLine(250);
                    DateTime now = DateTime.UtcNow;
                    if (line != null)
                    {
                        lastHeard = now;
                        string reply = (HandleControl(control, line, clipboard) || locked) ? null : injector.Handle(line);
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
                    if ((now - lastCheck).TotalMilliseconds < 1000) continue;
                    lastCheck = now;

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
                        control.Send(locked ? "STATE locked" : "STATE unlocked");
                        video.Pause(locked);
                        Log(locked ? "Windows ist gesperrt, Übertragung pausiert." : "Windows entsperrt, Übertragung läuft wieder.");
                        ShowSession(mac, lidOpen, locked);
                    }
                    if (locked) continue;

                    bool open = LidIsOpen();
                    bool lidChanged = open != lidOpen;
                    if (lidChanged)
                    {
                        lidOpen = open;
                        Log(lidOpen ? "Deckel aufgeklappt: Laptop-Panel kommt wieder dazu." : "Deckel zugeklappt: nur noch der Mac-Bildschirm.");
                        ShowSession(mac, lidOpen, false);
                        Thread.Sleep(1500);  // let Windows finish its own reconfiguration first
                        DisplayInfo reconfigured = Configure(o, lidOpen);
                        if (reconfigured != null) external = reconfigured;
                        lastReconfigure = DateTime.UtcNow;
                    }

                    string layout = Displays.Describe();
                    bool screenChanged = !Nullable.Equals(macScreen, chosenFor);
                    if (!lidChanged && !screenChanged && layout == lastLayout) continue;
                    if (layout != lastLayout) Log("Anzeige: " + layout);
                    lastLayout = layout;

                    DisplayInfo current = Displays.External();
                    if (current == null) continue;
                    if (screenChanged)
                    {
                        /* LaptopScreen moved to another screen of the Mac, or that screen changed its resolution */
                        chosenFor = macScreen;
                        ChooseMode(o, current);
                        lastReconfigure = DateTime.UtcNow;
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
                        try { current = Displays.EnsureMode(current, mode.Width, mode.Height, o.Refresh, o.Scale) ?? current; }
                        catch (InvalidOperationException ex) { Log(ex.Message); }
                    }
                    external = current;
                    injector.SetArea(external);
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
                injector.ReleaseAll();
                control.Dispose();
                StopVideo();
            }
        }

        static void ShowSession(string mac, bool lidOpen, bool locked)
        {
            if (locked)
                window.ShowStatus(new AgentStatus(Light.Paused, "Verbunden mit " + mac,
                    "Windows ist gesperrt. Die Übertragung ruht, bis du den Laptop entsperrst.", ThinBar.None));
            else
                window.ShowStatus(new AgentStatus(Light.On, "Verbunden mit " + mac, lidOpen
                    ? "Der Mac ist dein zweiter Bildschirm. Auf dem Mac LaptopScreen nach vorne holen."
                    : "Der Deckel ist zu: Der Mac ist dein einziger Bildschirm.", ThinBar.None));
        }

        /* Lines from the Mac that are not input events. Returns true if the line was handled here. */
        static bool HandleControl(ControlClient control, string line, ClipboardSync clipboard)
        {
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
            Log("Der Mac-Bildschirm hat " + screen.Width + "x" + screen.Height + " Pixel.");
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

        /* Lid open: extended desktop; lid closed: only the Mac display. Both with the mode for the Mac (ChooseMode). */
        static DisplayInfo Configure(Options o, bool lidOpen)
        {
            lock (cleanupLock) displayExtended = true;
            Func<DisplayInfo, Size> choose = delegate (DisplayInfo external) { return ChooseMode(o, external); };
            try
            {
                if (!lidOpen) return Displays.ExternalOnly(choose, o.Refresh, o.Scale);
                DisplayInfo current = Displays.External();
                DisplayInfo panel = Displays.Internal();
                /* the main display sits at (0,0) */
                DisplayInfo primary = o.InternalPrimary ? panel : current;
                if (current != null && panel != null && choose(current) == new Size(current.Width, current.Height)
                    && current.ScalePercent == o.Scale && primary.X == 0 && primary.Y == 0)
                    return current;  // still set up from before, e.g. LaptopScreen just restarted
                return Displays.Extend(choose, o.Refresh, o.Scale, !o.InternalPrimary);
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
            if (lidOpen != (panel != null)) return false;
            return panel == null || panel.X != external.X || panel.Y != external.Y;
        }

        /* The user changed the scaling in Windows' settings: keep it, for the next time too */
        static void AdoptScale(Options o, int percent)
        {
            o.Scale = percent;
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
                try { Displays.InternalOnly(); }
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
