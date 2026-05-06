using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IWhatsAppConfigRepository
{
    Task<WhatsAppConfig?> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(WhatsAppConfig config, CancellationToken cancellationToken);
}
