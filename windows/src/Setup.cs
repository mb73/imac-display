using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace ImacDisplay
{
    /* First start on a laptop: fetch ffmpeg, check Intel Quick Sync, add a Start menu entry */
    internal static class Setup
    {
        /* ffmpeg is missing: fetch it with windows\setup.ps1 (pinned version, checksum verified) */
        public static string InstallFfmpeg()
        {
            Console.WriteLine("Für die Übertragung braucht iMac-Display das freie Programm ffmpeg (einmalig, ca. 110 MB).");
            string script = Path.Combine(Program.BaseDirectory, "windows", "setup.ps1");
            if (!File.Exists(script))
            {
                Console.WriteLine("Dazu fehlt " + script + ". Bitte die Zip-Datei vollständig entpacken.");
                return null;
            }
            if (!Program.Ask("Jetzt herunterladen?")) return null;
            var info = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"");
            info.UseShellExecute = false;
            try
            {
                using (Process process = Process.Start(info)) process.WaitForExit();
            }
            catch (Win32Exception ex)
            {
                Console.WriteLine("PowerShell ließ sich nicht starten: " + ex.Message);
                return null;
            }
            string ffmpeg = Program.FindFfmpeg();
            if (ffmpeg == null)
            {
                Console.WriteLine("ffmpeg ist nicht angekommen (siehe oben). Später einfach noch einmal starten.");
                return null;
            }
            string problem = CheckQuickSync(ffmpeg);
            if (problem == null) Console.WriteLine("Intel Quick Sync funktioniert.");
            else Console.WriteLine("Achtung: Intel Quick Sync geht hier nicht (" + problem + "). Ohne Intel-Grafik kann iMac-Display das Bild nicht übertragen.");
            Console.WriteLine();
            return ffmpeg;
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
                Console.WriteLine("Im Startmenü gibt es jetzt „iMac-Display“; per Rechtsklick lässt es sich an die Taskleiste anheften.");
                Console.WriteLine();
            }
            catch (Exception) { }  // COM or binder errors vary; without the entry everything still works
        }
    }
}
