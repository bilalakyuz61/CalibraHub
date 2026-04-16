namespace CalibraHub.Application.Contracts;

/// <summary>
/// Form (ekran) katalogu DTO'lari — Admin "Form Yöneticisi" ekranı için.
/// </summary>
public sealed record FormDto(
    int Id,
    string FormCode,
    string FormName,
    string? Module,
    string? SubModule,
    int SortOrder,
    bool IsActive,
    string? BaseTable,
    string? BaseRecordKey);

public sealed record CreateFormRequest(
    string FormCode,
    string FormName,
    string? Module,
    string? SubModule,
    int SortOrder,
    bool IsActive,
    string? BaseTable,
    string? BaseRecordKey);

public sealed record UpdateFormRequest(
    int Id,
    string FormCode,
    string FormName,
    string? Module,
    string? SubModule,
    int SortOrder,
    bool IsActive,
    string? BaseTable,
    string? BaseRecordKey);
