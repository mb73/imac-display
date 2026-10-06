using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace ImacDisplay
{
    /* First start on a laptop: fetch ffmpeg, check Intel Quick Sync, add a Start menu entry */
    internal static class Setup
    {
        /*
         The gyan.dev "essentials" build, mirrored on GitHub because gyan.dev itself delivers only a few
         hundred KB/s; the checksum is the one gyan.dev publishes. To update: change both together.
         */
        const string FfmpegVersion = "9.0.2";
        const string FfmpegSha256 = "60f467265b1e312373dbcd92200c2618a74850f98d3d078e94296bb3fa2047ba";
        const string FfmpegName = "ffmpeg-" + FfmpegVersion + "-essentials_build";
        const string FfmpegUrl = "https://github.com/GyanD/codexffmpeg/releases/download/" + FfmpegVersion + "/" + FfmpegName + ".zip";

        /* ffmpeg is missing: asks, fetches it into tools\ and checks Quick Sync; null if that did not work */
        public static string InstallFfmpeg(MainWindow window)
        {
            if (!window.Ask("Für die Übertragung braucht iMac-Display das freie Programm ffmpeg (einmalig, ca. 110 MB).\r\n\r\nJetzt herunterladen?", "Herunterladen"))
                return null;
            string zip = Path.Combine(Path.GetTempPath(), FfmpegName + ".zip");
            try
            {
                Program.Log("Lade ffmpeg " + FfmpegVersion + " herunter …");
                Download(zip, delegate (long done, long total)
                {
                    int percent = total > 0 ? (int)(done * 100 / total) : ThinBar.Unknown;
                    string amount = total > 0 ? Megabytes(done) + " von " + Megabytes(total) + " MB" : Megabytes(done) + " MB";
                    window.ShowStatus(new AgentStatus(Light.Busy, "Lade ffmpeg herunter …", amount, percent));
                });
                window.ShowStatus(new AgentStatus(Light.Busy, "Entpacke ffmpeg …", "", ThinBar.Unknown));
                Unpack(zip);
            }
            catch (WebException ex) { return Failed(window, ex.Message); }
            catch (IOException ex) { return Failed(window, ex.Message); }
            catch (InvalidDataException ex) { return Failed(window, ex.Message); }
            catch (UnauthorizedAccessException ex) { return Failed(window, ex.Message); }
            finally { Updater.DeleteQuietly(zip); }

            string ffmpeg = Program.FindFfmpeg();
            if (ffmpeg == null) return Failed(window, "ffmpeg.exe fehlt in der heruntergeladenen Datei.");
            Program.Log("ffmpeg ist da: " + ffmpeg);
            window.ShowStatus(new AgentStatus(Light.Busy, "Prüfe Intel Quick Sync …", "", ThinBar.Unknown));
            string problem = CheckQuickSync(ffmpeg);
            if (problem == null) Program.Log("Intel Quick Sync funktioniert.");
            else
            {
                Program.Log("Intel Quick Sync geht hier nicht: " + problem);
                window.Warn("Intel Quick Sync geht auf diesem Laptop nicht (" + problem + ").\r\n\r\n"
                    + "Ohne Intel-Grafik kann iMac-Display das Bild nicht übertragen.");
            }
            return ffmpeg;
        }

        /* The pinned build into zip, checked against its SHA-256; throws WebException, IOException or InvalidDataException */
        public static void Download(string zip, Action<long, long> progress)
        {
            string sha;
            using (var hash = SHA256.Create())
            {
                Web.File(FfmpegUrl, zip, progress, hash);
                sha = Crypto.Hex(hash.Hash);
            }
            if (sha != FfmpegSha256) throw new InvalidDataException("Die Prüfsumme stimmt nicht: erwartet " + FfmpegSha256 + ", erhalten " + sha + ".");
        }

        /* Into tools\, via tools\.unpack: FindFfmpeg skips that, so an unpacking that stops halfway leaves no broken ffmpeg */
        public static void Unpack(string zip)
        {
            string tools = Path.Combine(Program.BaseDirectory, "tools");
            string unpack = Path.Combine(tools, ".unpack");
            if (Directory.Exists(unpack)) Directory.Delete(unpack, true);
            ZipFile.ExtractToDirectory(zip, unpack);
            string target = Path.Combine(tools, FfmpegName);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(Path.Combine(unpack, FfmpegName), target);
            Directory.Delete(unpack, true);
        }

        static string Failed(MainWindow window, string reason)
        {
            Program.Log("ffmpeg ließ sich nicht installieren: " + reason);
            window.Warn("ffmpeg ließ sich nicht herunterladen:\r\n" + reason + "\r\n\r\n„Verbinden“ versucht es noch einmal.");
            return null;
        }

        static string Megabytes(long bytes)
        {
            return (bytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture);
        }

        /* Encodes three tiny frames with h264_qsv: null if Quick Sync works, otherwise ffmpeg's complaint */
        public static string CheckQuickSync(string ffmpeg)
        {
            var info = new ProcessStartInfo(ffmpeg, "-hide_banner -nostdin -loglevel error -f lavfi -i color=black:s=256x144:r=30 " +
                "-frames:v 3 -pix_fmt nv12 -c:v h264_qsv -low_power 1 -f null -");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardError = true;
            string last = null;
            using (var process = new Process())
            {
                process.StartInfo = info;
                process.ErrorDataReceived += delegate (object sender, DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrWhiteSpace(e.Data)) last = e.Data.Trim();
                };
                try { process.Start(); }
                catch (Win32Exception ex) { return ex.Message; }
                process.BeginErrorReadLine();
                if (!process.WaitForExit(15000))
                {
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    catch (Win32Exception) { }
                    return "ffmpeg antwortet nicht";
                }
                process.WaitForExit();  // lets the asynchronous stderr reader finish
                return process.ExitCode == 0 ? null : last ?? "Fehlercode " + process.ExitCode;
            }
        }

        /* Start menu entry "iMac-Display" for this user (no admin rights needed): easy to find and to pin */
        public static void EnsureStartMenuEntry()
        {
            try
            {
                string link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "iMac-Display.lnk");
                if (File.Exists(link)) return;
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic shortcut = shell.CreateShortcut(link);
                shortcut.TargetPath = Program.ExePath;
                shortcut.WorkingDirectory = Program.BaseDirectory;
                shortcut.Description = "Der Mac als zusätzlicher Bildschirm für diesen Laptop";
                shortcut.Save();
                Program.Log("Im Startmenü gibt es jetzt „iMac-Display“; per Rechtsklick lässt es sich an die Taskleiste anheften.");
            }
            catch (Exception) { }  // COM or binder errors vary; without the entry everything still works
        }
    }
}
