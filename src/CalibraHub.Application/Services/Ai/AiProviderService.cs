using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;
using Microsoft.Extensions.AI;

namespace CalibraHub.Application.Services.Ai;

/// <summary>
/// 2026-05-23 — AiProvider CRUD service (Şirket Ayarları "Yapay Zeka" sekmesi).
/// </summary>
public sealed class AiProviderService : IAiProviderService
{
    private readonly IAiProviderRepository _repo;
    private readonly IAiClientFactory _clientFactory;

    public AiProviderService(IAiProviderRepository repo, IAiClientFactory clientFactory)
    {
        _repo = repo;
        _clientFactory = clientFactory;
    }

    public async Task<IReadOnlyList<AiProviderDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var list = await _repo.ListAsync(includeInactive, ct).ConfigureAwait(false);
        return list.Select(ToDto).ToList();
    }

    public async Task<AiProviderDto?> GetAsync(int id, CancellationToken ct)
    {
        var p = await _repo.GetByIdAsync(id, ct).ConfigureAwait(false);
        return p is null ? null : ToDto(p);
    }

    public async Task<int> SaveAsync(SaveAiProviderRequest req, int? currentUserId, CancellationToken ct)
    {
        var entity = new AiProvider
        {
            Id           = req.Id,
            Code         = (req.Code ?? string.Empty).Trim().ToLowerInvariant(),
            Label        = (req.Label ?? string.Empty).Trim(),
            EndpointUrl  = string.IsNullOrWhiteSpace(req.EndpointUrl)  ? null : req.EndpointUrl.Trim(),
            DefaultModel = string.IsNullOrWhiteSpace(req.DefaultModel) ? null : req.DefaultModel.Trim(),
            ExtraJson    = string.IsNullOrWhiteSpace(req.ExtraJson)    ? null : req.ExtraJson.Trim(),
            IsActive     = req.IsActive,
            IsDefault    = req.IsDefault,
            SortOrder    = req.SortOrder,
        };
        if (req.Id <= 0)
            entity.CreatedById = currentUserId;
        else
            entity.UpdatedById = currentUserId;

        return await _repo.SaveAsync(entity, req.ApiKey, ct).ConfigureAwait(false);
    }

    public Task DeleteAsync(int id, CancellationToken ct) => _repo.DeleteAsync(id, ct);

    public async Task<(bool Ok, string? Error, string? Sample)> TestConnectionAsync(int providerId, CancellationToken ct)
    {
        var p = await _repo.GetByIdAsync(providerId, ct).ConfigureAwait(false);
        if (p is null) return (false, "Provider bulunamadı.", null);
        if (!p.IsActive) return (false, "Provider pasif.", null);

        IChatClient? client;
        try
        {
            client = await _clientFactory.CreateAsync(p.Code, userId: null, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (false, "Adapter oluşturulamadı: " + ex.Message, null);
        }
        if (client is null) return (false, "API key girilmemiş.", null);

        try
        {
            // Minimal token mesaj — "ping" yanıtı 2-3 token sürer
            var messages = new[] { new ChatMessage(ChatRole.User, "ping") };
            var opts = new ChatOptions { MaxOutputTokens = 5 };
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            var resp = await client.GetResponseAsync(messages, opts, cts.Token).ConfigureAwait(false);
            var sample = resp.Messages.FirstOrDefault()?.Text ?? string.Empty;
            return (true, null, sample);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
        finally
        {
            client?.Dispose();
        }
    }

    private static AiProviderDto ToDto(AiProvider p) => new(
        Id:           p.Id,
        Code:         p.Code,
        Label:        p.Label,
        HasApiKey:    !string.IsNullOrWhiteSpace(p.ApiKeyEncrypted),
        EndpointUrl:  p.EndpointUrl,
        DefaultModel: p.DefaultModel,
        ExtraJson:    p.ExtraJson,
        IsActive:     p.IsActive,
        IsDefault:    p.IsDefault,
        SortOrder:    p.SortOrder,
        Created:      p.Created,
        Updated:      p.Updated);
}
