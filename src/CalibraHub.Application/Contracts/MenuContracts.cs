namespace CalibraHub.Application.Contracts;

/// <summary>
/// dbo.Forms'taki IsMenuItem=1 satırından türetilen menü öğesi.
/// MenuService bu DTO'yu statik ağaca enjekte eder.
/// </summary>
public sealed record MenuItemDto(
    int Id,
    string FormCode,
    string MenuKey,
    string MenuLabel,
    string? MenuLabelEn,
    string? Url,           // dbo.Forms.ListUrl (şimdilik null — sonraki fazda eklenir)
    string? MatchPath,     // dbo.Forms.MenuMatchPath
    string? MenuGroupKey,
    string? MenuGroupName,
    string? MenuGroupIcon,
    int? MenuGroupSortOrder,
    int? MenuSortOrder);
