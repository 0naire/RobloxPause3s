using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RobloxPause
{
    internal static class Program
    {
        public const int DurationMs = 3000;
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--worker") return Worker(args[1], args[2]);
            if (args.Length == 2 && args[0] == "--diagnose") return Diagnose(args[1]);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            bool first;
            using (var mutex = new Mutex(true, @"Local\0NAIRE.RobloxPause3s.UI", out first))
            {
                if (!first) { MessageBox.Show("이미 실행 중입니다. 작업 표시줄에서 Roblox 3초 연결 제어를 열어 주세요."); return 0; }
                try { Application.Run(new MainForm()); return 0; }
                catch (Exception ex) { MessageBox.Show(ex.Message, "실행 오류", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            }
        }
        private static int Worker(string token, string encodedPaths)
        {
            // The worker owns the dynamic WFP session. UI freezes/exits cannot extend its 3-second timer.
            Guid parsed;
            if (!Guid.TryParseExact(token, "N", out parsed)) return 10;
            EventWaitHandle ready = null, cancel = null, done = null;
            try
            {
                ready = EventWaitHandle.OpenExisting(EventName(token, "ready"));
                cancel = EventWaitHandle.OpenExisting(EventName(token, "cancel"));
                done = EventWaitHandle.OpenExisting(EventName(token, "done"));
                string[] paths = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPaths)).Split('\n');
                if (paths.Length < 1 || paths.Length > 8) throw new InvalidOperationException("대상 수가 올바르지 않습니다.");
                foreach (string path in paths)
                    if (!Path.IsPathRooted(path) || !File.Exists(path) || !IsRoblox(path)) throw new InvalidOperationException("로블록스 실행 파일만 차단할 수 있습니다.");
                using (var session = new WfpSession())
                {
                    if (cancel.WaitOne(0)) return 0;
                    session.Block(paths);
                    ready.Set();
                    cancel.WaitOne(DurationMs);
                }
                return 0;
            }
            catch (Exception ex) { WriteLog(ex.ToString()); return 1; }
            finally
            {
                if (done != null) { done.Set(); done.Dispose(); }
                if (cancel != null) cancel.Dispose();
                if (ready != null) ready.Dispose();
                // Returning ends this process, so Windows removes this dynamic session even if close failed.
            }
        }
        public static string EventName(string token, string kind) { return @"Local\0NAIRE.RobloxPause3s." + token + "." + kind; }
        public static bool IsRoblox(string path) { return String.Equals(Path.GetFileName(path), "RobloxPlayerBeta.exe", StringComparison.OrdinalIgnoreCase); }
        public static string[] FindRoblox()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcessesByName("RobloxPlayerBeta"))
            {
                using (p) { try { string path = p.MainModule.FileName; if (IsRoblox(path) && File.Exists(path)) paths.Add(path); } catch { } }
            }
            return paths.ToArray();
        }
        public static bool Admin { get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } }
        private static int Diagnose(string path)
        {
            try
            {
                using (new WfpSession()) { }
                File.WriteAllLines(path, new[] { "OK", "Admin=" + Admin, "OS=" + Environment.OSVersion,
                    "FilterSize=" + Marshal.SizeOf(typeof(Filter)), "ConditionSize=" + Marshal.SizeOf(typeof(Condition)),
                    "SessionSize=" + Marshal.SizeOf(typeof(Session)), "Roblox=" + String.Join(";", FindRoblox()) });
                return 0;
            }
            catch (Exception ex) { File.WriteAllText(path, ex.ToString()); return 1; }
        }
        public static void WriteLog(string text)
        {
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"), DateTime.Now.ToString("s") + " " + text + Environment.NewLine); } catch { }
        }
    }

    internal sealed class MainForm : Form
    {
        private const int CutHotkey = 301, RestoreHotkey = 302;
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
        private readonly Label state = new Label(), target = new Label(), countdown = new Label(), hotkeyState = new Label();
        private readonly Button cut = new Button(), restore = new Button();
        private readonly ComboBox key = new ComboBox();
        private readonly System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer();
        private readonly Stopwatch clock = new Stopwatch();
        private readonly object gate = new object();
        private EventWaitHandle cancelSignal;
        private Process worker;
        private bool busy, closing, hotkeyRegistered;
        private volatile bool cancelRequested;
        private int tickCount;
        private string lastIssue;
        public MainForm()
        {
            Text = "Roblox 3초 연결 제어 · 0NAIRE"; ClientSize = new Size(486, 450);
            Font = new Font("맑은 고딕", 10); BackColor = Color.FromArgb(20, 24, 33); ForeColor = Color.FromArgb(235, 240, 250);
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AddText("ROBLOX  /  3 SECOND PAUSE", 27, 23, 435, 27, 11, Color.FromArgb(109, 169, 255), FontStyle.Bold);
            AddText("로블록스 연결을 잠깐 멈추기", 25, 58, 445, 39, 19, ForeColor, FontStyle.Bold);
            AddText("한 번 누르면 3초 후 차단을 자동으로 해제합니다.", 27, 105, 435, 27, 10, Color.FromArgb(169, 180, 201), FontStyle.Regular);
            state.SetBounds(27, 149, 295, 30); state.Font = new Font(Font.FontFamily, 13, FontStyle.Bold); Controls.Add(state);
            target.SetBounds(27, 182, 295, 42); target.ForeColor = Color.FromArgb(169, 180, 201); target.Font = new Font(Font.FontFamily, 9); Controls.Add(target);
            countdown.SetBounds(338, 144, 118, 70); countdown.Text = "3.0"; countdown.TextAlign = ContentAlignment.MiddleRight;
            countdown.Font = new Font("Segoe UI", 32, FontStyle.Bold); countdown.ForeColor = Color.FromArgb(109, 169, 255); Controls.Add(countdown);
            AddText("단축키", 27, 234, 75, 28, 10, ForeColor, FontStyle.Regular);
            key.SetBounds(102, 230, 102, 30); key.DropDownStyle = ComboBoxStyle.DropDownList;
            key.Items.AddRange(new object[] { "F6", "F7", "F8", "F9", "F10", "F11", "F12" });
            key.SelectedItem = LoadKey(); key.SelectedIndexChanged += delegate { BindKeys(); SaveKey(); }; Controls.Add(key);
            hotkeyState.SetBounds(217, 234, 240, 29); hotkeyState.Font = new Font(Font.FontFamily, 9); Controls.Add(hotkeyState);
            cut.Text = "3초간 연결 끊기"; cut.SetBounds(27, 279, 268, 51); StyleButton(cut, Color.FromArgb(51, 112, 224)); cut.Click += delegate { StartCycle(); }; Controls.Add(cut);
            restore.Text = "즉시 복구"; restore.SetBounds(309, 279, 148, 51); StyleButton(restore, Color.FromArgb(51, 59, 75)); restore.Enabled = false;
            restore.Click += delegate { CancelCycle(); }; Controls.Add(restore);
            AddText("즉시 복구 단축키: Ctrl + Alt + R", 27, 348, 430, 26, 10, Color.FromArgb(169, 180, 201), FontStyle.Regular);
            AddText("3초 뒤 네트워크 차단은 해제됩니다. 서버가 접속을\n종료한 경우에는 직접 재접속해야 할 수 있습니다.", 27, 382, 435, 51, 9, Color.FromArgb(143, 153, 173), FontStyle.Regular);
            uiTimer.Interval = 50; uiTimer.Tick += delegate { Tick(); }; uiTimer.Start();
            Shown += delegate { BindKeys(); RefreshTarget(); };
            FormClosing += delegate { closing = true; uiTimer.Stop(); CancelCycle(); StopWorker(); UnregisterHotKey(Handle, CutHotkey); UnregisterHotKey(Handle, RestoreHotkey); };
        }
        private void AddText(string text, int x, int y, int w, int h, float size, Color color, FontStyle style)
        { Controls.Add(new Label { Text = text, Left = x, Top = y, Width = w, Height = h, Font = new Font(Font.FontFamily, size, style), ForeColor = color }); }
        private static void StyleButton(Button button, Color color)
        { button.BackColor = color; button.ForeColor = Color.White; button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 0; button.Cursor = Cursors.Hand; }
        private string SettingsPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hotkey.txt"); } }
        private string LoadKey()
        { try { string value = File.ReadAllText(SettingsPath).Trim(); if (new[] { "F6", "F7", "F8", "F9", "F10", "F11", "F12" }.Contains(value)) return value; } catch { } return "F8"; }
        private void SaveKey() { try { File.WriteAllText(SettingsPath, (string)key.SelectedItem); } catch { } }
        private void BindKeys()
        {
            if (!IsHandleCreated) return;
            UnregisterHotKey(Handle, CutHotkey); UnregisterHotKey(Handle, RestoreHotkey);
            Keys value = (Keys)Enum.Parse(typeof(Keys), (string)key.SelectedItem);
            hotkeyRegistered = RegisterHotKey(Handle, CutHotkey, 0x4000, (uint)value);
            bool recovery = RegisterHotKey(Handle, RestoreHotkey, 0x4000 | 1 | 2, (uint)Keys.R);
            hotkeyState.Text = hotkeyRegistered ? "게임 중에도 사용 가능" : "다른 키를 골라 주세요";
            hotkeyState.ForeColor = hotkeyRegistered ? Color.FromArgb(132, 204, 171) : Color.FromArgb(255, 172, 114);
            if (!recovery) hotkeyState.Text += " · 복구는 버튼 사용";
        }
        protected override void WndProc(ref Message m)
        { if (m.Msg == 0x0312) { if ((int)m.WParam == CutHotkey) StartCycle(); if ((int)m.WParam == RestoreHotkey) CancelCycle(); } base.WndProc(ref m); }
        private void RefreshTarget()
        {
            if (busy || closing) return;
            string[] paths = Program.FindRoblox();
            cut.Enabled = paths.Length > 0 && Program.Admin;
            state.Text = lastIssue ?? (paths.Length > 0 ? "연결 중 · 사용 준비 완료" : "로블록스를 먼저 실행해 주세요");
            state.ForeColor = lastIssue == null ? ForeColor : Color.FromArgb(255, 172, 114);
            target.Text = paths.Length > 0 ? "RobloxPlayerBeta.exe 자동 감지\n업데이트된 실행 경로도 자동으로 확인합니다." : "게임이 열리면 자동으로 감지합니다.";
        }
        private void Tick()
        {
            if (clock.IsRunning) countdown.Text = Math.Max(0, 3 - clock.Elapsed.TotalSeconds).ToString("0.0");
            if (++tickCount % 40 == 0) RefreshTarget();
        }
        private async void StartCycle()
        {
            if (busy || closing) return;
            string[] paths = Program.FindRoblox();
            if (paths.Length == 0) { RefreshTarget(); return; }
            busy = true; cancelRequested = false; lastIssue = null; cut.Enabled = false; key.Enabled = false; restore.Enabled = true;
            state.Text = "연결 제어 준비 중…";
            try { await Task.Run(delegate { RunWorker(paths); }); }
            catch (Exception ex) { lastIssue = "실행하지 못했습니다"; Program.WriteLog(ex.ToString()); if (!closing) MessageBox.Show(this, ex.Message, "연결 제어", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            finally
            {
                clock.Reset(); busy = false;
                if (!closing) { countdown.Text = "3.0"; key.Enabled = true; restore.Enabled = false; RefreshTarget(); }
            }
        }
        private void RunWorker(string[] paths)
        {
            string token = Guid.NewGuid().ToString("N");
            using (var ready = new EventWaitHandle(false, EventResetMode.ManualReset, Program.EventName(token, "ready")))
            using (var cancel = new EventWaitHandle(false, EventResetMode.ManualReset, Program.EventName(token, "cancel")))
            using (var done = new EventWaitHandle(false, EventResetMode.ManualReset, Program.EventName(token, "done")))
            {
                lock (gate) { cancelSignal = cancel; if (closing || cancelRequested) cancel.Set(); }
                try
                {
                    if (cancel.WaitOne(0)) return;
                    string arg = Convert.ToBase64String(Encoding.UTF8.GetBytes(String.Join("\n", paths)));
                    var info = new ProcessStartInfo(Application.ExecutablePath, "--worker " + token + " " + arg) { UseShellExecute = false, CreateNoWindow = true };
                    lock (gate) { if (closing || cancelRequested) return; worker = Process.Start(info); }
                    int result = WaitHandle.WaitAny(new WaitHandle[] { ready, done }, 8000);
                    if (result == WaitHandle.WaitTimeout) { cancel.Set(); throw new InvalidOperationException("준비 시간이 초과되어 차단을 취소했습니다."); }
                    if (result == 0 && !closing)
                        BeginInvoke((System.Action)delegate { if (!closing) { clock.Restart(); state.Text = "연결 일시 중단 · 3초"; state.ForeColor = Color.FromArgb(255, 172, 114); } });
                    if (!done.WaitOne(4500)) { cancel.Set(); throw new InvalidOperationException("자동 복구를 위해 연결 제어 작업을 종료했습니다."); }
                    Process current; lock (gate) current = worker;
                    if (current != null && current.WaitForExit(1500) && current.ExitCode != 0)
                        throw new InvalidOperationException("연결 제어에 실패했습니다. 같은 폴더의 error.log를 확인해 주세요.");
                }
                finally
                {
                    cancel.Set(); StopWorker();
                    lock (gate) cancelSignal = null;
                }
            }
        }
        private void CancelCycle() { cancelRequested = true; lock (gate) { if (cancelSignal != null) { try { cancelSignal.Set(); } catch (ObjectDisposedException) { } } } }
        private void StopWorker()
        {
            Process current; lock (gate) { current = worker; worker = null; }
            if (current != null) { try { if (!current.WaitForExit(500)) { current.Kill(); current.WaitForExit(1000); } } catch { } finally { current.Dispose(); } }
        }
    }
}
