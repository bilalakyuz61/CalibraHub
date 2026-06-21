namespace CalibraHub.Application.Contracts;

/// <summary>Tablo ozet bilgisi (liste paneli icin).</summary>
public sealed record DbTableSummaryDto(
    string Schema,
    string Name,
    long RowCount,
    int ColumnCount,
    int ForeignKeyCount,
    /// <summary>
    /// Entity sinif seviyesindeki [System.ComponentModel.Description]'dan
    /// okunan kisa tablo ozeti. Eslesme yoksa null.
    /// </summary>
    string? Description = null);

/// <summary>Tablo detayi — kolonlar, indeksler, FK'ler (iki yonlu).</summary>
public sealed record DbTableDetailDto(
    string Schema,
    string Name,
    long RowCount,
    IReadOnlyList<DbColumnDto> Columns,
    IReadOnlyList<DbIndexDto> Indexes,
    IReadOnlyList<DbForeignKeyDto> OutgoingForeignKeys,
    IReadOnlyList<DbForeignKeyDto> IncomingForeignKeys,
    /// <summary>Tablo seviyesi [Description] (Entity sinifindan).</summary>
    string? Description = null,
    /// <summary>Eslesen C# Entity tipinin tam adi (debugging icin).</summary>
    string? ClrTypeName = null);

public sealed record DbColumnDto(
    int OrdinalPosition,
    string Name,
    string SqlType,
    int? MaxLength,
    int? Precision,
    int? Scale,
    bool IsNullable,
    string? DefaultDefinition,
    bool IsPrimaryKey,
    bool IsIdentity,
    bool IsForeignKey,
    string? ForeignKeyTarget,
    /// <summary>
    /// C# Entity property'si uzerindeki [System.ComponentModel.Description]
    /// attribute degerinden okunur. Bulunamazsa null.
    /// </summary>
    string? Description = null,
    /// <summary>
    /// C# property'nin tipi bir Enum ise (veya Nullable&lt;Enum&gt;), enum
    /// degerleri + isimleri. Aksi halde null.
    /// </summary>
    IReadOnlyList<DbEnumValueDto>? EnumValues = null,
    /// <summary>
    /// C# Entity property ismi (varsa). Snake_case -> PascalCase donusumu
    /// sonrasi eslesen property adi. Bulunamazsa null — reflection mapping
    /// eksikligini gosterir.
    /// </summary>
    string? ClrPropertyName = null);

public sealed record DbEnumValueDto(
    string Name,
    long Value,
    string? Description);

public sealed record DbIndexDto(
    string Name,
    string Type,
    bool IsUnique,
    bool IsPrimaryKey,
    IReadOnlyList<string> Columns);

public sealed record DbForeignKeyDto(
    string ConstraintName,
    string FromTable,
    string FromColumn,
    string ToTable,
    string ToColumn,
    string DeleteAction,
    string UpdateAction);

// ── Report Designer — view introspection ──────────────────────────────────

/// <summary>Report Designer kaynak seçici için tek view + kolon bilgisi.</summary>
public sealed record RdViewInfo(string Name, IReadOnlyList<RdColumnInfo> Columns);

/// <summary>View kolonu — Report Designer metrik/grup sınıflandırması için.</summary>
public sealed record RdColumnInfo(string Name, string SqlType, bool IsNumeric, bool IsTime);

// ── DB Schema haritası — view kataloğu ────────────────────────────────────

/// <summary>
/// DB Schema haritasında gösterilen CalibraHub view bilgisi.
/// Statik açıklamalar CalibraViewCatalog'dan, fiziksel kolon listesi sys.columns'dan gelir.
/// </summary>
public sealed record DbViewInfoDto(
    string Name,
    string Description,
    string UsedIn,
    bool IsCustomizable,
    bool ExistsInDb,
    IReadOnlyList<string> Columns);
