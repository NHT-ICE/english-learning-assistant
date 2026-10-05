using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace LearningIme {
    internal static class InputSessionTests {
        public static void Run(string root) {
            var checks = new List<string>();
            string error = null;
            try {
                var input = new InputSession();
                string old = "这是一段已有笔记内容。";
                input.Baseline(old); input.Update(old + "光标移动");
                Require(input.Text == "", "未开始输入时没有学习内容"); checks.Add("开始输入前不记录正文");
                input.Baseline(old); input.Arm(); input.Update(old + "nihao");
                input.Update(old + "你好");
                Require(input.Text == "你好", "临时拼音替换中文"); checks.Add("拼音替换中文并排除旧正文");
                input.Update(old + "你好，今天学习阅读。\n明天一起散步。");
                Require(input.Text == "你好，今天学习阅读。\n明天一起散步。", "换行衔接");
                var snapshot = new Snapshot { scope = "test", text = input.Text, typed_only = true };
                Require(snapshot.Sentence() == "你好，今天学习阅读。 明天一起散步。", "翻译保留上一句"); checks.Add("换行保留本次输入的前一句");
                input.Update(old + "你好，今天学习阅读。\n明天一起散步");
                Require(input.Text.EndsWith("散步"), "退格删掉句号"); checks.Add("退格同步");
                input.Update("另一段已有正文。"); Require(input.Text == "" && !input.Armed, "移动到旧段落重置"); checks.Add("跳到旧段落不读取旧上下文");
                old = new string('旧', 480); input.Baseline(old); input.Arm();
                string added = "我们种了两棵小树。";
                for (int i = 1; i <= 30; i++) {
                    string typed = Repeat(added, i);
                    string full = old + typed;
                    input.Update(full.Substring(Math.Max(0, full.Length - 512)));
                    Require(input.Text == typed, "512 字读取窗口滚动");
                }
                checks.Add("读取窗口滚动不混入旧正文");
                input.Baseline(""); input.Arm(); input.Update(new string('中', 520)); Require(input.Text.Length == 512, "缓存上限"); checks.Add("本次输入缓存有界");
                input.Baseline("正文"); input.Arm(); input.Update("正文新输入"); input.Update("正文"); Require(input.Text == "", "删除当前输入"); input.Update(""); Require(!input.Armed, "删到旧正文重置"); checks.Add("删到旧正文停止本次记录");
                input.Baseline(""); input.Arm(); input.Update("新打的中文"); input.Update("跳到已经存在的另一段内容"); Require(input.Text == "" && !input.Armed, "整段不相关文本不能成为增量"); checks.Add("空白起点后跳到旧内容也要重置");
                input.Baseline(old); input.Arm(); input.Update(old + "今天去散步吗？"); var retained = input.Capture();
                input.Baseline(""); Require(input.Restore(retained, old + "今天去散步吗？") && input.Text == "今天去散步吗？" && !input.Armed, "返回相同未变输入框恢复已知新输入");
                input.Arm(); input.Update(old + "今天去散步吗？天气很晴朗。我们去公园吧。你想带些水果吗？");
                Require(new Snapshot { scope = "test", text = input.Text }.Sentence() == "今天去散步吗？天气很晴朗。我们去公园吧。你想带些水果吗？", "四句同次输入不可截成最后一句"); checks.Add("返回未变输入框保留多句前文并继续完整组句");
                input.Baseline(""); Require(!input.Restore(retained, "别处的旧笔记正文。"), "不匹配的旧正文禁止恢复"); checks.Add("返回不同内容不恢复旧缓存");
                retained.SavedAt = DateTime.UtcNow.AddMinutes(-6); Require(!input.Restore(retained, old + "今天去散步吗？"), "过期缓存禁止恢复"); checks.Add("前文缓存五分钟过期");
            } catch (Exception failure) { error = failure.Message; }
            File.WriteAllText(Path.Combine(root, "input-session-test-results.json"), new JavaScriptSerializer().Serialize(new { passed = error == null, error, checks }));
        }
        private static void Require(bool passed, string label) { if (!passed) throw new Exception(label); }
        private static string Repeat(string text, int count) { return string.Concat(System.Linq.Enumerable.Repeat(text, count)); }
    }
}
