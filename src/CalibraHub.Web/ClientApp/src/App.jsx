/**
 * Standalone preview for CalibraSmartBoard
 *
 * 3 farkli SmartBoard'u alt alta gosterir:
 *   1. Malzeme Kartlari  (Stok, Fiyat, Birim, Depo, Urun Agaci link)
 *   2. Cari Hesaplar     (Bakiye, Risk, Telefon, Tip, Ekstre Al link)
 *   3. Satis Teklifleri  (Tutar, Gecerlilik, Kalem, Siparise Cevir link)
 *
 * Hicbir API cagrisi yok. Hicbir entity-specific kod yok.
 * Tum veriler JSON props olarak iletilir.
 */
import { SmartBoard } from './components/CalibraSmartBoard'

/* ═════════════════════════════════════════════════════════
   MOCK DATASET 1 — MALZEME KARTLARI
   ═════════════════════════════════════════════════════════ */
var materialBoardConfig = {
  boardKey: 'preview-material-cards',
  title: 'Malzeme Kartlari',
  subtitle: '4 malzeme',
  icon: 'Package',
  iconColor: 'indigo',
  searchPlaceholder: 'Malzeme ara... (kod, isim)',
  actions: [
    { id: 'new', label: 'Yeni Malzeme', icon: 'Package', variant: 'primary', url: '/Logistics/MaterialCardEdit' }
  ],
  entities: [
    {
      // Bu kart, generic dataType factory'nin yeteneklerini gosterir.
      // Widget'larin coğunda icon/color TANIMLI DEGIL — factory dataType'tan cozumler.
      id: 'mat-1',
      title: 'Celik Levha 2mm',
      subtitle: 'CLK-2MM-A',
      description: 'St37 kalite, galvanizli',
      statusBadge: { label: 'Aktif', color: 'emerald' },
      widgets: [
        // dataType: numeric → Hash ikonu + tr-TR format (1.250)
        { id: 'stock',  type: 'data', dataType: 'numeric',  label: 'Stok',    value: 1250,       detail: 'Adet | Min: 200' },
        // dataType: currency → DollarSign ikonu + "₺ 45,50" formati
        { id: 'price',  type: 'data', dataType: 'currency', label: 'Birim Fiyat', value: 45.50, detail: 'KDV haric' },
        // dataType: percent → Percent ikonu + "%18" formati
        { id: 'tax',    type: 'data', dataType: 'percent',  label: 'KDV',     value: 18,         detail: 'Vergi orani' },
        // dataType: text → manuel icon override
        { id: 'unit',   type: 'data', dataType: 'text',     label: 'Birim',   value: 'Kg', icon: 'Scale', color: 'cyan' },
        // dataType: boolean → CheckCircle/XCircle otomatik
        { id: 'sales',  type: 'data', dataType: 'boolean',  label: 'Satisa Uygun', value: true,  detail: 'Satis modulu icin aktif' },
        //
        // ┌─────────────────────────────────────────────────────────────────┐
        // │ SADECE YETKILI GORUR — VIEW_EXPIRY permission gerektirir        │
        // │ C# Controller bu widget'i yetki kontrolunden gecirir ve eger    │
        // │ kullanicinin 'VIEW_EXPIRY' izni yoksa JSON'a HIC eklenmez.      │
        // │ React'e ulastigi an — demek ki yetki zaten var, factory cizer.  │
        // │ Mock data'da permissionKey bilgi amacli, React ignore eder.     │
        // └─────────────────────────────────────────────────────────────────┘
        { id: 'expiry', type: 'data', dataType: 'date',     label: 'Son Kullanma', value: '2026-12-31',
          detail: 'Stok takip eden tarih', permissionKey: 'VIEW_EXPIRY', color: 'amber' },
        // Link widget degismedi
        { id: 'tree',   type: 'link', icon: 'TreePine',   label: 'Urun Agaci', url: '/Production/ProductTree?code=CLK-2MM-A', color: 'indigo' },
      ],
      primaryAction: { label: 'Duzenle', icon: 'Edit', url: '/Logistics/MaterialCardEdit?id=1' },
      secondaryAction: { label: 'Sil', icon: 'Trash2', url: '#', confirm: 'Bu malzeme silinecek, emin misiniz?' },
    },
    {
      id: 'mat-2',
      title: 'Aluminyum Profil 5mm',
      subtitle: 'ALM-5MM-B',
      description: '6063 T5 alasim, eloksal',
      statusBadge: { label: 'Aktif', color: 'emerald' },
      widgets: [
        { id: 'stock',  type: 'data', icon: 'Package',    label: 'Stok',    value: '420', detail: 'Adet | Min: 50', color: 'emerald' },
        { id: 'price',  type: 'data', icon: 'DollarSign', label: 'Fiyat',   value: '₺128,00', color: 'amber' },
        { id: 'unit',   type: 'data', icon: 'Scale',      label: 'Birim',   value: 'Metre', color: 'cyan' },
        { id: 'store',  type: 'data', icon: 'Warehouse',  label: 'Depo',    value: 'B-03', color: 'blue' },
        { id: 'tree',   type: 'link', icon: 'TreePine',   label: 'Urun Agaci', url: '/Production/ProductTree?code=ALM-5MM-B', color: 'indigo' },
      ],
      primaryAction: { label: 'Duzenle', icon: 'Edit', url: '/Logistics/MaterialCardEdit?id=2' },
      secondaryAction: { label: 'Sil', icon: 'Trash2', url: '#' },
    },
    {
      id: 'mat-3',
      title: 'Civata M8x40 Zn',
      subtitle: 'BLN-M8-ZN',
      description: 'DIN 933, tam dis, galvaniz',
      statusBadge: { label: 'Kritik', color: 'rose' },
      widgets: [
        { id: 'stock',  type: 'data', icon: 'Package',    label: 'Stok',    value: '85', detail: 'KRITIK | Min: 500', color: 'rose' },
        { id: 'price',  type: 'data', icon: 'DollarSign', label: 'Fiyat',   value: '₺1,20', color: 'amber' },
        { id: 'unit',   type: 'data', icon: 'Scale',      label: 'Birim',   value: 'Adet', color: 'cyan' },
        { id: 'tree',   type: 'link', icon: 'TreePine',   label: 'Urun Agaci', url: '/Production/ProductTree?code=BLN-M8-ZN', color: 'indigo' },
      ],
      primaryAction: { label: 'Duzenle', icon: 'Edit', url: '/Logistics/MaterialCardEdit?id=3' },
      secondaryAction: { label: 'Sil', icon: 'Trash2', url: '#' },
    },
  ],
}

/* ═════════════════════════════════════════════════════════
   MOCK DATASET 2 — CARI HESAPLAR
   ═════════════════════════════════════════════════════════ */
var accountBoardConfig = {
  boardKey: 'preview-accounts',
  title: 'Cari Hesaplar',
  subtitle: '3 cari',
  icon: 'Building2',
  iconColor: 'cyan',
  searchPlaceholder: 'Cari ara... (kod, unvan)',
  actions: [
    { id: 'new', label: 'Yeni Cari', icon: 'Users', variant: 'primary', url: '/Finance/AccountEdit' }
  ],
  entities: [
    {
      id: 'cri-1',
      title: 'Adamar Endustri A.S.',
      subtitle: 'CRI-00101',
      description: 'Istanbul / Kadikoy',
      statusBadge: { label: 'Guvenli', color: 'emerald' },
      widgets: [
        { id: 'balance', type: 'data', icon: 'DollarSign',    label: 'Bakiye',     value: '₺24.500', detail: 'Borc: ₺5.200 | Alacak: ₺29.700', color: 'amber' },
        { id: 'risk',    type: 'data', icon: 'AlertTriangle', label: 'Risk',       value: 'Dusuk', detail: 'Risk limiti: ₺100.000', color: 'emerald' },
        { id: 'phone',   type: 'data', icon: 'Phone',         label: 'Telefon',    value: '0216 555 **', color: 'cyan' },
        { id: 'type',    type: 'data', icon: 'Building2',     label: 'Tip',        value: 'Musteri', color: 'violet' },
        { id: 'extre',   type: 'link', icon: 'Receipt',       label: 'Ekstre Al',  url: '/Finance/AccountStatement?id=101', color: 'blue' },
        { id: 'history', type: 'link', icon: 'History',       label: 'Fiyat Gecmisi', url: '/Finance/PriceHistory?id=101', color: 'violet' },
      ],
      primaryAction: { label: 'Duzenle', icon: 'Edit', url: '/Finance/AccountEdit?id=101' },
      secondaryAction: { label: 'Sil', icon: 'Trash2', url: '#' },
    },
    {
      id: 'cri-2',
      title: 'Mavi Yildiz Tedarik Ltd.',
      subtitle: 'CRI-00102',
      description: 'Ankara / Cankaya',
      statusBadge: { label: 'Riskli', color: 'amber' },
      widgets: [
        { id: 'balance', type: 'data', icon: 'DollarSign',    label: 'Bakiye',     value: '-₺8.750', detail: 'Borc: ₺15.200 | Alacak: ₺6.450', color: 'rose' },
        { id: 'risk',    type: 'data', icon: 'AlertTriangle', label: 'Risk',       value: 'Yuksek', detail: 'Risk limiti: ₺50.000 | Kullanim: %78', color: 'amber' },
        { id: 'phone',   type: 'data', icon: 'Phone',         label: 'Telefon',    value: '0312 444 **', color: 'cyan' },
        { id: 'type',    type: 'data', icon: 'Building2',     label: 'Tip',        value: 'Tedarikci', color: 'violet' },
        { id: 'extre',   type: 'link', icon: 'Receipt',       label: 'Ekstre Al',  url: '/Finance/AccountStatement?id=102', color: 'blue' },
      ],
      primaryAction: { label: 'Duzenle', icon: 'Edit', url: '/Finance/AccountEdit?id=102' },
      secondaryAction: { label: 'Sil', icon: 'Trash2', url: '#' },
    },
    {
      id: 'cri-3',
      title: 'Deniz Metal San. Tic.',
      subtitle: 'CRI-00103',
      description: 'Izmir / Karsiyaka',
      statusBadge: { label: 'Pasif', color: 'slate' },
      widgets: [
        { id: 'balance', type: 'data', icon: 'DollarSign',    label: 'Bakiye',     value: '₺0,00', color: 'slate' },
        { id: 'risk',    type: 'data', icon: 'AlertTriangle', label: 'Risk',       value: 'Normal', color: 'emerald' },
        { id: 'type',    type: 'data', icon: 'Building2',     label: 'Tip',        value: 'Musteri', color: 'violet' },
        { id: 'extre',   type: 'link', icon: 'Receipt',       label: 'Ekstre Al',  url: '/Finance/AccountStatement?id=103', color: 'blue' },
      ],
      primaryAction: { label: 'Duzenle', icon: 'Edit', url: '/Finance/AccountEdit?id=103' },
    },
  ],
}

/* ═════════════════════════════════════════════════════════
   MOCK DATASET 3 — SATIS TEKLIFLERI
   ═════════════════════════════════════════════════════════ */
var quoteBoardConfig = {
  boardKey: 'preview-sales-quotes',
  title: 'Satis Teklifleri',
  subtitle: '3 teklif',
  icon: 'FileText',
  iconColor: 'violet',
  searchPlaceholder: 'Teklif ara... (no, musteri)',
  actions: [
    { id: 'new', label: 'Yeni Teklif', icon: 'FileText', variant: 'primary', url: '/Sales/SalesQuoteEdit' }
  ],
  entities: [
    {
      id: 'tkl-1',
      title: 'Teklif #TKL2026000042',
      subtitle: 'Adamar Endustri A.S.',
      description: 'Celik konstruksiyon malzemeleri',
      statusBadge: { label: 'Onaylandi', color: 'emerald' },
      widgets: [
        { id: 'amount',  type: 'data', icon: 'DollarSign', label: 'Tutar',      value: '₺12.450', detail: 'KDV dahil', color: 'amber' },
        { id: 'valid',   type: 'data', icon: 'Calendar',   label: 'Gecerlilik', value: '30.04.2026', detail: '15 gun kaldi', color: 'cyan' },
        { id: 'lines',   type: 'data', icon: 'Layers',     label: 'Kalem',      value: '8', color: 'violet' },
        { id: 'rep',     type: 'data', icon: 'User',       label: 'Temsilci',   value: 'Ahmet Y.', color: 'blue' },
        { id: 'order',   type: 'link', icon: 'FileCheck',  label: 'Siparise Cevir', url: '/Sales/ConvertToOrder?quoteId=1', color: 'emerald' },
        { id: 'print',   type: 'link', icon: 'Printer',    label: 'Yazdir',     url: '/Sales/PrintQuote?id=1', color: 'slate' },
      ],
      primaryAction: { label: 'Duzenle', icon: 'Edit', url: '/Sales/SalesQuoteEdit?id=1' },
      secondaryAction: { label: 'Sil', icon: 'Trash2', url: '#' },
    },
    {
      id: 'tkl-2',
      title: 'Teklif #TKL2026000043',
      subtitle: 'Mavi Yildiz Tedarik',
      description: 'Aluminyum profil siparisi',
      statusBadge: { label: 'Beklemede', color: 'amber' },
      widgets: [
        { id: 'amount',  type: 'data', icon: 'DollarSign', label: 'Tutar',      value: '₺8.200', color: 'amber' },
        { id: 'valid',   type: 'data', icon: 'Calendar',   label: 'Gecerlilik', value: '25.04.2026', detail: '10 gun kaldi', color: 'cyan' },
        { id: 'lines',   type: 'data', icon: 'Layers',     label: 'Kalem',      value: '3', color: 'violet' },
        { id: 'rep',     type: 'data', icon: 'User',       label: 'Temsilci',   value: 'Mehmet K.', color: 'blue' },
        { id: 'order',   type: 'link', icon: 'FileCheck',  label: 'Siparise Cevir', url: '/Sales/ConvertToOrder?quoteId=2', color: 'emerald' },
      ],
      primaryAction: { label: 'Duzenle', icon: 'Edit', url: '/Sales/SalesQuoteEdit?id=2' },
      secondaryAction: { label: 'Sil', icon: 'Trash2', url: '#' },
    },
    {
      id: 'tkl-3',
      title: 'Teklif #TKL2026000044',
      subtitle: 'Deniz Metal San.',
      description: 'Civata ve baglanti elemanlari',
      statusBadge: { label: 'Reddedildi', color: 'rose' },
      widgets: [
        { id: 'amount',  type: 'data', icon: 'DollarSign', label: 'Tutar',      value: '₺1.850', color: 'amber' },
        { id: 'valid',   type: 'data', icon: 'Calendar',   label: 'Gecerlilik', value: '05.04.2026', detail: 'Suresi doldu', color: 'rose' },
        { id: 'lines',   type: 'data', icon: 'Layers',     label: 'Kalem',      value: '12', color: 'violet' },
        { id: 'rep',     type: 'data', icon: 'User',       label: 'Temsilci',   value: 'Ayse D.', color: 'blue' },
      ],
      primaryAction: { label: 'Duzenle', icon: 'Edit', url: '/Sales/SalesQuoteEdit?id=3' },
    },
  ],
}

/* ═════════════════════════════════════════════════════════
   APP (Standalone preview with 3 boards stacked vertically)
   ═════════════════════════════════════════════════════════ */
export default function App() {
  // Light/dark toggle switch for preview — default dark
  // (Gercek projede tema Razor layout'tan gelir)
  if (typeof document !== 'undefined') {
    document.documentElement.classList.add('dark')
  }

  return (
    <div className="min-h-screen bg-[#0c0f1a]">
      {/* Board 1: Malzeme */}
      <div className="border-b border-white/[0.06]">
        <div style={{ height: '60vh' }}>
          <SmartBoard {...materialBoardConfig} />
        </div>
      </div>

      {/* Board 2: Cari Hesaplar */}
      <div className="border-b border-white/[0.06]">
        <div style={{ height: '60vh' }}>
          <SmartBoard {...accountBoardConfig} />
        </div>
      </div>

      {/* Board 3: Satis Teklifleri */}
      <div>
        <div style={{ height: '60vh' }}>
          <SmartBoard {...quoteBoardConfig} />
        </div>
      </div>
    </div>
  )
}
