using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LearningUia;

namespace LearningIme {
    // 只沿当前焦点查找，不枚举聊天内容。
    internal sealed class LegacyInput {
        [DllImport("oleacc.dll")]
        static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        private IAccessible accessible;
        private object child = 0;
        public string Scope;
        public int Role, State;
        public bool Editable { get { return Role == 42 && (State & 0x20000040) == 0; } }
        public static LegacyInput Focus(int expectedProcess) {
            var window = GetForegroundWindow();
            uint pid; GetWindowThreadProcessId(window, out pid);
            if (pid != expectedProcess) return null;
            // 仅针对微信，不读取 Word 的整篇 Value。
            using (var process = Process.GetProcessById(expectedProcess)) {
                if (process.ProcessName != "Weixin" && process.ProcessName != "WeChat") return null;
            }
            Guid iid = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
            IAccessible root;
            if (AccessibleObjectFromWindow(window, 0xFFFFFFFC, ref iid, out root) != 0 || root == null) return null;
            object child = 0;
            for (int depth = 0; depth < 8; depth++) {
                object focused = root.accFocus;
                var nested = focused as IAccessible;
                if (nested != null) { root = nested; continue; }
                if (focused is int) child = focused;
                break;
            }
            var result = new LegacyInput { accessible = root, child = child };
            result.Role = Convert.ToInt32(root.get_accRole(child));
            result.State = Convert.ToInt32(root.get_accState(child));
            IntPtr identity = Marshal.GetIUnknownForObject(root);
            try { result.Scope = expectedProcess + ":msaa:" + identity.ToInt64() + ":" + child; }
            finally { Marshal.Release(identity); }
            return result;
        }
        public string Tail() {
            if (!Editable) return "";
            string value = accessible.get_accValue(child) ?? "";
            return value.Substring(Math.Max(0, value.Length - 512));
        }
        public Anchor Anchor() {
            int x = 0, y = 0, width = 0, height = 0;
            accessible.accLocation(out x, out y, out width, out height, child);
            return new Anchor { left = x, top = y, right = x + width, bottom = y + Math.Min(height, 32) };
        }
    }
}
