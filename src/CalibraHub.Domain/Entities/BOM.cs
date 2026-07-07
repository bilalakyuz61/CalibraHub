using System.ComponentModel;
using CalibraHub.Domain.Common;

namespace CalibraHub.Domain.Entities;

[Description("Urun agaci baslik (Bill of Materials). ItemId FK ile Items tablosuna, ConfigId FK ile ItemConfiguration varyantina baglanir. Bilesenleri BOMLine tablosunda 1-N iliskiyle tutulur. ImageRotation: gorselin gosterim donus acisi (0/90/180/270 derece). IsActive=0 soft-delete; eski is emirleri orphan kalmasin diye fiziksel silme yapilmaz.")]
public class BOM
{
    public int Id { get; init; }
    public int ItemId { get; init; }
    public int? ConfigId { get; init; }
    public string? Description { get; init; }
    public byte[]? ImageData { get; init; }
    public string? ImageMimeType { get; init; }
    public string? ImageFitMode { get; init; }
    public int ImageRotation { get; init; } = 0;

    // Audit dortlusu + soft delete bayragi (rapor §2026-05-17 BOM analizi madde 3.5, 3.6).
    // Yeni standart: tum tablolarda zorunlu (CLAUDE.md "Standart kolon seti").
    public bool IsActive { get; init; } = true;
    public int? CreatedById { get; init; }
    public DateTime Created { get; init; } = DateTime.UtcNow;
    public int? UpdatedById { get; init; }
    public DateTime? Updated { get; init; }

    /// <summary>
    /// Bu reçetenin üretileceği rota (opsiyonel FK -> Routing.Id). NULL ise iş emri
    /// açılırken kullanıcı/sistem ayrı seçer. Dolu ise: WorkOrder.RoutingId default'u
    /// olur — reçete + rota tek noktada birleşir. (Rapor 2026-05-19 kararı — kullanıcı
    /// önerisi: line-level Operation yerine header-level Routing FK.)
    /// </summary>
    public int? RoutingId { get; init; }

    /// <summary>Rota kodu — display amaçlı, repository JOIN ile doldurulur. Persist edilmez.</summary>
    public string? RoutingCode { get; set; }
    /// <summary>Rota adı — display amaçlı, repository JOIN ile doldurulur. Persist edilmez.</summary>
    public string? RoutingName { get; set; }

    // Lines mutable referans degil — collection init, ama icindeki List
    // mutate edilebilir (AddLine/RemoveLine domain davranisi yerinde duzeltir).
    public ICollection<BOMLine> Lines { get; init; } = new List<BOMLine>();

    // ═══════════════════════════════════════════════════════════════════
    // Domain davranis metotlari (rapor 2026-05-17 madde 3.2 — rich domain).
    // Mevcut setter'lar GERIYE UYUM icin acik birakildi; service'ler dogrudan
    // entity insa edebilir. Yeni kod bu metotlari cagirip invariant
    // garantilerini alir (duplicate kontrol, line invariant'i, bos liste, vs.).
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Satir ekler. Duplicate (ayni ItemId+ConfigId) korumasi var; line invariant'i
    /// satirin kendisi tarafindan kontrol edilir (BOMLine.EnsureValid).
    /// Kullanici dostu mesajlar (rapor 2026-05-17 madde 3.11) — teknik ID ifsa edilmez.
    /// 2026-07-05: allowDuplicate — sirket parametresi BOM_ALLOW_DUPLICATE_COMPONENTS
    /// aciksa ayni bilesen farkli satirlarda tekrar edebilir (farkli fire/aciklama
    /// pozisyonlari icin). Parametre okumasi Application katmanindadir; domain yalniz
    /// bayragi uygular.
    /// </summary>
    public void AddLine(BOMLine line, bool allowDuplicate = false)
    {
        DomainException.ThrowIf(line is null, "Bilesen bilgisi eksik.");
        line!.EnsureValid();
        if (!allowDuplicate)
        {
            DomainException.ThrowIf(
                Lines.Any(l => l.ItemId == line.ItemId && (l.ConfigId ?? 0) == (line.ConfigId ?? 0)),
                "Ayni bilesen birden fazla kez eklenemez. Lutfen bilesen listesini gozden geciriniz.");
        }
        Lines.Add(line);
    }

    /// <summary>
    /// (ItemId, ConfigId) anahtariyla satir cikartir. Bulunamazsa DomainException.
    /// </summary>
    public void RemoveLine(int itemId, int? configId = null)
    {
        var line = Lines.FirstOrDefault(l =>
            l.ItemId == itemId && (l.ConfigId ?? 0) == (configId ?? 0));
        DomainException.ThrowIf(line is null,
            "Silinmek istenen bilesen recetede bulunamadi.");
        Lines.Remove(line!);
    }

    /// <summary>
    /// Bir bilesenin miktarini guvenle guncelle (BOMLine.ChangeQuantity'e delege).
    /// </summary>
    public void ChangeLineQuantity(int itemId, int? configId, decimal newQuantity)
    {
        var line = Lines.FirstOrDefault(l =>
            l.ItemId == itemId && (l.ConfigId ?? 0) == (configId ?? 0));
        DomainException.ThrowIf(line is null,
            "Miktari degistirilmek istenen bilesen recetede bulunamadi.");
        line!.ChangeQuantity(newQuantity);
    }

    /// <summary>
    /// Tum BOM seviyesi invariant'lari — save oncesi son kontrol noktasi.
    /// Cycle check ayri (EnsureNoCycle), cunku repository delegate'i lazim.
    /// allowDuplicateLines: BOM_ALLOW_DUPLICATE_COMPONENTS parametresi (bkz. AddLine).
    /// </summary>
    public void EnsureValid(bool allowDuplicateLines = false)
    {
        DomainException.ThrowIf(ItemId <= 0,
            "Mamul secmek zorunludur. Listeden bir mamul kodu seciniz.");
        DomainException.ThrowIf(Lines.Count == 0,
            "Recetede en az bir bilesen olmalidir.");

        // Duplicate koruma — AddLine zaten ekleme aninda kontrol eder, ama service
        // entity'yi koleksiyon initializer ile dogrudan kursaydi AddLine cagrilmazdi.
        // Bu defansif tarama o senaryoyu da kapsiyor. (Parametre acikken atlanir.)
        if (!allowDuplicateLines)
        {
            var hasDup = Lines
                .GroupBy(l => (l.ItemId, l.ConfigId ?? 0))
                .Any(g => g.Count() > 1);
            DomainException.ThrowIf(hasDup,
                "Recetede ayni bilesen birden fazla kez var. Lutfen bilesen listesini gozden geciriniz.");
        }

        // Her satirin kendi invariant'i (negatif quantity/scrap, ItemId<=0 vs.)
        foreach (var line in Lines)
            line.EnsureValid();

        // ImageFitMode whitelist (free/square dahili kullanim — disardan keyfi string gelmesin)
        if (!string.IsNullOrWhiteSpace(ImageFitMode))
        {
            var m = ImageFitMode!.Trim().ToLowerInvariant();
            DomainException.ThrowIf(m != "square" && m != "free" && m != "contain",
                "Gorsel oturma modu yalniz 'square', 'free' veya 'contain' olabilir.");
        }

        // ImageRotation tahsisli set — repository de normalize ediyor; defansif
        DomainException.ThrowIf(
            ImageRotation != 0 && ImageRotation != 90 && ImageRotation != 180 && ImageRotation != 270,
            "Gorsel donus acisi 0, 90, 180 veya 270 derece olmalidir.");
    }

    /// <summary>
    /// Cycle (dongusel bagimlilik) korumasi — A nin recetesine B, B nin recetesine A
    /// gibi durumlari yakalar. Direkt self-reference (parent kendi bilesen listesinde)
    /// trivial case; indirect loop (A→B→C→A) icin recursive cocuk gezinmesi gerekir.
    ///
    /// `getChildren` delegate'i bir itemId verildiginde o item'in BOM'undaki ItemId
    /// listesini doner (repository'den lazy). Domain repository'ye bagimli kalmasin
    /// diye delegate pattern'i kullanildi.
    ///
    /// Algoritma: BFS, depth cap 20 (gercek dunyada 5-6 seviye yeterli, 20 sonsuza
    /// karsi guvenlik). Cycle bulunursa DomainException firlatilir — caller ArgumentException
    /// olarak yansitir.
    /// </summary>
    public static void EnsureNoCycle(
        int parentItemId,
        IReadOnlyCollection<int> proposedChildItemIds,
        Func<int, IEnumerable<int>> getChildren)
        => EnsureNoCycleAsync(parentItemId, proposedChildItemIds,
                childId => Task.FromResult(getChildren(childId)))
            .GetAwaiter().GetResult(); // delegate sync — hicbir await askida kalmaz, gercek blokaj yok

    /// <summary>
    /// <see cref="EnsureNoCycle"/> ile ayni algoritma; cocuk listesi async kaynaklardan
    /// (repository) geldiginde thread bloklamadan gezinmek icin.
    /// </summary>
    public static async Task EnsureNoCycleAsync(
        int parentItemId,
        IReadOnlyCollection<int> proposedChildItemIds,
        Func<int, Task<IEnumerable<int>>> getChildren)
    {
        const int maxDepth = 20;
        DomainException.ThrowIf(
            proposedChildItemIds.Contains(parentItemId),
            "Recetenin kendi mamulu bilesen olarak eklenemez (dongusel bagimlilik).");

        // BFS: her seviyeyi tek tek expand et, gorulen ItemId'leri visited set'inde tut.
        // proposedChildItemIds zaten frontier (1. seviye).
        var visited = new HashSet<int> { parentItemId };
        var frontier = new List<int>(proposedChildItemIds);

        for (var depth = 1; depth <= maxDepth && frontier.Count > 0; depth++)
        {
            var nextFrontier = new List<int>();
            foreach (var childId in frontier)
            {
                if (!visited.Add(childId)) continue; // zaten gezildi, atla
                var grandChildren = await getChildren(childId);
                if (grandChildren == null) continue;
                foreach (var gc in grandChildren)
                {
                    DomainException.ThrowIf(
                        gc == parentItemId,
                        "Dongusel recete tespit edildi: bu bilesenlerden biri eninde sonunda mamulun kendisine bagli.");
                    nextFrontier.Add(gc);
                }
            }
            frontier = nextFrontier;
        }
    }
}
