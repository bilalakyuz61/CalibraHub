namespace CalibraHub.Domain.Entities;

/// <summary>
/// dbo.Forms — Form (ekran) katalogu.
///
/// Bu tablo zaten mevcut (CalibraDatabaseInitializer.EnsureFormsTableAsync)
/// ve 25+ form seed edilmis. Yeni WidgetMas.FormId FK buraya baglanir.
///
/// Kolonlar: Id, FormCode, FormName, Module, SubModule, SortOrder, IsActive,
/// BaseTable, BaseRecordKey
///
/// Faz H — Flattened View:
///   BaseTable     → Bu form'un fiziksel karsilik tablosu (orn. 'dbo.ContactAccounts')
///   BaseRecordKey → WidgetTra.RecordId'ye denk gelen kolon (orn. 'AccountCode')
///   Ikisi de dolu ise v_Flat_{FormCode} view'i dinamik olarak olusturulur;
///   EAV widget'lari pivot edilip base tablonun kolonlariyla birlesir.
///   Ikisi de NULL ise view yok — form sirf EAV olarak calisir.
/// </summary>
public sealed class FormDefinition
{
    public int Id { get; init; }
    public required string FormCode { get; init; }
    public required string FormName { get; init; }
    public required string Module { get; init; }
    public string? SubModule { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
    public string? BaseTable { get; init; }
    public string? BaseRecordKey { get; init; }
}
