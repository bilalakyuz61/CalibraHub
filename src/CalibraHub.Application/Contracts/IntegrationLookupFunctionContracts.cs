namespace CalibraHub.Application.Contracts;

/// <summary>
/// Entegrasyon mapping'inde "Fonksiyon" source tipi icin metadata.
/// UI dropdown'lari + backend registry bu yapida bilgi tasir.
/// </summary>
public sealed record IntegrationLookupFunctionDto(
    /// <summary>Stabil ID (enum yerine string): "ITEMS", "CONTACTS", "LOCATIONS"...</summary>
    string Id,
    /// <summary>UI'da gosterilen baslik: "Stok", "Cari", "Depo"...</summary>
    string Label,
    /// <summary>Kisa aciklama (tooltip).</summary>
    string Description,
    /// <summary>Donulebilir kolon listesi — UI'da "Donus" dropdown'i icin.</summary>
    IReadOnlyList<IntegrationLookupFunctionColumn> ReturnColumns,
    /// <summary>
    /// Fonksiyon modu — UI hangi input'larin gosterilecegine karar verir:
    ///   "view"    = Rehber/View+Key (klasik) → form alani secimi + donus kolonu
    ///   "sqlfn"   = SQL Fonksiyonu (yeni)     → form alani secimi + manuel @P3 input
    ///   "snippet" = SQL Snippet (legacy)      → sadece form alani (returnColumn opsiyonel)
    /// </summary>
    string Kind = "view");

/// <summary>Fonksiyon icin donulebilir bir kolon.</summary>
public sealed record IntegrationLookupFunctionColumn(
    /// <summary>Kolon adi (view kolonu): "Code", "Name", "AccountTitle"...</summary>
    string Column,
    /// <summary>UI'da gosterilen baslik.</summary>
    string Label);

/// <summary>Admin panelinde liste/edit icin tam DTO.</summary>
public sealed record IntegrationLookupFunctionAdminDto(
    int Id,
    string Code,
    string Label,
    string? Description,
    string? ViewName,
    string? KeyColumn,
    string? SqlSnippet,             // [LEGACY]
    string? SqlFunctionName,        // YENI: DB'de tanimli scalar function (3-param standart imza)
    int SortOrder,
    bool IsActive,
    IReadOnlyList<IntegrationLookupFunctionColumn> Columns);

/// <summary>Admin paneli save request (yeni veya guncelle).</summary>
public sealed record SaveIntegrationLookupFunctionRequest(
    int? Id,
    string Code,
    string Label,
    string? Description,
    string? ViewName,
    string? KeyColumn,
    string? SqlSnippet,             // [LEGACY]
    string? SqlFunctionName,        // YENI
    int SortOrder,
    bool IsActive,
    IReadOnlyList<IntegrationLookupFunctionColumn> Columns);

/// <summary>
/// Admin UI'sinin "SQL Fonksiyonu" dropdown'unu doldurmasi icin: DB'de bulunan
/// scalar/inline-TVF/multi-TVF fonksiyon listesi (sys.objects type IN ('FN','IF','TF')).
/// </summary>
public sealed record AvailableDbFunctionDto(
    /// <summary>Schema-qualified ad (orn. "dbo.fn_GetContactBalance").</summary>
    string FullName,
    /// <summary>SQL Server tipi: "FN" = scalar, "IF" = inline TVF, "TF" = multi-TVF.</summary>
    string Type,
    /// <summary>Function parametre sayisi (3 = standart imzaya uygun).</summary>
    int ParameterCount);
