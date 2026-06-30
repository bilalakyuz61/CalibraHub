using CalibraHub.Application.Contracts;
using CalibraHub.Domain.Entities;

namespace CalibraHub.Application.Abstractions.Persistence;

public interface IPersonnelRepository
{
    /// <summary>Şirket personnel listesi (filtre: pasifler, sadece operatörler).</summary>
    Task<IReadOnlyCollection<PersonnelDto>> ListAsync(bool includeInactive, bool onlyOperators, CancellationToken ct);

    Task<PersonnelDto?> GetAsync(int id, CancellationToken ct);

    Task<int> SaveAsync(Personnel entity, CancellationToken ct);

    Task DeleteAsync(int id, CancellationToken ct);

    /// <summary>
    /// Shop-floor giriş — PIN veya NFC kart numarası ile aktif üretim operatörünü bulur.
    /// İkisinden biri verilmelidir; ikisi de boşsa NULL döner. Sadece IsActive + IsProductionOperator true olanlar.
    /// </summary>
    Task<PersonnelDto?> GetByPinOrCardAsync(string? pinCode, string? cardNo, CancellationToken ct);

    /// <summary>
    /// 2026-05-22: Sicil + PIN ikilisi ile auth — daha güvenli (brute-force azaltır).
    /// personnelCode set ise PIN kontrolü Code'a göre filtrelenir. Card yolu Code'suz da çalışır
    /// (NFC kart fiziksel sahiplik kanıtı).
    /// </summary>
    Task<PersonnelDto?> GetByPinOrCardAsync(string? personnelCode, string? pinCode, string? cardNo, CancellationToken ct);

    /// <summary>
    /// ShopFloor lockout için: aktif veya pasif farketmez, üretim operatörü sicilinin Id + IsActive bilgisini döner.
    /// Null = bu Code'da operatör yok.
    /// </summary>
    Task<(int Id, bool IsActive)?> GetIdAndActiveByCodeAsync(string code, CancellationToken ct);

    /// <summary>ShopFloor lockout için: sicili pasife alır (IsActive = 0). Audit alanları dokunulmaz.</summary>
    Task DeactivateAsync(int id, CancellationToken ct);

    /// <summary>Sistem kullanıcısına bağlı personel kartını döner (UserId eşleşmesi).</summary>
    Task<PersonnelDto?> GetByUserIdAsync(int userId, CancellationToken ct);
}
