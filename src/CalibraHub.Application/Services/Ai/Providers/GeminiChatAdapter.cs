using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CalibraHub.Application.Services.Ai.Providers;

/// <summary>
/// 2026-05-23 — Google Gemini generateContent / streamGenerateContent API adapter
/// (https://ai.google.dev/api/generate-content).
/// 2026-05-24 — Tool calling desteklendi (Calibo write tool'lari Gemini'de calisir).
///
/// **Endpoint:** https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}
/// **Stream:** ?alt=sse + streamGenerateContent
///
/// Roller: "user" ve "model" (assistant degil). System mesajlari systemInstruction'a tasinir.
///
/// **Tool calling formati:**
///   - Request: body.tools = [{ functionDeclarations: [{ name, description, parameters }] }]
///   - Response: content.parts icinde { functionCall: { name, args } } cikabilir
///   - Tool result: user role mesajinda parts: [{ functionResponse: { name, response: {result} } }]
/// </summary>
public sealed class GeminiChatAdapter : IChatClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";
    private const string DefaultModel = "gemini-1.5-flash";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly bool _ownsHttp;

    public GeminiChatAdapter(string apiKey, string? defaultModel, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Gemini API key boş.", nameof(apiKey));
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(defaultModel) ? DefaultModel : defaultModel;
        if (http is null) { _http = new HttpClient(); _ownsHttp = true; }
        else { _http = http; _ownsHttp = false; }

        Metadata = new ChatClientMetadata("gemini", new Uri("https://generativelanguage.googleapis.com"), _model);
    }

    public ChatClientMetadata Metadata { get; }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType?.IsInstanceOfType(this) == true ? this : null;

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var model = options?.ModelId ?? _model;
        var body = BuildBody(messages, options);
        var url = $"{BaseUrl}/{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(_apiKey)}";

        using var resp = await _http.PostAsJsonAsync(url, body, cancellationToken).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gemini HTTP {(int)resp.StatusCode}: {Truncate(json, 400)}");

        using var doc = JsonDocument.Parse(json);
        var contents = ExtractContents(doc.RootElement);

        var msg = contents.Count > 0
            ? new ChatMessage(ChatRole.Assistant, contents)
            : new ChatMessage(ChatRole.Assistant, string.Empty);

        return new ChatResponse(msg)
        {
            ModelId = model,
            FinishReason = ChatFinishReason.Stop,
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var model = options?.ModelId ?? _model;
        var body = BuildBody(messages, options);
        var url = $"{BaseUrl}/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse&key={Uri.EscapeDataString(_apiKey)}";

        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException($"Gemini HTTP {(int)resp.StatusCode}: {Truncate(err, 400)}");
        }

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var payload = line[5..].Trim();
            if (string.IsNullOrEmpty(payload)) continue;

            JsonDocument? doc = null;
            List<AIContent>? chunkContents = null;
            try
            {
                doc = JsonDocument.Parse(payload);
                chunkContents = ExtractContents(doc.RootElement);
            }
            catch (JsonException) { /* skip */ }
            finally { doc?.Dispose(); }

            if (chunkContents != null && chunkContents.Count > 0)
            {
                var update = new ChatResponseUpdate { Role = ChatRole.Assistant };
                foreach (var c in chunkContents) update.Contents.Add(c);
                yield return update;
            }
        }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static object BuildBody(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var sysBuilder = new StringBuilder();
        var contents = new List<Dictionary<string, object?>>();

        foreach (var m in messages)
        {
            if (m.Role == ChatRole.System)
            {
                var txt = m.Text ?? string.Empty;
                if (sysBuilder.Length > 0) sysBuilder.Append('\n');
                sysBuilder.Append(txt);
                continue;
            }

            // Gemini role mapping — "user", "model" (assistant). Tool sonucu user role'unde gelir.
            string role;
            if (m.Role == ChatRole.User) role = "user";
            else if (m.Role == ChatRole.Tool) role = "user";
            else role = "model";

            // Parts: text, inlineData (image), functionCall (assistant tool_use), functionResponse (tool result)
            var parts = new List<Dictionary<string, object?>>();

            if (m.Contents != null && m.Contents.Count > 0)
            {
                foreach (var c in m.Contents)
                {
                    if (c is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                    {
                        parts.Add(new Dictionary<string, object?>
                        {
                            ["text"] = tc.Text,
                        });
                    }
                    else if (c is DataContent dc && (dc.MediaType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add(new Dictionary<string, object?>
                        {
                            ["inlineData"] = new Dictionary<string, object?>
                            {
                                ["mimeType"] = dc.MediaType,
                                ["data"] = Convert.ToBase64String(dc.Data.ToArray()),
                            }
                        });
                    }
                    else if (c is FunctionCallContent fcc)
                    {
                        // Model'in onceki tool cagrisi — Gemini format: { functionCall: { name, args } }
                        parts.Add(new Dictionary<string, object?>
                        {
                            ["functionCall"] = new Dictionary<string, object?>
                            {
                                ["name"] = fcc.Name,
                                ["args"] = fcc.Arguments != null
                                    ? fcc.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value)
                                    : new Dictionary<string, object?>(),
                            }
                        });
                    }
                    else if (c is FunctionResultContent frc)
                    {
                        // Tool sonucu — Gemini format: { functionResponse: { name, response: { result } } }
                        // Not: name burada bilinmiyor (FunctionResultContent name tasimaz); ama Gemini'nin
                        // tool_use_id'sini takip etmiyoruz — name argumanini ust mesajdan turetmek zorundayiz.
                        // Pratik cozum: result objesini "result" key altinda sarmalla.
                        var resultObj = frc.Result switch
                        {
                            null => (object)new { result = "" },
                            string s => new { result = s },
                            _ => new { result = frc.Result },
                        };
                        parts.Add(new Dictionary<string, object?>
                        {
                            ["functionResponse"] = new Dictionary<string, object?>
                            {
                                ["name"] = ResolveFunctionNameForCallId(messages, frc.CallId) ?? "unknown",
                                ["response"] = resultObj,
                            }
                        });
                    }
                }
            }
            else if (!string.IsNullOrEmpty(m.Text))
            {
                parts.Add(new Dictionary<string, object?> { ["text"] = m.Text });
            }

            if (parts.Count == 0) continue;

            contents.Add(new Dictionary<string, object?>
            {
                ["role"] = role,
                ["parts"] = parts,
            });
        }

        var body = new Dictionary<string, object?>
        {
            ["contents"] = contents,
        };
        if (sysBuilder.Length > 0)
        {
            body["systemInstruction"] = new Dictionary<string, object?>
            {
                ["parts"] = new[] { new Dictionary<string, object?> { ["text"] = sysBuilder.ToString() } }
            };
        }

        // 2026-05-24: Tools — Gemini format: tools[{ functionDeclarations: [{ name, description, parameters }] }]
        // ÖNEMLI: Gemini OpenAPI/Proto subset bekler; MEAI'nin JSON Schema 2020-12 cikisi:
        //   - "type": ["string", "null"]  →  Gemini reddeder. "type": "string", "nullable": true olmali.
        //   - additionalProperties, $schema, unevaluatedProperties → unknown, kaldirilmali.
        //   - format alanlarinin bazi degerleri uyumsuz olabilir (date-time vs.) → muhafazakar yaklasim: bilinmeyen alan adlari da kaldirilir.
        var tools = options?.Tools;
        if (tools != null && tools.Count > 0)
        {
            var functionDecls = new List<object>();
            foreach (var t in tools)
            {
                if (t is AIFunction af)
                {
                    var sanitizedSchema = SanitizeSchemaForGemini(af.JsonSchema);
                    functionDecls.Add(new Dictionary<string, object?>
                    {
                        ["name"] = af.Name,
                        ["description"] = af.Description ?? string.Empty,
                        ["parameters"] = sanitizedSchema,
                    });
                }
            }
            if (functionDecls.Count > 0)
            {
                body["tools"] = new[]
                {
                    new Dictionary<string, object?> { ["functionDeclarations"] = functionDecls }
                };
            }
        }

        var genConfig = new Dictionary<string, object?>();
        if (options?.Temperature is { } temp) genConfig["temperature"] = temp;
        if (options?.MaxOutputTokens is { } maxT) genConfig["maxOutputTokens"] = maxT;
        if (options?.TopP is { } tp) genConfig["topP"] = tp;
        if (genConfig.Count > 0) body["generationConfig"] = genConfig;

        return body;
    }

    /// <summary>
    /// FunctionResultContent.CallId'den functionCall.name'i bulur — Gemini functionResponse'ta
    /// name zorunlu (call id desteklemiyor); MEAI ise sadece call id tasiyor.
    /// Cozum: ayni mesaj listesinde geriye dogru tarayip ayni CallId'li FunctionCallContent'i bul.
    /// </summary>
    private static string? ResolveFunctionNameForCallId(IEnumerable<ChatMessage> messages, string callId)
    {
        if (string.IsNullOrEmpty(callId)) return null;
        foreach (var m in messages)
        {
            if (m.Contents == null) continue;
            foreach (var c in m.Contents)
            {
                if (c is FunctionCallContent fcc && fcc.CallId == callId)
                    return fcc.Name;
            }
        }
        return null;
    }

    /// <summary>
    /// Gemini response'tan candidates[0].content.parts'i AIContent listesine cevirir.
    /// text → TextContent; functionCall → FunctionCallContent.
    /// </summary>
    private static List<AIContent> ExtractContents(JsonElement root)
    {
        var list = new List<AIContent>();
        if (!root.TryGetProperty("candidates", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var cand in arr.EnumerateArray())
        {
            if (!cand.TryGetProperty("content", out var content)) continue;
            if (!content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array) continue;

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var txt))
                {
                    var s = txt.GetString();
                    if (!string.IsNullOrEmpty(s))
                        list.Add(new TextContent(s));
                }
                else if (part.TryGetProperty("functionCall", out var fcEl))
                {
                    var name = fcEl.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                    Dictionary<string, object?>? args = null;
                    if (fcEl.TryGetProperty("args", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
                    {
                        args = new Dictionary<string, object?>();
                        foreach (var p in argsEl.EnumerateObject())
                            args[p.Name] = JsonElementToObject(p.Value);
                    }
                    if (!string.IsNullOrEmpty(name))
                    {
                        // Gemini'de call id yok — biz uretiyoruz (FunctionResponse ile eslestirme isimle yapilir)
                        var callId = Guid.NewGuid().ToString("N");
                        list.Add(new FunctionCallContent(callId, name, args));
                    }
                }
            }
        }
        return list;
    }

    /// <summary>
    /// MEAI'nin uretttigi JSON Schema'yi Gemini'nin OpenAPI/Proto subset'ine uyumlu hale getirir.
    ///   - "type": ["X", "null"]  →  "type": "X", "nullable": true
    ///   - additionalProperties, $schema, unevaluatedProperties → kaldirilir
    ///   - Bilinmeyen alan adlari ($defs, allOf vs.) → kaldirilir (Gemini reddeder)
    /// </summary>
    private static object SanitizeSchemaForGemini(JsonElement schema)
        => SanitizeSchemaInternal(schema) ?? new Dictionary<string, object?> { ["type"] = "object", ["properties"] = new Dictionary<string, object?>() };

    private static object? SanitizeSchemaInternal(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                {
                    var result = new Dictionary<string, object?>();
                    bool nullable = false;

                    foreach (var prop in el.EnumerateObject())
                    {
                        var name = prop.Name;
                        var val = prop.Value;

                        // Gemini'nin desteklemedigi alanlar — atla
                        if (name == "$schema" || name == "$id" || name == "$defs" ||
                            name == "$ref" || name == "additionalProperties" ||
                            name == "unevaluatedProperties" || name == "allOf" ||
                            name == "anyOf" || name == "oneOf" || name == "not" ||
                            name == "patternProperties" || name == "const")
                            continue;

                        if (name == "type")
                        {
                            // Array tip → tek tip + nullable
                            if (val.ValueKind == JsonValueKind.Array)
                            {
                                string? primary = null;
                                foreach (var t in val.EnumerateArray())
                                {
                                    if (t.ValueKind != JsonValueKind.String) continue;
                                    var s = t.GetString();
                                    if (s == "null") { nullable = true; continue; }
                                    if (primary == null) primary = s;
                                }
                                if (primary != null) result["type"] = primary;
                            }
                            else if (val.ValueKind == JsonValueKind.String)
                            {
                                var s = val.GetString();
                                if (s == "null") { nullable = true; result["type"] = "string"; }
                                else if (!string.IsNullOrEmpty(s)) result["type"] = s;
                            }
                            continue;
                        }

                        // Nested: properties, items, enum recursive sanitize
                        if (name == "properties" && val.ValueKind == JsonValueKind.Object)
                        {
                            var propsDict = new Dictionary<string, object?>();
                            foreach (var p in val.EnumerateObject())
                            {
                                var inner = SanitizeSchemaInternal(p.Value);
                                if (inner != null) propsDict[p.Name] = inner;
                            }
                            result["properties"] = propsDict;
                            continue;
                        }

                        if (name == "items")
                        {
                            var inner = SanitizeSchemaInternal(val);
                            if (inner != null) result["items"] = inner;
                            continue;
                        }

                        // Skalar alanlar (description, required, enum, format, default, minimum, maximum, title, ...)
                        result[name] = JsonElementToObject(val);
                    }

                    if (nullable) result["nullable"] = true;
                    return result;
                }
            case JsonValueKind.Array:
                {
                    var list = new List<object?>();
                    foreach (var item in el.EnumerateArray())
                    {
                        var v = JsonElementToObject(item);
                        list.Add(v);
                    }
                    return list;
                }
            default:
                return JsonElementToObject(el);
        }
    }

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
                var array = new List<object?>();
                foreach (var item in el.EnumerateArray()) array.Add(JsonElementToObject(item));
                return array;
            default: return null;
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "...");
}
