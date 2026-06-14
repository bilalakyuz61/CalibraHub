using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CalibraHub.Application.Services.Ai.Providers;

/// <summary>
/// 2026-05-23 — Anthropic Messages API adapter (https://docs.anthropic.com/en/api/messages).
/// 2026-05-24 — Tool calling desteklendi (Claude'da Calibo write tool'lari calisir).
///
/// **Mesaj formati (Anthropic):**
///   System: ayri "system" alani (en ust seviye)
///   User/Assistant: role + content (array of typed blocks)
///   Block tipleri: "text", "image", "tool_use", "tool_result"
///
/// **Tool akisi:**
///   - Request: body.tools = [{ name, description, input_schema }]
///   - Response: content blocks icinde { type: "tool_use", id, name, input }
///   - Yanit donerken: user role'unde { type: "tool_result", tool_use_id, content }
///
/// **Streaming:** SSE event-based.
///   - content_block_start    → yeni block (text veya tool_use)
///   - content_block_delta    → metin parcasi veya input_json_delta (tool args)
///   - content_block_stop     → block tamamlandi
///   - message_stop           → bitti
/// </summary>
public sealed class AnthropicChatAdapter : IChatClient
{
    private const string DefaultEndpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const string DefaultModel = "claude-3-5-sonnet-20241022";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly bool _ownsHttp;

    public AnthropicChatAdapter(string apiKey, string? defaultModel, HttpClient? http = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Anthropic API key boş.", nameof(apiKey));
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(defaultModel) ? DefaultModel : defaultModel;
        if (http is null) { _http = new HttpClient(); _ownsHttp = true; }
        else { _http = http; _ownsHttp = false; }

        Metadata = new ChatClientMetadata("anthropic", new Uri("https://api.anthropic.com"), _model);
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
            throw new InvalidOperationException($"Anthropic HTTP {(int)resp.StatusCode}: {Truncate(json, 400)}");

        using var doc = JsonDocument.Parse(json);
        var contents = ExtractContents(doc.RootElement);
        var modelUsed = doc.RootElement.TryGetProperty("model", out var mEl) ? mEl.GetString() : _model;

        var msg = contents.Count > 0
            ? new ChatMessage(ChatRole.Assistant, contents)
            : new ChatMessage(ChatRole.Assistant, string.Empty);

        return new ChatResponse(msg)
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
            throw new InvalidOperationException($"Anthropic HTTP {(int)resp.StatusCode}: {Truncate(err, 400)}");
        }

        using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // Per-block state — tool_use'lar icin partial JSON birikir
        var toolBlocks = new Dictionary<int, (string Id, string Name, StringBuilder JsonAcc)>();

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var payload = line[5..].Trim();
            if (payload == "[DONE]") yield break;

            JsonDocument? doc = null;
            string? deltaText = null;
            FunctionCallContent? completedTool = null;

            try
            {
                doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl)) continue;
                var evType = typeEl.GetString();

                switch (evType)
                {
                    case "content_block_start":
                    {
                        var index = root.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;
                        if (root.TryGetProperty("content_block", out var cb)
                            && cb.TryGetProperty("type", out var cbType)
                            && cbType.GetString() == "tool_use")
                        {
                            var id = cb.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                            var name = cb.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                            toolBlocks[index] = (id, name, new StringBuilder());
                        }
                        break;
                    }
                    case "content_block_delta":
                    {
                        var index = root.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;
                        if (root.TryGetProperty("delta", out var delta)
                            && delta.TryGetProperty("type", out var dtEl))
                        {
                            var dt = dtEl.GetString();
                            if (dt == "text_delta"
                                && delta.TryGetProperty("text", out var txtEl))
                            {
                                deltaText = txtEl.GetString();
                            }
                            else if (dt == "input_json_delta"
                                && delta.TryGetProperty("partial_json", out var pjEl)
                                && toolBlocks.TryGetValue(index, out var blk))
                            {
                                blk.JsonAcc.Append(pjEl.GetString() ?? "");
                                toolBlocks[index] = blk;
                            }
                        }
                        break;
                    }
                    case "content_block_stop":
                    {
                        var index = root.TryGetProperty("index", out var idxEl) ? idxEl.GetInt32() : 0;
                        if (toolBlocks.TryGetValue(index, out var blk))
                        {
                            // Tool_use blogu kapandi — argumanlari parse et, FunctionCallContent uret
                            Dictionary<string, object?>? args = null;
                            var jsonStr = blk.JsonAcc.ToString();
                            if (!string.IsNullOrWhiteSpace(jsonStr))
                            {
                                try
                                {
                                    using var argDoc = JsonDocument.Parse(jsonStr);
                                    if (argDoc.RootElement.ValueKind == JsonValueKind.Object)
                                    {
                                        args = new Dictionary<string, object?>();
                                        foreach (var p in argDoc.RootElement.EnumerateObject())
                                            args[p.Name] = JsonElementToObject(p.Value);
                                    }
                                }
                                catch (JsonException) { /* incomplete json */ }
                            }
                            completedTool = new FunctionCallContent(blk.Id, blk.Name, args);
                            toolBlocks.Remove(index);
                        }
                        break;
                    }
                    case "message_stop":
                        // Bitis — son update'i emit edip cikalim
                        break;
                }
            }
            catch (JsonException) { /* malformed payload — skip */ }
            finally { doc?.Dispose(); }

            if (!string.IsNullOrEmpty(deltaText) || completedTool != null)
            {
                var update = new ChatResponseUpdate { Role = ChatRole.Assistant };
                if (!string.IsNullOrEmpty(deltaText))
                    update.Contents.Add(new TextContent(deltaText));
                if (completedTool != null)
                    update.Contents.Add(completedTool);
                yield return update;
            }
        }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private HttpRequestMessage BuildHttpRequest(object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, DefaultEndpoint);
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);
        req.Content = JsonContent.Create(body);
        return req;
    }

    private object BuildBody(IEnumerable<ChatMessage> messages, ChatOptions? options, bool stream)
    {
        var model = options?.ModelId ?? _model;
        var maxTokens = options?.MaxOutputTokens ?? 2048;
        var temperature = options?.Temperature;
        var tools = options?.Tools;

        // System prompt'larini birlestir (Anthropic ayri "system" alani ister)
        var systemBuf = new StringBuilder();
        var anthropicMsgs = new List<Dictionary<string, object?>>();

        foreach (var m in messages)
        {
            if (m.Role == ChatRole.System)
            {
                var txt = m.Text ?? string.Empty;
                if (systemBuf.Length > 0) systemBuf.Append('\n');
                systemBuf.Append(txt);
                continue;
            }

            // role belirleme — tool sonucu da user role'unde gonderilir (Anthropic kurali)
            string role;
            if (m.Role == ChatRole.User) role = "user";
            else if (m.Role == ChatRole.Tool) role = "user";
            else role = "assistant";

            // Content blocks — text, image, tool_use, tool_result
            var contentBlocks = new List<object>();
            if (m.Contents != null && m.Contents.Count > 0)
            {
                foreach (var c in m.Contents)
                {
                    if (c is TextContent tc && !string.IsNullOrEmpty(tc.Text))
                    {
                        contentBlocks.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "text",
                            ["text"] = tc.Text,
                        });
                    }
                    else if (c is DataContent dc && (dc.MediaType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        contentBlocks.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "image",
                            ["source"] = new Dictionary<string, object?>
                            {
                                ["type"] = "base64",
                                ["media_type"] = dc.MediaType,
                                ["data"] = Convert.ToBase64String(dc.Data.ToArray()),
                            }
                        });
                    }
                    else if (c is FunctionCallContent fcc)
                    {
                        contentBlocks.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "tool_use",
                            ["id"] = fcc.CallId,
                            ["name"] = fcc.Name,
                            ["input"] = fcc.Arguments != null
                                ? fcc.Arguments.ToDictionary(kv => kv.Key, kv => kv.Value)
                                : new Dictionary<string, object?>(),
                        });
                    }
                    else if (c is FunctionResultContent frc)
                    {
                        var resultStr = frc.Result switch
                        {
                            null => "",
                            string s => s,
                            _ => JsonSerializer.Serialize(frc.Result),
                        };
                        contentBlocks.Add(new Dictionary<string, object?>
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = frc.CallId,
                            ["content"] = resultStr,
                        });
                    }
                }
            }
            else if (!string.IsNullOrEmpty(m.Text))
            {
                contentBlocks.Add(new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = m.Text,
                });
            }

            if (contentBlocks.Count == 0) continue;

            anthropicMsgs.Add(new Dictionary<string, object?>
            {
                ["role"] = role,
                ["content"] = contentBlocks,
            });
        }

        var body = new Dictionary<string, object?>
        {
            ["model"]      = model,
            ["max_tokens"] = maxTokens,
            ["messages"]   = anthropicMsgs,
            ["stream"]     = stream,
        };
        if (systemBuf.Length > 0) body["system"] = systemBuf.ToString();
        if (temperature.HasValue) body["temperature"] = temperature.Value;

        // 2026-05-24: Tools — Anthropic format: { name, description, input_schema }
        if (tools != null && tools.Count > 0)
        {
            var toolsList = new List<object>();
            foreach (var t in tools)
            {
                if (t is AIFunction af)
                {
                    toolsList.Add(new Dictionary<string, object?>
                    {
                        ["name"] = af.Name,
                        ["description"] = af.Description ?? string.Empty,
                        ["input_schema"] = af.JsonSchema,
                    });
                }
            }
            if (toolsList.Count > 0) body["tools"] = toolsList;
        }

        return body;
    }

    /// <summary>Non-streaming response — content array'ini AIContent listesine cevirir.</summary>
    private static List<AIContent> ExtractContents(JsonElement root)
    {
        var list = new List<AIContent>();
        if (!root.TryGetProperty("content", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var block in arr.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var typeEl)) continue;
            var t = typeEl.GetString();
            if (t == "text" && block.TryGetProperty("text", out var txtEl))
            {
                var s = txtEl.GetString();
                if (!string.IsNullOrEmpty(s))
                    list.Add(new TextContent(s));
            }
            else if (t == "tool_use")
            {
                var id = block.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var name = block.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                Dictionary<string, object?>? args = null;
                if (block.TryGetProperty("input", out var inEl) && inEl.ValueKind == JsonValueKind.Object)
                {
                    args = new Dictionary<string, object?>();
                    foreach (var p in inEl.EnumerateObject())
                        args[p.Name] = JsonElementToObject(p.Value);
                }
                list.Add(new FunctionCallContent(id, name, args));
            }
        }
        return list;
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
