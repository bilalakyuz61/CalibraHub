using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Application.Services.Ai.Tools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CalibraHub.Web.Controllers;

/// <summary>
/// 2026-05-23 — Yapay zeka runtime endpoint'leri (floating widget + diğer use case'ler).
///   POST /Ai/Chat      → SSE stream (text/event-stream)
///   GET  /Ai/Providers → kullanıcıya açık provider listesi (floating widget dropdown'ı için)
///
/// Faz 1.B (Summarize) + Faz 1.C (NlSql) bu controller'a eklenecek (ileride).
/// </summary>
[Authorize]
[CalibraHub.Web.Authorization.PermissionScope(CalibraHub.Application.Constants.FormCodes.CompanySettings)]
public sealed class AiController : Controller
{
    private readonly IAiChatService _chat;
    private readonly IAiClientFactory _factory;
    private readonly AiPendingActionStore _pendingStore;
    private readonly CalibroContactTools _contactTools;
    private readonly CalibroItemTools _itemTools;
    private readonly CalibroDocumentTools _documentTools;
    private readonly IAiToolInvocationRepository _auditLog;

    public AiController(
        IAiChatService chat,
        IAiClientFactory factory,
        AiPendingActionStore pendingStore,
        CalibroContactTools contactTools,
        CalibroItemTools itemTools,
        CalibroDocumentTools documentTools,
        IAiToolInvocationRepository auditLog)
    {
        _chat = chat;
        _factory = factory;
        _pendingStore = pendingStore;
        _contactTools = contactTools;
        _itemTools = itemTools;
        _documentTools = documentTools;
        _auditLog = auditLog;
    }

    [HttpGet("/Ai/Providers")]
    public async Task<IActionResult> Providers(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var list = await _factory.ListAvailableAsync(userId <= 0 ? null : userId, ct);
        return Json(new { ok = true, providers = list });
    }

    /// <summary>
    /// SSE stream — Content-Type: text/event-stream. Her parça `data: <chunk>\n\n` formatında.
    /// Stream sonunda `data: [DONE]\n\n`.
    /// Frontend (AiChatPanel.jsx) ReadableStream + EventSource yerine fetch+TextDecoder ile okur
    /// (POST body destekli olsun diye).
    /// </summary>
    [HttpPost("/Ai/Chat")]
    [ValidateAntiForgeryToken]
    public async Task Chat([FromBody] ChatRequest req, CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");          // nginx buffering kapalı
        await Response.Body.FlushAsync(ct).ConfigureAwait(false);

        if (req is null || req.Messages is null || req.Messages.Count == 0)
        {
            await WriteSseAsync("(Boş mesaj.)", ct);
            await WriteSseAsync("[DONE]", ct);
            return;
        }

        var userId = GetCurrentUserId();
        try
        {
            await foreach (var chunk in _chat.AskStreamAsync(req, userId <= 0 ? null : userId, ct))
            {
                await WriteSseAsync(chunk, ct);
            }
        }
        catch (OperationCanceledException) { /* client kapadı */ }
        catch (Exception ex)
        {
            await WriteSseAsync($"(AI hatası: {ex.Message})", ct);
        }
        finally
        {
            await WriteSseAsync("[DONE]", ct);
        }
    }

    private async Task WriteSseAsync(string data, CancellationToken ct)
    {
        // Newline'lar SSE'de payload'u bozar — escape et
        var safe = data.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");
        var line = "data: " + safe + "\n\n";
        var bytes = Encoding.UTF8.GetBytes(line);
        await Response.Body.WriteAsync(bytes, ct).ConfigureAwait(false);
        await Response.Body.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 2026-05-24 — Calibo write tool'larinin onayli execution endpoint'i.
    /// Frontend "Onayla" butonuna basinca tokenle bu endpoint'i cagirir.
    /// Endpoint: cache'ten intent pop'lar (tek kullanim, owner check), gercek tool method'unu
    /// confirm=true ile cagirir, sonucu doner.
    /// </summary>
    public sealed record ConfirmActionRequest(string Token);

    [HttpPost("/Ai/ConfirmAction")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmAction([FromBody] ConfirmActionRequest req, CancellationToken ct)
    {
        if (req is null || string.IsNullOrWhiteSpace(req.Token))
            return Json(new { ok = false, error = "Token zorunlu." });

        var userId = GetCurrentUserId();
        if (userId <= 0)
            return Json(new { ok = false, error = "Oturum yok." });

        var pending = _pendingStore.TryConsume(req.Token, userId);
        if (pending is null)
            return Json(new { ok = false, error = "Onay süresi dolmuş veya token geçersiz. Lütfen tekrar Calibo'ya talimat verin." });

        object? result = null;
        string status = "executed";
        string? errorMsg = null;
        try
        {
            result = pending.ToolName switch
            {
                "create_contact"    => await _contactTools.ExecuteCreateContactAsync(pending.Args, ct),
                "update_contact"    => await _contactTools.ExecuteUpdateContactAsync(pending.Args, ct),
                "create_item"       => await _itemTools.ExecuteCreateItemAsync(pending.Args, ct),
                "update_item"       => await _itemTools.ExecuteUpdateItemAsync(pending.Args, ct),
                "set_doc_status"    => await _documentTools.ExecuteSetDocumentStatusAsync(pending.Args, ct),
                "add_doc_line"      => await _documentTools.ExecuteAddDocumentLineAsync(pending.Args, ct),
                "create_quote_draft"=> await _documentTools.ExecuteCreateQuoteDraftAsync(pending.Args, ct),
                "create_purchase_request" => await _documentTools.ExecuteCreatePurchaseRequestAsync(pending.Args, ct),
                _ => new { success = false, error = $"Bilinmeyen tool: {pending.ToolName}" },
            };
        }
        catch (Exception ex)
        {
            status = "error";
            errorMsg = ex.Message;
            result = new { success = false, error = "Calistirma hatasi: " + ex.Message };
        }

        // 2026-05-24: Audit log — her execution DB'ye yazilir (basari ya da hata).
        try
        {
            await _auditLog.LogExecutedAsync(new AiToolInvocationLogEntry(
                UserId: userId,
                ToolName: pending.ToolName,
                ActionLabel: pending.ActionLabel,
                ArgumentsJson: SafeSerialize(pending.Args),
                Status: status,
                ResultSummary: ExtractMessage(result),
                AffectedEntity: ExtractEntity(pending.ToolName, result),
                ErrorMessage: errorMsg), ct);
        }
        catch { /* audit log basarisizligi user islemini bloklamaz */ }

        return Json(new { ok = status == "executed", result, error = errorMsg });
    }

    private static string? SafeSerialize(IReadOnlyDictionary<string, object?> args)
    {
        try { return JsonSerializer.Serialize(args); }
        catch { return null; }
    }

    private static string? ExtractMessage(object? result)
    {
        if (result is null) return null;
        var msgProp = result.GetType().GetProperty("message");
        return msgProp?.GetValue(result)?.ToString();
    }

    private static string? ExtractEntity(string toolName, object? result)
    {
        if (result is null) return null;
        var idProp = result.GetType().GetProperty("id");
        var id = idProp?.GetValue(result);
        if (id == null) return null;
        var kind = toolName switch
        {
            "create_contact" or "update_contact" => "Contact",
            "create_item" or "update_item" => "Item",
            "set_doc_status" or "add_doc_line" or "create_quote_draft" or "create_purchase_request" => "Document",
            _ => toolName,
        };
        return $"{kind}:{id}";
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var id) ? id : 0;
    }
}
