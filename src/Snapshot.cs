using System.Text.RegularExpressions;

namespace LearningIme {
    internal sealed class Snapshot {
        public string scope { get; set; }
        public string text { get; set; }
        public bool composing { get; set; }
        public Anchor rect { get; set; }
        public string command { get; set; }
        public string reason { get; set; }
        public bool preserve_display { get; set; }
        public bool typed_only { get; set; }
        public int bridge_messages { get; set; }
        public int bridge_commits { get; set; }

        public string Sentence() {
            if (composing || string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(text)) return "";
            string value = Regex.Replace(text.Trim(), @"[\r\n]+", " ");
            // The observer already excludes old document text. Preserve all of
            // this captured input, capped at 240 characters, rather than dropping
            // previous questions after two sentences.
            if (value.Length > 240) value = value.Substring(value.Length - 240).Trim();
            if (Regex.Matches(value, @"[\u3400-\u9fff]").Count < 2) return "";
            return value;
        }
    }
    internal sealed class Anchor {
        public int left { get; set; }
        public int top { get; set; }
        public int right { get; set; }
        public int bottom { get; set; }
    }
}
