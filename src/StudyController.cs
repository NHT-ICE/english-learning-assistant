using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LearningIme {
    internal sealed class StudyController : IDisposable {
        private readonly Settings settings;
        private readonly ITranslator translator;
        private readonly Action<Snapshot, string, string> display;
        private readonly Action<string, string> completed;
        private readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        private CancellationTokenSource pending;
        private long generation;
        private string lastIdentity = "";
        private string visibleScope = "";
        private bool paused;
        public StudyController(Settings settings, ITranslator translator, Action<Snapshot, string, string> display, Action<string, string> completed = null) {
            this.settings = settings; this.translator = translator; this.display = display;
            this.completed = completed;
        }
        // 此方法只在 UI 线程调用；异步网络结果回到同一同步上下文后核验代次。
        public void Accept(Snapshot snapshot) {
            // Reader baseline changes invalidate only its new-text buffer. The
            // already accepted sentence may finish translating and stay readable.
            if (!paused && snapshot.preserve_display) return;
            string source = snapshot.Sentence();
            string identity = (snapshot.scope ?? "") + ":" + source + ":" + snapshot.composing;
            if (identity == lastIdentity) return;
            lastIdentity = identity;
            generation++;
            if (pending != null) { pending.Cancel(); pending.Dispose(); pending = null; }
            if (paused || source.Length == 0) {
                // Keep a completed card readable while composing the next words in
                // the same edit. Its normal dismissal timer still applies.
                if (paused || !snapshot.composing || visibleScope != (snapshot.scope ?? "")) Show(snapshot, "", "");
                return;
            }
            pending = new CancellationTokenSource();
            Run(snapshot, source, generation, pending.Token);
        }
        private async void Run(Snapshot snapshot, string source, long version, CancellationToken token) {
            try {
                string cached;
                if (cache.TryGetValue(source, out cached)) { Show(snapshot, source, cached); return; }
                if (visibleScope != (snapshot.scope ?? "")) Show(snapshot, "", "");
                int delay = "。！？!?".IndexOf(source[source.Length - 1]) >= 0 ? Math.Min(60, settings.idle_ms) : settings.idle_ms;
                await Task.Delay(delay, token);
                if (version != generation) return;
                var context = SynchronizationContext.Current;
                string result = await translator.Translate(source, value => context.Post(_ => {
                    if (version == generation && !token.IsCancellationRequested) Show(snapshot, source, value);
                }, null), token);
                if (version != generation || token.IsCancellationRequested) return;
                if (cache.Count >= 128) cache.Clear();
                cache[source] = result;
                if (snapshot.typed_only && completed != null) completed(source, result);
                Show(snapshot, source, result);
            } catch (Exception) when (token.IsCancellationRequested) {
                // 编辑、切窗口后的旧结果不得重新显示。
            } catch (Exception error) {
                if (version == generation) Show(snapshot, source, "暂时无法翻译：" + error.Message);
            }
        }
        private void Show(Snapshot snapshot, string chinese, string english) {
            visibleScope = english.Length == 0 ? "" : (snapshot.scope ?? "");
            display(snapshot, chinese, english);
        }
        public void Pause(bool value) {
            paused = value; lastIdentity = ""; generation++;
            if (pending != null) { pending.Cancel(); pending.Dispose(); pending = null; }
            Show(new Snapshot(), "", "");
        }
        public void ReloadConfiguration() {
            cache.Clear(); lastIdentity = ""; generation++;
            if (pending != null) { pending.Cancel(); pending.Dispose(); pending = null; }
            Show(new Snapshot(), "", "");
        }
        public void Dispose() { Pause(true); }
    }
}
