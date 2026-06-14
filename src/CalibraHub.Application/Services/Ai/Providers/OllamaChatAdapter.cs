using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace CalibraHub.Application.Services.Ai.Providers;

/// <summary>
/// 2026-05-23 — Ollama (lokal LLM runtime) adapter (https://github.com/ollama/ollama/blob/main/docs/api.md).
///
/// Microsoft.Extensions.AI henüz Ollama için stabil paket sunmuyor (Microsoft.Extensions.AI.Ollama
/// preview). Kalan adapter'larla tutarlı olsun diye direkt HTTP üzerinden custom IChatClient.
///
/// **Endpoint:**
///   - Default: http://localhost:11434
///   - POST /api/chat
///
/// **Auth:**
///   - Lokal kurulum: API key gerekmez (boş geçilebilir)
///   - Reverse-proxy arkasında ise key, "Authorization" header'ında Bearer/Basic olarak gönderilir
///
/// **Streaming format:** NDJSON (her satır bir JSON document).
///   Örnek satır: {"model":"llama3.1","message":{"role":"assistant","content":" merhaba"},"done":false}
///   Son satır: {..., "done":true} ve istatistikler.
///
/// **Model:** `llama3.1:8b`, `llama3:8b`, `qwen2.5-coder:7b` vb. — kullanıcı `ollama pull` ile çekmiş olmalı.
/// </summary>
public sealed class OllamaChatAdapter : IChatClient
{
    private const string DefaultEndpoint = "http://localhost:11434";
    private const string DefaultModel    = "llama3.1:8b";

    private readonly HttpClient _http;
    private readonly string?    _apiKey;       // opsiyonel — proxy arkasinda auth varsa kullanilir
    private readonly string     _baseUrl;
    private readonly string     _model;
    private readonly bool       _ownsHttp;

    public OllamaChatAdapter(string? apiKey, string? endpointUrl, string? defaultModel, HttpClient? http = null)
    {
        // ApiKey opsiyonel — null/empty olabilir. Lokal Ollama yaygin senaryosu.
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _baseUrl = NormalizeBaseUrl(endpointUrl);
        _model = string.IsNullOrWhiteSpace(defaultModel) ? DefaultModel : defaultModel.Trim();

        if (http is null) { _http = new HttpClient(); _ownsHttp = true; }
        else { _http = http; _ownsHttp = false; }

        Metadata = new ChatClientMetadata("ollama", new Uri(_baseUrl), _model);
    }

    public ChatClientMetadata Metadata { get; }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType?.IsInstanceOfType(this) == true ? this : null;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var body = BuildBody(messages, options, stream: false);
        using var req = BuildHttpRequest(body);
        using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama HTTP {(int)resp.StatusCode}: {Truncate(json, 400)}");

        using var doc = JsonDocument.Parse(json);
        var text = ExtractMessageText(doc.RootElement);
        var modelUsed = doc.RootElement.TryGetProperty("model", out var mEl) ? mEl.GetString() : _model;

        // 2026-05-24: tool_calls — non-streaming response'da da parse et.
        var contents = new List<AIContent>();
        if (!string.IsNullOrEmpty(text))
            contents.Add(new TextContent(text));
        if (doc.RootElement.TryGetProperty("message", out var msgEl)
            && msgEl.TryGetProperty("tool_calls", out var tcArr)
            && tcArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in tcArr.EnumerateArray())
            {
                if (!tc.TryGetProperty("function", out var fnEl)) continue;
                var name = fnEl.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                if (string.IsNullOrEmpty(name)) continue;
                Dictionary<string, object?>? args = null;
                if (fnEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
                {
                    args = new Dictionary<string, object?>();
                    foreach (var prop in argsEl.EnumerateObject())
                        args[prop.Name] = JsonElementToObject(prop.Value);
                }
                var callId = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                contents.Add(new FunctionCallContent(callId, name!, args));
            }
        }

        var responseMessage = contents.Count > 0
            ? new ChatMessage(ChatRole.Assistant, contents)
            : new ChatMessage(ChatRole.Assistant, text);

        return new ChatResponse(responseMessage)
        {
            ModelId = modelUsed,
            FinishReason = ChatFinishReason.Stop,
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var body = BuildBody(messages, options, stream: true);
        using var req = BuildHttpRequest(body);
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Ollama HTTP {(int)resp.StatusCode}: {Truncate(err, 400)}");
        }

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        // NDJSON — her satir bir JSON document
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument? doc = null;
            string? deltaText = null;
            List<AIContent>? extraContents = null;   // 2026-05-24: tool_calls icin
            bool done = false;
            try
            {
                doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("done", out var doneEl) && doneEl.ValueKind == JsonValueKind.True)
                    done = true;
                if (doc.RootElement.TryGetProperty("message", out var msgEl))
                {
                    if (msgEl.TryGetProperty("content", out var contentEl))
                        deltaText = contentEl.GetString();

                    // 2026-05-24: tool_calls — Ollama tool calling response formati.
                    // FunctionInvokingChatClient wrapper'i FunctionCallContent gorunce
                    // otomatik tool'u calistirir, sonra request'i tekrar acar.
                    if (msgEl.TryGetProperty("tool_calls", out var tcArr) && tcArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tc in tcArr.EnumerateArray())
                        {
                            if (!tc.TryGetProperty("function", out var fnEl)) continue;
                            var name = fnEl.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                            if (string.IsNullOrEmpty(name)) continue;
                            // arguments — JsonElement (object). Dictionary'e cevir.
                            Dictionary<string, object?>? args = null;
                            if (fnEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
                            {
                                args = new Dictionary<string, object?>();
                                foreach (var prop in argsEl.EnumerateObject())
                                {
                                    args[prop.Name] = JsonElementToObject(prop.Value);
                                }
                            }
                            // CallId — Ollama bazi versiyonlarda donmuyor; bos da olabilir.
                            var callId = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                            (extraContents ??= new List<AIContent>()).Add(new FunctionCallContent(callId, name!, args));
                        }
                    }
                }
            }
            catch (JsonException) { /* malformed line — skip */ }
            finally { doc?.Dispose(); }

            if (!string.IsNullOrEmpty(deltaText) || extraContents != null)
            {
                var update = new ChatResponseUpdate { Role = ChatRole.Assistant };
                if (!string.IsNullOrEmpty(deltaText))
                    update.Contents.Add(new TextContent(deltaText));
                if (extraContents != null)
                {
                    foreach (var c in extraContents) update.Contents.Add(c);
                }
                yield return update;
            }

            if (done) yield break;
        }
    }

    /// <summary>JsonElement → CLR object (Dictionary/List/primitive). Tool argumanlarini deserialize ederken kullanilir.</summary>
    private static object? JsonElementToObject(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String: return el.GetString();
            case JsonValueKind.Number:
                if (el.TryGetInt64(out var l)) return l;
                if (el.TryGetDouble(out var d)) return d;
                return el.GetRawText();
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Null: return null;
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object?>();
                foreach (var p in el.EnumerateObject()) obj[p.Name] = JsonElementToObject(p.Value);
                return obj;
            case JsonValueKind.Array:
                var arr = new List<object?>();
                foreach (var item in el.EnumerateArray()) arr.Add(JsonElementToObject(item));
                return arr;
            default: return null;
        }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private HttpRequestMessage BuildHttpRequest(object body)
    {
        var url = _baseUrl + "/api/chat";
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = JsonContent.Create(body);
        if (!string.IsNullOrEmpty(_apiKey))
        {
            // Reverse-proxy auth — "Bearer X" veya "Basic ..." gibi degerler dogrudan kullanilir.
            // Sadece duz token yazdiysa "Bearer" prefix ekleyelim.
            var headerVal = _apiKey.Contains(' ') ? _apiKey : "Bearer " + _apiKey;
            req.Headers.TryAddWithoutValidation("Authorization", headerVal);
        }
        return req;
    }

    private object BuildBody(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
    {
        var model = options?.ModelId ?? _model;
        var temperature = options?.Temperature;
        var maxTokens = options?.MaxOutputTokens;
        var tools = options?.Tools;

        // Ollama icin mesaj format'i: { role, content, images?, tool_calls? }
        // 2026-05-24: Tool calling icin assistant FunctionCallContent + tool role messages
        // (FunctionResultContent) burada serialize edilir.
        var msgs = new List<Dictionary<string, object?>>();
        foreach (var m in messages)
        {
            // Role mapping — Tool role'u eklendi
            string role;
            if (m.Role == ChatRole.System) role = "system";
            else if (m.Role == ChatRole.User) role = "user";
            else if (m.Role == ChatRole.Tool) role = "tool";
            else role = "assistant";

            // Multimodal + tool calls: Contents'tan TextContent + DataContent + FunctionCallContent
            // + FunctionResultContent ayikla.
            var textBuf = new System.Text.StringBuilder();
            List<string>? images = null;
            List<Dictionary<string, object?>>? toolCalls = null;

            if (m.Contents != null && m.Contents.Count > 0)
            {
                foreach (var c in m.Contents)
                {
                    if (c is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                    {
                        if (textBuf.Length > 0) textBuf.Append('\n');
                        textBuf.Append(tc.Text);
                    }
                    else if (c is DataContent dc && (dc.MediaType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        var base64 = Convert.ToBase64String(dc.Data.ToArray());
                        (images ??= new List<string>()).Add(base64);
                    }
                    else if (c is FunctionCallContent fcc)
                    {
                        // Assistant mesajinin onceki tool cagrisi — Ollama'ya tekrar ileterek
                        // baglami koruyoruz (multi-turn tool reasoning).
                        var args = fcc.Arguments != null
                            ? fcc.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value)
                            : new Dictionary<string, object?>();
                        (toolCalls ??= new List<Dictionary<string, object?>>()).Add(new Dictionary<string, object?>
                        {
                            ["function"] = new Dictionary<string, object?>
                            {
                                ["name"] = fcc.Name,
                                ["arguments"] = args,
                            }
                        });
                    }
                    else if (c is FunctionResultContent frc)
                    {
                        // Tool sonucu — content alanina serialize et (Ollama "tool" role'u bekler)
                        var resultStr = frc.Result switch
                        {
                            null => "",
                            string s => s,
                            _ => JsonSerializer.Serialize(frc.Result),
                        };
                        if (textBuf.Length > 0) textBuf.Append('\n');
                        textBuf.Append(resultStr);
                    }
                }
            }
            else if (!string.IsNullOrEmpty(m.Text))
            {
                textBuf.Append(m.Text);
            }

            var msg = new Dictionary<string, object?>
            {
                ["role"]    = role,
                ["content"] = textBuf.ToString(),
            };
            if (images != null && images.Count > 0) msg["images"] = images;
            if (toolCalls != null && toolCalls.Count > 0) msg["tool_calls"] = toolCalls;
            msgs.Add(msg);
        }

        var body = new Dictionary<string, object?>
        {
            ["model"]    = model,
            ["messages"] = msgs,
            ["stream"]   = stream,
        };

        // 2026-05-24: Tool listesi — AIFunction'lardan JSON schema turetilir.
        // Ollama OpenAI-uyumlu format bekler: { type: "function", function: { name, description, parameters } }
        if (tools != null && tools.Count > 0)
        {
            var toolsList = new List<object>();
            foreach (var t in tools)
            {
                if (t is AIFunction af)
                {
                    // JsonSchema bir JsonElement — direkt JSON olarak inline'a yaz
                    toolsList.Add(new Dictionary<string, object?>
                    {
                        ["type"] = "function",
                        ["function"] = new Dictionary<string, object?>
                        {
                            ["name"] = af.Name,
                            ["description"] = af.Description ?? string.Empty,
                            ["parameters"] = af.JsonSchema,
                        }
                    });
                }
            }
            if (toolsList.Count > 0) body["tools"] = toolsList;
        }

        // Ollama "options" alt-objesi — temperature, num_predict (= max_tokens)
        var optsMap = new Dictionary<string, object?>();
        if (temperature.HasValue) optsMap["temperature"] = temperature.Value;
        if (maxTokens.HasValue) optsMap["num_predict"] = maxTokens.Value;
        if (optsMap.Count > 0) body["options"] = optsMap;
        return body;
    }

    private static string ExtractMessageText(JsonElement root)
    {
        if (root.TryGetProperty("message", out var msgEl)
            && msgEl.TryGetProperty("content", out var contentEl))
        {
            return contentEl.GetString() ?? string.Empty;
        }
        // Bazi eski Ollama versiyonlarinda "response" alaninda dondu
        if (root.TryGetProperty("response", out var respEl))
            return respEl.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static string NormalizeBaseUrl(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return DefaultEndpoint;
        var url = endpoint.Trim().TrimEnd('/');
        // Sik karsilasilan hata: kullanici "/api/chat" yapistirir — temizle.
        if (url.EndsWith("/api/chat", StringComparison.OrdinalIgnoreCase))
            url = url[..^"/api/chat".Length];
        if (url.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase))
            url = url[..^"/api/generate".Length];
        return url;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");

    // 2026-05-24: OllamaMessage record kaldirildi — multimodal icin Dictionary<string, object?>
    // kullaniliyor (text-only + image arrayli mesajlar tek format'ta serialize edilebilir).
}
