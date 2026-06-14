using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// WhatsApp JID'ini WaContact master kaydına çözen ve gerekirse oluşturan servis.
/// Polling pipeline'ın giriş noktasıdır — gelen her mesaj için çağrılır.
/// </summary>
public interface IWaContactResolver
{
    /// <summary>
    /// JID (phone veya LID) için WaContact kaydı döner.
    /// Kayıt yoksa oluşturur; pushName varsa günceller.
    /// </summary>
    Task<WaContact> GetOrCreateAsync(string jid, string? pushName, CancellationToken ct);

    /// <summary>
    /// LID JID'i Bridge'e sorarak telefon JID'ine çözmeyi dener.
    /// Bridge'te mapping yoksa null döner.
    /// </summary>
    Task<string?> ResolveLidToPhoneJidAsync(string lidJid, string bridgeBaseUrl, CancellationToken ct);
}
