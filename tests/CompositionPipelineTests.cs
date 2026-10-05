using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Runtime.Serialization;
using System.Web.Script.Serialization;
using LearningUia;

namespace LearningIme {
    internal static class CompositionPipelineTests {
        private sealed class Stub : RealProxy {
            private readonly Func<IMethodCallMessage, object> invoke;
            public Stub(Type type, Func<IMethodCallMessage, object> invoke) : base(type) { this.invoke = invoke; }
            public override IMessage Invoke(IMessage message) {
                var call = (IMethodCallMessage)message;
                try { return new ReturnMessage(invoke(call), null, 0, call.LogicalCallContext, call); }
                catch (Exception error) { return new ReturnMessage(error, call); }
            }
        }
        private static object Proxy(Type type, Func<IMethodCallMessage, object> call) { return new Stub(type, call).GetTransparentProxy(); }
        private static IUIAutomationTextRange Range(string page, int start, int end) {
            var type = typeof(BoundedTextRangeTests).GetNestedType("Range", BindingFlags.NonPublic);
            return (IUIAutomationTextRange)Activator.CreateInstance(type, page, start, end, 0, page.Length);
        }
        private static void Field(WindowsInputSource source, string name, object value) { typeof(WindowsInputSource).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(source, value); }
        private static void Call(WindowsInputSource source, string name, params object[] args) {
            try { typeof(WindowsInputSource).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source, args); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
        [STAThread] private static void Main() {
            string root = Settings.Load().Root; string testRoot = Path.Combine(root, "tests", "composition-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testRoot); var checks = new List<string>(); string error = null;
            try {
                string old = "已有正文。", page = old + "jintian"; bool composing = true, compositionProbeFails = false;
                bool focusLookupFails = false, hasKeyboardFocus = true; int selections = 0;
                var pattern = Proxy(typeof(IUIAutomationTextPattern), call => {
                    if (call.MethodName == "get_DocumentRange") return Range(page, 0, page.Length);
                    if (call.MethodName == "GetSelection") {
                        selections++;
                        return Proxy(typeof(IUIAutomationTextRangeArray), array => array.MethodName == "get_Length" ? (object)1 : Range(page, page.Length, page.Length));
                    }
                    throw new COMException("unsupported");
                });
                var editing = Proxy(typeof(IUIAutomationTextEditPattern), call => {
                    if (compositionProbeFails) throw new COMException("composition unavailable", unchecked((int)0x80040201));
                    if (call.MethodName == "GetActiveComposition") return composing ? Range(page, old.Length, page.Length) : null;
                    throw new COMException("unsupported");
                });
                var focused = (IUIAutomationElement)Proxy(typeof(IUIAutomationElement), call => {
                    switch (call.MethodName) {
                        case "get_CurrentProcessId": return 2;
                        case "get_CurrentIsPassword": return 0;
                        case "get_CurrentIsEnabled": return 1;
                        case "get_CurrentControlType": return 50004;
                        case "get_CurrentHasKeyboardFocus": return hasKeyboardFocus ? 1 : 0;
                        case "GetRuntimeId": return new int[] {42};
                        case "GetCurrentPattern":
                            if ((int)call.Args[0] == 10014) return pattern;
                            if ((int)call.Args[0] == 10032) return editing;
                            break;
                    }
                    throw new COMException("unsupported");
                });
                var automation = Proxy(typeof(IUIAutomation3), call => {
                    if (call.MethodName == "GetFocusedElement" && focusLookupFails)
                        throw new COMException("global focus unavailable", unchecked((int)0x80040201));
                    return call.MethodName == "GetFocusedElement" ? (object)focused : null;
                });
                var input = new InputSession(); var output = new List<Snapshot>();
                var keys = new BlockingCollection<Tuple<int,int>>(64);
                var source = (WindowsInputSource)FormatterServices.GetUninitializedObject(typeof(WindowsInputSource));
                Field(source, "root", testRoot); Field(source, "session", input); Field(source, "automation", automation);
                Field(source, "deliver", (Action<Snapshot>)output.Add); Field(source, "scope", ""); Field(source, "signature", ""); Field(source, "committed", "");
                Field(source, "ownProcess", 1); Field(source, "keys", keys); Field(source, "submission", new SubmissionRetention());
                Field(source, "changes", new BlockingCollection<Tuple<string,int,string[]>>(64));
                Field(source, "retainedInputs", new Dictionary<string,InputSessionState>()); Field(source, "capabilities", new Dictionary<string,object>());
                Field(source, "activeDrafts", new Dictionary<string,DateTime>());
                Call(source, "Bind", focused, "2:42", false);
                if (input.Text != "" || selections != 0) throw new Exception("首次组词基线包含拼音或使用了错误尾部");
                checks.Add("首次绑定从组词起点建立基线排除旧正文与临时拼音");
                keys.Add(Tuple.Create(2, 0x4A)); Call(source, "Tick");
                if (selections != 0 || output.Count == 0 || !output[output.Count-1].composing) throw new Exception("组词过程中反复读取文本尾部");
                checks.Add("真实观察器组词路径不读取文本尾部");
                composing = false; page = old + "今天";
                keys.Add(Tuple.Create(2, 0x20)); Call(source, "Tick");
                if (input.Text != "今天" || output[output.Count-1].Sentence() != "今天") throw new Exception("第一词今天丢失或混入旧正文");
                checks.Add("第一次上屏完整捕获今天且排除旧正文");
                composing = true; page = old + "今天qusanbuma";
                keys.Add(Tuple.Create(2, 0x51)); Call(source, "Tick");
                composing = false; page = old + "今天去散步吗？";
                keys.Add(Tuple.Create(2, 0x20)); Call(source, "Tick");
                if (output[output.Count-1].Sentence() != "今天去散步吗？") throw new Exception("连续组词丢掉前文");
                checks.Add("连续组词完整保留前一轮完整句子");
                page += "你想带些水果吗？";
                keys.Add(Tuple.Create(2, 0x10000 | 0x56)); Call(source, "Tick");
                if (output[output.Count-1].Sentence() != "今天去散步吗？你想带些水果吗？") throw new Exception("粘贴快捷键清空前文");
                checks.Add("CtrlV粘贴作为本次新增输入保留前文而不读取剪贴板");
                if (!InputCapturePolicy.CurrentDraft("ChatGPT",50004) || !InputCapturePolicy.CurrentDraft("msedge",50004)
                    || InputCapturePolicy.CurrentDraft("ChatGPT",50030) || InputCapturePolicy.CurrentDraft("WINWORD",50004)
                    || InputCapturePolicy.CurrentDraft("WINWORD",50030)) throw new Exception("草稿策略误用于聊天页面或Word正文");
                checks.Add("Codex浏览器编辑框用草稿，聊天页面和Word仍只新增");
                old = ""; composing = false; page = "星期六天气晴朗，大家一起去公园。";
                input.Baseline("星期六"); Field(source, "lastTail", "星期六");
                Call(source, "ConfigureCapture", true, false, 2, "2:42");
                keys.Add(Tuple.Create(2, 0x20)); Call(source, "Tick");
                if (output[output.Count-1].Sentence() != page) throw new Exception("草稿基线误吞星期六开头");
                checks.Add("首词被基线吞掉时当前草稿仍完整包含星期六");
                // Simulate switching away while a Chinese prefix was committed
                // during a provider focus gap, before any incremental snapshot.
                page = "今天去散步吗？"; input.Baseline(""); Field(source, "lastTail", "");
                Field(source, "draftActive", true);
                Call(source, "Reset", true, "焦点暂时离开应用", false);
                Call(source, "Bind", focused, "2:42", false);
                Call(source, "ConfigureCapture", true, false, 2, "2:42");
                Field(source, "nextObservation", DateTime.MinValue);
                Field(source, "lastKey", DateTime.UtcNow.AddSeconds(-1)); // Focus gap after the last key.
                Call(source, "Tick");
                if (output[output.Count-1].Sentence() != page) throw new Exception("焦点恢复后漏掉已提交首词");
                checks.Add("前缀提交发生在焦点间隙时恢复当前草稿不截开头");
                Field(source, "draftActive", false); Field(source, "signature", "");
                Field(source, "nextObservation", DateTime.MinValue); input.Baseline(page);
                int before = output.Count; Call(source, "Tick");
                if (output.Count != before) throw new Exception("未激活草稿在没有输入时自动翻译");
                checks.Add("没有本次输入活动的草稿不自动翻译");
                composing = false; page = "星期六"; input.Baseline(page);
                Call(source, "ConfigureCapture", false, false, 2, "2:42");
                keys.Add(Tuple.Create(2,0x20)); page += "一起去公园？"; Call(source,"Tick");
                if (output[output.Count-1].Sentence() != "一起去公园？") throw new Exception("文档增量模式混入原有星期六");
                checks.Add("文档新增模式继续排除原有正文");
                page = "星期六天气晴朗，大家一起去公园。"; input.Baseline(page);
                Field(source,"lastKeyboardProcess",2); Field(source,"lastKeyboardTicks",DateTime.UtcNow.Ticks);
                Field(source,"lastKey",DateTime.UtcNow.AddSeconds(-1)); // No accepted key in the new binding yet.
                Field(source,"signature",""); Field(source,"nextObservation",DateTime.MinValue);
                Call(source,"ConfigureCapture",true,false,2,"2:cold"); Call(source,"Tick");
                if (output[output.Count-1].Sentence() != page) throw new Exception("首次焦点读取前发生的按键丢失草稿");
                checks.Add("焦点读取晚于首词时根据最近输入活动读取完整草稿");
                Field(source,"lastKeyboardTicks",DateTime.UtcNow.AddSeconds(-3).Ticks);
                Field(source,"signature",""); Field(source,"nextObservation",DateTime.MinValue); input.Baseline(page);
                Call(source,"ConfigureCapture",true,false,2,"2:inactive"); before=output.Count; Call(source,"Tick");
                if (output.Count!=before) throw new Exception("过期按键活动触发旧草稿");
                checks.Add("过期活动不激活另一个旧草稿");
                Field(source,"draftActive",false); Field(source,"lastKeyboardTicks",DateTime.UtcNow.Ticks);
                Field(source,"signature",""); Field(source,"nextObservation",DateTime.MinValue);
                Call(source,"Tick");
                if (output[output.Count-1].Sentence()!=page) throw new Exception("同一编辑框临时丢焦点后漏掉已发生输入");
                checks.Add("同一编辑框临时丢焦点后无需重新绑定也激活完整草稿");
                // After sending, Codex replaces its DOM edit. The old provider
                // can throw even for CurrentProcessId, while the new one is live.
                var retired = Proxy(typeof(IUIAutomationElement), call => {
                    throw new COMException("retired edit", unchecked((int)0x80040201));
                });
                Field(source, "element", retired); Field(source, "scope", "2:retired");
                Field(source, "boundProcess", 2); Field(source, "nextObservation", DateTime.MinValue);
                keys.Add(Tuple.Create(2,0x20)); Call(source,"Tick");
                if ((string)typeof(WindowsInputSource).GetField("scope", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(source) != "2:42")
                    throw new Exception("失效旧输入框阻止新输入框绑定");
                // Fake PID 2 has no host name. Restore the Codex policy after
                // exercising the real reset/bind transition with that fixture.
                Call(source, "ConfigureCapture", true, false, 2, "2:42");
                Field(source, "lastKey", DateTime.UtcNow.AddSeconds(-1)); Field(source, "nextObservation", DateTime.MinValue);
                Call(source, "Tick");
                if (output[output.Count-1].Sentence()!=page) throw new Exception("失效旧输入框阻止新输入框绑定与更新");
                checks.Add("Codex发送后旧控件全部接口失效仍绑定新框并更新");
                focusLookupFails = true;
                Call(source, "GetObservedFocus", 2);
                checks.Add("全局焦点失败时用已绑定编辑框的实时键盘焦点恢复");
                hasKeyboardFocus = false;
                bool rejected = false;
                try { Call(source, "GetObservedFocus", 2); } catch (COMException) { rejected = true; }
                if (!rejected) throw new Exception("失焦旧框被误当作当前输入框");
                checks.Add("失焦旧编辑框不继续读取");
                hasKeyboardFocus = true; rejected = false;
                try { Call(source, "GetObservedFocus", 3); } catch (COMException) { rejected = true; }
                if (!rejected) throw new Exception("跨应用使用旧编辑框");
                checks.Add("切换应用时不使用原编辑框");
                Field(source, "clicked", true); rejected = false;
                try { Call(source, "GetObservedFocus", 2); } catch (COMException) { rejected = true; }
                if (!rejected) throw new Exception("鼠标更换焦点后沿用旧框");
                checks.Add("鼠标更换焦点后仍需重新定位");
                compositionProbeFails = true; rejected = false; Field(source, "composing", true);
                try { Call(source, "UpdateComposition"); } catch (COMException) { rejected = true; }
                if (!rejected) throw new Exception("组词接口异常被吞掉并永久保留组词标记");
                checks.Add("组词查询异常明确进入接口恢复而非永久等待");
            } catch (Exception failure) { error = failure.ToString(); }
            File.WriteAllText(Path.Combine(root, "composition-pipeline-test-results.json"), new JavaScriptSerializer().Serialize(new {passed=error==null,error,checks}));
        }
    }
}
