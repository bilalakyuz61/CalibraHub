import { useCallback, useEffect, useState } from 'react'
import { Loader2, Gauge } from 'lucide-react'
import * as api from '../../services/capacityLoadService'
import CapacityLoadToolbar from './CapacityLoadToolbar'
import HeatmapGrid from './HeatmapGrid'
import { todayInputValue, localDateInputToUtcIso } from './capacityLoadUtils'
import './CapacityLoad.css'

export default function CapacityLoad() {
  var [bucket, setBucket] = useState('day')
  var [dateFrom, setDateFrom] = useState(todayInputValue(0))
  var [dateTo, setDateTo] = useState(todayInputValue(13))

  var [buckets, setBuckets] = useState([])
  var [machines, setMachines] = useState([])
  var [loading, setLoading] = useState(true)
  var [refreshing, setRefreshing] = useState(false)

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

  if (loading) {
    return (
      <div className="cap-root">
        <div className="cap-loading">
          <Loader2 size={20} className="cap-spin" /> Kapasite verisi yükleniyor...
        </div>
      </div>
    )
  }

  var isEmpty = !machines || machines.length === 0

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
      />
      <div className="cap-body">
        {isEmpty ? (
          <div className="cap-empty">
            <Gauge size={28} />
            <div>Seçilen aralıkta makine veya kapasite verisi bulunamadı.</div>
          </div>
        ) : (
          <HeatmapGrid buckets={buckets} machines={machines} />
        )}
      </div>
    </div>
  )
}
