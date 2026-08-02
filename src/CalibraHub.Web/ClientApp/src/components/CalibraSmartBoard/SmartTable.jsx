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
 * fontWeight,label,headerWrap} } haritası) verilmişse per-sutun override uygulanır —
 * yoksa (kart modu board'ları bu prop'u hiç göndermez) davranış AYNEN eskisi
 * gibi kalır (regresyonsuz).
 *
 * `tableFormat` prop'u (SmartColumnSettings.jsx "Genel" bölümünün ürettiği
 * { headerFontSize, bodyFontSize, bodyFontWeight, rowSpacing } — SÜTUN BAZLI
 * DEĞİL, TABLO GENELİNE uygulanır) verilmişse `.cst-root`'a CSS değişkeni
 * (`--cst-head-fs`, `--cst-body-fs`, `--cst-body-fw`, `--cst-row-pad`) olarak
 * yazılır; index.css bunları MEVCUT değerleri fallback vererek tüketir
 * (`var(--cst-body-fs, 12.5px)` gibi).
 * Per-sütun `columnConfig` font override'ı (yukarısı, fontSize/fontWeight)
 * YALNIZCA veri hücresine DOĞRUDAN inline stil olarak yazılır (başlığa DEĞİL —
 * 2026-08-02, bkz. thead render'ı) ve `--cst-body-fs` genel ayarını her zaman
 * EZER — bilinçli ("genel = varsayılan, sütun = istisna"), bkz.
 * SmartTableRow.jsx fontStyleFor. Başlık font boyutu ise SADECE `--cst-head-fs`
 * (Genel → Başlık Font Boyutu) ile yönetilir, per-sütun ayarından etkilenmez.
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
import { useState, useMemo, useEffect } from 'react'
import { ChevronDown } from 'lucide-react'
import SmartTableRow from './SmartTableRow'
import { resolveIcon, resolveChipWidth, formatValue, TABLE_MENU_COL_WIDTH, TABLE_LEAD_WIDGET_IDS } from './DynamicWidgetFactory'

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
      headerWrap: c.headerWrap === true,
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

/* ═══════════════════════════════════════════════════════════════════════
   ÇOK ALANLI GRUPLAMA (2026-07-25, PageComment Seq 36)
   groupBy = [fieldId, ...] verildiginde tablo satirlari cok seviyeli gruplara
   ayrilir; zincir sirasi = kirilim hiyerarsisi. groupBy bos ise bu blok HIC
   calismaz — flat render (regresyonsuz). Gruplama pipeline'in EN SONUNDA:
   entities zaten SmartBoard'da arama+filtre uygulanmis (filteredEntities) +
   kimlik-sentezli (tableEntities) gelir. Server-side sayfali board'larda
   (apiUrl) sadece O AN YUKLU sayfa gruplanir — kabul edilmis V1 siniri.
   ═══════════════════════════════════════════════════════════════════════ */

// Sayisal aile — grup basliklarinin sayisal siralanmasi + panelde "sona alma"
// icin (SmartBoard.groupFields ile ayni tanim). currency/percent dahil.
var GROUP_NUMERIC_DTYPES = { numeric: true, currency: true, percent: true }

// Bir ham degeri kararli bir grup anahtarina + "bos mu" bilgisine cevirir.
// null/undefined/'' (trim sonrasi) → bos grup ("(Boş)"). Diger her sey
// String(value).trim() ile anahtarlanir — sayi 1 ile string '1' ayni gruba
// duser (dedup gosterim degeri uzerinden, kullanicinin bekledigi kirilim).
function normalizeGroupKey(value) {
  if (value === null || value === undefined) return { key: ' empty', empty: true }
  if (typeof value === 'boolean') return { key: value ? 'true' : 'false', empty: false }
  var s = String(value).trim()
  if (s === '') return { key: ' empty', empty: true }
  return { key: 'v:' + s, empty: false }
}

/**
 * entities'i groupChain'e (fieldId dizisi) gore cok seviyeli grup agacina
 * cevirir. fieldMeta[id] = { label, dataType, numeric }. Her dugum:
 *   { path, depth, fieldId, label, displayValue, empty, count, children|rows }
 * Siralama: sayisal alanlar sayisal, digerleri tr-locale alfabetik; bos grup
 * her zaman sona. Deger cikarimi entity->widget map'i uzerinden (bir kez
 * kurulur, O(1) okuma).
 */
function computeGroupTree(entities, groupChain, fieldMeta) {
  // Her entity icin { widgetId: value } haritasini bir kez kur (tekrarli find yerine).
  var valueByEntity = new Map()
  function valuesFor(entity) {
    var m = valueByEntity.get(entity)
    if (m) return m
    m = {}
    var ws = Array.isArray(entity.widgets) ? entity.widgets : []
    for (var i = 0; i < ws.length; i++) {
      var w = ws[i]
      if (w && w.id != null && !(w.id in m)) m[w.id] = w.value
    }
    valueByEntity.set(entity, m)
    return m
  }

  function groupLevel(list, level, parentPath) {
    var fieldId = groupChain[level]
    var meta = fieldMeta[fieldId] || { label: fieldId, dataType: null, numeric: false }
    var buckets = {}
    var seen = []
    for (var i = 0; i < list.length; i++) {
      var e = list[i]
      var raw = valuesFor(e)[fieldId]
      var nk = normalizeGroupKey(raw)
      var b = buckets[nk.key]
      if (!b) {
        b = {
          key: nk.key,
          empty: nk.empty,
          rawSort: raw,
          displayValue: nk.empty ? '(Boş)' : (meta.dataType ? formatValue(raw, meta.dataType) : String(raw)),
          items: [],
        }
        buckets[nk.key] = b
        seen.push(b)
      }
      b.items.push(e)
    }

    seen.sort(function (a, b2) {
      if (a.empty && b2.empty) return 0
      if (a.empty) return 1   // bos grup her zaman sona
      if (b2.empty) return -1
      if (meta.numeric) {
        var na = parseFloat(a.rawSort), nb = parseFloat(b2.rawSort)
        var va = isNaN(na) ? Infinity : na
        var vb = isNaN(nb) ? Infinity : nb
        if (va !== vb) return va - vb
      }
      return String(a.displayValue).localeCompare(String(b2.displayValue), 'tr', { numeric: true, sensitivity: 'base' })
    })

    var isLeaf = (level + 1 >= groupChain.length)
    return seen.map(function (b3) {
      var path = parentPath + '/' + fieldId + '=' + b3.key
      var node = {
        path: path, depth: level, fieldId: fieldId, label: meta.label,
        displayValue: b3.displayValue, empty: b3.empty, count: b3.items.length,
        children: null, rows: null,
      }
      if (isLeaf) node.rows = b3.items
      else node.children = groupLevel(b3.items, level + 1, path)
      return node
    })
  }

  return groupLevel(entities, 0, '')
}

/**
 * Grup agacini render sirasina duzlestirir: her grup basligini ekler; grup
 * KAPALI degilse alt gruplarini (veya yaprak satirlarini) ekler. collapsed =
 * kapali dugum path'lerinin Set'i (varsayilan bos = tumu acik).
 */
function flattenGroups(nodes, collapsed, out) {
  for (var i = 0; i < nodes.length; i++) {
    var node = nodes[i]
    out.push({ kind: 'group', node: node })
    if (!collapsed.has(node.path)) {
      if (node.children) flattenGroups(node.children, collapsed, out)
      else if (node.rows) {
        for (var j = 0; j < node.rows.length; j++) out.push({ kind: 'row', entity: node.rows[j] })
      }
    }
  }
  return out
}

/* ── Grup baslik satiri — tum sutunlari kaplayan tek hucre; ic icerik yatay
   kaydirmada sol kenarda sabit kalir (position:sticky left:0). Girinti derinlige
   gore; chevron acik/kapali durumu gosterir. Hafif (basit div'ler) — birkac bin
   satirda grup basliklari yuk getirmesin. ── */
function GroupHeaderRow(props) {
  var node = props.node
  var collapsed = props.collapsed
  var indent = 12 + node.depth * 18
  return (
    <tr className="cst-group-row">
      <td className="cst-group-cell" colSpan={props.colSpan}>
        <div
          className="cst-group-inner"
          style={{ paddingLeft: indent }}
          onClick={function () { props.onToggle(node.path) }}
          role="button"
          aria-expanded={!collapsed}
          title={node.label + ': ' + node.displayValue}
        >
          <ChevronDown
            size={14}
            className={'cst-group__chevron' + (collapsed ? ' cst-group__chevron--collapsed' : '')}
            strokeWidth={2.2}
          />
          <span className="cst-group__label">{node.label}:</span>
          <span className={'cst-group__value' + (node.empty ? ' cst-group__value--empty' : '')}>{node.displayValue}</span>
          <span className="cst-group__count">{node.count.toLocaleString('tr-TR')} kayıt</span>
        </div>
      </td>
    </tr>
  )
}

export default function SmartTable(props) {
  var entities = Array.isArray(props.entities) ? props.entities : []
  var masterWidgets = Array.isArray(props.masterWidgets) ? props.masterWidgets : []
  var visibleIds = Array.isArray(props.visibleIds) ? props.visibleIds : null
  var order = Array.isArray(props.order) ? props.order : null
  var columnConfig = (props.columnConfig && typeof props.columnConfig === 'object') ? props.columnConfig : null
  // tableFormat — SUTUN BAZLI DEGIL, TABLO GENELİNE uygulanan 4 ayar (Baslik
  // font boyutu + Veri font boyutu/kalinligi + Satir Araligi, kullanici bazinda;
  // bkz. SmartColumnSettings
  // "Genel" bolumu + columnConfigService normalizeColumnConfig `table` alani).
  // columnConfig (per-sutun) ile AYNI "trust upstream normalize" seviyesinde
  // dogrulanir (>0 sayi mi) — asagida CSS degiskeni olarak .cst-root'a yazilir;
  // index.css bunlari var(--cst-*, <mevcut-deger>) fallback ile tuketir.
  var tableFormat = (props.tableFormat && typeof props.tableFormat === 'object') ? props.tableFormat : null
  var onRefresh = typeof props.onRefresh === 'function' ? props.onRefresh : null
  var recentIds = props.recentIds instanceof Set ? props.recentIds : new Set()
  var isDark = !!props.isDark
  // groupBy — SmartBoard'dan (effectiveGroupBy) gelen, gecerli alanlara zaten
  // filtrelenmis fieldId dizisi; yine de burada dizi degilse [] guvencesi.
  var groupByRaw = Array.isArray(props.groupBy) ? props.groupBy : []

  var widgetMeta = useMemo(function () { return buildWidgetMeta(entities) }, [entities])
  var columns = useMemo(
    function () { return computeColumns(masterWidgets, visibleIds, order, widgetMeta, columnConfig) },
    [masterWidgets, visibleIds, order, widgetMeta, columnConfig]
  )

  // ── Gruplama (bkz. dosya ustu blok) ──
  // Grup alani meta'si master listeden (gorunur olsun/olmasin gruplanabilir) +
  // kullanicinin rename ettigi (columnConfig.label) baslik. Sayisal aile
  // siralama icin isaretlenir.
  var groupFieldMeta = useMemo(function () {
    var meta = {}
    masterWidgets.forEach(function (w) {
      if (!w || !w.id) return
      var dt = String(w.dataType || '').toLowerCase()
      var cfg = columnConfig && columnConfig[w.id]
      var label = (cfg && typeof cfg.label === 'string' && cfg.label.trim()) ? cfg.label.trim() : (w.label || w.id)
      meta[w.id] = { label: label, dataType: w.dataType, numeric: !!GROUP_NUMERIC_DTYPES[dt] }
    })
    return meta
  }, [masterWidgets, columnConfig])

  // Sadece meta'da karsiligi olan alan id'leri (stale id ele; sira korunur).
  var groupBy = useMemo(function () {
    return groupByRaw.filter(function (id) { return !!groupFieldMeta[id] })
  }, [groupByRaw, groupFieldMeta])
  var groupSig = groupBy.join('>')

  // Kapali grup path'leri. groupBy degisince sifirla (eski path'ler gecersiz +
  // "tumu acik" varsayilanina don).
  var [collapsed, setCollapsed] = useState(function () { return new Set() })
  useEffect(function () { setCollapsed(new Set()) }, [groupSig])
  function toggleGroup(path) {
    setCollapsed(function (prev) {
      var n = new Set(prev)
      if (n.has(path)) n.delete(path); else n.add(path)
      return n
    })
  }

  // Grup agaci — groupBy bos ise null (flat render). entities zaten arama/filtre
  // sonrasi geldigi icin gruplama pipeline'in en sonunda uygulanir.
  var groupTree = useMemo(function () {
    if (groupBy.length === 0) return null
    return computeGroupTree(entities, groupBy, groupFieldMeta)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [entities, groupSig, groupFieldMeta])

  // Render duzlestirmesi collapse'a bagli (agac degil) — collapse toggle'inda
  // agac yeniden hesaplanmaz, sadece duzlestirme.
  var flatGroupList = useMemo(function () {
    if (!groupTree) return null
    return flattenGroups(groupTree, collapsed, [])
  }, [groupTree, collapsed])

  var totalWidth = useMemo(function () {
    var sum = MENU_COL_WIDTH
    columns.forEach(function (c) { sum += c.width })
    return sum
  }, [columns])

  // rootStyle — sadece kullanici override etmisse CSS degiskeni yazilir;
  // yoksa key hic eklenmez ve index.css'teki var(--cst-*, <fallback>) devreye
  // girer (Otomatik). Sutun bazli fontSize/fontWeight (yalnizca TableValueCell —
  // veri hucresi; th render'ina UYGULANMAZ, 2026-08-02) BUNDAN DAHA SPESIFIK
  // oldugu icin veri hucresinde her zaman kazanir — genel ayar "varsayilan",
  // sutun ayari "istisna" (bilincli, bozma). Baslik (--cst-head-fs) tamamen
  // ayri: per-sutun ayardan etkilenmez.
  var rootStyle = {}
  if (tableFormat) {
    if (typeof tableFormat.headerFontSize === 'number' && tableFormat.headerFontSize > 0) rootStyle['--cst-head-fs'] = tableFormat.headerFontSize + 'px'
    if (typeof tableFormat.bodyFontSize === 'number' && tableFormat.bodyFontSize > 0) rootStyle['--cst-body-fs'] = tableFormat.bodyFontSize + 'px'
    if (typeof tableFormat.bodyFontWeight === 'number' && tableFormat.bodyFontWeight > 0) rootStyle['--cst-body-fw'] = tableFormat.bodyFontWeight
    if (typeof tableFormat.rowSpacing === 'number' && tableFormat.rowSpacing > 0) rootStyle['--cst-row-pad'] = tableFormat.rowSpacing + 'px'
  }

  return (
    <div className="cst-root" style={rootStyle}>
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
                // BAŞLIK FONT'U — per-sütun Boyut/Kalınlık (fontSize/fontWeight)
                // buraya UYGULANMAZ (2026-08-02, kullanıcı isteği: "kolon ayarları
                // başlığa müdahale etmesin"). Başlık font boyutu YALNIZCA "Genel"
                // bölümündeki "Başlık Font Boyutu" (--cst-head-fs) ile yönetilir;
                // per-sütun Boyut/Kalınlık artık SADECE veri hücrelerine
                // (SmartTableRow.jsx fontStyleFor) etki eder.
                return (
                  <th
                    key={c.id}
                    className={'cst-th' + (c.pinned ? ' cst-th--pinned' : '') + (c.headerWrap ? ' cst-th--wrap' : '')}
                    title={c.label}
                    style={thStyle}
                  >
                    <span className="cst-th__inner">
                      <Icon size={12} strokeWidth={2} className="cst-th__icon" />
                      <span className="cst-th__label">{c.label}</span>
                    </span>
                  </th>
                )
              })}
            </tr>
          </thead>
          <tbody>
            {flatGroupList
              ? flatGroupList.map(function (item) {
                  if (item.kind === 'group') {
                    return (
                      <GroupHeaderRow
                        key={'g:' + item.node.path}
                        node={item.node}
                        collapsed={collapsed.has(item.node.path)}
                        colSpan={columns.length + 1}
                        onToggle={toggleGroup}
                      />
                    )
                  }
                  return (
                    <SmartTableRow
                      key={'r:' + item.entity.id}
                      entity={item.entity}
                      columns={columns}
                      onRefresh={onRefresh}
                      isHighlighted={recentIds.has(item.entity.id)}
                      isDark={isDark}
                    />
                  )
                })
              : entities.map(function (entity) {
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
                })
            }
          </tbody>
        </table>
      </div>
    </div>
  )
}
