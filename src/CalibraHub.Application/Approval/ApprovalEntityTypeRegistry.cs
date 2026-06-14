namespace CalibraHub.Application.Approval;

/// <summary>
/// DI tarafından inject edilen tüm <see cref="IApprovalEntityType"/>'ları toplar
/// ve <see cref="Get"/> ile code üzerinden çözüm sağlar. Aynı code'la birden fazla
/// implementasyon varsa son kayıt edilen kazanır (Program.cs sırası).
/// </summary>
public sealed class ApprovalEntityTypeRegistry : IApprovalEntityTypeRegistry
{
    private readonly Dictionary<string, IApprovalEntityType> _byCode;
    private readonly IReadOnlyList<IApprovalEntityType> _all;

    public ApprovalEntityTypeRegistry(IEnumerable<IApprovalEntityType> entityTypes)
    {
        var list = entityTypes?.ToList() ?? new List<IApprovalEntityType>();
        _byCode = new Dictionary<string, IApprovalEntityType>(StringComparer.OrdinalIgnoreCase);
        foreach (var et in list)
        {
            if (et is null || string.IsNullOrWhiteSpace(et.Code)) continue;
            _byCode[et.Code] = et;
        }
        // Order: Önce kategori sıralaması (Belgeler → Üretim → Tanımlar → Diğer),
        // sonra kategori içinde "Document" wildcard en üstte, kalanlar alfabetik.
        // UI dropdown'da optgroup sırası deterministik olur.
        static int CategoryOrder(string? cat) => cat switch
        {
            "Belgeler" => 0,
            "Üretim"   => 1,
            "Tanımlar" => 2,
            _          => 3,
        };
        _all = list
            .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Code))
            .OrderBy(e => CategoryOrder(e.GroupCategory))
            .ThenBy(e => e.Code.Equals("Document", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<IApprovalEntityType> All => _all;

    public IApprovalEntityType? Get(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        return _byCode.TryGetValue(code, out var et) ? et : null;
    }
}
