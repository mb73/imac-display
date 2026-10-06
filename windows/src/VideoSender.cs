using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ImacDisplay
{
    /*
     Runs ffmpeg (screen capture + Intel QuickSync H.264 + FLV over TCP to the Mac) and restarts it
     whenever it ends, e.g. after a desktop switch. ffmpeg lives in a job object that is killed when
     this process ends, so no orphaned encoder can keep pushing video.
     */
    internal sealed class VideoSender : IDisposable
    {
        readonly string ffmpeg;
        volatile string arguments;
        readonly IntPtr job;
        readonly object gate = new object();
        Thread thread;
        Process current;
        volatile bool running;
        volatile bool paused;

        public event Action<string> Log;

        public VideoSender(string ffmpeg, string arguments)
        {
            this.ffmpeg = ffmpeg;
            this.arguments = arguments;
            job = CreateKillOnCloseJob();
        }

        public void Start()
        {
            running = true;
            thread = new Thread(Loop);
            thread.IsBackground = true;
            thread.Name = "video";
            thread.Start();
        }

        /* New capture settings (e.g. another output index after a display change); restarts ffmpeg */
        public void SetArguments(string newArguments)
        {
            arguments = newArguments;
            KillCurrent();
        }

        public void Pause(bool pause)
        {
            paused = pause;
            if (pause) KillCurrent();
        }

        public void Dispose()
        {
            running = false;
            KillCurrent();
            if (thread != null) thread.Join(3000);
        }

        void Loop()
        {
            int quickFailures = 0;
            while (running)
            {
                if (paused)
                {
                    Thread.Sleep(300);
                    continue;
                }
                DateTime started = DateTime.UtcNow;
                string lastError = RunOnce();
                if (!running) break;
                if (paused) continue;
                bool quick = (DateTime.UtcNow - started).TotalSeconds < 5;
                quickFailures = quick ? quickFailures + 1 : 0;
                Emit("Video-Übertragung neu gestartet" + (lastError != null ? " (" + lastError + ")" : ""));
                Thread.Sleep(Math.Min(5000, 500 * Math.Max(1, quickFailures)));
            }
        }

        string RunOnce()
        {
            var info = new ProcessStartInfo(ffmpeg, arguments);
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
                lock (gate)
                {
                    if (!running || paused) return null;
                    process.Start();
                    current = process;
                }
                Native.AssignProcessToJobObject(job, process.Handle);
                process.BeginErrorReadLine();
                process.WaitForExit();
                lock (gate) current = null;
            }
            return last;
        }

        void KillCurrent()
        {
            lock (gate)
            {
                if (current == null) return;
                try { if (!current.HasExited) current.Kill(); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }

        void Emit(string message)
        {
            var handler = Log;
            if (handler != null) handler(message);
        }

        static IntPtr CreateKillOnCloseJob()
        {
            IntPtr job = Native.CreateJobObject(IntPtr.Zero, null);
            var info = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            Native.SetInformationJobObject(job, Native.JobObjectExtendedLimitInformation, ref info,
                (uint)Marshal.SizeOf(typeof(Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION)));
            return job;
        }
    }
}
