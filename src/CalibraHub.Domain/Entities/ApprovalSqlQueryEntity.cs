namespace CalibraHub.Domain.Entities;

/// <summary>
/// ApprovalSqlQuery — Onay akışı Karar (Decision) node'larında kullanılan
/// SQL tabanlı koşullar için "Named Query" kütüphane kaydı.
///
/// Designer'da kullanıcı bu kütüphaneden seçim yapabilir veya admin yetkisiyle
/// ad-hoc SQL girer. Her iki yol da çalıştırılmadan önce ApprovalSqlQueryService
/// içindeki güvenlik validation'undan geçer (ScriptDom + whitelist + read-only).
///
/// Properties init-only — admin UI'dan upsert request DTO ile gelir.
/// </summary>
public sealed class ApprovalSqlQueryEntity
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string SqlText { get; init; }
    /// <summary>
    /// JSON: `[{"name":"documentId","type":"int","description":"..."}, ...]`
    /// Designer parametre dropdown'ı + sample değer prompt için kullanır.
    /// </summary>
    public string? ParametersJson { get; init; }
    /// <summary>'scalar' | 'boolean' | 'count' — designer karar mantığını şekillendirir.</summary>
    public string ResultType { get; init; } = "scalar";
    public bool IsActive { get; init; } = true;
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; }
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }
}
