/**
 * SmartBoard aksiyon navigasyonu — TEK KAYNAK.
 *
 * Önceden bu mantık SmartBoard.jsx, SmartCard.jsx ve SmartTableRow.jsx içinde
 * ÜÇ KEZ kopyalanmıştı (uzun uyarı yorumlarıyla birlikte). Kural değiştiğinde üçünü
 * birden güncellemek gerekiyordu; bu dosya o kopyayı sonlandırır.
 *
 * ── VARSAYILAN: KAYIT, LİSTENİN ALTINDA ALT SEKMEDE AÇILIR (2026-08-29) ──────────
 * Kullanıcı isteği: "Malzeme ve cari kart ekranlarında olduğu gibi tüm SmartBoard
 * liste ekranlarında da kayıt, liste sekmesinin İÇİNDE yeni sekme olarak açılsın."
 *
 * Eskiden bu davranış yalnızca backend `openInTab: { asChild: true }` gönderen
 * board'larda vardı (Malzeme Kartları, Cari). Diğer board'lar (Satış Siparişi,
 * Teklifler, İrsaliyeler, İş Emirleri…) alanı hiç göndermediği için kayıt ÜST
 * SEVİYE sekmede açılıyordu — aynı işlem ekrandan ekrana farklı davranıyordu.
 *
 * Artık kural şudur: `openInTab` HİÇ verilmemişse ve aksiyonun bir URL'i varsa,
 * gezinme alt sekme (asChild) olarak yapılır. Backend'i her board için tek tek
 * güncellemek yerine varsayılanı burada tanımlamak, gelecekte eklenecek board'ları
 * da otomatik kapsar (unutulan board = eski davranış tuzağı ortadan kalkar).
 *
 * Açıkça `openInTab` gönderen çağrılar KIRILMAZ: matchPath ile üst seviye sekme
 * isteyen ekranlar (ör. "Tekliften Sipariş", "İhtiyaç Karşılama") aynen çalışır.
 * Alt sekme İSTEMEYEN bir aksiyon `openInTab: { asChild: false }` gönderir.
 */

import { deriveMatchPathFromUrl } from '../../utils/workspaceNav'

/**
 * Aksiyonun URL'ine gider. Workspace kabuğu varsa sekme olarak açar, yoksa
 * çağıranın verdiği `fallbackNavigate` ile aynı çerçevede gezinir.
 *
 * @param {object} action   { url, label, openInTab? }
 * @param {object} opts     { defaultTitle, fallbackNavigate }
 * @returns {boolean}       true → sekme açıldı; false → çağıran fallback uygulamalı
 */
export function openActionUrl(action, opts) {
  if (!action || !action.url) return true            // yapacak bir şey yok
  var options = opts || {}
  var tab = action.openInTab

  // openInTab hiç verilmemişse VARSAYILAN alt sekme (bkz. dosya başı).
  var isAsChild
  var explicitMatchPath
  var title
  var parentKey = null

  if (tab) {
    isAsChild = !!tab.asChild
    explicitMatchPath = tab.matchPath
    title = tab.title
    parentKey = tab.parentKey || null
  } else {
    isAsChild = true
    explicitMatchPath = undefined
    title = undefined
  }

  /* matchPath kuralı:
     • asChild=true → HER ZAMAN null. Niyet "her kayıt KENDİ sekmesinde açılsın"dır;
       prefix eşleşmesi olsaydı farklı id'ler aynı sekmeyi ezerdi. Backend'in matchPath
       gönderip göndermediğine de güvenilmez (.NET WhenWritingNull null alanları
       serialize'da düşürebiliyor). Aynı URL ikinci kez tıklanırsa Shell'in exact-URL
       eşleşmesi zaten mevcut sekmeye odaklanır.
     • asChild=false ve matchPath verilmemişse → URL path'inden türetilir ("aynı sayfa
       zaten açıksa oraya git").
     • Açıkça null/false verilmişse → türetme YAPILMAZ (her tıklamada yeni sekme). */
  var matchPath
  if (isAsChild) {
    matchPath = null
  } else if (explicitMatchPath !== undefined) {
    matchPath = explicitMatchPath || null
  } else {
    matchPath = deriveMatchPathFromUrl(action.url)
  }

  try {
    if (window.top && window.top.CalibraHub && typeof window.top.CalibraHub.openWorkspaceTab === 'function') {
      window.top.CalibraHub.openWorkspaceTab({
        url: action.url,
        title: title || options.defaultTitle || action.label || 'Yeni Sekme',
        matchPath: matchPath,
        asChild: isAsChild,
        parentKey: parentKey,
      })
      return true
    }
  } catch (e) { /* cross-origin — aşağıdaki fallback devreye girer */ }

  return false
}
