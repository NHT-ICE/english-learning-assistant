using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LearningIme {
    internal interface ITranslator {
        Task<string> Translate(string source, Action<string> progress, CancellationToken token);
    }
    internal sealed class Translator : ITranslator, IDisposable {
        private readonly HttpClient http = new HttpClient();
        private readonly Settings settings;
        private readonly string keyOverride;
        public Translator(Settings settings, string keyOverride = null) {
            this.settings = settings;
            this.keyOverride = string.IsNullOrWhiteSpace(keyOverride) ? null : keyOverride.Trim();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            http.Timeout = TimeSpan.FromSeconds(8);
        }
        public async Task<string> Translate(string source, Action<string> progress, CancellationToken token) {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                deadline.CancelAfter(8000);
                return await TranslateCore(source, progress, deadline.Token).ConfigureAwait(false);
            }
        }
        private async Task<string> TranslateCore(string source, Action<string> progress, CancellationToken token) {
            var json = new JavaScriptSerializer();
            var options = new Dictionary<string, object> { {"source_lang", "Chinese"}, {"target_lang", "English"} };
            var terms = Glossary.Matches(source);
            if (terms.Count > 0) options["terms"] = terms;
            var body = new {
                model = settings.model,
                messages = new[] { new { role = "user", content = source } },
                translation_options = options,
                stream = true
            };
            using (var request = new HttpRequestMessage(HttpMethod.Post, settings.base_url.TrimEnd('/') + "/chat/completions")) {
                request.Headers.ExpectContinue = false;
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", keyOverride ?? settings.ReadKey());
                request.Content = new StringContent(json.Serialize(body), Encoding.UTF8, "application/json");
                using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false)) {
                    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("翻译服务返回 HTTP " + (int)response.StatusCode + "，可稍后重试。");
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var stop = token.Register(() => stream.Dispose()))
                    using (var reader = new StreamReader(stream, Encoding.UTF8)) {
                        var output = new StringBuilder();
                        string line;
                        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null) {
                            token.ThrowIfCancellationRequested();
                            if (!line.StartsWith("data:")) continue;
                            string data = line.Substring(5).Trim();
                            if (data == "[DONE]") break;
                            var item = json.Deserialize<Dictionary<string, object>>(data);
                            object choicesObject;
                            if (!item.TryGetValue("choices", out choicesObject)) continue;
                            foreach (Dictionary<string, object> choice in (IEnumerable)choicesObject) {
                                object deltaObject;
                                if (!choice.TryGetValue("delta", out deltaObject)) continue;
                                var delta = deltaObject as Dictionary<string, object>;
                                object content;
                                if (delta != null && delta.TryGetValue("content", out content) && content is string) {
                                    output.Append((string)content);
                                    progress(output.ToString());
                                }
                            }
                        }
                        token.ThrowIfCancellationRequested();
                        if (output.Length == 0) throw new InvalidOperationException("服务未返回译文。");
                        return output.ToString();
                    }
                }
            }
        }
        public void Dispose() { http.Dispose(); }
    }
}
