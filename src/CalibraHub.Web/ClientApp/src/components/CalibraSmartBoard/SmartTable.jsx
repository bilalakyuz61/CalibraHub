/**
 * SmartTable — CalibraSmartBoard "tablo modu" (viewMode: 'table').
 *
 * SmartBoard, config.viewMode === 'table' geldiginde kart listesi yerine bu
 * bileseni render eder. Ayni entities/masterWidgets/visibleIds/order veriyle
 * calisir — sadece render farklidir ("ayni entity verisi, farkli render").
 *
 * Sutunlar (2026-07-16 revizyon 4 — kompozit kimlik sutunu kaldirildi):
 *   [Islemler (basliksiz kebab)] [widget sutunlari... (Stok Kodu + Stok Adi
 *   dahil, normal sutun olarak)]
 * Onceki "Kod/Ad" ozel kimlik hucresi TAMAMEN KALKTI — Stok Kodu (w_kod) ve
 * Stok Adi (w_ad) artik DIGER TUM sutunlarla AYNI pipeline'dan geçen normal
 * widget sutunlari (align/width/pin/font/rename/sirala hepsi kullanilabilir).
 * Bkz. DynamicWidgetFactory.js TABLE_LEAD_WIDGET_IDS.
 *
 * Widget sutun genisligi resolveChipWidth ile (kart modundaki chip genisligiyle
 * ayni tablo) — boylece basliklar hucrelerle hizali kalir. `columnConfig` prop'u
 * (SmartColumnSettings.jsx'in ürettiği { <id>: {align,width,pin,fontSize,
 * fontWeight,label} } haritası) verilmişse per-sutun override uygulanır —
 * yoksa (kart modu board'ları bu prop'u hiç göndermez) davranış AYNEN eskisi
 * gibi kalır (regresyonsuz).
 *
 * Sabit sol blok — sadece Islemler, sticky-left (0'da). Pin'li veri sutunlari
 * bu sutundan hemen sonra baslar. Opaklik/z-index: bkz. index.css
 * ".cst-td--menu/--pinned" — sticky hucreler TAM OPAK arka plan
 * (`--cst-sticky-bg`) tasir ki kaydirilan veri sutunlari altlarindan gecerken
 * seffaflik/overlap gorunmesin.
 *
 * Satır aksiyonları (SmartTableRow icinde render edilir, bkz. o dosyanin ustu):
 *   - "İşlemler" menüsü (primaryAction + entity.extraActions[] + en altta Sil)
 *     → tek sabit sutunda (kebab buton + dropdown). Sil, secondaryAction'in
 *     mevcut onay-modal akisini AYNEN tetikler.
 *   - Satır tıklaması (kimlik hucresi kalktigi icin artik TUM SATIR) →
 *     Duzenle (primaryAction) — bkz. SmartTableRow.
 *
 * Gorunurluk semantigi (revizyon 3'ten devam, revizyon 4'te fallback eklendi):
 *   - visibleIds === null (config hic yok, ilk kullanim) → tum sutunlar
 *     gorunur; Stok Kodu/Stok Adi (varsa) DOGAL SIRANIN BASINA alinir (ilk
 *     iki sutun), gerisi dogal sirada — leadsFirst().
 *   - visibleIds = [...] (kullanici secim yapmis) → sadece secilenler,
 *     "alwaysVisible" gibi bir zorunlu-gosterim bypass'i YOK.
 *   - visibleIds = [] VE filtre sonucu SIFIR sutun kaldiysa (kullanici
 *     bilincli olarak TUM sutunlari kaldirmis) → Stok Kodu + Stok Adi (varsa)
 *     fallback olarak gosterilir — asla tamamen bos/anlamsiz tablo olmaz.
 *
 * KORU edilen mekanizmalar (SmartBoard seviyesinde zaten calisir, bu bilesen
 * sadece render eder): in-place refresh (onRefresh/recentIds), filter paneli,
 * Excel export, widget config paneli (visibleIds/order), arama, sayfalama/
 * sonsuz kaydirma (append edilen entities otomatik yeni satir olur).
 */
import { useMemo } from 'react'
import SmartTableRow from './SmartTableRow'
import { resolveIcon, resolveChipWidth, TABLE_MENU_COL_WIDTH, TABLE_LEAD_WIDGET_IDS } from './DynamicWidgetFactory'

// MENU_COL_WIDTH, DynamicWidgetFactory.js'ten gelir (SmartTableRow de ayni
// sabiti kullanir; iki dosyanin birbirini import etmesi/dongusel import
// yerine ortak kaynaktan paylasilir).
var MENU_COL_WIDTH = TABLE_MENU_COL_WIDTH

/**
 * Her widget id'si icin butun entity'ler taranarak "guide-list displayScope"
 * bilgisi cikarilir. masterWidgets bu alani tasimaz (sadece ilk-gorulen
 * entity instance'inda bulunur) — bkz. SmartCard widgets useMemo'daki ayni
 * mantik (kart bazinda calisir, burada board capinda tek sefer hesaplanir).
 * NOT: "alwaysVisible" burada okunmuyor — tablo modunda sistem-widget
 * zorunlu-gorunurluk bypass'i yok, bkz. dosya ustu not.
 */
function buildWidgetMeta(entities) {
  var meta = {}
  entities.forEach(function (e) {
    if (!e || !Array.isArray(e.widgets)) return
    e.widgets.forEach(function (w) {
      if (!w || !w.id || meta[w.id]) return
      var isGuideList = String(w.dataType || '').toLowerCase() === 'guide-list'
      meta[w.id] = {
        displayScope: isGuideList
          ? String((w.metadata && w.metadata.displayScope) || 'both').toLowerCase()
          : 'both',
      }
    })
  })
  return meta
}

/**
 * candidates listesini TABLE_LEAD_WIDGET_IDS sirasina gore basa alir (varsa) —
 * "config hic yokken varsayilan sira" ve "bos secim fallback'i" ikisi de bunu
 * kullanir. Lead id'ler candidates'ta yoksa (ör. admin Kod/Ad'i custom widget'a
 * maplemis) sessizce atlanir — hicbir zaman crash/hayalet sutun uretmez.
 */
function leadsFirst(list) {
  var byId = {}
  list.forEach(function (w) { if (w && w.id) byId[w.id] = w })
  var lead = []
  var leadIdSet = {}
  TABLE_LEAD_WIDGET_IDS.forEach(function (lid) {
    if (byId[lid]) { lead.push(byId[lid]); leadIdSet[lid] = true }
  })
  var rest = list.filter(function (w) { return w && w.id && !leadIdSet[w.id] })
  return lead.concat(rest)
}

/**
 * masterWidgets + kullanici tercihleri (visibleIds/order) + widgetMeta'dan
 * kanonik sutun listesini uretir. Burada board capinda TEK sefer calisir
 * (tum satirlar ayni sutunlari kullanmali ki gercek bir tablo hizalamasi
 * olsun).
 *
 * columnConfig verilmisse (SmartColumnSettings) her sutuna { label, align,
 * width, pinned, fontSize, fontWeight } cozumlenmis alanlari eklenir + pin'li
 * sutunlar listenin basina alinir (stabil: kendi aralarindaki sira korunur).
 * Pin'li sutunlar icin kumulatif `stickyLeft` (Islemler sutunundan sonra) da
 * burada hesaplanir — SmartTable/SmartTableRow bu degeri dogrudan render eder.
 */
function computeColumns(masterWidgets, visibleIds, order, widgetMeta, columnConfig) {
  var candidates = masterWidgets.filter(function (w) {
    var m = widgetMeta[w.id]
    return !(m && m.displayScope === 'form')
  })

  var base
  if (!visibleIds && !order) {
    // Config hic yok (ilk kullanim) — tum sutunlar gorunur; Stok Kodu/Stok
    // Adi (varsa) ilk iki sirada, gerisi dogal sirada.
    base = leadsFirst(candidates)
  } else {
    (function () {
      var map = {}
      candidates.forEach(function (w) { map[w.id] = w })

      var result = []
      var usedIds = {}

      // Kural: sadece visibleIds'te olan gorunur — "alwaysVisible" gibi bir
      // zorunlu-gosterim bypass'i YOK. visibleIds bos dizi ise (uzunluk 0)
      // hicbir aday burada eklenmez (asagidaki dongulerin hicbiri push etmez).
      if (order) {
        order.forEach(function (wid) {
          if (visibleIds && visibleIds.indexOf(wid) === -1) return
          if (map[wid]) { result.push(map[wid]); usedIds[wid] = true }
        })
      } else if (visibleIds) {
        candidates.forEach(function (w) {
          if (visibleIds.indexOf(w.id) !== -1) { result.push(w); usedIds[w.id] = true }
        })
      }

      candidates.forEach(function (w) {
        if (!w || !w.id || usedIds[w.id]) return
        if (visibleIds && visibleIds.indexOf(w.id) === -1) return
        result.push(w)
      })

      // Kullanici bilincli olarak TUM sutunlari kaldirmis (visibleIds sonucu
      // sifir eslesme) — tamamen bos/anlamsiz tablo yerine Stok Kodu + Stok
      // Adi (varsa) fallback'i.
      if (result.length === 0) result = leadsFirst(candidates).filter(function (w) {
        return TABLE_LEAD_WIDGET_IDS.indexOf(w.id) !== -1
      })

      base = result
    })()
  }

  var cfg = (columnConfig && typeof columnConfig === 'object') ? columnConfig : null
  if (!cfg) {
    // columnConfig yok (kart modu board'lari bu prop'u hic gondermez) — eski
    // davranis AYNEN: enrich/partition atlanir, sadece dataType/type'a gore
    // dogal genislik ekiplenir (render tarafinin tek bir sekle guvenmesi icin).
    return base.map(function (w) {
      return Object.assign({}, w, { align: 'left', width: resolveChipWidth(w.dataType, w.type), pinned: false })
    })
  }

  var enriched = base.map(function (w) {
    var c = cfg[w.id] || {}
    return Object.assign({}, w, {
      label: (typeof c.label === 'string' && c.label.trim()) ? c.label : w.label,
      align: (c.align === 'center' || c.align === 'right') ? c.align : 'left',
      width: (typeof c.width === 'number' && c.width > 0) ? c.width : resolveChipWidth(w.dataType, w.type),
      pinned: c.pin === true,
      fontSize: (typeof c.fontSize === 'number' && c.fontSize > 0) ? c.fontSize : null,
      fontWeight: (typeof c.fontWeight === 'number' && c.fontWeight > 0) ? c.fontWeight : null,
    })
  })

  var pinned = enriched.filter(function (c) { return c.pinned })
  var unpinned = enriched.filter(function (c) { return !c.pinned })
  var ordered = pinned.concat(unpinned)

  var offset = MENU_COL_WIDTH
  ordered.forEach(function (c) {
    if (c.pinned) { c.stickyLeft = offset; offset += c.width }
  })

  return ordered
}

export default function SmartTable(props) {
  var entities = Array.isArray(props.entities) ? props.entities : []
  var masterWidgets = Array.isArray(props.masterWidgets) ? props.masterWidgets : []
  var visibleIds = Array.isArray(props.visibleIds) ? props.visibleIds : null
  var order = Array.isArray(props.order) ? props.order : null
  var columnConfig = (props.columnConfig && typeof props.columnConfig === 'object') ? props.columnConfig : null
  var onRefresh = typeof props.onRefresh === 'function' ? props.onRefresh : null
  var recentIds = props.recentIds instanceof Set ? props.recentIds : new Set()
  var isDark = !!props.isDark

  var widgetMeta = useMemo(function () { return buildWidgetMeta(entities) }, [entities])
  var columns = useMemo(
    function () { return computeColumns(masterWidgets, visibleIds, order, widgetMeta, columnConfig) },
    [masterWidgets, visibleIds, order, widgetMeta, columnConfig]
  )

  var totalWidth = useMemo(function () {
    var sum = MENU_COL_WIDTH
    columns.forEach(function (c) { sum += c.width })
    return sum
  }, [columns])

  return (
    <div className="cst-root">
      <div className="cst-wrap">
        <table className="cst-table" style={{ width: totalWidth }}>
          <colgroup>
            <col style={{ width: MENU_COL_WIDTH }} />
            {columns.map(function (c) {
              return <col key={c.id} style={{ width: c.width }} />
            })}
          </colgroup>
          <thead>
            <tr>
              <th className="cst-th cst-th--menu" aria-label="İşlemler" />
              {columns.map(function (c) {
                var Icon = resolveIcon(c.icon, null, c.dataType)
                var thStyle = { textAlign: c.align }
                if (c.pinned) thStyle.left = c.stickyLeft
                var labelStyle = {}
                if (c.fontSize) labelStyle.fontSize = c.fontSize + 'px'
                if (c.fontWeight) labelStyle.fontWeight = c.fontWeight
                return (
                  <th
                    key={c.id}
                    className={'cst-th' + (c.pinned ? ' cst-th--pinned' : '')}
                    title={c.label}
                    style={thStyle}
                  >
                    <span className="cst-th__inner">
                      <Icon size={12} strokeWidth={2} className="cst-th__icon" />
                      <span className="cst-th__label" style={labelStyle}>{c.label}</span>
                    </span>
                  </th>
                )
              })}
            </tr>
          </thead>
          <tbody>
            {entities.map(function (entity) {
              return (
                <SmartTableRow
                  key={entity.id}
                  entity={entity}
                  columns={columns}
                  onRefresh={onRefresh}
                  isHighlighted={recentIds.has(entity.id)}
                  isDark={isDark}
                />
              )
            })}
          </tbody>
        </table>
      </div>
    </div>
  )
}
