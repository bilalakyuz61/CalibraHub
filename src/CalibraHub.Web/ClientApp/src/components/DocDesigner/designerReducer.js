// Bant render sirasi — sema tarafindaki gercek dizilim. PDF'te ve mail HTML'inde
// hep bu sirayla iterate edilir. mail_body, mail sablonlarinin "Detay" bandi
// karsiligi oldugu icin Detail bandinin yaninda yer alir.
export const BAND_ORDER = [
  'PageHeader', 'DocumentHeader', 'TableHeader',
  'Detail', 'mail_body',
  'SubDetailHeader', 'SubDetail', 'SubDetailFooter',
  'TotalsBlock', 'SignatureBlock',
  'PageFooter',
]

export function sortBandsForRender(bands) {
  const idx = b => {
    const i = BAND_ORDER.indexOf(b.type)
    return i === -1 ? 99 : i
  }
  return [...bands].sort((a, b) => idx(a) - idx(b))
}

export const BAND_TYPES = [
  { type: 'PageHeader',       label: 'Sayfa Başlığı',     defaultHeight: 25 },
  { type: 'DocumentHeader',   label: 'Belge Başlığı',     defaultHeight: 40 },
  { type: 'TableHeader',      label: 'Tablo Başlığı',     defaultHeight: 8  },
  { type: 'Detail',           label: 'Detay Satırı',      defaultHeight: 7  },
  { type: 'SubDetailHeader',  label: 'Alt Detay Başlığı', defaultHeight: 7  },
  { type: 'SubDetail',        label: 'Alt Detay Satırı',  defaultHeight: 6  },
  { type: 'SubDetailFooter',  label: 'Alt Detay Altı',    defaultHeight: 8  },
  { type: 'TotalsBlock',      label: 'Toplam Bloku',      defaultHeight: 30 },
  { type: 'SignatureBlock',   label: 'İmza Bloku',        defaultHeight: 25 },
  { type: 'PageFooter',       label: 'Sayfa Altı',        defaultHeight: 15 },
  // Mail sablonu placeholder bandi — DocLayout.OutputFormat='email' iken kullanilir.
  // Render sirasinda kullanici tarafindan girilen "mail govdesi" buraya yerlesir.
  // Bu bandda element olmaz; sadece runtime'da islenecek bir isaretci.
  { type: 'mail_body',        label: 'Mail Gövdesi',      defaultHeight: 40, mailOnly: true },
]

// QR ayri bir Kind degil — Barcode'un bir tipi (barcodeType='QR'). Kullanici
// toolbox'tan Barkod ekler, barkod tipinden QR'a gecebilir. Boylelikle veri
// secimi (cift-tikla, alias.col) tum barkod tiplerinde tek noktadan calisir.
export const ELEMENT_KINDS = [
  { kind: 'Label',         label: 'Etiket',          icon: '𝐓' },
  { kind: 'BoundField',    label: 'Veri Alanı',      icon: '⟨⟩' },
  { kind: 'Image',         label: 'Resim',           icon: '🖼' },
  { kind: 'Shape',         label: 'Şekil',           icon: '▬' },
  { kind: 'Barcode',       label: 'Barkod',          icon: '▥' },
  { kind: 'AmountInWords', label: 'Yazı ile Tutar',  icon: '₺' },
  { kind: 'PageNumber',    label: 'Sayfa No',        icon: '#' },
  { kind: 'DateTimeNow',   label: 'Tarih/Saat',      icon: '📅' },
]

export const BARCODE_TYPES = [
  { value: 'Code128', label: 'Code 128' },
  { value: 'Code39',  label: 'Code 39'  },
  { value: 'EAN13',   label: 'EAN-13'   },
  { value: 'EAN8',    label: 'EAN-8'    },
  { value: 'UPCA',    label: 'UPC-A'    },
  { value: 'ITF',     label: 'ITF (Interleaved 2/5)' },
  { value: 'Codabar', label: 'Codabar'  },
  { value: 'QR',      label: 'QR Kod'   },
]

/**
 * Backward-compat: eski tasarimlarda kind='QrCode' olarak kaydedilmis elementleri
 * yeni semaya tasi: kind='Barcode', barcodeType='QR'. Kayitli verileri silmemek
 * icin load sirasinda cagrilir; geri yazimda yeni sema kullanilir.
 */
export function normalizeLegacyQrCode(el) {
  if (el && el.kind === 'QrCode') {
    return {
      ...el,
      kind: 'Barcode',
      barcodeType: 'QR',
      qrErrorCorrection: el.qrErrorCorrection ?? 'M',
      showBarcodeText: el.showBarcodeText ?? false,
    }
  }
  return el
}

export function makeId() {
  return Math.random().toString(36).slice(2) + Date.now().toString(36)
}

export function makeDefaultElement(kind, x = 0, y = 0, binding = null) {
  const needsBinding = kind === 'BoundField' || kind === 'AmountInWords' || kind === 'Barcode'
  return {
    id: makeId(),
    kind,
    x, y,
    w: kind === 'Shape' ? 50
      : kind === 'Image' ? 40
      : kind === 'AmountInWords' ? 80
      : kind === 'Barcode' ? 50
      : 50,
    h: kind === 'Shape' ? 1
      : kind === 'Image' ? 15
      : kind === 'Barcode' ? 15
      : 8,
    text: kind === 'Label' ? 'Yeni Etiket' : null,
    zIndex: 0,
    style: { fontSize: 10, bold: false, italic: false, underline: false, align: 'left', verticalAlign: 'middle', overflow: 'ellipsis', color: '#000000', bgColor: 'transparent', border: false },
    binding: binding ?? (needsBinding ? { alias: '', col: '' } : null),
    format: null,
    // Barcode: tip Code128 default; kullanici QR secerse qrErrorCorrection devreye girer
    barcodeType: kind === 'Barcode' ? 'Code128' : null,
    showBarcodeText: kind === 'Barcode' ? true : null,
    qrErrorCorrection: kind === 'Barcode' ? 'M' : null,
    imageSrc: kind === 'Image' ? null  : null,            // base64 / URL
    imageFit: kind === 'Image' ? 'contain' : null,        // contain | stretch | original

    // Davranış
    visible:          true,    // Görünür
    printable:        true,    // PDF/print çıktısında basılır
    rotation:         0,       // 0 / 90 / 180 / 270 (derece)
    suppressRepeated: false,   // Detail satırında aynı değer tekrarlanmasın
    hideZeros:        false,   // Sayı 0 ise boş gösterilsin
  }
}

export function makeDefaultBand(type) {
  const def = BAND_TYPES.find(b => b.type === type)
  return {
    id: makeId(),
    type,
    height: def?.defaultHeight ?? 20,
    repeatOnEveryPage: type === 'PageHeader' || type === 'PageFooter' || type === 'TableHeader',
    canGrow: type === 'Detail' || type === 'SubDetail',
    dataAlias: null,
    elements: [],

    // Sayfa akışı ayarları
    startNewPage:         false,   // bant öncesi yeni sayfaya geç
    printIfDetailEmpty:   true,    // detay yoksa bile basılsın (Detail bandı dışında anlamlı)
    allowSplit:           false,   // bant satır arasında bölünebilir mi (canGrow ile birlikte)
    keepDetailTogether:   true,    // master + detail aynı sayfada kalsın
  }
}

const defaultMeta = {
  id: 0, code: '', name: '', docType: 'sales_quote',
  pageW: 210, pageH: 297,
  marginTop: 10, marginBot: 10, marginLeft: 15, marginRight: 10,
  isDefault: false,
  // 2026-05-20: outputFormat artik 'pdf'e sabit (UI dropdown'i kaldirildi); legacy
  // defaultSubject/Body ve defaultsView*/Where alanlari backward-compat icin tutulur
  // ama Save sirasinda null olarak gonderilir. Mail sablonu kullanim niyeti yeni
  // bayrakla: useAsMailTemplate.
  outputFormat: 'pdf',
  defaultSubject: '',
  defaultBody: '',
  defaultsViewName: '',
  defaultsSubjectColumn: '',
  defaultsBodyColumn: '',
  defaultsWhere: '',
  // Yeni: bu dizayn mail compose ekraninda sablon olarak listelensin mi?
  useAsMailTemplate: false,
}

export const initialState = {
  meta: defaultMeta,
  bands: [],
  dataSources: [],
  selectedElementId: null,
  selectedElementIds: [],   // multi-select
  selectedBandId: null,
  editingElementId: null,   // çift tıklama → editor modal
  clipboard: [],            // copy/paste
  dirty: false,
  saving: false,
  previewing: false,
  previewHtml: null,
  error: null,
}

// 0.1mm hassasiyetinde yuvarlama — kullanıcı fareyle bıraktığı yer korunur,
// görsel snap yok. Önceden 2mm'ye yuvarlanıyordu; kullanıcı "fareyle verdiğim
// hassasiyeti round ediyor" diye raporladı. 0.1mm float kirini temizler ama
// görsel olarak fark edilmez.
function snapToGrid(val, grid = 0.1) {
  return Math.round(val / grid) * grid
}

/** Tüm bandlardaki elementleri düz liste olarak döner */
function allElements(state) {
  return state.bands.flatMap(b => b.elements)
}

export function reducer(state, action) {
  switch (action.type) {

    case 'LOAD': {
      const { layout } = action
      return {
        ...state,
        meta: {
          id: layout.id, code: layout.code, name: layout.name, docType: layout.docType,
          documentTypeId: layout.documentTypeId ?? null,
          pageW: layout.pageW, pageH: layout.pageH,
          marginTop: layout.marginTop, marginBot: layout.marginBot,
          marginLeft: layout.marginLeft, marginRight: layout.marginRight,
          isDefault: layout.isDefault ?? false,
          outputFormat: layout.outputFormat ?? 'pdf',
          defaultSubject: layout.defaultSubject ?? '',
          defaultBody:    layout.defaultBody    ?? '',
          defaultsViewName:      layout.defaultsViewName      ?? '',
          defaultsSubjectColumn: layout.defaultsSubjectColumn ?? '',
          defaultsBodyColumn:    layout.defaultsBodyColumn    ?? '',
          defaultsWhere:         layout.defaultsWhere         ?? '',
          useAsMailTemplate:     layout.useAsMailTemplate     ?? false,
        },
        bands: parseBands(layout.layoutJson),
        dataSources: layout.dataSources ?? [],
        selectedElementId: null,
        selectedElementIds: [],
        clipboard: [],
        dirty: false,
      }
    }

    case 'SET_META':
      return { ...state, meta: { ...state.meta, ...action.payload }, dirty: true }

    case 'ADD_BAND': {
      const band = makeDefaultBand(action.bandType)
      return { ...state, bands: [...state.bands, band], dirty: true }
    }

    case 'UPDATE_BAND': {
      const bands = state.bands.map(b => b.id === action.bandId ? { ...b, ...action.patch } : b)
      return { ...state, bands, dirty: true }
    }

    case 'RESIZE_BAND': {
      const bands = state.bands.map(b =>
        b.id === action.bandId ? { ...b, height: Math.max(4, action.height) } : b)
      return { ...state, bands, dirty: true }
    }

    case 'DELETE_BAND': {
      return {
        ...state,
        bands: state.bands.filter(b => b.id !== action.bandId),
        selectedBandId: state.selectedBandId === action.bandId ? null : state.selectedBandId,
        dirty: true,
      }
    }

    case 'ADD_ELEMENT': {
      const el = action.element ?? makeDefaultElement(action.kind, action.x ?? 0, action.y ?? 0, action.binding ?? null)
      const bands = state.bands.map(b =>
        b.id === action.bandId ? { ...b, elements: [...b.elements, el] } : b)
      return { ...state, bands, selectedElementId: el.id, selectedElementIds: [el.id], selectedBandId: action.bandId, dirty: true }
    }

    case 'UPDATE_ELEMENT': {
      const bands = state.bands.map(b => ({
        ...b,
        elements: b.elements.map(e =>
          e.id === action.elementId ? { ...e, ...action.patch } : e),
      }))
      return { ...state, bands, dirty: true }
    }

    case 'UPDATE_ELEMENTS_BULK': {
      // Çoklu seçimde toplu property güncelleme. patch = element-level patch
      // (style güncellemesi için patch.style: {...} kullan).
      const ids = new Set(action.elementIds ?? [])
      if (ids.size === 0) return state
      const bands = state.bands.map(b => ({
        ...b,
        elements: b.elements.map(e => {
          if (!ids.has(e.id)) return e
          const next = { ...e, ...action.patch }
          // style merge — patch.style varsa mevcut style ile birleştir, override değil
          if (action.patch.style) {
            next.style = { ...e.style, ...action.patch.style }
          }
          return next
        }),
      }))
      return { ...state, bands, dirty: true }
    }

    case 'MOVE_ELEMENT': {
      const bands = state.bands.map(b => ({
        ...b,
        elements: b.elements.map(e =>
          e.id === action.elementId
            ? { ...e, x: snapToGrid(action.x), y: snapToGrid(action.y) }
            : e),
      }))
      return { ...state, bands, dirty: true }
    }

    case 'RESIZE_ELEMENT': {
      const bands = state.bands.map(b => ({
        ...b,
        elements: b.elements.map(e =>
          e.id === action.elementId
            // Min 0.5mm (Konva boundBoxFunc'ta zaten enforce ediliyor — burada da
            // güvenlik için aynı). 5×3mm clamp kaldırıldı; ince çizgi elementleri
            // (imza altı, sayfa altı) tasarımcıda küçültülebilsin.
            ? { ...e, w: Math.max(0.5, snapToGrid(action.w)), h: Math.max(0.5, snapToGrid(action.h)) }
            : e),
      }))
      return { ...state, bands, dirty: true }
    }

    case 'DELETE_ELEMENT': {
      const targetIds = action.elementIds ?? (action.elementId ? [action.elementId] : [])
      const bands = state.bands.map(b => ({
        ...b, elements: b.elements.filter(e => !targetIds.includes(e.id)),
      }))
      return {
        ...state, bands,
        selectedElementId: targetIds.includes(state.selectedElementId) ? null : state.selectedElementId,
        selectedElementIds: state.selectedElementIds.filter(id => !targetIds.includes(id)),
        dirty: true,
      }
    }

    case 'SELECT_ELEMENT':
      return {
        ...state,
        selectedElementId: action.elementId,
        selectedElementIds: [action.elementId],
        selectedBandId: action.bandId ?? state.selectedBandId,
      }

    case 'MULTI_SELECT_ELEMENT': {
      const { elementId, bandId } = action
      const already = state.selectedElementIds.includes(elementId)
      const newIds = already
        ? state.selectedElementIds.filter(id => id !== elementId)
        : [...state.selectedElementIds, elementId]
      return {
        ...state,
        selectedElementIds: newIds,
        selectedElementId: newIds.length === 1 ? newIds[0] : (already ? null : state.selectedElementId),
        selectedBandId: bandId ?? state.selectedBandId,
      }
    }

    case 'SELECT_BAND':
      return { ...state, selectedBandId: action.bandId, selectedElementId: null, selectedElementIds: [] }

    case 'DESELECT':
      return { ...state, selectedElementId: null, selectedElementIds: [], selectedBandId: null }

    // ── Hizalama araçları ────────────────────────────────────────────────────

    case 'ALIGN_ELEMENTS': {
      const ids = state.selectedElementIds
      if (ids.length < 2) return state
      const els = allElements(state)
      const selected = ids.map(id => els.find(e => e.id === id)).filter(Boolean)
      const anchor = selected[0]
      const patches = {}
      selected.forEach(el => {
        switch (action.align) {
          case 'left':    patches[el.id] = { x: anchor.x }; break
          case 'right':   patches[el.id] = { x: anchor.x + anchor.w - el.w }; break
          case 'centerH': patches[el.id] = { x: anchor.x + (anchor.w - el.w) / 2 }; break
          case 'top':     patches[el.id] = { y: anchor.y }; break
          case 'bottom':  patches[el.id] = { y: anchor.y + anchor.h - el.h }; break
          case 'centerV': patches[el.id] = { y: anchor.y + (anchor.h - el.h) / 2 }; break
          default: break
        }
      })
      const bands = state.bands.map(b => ({
        ...b, elements: b.elements.map(e => patches[e.id] ? { ...e, ...patches[e.id] } : e),
      }))
      return { ...state, bands, dirty: true }
    }

    case 'DISTRIBUTE_ELEMENTS': {
      const ids = state.selectedElementIds
      if (ids.length < 3) return state
      const els = allElements(state)
      const selected = ids.map(id => els.find(e => e.id === id)).filter(Boolean)
      const patches = {}

      if (action.axis === 'h') {
        const sorted = [...selected].sort((a, b) => a.x - b.x)
        const first = sorted[0], last = sorted[sorted.length - 1]
        const totalW = sorted.reduce((s, e) => s + e.w, 0)
        const gap = (last.x + last.w - first.x - totalW) / (sorted.length - 1)
        let cur = first.x
        sorted.forEach(el => { patches[el.id] = { x: snapToGrid(cur) }; cur += el.w + gap })
      } else {
        const sorted = [...selected].sort((a, b) => a.y - b.y)
        const first = sorted[0], last = sorted[sorted.length - 1]
        const totalH = sorted.reduce((s, e) => s + e.h, 0)
        const gap = (last.y + last.h - first.y - totalH) / (sorted.length - 1)
        let cur = first.y
        sorted.forEach(el => { patches[el.id] = { y: snapToGrid(cur) }; cur += el.h + gap })
      }

      const bands = state.bands.map(b => ({
        ...b, elements: b.elements.map(e => patches[e.id] ? { ...e, ...patches[e.id] } : e),
      }))
      return { ...state, bands, dirty: true }
    }

    case 'NUDGE_ELEMENT': {
      const { dx, dy } = action
      const ids = state.selectedElementIds.length > 0
        ? state.selectedElementIds
        : state.selectedElementId ? [state.selectedElementId] : []
      if (ids.length === 0) return state
      const bands = state.bands.map(b => ({
        ...b,
        elements: b.elements.map(e =>
          ids.includes(e.id)
            ? { ...e, x: snapToGrid(e.x + (dx ?? 0)), y: snapToGrid(e.y + (dy ?? 0)) }
            : e),
      }))
      return { ...state, bands, dirty: true }
    }

    // ── Kopyala / Yapıştır ────────────────────────────────────────────────────

    case 'COPY_ELEMENTS': {
      const ids = state.selectedElementIds.length > 0
        ? state.selectedElementIds
        : state.selectedElementId ? [state.selectedElementId] : []
      const clipboard = ids
        .map(id => allElements(state).find(e => e.id === id))
        .filter(Boolean)
      return { ...state, clipboard }
    }

    case 'PASTE_ELEMENTS': {
      if (!state.clipboard.length || !state.selectedBandId) return state
      const newEls = state.clipboard.map(el => ({
        ...el, id: makeId(), x: el.x + 5, y: el.y + 5,
      }))
      const bands = state.bands.map(b =>
        b.id === state.selectedBandId ? { ...b, elements: [...b.elements, ...newEls] } : b)
      const newIds = newEls.map(e => e.id)
      return {
        ...state, bands,
        selectedElementIds: newIds,
        selectedElementId: newIds[0] ?? null,
        dirty: true,
      }
    }

    // ── Veri kaynağı ─────────────────────────────────────────────────────────

    case 'ADD_DATASOURCE': {
      const exists = state.dataSources.find(d => d.alias === action.source.alias)
      if (exists) return state
      return { ...state, dataSources: [...state.dataSources, action.source], dirty: true }
    }

    case 'UPDATE_DATASOURCE': {
      const dataSources = state.dataSources.map(d =>
        d.alias === action.alias ? { ...d, ...action.patch } : d)
      return { ...state, dataSources, dirty: true }
    }

    case 'REMOVE_DATASOURCE': {
      return { ...state, dataSources: state.dataSources.filter(d => d.alias !== action.alias), dirty: true }
    }

    case 'OPEN_ELEMENT_EDITOR':  return { ...state, editingElementId: action.elementId }
    case 'CLOSE_ELEMENT_EDITOR': return { ...state, editingElementId: null }

    case 'SET_SAVING':       return { ...state, saving: action.value }
    case 'MARK_SAVED':       return { ...state, saving: false, dirty: false, meta: { ...state.meta, id: action.id ?? state.meta.id } }
    case 'SET_PREVIEWING':   return { ...state, previewing: action.value }
    case 'SET_PREVIEW_HTML': return { ...state, previewHtml: action.html, previewing: false }
    case 'SET_ERROR':        return { ...state, error: action.message, saving: false, previewing: false }
    case 'CLEAR_ERROR':      return { ...state, error: null }

    default: return state
  }
}

// ── Serialization ─────────────────────────────────────────────────────────────

function parseBands(layoutJson) {
  try {
    const doc = JSON.parse(layoutJson)
    const bands = doc.bands ?? []
    // Backward-compat: eski tasarimlarda QrCode ayri kind idi; yeni semaya cevir.
    return bands.map(b => ({
      ...b,
      elements: (b.elements ?? []).map(normalizeLegacyQrCode),
    }))
  } catch {
    return []
  }
}

export function buildLayoutJson(meta, bands) {
  // Bantlari render sirasi BAND_ORDER ile sirala — kullanici "Mail Govdesi" bandini
  // sonradan eklese bile JSON'da Page Header ile Page Footer arasinda saklanir,
  // mail render eden backend (MailTemplateRenderer) bu sirayla render eder.
  const sortedBands = sortBandsForRender(bands)
  return JSON.stringify({
    pageWidth:  meta.pageW,
    pageHeight: meta.pageH,
    margins: { top: meta.marginTop, bottom: meta.marginBot, left: meta.marginLeft, right: meta.marginRight },
    bands: sortedBands.map(b => ({
      ...b,
      elements: [...b.elements].sort((a, z) => a.zIndex - z.zIndex),
    })),
  })
}
