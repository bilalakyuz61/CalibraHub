using CalibraHub.Application.Abstractions.Persistence;
using CalibraHub.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace CalibraHub.Persistence.Repositories;

/// <summary>
/// DocumentLineLink dual-write yardımcıları — FAZ 1b-iii (2026-07-20, "A: dönüşüm zinciri +
/// teslimat" mekanizması, bkz. DocumentLineLink-Tasarim.md §5 "A → Link"). WorkOrderService
/// (B, <c>TryLinkWorkOrderAllocAsync</c>/<c>TryReverseWorkOrderLinksAsync</c>) ve
/// <see cref="FulfillmentLedger"/> (C, <c>TryLinkFulfillmentEntriesAsync</c>/<c>ReverseByDocumentAsync</c>)
/// ile BİREBİR aynı defensive best-effort desen: ana kayıt (DocumentLine.SourceLineId INSERT/UPDATE
/// veya Document soft-delete) başarıyla TAMAMLANDIKTAN SONRA çağrılır, kendi try/catch'i içinde
/// çalışır, ASLA exception fırlatmaz — link tablosu bir bug taşırsa canlı belge kaydı/silme
/// kırılmaz (CLAUDE.md "sessiz kırık" kural #2: boş catch yasak, exception loglanır).
///
/// Üç YAZMA (insert) çağıran noktası (tasarım §5 "A → Link" satırı):
///   - <c>SqlDocumentRepository.SaveLinesAsync</c> — teklif→sipariş→irsaliye ticari kalem SourceLineId.
///   - <c>SqlStockDocRepository.ConvertOrderToDeliveryAsync</c> — sipariş→irsaliye (tek sipariş, web).
///   - <c>SqlStockDocRepository.SaveDeliveryFifoAsync</c> — sipariş→irsaliye (çok-sipariş FIFO, mobil).
/// İki TERS ÇEVİRME (reverse) çağıran noktası (KN-3 senkronu):
///   - <c>SqlDocumentRepository.DeleteAsync</c> — ticari belge (teklif/sipariş/talep) soft-delete.
///   - <c>SqlStockDocRepository.DeleteAsync</c> — stok fişi/irsaliye soft-delete.
/// İkisi de AYNI fiziksel <c>dbo.Document</c> tablosuna yazar, ayrı silme kapılarından geçer
/// (bkz. <see cref="FulfillmentLedger"/> sınıf başlığı) — repo genelinde bu tablonun IsActive=0
/// yapıldığı TEK iki nokta bunlardır (2026-07-20 itibarıyla doğrulandı), dolayısıyla link.IsActive
/// senkronu için başka bir üçüncü nokta yoktur.
/// </summary>
internal static class DerivationLinkHelper
{
    /// <summary>
    /// Bir belgenin (<paramref name="documentId"/>) SourceLineId taşıyan TÜM satırlarını okuyup
    /// <c>DocumentLineLink</c>'e <c>LinkType.Derivation</c> (10) olarak paralel yazar — RESET +
    /// REBUILD deseniyle (<c>SqlStockDocRepository.ReconcileOrderSerialsAsync</c>'in "önce bu
    /// belgenin tüm bağlarını sıfırla, sonra güncel kümeden yeniden kur" deseniyle birebir aynı
    /// fikir): önce bu belgeye (<c>TargetDocId=documentId</c>) ait TÜM aktif dönüşüm link'leri
    /// <see cref="IDocumentLineLinkRepository.ReverseByTargetAsync"/> ile pasifleştirilir, SONRA
    /// aşağıda okunan GÜNCEL küme yeniden eklenir.
    ///
    /// Reset ADIMI ZORUNLU — <c>InsertOrAccumulateAsync</c>'i (tek başına) kullanmak burada YANLIŞ
    /// olurdu: <c>SaveLinesAsync</c> aynı belge her yeniden kaydedildiğinde (kullanıcı zaten
    /// dönüştürülmüş bir siparişi tekrar açıp kalem miktarını değiştirip kaydettiğinde) TEKRAR
    /// çağrılır; reset olmadan "biriktir" davranışı Quantity'yi ÜSTE EKLERdi (10 → düzenle 15 →
    /// link 25 olurdu, olması gereken 15 değil). Reset ayrıca "satır kaldırıldı / SourceLineId
    /// temizlendi" durumunu da doğru kapatır: o satır artık aşağıdaki sorguda dönmediğinden pasif
    /// KALIR (yeniden eklenmez) — eski <c>GetDerivedLineAggregatesAsync</c> JOIN'inin artık o
    /// <c>dl</c> satırını hiç görmemesiyle birebir aynı sonuç. Bu iki senaryo olmadan (yalnız
    /// "ilk kayıt" varsayımıyla) link tablosu zamanla eski/şişkin veri biriktirir → "0 uyuşmazlık"
    /// gereksinimini ihlal eder.
    ///
    /// Reset + rebuild ATOMIK DEĞİLDİR (ikisi de kendi ayrı bağlantısını açar, best-effort) — reset
    /// başarılı olup rebuild ortasında bir entry patlarsa belge geçici olarak eksik link'le kalır;
    /// SONRAKİ başarılı <c>SaveLinesAsync</c> çağrısı yine baştan reset+rebuild yapacağından
    /// kendiliğinden düzelir. Bu, B/C'nin de kabul ettiği "best-effort, ana kaydı asla bloklamaz"
    /// toleransıyla aynı sınıf risktir (bkz. sınıf başlığı).
    ///
    /// Okuma (self-join) AMBIENT <paramref name="conn"/>/<paramref name="tx"/> üzerinden yapılır —
    /// <see cref="FulfillmentLedger"/>'daki "C" dersiyle birebir aynı: ayrı bir bağlantıdan okuma,
    /// ana transaction'ın az önce yazdığı/kilitlediği AYNI satırlara karşı DEADLOCK riski taşır.
    /// Tek sorgu hem hedefi (dl.Id/dl.DocumentId/dl.Quantity) hem kaynağın bağlı olduğu belgeyi
    /// (src.DocumentId — SourceDocId) toplu döner; <c>CalibraDatabaseInitializer</c> backfill-A ile
    /// AYNI JOIN şekli (fark: burada tek <paramref name="documentId"/>'ye daraltılır, backfill tüm
    /// tabloyu tarar — bu yüzden backfill'deki <c>td.[IsActive]=1</c> türev-belge filtresi burada
    /// GEREKMEZ: bu metot yalnız o an aktif olarak kaydedilmekte olan bir belgenin satırları için
    /// çağrılır, soft-delete edilmiş bir belge zaten bu kod yoluna hiç girmez).
    ///
    /// Asıl link YAZIMLARI (reset + <see cref="IDocumentLineLinkRepository.InsertOrAccumulateAsync"/>)
    /// kendi bağlantısını açar — ana <paramref name="tx"/>'e GİRMEZ (tasarım §8, WorkOrderService/
    /// FulfillmentLedger ile aynı ilke: link bir ayrı transaction'da, best-effort yazılır).
    /// </summary>
    public static async Task TryLinkDerivedLinesAsync(
        SqlConnection conn, SqlTransaction tx, string schema, int documentId,
        int? userId, IDocumentLineLinkRepository? lineLinks, ILogger? logger, CancellationToken ct)
    {
        if (lineLinks is null) return;
        try
        {
            var lineTable = $"[{schema}].[DocumentLine]";
            var entries = new List<(int TargetLineId, int TargetDocId, int SourceLineId, int SourceDocId, decimal Quantity)>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = $"""
                    SELECT dl.[Id], dl.[DocumentId], dl.[SourceLineId], src.[DocumentId], dl.[Quantity]
                      FROM {lineTable} dl
                      INNER JOIN {lineTable} src ON src.[Id] = dl.[SourceLineId]
                     WHERE dl.[DocumentId] = @DocumentId AND dl.[SourceLineId] IS NOT NULL;
                    """;
                cmd.Parameters.Add(new SqlParameter("@DocumentId", documentId));
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    entries.Add((r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetDecimal(4)));
            }

            // Reset: bu belgeye (TargetDocId=documentId) ait TÜM aktif dönüşüm link'leri sıfırla —
            // entries boş olsa bile çağrılır (tüm SourceLineId'li satırlar kaldırılmış olabilir).
            await lineLinks.ReverseByTargetAsync(documentId, userId, LinkType.Derivation, ct);

            // Rebuild: güncel kümeyi yeniden ekle. Reset'ten hemen sonra olduğundan aktif eşleşme
            // kalmamıştır (InsertOrAccumulateAsync'in UPDATE dalı eşleşmez) — pratikte her satır
            // INSERT olur; yine de InsertOrAccumulateAsync kullanmak WorkOrderService/FulfillmentLedger
            // ile aynı repo metodunu paylaşarak tutarlılığı korur (idempotent-safe).
            foreach (var e in entries)
            {
                await lineLinks.InsertOrAccumulateAsync(new DocumentLineLinkEntry(
                    LinkType.Derivation,
                    SourceLineId: e.SourceLineId,
                    SourceDocId: e.SourceDocId,
                    TargetLineId: e.TargetLineId,
                    TargetDocId: e.TargetDocId,
                    TargetWorkOrderId: null,
                    Quantity: e.Quantity), userId, ct);
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "[DerivationLink] DocumentLineLink insert edilemedi: DocumentId {DocumentId}",
                documentId);
        }
    }

    /// <summary>
    /// Bir türev belgenin (<paramref name="targetDocId"/>) soft-delete/pasifleşmesinde, o belgeye
    /// ait AKTİF dönüşüm (<c>LinkType.Derivation</c>=10) link'lerini pasifleştirir — KN-3 senkronu
    /// ("hedef belge silinince/iptal edilince link IsActive=0 senkronize edilir").
    ///
    /// Yalnız tip-10 FİLTRELİ (bkz. <see cref="IDocumentLineLinkRepository.ReverseByTargetAsync"/>
    /// KDoc'u): aynı <c>Document.Id</c> aynı anda başka bir <c>LinkType</c>'ın da (örn. karşılama
    /// 1-7 — bu belge bir İhtiyaç Kaydı'nı karşılıyorsa) <c>TargetDocId</c>'si olabilir; o link'ler
    /// AYNI silme akışında zaten <see cref="FulfillmentLedger.ReverseByDocumentAsync"/> tarafından
    /// ayrıca ters çevrilir. Tip filtresiz bir reverse burada YANLIŞ olurdu (iki farklı-anlamlı
    /// link kaydını karıştırır, birinin sorumluluğu diğerine taşar).
    ///
    /// Çağrıldığı iki nokta (repo genelinde <c>Document.IsActive</c>'i fiziksel olarak 0 yapan TEK
    /// iki yer — 2026-07-20 itibarıyla doğrulandı): <c>SqlDocumentRepository.DeleteAsync</c>
    /// (ticari belge: teklif/sipariş/talep) ve <c>SqlStockDocRepository.DeleteAsync</c> (stok fişi/
    /// irsaliye). Her iki metotta da mevcut <c>FulfillmentLedger.ReverseByDocumentAsync</c>
    /// çağrısıyla AYNI konumda (commit'ten ÖNCE, best-effort) çağrılır.
    /// </summary>
    public static async Task TryReverseDerivedLinksAsync(
        int targetDocId, int? userId, IDocumentLineLinkRepository? lineLinks, ILogger? logger, CancellationToken ct)
    {
        if (lineLinks is null) return;
        try
        {
            await lineLinks.ReverseByTargetAsync(targetDocId, userId, LinkType.Derivation, ct);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "[DerivationLink] DocumentLineLink reverse edilemedi: TargetDocId {TargetDocId}",
                targetDocId);
        }
    }
}
