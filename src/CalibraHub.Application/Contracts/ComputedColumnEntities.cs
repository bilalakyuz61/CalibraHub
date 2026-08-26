namespace CalibraHub.Application.Contracts;

/// <summary>
/// Hesaplanan Kolon'un takılabildiği varlıklar — TEK DOĞRULUK KAYNAĞI.
///
/// Buradaki liste keyfi bir enum değil, GERÇEKTEN BAĞLANMIŞ liste ekranlarının aynasıdır.
/// Bir varlığın kolon gösterebilmesi için o board'un yapılandırıcısının tanımları okuyup
/// hücreleri üretmesi gerekir; bağlanmamış bir varlığı tanım ekranında göstermek boş vaattir
/// (kullanıcı tanımı kaydeder, hiçbir yerde görünmez, hata da almaz).
///
/// YENİ VARLIK EKLERKEN: önce board'u bağla, SONRA buraya satır ekle. Ters sıra sessiz
/// hayal kırıklığı üretir.
/// </summary>
public static class ComputedColumnEntities
{
    public const string Item = "Item";
    public const string WorkOrder = "WorkOrder";
    public const string Contact = "Contact";
    public const string Document = "Document";

    /// <param name="Kind">Tanımda saklanan kod.</param>
    /// <param name="Label">Kullanıcıya görünen ad.</param>
    /// <param name="KeyHint">Anahtar kolonun ne olması beklendiği — tanım ekranında ipucu.</param>
    public sealed record Entry(string Kind, string Label, string KeyHint);

    /// <summary>Bağlı varlıklar. Sıra tanım ekranındaki açılır listenin sırasıdır.</summary>
    public static readonly IReadOnlyList<Entry> All = new[]
    {
        new Entry(Item,      "Malzeme",  "ItemId"),
        new Entry(WorkOrder, "İş Emri",  "WorkOrderId"),
        new Entry(Contact,   "Cari",     "ContactId"),
        new Entry(Document,  "Belge",    "DocumentId"),
    };

    public static string LabelOf(string? kind)
        => All.FirstOrDefault(e => string.Equals(e.Kind, kind, StringComparison.OrdinalIgnoreCase))?.Label ?? (kind ?? "");
}
