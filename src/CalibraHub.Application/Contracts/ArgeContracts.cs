namespace CalibraHub.Application.Contracts;

/// <summary>
/// AR-GE/ÜR-GE proje kaydetme isteği. Id=0 yeni proje, Id>0 mevcut projenin DocumentId'si.
/// ProjectType: 0=AR-GE, 1=ÜR-GE. Statu burada DEGISMEZ (yasam dongusu ChangeStatus'ten).
/// </summary>
public sealed record SaveArgeProjectRequest(
    int Id,
    string Name,
    byte ProjectType,
    int? OwnerPersonnelId,
    DateTime? TargetDate,
    decimal ProgressPercent,
    string? Description);

/// <summary>Proje liste/kart satiri (Document + ArgeProject + Personnel join + prototip sayisi).</summary>
public sealed record ArgeProjectListItem(
    int DocumentId,
    string DocumentNumber,
    string Name,
    byte ProjectType,
    byte Status,
    int? OwnerPersonnelId,
    string? OwnerName,
    DateTime? TargetDate,
    decimal ProgressPercent,
    string? Description,
    int PrototypeCount,
    DateTime Created);

/// <summary>Proje detay (edit ekrani hydration).</summary>
public sealed record ArgeProjectDetail(
    int DocumentId,
    string DocumentNumber,
    string Name,
    byte ProjectType,
    byte Status,
    int? OwnerPersonnelId,
    DateTime? TargetDate,
    decimal ProgressPercent,
    string? Description);

/// <summary>Sorumlu personel dropdown secenegi (edit ekrani).</summary>
public sealed record ArgePersonnelOption(int Id, string Name);

/// <summary>
/// Proje prototip satiri (ArgePrototype ⨝ Items + BOM/Routing varlik bayraklari).
/// ItemId null → henuz stok karti yok (recete/rota tanimlamak icin once Item turetilir).
/// HasBom/HasRouting → prototip Item'inda recete/rota tanimli mi (deep-link durumu).
/// </summary>
public sealed record ArgePrototypeDto(
    int Id,
    int ProjectId,
    string Name,
    string? Description,
    string? VersionLabel,
    int? ItemId,
    string? ItemCode,
    string? ItemName,
    bool IsApproved,
    bool HasBom,
    bool HasRouting,
    DateTime Created);

/// <summary>Prototip kaydetme istegi. Id=0 yeni, Id>0 mevcut. ProjectId = AR-GE Document.id.</summary>
public sealed record SavePrototypeRequest(
    int Id,
    int ProjectId,
    string Name,
    string? VersionLabel,
    string? Description);

/// <summary>
/// Projeye bagli is emirlerinin (WorkOrder.ArgeProjectId) isçilik maliyeti rollup'i (Faz 3).
/// LaborHours = Σ(gerçekleşen ?? planlanan süre, saate çevrili); LaborCost = Σ(saat × Operation.HourlyRate).
/// </summary>
public sealed record ArgeProjectLaborDto(
    decimal LaborCost,
    decimal LaborHours,
    int WorkOrderCount,
    int OperationCount);
