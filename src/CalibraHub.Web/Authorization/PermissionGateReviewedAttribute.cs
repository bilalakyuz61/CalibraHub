namespace CalibraHub.Web.Authorization;

/// <summary>
/// 2026-08-27 — "Bu ucun <see cref="PermissionScopeAttribute"/> taşımaması BİLİNÇLİ" beyanı.
/// Yetkilendirmeye etki ETMEZ; yalnızca sağlık kontrolünün yetki kapısı taramasına konuşur.
///
/// <para><b>Neden var:</b> tarama yalnız öznitelik arıyordu, gövdedeki kontrolü göremiyordu.
/// Sonuç: 28 mutasyonun 22'si "kapısız" diye raporlanıyordu — hepsi aslında korumalıydı.
/// Böyle bir uyarı gerçek bulguyu gürültüde boğar; her denetimde 28 ucu elle elemek gerekti.</para>
///
/// <para><b>İki meşru gerekçe var, ikisi de burada yazılır:</b></para>
/// <list type="number">
///   <item><b>Kontrol gövdede.</b> Paylaşılan controller'larda (SalesController hem satış hem
///   satın alma belgelerine hizmet eder) sabit bir form kodu YANLIŞ olurdu — kullanıcı yetkili
///   olduğu tipi beyan edip Id ile başka tipteki belgeyi hedefleyerek per-tip yetkiyi
///   atlatabilirdi. Bu uçlar belge tipini DB'den çözüp doğru form kodunda kontrol eder;
///   kapsam ancak çalışma zamanında bilinir.</item>
///   <item><b>İzin gerekmiyor.</b> Uç yalnızca ÇAĞIRANIN KENDİ tercihini yazar (userId ile
///   anahtarlanmış sütun düzeni, pano yerleşimi vb.). Başka kullanıcının verisine erişim
///   yolu yoktur, dolayısıyla kapı takmak anlamsızdır.</item>
/// </list>
///
/// <para><b>Kullanım kuralı:</b> gerekçeyi <paramref name="reason"/>'a AÇIKÇA yaz — hangi
/// kontrolün yapıldığını ya da neden gerekmediğini. Kontrolü kaldırıp özniteliği bırakmak
/// taramayı sessizce kör eder; bu, projenin kaçınmayı şart koştuğu "sessiz kırık" sınıfının
/// ta kendisidir.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class PermissionGateReviewedAttribute : Attribute
{
    /// <summary>Kapının nerede olduğu ya da neden gerekmediği — denetimde okunacak tek kaynak.</summary>
    public string Reason { get; }

    public PermissionGateReviewedAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Gerekçe zorunlu.", nameof(reason));
        Reason = reason;
    }
}
