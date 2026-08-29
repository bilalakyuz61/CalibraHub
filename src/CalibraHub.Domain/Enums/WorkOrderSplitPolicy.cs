namespace CalibraHub.Domain.Enums;

/// <summary>
/// Bir malzemenin ihtiyacı iş emrine dönüşürken NASIL gruplanacağı (MRP kırılım politikası).
/// Malzeme kartında (Items.WorkOrderSplitPolicy) tanımlanır; grup/şirket mirası YOKTUR —
/// kart boşsa <see cref="PerOrderLine"/> geçerlidir (bugünkü davranışın aynısı).
///
/// <para>Yalnız üretilebilir tiplerde (Mamul / Yarı Mamul) anlamlıdır. Çok seviyeli patlatmada
/// her seviye KENDİ malzemesinin politikasına göre gruplanır: bir yarı mamul "Kümüle" ise,
/// farklı mamullerden ve farklı siparişlerden gelen ihtiyacı tek alt iş emrinde birleşir.</para>
///
/// <para>DB'de string olarak saklanır (Items.TrackingType ile aynı desen) — sayısal değerler
/// kolonda görünmez, bu yüzden değer atamaları serbestçe değiştirilebilir ama <b>isimler</b>
/// kalıcı sözleşmedir.</para>
/// </summary>
public enum WorkOrderSplitPolicy
{
    /// <summary>Her açık sipariş satırı için ayrı iş emri. Varsayılan.</summary>
    PerOrderLine = 0,

    /// <summary>Aynı belgedeki aynı malzeme satırları tek iş emrinde birleşir.</summary>
    PerOrder = 1,

    /// <summary>Koşudaki tüm siparişlerin aynı malzeme ihtiyacı tek iş emrinde birleşir.</summary>
    Cumulative = 2,
}

/// <summary>
/// <see cref="WorkOrderSplitPolicy"/> için string ↔ enum çözümlemesi. DB'de ve JSON'da değer
/// STRING'dir; bilinmeyen/boş değer sessizce varsayılana düşer (fail-safe: politika okunamadı
/// diye MRP durmaz, bugünkü davranışa döner).
/// </summary>
public static class WorkOrderSplitPolicyCatalog
{
    public const string Default = nameof(WorkOrderSplitPolicy.PerOrderLine);

    public static WorkOrderSplitPolicy Parse(string? value)
        => Enum.TryParse<WorkOrderSplitPolicy>(value, ignoreCase: true, out var parsed)
            ? parsed
            : WorkOrderSplitPolicy.PerOrderLine;

    /// <summary>DB'ye yazılacak normalize edilmiş metin — geçersiz girdi varsayılana çevrilir.</summary>
    public static string Normalize(string? value) => Parse(value).ToString();

    public static string Label(WorkOrderSplitPolicy policy) => policy switch
    {
        WorkOrderSplitPolicy.PerOrder   => "Sipariş Bazında",
        WorkOrderSplitPolicy.Cumulative => "Kümüle (Tüm Siparişler)",
        _                               => "Sipariş Satırı Bazında",
    };
}
