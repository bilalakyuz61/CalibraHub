import { useRef, useCallback, useState, useEffect } from 'react'
import { createPortal } from 'react-dom'
import { NodeViewWrapper } from '@tiptap/react'
import { Pen, Eraser, Undo2, Trash2, Maximize2, Minimize2 } from 'lucide-react'
import './DrawingNodeView.css'

var COLORS = [
  '#000000','#374151','#6b7280','#d1d5db','#ffffff',
  '#ef4444','#f97316','#eab308','#22c55e','#14b8a6',
  '#3b82f6','#8b5cf6','#ec4899',
]
var SIZES = [2, 5, 10, 20]
var MIN_H = 180
var MAX_H = 1400

export function DrawingNodeView({ node, updateAttributes, deleteNode }) {
  var [tool,          setTool]          = useState('pen')
  var [color,         setColor]         = useState('#000000')
  var [size,          setSize]          = useState(5)
  var [saved,         setSaved]         = useState(!!node.attrs.snapshot)
  var [displayH,      setDisplayH]      = useState(node.attrs.height || 460)
  var [fullscreen,    setFullscreen]    = useState(false)
  var [confirmDelete, setConfirmDelete] = useState(false)

  /* ── iki ayrı canvas: biri editörde, biri portal'da ── */
  var embeddedRef        = useRef(null)
  var fsRef              = useRef(null)
  var embInitializedRef  = useRef(false)
  var fsInitializedRef   = useRef(false)

  var isDrawingRef   = useRef(false)
  var lastPtRef      = useRef(null)
  var historyRef     = useRef([])
  var saveTimerRef   = useRef(null)
  var savedTimerRef  = useRef(null)
  var heightRef      = useRef(node.attrs.height || 460)

  /* ── Embedded canvas — mount'ta boyut + snapshot ── */
  var embeddedRefCb = useCallback(function (canvas) {
    embeddedRef.current = canvas
    if (!canvas || embInitializedRef.current) return
    embInitializedRef.current = true

    var h = node.attrs.height || 460
    canvas.height = h
    heightRef.current = h

    var ctx = canvas.getContext('2d')
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width, h)

    if (node.attrs.snapshot) {
      var img = new window.Image()
      img.onload = function () { ctx.drawImage(img, 0, 0) }
      img.src = node.attrs.snapshot
    }
  }, [])

  /* ── Fullscreen canvas — embedded içeriğini kopyala ── */
  var fsCanvasRefCb = useCallback(function (canvas) {
    fsRef.current = canvas
    if (!canvas || fsInitializedRef.current) return
    fsInitializedRef.current = true

    var emb = embeddedRef.current
    if (!emb) return

    canvas.width  = emb.width
    canvas.height = emb.height

    var ctx = canvas.getContext('2d')
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
    ctx.drawImage(emb, 0, 0)

    historyRef.current = []
  }, [])

  /* ── Esc ile çık + focus-mode event dinle ── */
  useEffect(function () {
    function onKey(e) {
      if (e.key === 'Escape') {
        if (confirmDelete) { setConfirmDelete(false); return }
        if (fullscreen) exitFullscreen()
      }
    }
    window.addEventListener('keydown', onKey)
    return function () { window.removeEventListener('keydown', onKey) }
  }, [fullscreen, confirmDelete])

  function openFullscreen() {
    fsInitializedRef.current = false
    setFullscreen(true)
  }

  function exitFullscreen() {
    var emb = embeddedRef.current
    var fs  = fsRef.current
    if (emb && fs) {
      if (emb.height !== fs.height) {
        emb.height = fs.height
        heightRef.current = fs.height
        setDisplayH(fs.height)
      }
      var ctx = emb.getContext('2d')
      ctx.fillStyle = '#ffffff'
      ctx.fillRect(0, 0, emb.width, emb.height)
      ctx.drawImage(fs, 0, 0)
    }
    fsInitializedRef.current = false
    setFullscreen(false)
    scheduleSnapshot()
  }

  /* ── Snapshot kaydet (debounce) ── */
  function scheduleSnapshot() {
    clearTimeout(saveTimerRef.current)
    setSaved(false)
    saveTimerRef.current = setTimeout(function () {
      var canvas = fullscreen ? fsRef.current : embeddedRef.current
      if (!canvas) return
      var dataUrl = canvas.toDataURL('image/png')
      updateAttributes({ snapshot: dataUrl, height: heightRef.current })
      setSaved(true)
      clearTimeout(savedTimerRef.current)
      savedTimerRef.current = setTimeout(function () { setSaved(false) }, 2000)
    }, 500)
  }

  /* ── Koordinat (aktif canvas'a göre) ── */
  function getPosPx(e, canvas) {
    var rect = canvas.getBoundingClientRect()
    var src  = e.touches ? e.touches[0] : e
    return {
      x: (src.clientX - rect.left) * (canvas.width  / rect.width),
      y: (src.clientY - rect.top)  * (canvas.height / rect.height),
    }
  }

  function getActiveCanvas() {
    return fullscreen ? fsRef.current : embeddedRef.current
  }

  function saveSnapshot() {
    var canvas = getActiveCanvas()
    if (!canvas) return
    var snap = canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height)
    historyRef.current.push(snap)
    if (historyRef.current.length > 40) historyRef.current.shift()
  }

  /* ── Çizim ── */
  function onPointerDown(e) {
    e.preventDefault()
    var canvas = getActiveCanvas()
    if (!canvas) return
    saveSnapshot()
    var pt  = getPosPx(e, canvas)
    lastPtRef.current    = pt
    isDrawingRef.current = true
    var ctx = canvas.getContext('2d')
    var r   = (tool === 'eraser' ? size * 2.5 : size) / 2
    ctx.beginPath(); ctx.arc(pt.x, pt.y, r, 0, Math.PI * 2)
    ctx.fillStyle = tool === 'eraser' ? '#ffffff' : color
    ctx.fill()
  }

  function onPointerMove(e) {
    e.preventDefault()
    if (!isDrawingRef.current) return
    var canvas = getActiveCanvas()
    if (!canvas) return
    var ctx = canvas.getContext('2d')
    var pt  = getPosPx(e, canvas)
    ctx.beginPath()
    ctx.moveTo(lastPtRef.current.x, lastPtRef.current.y)
    ctx.lineTo(pt.x, pt.y)
    ctx.strokeStyle = tool === 'eraser' ? '#ffffff' : color
    ctx.lineWidth   = tool === 'eraser' ? size * 5 : size
    ctx.lineCap = 'round'; ctx.lineJoin = 'round'
    ctx.stroke()
    lastPtRef.current = pt
  }

  function onPointerUp() {
    if (!isDrawingRef.current) return
    isDrawingRef.current = false
    scheduleSnapshot()
  }

  function undo() {
    var snap = historyRef.current.pop()
    if (!snap) return
    getActiveCanvas()?.getContext('2d').putImageData(snap, 0, 0)
    scheduleSnapshot()
  }

  function clear() {
    saveSnapshot()
    var canvas = getActiveCanvas()
    if (!canvas) return
    var ctx = canvas.getContext('2d')
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, canvas.width, canvas.height)
    scheduleSnapshot()
  }

  /* ── Yükseklik resize handle (sadece embedded modda) ── */
  function onResizeStart(e) {
    e.preventDefault()
    var startY = e.clientY
    var startH = heightRef.current

    function onMove(ev) {
      var newH = Math.max(MIN_H, Math.min(MAX_H, startH + (ev.clientY - startY)))
      setDisplayH(newH)
      heightRef.current = newH
    }

    function onUp() {
      window.removeEventListener('pointermove', onMove)
      window.removeEventListener('pointerup',   onUp)
      var canvas = embeddedRef.current
      if (!canvas || canvas.height === heightRef.current) return
      var ctx    = canvas.getContext('2d')
      var saved  = ctx.getImageData(0, 0, canvas.width, canvas.height)
      canvas.height = heightRef.current
      ctx.fillStyle = '#ffffff'
      ctx.fillRect(0, 0, canvas.width, canvas.height)
      ctx.putImageData(saved, 0, 0)
      scheduleSnapshot()
    }

    window.addEventListener('pointermove', onMove)
    window.addEventListener('pointerup',   onUp)
  }

  /* ── Toolbar içeriği (render helper — component değil) ── */
  function toolbarItems(inFullscreen) {
    return (
      <>
        <div className="dn-tools">
          <button className={'dn-tool' + (tool === 'pen'    ? ' dn-tool--active' : '')}
            onClick={function () { setTool('pen') }} title="Kalem"><Pen size={14} /></button>
          <button className={'dn-tool' + (tool === 'eraser' ? ' dn-tool--active' : '')}
            onClick={function () { setTool('eraser') }} title="Silgi"><Eraser size={14} /></button>
        </div>
        <div className="dn-sep" />
        <div className="dn-colors">
          {COLORS.map(function (c) {
            return (
              <button key={c}
                className={'dn-color' + (color === c && tool === 'pen' ? ' dn-color--active' : '')}
                style={{ background: c }} title={c}
                onClick={function () { setColor(c); setTool('pen') }} />
            )
          })}
        </div>
        <div className="dn-sep" />
        <div className="dn-sizes">
          {SIZES.map(function (s) {
            var dim = Math.min(s * 2.2, 18)
            return (
              <button key={s}
                className={'dn-size' + (size === s ? ' dn-size--active' : '')}
                onClick={function () { setSize(s) }} title={s + 'px'}>
                <span style={{ width: dim, height: dim, borderRadius: '50%', background: 'var(--dn-size-dot)', display: 'block' }} />
              </button>
            )
          })}
        </div>
        <div className="dn-actions">
          <button className="dn-action" onClick={undo}  title="Geri al"><Undo2  size={14} /></button>
          <button className="dn-action" onClick={clear} title="Temizle"><Trash2 size={14} /></button>
          <div className="dn-sep" />
          {inFullscreen
            ? <button className="dn-action dn-action--exit-fs" onClick={exitFullscreen} title="Tam ekrandan çık (Esc)"><Minimize2 size={14} /></button>
            : <button className="dn-action" onClick={openFullscreen} title="Çizimi tam ekrana aç"><Maximize2 size={14} /></button>
          }
        </div>
      </>
    )
  }

  /* ── Silme onay modalı ── */
  var deleteConfirmModal = confirmDelete ? createPortal(
    <div
      className="dn-confirm-backdrop"
      onClick={function (e) { if (e.target === e.currentTarget) setConfirmDelete(false) }}
    >
      <div className="dn-confirm-card">
        <div className="dn-confirm-icon"><Trash2 size={28} color="#ef4444" /></div>
        <div className="dn-confirm-title">Çizimi Sil</div>
        <div className="dn-confirm-msg">Bu çizim kalıcı olarak silinecek. Emin misiniz?</div>
        <div className="dn-confirm-actions">
          <button className="dn-confirm-btn dn-confirm-btn--cancel" onClick={function () { setConfirmDelete(false) }}>Vazgeç</button>
          <button className="dn-confirm-btn dn-confirm-btn--ok" autoFocus onClick={deleteNode}>Sil</button>
        </div>
      </div>
    </div>,
    document.body
  ) : null

  /* ── Fullscreen portal ── */
  var fsOverlay = fullscreen ? createPortal(
    <div className="dn-fs-overlay">
      <div className="dn-toolbar dn-toolbar--fs">
        {toolbarItems(true)}
      </div>
      <div className="dn-fs-canvas-wrap">
        <canvas
          ref={fsCanvasRefCb}
          width={800}
          className="dn-canvas dn-canvas--fs"
          style={{ cursor: tool === 'eraser' ? 'cell' : 'crosshair' }}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerLeave={onPointerUp}
        />
      </div>
    </div>,
    document.body
  ) : null

  return (
    <NodeViewWrapper contentEditable={false}>
      {deleteConfirmModal}
      {fsOverlay}

      <div className="dn-wrap">
        <div className="dn-toolbar">
          {toolbarItems(false)}
        </div>

        <div className="dn-canvas-wrap" style={{ opacity: fullscreen ? 0.35 : 1 }}>
          <canvas
            ref={embeddedRefCb}
            width={800}
            className="dn-canvas"
            style={{ height: displayH + 'px', cursor: fullscreen ? 'default' : (tool === 'eraser' ? 'cell' : 'crosshair') }}
            onPointerDown={fullscreen ? undefined : onPointerDown}
            onPointerMove={fullscreen ? undefined : onPointerMove}
            onPointerUp={fullscreen ? undefined : onPointerUp}
            onPointerLeave={fullscreen ? undefined : onPointerUp}
          />
          {!fullscreen && (
            <div className="dn-resize-handle" onPointerDown={onResizeStart} title="Yüksekliği ayarla">
              <span className="dn-resize-dots" />
            </div>
          )}
        </div>

        <div className="dn-footer">
          {saved && <span className="dn-saved-hint">kaydedildi</span>}
          <button className="dn-btn dn-btn--delete" onClick={function () { setConfirmDelete(true) }}>Çizimi Sil</button>
        </div>
      </div>
    </NodeViewWrapper>
  )
}
