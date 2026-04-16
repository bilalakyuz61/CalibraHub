/**
 * GroupModal — Yeni grup olusturma modal'i.
 *
 * Eskiden GroupSelector.jsx icindeki inline form idi; simdi ayri bir modal'a
 * tasindi. AdminMiniModal base component'ini kullanir.
 *
 * Props:
 *   isOpen     bool                 — parent acik/kapali state'i
 *   onClose    fn                   — iptal / backdrop / ESC
 *   onCreated  fn(payload, cb?)     — kaydet callback'i:
 *                                     payload = { groupKey, groupLabel }
 *                                     cb(createdGroup) — parent backend save
 *                                     sonrasi modal'i kapatir ve basarili group
 *                                     id'sini donebilir
 *   saving     bool                 — butonu disable etmek icin
 */
import { useState, useEffect } from 'react'
import { Layers, Plus } from 'lucide-react'
import AdminMiniModal from './AdminMiniModal'

// Turkce karakter normalize + snake_case (GroupSelector'daki helper'in kopyasi)
function slugifyLabel(label) {
  return (label || '')
    .toLowerCase()
    .replace(/ı/g, 'i').replace(/ğ/g, 'g').replace(/ü/g, 'u')
    .replace(/ş/g, 's').replace(/ö/g, 'o').replace(/ç/g, 'c')
    .replace(/\s+/g, '_')
    .replace(/[^a-z0-9_]/g, '')
    .replace(/^[^a-z]+/, '')
}

export default function GroupModal(props) {
  var isOpen    = props.isOpen
  var onClose   = props.onClose
  var onCreated = props.onCreated
  var saving    = !!props.saving

  var [label, setLabel] = useState('')
  var [key, setKey]     = useState('')
  var [error, setError] = useState(null)

  // Modal her acildiginda state'i sifirla
  useEffect(function () {
    if (isOpen) {
      setLabel('')
      setKey('')
      setError(null)
    }
  }, [isOpen])

  function handleLabelChange(e) {
    var v = e.target.value
    setLabel(v)
    setKey(slugifyLabel(v))
    if (error) setError(null)
  }

  function validate() {
    var lbl = label.trim()
    if (!lbl) return 'Grup adı zorunlu'
    return null
  }

  function handleCreate() {
    var e = validate()
    if (e) { setError(e); return }
    if (onCreated) {
      onCreated({
        groupKey: key.trim(),
        groupLabel: label.trim(),
      })
    }
  }

  // Input stil helper'lari (WidgetBuilderForm ile ayni)
  var inputBase = 'w-full h-10 px-3 rounded-lg text-sm transition-all ' +
    'bg-white/60 dark:bg-white/[0.04] ' +
    'text-slate-800 dark:text-white/90 ' +
    'placeholder:text-slate-400 dark:placeholder:text-white/25 ' +
    'focus:outline-none '
  var inputOk = 'border border-slate-200 dark:border-white/[0.08] focus:border-indigo-400/60 dark:focus:border-white/20 focus:shadow-[0_0_0_3px_rgba(99,102,241,0.12)]'
  var inputErr = 'border border-red-400/60 focus:border-red-400/80 focus:shadow-[0_0_0_3px_rgba(239,68,68,0.15)]'
  var labelCls = 'block text-[10px] font-bold uppercase tracking-wider text-slate-500 dark:text-white/40 mb-1.5'

  var footer = (
    <>
      <div className="flex-1" />
      <button
        type="button"
        onClick={onClose}
        disabled={saving}
        className="px-4 py-2 rounded-xl bg-white/[0.04] hover:bg-white/[0.08] border border-slate-200 dark:border-white/[0.08] text-xs font-medium text-slate-600 dark:text-white/60 hover:text-slate-900 dark:hover:text-white/85 transition-all disabled:opacity-50"
      >
        İptal
      </button>
      <button
        type="button"
        onClick={handleCreate}
        disabled={saving}
        className="flex items-center gap-1.5 px-4 py-2 rounded-xl bg-indigo-500 hover:bg-indigo-600 dark:bg-indigo-500/25 dark:hover:bg-indigo-500/35 border border-indigo-500 dark:border-indigo-400/30 text-xs font-semibold text-white dark:text-indigo-200 transition-all disabled:opacity-50 shadow-sm"
      >
        <Plus size={13} strokeWidth={2.4} />
        {saving ? 'Kaydediliyor...' : 'Grubu Ekle'}
      </button>
    </>
  )

  return (
    <AdminMiniModal
      isOpen={isOpen}
      onClose={onClose}
      title="Yeni Grup Tanımla"
      subtitle="Widget'ları grupladığın katlanır başlık"
      icon={Layers}
      iconColor="indigo"
      maxWidth="max-w-md"
      footer={footer}
    >
      <div className="flex flex-col gap-4">
        <div>
          <label className={labelCls}>Grup Adı</label>
          <input
            type="text"
            value={label}
            onChange={handleLabelChange}
            placeholder="örneğin: Stok Bilgileri"
            autoFocus
            className={inputBase + (error ? inputErr : inputOk)}
            onKeyDown={function (e) { if (e.key === 'Enter') handleCreate() }}
          />
        </div>
        {error && (
          <p className="text-[11px] text-red-600 dark:text-red-400/90 bg-red-500/5 border border-red-400/20 rounded-lg px-3 py-2">
            {error}
          </p>
        )}
      </div>
    </AdminMiniModal>
  )
}
