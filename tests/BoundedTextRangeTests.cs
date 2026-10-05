using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using LearningUia;
using LearningIme;

internal static class BoundedTextRangeTests {
    // Model a provider whose endpoint movement can leave the edit and enter the page.
    private sealed class Range : IUIAutomationTextRange {
        public static int CharacterWalks;
        public readonly string Page;
        public int Start, End, OwnerStart, OwnerEnd;
        public Range(string page, int start, int end, int ownerStart, int ownerEnd) { Page = page; Start = start; End = end; OwnerStart = ownerStart; OwnerEnd = ownerEnd; }
        private int At(TextPatternRangeEndpoint endpoint) { return endpoint == TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start ? Start : End; }
        private void Set(TextPatternRangeEndpoint endpoint, int value) { if (endpoint == TextPatternRangeEndpoint.TextPatternRangeEndpoint_Start) { Start = value; if (Start > End) End = Start; } else { End = value; if (End < Start) Start = End; } }
        public IUIAutomationTextRange Clone() { return new Range(Page, Start, End, OwnerStart, OwnerEnd); }
        public int Compare(IUIAutomationTextRange range) { var other = (Range)range; return Start == other.Start && End == other.End ? 1 : 0; }
        public int CompareEndpoints(TextPatternRangeEndpoint endpoint, IUIAutomationTextRange other, TextPatternRangeEndpoint target) { return At(endpoint).CompareTo(((Range)other).At(target)); }
        public void MoveEndpointByRange(TextPatternRangeEndpoint endpoint, IUIAutomationTextRange other, TextPatternRangeEndpoint target) { Set(endpoint, ((Range)other).At(target)); }
        public int MoveEndpointByUnit(TextPatternRangeEndpoint endpoint, TextUnit unit, int count) { CharacterWalks++; int before = At(endpoint); Set(endpoint, Math.Max(0, Math.Min(Page.Length, before + count))); return At(endpoint) - before; }
        public string GetText(int maximum) { if (Start < OwnerStart || End > OwnerEnd || maximum > 512 || maximum < 0) throw new Exception("读取越过当前输入框或512字上限"); return Page.Substring(Start, Math.Min(maximum, End - Start)); }
        public void ExpandToEnclosingUnit(TextUnit unit) { throw new NotSupportedException(); }
        public IUIAutomationTextRange FindAttribute(int attribute, object value, int backward) { throw new NotSupportedException(); }
        public IUIAutomationTextRange FindText(string text, int backward, int ignoreCase) { throw new NotSupportedException(); }
        public object GetAttributeValue(int attribute) { throw new NotSupportedException(); }
        public double[] GetBoundingRectangles() { throw new NotSupportedException(); }
        public IUIAutomationElement GetEnclosingElement() { throw new NotSupportedException(); }
        public int Move(TextUnit unit, int count) { throw new NotSupportedException(); }
        public void Select() { throw new NotSupportedException(); }
        public void AddToSelection() { throw new NotSupportedException(); }
        public void RemoveFromSelection() { throw new NotSupportedException(); }
        public void ScrollIntoView(int align) { throw new NotSupportedException(); }
        public IUIAutomationElementArray GetChildren() { throw new NotSupportedException(); }
    }
    private sealed class Pattern : IUIAutomationTextPattern {
        public IUIAutomationTextRange DocumentRange { get; set; }
        public SupportedTextSelection SupportedTextSelection { get { return SupportedTextSelection.SupportedTextSelection_Single; } }
        public IUIAutomationTextRange RangeFromPoint(tagPOINT point) { throw new NotSupportedException(); }
        public IUIAutomationTextRange RangeFromChild(IUIAutomationElement child) { throw new NotSupportedException(); }
        public IUIAutomationTextRangeArray GetSelection() { throw new NotSupportedException(); }
        public IUIAutomationTextRangeArray GetVisibleRanges() { throw new NotSupportedException(); }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string root) {
        var checks = new List<string>(); string error = null;
        try {
            string page = new string('旧', 1000) + "你好nihao" + "其他页面内容";
            var pattern = new Pattern { DocumentRange = new Range(page, 1000, 1007, 1000, 1007) };
            var composition = new Range(page, 1002, 1007, 1000, 1007);
            Range.CharacterWalks = 0;
            Require(BoundedTextRange.Tail(pattern, composition, false) == "你好", "组词前已提交中文"); checks.Add("组词尾部限制在编辑框内");
            Require(BoundedTextRange.Tail(pattern, composition, true) == "你好nihao", "选择末尾读取"); checks.Add("读取选择末端不丢输入");
            Require(Range.CharacterWalks == 0, "短编辑框不应向聊天页面倒走512个字符"); checks.Add("短编辑框不做向框外的512字遍历");
            pattern.DocumentRange = new Range(page, 1000, 1000, 1000, 1000);
            Require(BoundedTextRange.Tail(pattern, new Range(page, 1000, 1000, 1000, 1000), true) == "", "空输入框不能读取页面"); checks.Add("空输入框不读取周围页面");
            pattern.DocumentRange = new Range(page, 1000, 1007, 1000, 1007);
            Require(BoundedTextRange.Tail(pattern, new Range(page, 2, 2, 1000, 1007), true) == "", "过期选择在框前");
            Require(BoundedTextRange.Tail(pattern, new Range(page, page.Length, page.Length, 1000, 1007), true) == "你好nihao", "过期选择在框后"); checks.Add("过期范围两端都钳制到编辑框");
            string old = new string('旧', 3000); page = old + "新输入。";
            pattern.DocumentRange = new Range(page, 0, page.Length, 0, page.Length);
            var input = new InputSession();
            input.Baseline(BoundedTextRange.Tail(pattern, new Range(page, 3000, 3000, 0, page.Length), true)); input.Arm();
            string tail = BoundedTextRange.Tail(pattern, new Range(page, page.Length, page.Length, 0, page.Length), true);
            input.Update(tail); Require(tail.Length == 512 && input.Text == "新输入。", "长Word正文增量"); checks.Add("长Word正文仅读512字并排除旧正文");
            pattern.DocumentRange = null; Require(BoundedTextRange.Tail(pattern, composition, false) == "", "无边界不读取"); checks.Add("缺少输入框边界时不读取");
        } catch (Exception failure) { error = failure.Message; }
        File.WriteAllText(Path.Combine(root, "bounded-range-test-results.json"), new JavaScriptSerializer().Serialize(new { passed = error == null, error, checks }));
    }
}

namespace LearningIme {
    internal static class RuntimeRegression {
        [STAThread] private static void Main() {
            var settings = Settings.Load();
            BoundedTextRangeTests.Run(settings.Root);
            InputSessionTests.Run(settings.Root);
            SubmissionTests.Run(settings.Root);
            System.Windows.Forms.Application.EnableVisualStyles();
            ControllerTests.Run(settings);
            SettingsTests.Run(settings.Root);
        }
    }
    internal static class SubmissionTests {
        public static void Run(string root) {
            var checks = new List<string>(); string error = null;
            try {
                var retention = new SubmissionRetention(); var now = DateTime.UtcNow; string typed;
                retention.Enter("我们种了两棵小树。", false, false, now);
                if (!retention.TakeIfCleared("", now.AddMilliseconds(200), out typed) || typed != "我们种了两棵小树。") throw new Exception("发送清空时丢句子"); checks.Add("立即发送后保留本次学习句子");
                if (retention.TakeIfCleared("", now, out typed)) throw new Exception("重复提交"); checks.Add("同次提交仅保留一次");
                retention.Enter("我们种了两棵小树。", true, false, now);
                if (retention.TakeIfCleared("", now, out typed)) throw new Exception("组词中的Enter被当作发送");
                retention.Enter("我们种了两棵小树。", false, true, now);
                if (retention.TakeIfCleared("", now, out typed)) throw new Exception("ShiftEnter被当作发送"); checks.Add("候选词确认和换行不算发送");
                retention.Enter("我们种了两棵小树。", false, false, now);
                if (retention.TakeIfCleared("我们种了两棵小树。\n", now, out typed)) throw new Exception("Word换行被当作清空"); checks.Add("Word正文换行继续原会话");
                if (retention.TakeIfCleared("", now.AddSeconds(2), out typed)) throw new Exception("无关清空用了旧句子"); checks.Add("过期提交不关联后续清空");
                retention.Enter("我们种了两棵小树。", false, false, now); retention.Clear();
                if (retention.TakeIfCleared("", now, out typed)) throw new Exception("移动光标后仍保留旧提交"); checks.Add("导航切窗停止保留");
            } catch (Exception failure) { error = failure.Message; }
            File.WriteAllText(Path.Combine(root, "submission-test-results.json"), new JavaScriptSerializer().Serialize(new { passed = error == null, error, checks }));
        }
    }
}
