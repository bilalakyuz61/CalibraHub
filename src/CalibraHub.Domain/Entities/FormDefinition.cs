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
///   BaseTable     → Bu form'un fiziksel karsilik tablosu (orn. 'dbo.Contacts')
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
    // 2026-06-09 — UI ikon ve renk bilgisi (ModuleSelector için DB-driven entity türetme)
    public string? Icon { get; init; }
    public string? IconColor { get; init; }

    // Faz 2 (2026-06-09) — DB-driven menu kolonları
    public bool IsMenuItem { get; init; }
    public string? MenuKey { get; init; }
    public string? MenuLabel { get; init; }
    public string? MenuLabelEn { get; init; }
    public string? MenuGroupKey { get; init; }
    public string? MenuGroupName { get; init; }
    public string? MenuGroupIcon { get; init; }
    public int? MenuGroupSortOrder { get; init; }
    public int? MenuSortOrder { get; init; }
    public string? MenuMatchPath { get; init; }
    public bool AdminOnly { get; init; }

    // 2026-06-09 — Alan Rehberi dropdown filtresi
    // true  → Alan Rehberi'nde görünür (widget konfigürasyon hedefi: edit formu, kalem formu)
    // false → gizli (container liste formu, _NEW navigasyon formu, ayarlar sayfası)
    public bool IsWidgetForm { get; init; } = true;
}
