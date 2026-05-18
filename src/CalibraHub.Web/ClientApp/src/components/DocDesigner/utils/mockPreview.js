import { convertNumberToText } from './amountInWords'

const MM = 96 / 25.4  // mm → px

const MOCK_VALUES = {
  default: { No: 'TKL-2024-001', Tarih: '11.05.2026', Toplam: '12.345,67', KDV: '2.222,22', GenelToplam: '14.567,89', Aciklama: 'Örnek açıklama metni', Adi: 'Test Ürün', Adet: '5', BirimFiyat: '2.469,13', Unvan: 'ABC Ticaret A.Ş.', Adres: 'Örnek Mah. Test Cad. No:1 İstanbul' },
}

function mockValue(binding) {
  if (!binding?.col) return '—'
  const colLower = (binding.col ?? '').toLowerCase()
  for (const [key, val] of Object.entries(MOCK_VALUES.default)) {
    if (colLower.includes(key.toLowerCase()) || key.toLowerCase().includes(colLower)) return val
  }
  return `[${binding.alias}.${binding.col}]`
}

function elHtml(el) {
  // Görünür değil veya yazdırılamaz → mock çıktıda hiç görünme
  if (el.visible === false || el.printable === false) return ''

  const s = el.style ?? {}
  const rot = el.rotation ?? 0
  const styles = [
    `position:absolute`,
    `left:${el.x * MM}px`, `top:${el.y * MM}px`,
    `width:${el.w * MM}px`, `height:${el.h * MM}px`,
    rot ? `transform:rotate(${rot}deg);transform-origin:top left` : '',
    `font-size:${(s.fontSize ?? 10) * 1.33}px`,
    `font-family:Arial,sans-serif`,
    s.bold   ? 'font-weight:700'  : 'font-weight:400',
    s.italic ? 'font-style:italic' : '',
    s.underline ? 'text-decoration:underline' : '',
    `text-align:${s.align ?? 'left'}`,
    `color:${s.color ?? '#000'}`,
    s.bgColor && s.bgColor !== 'transparent' ? `background:${s.bgColor}` : '',
    (() => {
      const legacyAll = s.border === true
      const bT = s.borderTop    ?? legacyAll
      const bR = s.borderRight  ?? legacyAll
      const bB = s.borderBottom ?? legacyAll
      const bL = s.borderLeft   ?? legacyAll
      return [
        bT && 'border-top:1px solid #333',
        bR && 'border-right:1px solid #333',
        bB && 'border-bottom:1px solid #333',
        bL && 'border-left:1px solid #333',
      ].filter(Boolean).join(';')
    })(),
    `overflow:hidden`, `box-sizing:border-box`,
    `display:flex`,
    `align-items:${s.verticalAlign === 'top' ? 'flex-start' : s.verticalAlign === 'bottom' ? 'flex-end' : 'center'}`,
    `padding:0 2px`,
  ].filter(Boolean).join(';')

  let content = ''
  switch (el.kind) {
    case 'Label':
      content = el.text ?? ''
      break
    case 'BoundField':
      content = mockValue(el.binding)
      break
    case 'AmountInWords': {
      const rawVal = parseFloat((mockValue(el.binding) ?? '').replace(/\./g, '').replace(',', '.')) || 12345.67
      content = convertNumberToText(rawVal)
      break
    }
    case 'PageNumber':
      content = '1'
      break
    case 'DateTimeNow':
      content = new Date().toLocaleDateString('tr-TR')
      break
    case 'Image': {
      if (el.imageSrc) {
        const objectFit = el.imageFit === 'stretch' ? 'fill' : el.imageFit === 'original' ? 'none' : 'contain'
        return `<div style="${styles};overflow:hidden;padding:0;display:block">
          <img src="${el.imageSrc}" style="width:100%;height:100%;object-fit:${objectFit};display:block" />
        </div>`
      }
      content = '<span style="font-size:18px">🖼</span>'
      break
    }
    case 'Barcode': {
      const val = (el.binding?.alias && el.binding?.col) ? mockValue(el.binding) : (el.text || el.barcodeType || '1234567890')
      const lines = []
      let xp = 4, i = 0
      const pat = [2,1,3,1,2,2,1,3,1,1,2,1,3,2,1,1,2,3,1,2,1,2,3,1]
      const lineH = (el.showBarcodeText !== false) ? (el.h * MM - 14) : (el.h * MM - 8)
      while (xp < el.w * MM - 4) {
        const lw = pat[i % pat.length]
        if (i % 2 === 0) lines.push(`<div style="position:absolute;left:${xp}px;top:6px;width:${lw}px;height:${lineH}px;background:#000"></div>`)
        xp += lw; i++
      }
      const textHtml = (el.showBarcodeText !== false)
        ? `<div style="position:absolute;left:0;right:0;bottom:0;text-align:center;font-family:monospace;font-size:8px;color:#000">${val}</div>`
        : ''
      return `<div style="${styles};background:#fff;display:block;padding:0">${lines.join('')}${textHtml}</div>`
    }
    case 'QrCode': {
      // Basitleştirilmiş QR placeholder
      return `<div style="${styles};background:#fff;display:flex;align-items:center;justify-content:center;padding:0">
        <div style="display:grid;grid-template-columns:repeat(11,1fr);width:80%;height:80%;gap:1px">
          ${Array.from({length: 121}).map((_, i) => {
            const r = Math.floor(i / 11), c = i % 11
            const inFinder = (r < 4 && c < 4) || (r < 4 && c >= 7) || (r >= 7 && c < 4)
            const filled = inFinder ? ((r === 0 || r === 3 || c === 0 || c === 3) ? true : (r === 1 && c === 1) || (r === 2 && c === 2))
              : (((r * 17 + c * 31) % 5) < 2)
            return `<div style="background:${filled ? '#000' : 'transparent'}"></div>`
          }).join('')}
        </div>
      </div>`
    }
    case 'Shape':
      return `<div style="${styles};background:${s.color ?? '#333'};height:1px"></div>`
    default:
      content = el.kind
  }
  return `<div style="${styles}">${content}</div>`
}

function bandHtml(band, meta) {
  const padL = meta.marginLeft * MM
  const padR = meta.marginRight * MM
  const innerW = (meta.pageW - meta.marginLeft - meta.marginRight) * MM
  const bandH  = band.height * MM

  const elHtmls = [...band.elements]
    .sort((a, b) => a.zIndex - b.zIndex)
    .map(elHtml)
    .join('')

  return `
    <div style="position:relative;width:${innerW}px;height:${bandH}px;overflow:hidden;box-sizing:border-box;border-bottom:1px dashed #e5e7eb">
      ${elHtmls}
    </div>`
}

const BAND_ORDER = ['PageHeader','DocumentHeader','TableHeader','Detail','TotalsBlock','SignatureBlock','PageFooter']

export function buildMockPreviewHtml(meta, bands) {
  const pageWpx = meta.pageW * MM
  const padT    = meta.marginTop * MM
  const padB    = meta.marginBot * MM
  const padL    = meta.marginLeft * MM
  const padR    = meta.marginRight * MM

  const sorted = [...bands].sort((a, b) => {
    const ai = BAND_ORDER.indexOf(a.type), bi = BAND_ORDER.indexOf(b.type)
    return (ai === -1 ? 99 : ai) - (bi === -1 ? 99 : bi)
  })

  const bandsHtml = sorted.map(b => bandHtml(b, meta)).join('')

  return `<!DOCTYPE html>
<html><head><meta charset="utf-8">
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { background: #f3f4f6; display: flex; justify-content: center; padding: 24px; font-family: Arial, sans-serif; }
  .page { background: #fff; width: ${pageWpx}px; box-shadow: 0 4px 20px rgba(0,0,0,0.12);
    padding: ${padT}px ${padR}px ${padB}px ${padL}px; }
  .mock-badge { position: fixed; top: 8px; right: 8px; background: #f59e0b; color: #fff;
    font-size: 11px; padding: 3px 8px; border-radius: 4px; font-weight: 700; }
</style></head>
<body>
  <div class="mock-badge">MOCK VERİ</div>
  <div class="page">${bandsHtml}</div>
</body></html>`
}
