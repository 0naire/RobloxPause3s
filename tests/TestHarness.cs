using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RobloxPause;

class TestHarness
{
    static readonly List<string> Report = new List<string>();
    static string EventName(string token, string kind) { return @"Local\0NAIRE.RobloxPause3s." + token + "." + kind; }
    static Process Start(string file, string args) { return Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true }); }
    static string Q(string s) { return "\"" + s + "\""; }
    static void Need(bool value, string message) { if (!value) throw new Exception(message); Report.Add("PASS " + message); }
    static void Stop(Process p) { if (p == null) return; try { if (!p.HasExited) { p.Kill(); p.WaitForExit(2000); } } catch { } p.Dispose(); }
    static void Echo(TcpListener server) { Task.Run(delegate { try { while (true) using (var c = server.AcceptTcpClient()) { } } catch { } }); }
    static void Echo(UdpClient server) { Task.Run(delegate { try { while (true) { IPEndPoint remote = null; byte[] data = server.Receive(ref remote); server.Send(data, data.Length, remote); } } catch { } }); }
    static void CheckWindow(string file, long start, long end, bool expected, string phase)
    {
        string[] lines;
        using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream)) lines = reader.ReadToEnd().Split('\n');
        foreach (string kind in new[] { "TCP4", "TCP6", "UDP4", "UDP6" })
        {
            var samples = lines.Select(l => l.Trim().Split(',')).Where(p => p.Length == 4 && p[2] == kind && Int64.Parse(p[0]) >= start && Int64.Parse(p[1]) <= end).ToArray();
            Need(samples.Length > 0 && samples.All(p => (p[3] == "OK") == expected), phase + " " + kind + " (samples=" + samples.Length + ")");
        }
    }
    static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--crash-worker")
        {
            using (var session = new WfpSession()) { session.Block(new[] { args[1] }); File.WriteAllText(args[2], "READY"); Thread.Sleep(30000); }
            return 0;
        }
        string root = Path.GetDirectoryName(typeof(TestHarness).Assembly.Location);
        string app = Path.GetFullPath(Path.Combine(root, "..", "..", "dist", "RobloxPause3s.exe"));
        string probe = Path.Combine(root, "RobloxPlayerBeta.exe"), control = Path.Combine(root, "ControlProbe.exe");
        string probeLog = Path.Combine(root, "target.csv"), controlLog = Path.Combine(root, "control.csv");
        Process p = null, c = null, w = null;
        var t4 = new TcpListener(IPAddress.Loopback, 0); var t6 = new TcpListener(IPAddress.IPv6Loopback, 0);
        var u4 = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)); var u6 = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        try
        {
            t4.Start(); t6.Start(); Echo(t4); Echo(t6); Echo(u4); Echo(u6);
            string ports = ((IPEndPoint)t4.LocalEndpoint).Port + " " + ((IPEndPoint)t6.LocalEndpoint).Port + " " + ((IPEndPoint)u4.Client.LocalEndPoint).Port + " " + ((IPEndPoint)u6.Client.LocalEndPoint).Port;
            p = Start(probe, Q(probeLog) + " " + ports); c = Start(control, Q(controlLog) + " " + ports);
            Thread.Sleep(1300);
            long now = DateTime.UtcNow.Ticks;
            CheckWindow(probeLog, now-TimeSpan.FromMilliseconds(850).Ticks, now, true, "baseline");
            string token = Guid.NewGuid().ToString("N");
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(token,"ready")))
            using (var cancel = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(token,"cancel")))
            using (var done = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(token,"done")))
            {
                w = Start(app, "--worker " + token + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(probe)));
                Need(ready.WaitOne(5000), "real app worker ready, isolated test executable only");
                long begin = DateTime.UtcNow.Ticks; var clock = Stopwatch.StartNew();
                Need(done.WaitOne(5000), "real worker automatically completed");
                Need(w.WaitForExit(1500) && w.ExitCode == 0, "real worker clean exit");
                double ms = clock.Elapsed.TotalMilliseconds;
                Need(ms > 2800 && ms < 3700, "automatic duration " + ms.ToString("F1") + "ms");
                CheckWindow(probeLog, begin+TimeSpan.FromMilliseconds(200).Ticks, begin+TimeSpan.FromMilliseconds(2650).Ticks, false, "blocked");
                CheckWindow(controlLog, begin+TimeSpan.FromMilliseconds(200).Ticks, begin+TimeSpan.FromMilliseconds(2650).Ticks, true, "other app unaffected");
            }
            Stop(w); w = null; Thread.Sleep(800); now = DateTime.UtcNow.Ticks;
            CheckWindow(probeLog, now-TimeSpan.FromMilliseconds(450).Ticks, now, true, "automatic restore");
            token = Guid.NewGuid().ToString("N");
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(token,"ready")))
            using (var cancel = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(token,"cancel")))
            using (var done = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(token,"done")))
            {
                w = Start(app, "--worker " + token + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(probe)));
                Need(ready.WaitOne(5000), "manual restore worker ready"); Thread.Sleep(450); var clock = Stopwatch.StartNew(); cancel.Set();
                Need(done.WaitOne(1800), "manual restore completed");
                Need(clock.ElapsedMilliseconds < 1500, "manual restore within 1.5 seconds");
            }
            Stop(w); w = null; Thread.Sleep(800); now = DateTime.UtcNow.Ticks;
            CheckWindow(probeLog, now-TimeSpan.FromMilliseconds(450).Ticks, now, true, "manual restore");
            string readyFile = Path.Combine(root, "crash-ready.txt"); if (File.Exists(readyFile)) File.Delete(readyFile);
            w = Start(typeof(TestHarness).Assembly.Location, "--crash-worker " + Q(probe) + " " + Q(readyFile));
            var wait = Stopwatch.StartNew(); while (!File.Exists(readyFile) && wait.ElapsedMilliseconds < 5000) Thread.Sleep(25);
            Need(File.Exists(readyFile), "crash test dynamic filters active");
            long crashStart = DateTime.UtcNow.Ticks; Thread.Sleep(700);
            CheckWindow(probeLog, crashStart+TimeSpan.FromMilliseconds(100).Ticks, DateTime.UtcNow.Ticks, false, "before crash");
            Stop(w); w = null; Thread.Sleep(1200); now = DateTime.UtcNow.Ticks;
            CheckWindow(probeLog, now-TimeSpan.FromMilliseconds(600).Ticks, now, true, "process crash cleanup");
            Report.Add("PASS No running Roblox process was targeted. All traffic tests used localhost and two dedicated test executables.");
            File.WriteAllLines(Path.Combine(root, "test-results.txt"), Report); return 0;
        }
        catch (Exception ex) { Report.Add("FAIL " + ex); File.WriteAllLines(Path.Combine(root, "test-results.txt"), Report); return 1; }
        finally { Stop(w); Stop(p); Stop(c); t4.Stop(); t6.Stop(); u4.Dispose(); u6.Dispose(); }
    }
}
