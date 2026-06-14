using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Varlık Yönetimi iş kuralları — CRUD, geçmiş (AssetEvent), durum geçişleri ve
/// Makine birleşimi (projection + lazy-materialize). İsim benzersizliği + kod auto-derive
/// (CLAUDE.md: kullanıcı kod girmez) burada uygulanır.
/// </summary>
public interface IAssetService
{
    // ── Asset CRUD ────────────────────────────────────────────────
    Task<IReadOnlyCollection<AssetDto>> GetAssetsAsync(CancellationToken ct);
    Task<AssetDto?> GetAssetByIdAsync(int id, CancellationToken ct);
    Task<int> CreateAssetAsync(CreateAssetRequest request, CancellationToken ct);
    Task UpdateAssetAsync(UpdateAssetRequest request, CancellationToken ct);
    Task DeleteAssetAsync(int id, CancellationToken ct);

    /// <summary>Edit ekranı dropdown verileri (lokasyon ağacı, departman, personel, makine).</summary>
    Task<AssetEditLookupsDto> GetEditLookupsAsync(CancellationToken ct);

    // ── Birleşik board (Asset + Machine projection) ───────────────
    Task<IReadOnlyCollection<AssetCardDto>> GetBoardCardsAsync(CancellationToken ct);
    /// <summary>
    /// Bir makineye ait Asset kaydını döner; yoksa lazy-materialize eder (Kind=Machine, MachineId dolu).
    /// Edit ekranı bir makine kartından açıldığında kullanılır.
    /// </summary>
    Task<AssetDto> GetOrMaterializeByMachineIdAsync(int machineId, int? userId, CancellationToken ct);

    // ── Geçmiş (AssetEvent) ───────────────────────────────────────
    Task<IReadOnlyCollection<AssetEventDto>> GetEventsAsync(int assetId, CancellationToken ct);
    /// <summary>Olay ekler; bakım/kalibrasyon olaylarında varlığın Last*/Next* tarihlerini günceller.</summary>
    Task<int> AddEventAsync(CreateAssetEventRequest request, CancellationToken ct);
    Task DeleteEventAsync(int id, CancellationToken ct);

    // ── Zimmet (operasyonel hareket) ──────────────────────────────
    /// <summary>
    /// Varlığı bir personele VEYA departmana zimmetler (en az biri zorunlu). Aktif zimmet varsa önce kapatılır (assignDate ile iade).
    /// Yeni AssetAssignment kaydı açılır (personel/departman/lokasyon), Asset denormalize alanları güncellenir. Yeni zimmet Id döner (belge için).
    /// </summary>
    Task<int> AssignAsync(int assetId, int? personnelId, int? departmentId, int? locationId, DateTime assignDate, string? note, string? documentNo, int? userId, CancellationToken ct);
    /// <summary>Aktif zimmeti iade alır (ReturnDate set). Asset.AssignedPersonnelId temizlenir.</summary>
    Task ReturnAsync(int assetId, DateTime returnDate, string? note, int? userId, CancellationToken ct);
    /// <summary>Varlığın tüm zimmet hareketleri (geçmiş + aktif), tarihe göre azalan.</summary>
    Task<IReadOnlyCollection<AssetAssignmentDto>> GetAssignmentsAsync(int assetId, CancellationToken ct);
    /// <summary>Aktif (iade edilmemiş) zimmet; yoksa null.</summary>
    Task<AssetAssignmentDto?> GetCurrentAssignmentAsync(int assetId, CancellationToken ct);
    /// <summary>Tekil zimmet kaydı (belge basımı için).</summary>
    Task<AssetAssignmentDto?> GetAssignmentByIdAsync(int assignmentId, CancellationToken ct);
    /// <summary>Sade zimmet takip raporu — tüm varlıklar: kime zimmetli, ne zaman verildi/iade alındı.</summary>
    Task<IReadOnlyCollection<AssignmentReportRowDto>> GetAssignmentReportAsync(CancellationToken ct);
}
