#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GeminiWebTranslator;

public sealed class GeminiGuestHttpClient : IDisposable
{
    public sealed record CapturedRequest(string Url, IReadOnlyDictionary<string, string> Headers, string PostData);

    private readonly WebView2 _webView;
    private readonly GeminiAutomation _automation;
    private readonly WebViewRequestCaptor _captor;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public event Action<string>? OnLog;

    public GeminiGuestHttpClient(WebView2 webView, GeminiAutomation automation)
    {
        _webView = webView;
        _automation = automation;
        _captor = new WebViewRequestCaptor(webView);
    }

    public async Task<string> GenerateAsync(string prompt, int timeoutMs = 30000)
    {
        if (!await _lock.WaitAsync(0))
        {
            throw new InvalidOperationException("동시 요청은 지원되지 않습니다.");
        }

        try
        {
            if (_webView.CoreWebView2 == null)
            {
                throw new InvalidOperationException("WebView2가 초기화되지 않았습니다.");
            }

            var ready = await _automation.EnsureReadyAsync();
            if (!ready)
            {
                throw new Exception("WebView 준비 상태가 아닙니다.");
            }

            OnLog?.Invoke("[GuestHTTP] StreamGenerate 요청 캡처 준비...");
            var captureTask = _captor.CaptureStreamGenerateAsync(TimeSpan.FromMilliseconds(timeoutMs));

            var sent = await _automation.SendMessageAsync(prompt);
            if (!sent)
            {
                throw new Exception("메시지 전송 실패");
            }

            var captured = await captureTask;
            ValidateModelHeaders(captured.Headers);

            OnLog?.Invoke("[GuestHTTP] 쿠키 없이 요청 재전송...");
            var body = await ReplayNoCookieAsync(captured);
            var text = ExtractWrbText(body);

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new Exception("응답 텍스트 추출 실패");
            }

            return text;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static void ValidateModelHeaders(IReadOnlyDictionary<string, string> headers)
    {
        var required = new[]
        {
            "x-goog-ext-525001261-jspb",
            "x-goog-ext-525005358-jspb",
            "x-goog-ext-73010989-jspb",
        };

        var missing = required.Where(h => !headers.Keys.Any(k => string.Equals(k, h, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missing.Count > 0)
        {
            throw new Exception("모델 헤더 누락: " + string.Join(", ", missing));
        }
    }

    private static async Task<string> ReplayNoCookieAsync(CapturedRequest captured)
    {
        using var http = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, captured.Url);

        foreach (var kv in captured.Headers)
        {
            if (kv.Key.Equals("cookie", StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Key.Equals("host", StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Key.Equals("content-length", StringComparison.OrdinalIgnoreCase)) continue;
            if (kv.Key.Equals("content-type", StringComparison.OrdinalIgnoreCase)) continue;

            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }

        req.Content = new StringContent(captured.PostData ?? "", Encoding.UTF8, "application/x-www-form-urlencoded");

        var resp = await http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();

        if (!resp.IsSuccessStatusCode)
        {
            var snippet = body.Length > 500 ? body[..500] : body;
            throw new Exception($"HTTP {(int)resp.StatusCode}: {snippet}");
        }

        return body;
    }

    private static string ExtractWrbText(string body)
    {
        if (string.IsNullOrEmpty(body)) return "";

        if (body.StartsWith(")]}'"))
        {
            var idx = body.IndexOf('\n');
            if (idx >= 0) body = body[(idx + 1)..];
        }

        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var strings = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (int.TryParse(line, out _))
            {
                if (i + 1 < lines.Length) line = lines[++i].Trim();
                else break;
            }

            if (!line.StartsWith("[[")) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) continue;

                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Array) continue;
                    if (item.GetArrayLength() < 3) continue;
                    if (item[0].ValueKind != JsonValueKind.String) continue;
                    if (item[0].GetString() != "wrb.fr") continue;
                    if (item[2].ValueKind != JsonValueKind.String) continue;

                    var payloadStr = item[2].GetString();
                    if (string.IsNullOrEmpty(payloadStr)) continue;

                    try
                    {
                        using var payloadDoc = JsonDocument.Parse(payloadStr);
                        CollectStrings(payloadDoc.RootElement, strings);
                    }
                    catch { }
                }
            }
            catch { }
        }

        return strings.Count == 0 ? "" : strings.OrderByDescending(s => s.Length).FirstOrDefault() ?? "";
    }

    private static void CollectStrings(JsonElement el, List<string> strings)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                strings.Add(el.GetString() ?? "");
                break;
            case JsonValueKind.Array:
                foreach (var v in el.EnumerateArray()) CollectStrings(v, strings);
                break;
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject()) CollectStrings(p.Value, strings);
                break;
        }
    }

    public void Dispose()
    {
        _captor.Dispose();
        _lock.Dispose();
    }

    private sealed class WebViewRequestCaptor : IDisposable
    {
        private readonly WebView2 _webView;
        private readonly object _gate = new();
        private TaskCompletionSource<CapturedRequest>? _pending;
        private bool _networkEnabled;

        public WebViewRequestCaptor(WebView2 webView)
        {
            _webView = webView;
        }

        public async Task<CapturedRequest> CaptureStreamGenerateAsync(TimeSpan timeout)
        {
            if (_webView.CoreWebView2 == null)
            {
                throw new InvalidOperationException("WebView2가 초기화되지 않았습니다.");
            }

            await EnsureNetworkEnabledAsync();

            var tcs = new TaskCompletionSource<CapturedRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                if (_pending != null)
                {
                    throw new InvalidOperationException("이미 캡처가 진행 중입니다.");
                }
                _pending = tcs;
            }

            using var cts = new CancellationTokenSource(timeout);
            cts.Token.Register(() =>
            {
                tcs.TrySetException(new TimeoutException("StreamGenerate 요청 캡처 타임아웃"));
                lock (_gate)
                {
                    _pending = null;
                }
            });

            return await tcs.Task;
        }

        private async Task EnsureNetworkEnabledAsync()
        {
            if (_networkEnabled) return;

            if (_webView.CoreWebView2 == null)
            {
                throw new InvalidOperationException("WebView2가 초기화되지 않았습니다.");
            }

            await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.enable", "{}");
            _webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent").DevToolsProtocolEventReceived += OnDevToolsProtocolEventReceived;
            _networkEnabled = true;
        }

        private async void OnDevToolsProtocolEventReceived(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            TaskCompletionSource<CapturedRequest>? tcs;
            lock (_gate)
            {
                tcs = _pending;
            }

            if (tcs == null) return;

            try
            {
                using var doc = JsonDocument.Parse(e.ParameterObjectAsJson);
                var root = doc.RootElement;

                if (!root.TryGetProperty("request", out var requestEl)) return;
                var url = requestEl.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? "" : "";
                if (!url.Contains("StreamGenerate", StringComparison.OrdinalIgnoreCase)) return;

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (requestEl.TryGetProperty("headers", out var headersEl) && headersEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var h in headersEl.EnumerateObject())
                    {
                        headers[h.Name] = h.Value.ValueKind == JsonValueKind.String ? (h.Value.GetString() ?? "") : h.Value.ToString();
                    }
                }

                string? postData = null;
                if (requestEl.TryGetProperty("postData", out var postDataEl) && postDataEl.ValueKind == JsonValueKind.String)
                {
                    postData = postDataEl.GetString();
                }

                if (string.IsNullOrEmpty(postData))
                {
                    if (root.TryGetProperty("requestId", out var reqIdEl))
                    {
                        var reqId = reqIdEl.GetString();
                        if (!string.IsNullOrEmpty(reqId))
                        {
                            var reqJson = $"{{\"requestId\":\"{reqId}\"}}";
                            var respJson = await _webView.CoreWebView2!.CallDevToolsProtocolMethodAsync("Network.getRequestPostData", reqJson);
                            if (!string.IsNullOrEmpty(respJson))
                            {
                                using var postDoc = JsonDocument.Parse(respJson);
                                if (postDoc.RootElement.TryGetProperty("postData", out var pd) && pd.ValueKind == JsonValueKind.String)
                                {
                                    postData = pd.GetString();
                                }
                            }
                        }
                    }
                }

                var captured = new CapturedRequest(url, headers, postData ?? "");
                tcs.TrySetResult(captured);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                lock (_gate)
                {
                    _pending = null;
                }
            }
        }

        public void Dispose()
        {
            try
            {
                if (_webView.CoreWebView2 != null && _networkEnabled)
                {
                    _webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent").DevToolsProtocolEventReceived -= OnDevToolsProtocolEventReceived;
                }
            }
            catch { }
        }
    }
}
