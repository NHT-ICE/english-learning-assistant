using System;

namespace LearningIme {
    // 只累积基线之后的增量；不把光标前已有正文当作学习内容。
    internal sealed class InputSession {
        private string previous = "";
        public string Text { get; private set; } = "";
        public bool Armed { get; private set; }
        public void Baseline(string text) { previous = text ?? ""; Text = ""; Armed = false; }
        public void Arm() { Armed = true; }
        public InputSessionState Capture() {
            return Text.Length == 0 ? null : new InputSessionState { Previous = previous, Text = Text, SavedAt = DateTime.UtcNow };
        }
        public bool Restore(InputSessionState state, string current) {
            // Only reuse previously captured input when this exact editor tail
            // is unchanged. Never adopt old document text as new input.
            if (state == null || DateTime.UtcNow - state.SavedAt > TimeSpan.FromMinutes(5)
                || state.Previous != (current ?? "")) return false;
            previous = current ?? ""; Text = state.Text; Armed = false;
            return true;
        }
        public bool Update(string current) {
            current = current ?? "";
            if (!Armed) { previous = current; return false; }
            if (current == previous) return false;
            if (current.StartsWith(previous, StringComparison.Ordinal)) Text += current.Substring(previous.Length);
            else if (previous.StartsWith(current, StringComparison.Ordinal)) {
                int deleted = previous.Length - current.Length;
                if (deleted <= Text.Length) Text = Text.Substring(0, Text.Length - deleted);
                else { Baseline(current); return true; }
            } else {
                // IME 会把临时拼音替换为中文。只有替换完全落在本次输入区内才接受。
                int protectedLength = Math.Max(0, previous.Length - Text.Length);
                int common = 0;
                while (common < previous.Length && common < current.Length && previous[common] == current[common]) common++;
                bool latinComposition = previous.Length > 0;
                foreach (char letter in previous) if (!(letter >= 'a' && letter <= 'z') && !(letter >= 'A' && letter <= 'Z') && letter != '\'') latinComposition = false;
                if (previous.EndsWith(Text, StringComparison.Ordinal) && common >= protectedLength
                    && (protectedLength > 0 || common > 0 || latinComposition)) {
                    Text = current.Substring(protectedLength);
                    previous = current;
                    Trim();
                    return true;
                }
                // 读取窗口滚动时，只有“旧尾部＋新尾部”能明确确认成追加。
                int overlap = Math.Min(previous.Length, current.Length);
                while (overlap > 0 && !current.StartsWith(previous.Substring(previous.Length - overlap), StringComparison.Ordinal)) overlap--;
                if (overlap >= Math.Min(32, previous.Length) && overlap > 0) Text += current.Substring(overlap);
                else { Baseline(current); return true; }
            }
            previous = current;
            Trim();
            return true;
        }
        private void Trim() {
            if (Text.Length > 512) {
                int start = Text.Length - 512;
                if (char.IsLowSurrogate(Text[start])) start++;
                Text = Text.Substring(start);
            }
        }
    }
    internal sealed class InputSessionState {
        public string Previous, Text;
        public DateTime SavedAt;
    }
}
