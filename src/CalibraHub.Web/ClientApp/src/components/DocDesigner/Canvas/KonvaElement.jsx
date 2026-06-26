import React, { useRef, useEffect, useState } from 'react'
import { flushSync } from 'react-dom'
import { Rect, Text, Group, Line, Image as KonvaImage, Transformer } from 'react-konva'
import { mmToPx, pxToMm } from './DesignerCanvas'

function useImageLoad(src) {
  const [img, setImg] = useState(null)
  useEffect(() => {
    if (!src) { setImg(null); return }
    const i = new window.Image()
    i.crossOrigin = 'anonymous'
    let cancelled = false
    i.onload = () => { if (!cancelled) setImg(i) }
    i.onerror = () => { if (!cancelled) setImg(null) }
    i.src = src
    return () => { cancelled = true }
  }, [src])
  return img
}

function computeFit(natW, natH, boxW, boxH, mode) {
  if (mode === 'stretch') return { x: 0, y: 0, w: boxW, h: boxH }
  if (mode === 'original') {
    return { x: (boxW - natW) / 2, y: (boxH - natH) / 2, w: natW, h: natH }
  }
  // contain
  const r = Math.min(boxW / natW, boxH / natH)
  const w = natW * r, h = natH * r
  return { x: (boxW - w) / 2, y: (boxH - h) / 2, w, h }
}

/**
 * Snap targets toplar: kardeş elementler ile bant kenarları ve orta noktası.
 * Bant koordinatlarında (px, zoom uygulanmadan) hesaplanır.
 * extraXs: cross-band X target'ları (diğer bantlardaki elementlerin x/center/right) —
 * tablo kolonu hizalama için (örn. SubDetail elementinin TableHeader Miktar başlığına snap).
 */
function buildSnapTargets(siblings, bandWidthPx, bandHeightPx, extraXs = []) {
  const xs = new Set([0, bandWidthPx, bandWidthPx / 2, ...extraXs])
  const ys = new Set([0, bandHeightPx, bandHeightPx / 2])
  for (const sib of (siblings ?? [])) {
    const sx = mmToPx(sib.x); const sy = mmToPx(sib.y)
    const sw = mmToPx(sib.w); const sh = mmToPx(sib.h)
    xs.add(sx); xs.add(sx + sw / 2); xs.add(sx + sw)
    ys.add(sy); ys.add(sy + sh / 2); ys.add(sy + sh)
  }
  return { xs: [...xs], ys: [...ys] }
}

/** Verilen pos için en yakın snap target'ı bulur (tolerans içinde). */
function findSnap(myEdges, targets, tolerance) {
  let bestTarget = null, bestDelta = 0
  for (const mine of myEdges) {
    for (const t of targets) {
      const d = t - mine
      if (Math.abs(d) < tolerance && (bestTarget === null || Math.abs(d) < Math.abs(bestDelta))) {
        bestTarget = t
        bestDelta = d
      }
    }
  }
  return { target: bestTarget, delta: bestDelta }
}

export default function KonvaElement({
  el, isSelected, isMultiSelected, bandId, dispatch,
  zoom = 1, bandWidthPx = Infinity, bandHeightPx = Infinity,
  siblings = null, extraXTargets = [], onDragGuide = null,
}) {
  const shapeRef = useRef(null)
  const trRef    = useRef(null)
  const imgEl    = useImageLoad(el.kind === 'Image' ? el.imageSrc : null)

  useEffect(() => {
    if (isSelected && trRef.current && shapeRef.current) {
      trRef.current.nodes([shapeRef.current])
      trRef.current.getLayer()?.batchDraw()
    }
  }, [isSelected, el.x, el.y, el.w, el.h])

  const x = mmToPx(el.x)
  const y = mmToPx(el.y)
  const w = mmToPx(el.w)
  const h = mmToPx(el.h)
  const s = el.style ?? {}

  const fill = s.bgColor === 'transparent' ? 'rgba(0,0,0,0)' : (s.bgColor ?? 'rgba(0,0,0,0)')

  const bindingText = el.binding?.alias && el.binding?.col
    ? `[${el.binding.alias}.${el.binding.col}]`
    : (el.text ? el.text : null)

  const label =
    el.kind === 'Label'         ? (el.text ?? '')
    : el.kind === 'BoundField'  ? `[${el.binding?.alias ?? '?'}.${el.binding?.col ?? '?'}]`
    : el.kind === 'AmountInWords'
      ? `[₺ ${el.binding?.alias ? el.binding.alias + '.' + el.binding.col : 'yazı ile tutar'}]`
    : el.kind === 'PageNumber'  ? '[#]'
    : el.kind === 'DateTimeNow' ? '[Tarih]'
    : el.kind === 'Image'       ? (el.imageSrc ? '' : '[ 🖼 ]')
    : el.kind === 'Barcode'     ? '' // own rendering (bar lines veya QR matrisi)
    : el.kind === 'Shape'       ? ''
    : el.kind === 'Aggregate'
      ? `Σ ${el.aggFunc ?? 'SUM'}(${el.aggSource || '?'}.${el.aggField || '?'})`
    : el.kind === 'Table'       ? ''
    : el.kind

  // Tek bayrak: Barcode elementi QR tipinde mi? QR ayri kind degil, barcodeType.
  const isQr = el.kind === 'Barcode' && el.barcodeType === 'QR'

  // Sahte barkod çizgileri için sabit deterministik desen (QR HARIC)
  const barcodeLines = (el.kind === 'Barcode' && !isQr)
    ? (() => {
        const lines = []
        let i = 0
        let xPos = 4
        const pattern = [2, 1, 3, 1, 2, 2, 1, 3, 1, 1, 2, 1, 3, 2, 1, 1, 2, 3, 1, 2, 1, 2, 3, 1]
        while (xPos < w - 4) {
          const lw = pattern[i % pattern.length]
          if (i % 2 === 0 && xPos + lw < w - 4) {
            lines.push({ x: xPos, w: lw })
          }
          xPos += lw
          i++
        }
        return lines
      })()
    : null

  // Sahte QR matrisi — Barcode + barcodeType='QR'
  const qrCells = isQr
    ? (() => {
        const N = 11
        const cellSize = Math.min(w, h) / (N + 2)
        const offsetX = (w - cellSize * N) / 2
        const offsetY = (h - cellSize * N) / 2
        const cells = []
        // Üç köşe finder pattern
        const finder = [[0,0],[0,N-7],[N-7,0]]
        finder.forEach(([cr, cc]) => {
          for (let r = 0; r < 7; r++) for (let c = 0; c < 7; c++) {
            if (r === 0 || r === 6 || c === 0 || c === 6 || (r >= 2 && r <= 4 && c >= 2 && c <= 4)) {
              cells.push({ x: offsetX + (cc + c) * cellSize, y: offsetY + (cr + r) * cellSize, s: cellSize })
            }
          }
        })
        // Rastgele görünümlü ama sabit data hücreleri
        const seed = (el.binding?.col ?? el.text ?? 'qr').length
        for (let r = 0; r < N; r++) {
          for (let c = 0; c < N; c++) {
            const inFinder = (r < 7 && c < 7) || (r < 7 && c >= N-7) || (r >= N-7 && c < 7)
            if (inFinder) continue
            if (((r * 17 + c * 31 + seed * 7) % 5) < 2) {
              cells.push({ x: offsetX + c * cellSize, y: offsetY + r * cellSize, s: cellSize })
            }
          }
        }
        return cells
      })()
    : null

  // Per-side borders: yeni model { borderTop, borderRight, borderBottom, borderLeft }
  // Eski model: tek `border` boolean (geriye uyumlu, hepsi açık demek)
  const legacyAll = s.border === true
  const bT = s.borderTop    ?? legacyAll
  const bR = s.borderRight  ?? legacyAll
  const bB = s.borderBottom ?? legacyAll
  const bL = s.borderLeft   ?? legacyAll
  const userBorder = bT || bR || bB || bL

  // Seçim/hint stroke — kullanıcı border'ı yoksa belirgin bir indigo hint çiziyoruz
  const hintStroke = isSelected
    ? '#6366f1'
    : isMultiSelected
    ? '#a5b4fc'
    : 'rgba(99,102,241,0.55)'   // daha belirgin (önce 0.2 idi)
  const hintStrokeWidth = isSelected ? 1.5 : isMultiSelected ? 1 : 0.75
  const dash = isMultiSelected && !isSelected
    ? [4, 2]
    : (!isSelected && !userBorder ? [2, 2] : undefined)   // hint kesikli, kullanıcı bordürü düz

  return (
    <>
      <Group
        ref={shapeRef}
        x={x} y={y}
        rotation={el.rotation ?? 0}
        opacity={el.visible === false ? 0.25 : (el.printable === false ? 0.65 : 1)}
        draggable
        dragBoundFunc={pos => {
          // pos absolute screen coords (CSS px); stage scale = zoom
          // Bant koordinatlarına çevir (zoom uygulanmadan)
          let xPx = pos.x / zoom
          let yPx = pos.y / zoom

          // Snap hesabı — siblings boşsa bile cross-band x target'ları varsa onları kullan
          let guideX = null, guideY = null
          const hasAnyTarget = (siblings && siblings.length > 0) || (extraXTargets && extraXTargets.length > 0)
          if (hasAnyTarget) {
            const { xs, ys } = buildSnapTargets(siblings, bandWidthPx, bandHeightPx, extraXTargets)
            const tolerance = 4   // px (bant koord) — ekstra esnek snap
            const myXs = [xPx, xPx + w / 2, xPx + w]
            const myYs = [yPx, yPx + h / 2, yPx + h]
            const sx = findSnap(myXs, xs, tolerance)
            const sy = findSnap(myYs, ys, tolerance)
            if (sx.target !== null) { xPx += sx.delta; guideX = sx.target }
            if (sy.target !== null) { yPx += sy.delta; guideY = sy.target }
          }

          // Bant sınırına clamp
          xPx = Math.max(0, Math.min(xPx, bandWidthPx  - w))
          yPx = Math.max(0, Math.min(yPx, bandHeightPx - h))

          // Guide overlay state'ini güncelle (rAF ile React render'ı tetikleme bantta)
          if (onDragGuide) {
            requestAnimationFrame(() => onDragGuide({ x: guideX, y: guideY }))
          }

          return { x: xPx * zoom, y: yPx * zoom }
        }}
        onClick={e => {
          e.cancelBubble = true
          if (e.evt.shiftKey || e.evt.ctrlKey) {
            dispatch({ type: 'MULTI_SELECT_ELEMENT', elementId: el.id, bandId })
          } else {
            dispatch({ type: 'SELECT_ELEMENT', elementId: el.id, bandId })
          }
        }}
        onDblClick={e => {
          e.cancelBubble = true
          // Barkod da veri secimi gerektiren bir element — cift tikla editor acilir.
          if (['Label','BoundField','AmountInWords','Barcode'].includes(el.kind)) {
            dispatch({ type: 'OPEN_ELEMENT_EDITOR', elementId: el.id })
          }
        }}
        onDragEnd={e => {
          // Güvenlik: dragBoundFunc'a rağmen ekstra clamp
          const nx = Math.max(0, Math.min(e.target.x(), Math.max(0, (bandWidthPx  - w))))
          const ny = Math.max(0, Math.min(e.target.y(), Math.max(0, (bandHeightPx - h))))
          dispatch({
            type: 'MOVE_ELEMENT',
            elementId: el.id,
            x: pxToMm(nx),
            y: pxToMm(ny),
          })
          // Drag bitti → guide overlay'i temizle
          if (onDragGuide) onDragGuide(null)
        }}
      >
        {/* Arkaplan + seçim/hint çerçevesi */}
        <Rect
          width={w} height={h}
          fill={fill}
          stroke={hintStroke}
          strokeWidth={hintStrokeWidth}
          dash={dash}
          strokeScaleEnabled={false}
          cornerRadius={2}
        />

        {/* Kullanıcı kenarlığı — başına çıkışı yok, hint üzerine binecek */}
        {bT && <Line points={[0, 0, w, 0]} stroke="#333" strokeWidth={1} strokeScaleEnabled={false} />}
        {bR && <Line points={[w, 0, w, h]} stroke="#333" strokeWidth={1} strokeScaleEnabled={false} />}
        {bB && <Line points={[0, h, w, h]} stroke="#333" strokeWidth={1} strokeScaleEnabled={false} />}
        {bL && <Line points={[0, 0, 0, h]} stroke="#333" strokeWidth={1} strokeScaleEnabled={false} />}

        {/* Barkod gorseli — duz bar (QR HARIC tum tipler) */}
        {el.kind === 'Barcode' && !isQr && (
          <>
            <Rect width={w} height={h} fill="#ffffff" />
            {barcodeLines?.map((ln, i) => (
              <Rect key={i} x={ln.x} y={6}
                width={ln.w} height={h - (el.showBarcodeText ? 14 : 8)}
                fill="#000" />
            ))}
            {el.showBarcodeText && (
              <Text
                text={(el.binding?.alias && el.binding?.col)
                  ? `[${el.binding.alias}.${el.binding.col}]`
                  : (el.text || el.barcodeType || 'Barcode')}
                x={0} y={h - 11}
                width={w} height={10}
                fontSize={7} fontFamily="monospace" fill="#000" align="center"
              />
            )}
          </>
        )}

        {/* QR gorseli — Barcode + barcodeType='QR' */}
        {isQr && (
          <>
            <Rect width={w} height={h} fill="#ffffff" />
            {qrCells?.map((c, i) => (
              <Rect key={i} x={c.x} y={c.y} width={c.s} height={c.s} fill="#000" />
            ))}
          </>
        )}

        {/* Tablo canvas önizlemesi */}
        {el.kind === 'Table' && (() => {
          const cols = el.tableCols ?? []
          const headerH = mmToPx(6)
          const borderC = el.tableBorderColor ?? '#e2e8f0'
          const headerBg = el.tableHeaderBgColor ?? '#f1f5f9'
          // Kolon genişliklerini toplam mm'den px'e
          const totalColMm = cols.reduce((s, c) => s + (c.width ?? 30), 0)
          const scaleX = totalColMm > 0 ? w / mmToPx(totalColMm) : 1
          let cx = 0
          const colLines = []
          cols.forEach((col, i) => {
            const cw = mmToPx(col.width ?? 30) * scaleX
            if (i > 0) colLines.push(cx)
            cx += cw
          })
          return (
            <>
              <Rect width={w} height={h} fill="#f8fafc" stroke={borderC} strokeWidth={0.7} strokeScaleEnabled={false} />
              {el.showHeader !== false && (
                <Rect width={w} height={Math.min(headerH, h)} fill={headerBg} strokeScaleEnabled={false} />
              )}
              <Line points={[0, headerH, w, headerH]} stroke={borderC} strokeWidth={0.7} strokeScaleEnabled={false} />
              {colLines.map((cx2, i) => (
                <Line key={i} points={[cx2, 0, cx2, h]} stroke={borderC} strokeWidth={0.7} strokeScaleEnabled={false} />
              ))}
              {cols.length === 0 && (
                <Text x={4} y={4} width={w - 8} height={h - 8}
                  text="[Tablo — kolon tanımı yok]"
                  fontSize={8} fontFamily="Arial" fill="#94a3b8"
                  align="center" verticalAlign="middle" />
              )}
            </>
          )
        })()}

        {/* Resim — yüklenmişse */}
        {el.kind === 'Image' && imgEl && (() => {
          const fit = computeFit(imgEl.naturalWidth, imgEl.naturalHeight, w, h, el.imageFit ?? 'contain')
          return (
            <Group clipX={0} clipY={0} clipWidth={w} clipHeight={h}>
              <KonvaImage image={imgEl} x={fit.x} y={fit.y} width={fit.w} height={fit.h} />
            </Group>
          )
        })()}

        {/* Yazdırılamaz badge (sadece designer'da görünür, çıktıya girmez) */}
        {el.printable === false && (
          <>
            <Rect x={w - 18} y={2} width={16} height={11} fill="#fef3c7" stroke="#f59e0b" strokeWidth={0.5} cornerRadius={2} strokeScaleEnabled={false} />
            <Text x={w - 18} y={3.5} width={16} height={11} text="⊘" fontSize={9} fontFamily="Arial" fill="#b45309" align="center" />
          </>
        )}

        {/* Koşul badge — koşul tanımlıysa küçük "?" rozeti */}
        {el.condition && (
          <>
            <Rect x={2} y={2} width={13} height={10} fill="#ede9fe" stroke="#7c3aed" strokeWidth={0.5} cornerRadius={2} strokeScaleEnabled={false} />
            <Text x={2} y={3} width={13} height={10} text="?" fontSize={8} fontFamily="Arial" fill="#7c3aed" align="center" />
          </>
        )}

        {label ? (
          <Text
            text={label}
            x={2} y={2}
            width={w - 4} height={h - 4}
            fontSize={s.fontSize ?? 10}
            fontFamily="Arial"
            fontStyle={[s.bold && 'bold', s.italic && 'italic'].filter(Boolean).join(' ') || 'normal'}
            fill={s.color ?? '#333'}
            align={s.align ?? 'left'}
            verticalAlign={s.verticalAlign ?? 'middle'}
            wrap={s.overflow === 'wrap' ? 'word' : 'none'}
            ellipsis={s.overflow !== 'wrap'}
          />
        ) : null}
      </Group>

      {isSelected && (
        <Transformer
          ref={trRef}
          rotateEnabled={false}
          keepRatio={false}
          // flipEnabled=false → kullanıcı alt kenarı üst kenarın ötesine sürüklediğinde
          // Konva default'ta kutuyu otomatik flip eder ve handle'lar yer değiştirir
          // (user "fare aşağıdan yukarı çekerken üsttekine etki ediyor" diye raporladı).
          // false yaparak min boyutta sıkışsın, flip etmesin.
          flipEnabled={false}
          anchorSize={Math.max(5, 8 / zoom)}
          anchorStrokeWidth={1 / zoom}
          borderStrokeWidth={1 / zoom}
          ignoreStroke={true}
          boundBoxFunc={(oldBox, newBox) => {
            // Bant sınırları içinde kalsın (newBox CSS px, scale=zoom)
            const maxRight  = bandWidthPx  * zoom
            const maxBottom = bandHeightPx * zoom
            let { x, y, width, height } = newBox
            // Min 0.5mm — ince çizgi (imza altı, sayfa altı) tasarımına izin ver.
            // Önceden 5×3mm idi, 0.5mm imza çizgisi tasarlanamıyordu.
            width  = Math.max(mmToPx(0.5) * zoom, width)
            height = Math.max(mmToPx(0.5) * zoom, height)
            if (x < 0) { width += x; x = 0 }
            if (y < 0) { height += y; y = 0 }
            if (x + width  > maxRight)  width  = maxRight  - x
            if (y + height > maxBottom) height = maxBottom - y

            // ── Resize sırasında snap (drag ile aynı mantık, kenar bazlı) ─────
            let guideX = null, guideY = null
            const hasAnyTarget = (siblings && siblings.length > 0) || (extraXTargets && extraXTargets.length > 0)
            if (hasAnyTarget) {
              const { xs, ys } = buildSnapTargets(siblings, bandWidthPx, bandHeightPx, extraXTargets)
              const tolerance = 4   // bant-koord px

              // Hangi kenarlar değişti? (corner handle ikisini de değiştirebilir)
              const leftChanged   = Math.abs(newBox.x - oldBox.x) > 0.5
              const rightChanged  = Math.abs((newBox.x + newBox.width)  - (oldBox.x + oldBox.width))  > 0.5
              const topChanged    = Math.abs(newBox.y - oldBox.y) > 0.5
              const bottomChanged = Math.abs((newBox.y + newBox.height) - (oldBox.y + oldBox.height)) > 0.5

              // X ekseni — bant koordinatlarında çalış
              const leftCoord  = x / zoom
              const rightCoord = (x + width) / zoom

              if (leftChanged) {
                const sx = findSnap([leftCoord], xs, tolerance)
                if (sx.target !== null) {
                  const dx = sx.delta * zoom
                  x     += dx
                  width -= dx
                  guideX = sx.target
                }
              }
              if (rightChanged && guideX === null) {
                const sr = findSnap([rightCoord], xs, tolerance)
                if (sr.target !== null) {
                  width += sr.delta * zoom
                  guideX = sr.target
                }
              }

              // Y ekseni
              const topCoord    = y / zoom
              const bottomCoord = (y + height) / zoom

              if (topChanged) {
                const sy = findSnap([topCoord], ys, tolerance)
                if (sy.target !== null) {
                  const dy = sy.delta * zoom
                  y      += dy
                  height -= dy
                  guideY = sy.target
                }
              }
              if (bottomChanged && guideY === null) {
                const sb = findSnap([bottomCoord], ys, tolerance)
                if (sb.target !== null) {
                  height += sb.delta * zoom
                  guideY = sb.target
                }
              }
            }

            if (onDragGuide) {
              requestAnimationFrame(() => onDragGuide({ x: guideX, y: guideY }))
            }

            // Aynı min ile son güvenlik kontrolü (snap sonrası boyut tekrar sapabilir)
            if (width  < mmToPx(0.5) * zoom || height < mmToPx(0.5) * zoom) return oldBox
            return { ...newBox, x, y, width, height }
          }}
          onTransformEnd={() => {
            const node = shapeRef.current
            const sx   = node.scaleX()
            const sy   = node.scaleY()
            const nx   = node.x()
            const ny   = node.y()
            // 1) Reset Konva scale so internal state matches the new dims
            node.scaleX(1)
            node.scaleY(1)
            // 2) Apply new dims SYNCHRONOUSLY before the browser paints — flushSync
            //    forces React to commit the state update (and react-konva to update
            //    the underlying Konva nodes) inside the same JS task. Without this,
            //    React batches the dispatch async; the browser paints the scale=1 +
            //    OLD width frame first → user sees a brief snap to original size.
            flushSync(() => {
              dispatch({ type: 'RESIZE_ELEMENT', elementId: el.id, w: pxToMm(w * sx), h: pxToMm(h * sy) })
              dispatch({ type: 'MOVE_ELEMENT',   elementId: el.id, x: pxToMm(nx), y: pxToMm(ny) })
            })
            // 3) Re-sync Transformer handles to the freshly-rendered node
            if (trRef.current && shapeRef.current) {
              trRef.current.nodes([shapeRef.current])
              trRef.current.getLayer()?.batchDraw()
            }
            // 4) Snap kılavuzlarını temizle
            if (onDragGuide) onDragGuide(null)
          }}
        />
      )}
    </>
  )
}
