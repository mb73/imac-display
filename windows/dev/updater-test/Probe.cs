using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

/*
 Stands in for the new imac-display.exe inside the zips of updater-test.ps1. Started by Updater.Restart it
 writes what it got (path, working folder, IMAC_DISPLAY_UPDATED, arguments) to probe-result.txt next to itself.
 With PROBE_SLEEP set it only sleeps that many milliseconds: a previous version still running from its .old.
 */
static class Probe
{
    static int Main(string[] args)
    {
        string sleep = Environment.GetEnvironmentVariable("PROBE_SLEEP");
        if (sleep != null)
        {
            Thread.Sleep(int.Parse(sleep));
            return 0;
        }
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        var text = new StringBuilder();
        text.AppendLine("exe=" + Assembly.GetExecutingAssembly().Location);
        text.AppendLine("cwd=" + Environment.CurrentDirectory);
        text.AppendLine("env=" + (Environment.GetEnvironmentVariable("IMAC_DISPLAY_UPDATED") ?? "<unset>"));
        text.AppendLine("argc=" + args.Length);
        for (int i = 0; i < args.Length; i++) text.AppendLine("arg" + i + "=[" + args[i] + "]");
        text.AppendLine("cmdline=" + Environment.CommandLine);
        /* written aside first, so the waiting side never reads half of it */
        string temporary = Path.Combine(dir, "probe-result.tmp");
        File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));
        File.Move(temporary, Path.Combine(dir, "probe-result.txt"));
        return 0;
    }
}
