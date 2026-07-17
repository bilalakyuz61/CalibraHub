namespace CalibraHub.Domain.Entities;

/// <summary>
/// SQL View Yönetimi — bir ViewDefinition satırı her ezildiğinde (Apply/Revert), ezilmeden
/// hemen ÖNCEKİ hali buraya snapshot olarak yazılır (klasik undo geçmişi). Revert, en son
/// buradaki satırın GeneratedSql'ini yeniden apply eder.
/// </summary>
public sealed class ViewDefinitionRevision
{
    public int Id { get; set; }
    public int ViewDefinitionId { get; set; }
    public string? DefinitionJson { get; set; }
    public required string GeneratedSql { get; set; }
    public bool IsRawMode { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime Created { get; set; }
}
