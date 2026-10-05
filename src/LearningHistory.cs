using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LearningIme {
    // 有界后台队列逐条追加成功译文；不读取历史文件，不记录流式片段或取消的请求。
    internal sealed class LearningHistory : IDisposable {
        private readonly string root;
        private readonly BlockingCollection<string> queue = new BlockingCollection<string>(64);
        private readonly Task worker;
        private readonly Action<string> reportError;
        public LearningHistory(string root, Action<string> reportError) {
            this.root = root; this.reportError = reportError;
            worker = Task.Run((Action)Write);
        }
        public void Record(string chinese, string english) {
            var now = DateTime.Now;
            var row = new JavaScriptSerializer().Serialize(new {
                local_time = now.ToString("o"), chinese, english, terms = Glossary.Matches(chinese)
            });
            if (!queue.TryAdd(now.ToString("yyyy-MM-dd") + "\n" + row)) reportError("记录队列已满，本次译文未保存。");
        }
        private void Write() {
            foreach (string item in queue.GetConsumingEnumerable()) {
                try {
                    int split = item.IndexOf('\n');
                    var directory = Path.Combine(root, "history");
                    Directory.CreateDirectory(directory);
                    using (var stream = new FileStream(Path.Combine(directory, item.Substring(0, split) + ".jsonl"), FileMode.Append, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.WriteLine(item.Substring(split + 1));
                } catch (Exception error) { reportError("学习记录未保存：" + error.Message); }
            }
        }
        public void Dispose() { queue.CompleteAdding(); worker.Wait(2000); }
    }
}
