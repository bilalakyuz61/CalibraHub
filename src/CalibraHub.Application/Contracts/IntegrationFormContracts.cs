namespace CalibraHub.Application.Contracts;

/// <summary>
/// Wizard Step 1'in form picker'i icin — basit form tanimi (FormCode + Label + grouping).
/// dbo.Forms tablosundan okunur. Master-Detail entegrasyon icin LinesFormCode +
/// LinesParentColumn da doner — wizard kullanicisi tek form secince kalem form'u
/// otomatik kesfedilir.
/// </summary>
public sealed record IntegrationFormDto(
    int Id,
    string FormCode,
    string FormName,
    string Module,
    string? SubModule,
    string? BaseTable,
    string? BaseRecordKey,
    string? LinesFormCode = null,         // "SALES_ORDER_LINES" — kalem form (NULL = tek seviyeli)
    string? LinesParentColumn = null,     // "DocumentId" — kalem tablosundaki parent FK
    // Faz N — Forms tablosundan UI/entegrasyon metadata. Tek-noktada (Forms.* kolon).
    // Yeni belge tipi eklenince DB seed yeterli; kod/UI degisikligi yok.
    string? ListUrl = null,               // "/Sales/Orders" — save/delete sonrasi geri donus
    string? NewUrl = null,                // "/Sales/DocumentEdit?type=order"
    string? EditUrl = null,               // "/Sales/DocumentEdit"
    string? Icon = null,                  // Lucide icon adi
    string? IconColor = null,             // tema renk anahtari
    bool IsTransferable = true);          // entegrasyon picker'da goster + "ERP'ye Aktar" buton

/// <summary>
/// Wizard Step 1'in form alani agacinda gosterilen tek alan.
/// dbo.WidgetMas + sistem alanlari (Id, Created, vb.) birlestirilerek doner.
///
/// IsPlainField=true ise temel tablo kolonudur (orn. Document.DocumentNumber).
/// IsPlainField=false ise dinamik widget'tir (WidgetTra'da deger tutulan EAV alan).
///
/// Section: 3 katmanli "veri seti" katmani — wizard Step 3'te source field grubu
/// olarak gosterilir. Mapping'lerde IntegrationMapping.SourceSection'a yansir.
///   "Header"      = ust form alani (form'un kendisi)
///   "Lines"       = kalem form alani (form.LinesFormCode dolu ise eklenir)
///   "Combination" = kalem kombinasyon kodu (DocumentLine.CombinationId resolver, runtime)
/// </summary>
public sealed record IntegrationFormFieldDto(
    string Code,         // WidgetCode veya kolon adi (orn. "DocumentNumber", "MusteriKodu")
    string Label,        // Kullaniciya gosterilecek metin
    string DataType,     // string | numeric | decimal | date | datetime | bool | text | dropdown | lookup
    bool IsRequired,
    bool IsPlainField,   // true => base table kolonu, false => widget
    string? GroupKey,    // Nested alan grubu (orn. "Kalemler[]" — V2'de aktif)
    string Section = "Header");   // "Header" | "Lines" | "Combination"

/// <summary>
/// Wizard Step 4'te onizleme icin: bir form kaydinin tum alanlarinin degerleri.
/// v_Flat_{FormCode} view'indan okunur — base + widget hepsi flat.
///
/// FieldValues dictionary'sinde:
///   key   = WidgetCode veya kolon adi
///   value = sahipte ne varsa (string, int, datetime, decimal, ...).
/// </summary>
public sealed record IntegrationSampleRecordDto(
    string RecordId,
    IReadOnlyDictionary<string, object?> FieldValues);
