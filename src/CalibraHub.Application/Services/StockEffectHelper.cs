using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Abstractions.Services;
using CalibraHub.Application.Constants;

namespace CalibraHub.Application.Services;

/// <summary>
/// Belge türü bazlı "stok bakiyesini etkiler" parametresinin (STOCK_EFFECT_{code})
/// sorgu yardımcısı. Stok bakiyesi hesaplayan her SQL, buradan dönen dışlanmış
/// DocumentTypeId listesini "(d.DocumentTypeId IS NULL OR d.DocumentTypeId NOT IN (...))"
/// filtresi olarak uygular. Parametre tanımsız veya "true" ise tür dahil kalır —
/// boş liste dönerse filtre eklenmez (mevcut davranış).
/// </summary>
public static class StockEffectHelper
{
    public static async Task<IReadOnlyList<int>> GetDisabledDocTypeIdsAsync(
        ICompanyParameterService parameters,
        IDocumentTypeRepository documentTypes,
        CancellationToken ct)
    {
        var list = await parameters.ListAsync(StockParameters.FormCode, ct);
        var disabledCodes = list
            .Where(p => p.ParamKey.StartsWith(StockParameters.EffectKeyPrefix, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(p.ParamValue, "false", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.ParamKey[StockParameters.EffectKeyPrefix.Length..])
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (disabledCodes.Count == 0) return [];

        var ids = new List<int>();
        foreach (var code in disabledCodes)
        {
            var dt = await documentTypes.GetByCodeAsync(code, ct);
            if (dt != null) ids.Add(dt.Id);
        }
        return ids;
    }
}
