namespace CalibraHub.Application.Contracts;

/// <summary>
/// ApprovalSqlQuery kütüphane kaydı — admin UI listeleme + designer dropdown'unda
/// kullanılır. ParametersJson schema:
/// <code>[{"name":"documentId","type":"int","description":"Belge Id"}]</code>
/// </summary>
public sealed record ApprovalSqlQueryDto(
    int Id,
    string Name,
    string? Description,
    string SqlText,
    string? ParametersJson,
    string ResultType,
    bool IsActive,
    int? CreatedById,
    DateTime Created,
    int? UpdatedById,
    DateTime? Updated);

/// <summary>
/// Named query upsert isteği — Admin/SqlQueryLibrary/Save endpoint'i.
/// Id=0 yeni kayıt, Id>0 güncelleme.
/// </summary>
public sealed record SaveApprovalSqlQueryRequest(
    int Id,
    string Name,
    string? Description,
    string SqlText,
    string? ParametersJson,
    string ResultType,
    bool IsActive);

/// <summary>
/// SQL çalıştırma isteği — designer "Test" butonu veya runtime karar değerlendirmesi.
/// QueryId verilirse kütüphaneden alınan SqlText kullanılır; aksi halde SqlText
/// (ad-hoc, admin only) doğrudan çalıştırılır. Parameters bağlama değerleridir.
/// </summary>
public sealed record ExecuteApprovalSqlRequest(
    string? SqlText,
    int? QueryId,
    IReadOnlyDictionary<string, object?>? Parameters);

/// <summary>
/// SQL çalıştırma sonucu. Ok=false ise Error doludur; Ok=true ise Value scalar
/// dönüş (object?: int, decimal, string, bool, vb.). ElapsedMs ms cinsinden süre.
/// </summary>
public sealed record ExecuteApprovalSqlResult(
    bool Ok,
    object? Value,
    string? Error,
    long ElapsedMs);
