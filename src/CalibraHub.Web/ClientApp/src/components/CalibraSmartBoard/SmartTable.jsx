/**
 * SmartTable — CalibraSmartBoard "tablo modu" (viewMode: 'table').
 *
 * SmartBoard, config.viewMode === 'table' geldiginde kart listesi yerine bu
 * bileseni render eder. Ayni entities/masterWidgets/visibleIds/order veriyle
 * calisir — sadece render farklidir ("ayni entity verisi, farkli render").
 *
 * Sutunlar:
 *   [Kod / Ad kimlik sutunu] [widget sutunlari...] [Islem]
 * Widget sutun genisligi resolveChipWidth ile (kart modundaki chip genisligiyle
 * ayni tablo) — boylece basliklar hucrelerle hizali kalir. `columnConfig` prop'u
 * (SmartColumnSettings.jsx'in ürettiği { <id>: {align,width,pin,fontSize,
 * fontWeight,label} } haritası) verilmişse per-sutun override uygulanır —
 * yoksa (kart modu board'ları bu prop'u hiç göndermez) davranış AYNEN eskisi
 * gibi kalır (regresyonsuz).
 *
 * Pin (sabitleme): sabitlenmiş sütunlar listenin başına (kimlik sütunundan
 * hemen sonra) alınır ve sticky-left ile kaydırmada sabit kalır — kimlik
 * sütunu da (Kod/Ad) her zaman sticky-left'tir, aksi halde pin'lenmiş bir
 * sütun kaydırıldığında kimlik bağlamı kaybolurdu.
 *
 * KORU edilen mekanizmalar (SmartBoard seviyesinde zaten calisir, bu bilesen
 * sadece render eder): in-place refresh (onRefresh/recentIds), filter paneli,
 * Excel export, widget config paneli (visibleIds/order), arama, sayfalama/
 * sonsuz kaydirma (append edilen entities otomatik yeni satir olur).
 */
import { useMemo } from 'react'
import SmartTableRow from './SmartTableRow'
import { resolveIcon, resolveChipWidth } from './DynamicWidgetFactory'

var IDENTITY_COL_WIDTH = 280
var ACTION_COL_WIDTH   = 92

function justifyFor(align) {
  return align === 'center' ? 'center' : align === 'right' ? 'flex-end' : 'flex-start'
}

/**
 * Her widget id'si icin butun entity'ler taranarak "her zaman gorunur" ve
 * "guide-list displayScope" bilgisi cikarilir. masterWidgets bu alanlari
 * tasimaz (sadece ilk-gorulen entity instance'inda bulunur) — bkz. SmartCard
 * widgets useMemo'daki ayni mantik (kart bazinda calisir, burada board
 * capinda tek sefer hesaplanir).
 */
function buildWidgetMeta(entities) {
  var meta = {}
  entities.forEach(function (e) {
    if (!e || !Array.isArray(e.widgets)) return
    e.widgets.forEach(function (w) {
      if (!w || !w.id || meta[w.id]) return
      var isGuideList = String(w.dataType || '').toLowerCase() === 'guide-list'
      meta[w.id] = {
        alwaysVisible: w.alwaysVisible === true,
        displayScope: isGuideList
          ? String((w.metadata && w.metadata.displayScope) || 'both').toLowerCase()
          : 'both',
      }
    })
  })
  return meta
}

/**
 * masterWidgets + kullanici tercihleri (visibleIds/order) + widgetMeta'dan
 * kanonik sutun listesini uretir. SmartCard'in kart-bazli "widgets" useMemo'su
 * ile ayni algoritma — burada board capinda TEK sefer calisir (tum satirlar
 * ayni sutunlari kullanmali ki gercek bir tablo hizalamasi olsun).
 *
 * columnConfig verilmisse (SmartColumnSettings) her sutuna { label, align,
 * width, pinned, fontSize, fontWeight } cozumlenmis alanlari eklenir + pin'li
 * sutunlar listenin basina alinir (stabil: kendi aralarindaki sira korunur).
 * Pin'li sutunlar icin kumulatif `stickyLeft` (kimlik sutunundan sonra) da
 * burada hesaplanir — SmartTable/SmartTableRow bu degeri dogrudan render eder.
 */
function computeColumns(masterWidgets, visibleIds, order, widgetMeta, columnConfig) {
  var candidates = masterWidgets.filter(function (w) {
    var m = widgetMeta[w.id]
    return !(m && m.displayScope === 'form')
  })

  var base
  if (!visibleIds && !order) {
    base = candidates
  } else {
    (function () {
      function isAlwaysVisible(w) {
        var m = w && widgetMeta[w.id]
        return !!(m && m.alwaysVisible)
      }

      var map = {}
      candidates.forEach(function (w) { map[w.id] = w })

      var result = []
      var usedIds = {}

      if (order) {
        order.forEach(function (wid) {
          if (visibleIds && visibleIds.indexOf(wid) === -1 && !isAlwaysVisible(map[wid])) return
          if (map[wid]) { result.push(map[wid]); usedIds[wid] = true }
        })
      } else if (visibleIds) {
        candidates.forEach(function (w) {
          if (visibleIds.indexOf(w.id) !== -1 || isAlwaysVisible(w)) { result.push(w); usedIds[w.id] = true }
        })
      }

      candidates.forEach(function (w) {
        if (!w || !w.id || usedIds[w.id]) return
        if (visibleIds && visibleIds.indexOf(w.id) === -1 && !isAlwaysVisible(w)) return
        result.push(w)
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

  var offset = IDENTITY_COL_WIDTH
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
  var onRefresh = typeof props.onRefresh === 'function' ? props.onRefresh : null
  var recentIds = props.recentIds instanceof Set ? props.recentIds : new Set()
  var isDark = !!props.isDark

  var widgetMeta = useMemo(function () { return buildWidgetMeta(entities) }, [entities])
  var columns = useMemo(
    function () { return computeColumns(masterWidgets, visibleIds, order, widgetMeta) },
    [masterWidgets, visibleIds, order, widgetMeta]
  )

  var totalWidth = useMemo(function () {
    var sum = IDENTITY_COL_WIDTH + ACTION_COL_WIDTH
    columns.forEach(function (c) { sum += resolveChipWidth(c.dataType, c.type) })
    return sum
  }, [columns])

  return (
    <div className="cst-root">
      <div className="cst-wrap">
        <table className="cst-table" style={{ width: totalWidth }}>
          <colgroup>
            <col style={{ width: IDENTITY_COL_WIDTH }} />
            {columns.map(function (c) {
              return <col key={c.id} style={{ width: resolveChipWidth(c.dataType, c.type) }} />
            })}
            <col style={{ width: ACTION_COL_WIDTH }} />
          </colgroup>
          <thead>
            <tr>
              <th className="cst-th cst-th--identity">Kod / Ad</th>
              {columns.map(function (c) {
                var Icon = resolveIcon(c.icon, null, c.dataType)
                return (
                  <th key={c.id} className="cst-th" title={c.label}>
                    <span className="cst-th__inner">
                      <Icon size={12} strokeWidth={2} className="cst-th__icon" />
                      <span className="cst-th__label">{c.label}</span>
                    </span>
                  </th>
                )
              })}
              <th className="cst-th cst-th--action">İşlem</th>
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
