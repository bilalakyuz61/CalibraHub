using CalibraHub.Domain.Enums;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// SQL View Yönetimi — kullanıcı tarafından kaydedilmiş bir view override/tanımı.
/// Tablo şeması "view-db" ajanı tarafından kurulur (bkz. CLAUDE.md "SQL View Yönetimi —
/// Kontrollü İstisna"); bu entity o kilitli şemayı birebir yansıtır.
///
/// UNIQUE(ViewName) WHERE IsActive=1 — aynı view adı için tek aktif override.
/// </summary>
public sealed class ViewDefinition
{
    public int Id { get; set; }
    public required string ViewName { get; set; }
    public ViewDefinitionKind Kind { get; set; }

    /// <summary>Görsel kurucu tanımının JSON gövdesi (camelCase). Salt raw-SQL ile oluşturulan
    /// view'larda null olabilir.</summary>
    public string? DefinitionJson { get; set; }

    /// <summary>Derlenmiş/onaylanmış SELECT gövdesi — CREATE OR ALTER VIEW ... AS burada kullanılır.</summary>
    public required string GeneratedSql { get; set; }

    /// <summary>true ise kullanıcı "gelişmiş SQL" modunda GeneratedSql'i elle düzenlemiştir;
    /// DefinitionJson dolu olsa bile artık ikincildir (sadece son bilinen görsel state).</summary>
    public bool IsRawMode { get; set; }

    public bool IsActive { get; set; } = true;

    public string? CreatedBy { get; set; }
    public DateTime Created { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? Updated { get; set; }
}
