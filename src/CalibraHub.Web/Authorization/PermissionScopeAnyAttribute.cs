namespace CalibraHub.Web.Authorization;

/// <summary>
/// 2026-07-26 — Action seviyesinde ÇOKLU FormCode kapsamı. Bir uç birden fazla ekrandan
/// (host form) çağrılabildiğinde, kullanıcının bu FormCode'lardan HERHANGİ BİRİNDE ilgili
/// izne (VIEW/CREATE/EDIT/DELETE — HTTP method + ad kalıbından çözülür) sahip olması yeterlidir.
///
/// <see cref="PermissionEnforcementFilter"/> bu attribute'u taşıyan action'da tekil
/// <see cref="PermissionScopeAttribute"/> yerine buradaki FormCode listesini kullanır ve
/// <c>CheckAnyAsync</c>'i her form için sırayla dener — ilki geçerse izin verilir.
///
/// **Neden var:** Aynı uç birden çok host ekrandan tüketiliyor (ör. OperationMachineTimes hem
/// Operasyon Düzenleme hem Rota ekranından; SaveCardGroupMappings Malzeme/Cari/Makine kartından).
/// Tekil scope tek bir forma kilitleyip diğer ekranın grant'lı kullanıcısını 403'e düşürüyordu.
///
/// **OPT-IN (kritik):** Yalnızca bu attribute'u AÇIKÇA taşıyan action etkilenir. Tekil
/// <see cref="PermissionScopeAttribute"/> taşıyan hiçbir action'ın davranışı değişmez —
/// tek elemanlı liste ile birebir aynı sonucu üretir.
///
/// **Precedence:** Action'da hem PermissionScopeAny hem (miras/eylem) PermissionScope varsa
/// PermissionScopeAny kazanır (daha spesifik, çoklu-form niyeti açık).
///
/// **Action kodu çözümü DEĞİŞMEZ:** Aynen tekil scope gibi — HTTP method + ad kalıbı
/// (ResolveActionCodes) veya AÇIK <see cref="PermissionActionAttribute"/>. Bu attribute
/// yalnızca FormCode KÜMESİNİ genişletir; hangi izin kodunun arandığını etkilemez.
///
/// **Kullanım:**
/// <code>
/// [PermissionScopeAny(FormCodes.OperationEdit, FormCodes.RoutingEdit)]
/// [HttpPost] public IActionResult SaveOperationMachineTime(...) { ... }
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PermissionScopeAnyAttribute : Attribute
{
    /// <summary>Herhangi biri yeterli sayılan FormCode'lar (en az bir tane zorunlu).</summary>
    public string[] FormCodes { get; }

    public PermissionScopeAnyAttribute(params string[] formCodes)
    {
        var cleaned = (formCodes ?? Array.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToArray();
        if (cleaned.Length == 0)
            throw new ArgumentException("En az bir FormCode zorunlu.", nameof(formCodes));
        FormCodes = cleaned;
    }
}
