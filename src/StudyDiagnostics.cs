using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Web.Script.Serialization;

namespace LearningIme {
    // 只记录触发状态与字符数量，供不同宿主的兼容性排查；不保存输入或译文。
    internal sealed class StudyDiagnostics {
        private readonly string path;
        private readonly Dictionary<string, object> hosts = new Dictionary<string, object>();
        private string lastScope = "";
        private string displayScope = "";
        private int displayLength;
        private bool translationFailed;
        private object window;
        private readonly Queue<object> windowEvents = new Queue<object>();
        private readonly Queue<object> inputEvents = new Queue<object>();
        public StudyDiagnostics(string root) { path = Path.Combine(root, "popup-diagnostics.json"); }
        public void Accept(Snapshot snapshot) {
            if (!DiagnosticsPolicy.Enabled) return;
            lastScope = snapshot.scope ?? "";
            inputEvents.Enqueue(new { utc = DateTime.UtcNow.ToString("o"), snapshot.scope, snapshot.reason, snapshot.composing, snapshot.preserve_display, length = (snapshot.text ?? "").Length,
                chinese_length = System.Text.RegularExpressions.Regex.Matches(snapshot.text ?? "", @"[\u3400-\u9fff]").Count });
            while (inputEvents.Count > 64) inputEvents.Dequeue();
            if (lastScope.Length > 0) {
                int pid;
                string process = "unknown";
                if (int.TryParse(lastScope.Split(':')[0], out pid)) {
                    try { using (var host = Process.GetProcessById(pid)) process = host.ProcessName; } catch (Exception) { }
                }
                if (hosts.Count >= 32 && !hosts.ContainsKey(lastScope)) hosts.Clear();
                hosts[lastScope] = new {
                    process, composing = snapshot.composing,
                    typed_only = snapshot.typed_only,
                    text_length = (snapshot.text ?? "").Length,
                    accepted_sentence_length = snapshot.Sentence().Length,
                    utc = DateTime.UtcNow.ToString("o")
                };
            }
            Save();
        }
        public void Display(Snapshot snapshot, int length, bool failed = false) {
            if (!DiagnosticsPolicy.Enabled) return;
            displayScope = snapshot.scope ?? ""; displayLength = length; translationFailed = failed; Save();
        }
        public void Window(string reason, object state) {
            if (!DiagnosticsPolicy.Enabled) return;
            window = state;
            windowEvents.Enqueue(new { utc = DateTime.UtcNow.ToString("o"), reason, state });
            while (windowEvents.Count > 64) windowEvents.Dequeue();
            Save();
        }
        private void Save() {
            try {
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(new {
                    utc = DateTime.UtcNow.ToString("o"), last_scope = lastScope,
                    display_scope = displayScope, translation_length = displayLength, translation_failed = translationFailed, hosts, window, window_events = windowEvents.ToArray(), input_events = inputEvents.ToArray()
                }));
            } catch (IOException) { }
        }
    }
}
