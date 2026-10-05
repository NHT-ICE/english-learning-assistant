using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace LearningIme {
    internal static class PortableTests {
        private static void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
        [STAThread] private static int Main() {
            string project = Settings.Load().Root;
            string fixture = Path.Combine(project, "tests", "portable-" + Guid.NewGuid().ToString("N"));
            string fresh = Path.Combine(fixture, "fresh-user");
            Directory.CreateDirectory(fixture);
            File.WriteAllText(Path.Combine(fixture, "settings.json"), "{\"base_url\":\"https://parent-private.invalid/v1\",\"model\":\"parent-private-model\"}");
            var checks = new List<string>(); string error = null;
            try {
                var settings = Settings.LoadPortable(fresh);
                Require(settings.Root == fresh && settings.model == "qwen-mt-flash"
                    && settings.base_url == "https://dashscope.aliyuncs.com/compatible-mode/v1", "误读父目录个人配置");
                checks.Add("删除或缺失设置时在自身目录创建干净配置，不读取父目录");
                Require(!settings.HasKey && !settings.HasUsableKey && !settings.diagnostics_enabled && !settings.fixed_position, "新用户默认状态错误");
                checks.Add("新用户无密钥、位置重置、默认关闭诊断");
                int opens = 0;
                Require(!StartupConfiguration.Ensure(settings, () => { opens++; return false; }) && opens == 1, "取消配置后仍进入监听启动");
                checks.Add("首次取消配置停止后续启动");
                Require(!StartupConfiguration.Ensure(settings, () => true), "配置窗口未保存密钥仍启动");
                checks.Add("配置返回成功但没有可用密钥时仍停止启动");
                Application.EnableVisualStyles();
                using (var form = new StudySettingsForm(settings, true)) {
                    Require(form.Text.Contains("首次使用"), "首次引导标题缺失");
                    var key = form.Controls.OfType<TextBox>().Single(control => control.Location.Y == 143);
                    Require(key.UseSystemPasswordChar && key.Text == "", "密钥输入框不为空或未隐藏");
                    Require(form.Controls.OfType<LinkLabel>().Any(control => control.Text.Contains("阿里云")), "缺少注册链接");
                    form.ExportPreview(Path.Combine(project, "tests", "portable-first-run-preview.png"));
                    key.Text = "portable-test-only-not-a-real-key";
                    var save = form.Controls.OfType<Button>().Single(control => control.Text == "保存");
                    typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(save, new object[] { EventArgs.Empty });
                    Require(form.DialogResult == DialogResult.OK && settings.HasUsableKey, "首次设置没有保存自己的密钥");
                }
                checks.Add("首次引导有申请链接和空的隐藏密钥框，保存路径可用");
                Require(settings.ReadKey() == "portable-test-only-not-a-real-key"
                    && !File.ReadAllText(Path.Combine(fresh,".secrets","api-key.dpapi")).Contains("portable-test-only")
                    && !File.ReadAllText(Path.Combine(fresh,"settings.json")).Contains("portable-test-only"), "密钥未加密保存");
                checks.Add("密钥DPAPI往返成功且不进入设置明文");
                opens = 0;
                Require(StartupConfiguration.Ensure(settings, () => { opens++; return false; }) && opens == 0, "再次启动仍强制配置");
                checks.Add("已配置用户直接进入后续启动");
                var copy = settings.ForConnection(settings.base_url,settings.model);
                copy.Save(""); settings.Apply(copy);
                Require(!Settings.LoadPortable(fresh).diagnostics_enabled, "保存API设置启用了诊断");
                checks.Add("修改API选项后仍保留轻量化诊断设置");
                File.WriteAllText(Path.Combine(fresh,".secrets","api-key.dpapi"),"not-valid-ciphertext");
                Require(!settings.HasUsableKey && !StartupConfiguration.Ensure(settings, () => false), "损坏密钥未要求重新配置");
                checks.Add("不可解密密钥不会作为可用配置启动");
                DiagnosticsPolicy.Enabled = false;
                var diagnostics = new StudyDiagnostics(fresh);
                diagnostics.Accept(new Snapshot {scope="test:edit",text="测试正文"});
                diagnostics.Display(new Snapshot(),3);
                diagnostics.Window("test",new {visible=false});
                var route = new InputRouteDiagnostics(fresh);
                route.Windows(new Snapshot {text="测试正文"}); route.Native(new Snapshot {text="测试正文"},false);
                var source = (WindowsInputSource)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(WindowsInputSource));
                typeof(WindowsInputSource).GetMethod("SaveStatus", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(source,new object[] {"test"});
                Require(!File.Exists(Path.Combine(fresh,"popup-diagnostics.json")) && !File.Exists(Path.Combine(fresh,"input-route-status.json"))
                    && !File.Exists(Path.Combine(fresh,"windows-input-status.json")), "轻量版仍写诊断文件");
                checks.Add("输入、浮窗、路由及UIA诊断默认不写盘");
            } catch (Exception failure) { error = failure.ToString(); }
            finally { DiagnosticsPolicy.Enabled = true; }
            File.WriteAllText(Path.Combine(project,"portable-test-results.json"),new JavaScriptSerializer().Serialize(new {passed=error==null,error,checks}));
            return error==null?0:1;
        }
    }
}
