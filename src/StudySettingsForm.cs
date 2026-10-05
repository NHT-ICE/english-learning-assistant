using System;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace LearningIme {
    internal sealed class StudySettingsForm : Form {
        private CancellationTokenSource testing;
        public StudySettingsForm(Settings settings, bool firstRun = false) {
            Text = firstRun ? "首次使用：配置自己的翻译 API" : "英语学习设置"; ClientSize = new Size(660, 520);
            Font = new Font("Microsoft YaHei UI", 9);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            LabelAt(firstRun ? "先申请阿里云百炼 API，再填写自己的地址、模型和密钥。" : "翻译接口", 24, 18);
            LabelAt("API 地址", 24, 60);
            var address = new TextBox { Text = settings.base_url, Location = new Point(155, 55), Width = 480 };
            LabelAt("模型名称", 24, 104);
            var model = new TextBox { Text = settings.model, Location = new Point(155, 99), Width = 480 };
            LabelAt("API 密钥", 24, 148);
            var key = new TextBox { UseSystemPasswordChar = true, Location = new Point(155, 143), Width = 397 };
            var reveal = new CheckBox { Text = "显示", Location = new Point(563, 144), AutoSize = true };
            reveal.CheckedChanged += (_, __) => key.UseSystemPasswordChar = !reveal.Checked;
            bool usableKey = settings.HasUsableKey;
            var keyHint = new Label { Text = usableKey ? "已保存密钥：留空沿用，填写新密钥则替换。" : "请填写自己申请的 API 密钥，密钥只在本机加密保存。", Location = new Point(155, 178), Width = 480, Height = 23, ForeColor = Color.DimGray };
            Controls.AddRange(new Control[] { address, model, key, reveal, keyHint });
            var test = new Button { Text = "测试连接", Location = new Point(24, 211), Width = 112, Height = 31 };
            var status = new Label { Text = "填写服务商的接口 Base URL，支持现有百炼兼容接口。", Location = new Point(155, 211), Width = 480, Height = 44, ForeColor = Color.DimGray };
            Controls.Add(test); Controls.Add(status);
            Controls.Add(new Label { BorderStyle = BorderStyle.Fixed3D, Location = new Point(24, 264), Size = new Size(612, 2) });
            LabelAt("停顿多久后翻译（毫秒）", 24, 294);
            var pause = new NumericUpDown { Minimum = 50, Maximum = 3000, Increment = 50, Value = settings.idle_ms, Location = new Point(490, 289), Width = 145 };
            LabelAt("译文显示多久（秒）", 24, 338);
            var disappear = new NumericUpDown { Minimum = 3, Maximum = 120, Value = Math.Min(120, settings.dismiss_ms / 1000), Location = new Point(490, 333), Width = 145 };
            var follow = new CheckBox { Text = "浮窗跟随光标（拖动后会固定位置）", Checked = !settings.fixed_position, AutoSize = true, Location = new Point(24, 378) };
            var history = new CheckBox { Text = "保存成功译文，供日后复习", Checked = settings.record_history, AutoSize = true, Location = new Point(24, 412) };
            var save = new Button { Text = "保存", Location = new Point(410, 468), Width = 105, Height = 31 };
            var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(530, 468), Width = 105, Height = 31 };
            Controls.AddRange(new Control[] { pause, disappear, follow, history, save, cancel });
            var help = new LinkLabel { Text = "申请阿里云 API / 配置帮助", AutoSize = true, Location = new Point(24, 475) };
            help.LinkClicked += (_, __) => Process.Start(new ProcessStartInfo("https://help.aliyun.com/zh/model-studio/get-api-key") { UseShellExecute = true });
            Controls.Add(help);
            AcceptButton = save; CancelButton = cancel;
            test.Click += async (_, __) => {
                try {
                    var candidate = settings.ForConnection(address.Text, model.Text);
                    if (!usableKey && string.IsNullOrWhiteSpace(key.Text)) throw new ArgumentException("请先填写 API 密钥。");
                    test.Enabled = save.Enabled = false; status.ForeColor = Color.DimGray; status.Text = "正在测试连接……";
                    testing = new CancellationTokenSource();
                    var clock = Stopwatch.StartNew();
                    using (var translator = new Translator(candidate, key.Text)) {
                        string result = await translator.Translate("今天的天气很好。", _ => { }, testing.Token);
                        if (!IsDisposed) { status.ForeColor = Color.ForestGreen; status.Text = "连接成功（" + clock.ElapsedMilliseconds + " 毫秒）\n" + (result.Length > 100 ? result.Substring(0, 100) + "…" : result); }
                    }
                } catch (Exception error) {
                    if (!IsDisposed) { status.ForeColor = Color.Firebrick; status.Text = "连接失败：" + error.Message; }
                } finally {
                    if (testing != null) { testing.Dispose(); testing = null; }
                    if (!IsDisposed) test.Enabled = save.Enabled = true;
                }
            };
            save.Click += (_, __) => {
                try {
                    var candidate = settings.ForConnection(address.Text, model.Text);
                    if (!usableKey && string.IsNullOrWhiteSpace(key.Text)) throw new ArgumentException("请填写 API 密钥。");
                    candidate.idle_ms = (int)pause.Value; candidate.dismiss_ms = (int)disappear.Value * 1000;
                    candidate.fixed_position = !follow.Checked; candidate.record_history = history.Checked;
                    candidate.Save(key.Text);
                    settings.Apply(candidate);
                    DialogResult = DialogResult.OK; Close();
                } catch (Exception error) { status.ForeColor = Color.Firebrick; status.Text = "未保存：" + error.Message; }
            };
            FormClosed += (_, __) => { if (testing != null) testing.Cancel(); key.Clear(); };
        }
        private void LabelAt(string text, int x, int y) { Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y) }); }
        public void ExportPreview(string path) {
            CreatePreviewHandles(this);
            PerformLayout();
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height)); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
        }
        private static void CreatePreviewHandles(Control control) {
            var handle = control.Handle;
            foreach (Control child in control.Controls) CreatePreviewHandles(child);
        }
    }
}
