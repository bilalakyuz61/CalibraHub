using System.ComponentModel;

namespace CalibraHub.Domain.Entities;

/// <summary>
/// Tek kolon eşlemesi: harici kaynağın bir kolonu → hedef entity'nin bir alanı.
/// Yön dışa aktarımın tersidir (orada hedef JSON yolu, burada kaynak kolon).
/// </summary>
[Description("İçe aktarım kolon eşlemesi: kaynak tablo/view kolonu → hedef entity alan anahtarı.")]
public sealed class DataImportJobColumn
{
    public int Id { get; set; }

    public int JobId { get; set; }

    /// <summary>Kaynak nesnedeki kolon adı — okuma öncesi kaynağın sys kataloğuna doğrulanır.</summary>
    public string SourceColumn { get; set; } = string.Empty;

    /// <summary>Hedef alan anahtarı — <c>IImportTargetHandler.GetFields()</c> kataloğundan.</summary>
    public string TargetKey { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
