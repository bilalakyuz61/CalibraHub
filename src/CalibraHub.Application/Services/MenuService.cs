using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Services;

/// <summary>
/// dbo.Forms'tan IsMenuItem=1 satırlarını okur (IWidgetRepository üzerinden).
/// Web katmanındaki menü assembly için ham veri sağlar.
/// </summary>
public sealed class MenuService : IMenuService
{
    private const int MinMenuItems = 5;
    private readonly IWidgetRepository _widgetRepo;

    public MenuService(IWidgetRepository widgetRepo)
    {
        _widgetRepo = widgetRepo;
    }

    public async Task<bool> HasMenuDataAsync(CancellationToken ct)
    {
        var items = await _widgetRepo.GetFormsAsync(ct);
        return items.Count(f => f.IsMenuItem) >= MinMenuItems;
    }

    public async Task<IReadOnlyList<MenuItemDto>> GetMenuItemsAsync(CancellationToken ct)
    {
        var forms = await _widgetRepo.GetFormsAsync(ct);
        return forms
            .Where(f => f.IsMenuItem && !string.IsNullOrEmpty(f.MenuKey))
            .OrderBy(f => f.MenuGroupSortOrder ?? 99)
            .ThenBy(f => f.MenuSortOrder ?? 99)
            .Select(f => new MenuItemDto(
                Id: f.Id,
                FormCode: f.FormCode,
                MenuKey: f.MenuKey!,
                MenuLabel: f.MenuLabel ?? f.FormName,
                MenuLabelEn: f.MenuLabelEn,
                Url: null,         // FormDefinition.ListUrl henüz yok — sonraki fazda eklenir
                MatchPath: f.MenuMatchPath,
                MenuGroupKey: f.MenuGroupKey,
                MenuGroupName: f.MenuGroupName,
                MenuGroupIcon: f.MenuGroupIcon,
                MenuGroupSortOrder: f.MenuGroupSortOrder,
                MenuSortOrder: f.MenuSortOrder))
            .ToList()
            .AsReadOnly();
    }
}
