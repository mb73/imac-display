using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

namespace ImacDisplay
{
    internal sealed class Options
    {
        public string Host;
        public int Port = 47101;
        /* 200 %: Windows text is smaller than macOS text at the same logical size, 150 % reads too small on a Mac */
        public int Width = 3840, Height = 2160, Refresh = 60, Scale = 200, Fps = 60, Bitrate = 80;
        public bool InternalPrimary, Repair, Test, Restore, Update;
        public bool ShareClipboard = true;
        public string Code, UpdateZip;

        public static Options Parse(string[] args)
        {
            var o = new Options();
            Settings.Load(o);
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                string next = i + 1 < args.Length ? args[i + 1] : null;
                switch (a)
                {
                    case "--host": o.Host = next; i++; break;
                    case "--port": o.Port = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "--size":
                        string[] wh = next.Split('x');
                        o.Width = int.Parse(wh[0], CultureInfo.InvariantCulture);
                        o.Height = int.Parse(wh[1], CultureInfo.InvariantCulture);
                        i++;
                        break;
                    case "--scale": o.Scale = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "--fps": o.Fps = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "--bitrate": o.Bitrate = int.Parse(next, CultureInfo.InvariantCulture); i++; break;
                    case "--code": o.Code = next; i++; break;
                    case "--clipboard":
                        /* remembered, so "--clipboard off" once is enough */
                        o.ShareClipboard = !string.Equals(next, "off", StringComparison.OrdinalIgnoreCase);
                        Settings.Save("clipboard", o.ShareClipboard ? 1 : 0);
                        i++;
                        break;
                    case "--internal-primary": o.InternalPrimary = true; break;
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
                        Console.WriteLine("Unbekannte Option: " + args[i]);
                        Console.WriteLine("Optionen: --host <name|ip> --port <n> --size 3840x2160 --scale 200 --fps 60 --bitrate 80");
                        Console.WriteLine("          --code <kopplungscode> --repair --clipboard on|off --internal-primary");
                        Console.WriteLine("          --test --restore --update [zip]");
                        return null;
                }
            }
            return o;
        }

        static bool IsZip(string path)
        {
            return path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        }
    }

    /* Remembered preferences (%APPDATA%\imac-display\settings.txt, lines "key=value"); command-line options win */
    internal static class Settings
    {
        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "imac-display", "settings.txt"); }
        }

        public static void Load(Options o)
        {
            foreach (KeyValuePair<string, int> pair in Read())
            {
                if (pair.Key == "scale") o.Scale = pair.Value;
                else if (pair.Key == "clipboard") o.ShareClipboard = pair.Value != 0;
            }
        }

        public static void Save(string key, int value)
        {
            Dictionary<string, int> all = Read();
            all[key] = value;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, all.Select(pair => pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture)));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static Dictionary<string, int> Read()
        {
            var result = new Dictionary<string, int>();
            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] pair = line.Split(new[] { '=' }, 2);
                    int value;
                    if (pair.Length == 2 && int.TryParse(pair[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                        result[pair[0].Trim()] = value;
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

        public static string Ask()
        {
            while (true)
            {
                Console.Write("Kopplungscode vom Mac eingeben (steht im LaptopScreen-Fenster): ");
                string line = Console.ReadLine();
                if (line == null) return null;
                if (Crypto.Normalize(line).Length >= 8)
                {
                    Save(line.Trim());
                    return line.Trim();
                }
                Console.WriteLine("Das sieht nicht wie ein Kopplungscode aus (z. B. ABCD-EFGH-JKLM).");
            }
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

    internal static class Program
    {
        public static readonly string BaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string ExePath = Assembly.GetExecutingAssembly().Location;

        static volatile bool stopping;
        static Native.ConsoleCtrlHandler consoleHandler;  // kept in a field so the GC does not collect it
        static Mutex instance;
        static readonly object cleanupLock = new object();
        static readonly object logLock = new object();
        static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "imac-display", "imac-display.log");
        static VideoSender video;
        static LidWatcher lid;
        static bool displayExtended;
        static string lastStatus;
        /* LaptopScreen's version in the current session; LaptopScreen before 1.2.0 never reports it */
        static string macVersion;
        static string reportedMacVersion;
        static bool warnedOldMac;

        static int Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); }  // per-monitor v2: physical pixels everywhere
            catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "iMac-Display";

            Options options = Options.Parse(args);
            if (options == null) return Finish(2);
            Console.WriteLine("iMac-Display " + Updater.Version + " – der Mac als Bildschirm für diesen Laptop");
            Console.WriteLine();
            if (options.Test) return Finish(RunTest(options));
            if (options.Restore)
            {
                /* emergency exit: only the laptop panel, e.g. after a crash left the invisible display active */
                Displays.InternalOnly();
                Console.WriteLine("Nur noch der Laptop-Bildschirm ist aktiv.");
                return Finish(0);
            }
            if (options.Update) return Updater.Run(options.UpdateZip);
            if (RunningFromArchive())
            {
                Console.WriteLine("iMac-Display läuft hier direkt aus der Zip-Datei heraus. Bitte entpacke sie zuerst");
                Console.WriteLine("(Rechtsklick auf die Zip-Datei → „Alle extrahieren …“) und starte imac-display.exe dann");
                Console.WriteLine("aus dem entpackten Ordner.");
                return Finish(1);
            }
            bool restarted = Environment.GetEnvironmentVariable(Updater.RestartedVariable) != null;
            if (!AcquireInstance(restarted))
            {
                Console.WriteLine("iMac-Display läuft schon in einem anderen Fenster.");
                return Finish(1);
            }
            if (!restarted && Updater.OfferDownloadedUpdate())
            {
                ReleaseInstance();
                Updater.Restart(args);
                return 0;
            }

            consoleHandler = OnConsoleEvent;
            Native.SetConsoleCtrlHandler(consoleHandler, true);
            RotateLog();
            lid = LidWatcher.Start();

            string ffmpeg = FindFfmpeg() ?? Setup.InstallFfmpeg();
            if (ffmpeg == null) return Finish(1);

            string code = options.Code ?? (options.Repair ? null : PairingStore.Load());
            if (code == null)
            {
                /* first start: make the program easy to find again */
                Setup.EnsureStartMenuEntry();
                code = PairingStore.Ask();
            }
            if (code == null) return Finish(1);
            if (options.Code != null) PairingStore.Save(code);

            Log("iMac-Display läuft. Beenden mit Strg+C oder durch Schließen dieses Fensters.");
            DateTime? keepDisplayUntil = null;
            while (!stopping)
            {
                if (keepDisplayUntil.HasValue && DateTime.UtcNow > keepDisplayUntil.Value)
                {
                    keepDisplayUntil = null;
                    RevertDisplay("Der Mac ist nicht zurückgekommen: nur noch der Laptop-Bildschirm.");
                }
                List<MacEndpoint> endpoints = FindMac(options);
                if (endpoints.Count == 0)
                {
                    Status("Suche den Mac … (läuft dort LaptopScreen?)");
                    Wait(3000);
                    continue;
                }
                ControlClient control = null;
                string error = null;
                foreach (MacEndpoint endpoint in endpoints)
                {
                    control = ControlClient.Connect(endpoint, code, out error);
                    if (control != null)
                    {
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
                        code = PairingStore.Ask();
                        if (code == null) break;
                    }
                    else
                    {
                        Status("Verbindung zum Mac fehlgeschlagen: " + error);
                        Wait(3000);
                    }
                    continue;
                }
                RunSession(control, ffmpeg, options);
                /* keep the Mac display for a moment: LaptopScreen may just be restarting, e.g. after an update */
                keepDisplayUntil = DateTime.UtcNow.AddSeconds(15);
                if (!stopping) Wait(2000);
            }
            Cleanup();
            Log("Beendet.");
            return 0;
        }

        static void RunSession(ControlClient control, string ffmpeg, Options o)
        {
            var injector = new Injector();
            ClipboardSync clipboard = o.ShareClipboard ? new ClipboardSync() : null;
            DateTime started = DateTime.UtcNow;
            macVersion = null;
            try
            {
                control.Send("VERSION " + Updater.Version);
                control.Send(clipboard != null ? "CLIPBOARD on" : "CLIPBOARD off");
                if (!WaitForDummy(control, clipboard)) return;

                bool lidOpen = LidIsOpen();
                Log(lidOpen ? "Schalte den Mac-Bildschirm zu …" : "Deckel ist zu: Mac-Bildschirm wird der einzige Bildschirm …");
                DisplayInfo external = Configure(o, lidOpen);
                if (external == null)
                {
                    Log("Kein externer Bildschirm gefunden. Steckt der HDMI-Dummy-Stecker?");
                    Wait(5000);
                    return;
                }
                Log("Mac-Bildschirm: " + external);
                injector.SetArea(external);
                int output = Displays.DxgiOutputIndex(external.GdiName);
                if (output < 0)
                {
                    Log("Den Bildschirm für die Aufnahme nicht gefunden.");
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

                string lastLayout = Displays.Describe();
                Log("Anzeige: " + lastLayout);
                DateTime lastHeard = DateTime.UtcNow, lastCheck = DateTime.UtcNow, lastReconfigure = DateTime.UtcNow, lastClipboard = DateTime.UtcNow;
                while (!stopping)
                {
                    string line = control.ReadLine(250);
                    DateTime now = DateTime.UtcNow;
                    if (line != null)
                    {
                        lastHeard = now;
                        if (!HandleControl(control, line, clipboard) && !locked) injector.Handle(line);
                    }
                    else if ((now - lastHeard).TotalSeconds > 8)
                    {
                        Log("Der Mac antwortet nicht mehr.");
                        break;
                    }
                    if (clipboard != null && !locked && (now - lastClipboard).TotalMilliseconds >= 250)
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
                    }
                    if (locked) continue;

                    bool open = LidIsOpen();
                    bool lidChanged = open != lidOpen;
                    if (lidChanged)
                    {
                        lidOpen = open;
                        Log(lidOpen ? "Deckel aufgeklappt: Laptop-Panel kommt wieder dazu." : "Deckel zugeklappt: nur noch der Mac-Bildschirm.");
                        Thread.Sleep(1500);  // let Windows finish its own reconfiguration first
                        DisplayInfo reconfigured = Configure(o, lidOpen);
                        if (reconfigured != null) external = reconfigured;
                        lastReconfigure = DateTime.UtcNow;
                    }

                    string layout = Displays.Describe();
                    if (!lidChanged && layout == lastLayout) continue;
                    lastLayout = layout;
                    Log("Anzeige: " + layout);

                    DisplayInfo current = Displays.External();
                    if (current == null) continue;
                    bool windowsReset = current.Width != o.Width || current.Height != o.Height
                        || (DateTime.UtcNow - lastReconfigure).TotalSeconds < 10;
                    if (current.ScalePercent > 0 && current.ScalePercent != o.Scale && !windowsReset)
                    {
                        /* only the scaling changed, long after any lid change: the user chose it, so keep it */
                        o.Scale = current.ScalePercent;
                        Settings.Save("scale", o.Scale);
                        Log("Skalierung " + o.Scale + " % übernommen und für das nächste Mal gemerkt.");
                    }
                    else if (current.Width != o.Width || current.Height != o.Height || current.ScalePercent != o.Scale)
                    {
                        Log("Stelle " + o.Width + "x" + o.Height + " bei " + o.Scale + " % wieder her.");
                        try { current = Displays.EnsureMode(current, o.Width, o.Height, o.Refresh, o.Scale) ?? current; }
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
                injector.ReleaseAll();
                control.Dispose();
                StopVideo();
            }
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
            if (line == "GETUPDATE")
            {
                SendMacUpdate(control);
                return true;
            }
            if (line.StartsWith("CLIP", StringComparison.Ordinal))
            {
                if (clipboard != null) clipboard.Handle(line);
                return true;
            }
            if (line.StartsWith("FOCUS ", StringComparison.Ordinal))
            {
                if (clipboard != null) clipboard.SetFocus(line == "FOCUS 1");
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
                Log("LaptopScreen auf dem Mac (" + version + ") ist neuer als dieses Programm (" + Updater.Version + "). Zum Aktualisieren update.cmd doppelklicken.");
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
            DateTime lastHeard = DateTime.UtcNow, lastCheck = DateTime.UtcNow;
            while (!stopping)
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

        /* Lid open: extended desktop; lid closed: only the Mac display. Both with the requested mode. */
        static DisplayInfo Configure(Options o, bool lidOpen)
        {
            lock (cleanupLock) displayExtended = true;
            try
            {
                if (!lidOpen) return Displays.ExternalOnly(o.Width, o.Height, o.Refresh, o.Scale);
                DisplayInfo current = Displays.External();
                if (current != null && Displays.InternalActive() && current.Width == o.Width && current.Height == o.Height
                    && current.ScalePercent == o.Scale && (o.InternalPrimary || (current.X == 0 && current.Y == 0)))
                    return current;  // still set up from before, e.g. LaptopScreen just restarted
                return Displays.Extend(o.Width, o.Height, o.Refresh, o.Scale, !o.InternalPrimary);
            }
            catch (InvalidOperationException ex)
            {
                Log("Anzeige-Umschaltung fehlgeschlagen: " + ex.Message);
                return Displays.External();
            }
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
            if (Directory.Exists(tools))
            {
                string found = Directory.GetFiles(tools, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (found != null) return found;
            }
            return null;
        }

        /* Read-only diagnostics: displays, capture index, dummy, Quick Sync, Mac discovery */
        static int RunTest(Options o)
        {
            Console.WriteLine("Bildschirme:");
            foreach (DisplayInfo display in Displays.Active())
                Console.WriteLine("  " + display + "  -> ddagrab output_idx " + Displays.DxgiOutputIndex(display.GdiName));
            Console.WriteLine("HDMI-Dummy-Stecker: " + (Displays.ExternalConnected() ? "angeschlossen" : "nicht gefunden"));
            Console.WriteLine("Windows gesperrt: " + Session.IsLocked());
            LidWatcher watcher = LidWatcher.Start();
            bool? open = watcher == null ? null : watcher.IsOpen;
            Console.WriteLine("Deckel: " + (open == null ? "unbekannt" : open.Value ? "offen" : "zu"));
            string ffmpeg = FindFfmpeg();
            Console.WriteLine("ffmpeg: " + (ffmpeg ?? "(nicht gefunden)"));
            if (ffmpeg != null)
            {
                string problem = Setup.CheckQuickSync(ffmpeg);
                Console.WriteLine("Intel Quick Sync: " + (problem == null ? "funktioniert" : "geht nicht – " + problem));
            }
            Console.WriteLine("Zwischenablage teilen: " + (o.ShareClipboard ? "ja" : "nein"));
            Console.WriteLine("Suche LaptopScreen per Bonjour …");
            List<MacEndpoint> found = Discovery.Find(1500);
            foreach (MacEndpoint endpoint in found) Console.WriteLine("  " + endpoint);
            if (found.Count == 0) Console.WriteLine("  (keinen gefunden)");
            return 0;
        }

        /* Explorer runs an exe inside a zip from %TEMP%\Temp1_<name>.zip\…, other archivers copy just the exe to %TEMP% */
        static bool RunningFromArchive()
        {
            if (!BaseDirectory.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) return false;
            return BaseDirectory.IndexOf(".zip\\", StringComparison.OrdinalIgnoreCase) >= 0
                || !File.Exists(Path.Combine(BaseDirectory, "VERSION"));
        }

        /* Yes/no question in the console; Enter means yes */
        public static bool Ask(string question)
        {
            Console.Write(question + " [J/n] ");
            string answer = Console.ReadLine();
            if (answer == null) return false;
            answer = answer.Trim().ToLowerInvariant();
            return answer.Length == 0 || answer.StartsWith("j", StringComparison.Ordinal) || answer.StartsWith("y", StringComparison.Ordinal);
        }

        public static void Pause()
        {
            Console.WriteLine();
            Console.Write("Taste drücken zum Schließen …");
            try { Console.ReadKey(true); }
            catch (InvalidOperationException) { }  // no interactive console
            Console.WriteLine();
        }

        /* Started by double-click, the window would vanish together with the message: wait for a key first */
        static int Finish(int code)
        {
            if (Native.GetConsoleProcessList(new uint[4], 4) == 1) Pause();
            return code;
        }

        /* One agent at a time; right after an update the previous process may still be finishing */
        static bool AcquireInstance(bool restarted)
        {
            bool created;
            instance = new Mutex(true, @"Local\imac-display", out created);
            if (created) return true;
            try { return instance.WaitOne(restarted ? 5000 : 0); }
            catch (AbandonedMutexException) { return true; }
        }

        static void ReleaseInstance()
        {
            instance.ReleaseMutex();
            instance.Dispose();
            instance = null;
        }

        static bool OnConsoleEvent(int type)
        {
            stopping = true;
            /* closing the window, logoff and shutdown kill the process right after this handler */
            if (type == 2 || type == 5 || type == 6) Cleanup();
            return true;
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

        /* Back to the laptop panel alone; the invisible display must not outlive the connection for long */
        static void RevertDisplay(string message)
        {
            lock (cleanupLock)
            {
                if (!displayExtended) return;
                try { Displays.InternalOnly(); }
                catch (InvalidOperationException) { }
                displayExtended = false;
            }
            if (message != null) Log(message);
        }

        static void Cleanup()
        {
            StopVideo();
            RevertDisplay(null);
        }

        static void Wait(int milliseconds)
        {
            for (int waited = 0; waited < milliseconds && !stopping; waited += 100) Thread.Sleep(100);
        }

        static void Status(string message)
        {
            if (message == lastStatus) return;
            Log(message);
        }

        public static void Log(string message)
        {
            lastStatus = message;
            DateTime now = DateTime.Now;
            Console.WriteLine("[{0:HH:mm:ss}] {1}", now, message);
            lock (logLock)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    File.AppendAllText(LogPath, now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture) + message + Environment.NewLine, Encoding.UTF8);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
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
