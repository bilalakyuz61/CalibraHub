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
 * ayni tablo) — boylece basliklar hucrelerle hizali kalir.
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
 */
function computeColumns(masterWidgets, visibleIds, order, widgetMeta) {
  var candidates = masterWidgets.filter(function (w) {
    var m = widgetMeta[w.id]
    return !(m && m.displayScope === 'form')
  })

  if (!visibleIds && !order) return candidates

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

  return result
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
