using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using LearningUia;

namespace LearningIme {
    // 独立后台观察器。键盘钩子只发活动通知，不截获、不改变输入；UIA 只读取当前编辑区有界尾部。
    internal sealed class WindowsInputSource : IDisposable {
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int hook, KeyboardCallback callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern int GetMessage(out NativeMessage message, IntPtr window, uint first, uint last);
        [DllImport("user32.dll")] static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint first, uint last, uint remove);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref NativeMessage message);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref NativeMessage message);
        [StructLayout(LayoutKind.Sequential)] private struct NativeMessage { public IntPtr window; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; public uint privateData; }
        delegate IntPtr KeyboardCallback(int code, IntPtr message, IntPtr data);
        private readonly KeyboardCallback keyboard;
        private readonly KeyboardCallback mouse;
        private IntPtr hook, mouseHook;
        private uint hookThreadId;
        private readonly Thread hookThread;
        private readonly ManualResetEventSlim hookReady = new ManualResetEventSlim(false);
        private int hookError, keyboardEvents, acceptedActivity, unmatchedActivity, textEvents, resets;
        private long lastKeyboardTicks;
        private int lastKeyboardProcess;
        private string lastReset = "启动";
        private bool eventSubscription;
        private bool refreshNeeded;
        private bool readerSuspended;
        private bool baselineReady;
        private bool sessionUsesTextPattern;
        private bool recoveringProvider;
        private bool currentDraft, draftActive;
        private int draftLength;
        private readonly Dictionary<string, DateTime> activeDrafts = new Dictionary<string, DateTime>();
        private string readStage = "启动";
        private DateTime nextObservation = DateTime.MinValue, lastComposition = DateTime.MinValue;
        private uint previousForeground;
        private int interfaceFailures;
        private readonly Queue<object> queryTimings = new Queue<object>();
        private sealed class QueryMetric { public int calls, failures; public long total_ms, max_ms; }
        private readonly Dictionary<string, QueryMetric> queryMetrics = new Dictionary<string, QueryMetric>();
        private int retainedFocusReads;
        private string unavailableStatus = "当前输入控件不提供可用文本接口";
        private readonly BlockingCollection<Tuple<int, int>> keys = new BlockingCollection<Tuple<int, int>>(64);
        private readonly Action<Snapshot> deliver;
        private readonly string root;
        private readonly ImeCommitBridge bridge;
        private readonly Thread worker;
        private volatile bool stopped;
        private volatile bool paused;
        private volatile bool clicked;
        private IUIAutomation3 automation;
        private IUIAutomationElement element;
        private int boundProcess;
        private IUIAutomationTextPattern pattern;
        private IUIAutomationTextPattern2 caretPattern;
        private IUIAutomationTextEditPattern editing;
        private IUIAutomationTextRange compositionRange;
        private LegacyInput legacy;
        private WindowsTextEvents events;
        private readonly InputSession session = new InputSession();
        private readonly Dictionary<string, InputSessionState> retainedInputs = new Dictionary<string, InputSessionState>();
        private readonly SubmissionRetention submission = new SubmissionRetention();
        private readonly BlockingCollection<Tuple<string, int, string[]>> changes = new BlockingCollection<Tuple<string, int, string[]>>(64);
        private readonly Dictionary<string, object> capabilities = new Dictionary<string, object>();
        private string scope = "", signature = "", committed = "";
        private bool composing, haveFinalized;
        private int compositionEvents, finalizedEvents, compositionLength, ordinaryTextEvents;
        private int compositionChineseLength, compositionLatinLength;
        private readonly int ownProcess = Process.GetCurrentProcess().Id;
        private DateTime lastKey = DateTime.MinValue, nextReport = DateTime.MinValue;
        public WindowsInputSource(string root, Action<Snapshot> deliver, ImeCommitBridge bridge = null) {
            this.root = root; this.deliver = deliver; this.bridge = bridge;
            keyboard = OnKey;
            mouse = OnMouse;
            hookThread = new Thread(RunHooks) { IsBackground = true, Name = "Input activity message pump" };
            hookThread.SetApartmentState(ApartmentState.STA); hookThread.Start();
            if (!hookReady.Wait(3000) || hook == IntPtr.Zero) {
                stopped = true; if (hookThreadId != 0) PostThreadMessage(hookThreadId, 0x12, IntPtr.Zero, IntPtr.Zero);
                throw new InvalidOperationException("无法监听输入活动，错误码 " + hookError);
            }
            worker = new Thread(Run) { IsBackground = true, Name = "Windows input observer" };
            worker.SetApartmentState(ApartmentState.MTA); worker.Start();
        }
        private IntPtr OnKey(int code, IntPtr message, IntPtr data) {
            if (code >= 0 && (message.ToInt64() == 0x100 || message.ToInt64() == 0x104)) {
                int key = Marshal.ReadInt32(data);
                int flags = Marshal.ReadInt32(data, 8);
                if ((flags & 0x10) == 0) {
                    Interlocked.Increment(ref keyboardEvents);
                    Interlocked.Exchange(ref lastKeyboardTicks, DateTime.UtcNow.Ticks);
                    if (GetAsyncKeyState(0x11) < 0) key |= 0x10000;
                    if (GetAsyncKeyState(0x12) < 0 || GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0) key |= 0x10000;
                    if (GetAsyncKeyState(0x10) < 0) key |= 0x20000;
                    uint process; GetWindowThreadProcessId(GetForegroundWindow(), out process);
                    Interlocked.Exchange(ref lastKeyboardProcess, (int)process);
                    keys.TryAdd(Tuple.Create((int)process, key));
                }
            }
            return CallNextHookEx(hook, code, message, data);
        }
        private void RunHooks() {
            try {
                hookThreadId = GetCurrentThreadId();
                NativeMessage message;
                PeekMessage(out message, IntPtr.Zero, 0, 0, 0); // 创建本线程消息队列。
                hook = SetWindowsHookEx(13, keyboard, GetModuleHandle(null), 0);
                if (hook == IntPtr.Zero) hookError = Marshal.GetLastWin32Error();
                mouseHook = SetWindowsHookEx(14, mouse, GetModuleHandle(null), 0);
                hookReady.Set();
                if (hook == IntPtr.Zero) return;
                while (!stopped && GetMessage(out message, IntPtr.Zero, 0, 0) > 0) {
                    TranslateMessage(ref message); DispatchMessage(ref message);
                }
            } finally {
                if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
                if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
                hookReady.Set();
            }
        }
        private IntPtr OnMouse(int code, IntPtr message, IntPtr data) {
            if (code >= 0 && (message.ToInt64() == 0x201 || message.ToInt64() == 0x204)) clicked = true;
            return CallNextHookEx(mouseHook, code, message, data);
        }
        private void Run() {
            try {
                automation = (IUIAutomation3)new CUIAutomation8();
                automation.ConnectionTimeout = 250; automation.TransactionTimeout = 250;
                events = new WindowsTextEvents((sender, type, strings) => {
                    Interlocked.Increment(ref textEvents);
                    try { changes.TryAdd(Tuple.Create(Id(sender), (int)type, strings)); } catch (Exception) { }
                });
                while (!stopped) {
                    try { Tick(); interfaceFailures = 0; }
                    catch (COMException error) {
                        uint foreground; GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
                        RecoverProvider(error, (int)foreground);
                    }
                    catch (Exception error) { lastReset = "接口异常：" + error.GetType().Name; SaveStatus(lastReset); Reset(); }
                    Thread.Sleep(40);
                }
            } catch (Exception error) { SaveStatus("监听启动失败：" + error.Message); }
            finally { if (automation != null) { try { automation.RemoveAllEventHandlers(); } catch (Exception) { } } }
        }
        internal void RecoverProvider(COMException error, int foreground) {
            lastReset = "接口暂时不可用：" + error.ErrorCode.ToString("X8") + " 阶段=" + readStage;
            interfaceFailures++;
            // Provider failures do not invalidate already observed own input.
            // Tick checks the focused control's identity before refreshing it.
            int bound; int.TryParse(scope.Split(':')[0], out bound);
            if (bound != 0 && foreground != bound && foreground != ownProcess) Reset(reason: lastReset);
            else if (element != null) { refreshNeeded = true; recoveringProvider = true; }
            SaveStatus(lastReset);
        }
        private static string Id(IUIAutomationElement item) { return item.CurrentProcessId + ":" + string.Join(".", item.GetRuntimeId()); }
        private void Tick() {
            if (paused) { if (element != null) Reset(); DrainKeys(); return; }
            uint frontProcess; uint frontThread = GetWindowThreadProcessId(GetForegroundWindow(), out frontProcess);
            var now = DateTime.UtcNow;
            if (frontProcess == previousForeground && !clicked && keys.Count == 0 && changes.Count == 0 && now < nextObservation) return;
            previousForeground = frontProcess;
            bool active = now.Ticks - Interlocked.Read(ref lastKeyboardTicks) < TimeSpan.FromSeconds(1).Ticks;
            nextObservation = now.AddMilliseconds(active || composing || refreshNeeded ? 80 : 450);
            readStage = "取得焦点";
            var focused = GetObservedFocus((int)frontProcess);
            bool chat = false;
            if (bridge != null && bridge.Enabled && focused != null && focused.CurrentIsPassword == 0 && frontProcess == focused.CurrentProcessId) {
                try { using (var host = Process.GetProcessById((int)frontProcess)) chat = host.ProcessName == "Weixin" || host.ProcessName == "WeChat"; } catch (Exception) { }
            }
            if (chat) {
                string bridgeScope = frontProcess + ":imm:" + frontThread;
                if (scope != bridgeScope) { Reset(); scope = bridgeScope; }
                bridge.Observe(frontProcess, frontThread);
                DrainKeys();
                if (DateTime.UtcNow > nextReport) {
                    SaveStatus(bridge.Active ? "微信输入提交兼容模块监听中" : "微信兼容模块未启用，错误码 " + bridge.Error);
                    nextReport = DateTime.UtcNow.AddSeconds(2);
                }
                return;
            }
            if (bridge != null && bridge.Active) bridge.Stop();
            bool valid = focused != null && focused.CurrentIsPassword == 0 && focused.CurrentIsEnabled != 0
                && focused.CurrentProcessId != ownProcess
                && (focused.CurrentControlType == 50004 || focused.CurrentControlType == 50030);
            LegacyInput compatible = null;
            if (!valid && focused != null && focused.CurrentIsPassword == 0 && focused.CurrentProcessId != ownProcess) {
                try { compatible = LegacyInput.Focus(focused.CurrentProcessId); } catch (Exception) { }
            }
            if (compatible != null && compatible.Editable) valid = true;
            if (InputFocusPolicy.Suspend(ownProcess, (int)frontProcess,
                focused == null ? 0 : focused.CurrentProcessId,
                element == null ? 0 : boundProcess, valid,
                focused != null && focused.CurrentIsPassword != 0)) {
                if (!readerSuspended) {
                    readerSuspended = true;
                    deliver(new Snapshot { scope = scope, text = "", preserve_display = true, reason = "临时焦点离开编辑区：停止读取，保留学习浮窗" });
                }
                clicked = false;
                DrainKeys();
                return; // No reading, no new request, no cancellation of the learning card.
            }
            readerSuspended = false;
            string id = valid ? (compatible != null ? compatible.Scope : Id(focused)) : "";
            if (id != scope) {
                // First binding has no previous input session to invalidate. It
                // must not cancel an independently displayed test/learning card.
                Reset(false, "焦点改变：原=" + scope + " 新=" + id + " 控件=" + (focused == null ? 0 : focused.CurrentControlType) + " 前台进程=" + frontProcess, scope.Length == 0);
                if (valid) { legacy = compatible; Bind(focused, id); }
            }
            else if (valid && refreshNeeded) {
                try { automation.RemoveAllEventHandlers(); } catch (Exception) { }
                Bind(focused, id, true);
                refreshNeeded = false;
            }
            if (element == null) {
                DrainKeys();
                if (DateTime.UtcNow > nextReport) {
                    if (focused != null && focused.CurrentProcessId != ownProcess) Describe(focused, false, false);
                    if (compatible != null) capabilities[Id(focused) + ":legacy"] = new { role = compatible.Role, state = compatible.State, editable = compatible.Editable };
                    SaveStatus(unavailableStatus); nextReport = DateTime.UtcNow.AddSeconds(2);
                }
                return;
            }
            if (currentDraft && !draftActive && Volatile.Read(ref lastKeyboardProcess) == element.CurrentProcessId
                && DateTime.UtcNow.Ticks - Interlocked.Read(ref lastKeyboardTicks) < TimeSpan.FromSeconds(2).Ticks) draftActive = true;
            UpdateComposition();
            if (clicked) {
                clicked = false;
                // 只在点击确实改变光标附近文字时清空，选择候选词和拖动浮窗不打断上下文。
                if (!composing && DateTime.UtcNow - lastComposition > TimeSpan.FromMilliseconds(300) && ReadTail() != lastTail) Rebase("鼠标移动光标", true);
            }
            Tuple<int, int> activity;
            while (keys.TryTake(out activity)) {
                if (activity.Item1 != element.CurrentProcessId) { unmatchedActivity++; continue; }
                acceptedActivity++;
                int key = activity.Item2;
                bool ctrl = (key & 0x10000) != 0, shifted = (key & 0x20000) != 0; key &= 0xFFFF;
                if (ctrl && key == 0x56) { // Paste is new input, not navigation. No clipboard read.
                    draftActive = currentDraft;
                    session.Arm(); lastKey = DateTime.UtcNow; continue;
                }
                if (ctrl && (key == 0x43 || key == 0x53 || key == 0x10 || key == 0x11 || key == 0x12)) continue;
                if (key != 0x0D || shifted) submission.Clear();
                if (key == 0x0D && !composing && !shifted) {
                    submission.Enter(haveFinalized ? committed : session.Text, composing, shifted, DateTime.UtcNow);
                    if (ctrl) { lastKey = DateTime.UtcNow; continue; }
                }
                if (!ctrl && composing && key >= 0x21 && key <= 0x28) {
                    session.Arm(); lastKey = DateTime.UtcNow; continue; // 输入法候选词导航仍属当前组句。
                }
                if (ctrl
                    || (key >= 0x21 && key <= 0x28) || key == 0x2E || key == 0x1B || key == 0x09) {
                    Rebase("导航或快捷键：VK=" + key.ToString("X2") + " 修饰=" + (ctrl ? "Ctrl/Alt/Win" : shifted ? "Shift" : "无")); continue;
                }
                if ((key >= 0x30 && key <= 0x5A) || key == 0x20 || key == 0x08 || key == 0x0D || (key >= 0xBA && key <= 0xE2)) {
                    draftActive = currentDraft;
                    session.Arm(); lastKey = DateTime.UtcNow;
                    if (key == 0x08 && haveFinalized && !composing && committed.Length > 0) {
                        int length = committed.Length - 1;
                        if (length > 0 && char.IsLowSurrogate(committed[length])) length--;
                        committed = committed.Substring(0, length);
                    }
                    if (key == 0x0D && haveFinalized) committed += "\n";
                }
            }
            Tuple<string, int, string[]> change;
            bool finalizedThisTick = false;
            while (changes.TryTake(out change)) {
                if (change.Item1 != scope) continue;
                if (change.Item2 == 0) { ordinaryTextEvents++; continue; }
                if (change.Item2 == (int)TextEditChangeType.TextEditChangeType_Composition) compositionEvents++;
                if (change.Item2 == (int)TextEditChangeType.TextEditChangeType_CompositionFinalized) finalizedEvents++;
                if (!session.Armed) {
                    // 已确认的上屏事件只含新提交文字；范围变动不应丢掉刚上屏的中文。
                    if (change.Item2 == (int)TextEditChangeType.TextEditChangeType_CompositionFinalized) { session.Arm(); lastKey = DateTime.UtcNow; }
                    else continue;
                }
                if (change.Item2 == (int)TextEditChangeType.TextEditChangeType_Composition && (editing == null || compositionRange != null)) composing = true;
                if (change.Item2 == (int)TextEditChangeType.TextEditChangeType_CompositionFinalized) {
                    draftActive = currentDraft;
                    finalizedThisTick = true;
                    composing = false; haveFinalized = true;
                    foreach (string text in change.Item3 ?? new string[0]) committed += text;
                    if (committed.Length > 512) {
                        int start = committed.Length - 512;
                        if (char.IsLowSurrogate(committed[start])) start++;
                        committed = committed.Substring(start);
                    }
                }
            }
            if (composing) {
                // Keep the last committed tail, instead of crossing the provider
                // on every pinyin keystroke. Chinese is read once it is committed.
                lastComposition = DateTime.UtcNow;
                Publish("", true);
                if (DateTime.UtcNow > nextReport) { SaveStatus("组词中：等待中文上屏"); nextReport = DateTime.UtcNow.AddSeconds(2); }
                return;
            }
            string tail = currentDraft ? ReadDraft() : ReadTail();
            string sent;
            if (submission.TakeIfCleared(tail, DateTime.UtcNow, out sent)) {
                lastTail = tail; session.Baseline(tail); committed = ""; haveFinalized = false; composing = false;
                lastReset = "发送后保留本次输入的学习句子";
                Publish(sent, false); SaveStatus("发送后等待译文"); return;
            }
            string previousTail = lastTail;
            if (!currentDraft && !recoveringProvider && session.Armed && DateTime.UtcNow - lastKey > TimeSpan.FromSeconds(1)
                && DateTime.UtcNow - lastComposition > TimeSpan.FromMilliseconds(300) && !composing && tail != lastTail) {
                Rebase("无键盘活动的范围变化", true); return; // 没有键盘活动的光标移动，不把旧正文当成新增文字。
            }
            lastTail = tail;
            bool changed = session.Update(tail);
            recoveringProvider = false;
            if (!session.Armed && changed) { lastReset = "新增范围不能对齐"; resets++; }
            if (pattern != null && session.Armed && tail.Length == 0 && previousTail.Length > 0 && haveFinalized
                && compositionRange == null && !finalizedThisTick) { Rebase("输入框清空"); return; }
            // 上屏事件是新中文的直接来源；组句范围暂时还未更新时也不会丢掉结果。
            string source = currentDraft && draftActive ? tail : haveFinalized ? committed : session.Text;
            if (!currentDraft && haveFinalized && committed.Length > 0 && session.Text.StartsWith(committed, StringComparison.Ordinal)) source = session.Text;
            // 未提供组句接口的输入框，末尾仍是拼音时继续等待。
            bool pendingPinyin = Regex.IsMatch(source, @"[a-z']+$")
                && (editing == null || DateTime.UtcNow - lastKey < TimeSpan.FromMilliseconds(450));
            if (composing || pendingPinyin) Publish("", true);
            else if ((session.Armed || haveFinalized || draftActive) && (changed || DateTime.UtcNow - lastKey > TimeSpan.FromMilliseconds(80))) Publish(source, false);
            if (DateTime.UtcNow > nextReport) { SaveStatus(pattern == null ? "仅上屏事件" : "监听中"); nextReport = DateTime.UtcNow.AddSeconds(2); }
        }
        private void Bind(IUIAutomationElement focused, string id, bool preserve = false) {
            readStage = "绑定文本接口";
            element = focused; scope = id; boundProcess = focused.CurrentProcessId;
            string host = "";
            try { using (var process = Process.GetProcessById(focused.CurrentProcessId)) host = process.ProcessName; } catch (Exception) { }
            ConfigureCapture(InputCapturePolicy.CurrentDraft(host, focused.CurrentControlType), preserve, focused.CurrentProcessId, id);
            pattern = null; caretPattern = null; editing = null; eventSubscription = false;
            try { pattern = (IUIAutomationTextPattern)element.GetCurrentPattern(10014); } catch (Exception) { }
            if (preserve && baselineReady && sessionUsesTextPattern && pattern == null)
                throw new COMException("当前文本接口尚未恢复", unchecked((int)0x80040201));
            if (BoundedTextRange.IsReadOnly(pattern)) {
                Describe(element, pattern != null, false);
                element = null; pattern = null;
                unavailableStatus = "只读正文不记录";
                SaveStatus(unavailableStatus); return;
            }
            try { editing = (IUIAutomationTextEditPattern)element.GetCurrentPattern(10032); } catch (Exception) { }
            UpdateComposition();
            if (!preserve || !baselineReady || (!sessionUsesTextPattern && pattern != null)) {
                lastTail = currentDraft ? ReadDraft() : compositionRange != null && pattern != null ? BoundedTextRange.Tail(pattern, compositionRange, false, true) : ReadTail();
                session.Baseline(lastTail); baselineReady = true;
                InputSessionState retained;
                if (retainedInputs.TryGetValue(id, out retained)) session.Restore(retained, lastTail);
                sessionUsesTextPattern = pattern != null;
            }
            try { caretPattern = (IUIAutomationTextPattern2)element.GetCurrentPattern(10024); } catch (Exception) { }
            try { automation.AddAutomationEventHandler(20015, element, TreeScope.TreeScope_Element, null, events); } catch (Exception) { }
            try {
                automation.AddTextEditTextChangedEventHandler(element, TreeScope.TreeScope_Element, TextEditChangeType.TextEditChangeType_Composition, null, events);
                automation.AddTextEditTextChangedEventHandler(element, TreeScope.TreeScope_Element, TextEditChangeType.TextEditChangeType_CompositionFinalized, null, events);
                eventSubscription = true;
            } catch (Exception) { }
            Describe(element, pattern != null, editing != null);
            SaveStatus("已准备输入起点");
        }
        private void ConfigureCapture(bool useDraft, bool preserve, int process, string id) {
            currentDraft = useDraft;
            DateTime activated;
            if (!preserve) draftActive = currentDraft && (
                (activeDrafts.TryGetValue(id, out activated) && DateTime.UtcNow - activated < TimeSpan.FromMinutes(5))
                || (Volatile.Read(ref lastKeyboardProcess) == process
                    && DateTime.UtcNow.Ticks - Interlocked.Read(ref lastKeyboardTicks) < TimeSpan.FromSeconds(2).Ticks));
        }
        private string lastTail = "";
        private void Describe(IUIAutomationElement item, bool textPattern, bool textEdit) {
            string name = "unknown";
            try { using (var process = Process.GetProcessById(item.CurrentProcessId)) name = process.ProcessName; } catch (Exception) { }
            if (capabilities.Count >= 32) capabilities.Clear();
            capabilities[Id(item)] = new { process = name, control_type = item.CurrentControlType, text_pattern = textPattern, text_edit_pattern = textEdit, password = item.CurrentIsPassword != 0 };
        }
        private string ReadTail() {
            readStage = "读取有界文本范围";
            if (legacy != null) return legacy.Tail();
            if (pattern == null) return "";
            // Track a stable caret tail, including temporary pinyin in memory. The
            // composition flag blocks API requests until conversion ends. Switching
            // between composition-start and caret-end made the baseline jump on each word.
            IUIAutomationTextRange range = null;
            if (caretPattern != null) {
                try { int active; var caret = caretPattern.GetCaretRange(out active); if (active != 0) range = caret; } catch (Exception) { }
            }
            if (range == null) {
                var selected = pattern.GetSelection();
                if (selected == null || selected.Length == 0)
                    throw new COMException("当前光标文本范围暂不可用", unchecked((int)0x80040201));
                range = selected.GetElement(0);
            }
            return BoundedTextRange.Tail(pattern, range, true, true);
        }
        private string ReadDraft() {
            readStage = "读取当前输入框草稿";
            if (pattern == null) throw new COMException("当前输入框暂不可用", unchecked((int)0x80040201));
            var range = Query("草稿边界", () => pattern.DocumentRange);
            if (range == null) throw new COMException("当前输入框边界暂不可用", unchecked((int)0x80040201));
            string text = Query("草稿文字", () => range.GetText(512));
            if (text == null) throw new COMException("当前草稿暂不可用", unchecked((int)0x80040201));
            draftLength = text.Length;
            // A short edit's DocumentRange includes its prefix and suffix without
            // relying on an IME's transient caret. Long edits retain bounded tail.
            return text.Length < 512 ? text : ReadTail();
        }
        private void UpdateComposition() {
            compositionRange = null; compositionLength = 0; compositionChineseLength = 0; compositionLatinLength = 0;
            if (editing == null) { composing = false; return; }
            readStage = "查询组词";
            try {
                var range = Query("组词范围", () => editing.GetActiveComposition());
                string text = range == null ? "" : Query("组词文字", () => range.GetText(512) ?? "");
                compositionLength = text.Length;
                compositionChineseLength = Regex.Matches(text, @"[\u3400-\u9fff]").Count;
                compositionLatinLength = Regex.Matches(text, @"[a-zA-Z]").Count;
                if (compositionLength > 0) compositionRange = range;
                composing = compositionRange != null;
            } catch (COMException) { throw; } // A stale flag must trigger provider recovery, not wait forever.
        }
        private IUIAutomationElement GetObservedFocus(int foreground) {
            try { return Query("全局焦点", () => automation.GetFocusedElement()); }
            catch (COMException) {
                // Codex sometimes fails the global focus lookup while its edit
                // remains live. Reuse it only with a fresh keyboard-focus proof.
                if (clicked || element == null) throw;
                var retained = Query("原编辑框焦点验证", () => element.CurrentProcessId == foreground
                    && element.CurrentHasKeyboardFocus != 0 ? element : null);
                if (retained == null) throw;
                retainedFocusReads++;
                return retained;
            }
        }
        private T Query<T>(string operation, Func<T> read) {
            if (!DiagnosticsPolicy.Enabled) return read();
            var timer = Stopwatch.StartNew(); string failure = null;
            try { return read(); }
            catch (COMException error) { failure = error.ErrorCode.ToString("X8"); throw; }
            finally {
                if (queryTimings != null) {
                    queryTimings.Enqueue(new { utc = DateTime.UtcNow.ToString("o"), process = previousForeground,
                        operation, elapsed_ms = timer.ElapsedMilliseconds, failure });
                    while (queryTimings.Count > 32) queryTimings.Dequeue();
                }
                if (queryMetrics != null) {
                    string id = previousForeground + ":" + operation; QueryMetric metric;
                    if (!queryMetrics.TryGetValue(id, out metric)) {
                        if (queryMetrics.Count >= 64) queryMetrics.Clear();
                        queryMetrics[id] = metric = new QueryMetric();
                    }
                    metric.calls++; if (failure != null) metric.failures++;
                    metric.total_ms += timer.ElapsedMilliseconds;
                    metric.max_ms = Math.Max(metric.max_ms, timer.ElapsedMilliseconds);
                }
            }
        }
        private void Publish(string text, bool preedit, string reason = null, bool preserveDisplay = false) {
            string next = scope + ":" + preedit + ":" + text;
            if (next == signature) return;
            readStage = "取得浮窗定位";
            var cursor = System.Windows.Forms.Cursor.Position;
            var anchor = new Anchor { left = cursor.X, top = cursor.Y, bottom = cursor.Y + 24 };
            if (!preedit && text.Length > 0) try {
                var box = element.CurrentBoundingRectangle;
                anchor = new Anchor { left = box.left, top = box.top, right = box.right, bottom = box.top + 24 };
            } catch (COMException) { } // Placement is optional; known text must still be delivered.
            if (!preedit && text.Length > 0 && legacy != null) { try { anchor = legacy.Anchor(); } catch (Exception) { } }
            if (!currentDraft && !preedit && text.Length > 0) try {
                var selection = pattern == null ? null : pattern.GetSelection();
                var bounds = selection == null || selection.Length == 0 ? null : selection.GetElement(0).GetBoundingRectangles();
                if (bounds != null && bounds.Length >= 4) anchor = new Anchor { left = (int)bounds[0], top = (int)bounds[1], right = (int)(bounds[0] + bounds[2]), bottom = (int)(bounds[1] + bounds[3]) };
            } catch (Exception) { }
            // Mark delivered only after all required provider calls succeed.
            // Otherwise the recovery tick must retry this same sentence.
            signature = next;
            deliver(new Snapshot { scope = scope, text = text, composing = preedit, typed_only = true, rect = anchor,
                reason = reason ?? (currentDraft ? "当前输入框草稿" : "文档新增文字"), preserve_display = preserveDisplay });
        }
        private void Rebase(string reason, bool preserveDisplay = false) {
            submission.Clear(); lastReset = reason; resets++;
            retainedInputs.Remove(scope);
            activeDrafts.Remove(scope); draftActive = false;
            // Clear navigation state before a provider call that may throw. A
            // subsequent recovery must baseline at the new caret, not reuse it.
            session.Baseline(""); baselineReady = false; recoveringProvider = false;
            committed = ""; haveFinalized = false; composing = false;
            Publish("", false, reason, preserveDisplay);
            lastTail = ReadTail(); session.Baseline(lastTail); baselineReady = true;
        }
        private void Reset(bool drain = true, string reason = "监听重置", bool preserveDisplay = false) {
            if (currentDraft && draftActive && scope.Length > 0) {
                if (activeDrafts.Count >= 8 && !activeDrafts.ContainsKey(scope)) activeDrafts.Clear();
                activeDrafts[scope] = DateTime.UtcNow;
            }
            if (baselineReady && scope.Length > 0) {
                var saved = session.Capture();
                if (saved != null) {
                    if (retainedInputs.Count >= 8 && !retainedInputs.ContainsKey(scope)) retainedInputs.Clear();
                    retainedInputs[scope] = saved;
                }
            }
            if (element != null && events != null) { try { automation.RemoveTextEditTextChangedEventHandler(element, events); } catch (Exception) { } }
            if (element != null && events != null) { try { automation.RemoveAutomationEventHandler(20015, element, events); } catch (Exception) { } }
            element = null; boundProcess = 0; pattern = null; caretPattern = null; editing = null; compositionRange = null; compositionLength = 0; legacy = null; scope = ""; signature = ""; committed = ""; composing = false; haveFinalized = false;
            session.Baseline(""); lastTail = ""; if (drain) DrainKeys();
            baselineReady = false;
            sessionUsesTextPattern = false;
            recoveringProvider = false;
            currentDraft = false; draftActive = false;
            draftLength = 0;
            submission.Clear();
            refreshNeeded = false; interfaceFailures = 0; unavailableStatus = "当前输入控件不提供可用文本接口";
            eventSubscription = false; resets++;
            deliver(new Snapshot { scope = "", text = "", reason = reason, preserve_display = preserveDisplay });
        }
        private void DrainKeys() { Tuple<int, int> key; while (keys.TryTake(out key)) { } }
        private void SaveStatus(string status) {
            if (!DiagnosticsPolicy.Enabled) return;
            try { File.WriteAllText(Path.Combine(root, "windows-input-status.json"), new JavaScriptSerializer().Serialize(new { utc = DateTime.UtcNow.ToString("o"), status, scope, paused, armed = session.Armed, composing, finalized_events = haveFinalized, new_text_length = session.Text.Length, committed_length = committed.Length,
                keyboard_events = Volatile.Read(ref keyboardEvents), accepted_activity = acceptedActivity, unmatched_activity = unmatchedActivity, text_events = Volatile.Read(ref textEvents), event_subscription = eventSubscription,
                composition_events = compositionEvents, finalized_event_count = finalizedEvents, ordinary_text_events = ordinaryTextEvents, composition_length = compositionLength,
                composition_chinese_length = compositionChineseLength, composition_latin_length = compositionLatinLength,
                last_keyboard_utc = new DateTime(Interlocked.Read(ref lastKeyboardTicks), DateTimeKind.Utc).ToString("o"), resets, last_reset = lastReset, read_stage = readStage, interface_failures = interfaceFailures,
                capture_mode = currentDraft ? "当前输入框草稿" : "文档新增文字", draft_active = draftActive, draft_length = draftLength,
                retained_focus_reads = retainedFocusReads, query_timings = queryTimings == null ? new object[0] : queryTimings.ToArray(), query_metrics = queryMetrics, capabilities })); } catch (IOException) { }
        }
        public void Dispose() {
            stopped = true; PostThreadMessage(hookThreadId, 0x12, IntPtr.Zero, IntPtr.Zero);
            hookThread.Join(1500); worker.Join(1500);
        }
        public void Pause(bool value) { paused = value; if (bridge != null) bridge.Pause(value); }
    }
}
