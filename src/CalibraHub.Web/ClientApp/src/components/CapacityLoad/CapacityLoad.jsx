import { useCallback, useEffect, useMemo, useState } from 'react'
import { Loader2, Gauge } from 'lucide-react'
import * as api from '../../services/capacityLoadService'
import SmartBoardFilterPanel, { entityMatchesFilters } from '../CalibraSmartBoard/SmartBoardFilterPanel'
import CapacityLoadToolbar from './CapacityLoadToolbar'
import CapacityLegend from './CapacityLegend'
import HeatmapGrid from './HeatmapGrid'
import { todayInputValue, localDateInputToUtcIso, minutesToHm } from './capacityLoadUtils'
import './CapacityLoad.css'

var BOARD_KEY = 'production-capacity-load'

/* Filtrelenebilir alanlar — SmartBoardFilterPanel bu sözleşmeyi bekler ({id,label,dataType}).
   Isı haritasında "sütun" zaman kovalarıdır (tarih aralığından türer, kullanıcı seçemez);
   bu yüzden sütun ayarları paneli YOKTUR. Filtreleme ise MAKİNE düzeyindeki toplamlar
   üzerinden anlamlıdır: hangi makineler dolu, hangileri aşımda? */
var MACHINE_FIELDS = [
  { id: 'machineName',    label: 'Makine',            dataType: 'text' },
  { id: 'capacityHours',  label: 'Kapasite (saat)',   dataType: 'numeric' },
  { id: 'loadHours',      label: 'Yük (saat)',        dataType: 'numeric' },
  { id: 'utilizationPct', label: 'Doluluk (%)',       dataType: 'numeric' },
  { id: 'hasOverload',    label: 'Aşım Var',          dataType: 'boolean' },
]

/** Makinenin tüm kovalarını toplayıp dönem geneli özetini çıkarır. */
function summarize(machine) {
  var cap = 0, load = 0, over = 0
  ;(machine.cells || []).forEach(function (c) {
    cap += c.capacityMinutes || 0
    load += c.loadMinutes || 0
    over += c.overloadMinutes || 0
  })
  var pct = cap > 0 ? (load / cap) * 100 : null
  return { capacityMinutes: cap, loadMinutes: load, overloadMinutes: over, utilizationPct: pct }
}

function round1(v) { return v === null || v === undefined ? null : Math.round(v * 10) / 10 }

export default function CapacityLoad() {
  var [bucket, setBucket] = useState('day')
  var [dateFrom, setDateFrom] = useState(todayInputValue(0))
  var [dateTo, setDateTo] = useState(todayInputValue(13))

  var [buckets, setBuckets] = useState([])
  var [machines, setMachines] = useState([])
  var [loading, setLoading] = useState(true)
  var [refreshing, setRefreshing] = useState(false)

  // ── C-Grid araçları ──
  var [search, setSearch] = useState('')
  var [filters, setFilters] = useState([])
  var [filterOpen, setFilterOpen] = useState(false)

  var fetchData = useCallback(function (showSpinner) {
    if (showSpinner) setRefreshing(true)
    var fromIso = localDateInputToUtcIso(dateFrom, false)
    var toIso = localDateInputToUtcIso(dateTo, true)
    return api.getCapacityLoad(fromIso, toIso, bucket)
      .then(function (res) {
        if (!res || !res.ok) {
          window.CalibraHub?.toast?.('Kapasite verisi yüklenemedi.', 'err')
          return
        }
        setBuckets(res.buckets || [])
        setMachines(res.machines || [])
      })
      .catch(function (e) {
        console.error('[CapacityLoad] fetch error:', e)
        window.CalibraHub?.toast?.('Kapasite verisi yüklenirken hata oluştu.', 'err')
      })
      .finally(function () {
        setLoading(false)
        setRefreshing(false)
      })
  }, [dateFrom, dateTo, bucket])

  useEffect(function () {
    setLoading(true)
    fetchData(false)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dateFrom, dateTo, bucket])

  function handleBucketChange(next) {
    if (next === bucket) return
    setBucket(next)
  }

  /* Makineler filtre paneli için entity biçimine çevrilir; SmartBoard ile AYNI
     filtre motoru kullanılır (entityMatchesFilters) — ekrana özel ikinci bir
     filtre uygulaması yazılmaz. */
  var machineEntities = useMemo(function () {
    return machines.map(function (m) {
      var s = summarize(m)
      return {
        id: m.machineId,
        title: m.machineName || '',
        widgets: [
          { id: 'machineName',    label: 'Makine',          dataType: 'text',    value: m.machineName || '' },
          { id: 'capacityHours',  label: 'Kapasite (saat)', dataType: 'numeric', value: round1(s.capacityMinutes / 60) },
          { id: 'loadHours',      label: 'Yük (saat)',      dataType: 'numeric', value: round1(s.loadMinutes / 60) },
          { id: 'utilizationPct', label: 'Doluluk (%)',     dataType: 'numeric', value: round1(s.utilizationPct) },
          { id: 'hasOverload',    label: 'Aşım Var',        dataType: 'boolean', value: s.overloadMinutes > 0 ? 'Evet' : 'Hayır' },
        ],
      }
    })
  }, [machines])

  var visibleMachines = useMemo(function () {
    var q = search.trim().toLocaleLowerCase('tr')
    var byId = {}
    machineEntities.forEach(function (e) { byId[e.id] = e })
    return machines.filter(function (m) {
      if (q && (m.machineName || '').toLocaleLowerCase('tr').indexOf(q) < 0) return false
      if (filters.length > 0 && !entityMatchesFilters(byId[m.machineId], filters)) return false
      return true
    })
  }, [machines, search, filters, machineEntities])

  /* Alt başlık — kaç makine gösteriliyor, dönemin ortalama doluluğu ne?
     Süzülmüşse toplam da yazılır ki kullanıcı "eksik mi görüyorum" diye şüphelenmesin. */
  var subtitle = useMemo(function () {
    var totalCap = 0, totalLoad = 0
    visibleMachines.forEach(function (m) {
      var s = summarize(m)
      totalCap += s.capacityMinutes
      totalLoad += s.loadMinutes
    })
    var pct = totalCap > 0 ? Math.round((totalLoad / totalCap) * 100) : null
    var parts = []
    parts.push(visibleMachines.length + (machines.length !== visibleMachines.length ? ('/' + machines.length) : '') + ' makine')
    parts.push(buckets.length + (bucket === 'week' ? ' hafta' : ' gün'))
    if (pct !== null) parts.push('ortalama doluluk %' + pct)
    return parts.join(' · ')
  }, [visibleMachines, machines, buckets, bucket])

  /** Uzun biçim dışa aktarım: makine × dönem. Analiz için pivot'tan daha kullanışlı. */
  function exportExcel() {
    if (!visibleMachines.length || !buckets.length) return
    var headers = [
      { id: 'machine',   label: 'Makine' },
      { id: 'period',    label: 'Dönem' },
      { id: 'capacity',  label: 'Kapasite (saat)' },
      { id: 'load',      label: 'Yük (saat)' },
      { id: 'util',      label: 'Doluluk (%)' },
      { id: 'overload',  label: 'Aşım (saat)' },
    ]
    var rows = []
    visibleMachines.forEach(function (m) {
      var byKey = {}
      ;(m.cells || []).forEach(function (c) { byKey[c.bucketKey] = c })
      buckets.forEach(function (b) {
        var c = byKey[b.key]
        rows.push({
          machine:  m.machineName || '',
          period:   b.label,
          capacity: c ? round1((c.capacityMinutes || 0) / 60) : 0,
          load:     c ? round1((c.loadMinutes || 0) / 60) : 0,
          util:     c && c.utilizationPct !== null && c.utilizationPct !== undefined ? Math.round(c.utilizationPct) : '',
          overload: c ? round1((c.overloadMinutes || 0) / 60) : 0,
        })
      })
    })

    var ts = new Date()
    var pad = function (x) { return x < 10 ? '0' + x : String(x) }
    var stamp = ts.getFullYear() + pad(ts.getMonth() + 1) + pad(ts.getDate()) + '_' +
                pad(ts.getHours()) + pad(ts.getMinutes())
    var tokenEl = document.querySelector('input[name="__RequestVerificationToken"]')

    var form = document.createElement('form')
    form.method = 'POST'; form.action = '/api/export/smartboard-excel'
    form.target = '_self'; form.style.display = 'none'
    var payload = document.createElement('textarea')
    payload.name = 'payload'
    payload.value = JSON.stringify({
      fileName: 'kapasite-yuk_' + stamp + '.xlsx',
      sheetName: 'Kapasite Yuk',
      headers: headers, rows: rows,
    })
    form.appendChild(payload)
    if (tokenEl && tokenEl.value) {
      var ti = document.createElement('input')
      ti.type = 'hidden'; ti.name = '__RequestVerificationToken'; ti.value = tokenEl.value
      form.appendChild(ti)
    }
    document.body.appendChild(form)
    form.submit()
    setTimeout(function () { try { document.body.removeChild(form) } catch (e) {} }, 1000)
  }

  if (loading) {
    return (
      <div className="cap-root">
        <div className="cap-loading">
          <Loader2 size={20} className="cap-spin" /> Kapasite verisi yükleniyor...
        </div>
      </div>
    )
  }

  var isEmpty = !visibleMachines || visibleMachines.length === 0

  return (
    <div className="cap-root">
      <CapacityLoadToolbar
        bucket={bucket}
        onBucketChange={handleBucketChange}
        dateFrom={dateFrom}
        dateTo={dateTo}
        onDateFromChange={setDateFrom}
        onDateToChange={setDateTo}
        onRefresh={function () { fetchData(true) }}
        refreshing={refreshing}
        search={search}
        onSearchChange={setSearch}
        subtitle={subtitle}
        filterCount={filters.length}
        onOpenFilter={function () { setFilterOpen(true) }}
        onExport={exportExcel}
        exportDisabled={isEmpty}
      />
      <CapacityLegend />
      <div className="cap-body">
        {isEmpty ? (
          <div className="cap-empty">
            <Gauge size={28} />
            <div>
              {machines.length > 0
                ? 'Arama veya filtreye uyan makine yok.'
                : 'Seçilen aralıkta makine veya kapasite verisi bulunamadı.'}
            </div>
          </div>
        ) : (
          <HeatmapGrid buckets={buckets} machines={visibleMachines} />
        )}
      </div>

      <SmartBoardFilterPanel
        isOpen={filterOpen}
        onClose={function () { setFilterOpen(false) }}
        boardKey={BOARD_KEY}
        formCode="CAPACITY_LOAD"
        masterWidgets={MACHINE_FIELDS}
        entities={machineEntities}
        filters={filters}
        onApply={function (next) { setFilters(Array.isArray(next) ? next : []) }}
      />
    </div>
  )
}
