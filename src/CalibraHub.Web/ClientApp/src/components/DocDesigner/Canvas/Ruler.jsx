import React, { useRef, useEffect } from 'react'

const RULER_SIZE = 24  // px thickness

function drawRuler(ctx, length, isVertical, zoom, mouseMm, isDark) {
  const MM_TO_PX = (96 / 25.4) * zoom
  const totalMm  = Math.ceil(length / MM_TO_PX) + 5

  const colors = isDark
    ? { bg: '#131822', tick: '#2a3347', text: '#556070', indicator: '#ef4444', border: '#2a3347' }
    : { bg: '#f8f9fb', tick: '#94a3b8', text: '#64748b', indicator: '#ef4444', border: '#e5e7eb' }

  ctx.clearRect(0, 0, isVertical ? RULER_SIZE : length, isVertical ? length : RULER_SIZE)
  ctx.fillStyle = colors.bg
  ctx.fillRect(0, 0, isVertical ? RULER_SIZE : length, isVertical ? length : RULER_SIZE)
  // Border along the inner edge
  ctx.fillStyle = colors.border
  if (isVertical) ctx.fillRect(RULER_SIZE - 1, 0, 1, length)
  else            ctx.fillRect(0, RULER_SIZE - 1, length, 1)

  ctx.strokeStyle = colors.tick
  ctx.fillStyle   = colors.text
  ctx.font        = 'bold 8px sans-serif'
  ctx.textAlign   = isVertical ? 'right' : 'center'
  ctx.textBaseline = isVertical ? 'middle' : 'top'

  for (let mm = 0; mm <= totalMm; mm++) {
    const pos = Math.round(mm * MM_TO_PX)
    const isMajor  = mm % 10 === 0
    const isMedium = mm % 5 === 0 && !isMajor

    const tickLen = isMajor ? 12 : isMedium ? 7 : 3

    ctx.beginPath()
    if (isVertical) {
      ctx.moveTo(RULER_SIZE - tickLen, pos)
      ctx.lineTo(RULER_SIZE,          pos)
    } else {
      ctx.moveTo(pos, RULER_SIZE - tickLen)
      ctx.lineTo(pos, RULER_SIZE)
    }
    ctx.stroke()

    if (isMajor && mm > 0) {
      if (isVertical) {
        ctx.save()
        ctx.translate(RULER_SIZE - 14, pos)
        ctx.rotate(-Math.PI / 2)
        ctx.fillText(String(mm), 0, 0)
        ctx.restore()
      } else {
        ctx.fillText(String(mm), pos, 2)
      }
    }
  }

  // Red position indicator
  if (mouseMm != null && mouseMm >= 0) {
    const pos = Math.round(mouseMm * MM_TO_PX)
    ctx.strokeStyle = colors.indicator
    ctx.lineWidth   = 1
    ctx.beginPath()
    if (isVertical) {
      ctx.moveTo(0,          pos)
      ctx.lineTo(RULER_SIZE, pos)
    } else {
      ctx.moveTo(pos, 0)
      ctx.lineTo(pos, RULER_SIZE)
    }
    ctx.stroke()
    ctx.lineWidth = 1
  }
}

export function HRuler({ widthPx, zoom, mouseMm, isDark }) {
  const ref = useRef(null)
  useEffect(() => {
    const canvas = ref.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    drawRuler(ctx, widthPx, false, zoom, mouseMm?.x, isDark)
  }, [widthPx, zoom, mouseMm, isDark])

  return (
    <canvas
      ref={ref}
      width={widthPx}
      height={RULER_SIZE}
      style={{ display: 'block', flexShrink: 0 }}
    />
  )
}

export function VRuler({ heightPx, zoom, mouseMm, isDark }) {
  const ref = useRef(null)
  useEffect(() => {
    const canvas = ref.current
    if (!canvas) return
    const ctx = canvas.getContext('2d')
    drawRuler(ctx, heightPx, true, zoom, mouseMm?.y, isDark)
  }, [heightPx, zoom, mouseMm, isDark])

  return (
    <canvas
      ref={ref}
      width={RULER_SIZE}
      height={heightPx}
      style={{ display: 'block', flexShrink: 0 }}
    />
  )
}

export const RULER_SIZE_PX = RULER_SIZE
