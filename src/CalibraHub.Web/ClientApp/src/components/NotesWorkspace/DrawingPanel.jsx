import { useState, useRef, useEffect, useCallback } from 'react'
import { X, Pen, Eraser, Undo2, Trash2 } from 'lucide-react'
import './DrawingPanel.css'

var COLORS = [
  '#000000','#374151','#6b7280','#d1d5db','#ffffff',
  '#ef4444','#f97316','#eab308','#22c55e','#14b8a6',
  '#3b82f6','#8b5cf6','#ec4899',
]
var SIZES = [2, 5, 10, 20]

export function DrawingPanel({ open, onClose, onInsert }) {
  var [tool, setTool]   = useState('pen')
  var [color, setColor] = useState('#000000')
  var [size, setSize]   = useState(5)
  var isDrawingRef = useRef(false)
  var lastPtRef    = useRef(null)
  var historyRef   = useRef([])
  var canvasRef    = useRef(null)

  useEffect(function () {
    if (!open) return
    var canvas = canvasRef.current
    if (!canvas) return
    var ctx = canvas.getContext('2d')
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
    historyRef.current = []
    isDrawingRef.current = false
  }, [open])

  var getPos = useCallback(function (e) {
    var canvas = canvasRef.current
    var rect = canvas.getBoundingClientRect()
    var scaleX = canvas.width / rect.width
    var scaleY = canvas.height / rect.height
    var src = e.touches ? e.touches[0] : e
    return {
      x: (src.clientX - rect.left) * scaleX,
      y: (src.clientY - rect.top)  * scaleY,
    }
  }, [])

  var saveSnapshot = useCallback(function () {
    var canvas = canvasRef.current
    var ctx = canvas.getContext('2d')
    var snap = ctx.getImageData(0, 0, canvas.width, canvas.height)
    historyRef.current.push(snap)
    if (historyRef.current.length > 40) historyRef.current.shift()
  }, [])

  var onPointerDown = useCallback(function (e) {
    e.preventDefault()
    saveSnapshot()
    var pt = getPos(e)
    lastPtRef.current = pt
    isDrawingRef.current = true
    // draw dot
    var canvas = canvasRef.current
    var ctx = canvas.getContext('2d')
    var r = (tool === 'eraser' ? size * 2.5 : size) / 2
    ctx.beginPath()
    ctx.arc(pt.x, pt.y, r, 0, Math.PI * 2)
    ctx.fillStyle = tool === 'eraser' ? '#ffffff' : color
    ctx.fill()
  }, [tool, color, size, getPos, saveSnapshot])

  var onPointerMove = useCallback(function (e) {
    e.preventDefault()
    if (!isDrawingRef.current) return
    var canvas = canvasRef.current
    var ctx = canvas.getContext('2d')
    var pt = getPos(e)
    ctx.beginPath()
    ctx.moveTo(lastPtRef.current.x, lastPtRef.current.y)
    ctx.lineTo(pt.x, pt.y)
    ctx.strokeStyle = tool === 'eraser' ? '#ffffff' : color
    ctx.lineWidth  = tool === 'eraser' ? size * 5 : size
    ctx.lineCap    = 'round'
    ctx.lineJoin   = 'round'
    ctx.stroke()
    lastPtRef.current = pt
  }, [tool, color, size, getPos])

  var onPointerUp = useCallback(function () {
    isDrawingRef.current = false
  }, [])

  function undo() {
    var snap = historyRef.current.pop()
    if (!snap) return
    canvasRef.current.getContext('2d').putImageData(snap, 0, 0)
  }

  function clear() {
    saveSnapshot()
    var canvas = canvasRef.current
    var ctx = canvas.getContext('2d')
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
  }

  function insertDrawing() {
    var dataUrl = canvasRef.current.toDataURL('image/png')
    onInsert(dataUrl)
    onClose()
  }

  if (!open) return null

  return (
    <div className="dp-backdrop" onClick={function (e) { if (e.target === e.currentTarget) onClose() }}>
      <div className="dp-panel">
        <div className="dp-header">
          <span className="dp-title">Çizim Paneli</span>
          <button className="dp-close" onClick={onClose}><X size={18} /></button>
        </div>

        <div className="dp-toolbar">
          {/* Araç */}
          <div className="dp-tools">
            <button className={'dp-tool' + (tool === 'pen' ? ' dp-tool--active' : '')}
              onClick={function () { setTool('pen') }} title="Kalem">
              <Pen size={15} />
            </button>
            <button className={'dp-tool' + (tool === 'eraser' ? ' dp-tool--active' : '')}
              onClick={function () { setTool('eraser') }} title="Silgi">
              <Eraser size={15} />
            </button>
          </div>

          <div className="dp-sep" />

          {/* Renkler */}
          <div className="dp-colors">
            {COLORS.map(function (c) {
              return (
                <button key={c}
                  className={'dp-color' + (color === c && tool === 'pen' ? ' dp-color--active' : '')}
                  style={{ background: c }}
                  title={c}
                  onClick={function () { setColor(c); setTool('pen') }}
                />
              )
            })}
          </div>

          <div className="dp-sep" />

          {/* Kalınlık */}
          <div className="dp-sizes">
            {SIZES.map(function (s) {
              var dim = Math.min(s * 2.2, 18)
              return (
                <button key={s}
                  className={'dp-size' + (size === s ? ' dp-size--active' : '')}
                  onClick={function () { setSize(s) }}
                  title={s + 'px'}>
                  <span style={{ width: dim, height: dim, borderRadius: '50%', background: 'var(--dp-sub)', display: 'block' }} />
                </button>
              )
            })}
          </div>

          <div className="dp-actions">
            <button className="dp-action" onClick={undo} title="Geri al"><Undo2 size={15} /></button>
            <button className="dp-action" onClick={clear} title="Temizle"><Trash2 size={15} /></button>
          </div>
        </div>

        <div className="dp-canvas-wrap">
          <canvas
            ref={canvasRef}
            width={800} height={500}
            className="dp-canvas"
            style={{ cursor: tool === 'eraser' ? 'cell' : 'crosshair' }}
            onPointerDown={onPointerDown}
            onPointerMove={onPointerMove}
            onPointerUp={onPointerUp}
            onPointerLeave={onPointerUp}
          />
        </div>

        <div className="dp-footer">
          <button className="dp-btn dp-btn--cancel" onClick={onClose}>İptal</button>
          <button className="dp-btn dp-btn--insert" onClick={insertDrawing}>Notaya Ekle</button>
        </div>
      </div>
    </div>
  )
}
