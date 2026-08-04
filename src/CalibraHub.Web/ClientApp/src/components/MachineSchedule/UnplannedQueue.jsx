import { useMemo, useState } from 'react'
import { useDraggable } from '@dnd-kit/core'
import { ListTodo, Search, Clock, Layers } from 'lucide-react'

function OpCard({ op }) {
  var { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: 'unplanned-' + op.workOrderOperationId,
    data: { kind: 'unplanned', op: op },
  })

  return (
    <div
      ref={setNodeRef}
      className={'ms-op-card' + (isDragging ? ' ms-op-card--dragging' : '')}
      {...listeners}
      {...attributes}
    >
      <div className="ms-op-wo">{op.workOrderNo}</div>
      <div className="ms-op-item" title={op.itemName}>{op.itemName}</div>
      <div className="ms-op-meta">
        <span className="ms-op-badge"><Layers size={11} /> {op.operationName}</span>
        {op.estimatedMinutes ? (
          <span className="ms-op-badge"><Clock size={11} /> {op.estimatedMinutes} dk</span>
        ) : null}
      </div>
    </div>
  )
}

/**
 * OpDragGhost — DragOverlay içinde imleci takip eden klon kart (OrgChart deseni).
 */
export function OpDragGhost({ op }) {
  if (!op) return null
  return (
    <div className="ms-op-card ms-op-card--ghost">
      <div className="ms-op-wo">{op.workOrderNo}</div>
      <div className="ms-op-item">{op.itemName}</div>
      <div className="ms-op-meta">
        <span className="ms-op-badge"><Layers size={11} /> {op.operationName}</span>
      </div>
    </div>
  )
}

export default function UnplannedQueue({ unplanned, loading }) {
  var [search, setSearch] = useState('')

  var filtered = useMemo(function () {
    var q = search.trim().toLowerCase()
    if (!q) return unplanned
    return unplanned.filter(function (op) {
      return (op.workOrderNo || '').toLowerCase().indexOf(q) !== -1 ||
        (op.itemName || '').toLowerCase().indexOf(q) !== -1 ||
        (op.operationName || '').toLowerCase().indexOf(q) !== -1
    })
  }, [unplanned, search])

  return (
    <div className="ms-queue">
      <div className="ms-queue-header">
        <div className="ms-queue-title">
          <ListTodo size={15} /> Planlanmamış Operasyonlar
          <span className="ms-queue-count">{unplanned.length}</span>
        </div>
        <div className="ms-search">
          <Search size={13} />
          <input
            placeholder="İş emri / ürün / operasyon ara..."
            value={search}
            onChange={function (e) { setSearch(e.target.value) }}
          />
        </div>
      </div>
      <div className="ms-queue-list">
        {loading ? (
          <div className="ms-queue-empty">Yükleniyor...</div>
        ) : filtered.length === 0 ? (
          <div className="ms-queue-empty">
            {unplanned.length === 0 ? 'Planlanacak operasyon yok.' : 'Sonuç bulunamadı.'}
          </div>
        ) : (
          filtered.map(function (op) {
            return <OpCard key={op.workOrderOperationId} op={op} />
          })
        )}
      </div>
    </div>
  )
}
