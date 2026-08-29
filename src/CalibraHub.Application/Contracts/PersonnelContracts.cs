namespace CalibraHub.Application.Contracts;

public sealed record PersonnelDto(
    int Id,
    int CompanyId,
    string Code,
    string FullName,
    string? Title,
    string? Department,
    string? PinCode,
    string? CardNo,
    bool IsProductionOperator,
    bool IsActive,
    int? UserId,
    string? UserFullName,
    string? Phone,
    string? Email,
    string? Notes,
    DateTime? BirthDate,
    DateTime Created,
    DateTime? Updated,
    int? LocationId = null,
    string? LocationName = null,
    /// <summary>Mobilde operasyon oncesi PIN sorulsun mu? Varsayilan true (mevcut davranis).</summary>
    bool IsMobilePinRequired = true,
    /// <summary>
    /// Calisabildigi istasyonlar (makine parki lokasyon Id'leri). BOS liste = atama yok
    /// (= kisitlanmamis), "hicbir istasyonda calisamaz" DEGIL.
    /// </summary>
    IReadOnlyList<int>? StationIds = null,
    /// <summary>Istasyon adlari — yalniz gosterim (liste/kart). Karsilastirmada KULLANILMAZ.</summary>
    string? StationNames = null);

public sealed record SavePersonnelRequest(
    int Id,
    string Code,
    string FullName,
    string? Title,
    string? Department,
    string? PinCode,
    string? CardNo,
    bool IsProductionOperator,
    bool IsActive,
    int? UserId,
    string? Phone,
    string? Email,
    string? Notes,
    DateTime? BirthDate,
    int? LocationId = null,
    bool IsMobilePinRequired = true,
    /// <summary>
    /// Calisabildigi istasyonlar. NULL = "dokunma" (mevcut atama korunur); bos liste =
    /// "tumunu kaldir". Bu ayrim onemli: eski istemciler bu alani hic gondermez, onlarin
    /// kaydetmesi mevcut atamalari silmemeli.
    /// </summary>
    IReadOnlyList<int>? StationIds = null);

/// <summary>
/// Atanabilir istasyon seçeneği. İstasyon ayrı bir tablo DEĞİL — makine parkı işaretli
/// bir <c>Location</c>'dır (makineler zaten oraya bağlı). Bu yüzden ayrı bir "WorkCenter"
/// kavramı açılmadı: makinenin iki farklı ağaca bağlı olması ilk çelişkide patlar.
/// </summary>
public sealed record StationOptionDto(
    int Id,
    string Code,
    string Name,
    string? ParentName,
    int MachineCount);
