using System;

namespace LearningIme {
    internal static class InputCapturePolicy {
        public static bool CurrentDraft(string process, int control) {
            if (control != 50004) return false; // Only a dedicated edit, never a page/document.
            return string.Equals(process, "ChatGPT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(process, "Codex", StringComparison.OrdinalIgnoreCase)
                || string.Equals(process, "msedge", StringComparison.OrdinalIgnoreCase)
                || string.Equals(process, "chrome", StringComparison.OrdinalIgnoreCase);
        }
    }
}
