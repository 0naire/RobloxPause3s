using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RobloxPause
{
    // Windows SDK fwpmtypes.h / fwptypes.h. Built as x64 for these native layouts.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DisplayData { [MarshalAs(UnmanagedType.LPWStr)] public string name; [MarshalAs(UnmanagedType.LPWStr)] public string description; }
    [StructLayout(LayoutKind.Sequential)]
    public struct ByteBlob { public uint size; public IntPtr data; }
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct Value { [FieldOffset(0)] public uint type; [FieldOffset(8)] public IntPtr pointer; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Condition { public Guid fieldKey; public uint matchType; public Value value; }
    [StructLayout(LayoutKind.Sequential)]
    public struct ActionData { public uint type; public Guid key; }
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct ContextData { [FieldOffset(0)] public ulong raw; [FieldOffset(0)] public Guid provider; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Filter
    {
        public Guid key; public DisplayData display; public uint flags; public IntPtr provider;
        public ByteBlob providerData; public Guid layer; public Guid subLayer; public Value weight;
        public uint conditionCount; public IntPtr conditions; public ActionData action;
        public ContextData context; public IntPtr reserved; public ulong id; public Value effectiveWeight;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct Session
    {
        public Guid key; public DisplayData display; public uint flags; public uint timeout;
        public uint processId; public IntPtr sid; public IntPtr username; public int kernel;
    }
    internal static class Native
    {
        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
        internal static extern uint FwpmEngineOpen0(string server, uint auth, IntPtr identity, ref Session session, out IntPtr engine);
        [DllImport("fwpuclnt.dll")] internal static extern uint FwpmEngineClose0(IntPtr engine);
        [DllImport("fwpuclnt.dll")] internal static extern uint FwpmTransactionBegin0(IntPtr engine, uint flags);
        [DllImport("fwpuclnt.dll")] internal static extern uint FwpmTransactionCommit0(IntPtr engine);
        [DllImport("fwpuclnt.dll")] internal static extern uint FwpmTransactionAbort0(IntPtr engine);
        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
        internal static extern uint FwpmGetAppIdFromFileName0(string file, out IntPtr appId);
        [DllImport("fwpuclnt.dll")] internal static extern void FwpmFreeMemory0(ref IntPtr memory);
        [DllImport("fwpuclnt.dll", CharSet = CharSet.Unicode)]
        internal static extern uint FwpmFilterAdd0(IntPtr engine, ref Filter filter, IntPtr security, out ulong id);
    }
    public sealed class WfpSession : IDisposable
    {
        private IntPtr engine;
        private bool transaction;
        private static readonly Guid AppId = new Guid("d78e1e87-8644-4ea5-9437-d809ecefc971");
        private static readonly Guid[] Layers = {
            new Guid("c38d57d1-05a7-4c33-904f-7fbceee60e82"),
            new Guid("4a72393b-319f-44bc-84c3-ba54dcb3b6b4"),
            new Guid("e1cd9fe7-f4b5-4273-96c0-592e487b8650"),
            new Guid("a3b42c97-9f04-4672-b87e-cee9c483257f") };
        public WfpSession()
        {
            if (IntPtr.Size != 8 || Marshal.SizeOf(typeof(Filter)) != 200 || Marshal.SizeOf(typeof(Condition)) != 40)
                throw new InvalidOperationException("64비트 Windows용 프로그램을 실행해 주세요.");
            var session = new Session { key = Guid.NewGuid(), flags = 1, timeout = 1500,
                display = new DisplayData { name = "0NAIRE Roblox Pause 3s", description = "Temporary dynamic session; removed automatically on process exit." } };
            Check(Native.FwpmEngineOpen0(null, 10, IntPtr.Zero, ref session, out engine), "연결 제어 준비");
        }
        public void Block(string[] executables)
        {
            if (executables == null || executables.Length == 0) throw new ArgumentException("로블록스를 먼저 실행해 주세요.");
            Check(Native.FwpmTransactionBegin0(engine, 0), "차단 준비");
            transaction = true;
            try
            {
                foreach (string file in executables) AddApplication(file);
                Check(Native.FwpmTransactionCommit0(engine), "차단 적용");
                transaction = false;
            }
            catch
            {
                if (transaction) { Native.FwpmTransactionAbort0(engine); transaction = false; }
                throw;
            }
        }
        private void AddApplication(string file)
        {
            IntPtr blob = IntPtr.Zero, conditionMemory = IntPtr.Zero;
            try
            {
                Check(Native.FwpmGetAppIdFromFileName0(file, out blob), "로블록스 경로 확인");
                if (blob == IntPtr.Zero) throw new InvalidOperationException("실행 파일 경로를 확인하지 못했습니다.");
                var condition = new Condition { fieldKey = AppId, matchType = 0, value = new Value { type = 12, pointer = blob } };
                conditionMemory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Condition)));
                Marshal.StructureToPtr(condition, conditionMemory, false);
                foreach (Guid layer in Layers)
                {
                    var filter = new Filter { key = Guid.NewGuid(), layer = layer,
                        display = new DisplayData { name = "0NAIRE Roblox Pause 3s", description = file },
                        conditionCount = 1, conditions = conditionMemory, action = new ActionData { type = 0x1001 } };
                    ulong id;
                    Check(Native.FwpmFilterAdd0(engine, ref filter, IntPtr.Zero, out id), "연결 차단 필터 추가");
                }
            }
            finally
            {
                if (conditionMemory != IntPtr.Zero) Marshal.FreeHGlobal(conditionMemory);
                if (blob != IntPtr.Zero) Native.FwpmFreeMemory0(ref blob);
            }
        }
        private static void Check(uint code, string operation)
        {
            if (code != 0) throw new InvalidOperationException(operation + " 실패 (0x" + code.ToString("X8") + "): " + new Win32Exception(unchecked((int)code)).Message);
        }
        public void Dispose()
        {
            if (engine == IntPtr.Zero) return;
            if (transaction) { Native.FwpmTransactionAbort0(engine); transaction = false; }
            uint result = Native.FwpmEngineClose0(engine);
            if (result != 0) throw new InvalidOperationException("차단 해제 확인 실패 (0x" + result.ToString("X8") + ")");
            engine = IntPtr.Zero;
            GC.SuppressFinalize(this);
        }
        ~WfpSession() { if (engine != IntPtr.Zero) Native.FwpmEngineClose0(engine); }
    }
}
