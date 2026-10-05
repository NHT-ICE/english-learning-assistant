using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LearningIme {
    internal static class ControllerTests {
        private sealed class SlowTranslator : ITranslator {
            public int Calls;
            public async Task<string> Translate(string source, Action<string> progress, CancellationToken token) {
                Calls++;
                // 故意忽略取消，模拟取消前已经在途的旧响应。
                await Task.Delay(320).ConfigureAwait(false);
                progress("English: " + source);
                return "English: " + source;
            }
        }
        public static void Run(Settings settings) {
            using (var host = new Form { Text = "学习请求测试", ShowInTaskbar = false }) {
                host.Shown += async (_, __) => {
                    var translator = new SlowTranslator();
                    var displayed = new List<string>();
                    var recorded = new List<string>();
                    int hidden = 0;
                    string error = null;
                    try {
                        using (var controller = new StudyController(settings, translator, (s, cn, en) => { if (en.Length > 0) displayed.Add(cn); else hidden++; }, (cn, en) => recorded.Add(cn))) {
                            controller.Accept(new Snapshot { scope = "a", text = "我们准备苹果。" });
                            await Task.Delay(230); // 第一条请求已经发出。
                            controller.Accept(new Snapshot { scope = "a", text = "我们准备香蕉。", typed_only = true });
                            await Task.Delay(750);
                            if (displayed.Count == 0 || displayed.Contains("我们准备苹果。")) throw new Exception("旧响应覆盖新输入");
                            if (recorded.Count != 1 || recorded[0] != "我们准备香蕉。") throw new Exception("历史记录包含取消请求或流式片段");
                            int calls = translator.Calls;
                            controller.Accept(new Snapshot { scope = "a", text = "我们准备香蕉。" });
                            await Task.Delay(30);
                            if (translator.Calls != calls) throw new Exception("重复快照发出重复请求");
                            int beforeComposition = hidden;
                            controller.Accept(new Snapshot { scope = "a", text = "我们准备香蕉。", composing = true });
                            if (hidden != beforeComposition) throw new Exception("继续组词立即隐藏已完成译文");
                            controller.Accept(new Snapshot { scope = "a", text = "我们准备香蕉。然后去公园野餐。" });
                            await Task.Delay(30);
                            if (hidden != beforeComposition) throw new Exception("同框等待新译文时浮窗闪烁");
                            controller.Accept(new Snapshot { scope = "b", text = "我们准备香蕉。" });
                            await Task.Delay(30);
                            if (translator.Calls != calls) throw new Exception("缓存未命中");
                            if (recorded.Count != 1) throw new Exception("缓存命中被重复记录");
                            controller.Accept(new Snapshot { scope = "c", text = "", composing = true });
                            if (hidden != beforeComposition + 1) throw new Exception("切换输入框未隐藏旧浮窗");
                            controller.ReloadConfiguration();
                            controller.Accept(new Snapshot { scope = "b", text = "我们准备香蕉。" });
                            await Task.Delay(750);
                            if (translator.Calls != calls + 1) throw new Exception("修改接口后仍使用旧译文缓存");
                            calls = translator.Calls;
                            controller.Pause(true);
                            controller.Accept(new Snapshot { scope = "b", text = "暂停以后不要翻译。" });
                            await Task.Delay(500);
                            if (translator.Calls != calls) throw new Exception("暂停后仍有请求");
                        }
                    } catch (Exception failure) { error = failure.Message; }
                    File.WriteAllText(Path.Combine(settings.Root, "controller-test-results.json"), new JavaScriptSerializer().Serialize(new {
                        passed = error == null, error, api_calls = translator.Calls,
                        checks = new[] { "旧响应丢弃", "重复快照去重", "跨输入框缓存", "暂停请求", "只记录成功结果", "缓存不重复记录", "接口修改后清空缓存", "继续组词保留已完成译文", "同框等待新结果不闪烁", "切换输入框隐藏旧浮窗" }
                    }), System.Text.Encoding.UTF8);
                    host.Close();
                };
                Application.Run(host);
            }
        }
    }
}
