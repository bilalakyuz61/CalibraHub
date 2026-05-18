using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Persistence;

/// <summary>
/// Form (ekran) katalogu CRUD persistence arayüzü.
/// dbo.Forms tablosuna erişimi soyutlar.
/// </summary>
public interface IFormRepository
{
    /// <summary>Tüm formları döner (aktif ve pasif).</summary>
    Task<IReadOnlyCollection<FormDto>> GetAllAsync(CancellationToken ct);

    /// <summary>Tek form — bulunamazsa null döner.</summary>
    Task<FormDto?> GetByIdAsync(int id, CancellationToken ct);

    /// <summary>
    /// FormCode ile form arar (case-insensitive). Bulunamazsa null doner.
    /// MappingEngine'in SqlFunction @P1 (FormId) lookup'u icin kullanir.
    /// </summary>
    Task<FormDto?> GetByCodeAsync(string formCode, CancellationToken ct);

    /// <summary>Yeni form ekler, yeni kaydın Id'sini döner.</summary>
    Task<int> CreateAsync(CreateFormRequest request, CancellationToken ct);

    /// <summary>Mevcut formu günceller.</summary>
    Task UpdateAsync(UpdateFormRequest request, CancellationToken ct);

    /// <summary>Formu fiziksel olarak siler.</summary>
    Task DeleteAsync(int id, CancellationToken ct);
}
