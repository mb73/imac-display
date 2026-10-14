using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ImacDisplay
{
    /*
     Keeps the pointer on the Mac smooth while the laptop is busy. ffmpeg gets the high CPU priority class and the high
     GPU scheduling priority, so that a build or a busy GPU does not delay capture and encoding; the session thread that
     injects the Mac's input runs time-critical. Both processes opt out of power throttling (EcoQoS): the window is
     mostly minimized and ffmpeg has none, and then Windows may run their threads on the efficient cores at a low clock.
     Realtime is avoided on purpose: for the CPU a standard user silently gets "high" anyway, for the GPU it could push
     back the desktop composition itself.
     */
    internal static class Priority
    {
        /* Opts a process out of power throttling; null when done, otherwise the reason */
        public static string HighQos(IntPtr process)
        {
            var state = new Native.PROCESS_POWER_THROTTLING_STATE();
            state.Version = 1;
            /* the timer flag exists since Windows 11; without it Windows 10 still takes the execution speed */
            foreach (uint mask in new[] { Native.PROCESS_POWER_THROTTLING_EXECUTION_SPEED | Native.PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
                Native.PROCESS_POWER_THROTTLING_EXECUTION_SPEED })
            {
                state.ControlMask = mask;
                state.StateMask = 0;
                if (Native.SetProcessInformation(process, Native.ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf(state))) return null;
            }
            return "Energiedrosselung: Fehler " + Marshal.GetLastWin32Error();
        }

        /*
         Raises ffmpeg: CPU class high, no power throttling, GPU scheduling high; null when all of it worked. Windows takes
         the GPU priority only once the process has a graphics device (before that 0xC000000D, measured 2026-10-14), so
         this tries again until ffmpeg has set up its capture, at most 5 s; call it while ffmpeg's output is being read.
         */
        public static string RaiseEncoder(Process process)
        {
            var failed = new List<string>();
            try { process.PriorityClass = ProcessPriorityClass.High; }
            catch (Win32Exception ex) { failed.Add("Prozessor: " + ex.Message); }
            catch (InvalidOperationException) { return null; }  // ffmpeg has already ended
            string qos = HighQos(process.Handle);
            if (qos != null) failed.Add(qos);
            try
            {
                int status = 0;
                for (int attempt = 0; attempt < 100; attempt++)
                {
                    status = Native.D3DKMTSetProcessSchedulingPriorityClass(process.Handle, Native.D3DKMT_SCHEDULINGPRIORITYCLASS_HIGH);
                    if (status == 0) break;
                    if (process.WaitForExit(50)) return null;
                }
                if (status != 0) failed.Add("Grafik: 0x" + status.ToString("X8"));
            }
            catch (EntryPointNotFoundException) { failed.Add("Grafik: nicht verfügbar"); }
            return failed.Count == 0 ? null : string.Join(", ", failed.ToArray());
        }

        /* Lets the calling thread go ahead of all ordinary programs (15, the top below the realtime class), or back to normal */
        public static void TimeCritical(bool on)
        {
            Native.SetThreadPriority(Native.GetCurrentThread(), on ? Native.THREAD_PRIORITY_TIME_CRITICAL : Native.THREAD_PRIORITY_NORMAL);
        }
    }
}
