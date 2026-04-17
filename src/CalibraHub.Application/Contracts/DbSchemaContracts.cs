namespace CalibraHub.Application.Contracts;

/// <summary>Tablo ozet bilgisi (liste paneli icin).</summary>
public sealed record DbTableSummaryDto(
    string Schema,
    string Name,
    long RowCount,
    int ColumnCount,
    int ForeignKeyCount);

/// <summary>Tablo detayi — kolonlar, indeksler, FK'ler (iki yonlu).</summary>
public sealed record DbTableDetailDto(
    string Schema,
    string Name,
    long RowCount,
    IReadOnlyList<DbColumnDto> Columns,
    IReadOnlyList<DbIndexDto> Indexes,
    IReadOnlyList<DbForeignKeyDto> OutgoingForeignKeys,
    IReadOnlyList<DbForeignKeyDto> IncomingForeignKeys);

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
    string? ForeignKeyTarget);

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
