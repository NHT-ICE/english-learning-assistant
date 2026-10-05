using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LearningIme {
    internal sealed class PipeListener : IDisposable {
        private volatile bool stopped;
        private NamedPipeServerStream waiting;
        private readonly Action<Snapshot> deliver;
        public PipeListener(Action<Snapshot> deliver) { this.deliver = deliver; }
        public void Start() { Task.Run((Func<Task>)Listen); }
        private async Task Listen() {
            var acl = new PipeSecurity();
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            while (!stopped) {
                NamedPipeServerStream pipe = null;
                try {
                    pipe = new NamedPipeServerStream("chinese-english-learning-cards", PipeDirection.In, 16, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous, 4096, 4096, acl);
                    waiting = pipe;
                    await pipe.WaitForConnectionAsync().ConfigureAwait(false);
                    waiting = null;
                    var connected = pipe; pipe = null;
                    // 一个宿主最多 512 UTF-16 单元，限定消息长度，不在文件中保存输入内容。
                    _ = Task.Run(async () => {
                        using (connected)
                        using (var reader = new StreamReader(connected, Encoding.UTF8)) {
                            try {
                                string line = await reader.ReadLineAsync().ConfigureAwait(false);
                                if (line != null && line.Length <= 8192 && !stopped) deliver(new JavaScriptSerializer().Deserialize<Snapshot>(line));
                            } catch (Exception) { }
                        }
                    });
                } catch (Exception) {
                    if (!stopped) await Task.Delay(200).ConfigureAwait(false);
                } finally { if (pipe != null) pipe.Dispose(); }
            }
        }
        public void Dispose() { stopped = true; if (waiting != null) waiting.Dispose(); }
    }
}
