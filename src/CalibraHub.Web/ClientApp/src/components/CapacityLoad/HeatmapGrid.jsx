import { AlertTriangle } from 'lucide-react'
import { utilizationBand, formatPct, cellTooltip } from './capacityLoadUtils'

var BUCKET_COL_WIDTH = 68
var MACHINE_COL_WIDTH = 200

export default function HeatmapGrid({ buckets, machines }) {
  if (!machines || machines.length === 0) return null

  var gridTemplateColumns = MACHINE_COL_WIDTH + 'px ' +
    buckets.map(function () { return BUCKET_COL_WIDTH + 'px' }).join(' ')

  return (
    <div className="cap-grid-scroll">
      <div className="cap-grid" style={{ gridTemplateColumns: gridTemplateColumns }}>
        <div className="cap-cell cap-cell--corner cap-cell--sticky-both">Makine</div>
        {buckets.map(function (b) {
          return (
            <div key={b.key} className="cap-cell cap-cell--bucket-header cap-cell--sticky-top" title={b.label}>
              {b.label}
            </div>
          )
        })}

        {machines.map(function (m) {
          var cellByKey = {}
          ;(m.cells || []).forEach(function (c) { cellByKey[c.bucketKey] = c })
          return (
            <FragmentRow key={m.machineId} machine={m} buckets={buckets} cellByKey={cellByKey} />
          )
        })}
      </div>
    </div>
  )
}

function FragmentRow({ machine, buckets, cellByKey }) {
  return (
    <>
      <div className="cap-cell cap-cell--machine-name cap-cell--sticky-left" title={machine.machineName}>
        {machine.machineName}
      </div>
      {buckets.map(function (b) {
        var cell = cellByKey[b.key]
        var band = utilizationBand(cell ? cell.utilizationPct : null)
        var isOverload = cell && cell.overloadMinutes > 0
        return (
          <div
            key={b.key}
            className={'cap-cell cap-cell--data cap-cell--' + band}
            title={cellTooltip(cell)}
          >
            <span className="cap-cell-pct">{cell ? formatPct(cell.utilizationPct) : '—'}</span>
            {isOverload && <AlertTriangle size={11} className="cap-cell-overload-icon" />}
          </div>
        )
      })}
    </>
  )
}
