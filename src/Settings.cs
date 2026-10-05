using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace LearningIme {
    internal sealed class Settings {
        public string base_url { get; set; }
        public string model { get; set; }
        public int idle_ms { get; set; }
        public int dismiss_ms { get; set; }
        public bool fixed_position { get; set; }
        public int fixed_x { get; set; }
        public int fixed_y { get; set; }
        public bool record_history { get; set; } = true;
        public bool diagnostics_enabled { get; set; } = true;
        public string Root { get; private set; }

        public static Settings Load(string directory = null) {
            var root = new DirectoryInfo(directory ?? AppDomain.CurrentDomain.BaseDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "settings.json"))) root = root.Parent;
            if (root == null) throw new InvalidOperationException("找不到 settings.json。");
            var settings = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(root.FullName, "settings.json"), Encoding.UTF8));
            settings.Root = root.FullName;
            var url = new Uri(settings.base_url);
            if (url.Scheme != "https") throw new InvalidOperationException("翻译接口必须使用 HTTPS。");
            settings.idle_ms = Math.Max(50, Math.Min(3000, settings.idle_ms));
            settings.dismiss_ms = Math.Max(3000, settings.dismiss_ms);
            return settings;
        }

        public static Settings LoadPortable(string directory = null) {
            string root = Path.GetFullPath(directory ?? AppDomain.CurrentDomain.BaseDirectory);
            string path = Path.Combine(root, "settings.json");
            if (!File.Exists(path)) {
                Directory.CreateDirectory(root);
                var defaults = new Settings {
                    base_url = "https://dashscope.aliyuncs.com/compatible-mode/v1", model = "qwen-mt-flash",
                    idle_ms = 150, dismiss_ms = 20000, fixed_position = false,
                    record_history = true, diagnostics_enabled = false
                };
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(new {
                    defaults.base_url, defaults.model, defaults.idle_ms, defaults.dismiss_ms,
                    defaults.fixed_position, defaults.fixed_x, defaults.fixed_y,
                    defaults.record_history, defaults.diagnostics_enabled
                }), new UTF8Encoding(false));
            }
            // The portable app never searches a parent project's configuration.
            return Load(root);
        }

        public string ReadKey() {
            string hex = File.ReadAllText(Path.Combine(Root, ".secrets", "api-key.dpapi")).Trim();
            var data = new byte[hex.Length / 2];
            for (int i = 0; i < data.Length; i++) data[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            var plain = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
            try { return Encoding.Unicode.GetString(plain); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }

        public bool HasKey { get { return File.Exists(Path.Combine(Root, ".secrets", "api-key.dpapi")); } }
        public bool HasUsableKey {
            get {
                if (!HasKey) return false;
                try { return !string.IsNullOrWhiteSpace(ReadKey()); }
                catch (CryptographicException) { return false; }
                catch (FormatException) { return false; }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }
        }

        public static string ValidateBaseUrl(string value) {
            Uri uri;
            value = (value ?? "").Trim().TrimEnd('/');
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https"
                || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                throw new ArgumentException("请填写 HTTPS 接口地址（Base URL），不要带查询参数、密钥或控制台网页链接。");
            if (value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - "/chat/completions".Length);
            return value;
        }

        public static string ValidateModel(string value) {
            value = (value ?? "").Trim();
            if (value.Length == 0 || value.Length > 200 || System.Text.RegularExpressions.Regex.IsMatch(value, @"\s"))
                throw new ArgumentException("请填写模型名称，例如 qwen-mt-flash。");
            return value;
        }

        public Settings ForConnection(string address, string name) {
            return new Settings { Root = Root, base_url = ValidateBaseUrl(address), model = ValidateModel(name), idle_ms = idle_ms, dismiss_ms = dismiss_ms,
                fixed_position = fixed_position, fixed_x = fixed_x, fixed_y = fixed_y, record_history = record_history,
                diagnostics_enabled = diagnostics_enabled };
        }

        public void Apply(Settings saved) {
            base_url = saved.base_url; model = saved.model; idle_ms = saved.idle_ms; dismiss_ms = saved.dismiss_ms;
            fixed_position = saved.fixed_position; fixed_x = saved.fixed_x; fixed_y = saved.fixed_y; record_history = saved.record_history;
            diagnostics_enabled = saved.diagnostics_enabled;
        }

        public void Save(string newKey = null) {
            string address = ValidateBaseUrl(base_url), name = ValidateModel(model);
            var json = new JavaScriptSerializer();
            string path = Path.Combine(Root, "settings.json");
            var values = json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(path));
            values["base_url"] = address; values["model"] = name;
            values["idle_ms"] = idle_ms; values["dismiss_ms"] = dismiss_ms;
            values["fixed_position"] = fixed_position;
            values["fixed_x"] = fixed_x; values["fixed_y"] = fixed_y;
            values["record_history"] = record_history;
            values["diagnostics_enabled"] = diagnostics_enabled;
            string keyPath = Path.Combine(Root, ".secrets", "api-key.dpapi");
            string oldCiphertext = null;
            bool replacingKey = !string.IsNullOrWhiteSpace(newKey);
            if (replacingKey) {
                newKey = newKey.Trim();
                if (newKey.Length > 4096 || System.Text.RegularExpressions.Regex.IsMatch(newKey, @"\s")) throw new ArgumentException("密钥不能包含空白字符。");
                if (File.Exists(keyPath)) oldCiphertext = File.ReadAllText(keyPath);
                var plain = Encoding.Unicode.GetBytes(newKey);
                byte[] encrypted;
                try { encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser); }
                finally { Array.Clear(plain, 0, plain.Length); }
                Directory.CreateDirectory(Path.GetDirectoryName(keyPath));
                AtomicWrite(keyPath, BitConverter.ToString(encrypted).Replace("-", "").ToLowerInvariant());
            }
            try { AtomicWrite(path, json.Serialize(values)); }
            catch {
                if (replacingKey) {
                    if (oldCiphertext != null) AtomicWrite(keyPath, oldCiphertext);
                    else File.Delete(keyPath);
                }
                throw;
            }
            base_url = address; model = name;
        }

        private static void AtomicWrite(string path, string content) {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
