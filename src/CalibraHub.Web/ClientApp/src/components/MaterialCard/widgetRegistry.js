/**
 * widgetRegistry — Malzeme kartlari icin tum olasi widget tanimlari.
 *
 * Her widget:
 *   - id:           Benzersiz anahtar (config'te kullanilir)
 *   - icon:         Lucide icon component (asla serialize edilmez, runtime'da cozulur)
 *   - label:        Kullaniciya gosterilen isim
 *   - defaultColor: Renk havuzundan varsayilan
 *   - category:     Widget havuzunda gruplama icin
 *   - getValue(item): API item'indan deger cikarir (null donebilir — widget gizlenir)
 *   - getDetail(item): Tooltip'te gorunen aciklama
 *   - getDynamicColor(item) (ops): Rengin dinamik belirlenmesi gerekiyorsa
 */
import {
  Scale, Tag, ShieldCheck, Package, DollarSign, Warehouse,
  Hash, Calendar, FileText, Layers,
} from 'lucide-react'

var widgetRegistry = [
  // ── Temel Bilgiler ────────────────────────────────────
  {
    id: 'unit',
    icon: Scale,
    label: 'Olcu Birimi',
    defaultColor: 'cyan',
    category: 'temel',
    getValue: function(item) { return item.unitName || null },
    getDetail: function() { return 'Ana olcu birimi' },
  },
  {
    id: 'type',
    icon: Tag,
    label: 'Malzeme Tipi',
    defaultColor: 'slate',
    category: 'temel',
    getValue: function(item) {
      return item.materialTypeId != null ? String(item.materialTypeId) : null
    },
    getDetail: function() { return 'Malzeme tipi' },
  },
  {
    id: 'status',
    icon: ShieldCheck,
    label: 'Durum',
    defaultColor: 'emerald',
    category: 'temel',
    getValue: function(item) { return item.isActive ? 'Aktif' : 'Pasif' },
    getDetail: function(item) {
      return item.isActive ? 'Malzeme aktif durumda' : 'Malzeme pasif durumda'
    },
    getDynamicColor: function(item) { return item.isActive ? 'emerald' : 'rose' },
  },
  {
    id: 'code',
    icon: Hash,
    label: 'Kod',
    defaultColor: 'indigo',
    category: 'temel',
    getValue: function(item) { return item.materialCode || null },
    getDetail: function(item) { return 'Malzeme kodu: ' + (item.materialCode || '') },
  },

  // ── Lojistik (API'den gelecek, simdilik placeholder) ─
  {
    id: 'stock',
    icon: Package,
    label: 'Stok Miktari',
    defaultColor: 'blue',
    category: 'lojistik',
    getValue: function(item) {
      return item.stockQuantity != null ? String(item.stockQuantity) : null
    },
    getDetail: function() { return 'Mevcut stok miktari' },
  },
  {
    id: 'warehouse',
    icon: Warehouse,
    label: 'Depo',
    defaultColor: 'teal',
    category: 'lojistik',
    getValue: function(item) { return item.warehouseName || null },
    getDetail: function() { return 'Ana depo lokasyonu' },
  },

  // ── Ticari (API'den gelecek) ─────────────────────────
  {
    id: 'price',
    icon: DollarSign,
    label: 'Birim Fiyat',
    defaultColor: 'amber',
    category: 'ticari',
    getValue: function(item) {
      return item.unitPrice != null ? '₺' + Number(item.unitPrice).toFixed(2) : null
    },
    getDetail: function() { return 'KDV haric birim fiyat' },
  },

]

export var colorOptions = [
  { id: 'emerald', label: 'Yesil',   hex: '#10b981' },
  { id: 'amber',   label: 'Sari',    hex: '#f59e0b' },
  { id: 'blue',    label: 'Mavi',    hex: '#3b82f6' },
  { id: 'violet',  label: 'Mor',     hex: '#8b5cf6' },
  { id: 'cyan',    label: 'Turkuaz', hex: '#06b6d4' },
  { id: 'rose',    label: 'Pembe',   hex: '#f43f5e' },
  { id: 'slate',   label: 'Gri',     hex: '#64748b' },
  { id: 'indigo',  label: 'Indigo',  hex: '#6366f1' },
  { id: 'teal',    label: 'Teal',    hex: '#14b8a6' },
  { id: 'orange',  label: 'Turuncu', hex: '#f97316' },
]

export var categories = [
  { id: 'temel',    label: 'Temel Bilgiler' },
  { id: 'lojistik', label: 'Lojistik' },
  { id: 'ticari',   label: 'Ticari' },
  { id: 'teknik',   label: 'Teknik' },
]

/**
 * Varsayilan widget konfigurasyonu — hicbir ayar yoksa bu kullanilir.
 */
export var DEFAULT_CONFIG = {
  visibleIds: ['unit', 'type', 'status'],
  order: ['unit', 'type', 'status'],
  colors: {},
}

/**
 * ID'ye gore widget'i bulur.
 */
export function getWidgetById(id) {
  for (var i = 0; i < widgetRegistry.length; i++) {
    if (widgetRegistry[i].id === id) return widgetRegistry[i]
  }
  return null
}

export default widgetRegistry
