import { v4 as uuidv4 } from 'crypto'

// Basit UUID üretici (crypto bağımlılığı gerektirmez)
export function makeId() {
  return Math.random().toString(36).slice(2) + Date.now().toString(36)
}

export const BAND_TYPES = [
  { type: 'PageHeader',     label: 'Sayfa Başlığı',       defaultHeight: 25, repeats: true },
  { type: 'DocumentHeader', label: 'Belge Başlığı',        defaultHeight: 35, repeats: false },
  { type: 'TableHeader',    label: 'Tablo Başlığı',        defaultHeight: 8,  repeats: true },
  { type: 'Detail',         label: 'Kalem Satırı',         defaultHeight: 7,  repeats: false },
  { type: 'TotalsBlock',    label: 'Toplamlar',            defaultHeight: 30, repeats: false },
  { type: 'SignatureBlock', label: 'İmza Alanı',           defaultHeight: 25, repeats: false },
  { type: 'PageFooter',     label: 'Sayfa Altbilgisi',     defaultHeight: 12, repeats: true },
]

export const ELEMENT_KINDS = [
  { kind: 'Label',         label: 'Metin',         icon: 'T' },
  { kind: 'BoundField',    label: 'Veri Alanı',    icon: '{}' },
  { kind: 'Image',         label: 'Görsel',        icon: '🖼' },
  { kind: 'Shape',         label: 'Şekil',         icon: '□' },
  { kind: 'AmountInWords', label: 'Yazı ile Tutar',icon: '₺' },
  { kind: 'PageNumber',    label: 'Sayfa No',      icon: '#' },
  { kind: 'DateTimeNow',   label: 'Tarih/Saat',    icon: '📅' },
]

export function makeDefaultElement(kind, x = 10, y = 2) {
  return {
    id: makeId(),
    kind,
    x, y, w: 60, h: 8,
    text: kind === 'Label' ? 'Metin' : null,
    style: { fontSize: 10, bold: false, italic: false, underline: false,
             align: 'left', color: '#000000', bgColor: 'transparent', border: false },
    binding: null,
    format: null,
    expression: null,
    shapeKind: 'Rectangle',
    imageSource: null,
    zIndex: 0
  }
}

export function makeDefaultBand(type) {
  const def = BAND_TYPES.find(b => b.type === type) ?? { defaultHeight: 20, repeats: false }
  return {
    id: makeId(),
    type,
    height: def.defaultHeight,
    repeatOnEveryPage: def.repeats,
    dataAlias: type === 'Detail' ? 'Lines' : null,
    canGrow: type === 'Detail',
    elements: []
  }
}

export const initialState = {
  meta: {
    id: 0, code: '', name: '', docType: 'sales_quote',
    description: '', pageW: 210, pageH: 297,
    marginTop: 10, marginBot: 10, marginLeft: 15, marginRight: 10,
    isDefault: false
  },
  bands: [],
  dataSources: [],
  selectedElementId: null,
  selectedBandId: null,
  dirty: false,
  saving: false,
  previewing: false,
  previewHtml: null,
  error: null
}

export function reducer(state, action) {
  switch (action.type) {
    case 'LOAD_LAYOUT':
      return {
        ...state,
        meta: {
          id: action.payload.id,
          code: action.payload.code,
          name: action.payload.name,
          docType: action.payload.docType,
          description: action.payload.description ?? '',
          pageW: action.payload.pageW,
          pageH: action.payload.pageH,
          marginTop: action.payload.marginTop,
          marginBot: action.payload.marginBot,
          marginLeft: action.payload.marginLeft,
          marginRight: action.payload.marginRight,
          isDefault: action.payload.isDefault
        },
        bands: parseBandsFromJson(action.payload.layoutJson),
        dataSources: action.payload.dataSources ?? [],
        dirty: false,
        selectedElementId: null,
        selectedBandId: null
      }

    case 'SET_META':
      return { ...state, meta: { ...state.meta, ...action.payload }, dirty: true }

    case 'ADD_BAND': {
      const newBand = makeDefaultBand(action.bandType)
      return { ...state, bands: [...state.bands, newBand], dirty: true }
    }

    case 'REMOVE_BAND':
      return {
        ...state,
        bands: state.bands.filter(b => b.id !== action.bandId),
        selectedBandId: state.selectedBandId === action.bandId ? null : state.selectedBandId,
        selectedElementId: null,
        dirty: true
      }

    case 'RESIZE_BAND':
      return {
        ...state,
        bands: state.bands.map(b => b.id === action.bandId ? { ...b, height: Math.max(5, action.height) } : b),
        dirty: true
      }

    case 'UPDATE_BAND':
      return {
        ...state,
        bands: state.bands.map(b => b.id === action.bandId ? { ...b, ...action.patch } : b),
        dirty: true
      }

    case 'SELECT_BAND':
      return { ...state, selectedBandId: action.bandId, selectedElementId: null }

    case 'ADD_ELEMENT': {
      const el = makeDefaultElement(action.kind, action.x ?? 10, action.y ?? 2)
      if (action.binding) el.binding = action.binding
      if (action.text) el.text = action.text
      // z-index = mevcut maksimum + 1
      const maxZ = state.bands.find(b => b.id === action.bandId)?.elements
        .reduce((m, e) => Math.max(m, e.zIndex ?? 0), 0) ?? 0
      el.zIndex = maxZ + 1
      return {
        ...state,
        bands: state.bands.map(b =>
          b.id === action.bandId ? { ...b, elements: [...b.elements, el] } : b),
        selectedElementId: el.id,
        selectedBandId: action.bandId,
        dirty: true
      }
    }

    case 'SELECT_ELEMENT':
      return {
        ...state,
        selectedElementId: action.elementId,
        selectedBandId: action.bandId ?? state.selectedBandId
      }

    case 'DESELECT':
      return { ...state, selectedElementId: null, selectedBandId: null }

    case 'MOVE_ELEMENT':
      return {
        ...state,
        bands: state.bands.map(b =>
          b.id === action.bandId
            ? { ...b, elements: b.elements.map(e =>
                e.id === action.elementId
                  ? { ...e, x: snapToGrid(action.x), y: snapToGrid(action.y) }
                  : e) }
            : b),
        dirty: true
      }

    case 'RESIZE_ELEMENT':
      return {
        ...state,
        bands: state.bands.map(b =>
          b.id === action.bandId
            ? { ...b, elements: b.elements.map(e =>
                e.id === action.elementId
                  ? { ...e, w: Math.max(5, action.w), h: Math.max(3, action.h) }
                  : e) }
            : b),
        dirty: true
      }

    case 'UPDATE_ELEMENT':
      return {
        ...state,
        bands: state.bands.map(b => ({
          ...b,
          elements: b.elements.map(e =>
            e.id === action.elementId ? { ...e, ...action.patch } : e)
        })),
        dirty: true
      }

    case 'DELETE_ELEMENT':
      return {
        ...state,
        bands: state.bands.map(b => ({
          ...b, elements: b.elements.filter(e => e.id !== action.elementId)
        })),
        selectedElementId: state.selectedElementId === action.elementId ? null : state.selectedElementId,
        dirty: true
      }

    case 'SET_DATASOURCES':
      return { ...state, dataSources: action.dataSources, dirty: true }

    case 'SET_SAVING':
      return { ...state, saving: action.saving }

    case 'SAVE_SUCCESS':
      return { ...state, dirty: false, saving: false, meta: { ...state.meta, id: action.id } }

    case 'SET_PREVIEWING':
      return { ...state, previewing: action.previewing }

    case 'SET_PREVIEW_HTML':
      return { ...state, previewHtml: action.html, previewing: false }

    case 'SET_ERROR':
      return { ...state, error: action.error, saving: false, previewing: false }

    default:
      return state
  }
}

// Helpers
function snapToGrid(val, grid = 2) {
  return Math.round(val / grid) * grid
}

function parseBandsFromJson(layoutJson) {
  try {
    const doc = JSON.parse(layoutJson)
    return (doc.bands ?? []).map(b => ({
      ...b,
      id: b.id ?? makeId(),
      elements: (b.elements ?? []).map(e => ({
        ...e,
        id: e.id ?? makeId(),
        zIndex: e.zIndex ?? 0,
        style: e.style ?? { fontSize: 10, bold: false, italic: false, underline: false,
                           align: 'left', color: '#000000', bgColor: 'transparent', border: false }
      })).sort((a, b) => (a.zIndex ?? 0) - (b.zIndex ?? 0))
    }))
  } catch {
    return []
  }
}

export function buildLayoutJson(meta, bands) {
  return JSON.stringify({
    pageWidth: meta.pageW,
    pageHeight: meta.pageH,
    margins: { top: meta.marginTop, bottom: meta.marginBot, left: meta.marginLeft, right: meta.marginRight },
    bands: bands.map(b => ({
      ...b,
      elements: [...b.elements].sort((a, e) => (a.zIndex ?? 0) - (e.zIndex ?? 0))
    }))
  })
}
