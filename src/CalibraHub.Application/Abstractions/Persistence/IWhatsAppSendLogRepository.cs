using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IWhatsAppSendLogRepository
{
    Task<long> InsertAsync(WhatsAppSendLog entry, CancellationToken cancellationToken);

    /// <summary>Belirli zaman penceresinde başarılı gönderim sayısı.</summary>
    Task<int> CountSuccessAsync(DateTime sinceUtc, CancellationToken cancellationToken);

    /// <summary>Belirli alıcıya bugünkü başarılı gönderim sayısı (UTC midnight'tan beri).</summary>
    Task<int> CountSuccessForRecipientTodayAsync(string toPhone, CancellationToken cancellationToken);

    /// <summary>Belirli message hash'inin bugünkü başarılı gönderim sayısı.</summary>
    Task<int> CountSuccessByHashTodayAsync(string messageHash, CancellationToken cancellationToken);

    /// <summary>Son N başarısızlığı al (consecutive failure tespiti için).</summary>
    Task<List<WhatsAppSendLog>> GetRecentLogsAsync(int count, CancellationToken cancellationToken);
}

public interface IWhatsAppSafetyRulesRepository
{
    Task<WhatsAppSafetyRules?> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(WhatsAppSafetyRules rules, CancellationToken cancellationToken);
}
