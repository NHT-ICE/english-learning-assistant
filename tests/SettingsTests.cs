using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace LearningIme {
    internal static class SettingsTests {
        public static void Run(string root) {
            string fixture = Path.Combine(root, "tests", "settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixture);
            string config = Path.Combine(fixture, "settings.json");
            File.WriteAllText(config, "{\"base_url\":\"https://example.invalid/v1\",\"model\":\"test-model\",\"idle_ms\":300,\"dismiss_ms\":20000,\"record_history\":false,\"retained_option\":42}");
            var checks = new List<string>(); string error = null;
            try {
                var settings = Settings.Load(fixture);
                Require(!settings.HasKey, "新安装状态"); checks.Add("无密钥时仍可进入设置");
                settings.Save("test-only-not-a-real-api-key");
                string keyPath = Path.Combine(fixture, ".secrets", "api-key.dpapi");
                Require(settings.ReadKey() == "test-only-not-a-real-api-key" && !File.ReadAllText(keyPath).Contains("test-only"), "DPAPI 加密往返"); checks.Add("密钥加密保存且可读取");
                string originalCiphertext = File.ReadAllText(keyPath);
                var candidate = settings.ForConnection(" https://new.example.invalid/v1/chat/completions/ ", " replacement-model ");
                Require(!candidate.record_history, "保留学习选项");
                candidate.Save(""); settings.Apply(candidate);
                var reloaded = Settings.Load(fixture);
                Require(reloaded.base_url == "https://new.example.invalid/v1" && reloaded.model == "replacement-model", "地址及模型持久化"); checks.Add("地址和模型持久化且自动规范化");
                Require(File.ReadAllText(keyPath) == originalCiphertext, "留空密钥必须保留"); checks.Add("留空沿用现有密钥");
                var values = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(config));
                Require(Convert.ToInt32(values["retained_option"]) == 42 && !reloaded.record_history && !File.ReadAllText(config).Contains("test-only"), "未知选项和秘密隔离"); checks.Add("保留原设置且配置不包含密钥");
                bool rejected = false;
                try { settings.ForConnection("http://example.invalid/v1", "model"); } catch (ArgumentException) { rejected = true; }
                Require(rejected, "拒绝非 HTTPS"); checks.Add("校验接口地址");
                rejected = false; try { settings.ForConnection(settings.base_url, ""); } catch (ArgumentException) { rejected = true; }
                Require(rejected, "空模型不能保存"); checks.Add("校验模型名称");
                using (var locked = new FileStream(config, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    rejected = false;
                    try { candidate.Save("second-test-only-key"); } catch (IOException) { rejected = true; } catch (UnauthorizedAccessException) { rejected = true; }
                    Require(rejected, "模拟配置文件写入失败");
                }
                Require(File.ReadAllText(keyPath) == originalCiphertext && settings.ReadKey() == "test-only-not-a-real-api-key", "保存失败须回滚密钥"); checks.Add("配置保存失败时回滚密钥");
            } catch (Exception failure) { error = failure.Message; }
            File.WriteAllText(Path.Combine(root, "settings-test-results.json"), new JavaScriptSerializer().Serialize(new { passed = error == null, error, checks }));
        }
        private static void Require(bool passed, string description) { if (!passed) throw new Exception(description); }
    }
}
