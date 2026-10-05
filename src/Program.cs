using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace LearningIme {
    internal static class Program {
        [STAThread]
        private static void Main(string[] args) {
#if !PORTABLE
            if (args.Length > 0 && args[0] == "--input-session-test") { InputSessionTests.Run(Settings.Load().Root); return; }
            if (args.Length > 0 && args[0] == "--settings-test") { SettingsTests.Run(Settings.Load().Root); return; }
#endif
            if (args.Length > 0 && (args[0] == "--settings" || args[0] == "--show-card-test")) {
                try {
                    using (var pipe = new System.IO.Pipes.NamedPipeClientStream(".", "chinese-english-learning-cards", System.IO.Pipes.PipeDirection.Out)) {
                        pipe.Connect(1000);
                        using (var writer = new StreamWriter(pipe)) writer.WriteLine(args[0] == "--settings" ? "{\"command\":\"settings\"}" : "{\"command\":\"card-test\"}");
                    }
                } catch (Exception) {
                    if (args[0] == "--show-card-test") return;
                    Application.EnableVisualStyles();
                    using (var form = new StudySettingsForm(LoadSettings())) Application.Run(form);
                }
                return;
            }
            bool first;
            using (var singleton = new Mutex(true, "Local\\ChineseEnglishLearningPopup", out first)) {
                if (!first) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try {
                    var settings = LoadSettings();
                    DiagnosticsPolicy.Enabled = settings.diagnostics_enabled;
#if !PORTABLE
                    if (args.Length > 0 && args[0] == "--render-settings-preview") {
                        using (var preview = new StudySettingsForm(settings)) preview.ExportPreview(Path.Combine(settings.Root, "settings-preview.png"));
                        return;
                    }
                    bool windowsInput = Path.GetFileNameWithoutExtension(Application.ExecutablePath) == "EnglishLearningAssistant"
                        || Array.IndexOf(args, "--windows-input") >= 0;
                    if (args.Length > 0 && args[0] == "--self-test") { SelfTests.Run(settings); return; }
                    if (args.Length > 0 && args[0] == "--controller-test") { ControllerTests.Run(settings); return; }
                    if (args.Length > 0 && args[0] == "--pipe-test") { PipeTests.Run(settings); return; }
                    if (args.Length > 0 && args[0] == "--server-test") { PipeTests.Run(settings, true); return; }
                    if (args.Length > 0 && args[0] == "--render-preview") {
                        using (var preview = new CardForm(20000)) {
                            preview.Present(new Snapshot(), "今天的天气很好。",
                                "The weather is nice today.");
                            preview.ExportPreview(Path.Combine(settings.Root, "popup-preview.png"));
                        }
                        return;
                    }
                    if (args.Length > 0 && args[0] == "--preview") {
                        using (var preview = new PreviewHost()) Application.Run(preview);
                        return;
                    }
#else
                    bool windowsInput = true;
#endif
                    if (!StartupConfiguration.Ensure(settings, () => {
                        using (var firstRun = new StudySettingsForm(settings, true))
                            return firstRun.ShowDialog() == DialogResult.OK;
                    })) return;
                    using (var translator = new Translator(settings))
                    using (var form = new CardForm(settings)) {
                        Action reloadConfiguration = null;
                        Action showSettings = () => {
                            using (var dialog = new StudySettingsForm(settings)) {
                                if (dialog.ShowDialog() == DialogResult.OK) {
                                    form.SetDismiss(settings.dismiss_ms);
                                    if (reloadConfiguration != null) reloadConfiguration();
                                }
                            }
                        };
                        // 在任何管道消息之前创建句柄，保证 BeginInvoke 落在这个 UI 线程。
                        var handle = form.Handle;
                        var diagnostics = new StudyDiagnostics(settings.Root);
                        var inputRoutes = new InputRouteDiagnostics(settings.Root);
                        form.StateChanged += reason => diagnostics.Window(reason, form.DiagnosticState);
                        Action showCardTest = () => {
                            var work = Screen.FromPoint(Cursor.Position).WorkingArea;
                            var example = new Snapshot { scope = "display-test", rect = new Anchor { left = work.Left + 40, top = work.Top + 80, bottom = work.Top + 100 } };
                            form.Present(example, "浮窗显示测试：今天的天气很好。", "The weather is nice today.");
                            diagnostics.Display(example, "The weather is nice today.".Length);
                        };
                        NotifyIcon historyTray = null;
                        using (var history = new LearningHistory(settings.Root, error => {
                            try { form.BeginInvoke((Action)(() => {
                                if (historyTray != null) historyTray.ShowBalloonTip(5000, "英语学习记录", error, ToolTipIcon.Warning);
                            })); } catch (InvalidOperationException) { }
                        }))
                        using (var controller = new StudyController(settings, translator, (snapshot, chinese, english) => {
                            form.Present(snapshot, chinese, english);
                            diagnostics.Display(snapshot, english.Length, english.StartsWith("暂时无法翻译：", StringComparison.Ordinal));
                        }, (chinese, english) => { if (settings.record_history) history.Record(chinese, english); }))
                        {
                        reloadConfiguration = controller.ReloadConfiguration;
                        Action<Snapshot> accept = snapshot => {
                            try { form.BeginInvoke((Action)(() => {
                                if (snapshot.command == "settings") showSettings();
                                else if (snapshot.command == "card-test") showCardTest();
                                else if (snapshot.command == "windows-bridge-status") {
                                    File.WriteAllText(Path.Combine(settings.Root, "wechat-bridge-status.json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {
                                        utc = DateTime.UtcNow.ToString("o"), snapshot.scope, snapshot.bridge_messages, snapshot.bridge_commits
                                    }));
                                } else {
                                    diagnostics.Accept(snapshot);
                                    controller.Accept(snapshot);
                                }
                            })); } catch (InvalidOperationException) { }
                        };
                        using (var listener = new PipeListener(snapshot => {
                            bool accepted = !windowsInput || snapshot.command == "settings" || snapshot.command == "card-test";
                            inputRoutes.Native(snapshot, accepted);
                            if (accepted) accept(snapshot);
                        }))
                        using (var bridge = windowsInput ? new ImeCommitBridge(accept) { Enabled = Array.IndexOf(args, "--wechat-compat") >= 0 } : null)
                        using (var input = windowsInput ? new WindowsInputSource(settings.Root, snapshot => { inputRoutes.Windows(snapshot); accept(snapshot); }, bridge) : null)
                        using (var tray = new NotifyIcon()) {
                            var context = new ApplicationContext();
                            var menu = new ContextMenuStrip();
                            var pause = new ToolStripMenuItem("暂停英语学习") { CheckOnClick = true };
                            pause.CheckedChanged += (_, __) => { controller.Pause(pause.Checked); if (input != null) input.Pause(pause.Checked); };
                            menu.Items.Add(pause);
                            if (bridge != null) {
                                var compatibility = new ToolStripMenuItem("微信兼容（试验）") { CheckOnClick = true, Checked = bridge.Enabled };
                                compatibility.CheckedChanged += (_, __) => { bridge.Enabled = compatibility.Checked; if (!compatibility.Checked) bridge.Stop(); };
                                menu.Items.Add(compatibility);
                            }
                            menu.Items.Add("学习设置（API）", null, (_, __) => showSettings());
                            menu.Items.Add("显示测试浮窗", null, (_, __) => showCardTest());
                            menu.Items.Add("打开学习记录", null, (_, __) => {
                                string directory = Path.Combine(settings.Root, "history");
                                Directory.CreateDirectory(directory);
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
                            });
                            menu.Items.Add("退出悬浮窗", null, (_, __) => context.ExitThread());
                            tray.Icon = System.Drawing.SystemIcons.Information;
                            historyTray = tray;
                            tray.Text = windowsInput ? "英语学习助手：使用微软拼音正常输入" : "中文英语学习：中文输入，英文悬浮显示";
                            tray.ContextMenuStrip = menu; tray.Visible = true;
                            listener.Start();
                            Application.Run(context);
                            tray.Visible = false;
                            historyTray = null;
                        }
                        }
                    }
                } catch (Exception error) {
                    MessageBox.Show("学习悬浮窗未启动：" + error.Message, "中文英语学习", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
        private static Settings LoadSettings() {
#if PORTABLE
            return Settings.LoadPortable();
#else
            return Settings.Load();
#endif
        }
    }
}
