import React from 'react'
import {
  Type, Database, Image, Square, DollarSign, Hash, Clock,
  GripVertical, MailOpen, CalendarDays, UserCircle, FileSignature, MessageSquare, Building2, Sparkles,
  Sigma, Table2,
} from 'lucide-react'

// Temel element tipleri — palette ust kismi
const KINDS = [
  { kind: 'Label',         label: 'Etiket',          Icon: Type },
  { kind: 'BoundField',    label: 'Veri Alanı',      Icon: Database },
  { kind: 'Image',         label: 'Resim',           Icon: Image },
  { kind: 'Shape',         label: 'Şekil',           Icon: Square },
  { kind: 'AmountInWords', label: 'Yazı ile Tutar',  Icon: DollarSign },
  { kind: 'Aggregate',     label: 'Alt Toplam',      Icon: Sigma },
  { kind: 'Table',         label: 'Tablo',           Icon: Table2 },
  { kind: 'PageNumber',    label: 'Sayfa No',        Icon: Hash },
  { kind: 'DateTimeNow',   label: 'Tarih / Saat',   Icon: Clock },
]

// Hazir snippet'ler — drop edilince Label element olusur, text+style override edilir.
// Mail tasarimi icin: tarih, selamlama, kapanis vb. yaygin patternlar tek tikla canvas'a.
// "text" alani mail token'larini (Compose ekranindaki gibi {{personName}}) icerebilir;
// gondrim aninda renderer dolduracak.
const SNIPPETS = [
  {
    key: 'mailGreeting',
    label: 'Selamlama (Sayın)',
    Icon: MailOpen,
    text: 'Sayın {{personName}},',
    style: { fontSize: 11, bold: true },
    w: 90, h: 8,
  },
  {
    key: 'mailGreetingCompany',
    label: 'Kurumsal Selamlama',
    Icon: Building2,
    text: 'Sayın {{contactName}} Yetkilisi,',
    style: { fontSize: 11, bold: true },
    w: 110, h: 8,
  },
  {
    key: 'dateLine',
    label: 'Tarih Satırı',
    Icon: CalendarDays,
    text: 'Tarih: {{currentDate}}',
    style: { fontSize: 10, align: 'right' },
    w: 60, h: 7,
  },
  {
    key: 'recipientLine',
    label: 'Kişi Bilgisi',
    Icon: UserCircle,
    text: '{{personName}} ({{personTitle}})',
    style: { fontSize: 10 },
    w: 80, h: 7,
  },
  {
    key: 'closingShort',
    label: 'Kapanış (Saygılarımla)',
    Icon: FileSignature,
    text: 'Saygılarımla,',
    style: { fontSize: 11 },
    w: 50, h: 7,
  },
  {
    key: 'closingFriendly',
    label: 'Kapanış (İyi çalışmalar)',
    Icon: MessageSquare,
    text: 'İyi çalışmalar dilerim.',
    style: { fontSize: 11 },
    w: 70, h: 7,
  },
]

function PaletteRow({ children, onDragStart, title }) {
  return (
    <div
      draggable
      onDragStart={onDragStart}
      title={title}
      style={{
        display: 'flex', alignItems: 'center', gap: 8,
        height: 32, padding: '0 8px', borderRadius: 6,
        cursor: 'grab', userSelect: 'none',
        transition: 'background 0.12s',
      }}
      onMouseEnter={e => e.currentTarget.style.background = 'rgba(99,102,241,0.08)'}
      onMouseLeave={e => e.currentTarget.style.background = 'transparent'}
      onMouseDown={e => e.currentTarget.style.cursor = 'grabbing'}
      onMouseUp={e => e.currentTarget.style.cursor = 'grab'}
    >
      {children}
    </div>
  )
}

function SectionTitle({ Icon, label }) {
  return (
    <div style={{
      display: 'flex', alignItems: 'center', gap: 6,
      padding: '8px 8px 4px',
      fontSize: 10, fontWeight: 700, textTransform: 'uppercase', letterSpacing: 0.4,
      color: '#6b7280',
    }}>
      {Icon && <Icon size={11} color="#6366f1" />}
      <span>{label}</span>
    </div>
  )
}

export default function ElementPalette() {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      <SectionTitle label="Temel Elementler" />
      {KINDS.map(({ kind, label, Icon }) => (
        <PaletteRow
          key={kind}
          title={label}
          onDragStart={e => e.dataTransfer.setData('element-kind', kind)}
        >
          <GripVertical size={12} color="#d1d5db" style={{ flexShrink: 0 }} />
          <Icon size={14} color="#6366f1" style={{ flexShrink: 0 }} />
          <span style={{ fontSize: 12, color: '#374151', flex: 1 }}>{label}</span>
        </PaletteRow>
      ))}

      <SectionTitle Icon={Sparkles} label="Hazır Şablonlar" />
      {SNIPPETS.map(({ key, label, Icon, text, style, w, h }) => (
        <PaletteRow
          key={key}
          title={text}
          onDragStart={e => {
            // Kind=Label, snippet payload'i ayri bir data tipi olarak. BandContainer drop
            // handler'i "element-snippet" varsa text+style+w+h'i override eder.
            e.dataTransfer.setData('element-kind', 'Label')
            e.dataTransfer.setData('element-snippet', JSON.stringify({ text, style: style ?? null, w: w ?? null, h: h ?? null }))
          }}
        >
          <GripVertical size={12} color="#d1d5db" style={{ flexShrink: 0 }} />
          <Icon size={13} color="#6366f1" style={{ flexShrink: 0 }} />
          <div style={{ fontSize: 12, color: '#374151', flex: 1, minWidth: 0 }}>
            <div>{label}</div>
            <div style={{
              fontSize: 9.5, color: '#9ca3af', marginTop: 1,
              whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis',
            }}>
              {text}
            </div>
          </div>
        </PaletteRow>
      ))}
    </div>
  )
}
