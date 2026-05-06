import React, { useRef, useEffect, useCallback } from 'react'
import { Rect, Text, Image as KonvaImage, Group, Transformer } from 'react-konva'
import { mmToPx, pxToMm } from './DesignerCanvas'

export default function KonvaElement({ element: el, bandId, isSelected, dispatch }) {
  const shapeRef = useRef(null)
  const trRef = useRef(null)

  useEffect(() => {
    if (isSelected && trRef.current && shapeRef.current) {
      trRef.current.nodes([shapeRef.current])
      trRef.current.getLayer()?.batchDraw()
    }
  }, [isSelected])

  const x = mmToPx(el.x)
  const y = mmToPx(el.y)
  const w = mmToPx(el.w)
  const h = mmToPx(el.h)
  const s = el.style ?? {}

  const onDragEnd = useCallback((e) => {
    dispatch({
      type: 'MOVE_ELEMENT',
      bandId,
      elementId: el.id,
      x: pxToMm(e.target.x()),
      y: pxToMm(e.target.y())
    })
  }, [bandId, el.id, dispatch])

  const onTransformEnd = useCallback((e) => {
    const node = shapeRef.current
    if (!node) return
    const scaleX = node.scaleX()
    const scaleY = node.scaleY()
    node.scaleX(1); node.scaleY(1)
    dispatch({
      type: 'RESIZE_ELEMENT',
      bandId,
      elementId: el.id,
      w: pxToMm(Math.max(20, node.width() * scaleX)),
      h: pxToMm(Math.max(8, node.height() * scaleY))
    })
  }, [bandId, el.id, dispatch])

  const onClick = useCallback((e) => {
    e.cancelBubble = true
    dispatch({ type: 'SELECT_ELEMENT', elementId: el.id, bandId })
  }, [el.id, bandId, dispatch])

  const label = getDisplayLabel(el)
  const fillColor = (s.bgColor && s.bgColor !== 'transparent') ? s.bgColor : 'transparent'
  const strokeColor = isSelected ? '#6366f1' : (s.border ? '#999' : 'transparent')
  const strokeWidth = isSelected ? 1.5 : (s.border ? 0.5 : 0)

  return (
    <>
      <Group
        x={x} y={y}
        draggable
        onDragEnd={onDragEnd}
        onClick={onClick}
      >
        <Rect
          ref={shapeRef}
          width={w} height={h}
          fill={fillColor}
          stroke={strokeColor}
          strokeWidth={strokeWidth}
          cornerRadius={2}
          onTransformEnd={onTransformEnd}
        />
        <Text
          width={w} height={h}
          text={label}
          fontSize={s.fontSize ?? 10}
          fontStyle={[s.bold ? 'bold' : '', s.italic ? 'italic' : ''].filter(Boolean).join(' ') || 'normal'}
          fill={s.color ?? '#000000'}
          align={s.align ?? 'left'}
          verticalAlign="middle"
          padding={2}
          listening={false}
        />
      </Group>

      {isSelected && (
        <Transformer
          ref={trRef}
          rotateEnabled={false}
          boundBoxFunc={(_, newBox) => ({
            ...newBox,
            width: Math.max(20, newBox.width),
            height: Math.max(8, newBox.height)
          })}
        />
      )}
    </>
  )
}

function getDisplayLabel(el) {
  switch (el.kind) {
    case 'Label':         return el.text ?? 'Metin'
    case 'BoundField':    return el.binding ? `[${el.binding.alias}.${el.binding.col}]` : '[Veri Alanı]'
    case 'PageNumber':    return '[Sayfa No]'
    case 'DateTimeNow':   return '[Tarih]'
    case 'AmountInWords': return '[Yazı ile Tutar]'
    case 'Image':         return '[Görsel]'
    case 'Shape':         return ''
    default:              return el.text ?? el.kind
  }
}
