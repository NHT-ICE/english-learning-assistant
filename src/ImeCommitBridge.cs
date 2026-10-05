using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LearningIme {
    internal sealed class ImeCommitBridge : NativeWindow, IDisposable {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int kind, IntPtr callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [StructLayout(LayoutKind.Sequential)] private struct CopyData { public IntPtr id; public int length; public IntPtr data; }
        private readonly Action<Snapshot> deliver;
        private readonly IntPtr module;
        private IntPtr sent, posted;
        private uint thread, process;
        private volatile bool paused;
        public volatile bool Enabled;
        public int Error { get; private set; }
        public bool Active { get { return sent != IntPtr.Zero && posted != IntPtr.Zero; } }
        public ImeCommitBridge(Action<Snapshot> deliver) {
            this.deliver = deliver;
            CreateHandle(new CreateParams { Caption = "EnglishLearningAssistant.CommitReceiver", Parent = new IntPtr(-3) });
            module = LoadLibrary(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImeCommitBridge.dll"));
        }
        // 仅对前台微信的一个 UI 线程启用，不全局注入，也不改变微信安装文件。
        public void Observe(uint pid, uint targetThread) {
            if (!Enabled || paused || module == IntPtr.Zero) { Stop(); return; }
            if (pid == process && targetThread == thread && Active) return;
            Stop(); process = pid; thread = targetThread;
            sent = SetWindowsHookEx(4, GetProcAddress(module, "ObserveSent"), module, thread);
            posted = SetWindowsHookEx(3, GetProcAddress(module, "ObservePosted"), module, thread);
            Error = Active ? 0 : Marshal.GetLastWin32Error();
        }
        protected override void WndProc(ref Message message) {
            if (message.Msg == 0x4A && !paused) {
                var data = (CopyData)Marshal.PtrToStructure(message.LParam, typeof(CopyData));
                if (data.id.ToInt64() == 0x454C494D && data.length > 0 && data.length <= 8192) {
                    uint sender; GetWindowThreadProcessId(message.WParam, out sender);
                    uint foreground; uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
                    if (sender == process && foreground == process && foregroundThread == thread) {
                        try { deliver(new JavaScriptSerializer().Deserialize<Snapshot>(Marshal.PtrToStringUni(data.data, data.length / 2 - 1))); } catch (Exception) { }
                    }
                }
                message.Result = new IntPtr(1); return;
            }
            base.WndProc(ref message);
        }
        public void Stop() {
            if (sent != IntPtr.Zero) UnhookWindowsHookEx(sent);
            if (posted != IntPtr.Zero) UnhookWindowsHookEx(posted);
            sent = posted = IntPtr.Zero; process = thread = 0;
        }
        public void Pause(bool value) { paused = value; if (value) Stop(); }
        public void Dispose() { Stop(); DestroyHandle(); if (module != IntPtr.Zero) FreeLibrary(module); }
    }
}
