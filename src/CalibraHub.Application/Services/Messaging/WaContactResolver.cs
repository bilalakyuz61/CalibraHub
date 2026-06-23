using System.Net.Http.Json;
using System.Text.Json;
using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.WhatsApp;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Application.Services.Messaging;

/// <summary>
/// WhatsApp JID → WaContact çözümleyici.
/// Gelen her mesaj için çağrılır; WaContact yoksa oluşturur, varsa displayName günceller.
/// </summary>
public sealed class WaContactResolver : IWaContactResolver
{
    private readonly IWaContactRepository _repo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WaContactResolver> _logger;

    public WaContactResolver(
        IWaContactRepository repo,
        IHttpClientFactory httpClientFactory,
        ILogger<WaContactResolver> logger)
    {
        _repo              = repo;
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
    }

    public async Task<WaContact> GetOrCreateAsync(string jid, string? pushName, CancellationToken ct)
    {
        // Önce JID ile ara
        var existing = await _repo.FindByJidAsync(jid, ct);
        if (existing is not null)
        {
            // DisplayName güncelle (pushName değiştiyse)
            if (!string.IsNullOrWhiteSpace(pushName)
                && !string.Equals(existing.DisplayName, pushName, StringComparison.OrdinalIgnoreCase))
            {
                existing.DisplayName = pushName;
                existing.Updated     = DateTime.UtcNow;
                await _repo.UpdateAsync(existing, ct);
            }
            return existing;
        }

        var isLid  = WaPhoneNormalizer.IsLid(jid);
        var phone  = isLid ? null : WaPhoneNormalizer.Normalize(jid);
        var jidType = isLid ? "lid" : "phone";

        // Telefon JID'i ise phone'a göre de ara (farklı JID formatından daha önce eklenmiş olabilir)
        if (!isLid && phone is not null)
        {
            existing = await _repo.FindByPhoneAsync(phone, ct);
            if (existing is not null)
            {
                // Yeni JID alias'ını ekle
                await _repo.AddJidAsync(new WaContactJid
                {
                    ContactId = existing.Id,
                    Jid       = jid,
                    JidType   = jidType,
                    IsPrimary = false,
                    Created   = DateTime.UtcNow,
                }, ct);
                return existing;
            }
        }

        // Yeni WaContact oluştur
        var contact = new WaContact
        {
            PrimaryPhone  = phone,
            DisplayName   = pushName,
            IsActive      = true,
            Created       = DateTime.UtcNow,
        };
        var id = await _repo.CreateAsync(contact, ct);

        // JID kaydı ekle
        await _repo.AddJidAsync(new WaContactJid
        {
            ContactId = id,
            Jid       = jid,
            JidType   = jidType,
            IsPrimary = true,
            Created   = DateTime.UtcNow,
        }, ct);

        _logger.LogDebug("[WaContactResolver] Yeni contact: Id={id} JID={jid} IsLid={isLid}", id, jid, isLid);

        // WaContact.Id set et (CreateAsync sadece Id döndü, nesneye yansımaz)
        // Repository'den tekrar çek veya Id'yi set et
        var created = await _repo.FindByJidAsync(jid, ct) ?? contact;
        return created;
    }

    public async Task<string?> ResolveLidToPhoneJidAsync(string lidJid, string bridgeBaseUrl, CancellationToken ct)
    {
        try
        {
            using var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(3);
            var url = bridgeBaseUrl.TrimEnd('/') + "/lid-resolve?jid=" + Uri.EscapeDataString(lidJid);
            using var resp = await http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("phoneJid", out var pj) && pj.ValueKind == JsonValueKind.String)
                return pj.GetString();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("[WaContactResolver] LID resolve hatası ({lid}): {msg}", lidJid, ex.Message);
        }
        return null;
    }
}
