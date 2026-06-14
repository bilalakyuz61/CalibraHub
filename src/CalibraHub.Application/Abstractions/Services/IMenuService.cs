using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// dbo.Forms tablosundan IsMenuItem=1 satırlarını okur.
/// Web katmanı (MenuService.cs) bu listeyi statik ağaçla birleştirir.
/// </summary>
public interface IMenuService
{
    /// <summary>
    /// DB'de en az 5 IsMenuItem=1 satırı varsa true döner.
    /// Startup'ta false ise statik menü kullanılmaya devam eder.
    /// </summary>
    Task<bool> HasMenuDataAsync(CancellationToken ct);

    /// <summary>
    /// IsMenuItem=1 olan formların menü DTO listesini döner.
    /// MenuGroupSortOrder + MenuSortOrder'a göre sıralıdır.
    /// </summary>
    Task<IReadOnlyList<MenuItemDto>> GetMenuItemsAsync(CancellationToken ct);
}
