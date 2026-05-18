using CalibraHub.Application.Contracts;

namespace CalibraHub.Application.Abstractions.Services;

/// <summary>
/// Entegrasyon mapping'inde "Fonksiyon" (IntegrationSourceType.Function) source tipi
/// icin pre-defined entity eslemelerini saglar.
///
/// Her fonksiyon: bir kaynak view (Stok/Cari/Depo gibi) + anahtar kolonu +
/// donulebilir kolon listesi tasir. Runtime'da `ResolveAsync(functionId, keyValue, returnColumn)`
/// cagrisi ile tek deger doner.
///
/// Yeni fonksiyon eklemek: bu registry'nin implementasyonuna yeni satir eklemek yeterli;
/// MappingEngine ve UI dropdown'lari otomatik gunceller.
/// </summary>
public interface IIntegrationLookupFunctionRegistry
{
    /// <summary>Tum fonksiyonlari listele — UI dropdown'unu doldurmak icin.</summary>
    IReadOnlyList<IntegrationLookupFunctionDto> List();

    /// <summary>Belirli fonksiyon icin metadata getir.</summary>
    IntegrationLookupFunctionDto? Get(string functionId);

    /// <summary>
    /// Verilen fonksiyonu calistir: anahtar deger ile view'da satir bul, donus kolonunu cek.
    /// Bulunamazsa null doner.
    /// </summary>
    Task<object?> ResolveAsync(string functionId, string? keyValue, string? returnColumn, CancellationToken ct);

    /// <summary>
    /// SQL Fonksiyonu modu (SqlFunctionName dolu) icin 3-paramli call.
    /// Diger modlar (View+Key, SqlSnippet[legacy]) icin asagidaki <see cref="ResolveAsync(string, string?, string?, CancellationToken)"/>
    /// kullanilir — bu overload sadece SqlFunctionName secimi olan fonksiyonlar icindir.
    /// </summary>
    /// <param name="functionId">Lookup function code (orn. "CARI_BAKIYE").</param>
    /// <param name="formCode">Mapping engine'in saglayacagi @P1 — kaynak form'un kodu (orn. "SALES_ORDER_NEW").</param>
    /// <param name="keyValue">Mapping satirinin LookupSourceField alanindan cekilen @P2.</param>
    /// <param name="manualParam">Mapping satirinin LookupParam alaninda kullanici yazimi @P3.</param>
    /// <param name="returnColumn">SQL Function modunda kullanilmaz (scalar deger doner).</param>
    Task<object?> ResolveWithParamsAsync(
        string functionId,
        string? formCode,
        string? keyValue,
        string? manualParam,
        string? returnColumn,
        CancellationToken ct);

    /// <summary>
    /// Yeni — wrapper tablosu olmadan, dogrudan DB'de tanimli scalar function'i 3 paramli imza ile cagirir.
    /// Wizard "Fonksiyon" source tipi artik bu yolu kullanir (admin "Lookup Fonksiyonu" tablosu by-pass).
    /// </summary>
    /// <param name="functionFullName">Schema-qualified DB fonksiyon adi (orn. "dbo.fn_GetContactBalance").</param>
    /// <param name="formCode">@P1 — kaynak form'un kodu.</param>
    /// <param name="keyValue">@P2 — mapping satirinin LookupSourceField alanindan cekilen deger.</param>
    /// <param name="manualParam">@P3 — mapping satirinin LookupParam alanindaki kullanici yazimi.</param>
    Task<object?> ExecuteDbFunctionAsync(
        string functionFullName,
        string? formCode,
        string? keyValue,
        string? manualParam,
        CancellationToken ct);

    /// <summary>
    /// DB'de tanimli scalar/TVF fonksiyonlari listeler — Wizard "Fonksiyon" source dropdown'i icin.
    /// (sys.objects type IN ('FN','IF','TF') AND NOT is_ms_shipped)
    /// </summary>
    Task<IReadOnlyList<AvailableDbFunctionDto>> ListDbFunctionsAsync(CancellationToken ct);
}
