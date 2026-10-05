using System;
using System.Text.RegularExpressions;

namespace LearningIme {
    // Retain only our own typed buffer when Enter immediately clears a chat/search edit.
    internal sealed class SubmissionRetention {
        private string candidate = "";
        private DateTime until;
        public void Clear() { candidate = ""; until = DateTime.MinValue; }
        public void Enter(string typed, bool composing, bool shifted, DateTime now) {
            Clear();
            if (composing || shifted || string.IsNullOrEmpty(typed) || Regex.IsMatch(typed, @"[a-zA-Z']+$")) return;
            var snapshot = new Snapshot { scope = "submission", text = typed, typed_only = true };
            if (snapshot.Sentence().Length == 0) return;
            candidate = typed.Length <= 512 ? typed : typed.Substring(typed.Length - 512);
            until = now.AddMilliseconds(1500);
        }
        public bool TakeIfCleared(string currentTail, DateTime now, out string typed) {
            typed = "";
            if (now > until) { Clear(); return false; }
            if (candidate.Length == 0 || !string.IsNullOrWhiteSpace(currentTail)) return false;
            typed = candidate; Clear(); return true;
        }
    }
}
