/**
 * entityRegistry — Admin "Alan Rehberi" entity tanim katalogu
 *
 * Entity = kullaniciya gosterilen mantiksal varlik (orn. "Satis Teklifi").
 * Her entity 1 veya N variant'a sahip olabilir:
 *   - Variants olmayan entity → tek formCode'a dogrudan map (formCode field).
 *   - Variants olan entity   → "Ust Bilgi / Kalem Bilgisi" gibi segmented
 *                              toggle ile variant seciminden sonra formCode'a inilir.
 *
 * hiddenAliases: dropdown'da gorunmeyen ama entity'ye ait kabul edilen
 * formCode'lar. Backend'de bunlar list/Yeni gibi sayfalar icin var; widget
 * tanimlama dropdown'da gizleriz ama entity eslestirmesi acisindan biliriz.
 *
 * Yeni entity eklerken: code (URL-safe slug), label (TR baslik), icon
 * (lucide-react ad), color (palet adi), formCode VEYA variants[] zorunlu.
 */

export var ENTITY_REGISTRY = [
  // === GENEL ===
  { code: 'notlar', label: 'Notlar', icon: 'StickyNote', color: 'amber', formCode: 'NOTES' },

  // === ONAY ISLEMLERI (e-Belgeler) ===
  { code: 'e-fatura',    label: 'e-Fatura',    icon: 'FileText', color: 'blue',  formCode: 'EINVOICE' },
  { code: 'e-arsiv',     label: 'e-Arşiv',     icon: 'Archive',  color: 'slate', formCode: 'EARCHIVE' },
  { code: 'e-irsaliye',  label: 'e-İrsaliye',  icon: 'Truck',    color: 'teal',  formCode: 'EDISPATCH' },

  // === LOJISTIK ===
  {
    // ITEMS pasifleştirildi (sadece widget/rehber kodu, controller yoktu).
    // Aktif form MATERIAL_CARD_EDIT — ModuleSelector'ın backend availability
    // kontrolü bu formCode üzerinden yapılır.
    code: 'malzeme-karti', label: 'Malzeme Kartları', icon: 'Package', color: 'indigo',
    formCode: 'MATERIAL_CARD_EDIT',
    hiddenAliases: ['ITEMS']
  },
  {
    code: 'urun-konfig', label: 'Ürün Konfigürasyonu', icon: 'Sliders', color: 'teal',
    variants: [
      { key: 'config',  label: 'Özellik & Kombinasyon', formCode: 'PRODUCT_CONFIG' },
      { key: 'feature', label: 'Özellik Düzenleme',     formCode: 'PRODUCT_FEATURE_EDIT' },
      { key: 'combo',   label: 'Kombinasyon Üretici',   formCode: 'PRODUCT_COMBINATIONS' },
    ]
  },
  {
    code: 'transfer', label: 'Depolar Arası Transfer', icon: 'Boxes', color: 'indigo',
    variants: [
      { key: 'header', label: 'Üst Bilgi',     formCode: 'TRANSFER' },
      { key: 'lines',  label: 'Kalem Bilgisi', formCode: 'TRANSFER_LINES' },
    ]
  },
  {
    code: 'stock-in', label: 'Ambar Giriş', icon: 'PackagePlus', color: 'emerald',
    variants: [
      { key: 'header', label: 'Üst Bilgi',     formCode: 'STOCK_IN' },
      { key: 'lines',  label: 'Kalem Bilgisi', formCode: 'STOCK_IN_LINES' },
    ]
  },
  {
    code: 'stock-out', label: 'Ambar Çıkış', icon: 'PackageMinus', color: 'rose',
    variants: [
      { key: 'header', label: 'Üst Bilgi',     formCode: 'STOCK_OUT' },
      { key: 'lines',  label: 'Kalem Bilgisi', formCode: 'STOCK_OUT_LINES' },
    ]
  },

  // === SATIS (header + lines) ===
  {
    code: 'satis-teklif', label: 'Satış Teklifi', icon: 'FileText', color: 'violet',
    variants: [
      { key: 'header', label: 'Üst Bilgi',     formCode: 'SALES_QUOTE_EDIT' },
      { key: 'lines',  label: 'Kalem Bilgisi', formCode: 'SALES_QUOTE_LINES' },
    ],
    hiddenAliases: ['SALES_QUOTE', 'SALES_QUOTE_NEW']
  },
  {
    code: 'satis-siparis', label: 'Satış Siparişi', icon: 'ShoppingCart', color: 'violet',
    variants: [
      { key: 'header', label: 'Üst Bilgi',     formCode: 'SALES_ORDER_EDIT' },
      { key: 'lines',  label: 'Kalem Bilgisi', formCode: 'SALES_ORDER_LINES' },
    ],
    hiddenAliases: ['SALES_ORDER', 'SALES_ORDER_NEW']
  },

  // === SATIN ALMA (header + lines) ===
  {
    code: 'ihtiyac-kaydi', label: 'İhtiyaç Kaydı', icon: 'ClipboardList', color: 'amber',
    variants: [
      { key: 'header', label: 'Üst Bilgi',     formCode: 'PURCHASE_REQUEST_EDIT' },
      { key: 'lines',  label: 'Kalem Bilgisi', formCode: 'PURCHASE_REQUEST_LINES' },
    ],
    hiddenAliases: ['PURCHASE_REQUEST', 'PURCHASE_REQUEST_NEW']
  },
  {
    code: 'sa-teklif', label: 'Satın Alma Teklif', icon: 'FileText', color: 'violet',
    variants: [
      { key: 'header', label: 'Üst Bilgi',     formCode: 'PURCHASE_QUOTE_EDIT' },
      { key: 'lines',  label: 'Kalem Bilgisi', formCode: 'PURCHASE_QUOTE_LINES' },
    ],
    hiddenAliases: ['PURCHASE_QUOTE', 'PURCHASE_QUOTE_NEW']
  },
  {
    code: 'sa-siparis', label: 'Satın Alma Sipariş', icon: 'ShoppingCart', color: 'violet',
    variants: [
      { key: 'header', label: 'Üst Bilgi',     formCode: 'PURCHASE_ORDER_EDIT' },
      { key: 'lines',  label: 'Kalem Bilgisi', formCode: 'PURCHASE_ORDER_LINES' },
    ],
    hiddenAliases: ['PURCHASE_ORDER', 'PURCHASE_ORDER_NEW']
  },

  // === URETIM ===
  {
    // PRODUCT_TREES pasifleştirildi (hiçbir controller kullanmıyordu).
    // BOM_EDIT aktif form — BomController [PermissionScope("BOM_EDIT")] + menü.
    code: 'urun-agaci', label: 'Ürün Ağacı', icon: 'GitBranch', color: 'emerald',
    formCode: 'BOM_EDIT',
    hiddenAliases: ['PRODUCT_TREES'],
  },
  { code: 'is-emri',    label: 'İş Emirleri', icon: 'ClipboardList', color: 'rose',
    formCode: 'WORK_ORDER_EDIT', hiddenAliases: ['WORK_ORDERS'] },
  { code: 'operasyon',  label: 'Operasyon',   icon: 'Hammer',        color: 'indigo',
    formCode: 'OPERATION_EDIT', hiddenAliases: ['OPERATIONS'] },
  { code: 'rota',       label: 'Rota',        icon: 'Workflow',      color: 'indigo',
    formCode: 'ROUTING_EDIT', hiddenAliases: ['ROUTINGS'] },
  { code: 'personel',        label: 'Personel',          icon: 'Users',        color: 'indigo',
    formCode: 'PERSONNEL_EDIT', hiddenAliases: ['PERSONNEL'] },
  { code: 'vardiya',         label: 'Vardiyalar',         icon: 'Clock',        color: 'slate',
    formCode: 'SHIFT_EDIT', hiddenAliases: ['SHIFTS'] },
  { code: 'aktivite-sebep',  label: 'Aktivite Sebepleri', icon: 'Tag',          color: 'amber',
    formCode: 'ACTIVITY_REASON_EDIT', hiddenAliases: ['ACTIVITY_REASONS'] },
  { code: 'rota-operasyon',  label: 'Rota Operasyonu',    icon: 'Workflow',     color: 'violet',
    formCode: 'ROUTING_OPERATION_EDIT' },
  { code: 'makine',          label: 'Makineler',          icon: 'Cog',          color: 'slate',
    formCode: 'MACHINES' },
  { code: 'makine-tipi',     label: 'Makine Tipleri',     icon: 'Settings2',    color: 'slate',
    formCode: 'MACHINE_TYPES' },

  // === FINANS ===
  { code: 'cari', label: 'Cari Hesaplar', icon: 'Building2', color: 'cyan',
    formCode: 'CONTACT_EDIT', hiddenAliases: ['CONTACTS'] },

  // === TANIMLAMALAR ===
  { code: 'departman',      label: 'Departmanlar',        icon: 'Building2', color: 'cyan',    formCode: 'DEPARTMENTS' },
  { code: 'sales-rep',      label: 'Satış Temsilcileri', icon: 'Users',     color: 'cyan',    formCode: 'SALES_REPS' },
  { code: 'doviz',          label: 'Döviz Tanımlamaları', icon: 'Coins',    color: 'amber',   formCode: 'CURRENCIES' },
  { code: 'lokasyon',       label: 'Lokasyonlar',        icon: 'MapPin',    color: 'rose',    formCode: 'LOCATIONS' },
  { code: 'olcu-birimi',    label: 'Ölçü Birimleri',     icon: 'Ruler',     color: 'slate',   formCode: 'MEASURE_UNITS' },
  { code: 'malzeme-grup',   label: 'Malzeme Grupları',   icon: 'Layers',    color: 'indigo',  formCode: 'MATERIAL_GROUPS' },
  { code: 'kart-grup',      label: 'Kart Grupları',      icon: 'FolderTree', color: 'teal',   formCode: 'CARD_GROUPS' },
  {
    code: 'fiyat-listesi', label: 'Fiyat Listesi', icon: 'Tag', color: 'emerald',
    variants: [
      { key: 'list',   label: 'Liste',  formCode: 'PRICE_LIST' },
      { key: 'groups', label: 'Gruplar', formCode: 'PRICE_GROUPS' },
    ]
  },

  // === TASARIM ===
  { code: 'belge-sablon', label: 'Belge Şablonları', icon: 'FileCode', color: 'violet', formCode: 'DOC_TEMPLATES' },
]

// ───────────────────────────────────────────────
// Helper API
// ───────────────────────────────────────────────

/**
 * Verilen formCode'a karsilik gelen entity'yi dondur. hiddenAliases ve
 * variant.formCode'lari da tarar. Bulamazsa null.
 */
export function findEntityByFormCode(code) {
  if (!code) return null
  var target = String(code).toUpperCase()
  for (var i = 0; i < ENTITY_REGISTRY.length; i++) {
    var e = ENTITY_REGISTRY[i]
    if (e.formCode && String(e.formCode).toUpperCase() === target) return e
    if (Array.isArray(e.hiddenAliases)) {
      for (var a = 0; a < e.hiddenAliases.length; a++) {
        if (String(e.hiddenAliases[a]).toUpperCase() === target) return e
      }
    }
    if (Array.isArray(e.variants)) {
      for (var v = 0; v < e.variants.length; v++) {
        if (String(e.variants[v].formCode).toUpperCase() === target) return e
      }
    }
  }
  return null
}

// ───────────────────────────────────────────────
// DB-driven entity türetme (2026-06-09)
// entityRegistry.js static listesine bağımlılığı kaldırır.
// ModuleSelector + AdminWidgetRegistryPanel bu fonksiyonları kullanır.
// ───────────────────────────────────────────────

/**
 * Backend'den gelen form listesini entity yapısına dönüştürür.
 * SubModule aynı olan formlar tek entity altında variant olarak gruplanır.
 * SubModule olmayan her form kendi başına entity olur.
 *
 * @param {Array} formDtoList — [{formCode, formName, subModule, icon, iconColor, ...}]
 * @returns {Array} — [{key, label, icon, color, formCode?, variants: [{key,label,formCode}]}]
 *   Single-form entity: variants=[], formCode dolu.
 *   Multi-form entity:  variants dolu, formCode=null.
 */
// Form adları bu listede ise widget editöründen gizle (navigasyon/oluşturma formları).
// Küçük harfle eşleştirme yapılır.
var _NAV_FORM_NAMES_LC = ['yeni', 'new']

// Form adları bu listede ise aynı SubModule altındaki diğer formlarla
// "variant" olarak gruplanır (Üst Bilgi/Kalem Bilgisi toggle).
// Bu listede OLMAYAN adlar (e-Fatura, e-Arşiv, Özellik Düzenleme vb.) ayrı
// entity satırı olarak gösterilir — SubModule sadece modül başlığı olarak kalır.
var _VARIANT_NAMES_LC = ['üst bilgi', 'kalem bilgisi', 'düzenleme']

export function buildEntitiesFromForms(formDtoList) {
  var entityMap = {}
  var entityOrder = []

  ;(formDtoList || []).forEach(function(f) {
    if (!f || !f.formCode) return
    var sub = f.subModule ? String(f.subModule).trim() : null
    var key = sub || f.formCode
    var label = sub || f.formName || f.formCode
    var icon = f.icon || 'FileText'
    var color = f.iconColor || 'slate'

    if (!entityMap[key]) {
      entityMap[key] = {
        key: key,
        label: label,
        icon: icon,
        color: color,
        variants: [],
      }
      entityOrder.push(key)
    }

    entityMap[key].variants.push({
      key: f.formCode,
      label: f.formName || f.formCode,
      formCode: f.formCode,
    })
  })

  var result = []

  entityOrder.forEach(function(k) {
    var e = entityMap[k]

    // Çok-variant grup içinde salt navigasyon formlarını (Yeni/New) filtrele.
    var variants = e.variants
    if (variants.length > 1) {
      var filtered = variants.filter(function(v) {
        return _NAV_FORM_NAMES_LC.indexOf(String(v.label).trim().toLowerCase()) === -1
      })
      if (filtered.length > 0) variants = filtered
    }

    // Çok-variant grup: yalnızca TÜM formlar "görünüm-tipi" adlara sahipse
    // grup toggle olarak gösterilir. Aksi hâlde her form kendi entity satırı olur.
    // Kural: "Üst Bilgi", "Kalem Bilgisi", "Düzenleme" → view-type → grupla.
    //        "e-Fatura", "e-Arşiv", "Özellik Düzenleme" vb. → entity-type → ayrı göster.
    if (variants.length > 1) {
      var allViewType = variants.every(function(v) {
        return _VARIANT_NAMES_LC.indexOf(String(v.label).trim().toLowerCase()) !== -1
      })

      if (!allViewType) {
        // Entity adları → her form kendi satırı
        variants.forEach(function(v) {
          result.push({
            key: v.formCode,
            label: v.label,
            icon: e.icon,
            color: e.color,
            formCode: v.formCode,
            variants: [],
          })
        })
        return  // bu SubModule grubunun işlemini bitir
      }

      // View-type grup → grouped entity (toggle aşağıda gösterilir)
      result.push({
        key: e.key,
        label: e.label,
        icon: e.icon,
        color: e.color,
        formCode: null,
        variants: variants,
      })
      return
    }

    // Tek variant → formCode doğrudan entity'de; toggle çıkmaz
    result.push({
      key: e.key,
      label: e.label,
      icon: e.icon,
      color: e.color,
      formCode: variants[0] ? variants[0].formCode : null,
      variants: [],
    })
  })

  return result
}

/**
 * Form listesinden formCode'a karşılık gelen entity'yi döndürür.
 * Hem formCode hem de variants içinde arama yapar.
 * Bulamazsa null.
 */
export function findEntityByFormCodeInForms(formDtoList, formCode) {
  if (!formDtoList || !formCode) return null
  var target = String(formCode).toUpperCase()
  var entities = buildEntitiesFromForms(formDtoList)
  for (var i = 0; i < entities.length; i++) {
    var e = entities[i]
    if (e.formCode && String(e.formCode).toUpperCase() === target) return e
    if (Array.isArray(e.variants)) {
      for (var j = 0; j < e.variants.length; j++) {
        if (String(e.variants[j].formCode).toUpperCase() === target) return e
      }
    }
  }
  return null
}

/**
 * Verilen formCode'un hangi entity'nin hangi variant'i olduguni dondur.
 * Eslesme yoksa null.
 */
export function findVariantByFormCode(code) {
  if (!code) return null
  var target = String(code).toUpperCase()
  for (var i = 0; i < ENTITY_REGISTRY.length; i++) {
    var e = ENTITY_REGISTRY[i]
    if (!Array.isArray(e.variants)) continue
    for (var v = 0; v < e.variants.length; v++) {
      if (String(e.variants[v].formCode).toUpperCase() === target) {
        return { entity: e, variant: e.variants[v] }
      }
    }
  }
  return null
}

/**
 * Entity'nin varsayilan formCode'u — variants varsa ilk variant'in formCode'u,
 * yoksa entity.formCode.
 */
export function getDefaultFormCode(entity) {
  if (!entity) return null
  if (Array.isArray(entity.variants) && entity.variants.length > 0) {
    return entity.variants[0].formCode
  }
  return entity.formCode || null
}

/**
 * Registry tarafindan yonetilen TUM formCode'lar (entity, variants, aliases).
 * Backend'den gelen form listesini filtrelemek icin kullanilir — bu listede
 * olmayan formCode'lar dropdown'a girmez.
 */
export function getAllManagedFormCodes() {
  var out = []
  ENTITY_REGISTRY.forEach(function(e) {
    if (e.formCode) out.push(String(e.formCode).toUpperCase())
    if (Array.isArray(e.hiddenAliases)) {
      e.hiddenAliases.forEach(function(a) { out.push(String(a).toUpperCase()) })
    }
    if (Array.isArray(e.variants)) {
      e.variants.forEach(function(v) { out.push(String(v.formCode).toUpperCase()) })
    }
  })
  return out
}
