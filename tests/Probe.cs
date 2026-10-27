using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

class Probe
{
    static bool Tcp(IPAddress ip, int port)
    {
        try { using (var client = new TcpClient(ip.AddressFamily)) { var ar = client.BeginConnect(ip, port, null, null); using (ar.AsyncWaitHandle) { if (!ar.AsyncWaitHandle.WaitOne(120)) return false; } client.EndConnect(ar); return true; } } catch { return false; }
    }
    static bool Udp(UdpClient client)
    {
        try { byte[] data = BitConverter.GetBytes(DateTime.UtcNow.Ticks); client.Send(data, data.Length); IPEndPoint remote = null; byte[] received = client.Receive(ref remote); return BitConverter.ToInt64(received, 0) == BitConverter.ToInt64(data, 0); } catch { return false; }
    }
    static int Main(string[] args)
    {
        using (var log = new StreamWriter(args[0]))
        using (var u4 = new UdpClient(AddressFamily.InterNetwork))
        using (var u6 = new UdpClient(AddressFamily.InterNetworkV6))
        {
            log.AutoFlush = true; u4.Client.ReceiveTimeout = 100; u6.Client.ReceiveTimeout = 100;
            u4.Connect(IPAddress.Loopback, Int32.Parse(args[3])); u6.Connect(IPAddress.IPv6Loopback, Int32.Parse(args[4]));
            for (int i = 0; i < 230; i++)
            {
                Func<bool>[] probes = { delegate { return Tcp(IPAddress.Loopback, Int32.Parse(args[1])); }, delegate { return Tcp(IPAddress.IPv6Loopback, Int32.Parse(args[2])); }, delegate { return Udp(u4); }, delegate { return Udp(u6); } };
                string[] labels = { "TCP4", "TCP6", "UDP4", "UDP6" };
                for (int j = 0; j < 4; j++) { long start = DateTime.UtcNow.Ticks; bool ok = probes[j](); log.WriteLine(start + "," + DateTime.UtcNow.Ticks + "," + labels[j] + "," + (ok ? "OK" : "FAIL")); }
                Thread.Sleep(55);
            }
        }
        return 0;
    }
}
