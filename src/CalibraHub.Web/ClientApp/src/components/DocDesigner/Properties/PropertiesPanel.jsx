import React from 'react'

export default function PropertiesPanel({ state, dispatch }) {
  const { bands, selectedElementId, selectedBandId, meta } = state

  const selectedBand = bands.find(b => b.id === selectedBandId)
  const selectedElement = selectedBand?.elements.find(e => e.id === selectedElementId)
    ?? bands.flatMap(b => b.elements).find(e => e.id === selectedElementId)

  if (!selectedElementId && !selectedBandId) {
    return (
      <PanelWrap>
        <PageProps meta={meta} dispatch={dispatch} />
      </PanelWrap>
    )
  }

  if (selectedElementId && selectedElement) {
    return (
      <PanelWrap>
        <ElementProps el={selectedElement} bandId={selectedBandId} dispatch={dispatch} />
      </PanelWrap>
    )
  }

  if (selectedBandId && selectedBand) {
    return (
      <PanelWrap>
        <BandProps band={selectedBand} dispatch={dispatch} />
      </PanelWrap>
    )
  }

  return <PanelWrap><div style={{ color: '#aaa', fontSize: 12 }}>Seçim yok</div></PanelWrap>
}

function PanelWrap({ children }) {
  return (
    <div style={{
      width: 240, flexShrink: 0, borderLeft: '1px solid var(--app-border, #e5e7eb)',
      background: 'var(--app-surface, #fff)', overflow: 'auto', padding: 12, fontSize: 12
    }}>
      {children}
    </div>
  )
}

// ── Sayfa özellikleri ─────────────────────────────────────────────────────────
function PageProps({ meta, dispatch }) {
  const set = (patch) => dispatch({ type: 'SET_META', payload: patch })
  return (
    <Section title="Sayfa Özellikleri">
      <Field label="Şablon Kodu">
        <input value={meta.code} onChange={e => set({ code: e.target.value })} style={inputStyle} />
      </Field>
      <Field label="Şablon Adı">
        <input value={meta.name} onChange={e => set({ name: e.target.value })} style={inputStyle} />
      </Field>
      <Field label="Belge Tipi">
        <select value={meta.docType} onChange={e => set({ docType: e.target.value })} style={inputStyle}>
          {['sales_quote','sales_order','purchase_order','delivery_note','invoice','expense_note','custom'].map(t => (
            <option key={t} value={t}>{t}</option>
          ))}
        </select>
      </Field>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 6 }}>
        <Field label="Genişlik (mm)">
          <input type="number" value={meta.pageW} onChange={e => set({ pageW: +e.target.value })} style={inputStyle} />
        </Field>
        <Field label="Yükseklik (mm)">
          <input type="number" value={meta.pageH} onChange={e => set({ pageH: +e.target.value })} style={inputStyle} />
        </Field>
        <Field label="Üst Kenar">
          <input type="number" value={meta.marginTop} onChange={e => set({ marginTop: +e.target.value })} style={inputStyle} />
        </Field>
        <Field label="Alt Kenar">
          <input type="number" value={meta.marginBot} onChange={e => set({ marginBot: +e.target.value })} style={inputStyle} />
        </Field>
        <Field label="Sol Kenar">
          <input type="number" value={meta.marginLeft} onChange={e => set({ marginLeft: +e.target.value })} style={inputStyle} />
        </Field>
        <Field label="Sağ Kenar">
          <input type="number" value={meta.marginRight} onChange={e => set({ marginRight: +e.target.value })} style={inputStyle} />
        </Field>
      </div>
    </Section>
  )
}

// ── Bant özellikleri ──────────────────────────────────────────────────────────
function BandProps({ band, dispatch }) {
  const set = (patch) => dispatch({ type: 'UPDATE_BAND', bandId: band.id, patch })
  return (
    <Section title="Bant Özellikleri">
      <Field label="Yükseklik (mm)">
        <input type="number" value={band.height} step="0.5"
          onChange={e => dispatch({ type: 'RESIZE_BAND', bandId: band.id, height: +e.target.value })}
          style={inputStyle} />
      </Field>
      <Field label="Veri Alias">
        <input value={band.dataAlias ?? ''} onChange={e => set({ dataAlias: e.target.value || null })} style={inputStyle} />
      </Field>
      <label style={{ display: 'flex', gap: 6, alignItems: 'center', marginTop: 6, cursor: 'pointer' }}>
        <input type="checkbox" checked={band.repeatOnEveryPage}
          onChange={e => set({ repeatOnEveryPage: e.target.checked })} />
        Her sayfada tekrarla
      </label>
      <label style={{ display: 'flex', gap: 6, alignItems: 'center', marginTop: 4, cursor: 'pointer' }}>
        <input type="checkbox" checked={band.canGrow}
          onChange={e => set({ canGrow: e.target.checked })} />
        İçeriğe göre büyüyebilir
      </label>
    </Section>
  )
}

// ── Element özellikleri ───────────────────────────────────────────────────────
function ElementProps({ el, bandId, dispatch }) {
  const set = (patch) => dispatch({ type: 'UPDATE_ELEMENT', elementId: el.id, patch })
  const setStyle = (stylePatch) => set({ style: { ...el.style, ...stylePatch } })
  const setBinding = (bindingPatch) => set({ binding: { ...(el.binding ?? { alias: '', col: '' }), ...bindingPatch } })

  return (
    <>
      <Section title="Konum & Boyut">
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 6 }}>
          {['x','y','w','h'].map(k => (
            <Field key={k} label={k.toUpperCase() + ' (mm)'}>
              <input type="number" step="0.5" value={(el[k] ?? 0).toFixed(1)}
                onChange={e => set({ [k]: +e.target.value })} style={inputStyle} />
            </Field>
          ))}
        </div>
      </Section>

      {(el.kind === 'Label') && (
        <Section title="Metin">
          <Field label="İçerik">
            <textarea value={el.text ?? ''} rows={2}
              onChange={e => set({ text: e.target.value })}
              style={{ ...inputStyle, resize: 'vertical' }} />
          </Field>
        </Section>
      )}

      {el.kind === 'BoundField' && (
        <Section title="Veri Bağlantısı">
          <Field label="Alias">
            <input value={el.binding?.alias ?? ''} onChange={e => setBinding({ alias: e.target.value })} style={inputStyle} />
          </Field>
          <Field label="Kolon">
            <input value={el.binding?.col ?? ''} onChange={e => setBinding({ col: e.target.value })} style={inputStyle} />
          </Field>
          <Field label="Format">
            <input value={el.format ?? ''} placeholder="#,##0.00 veya dd.MM.yyyy"
              onChange={e => set({ format: e.target.value || null })} style={inputStyle} />
          </Field>
        </Section>
      )}

      <Section title="Metin Stili">
        <Field label="Font Boyutu">
          <input type="number" value={el.style?.fontSize ?? 10} min={6} max={72}
            onChange={e => setStyle({ fontSize: +e.target.value })} style={inputStyle} />
        </Field>
        <div style={{ display: 'flex', gap: 6, marginBottom: 6 }}>
          {[['bold','K'],['italic','İ'],['underline','A']].map(([k, lbl]) => (
            <button key={k}
              onClick={() => setStyle({ [k]: !el.style?.[k] })}
              style={{
                flex: 1, border: '1px solid #e0e0e0', borderRadius: 4, padding: '4px 0',
                background: el.style?.[k] ? '#ede9fe' : '#fff', cursor: 'pointer',
                fontWeight: k === 'bold' ? 'bold' : 'normal',
                fontStyle: k === 'italic' ? 'italic' : 'normal',
                textDecoration: k === 'underline' ? 'underline' : 'none'
              }}
            >{lbl}</button>
          ))}
        </div>
        <Field label="Hizalama">
          <select value={el.style?.align ?? 'left'} onChange={e => setStyle({ align: e.target.value })} style={inputStyle}>
            {['left','center','right','justify'].map(a => <option key={a} value={a}>{a}</option>)}
          </select>
        </Field>
        <Field label="Yazı Rengi">
          <input type="color" value={el.style?.color ?? '#000000'}
            onChange={e => setStyle({ color: e.target.value })} style={{ ...inputStyle, height: 30, padding: 2 }} />
        </Field>
        <Field label="Arkaplan">
          <input type="color" value={el.style?.bgColor === 'transparent' ? '#ffffff' : (el.style?.bgColor ?? '#ffffff')}
            onChange={e => setStyle({ bgColor: e.target.value })} style={{ ...inputStyle, height: 30, padding: 2 }} />
        </Field>
        <label style={{ display: 'flex', gap: 6, alignItems: 'center', marginTop: 4, cursor: 'pointer' }}>
          <input type="checkbox" checked={el.style?.border ?? false} onChange={e => setStyle({ border: e.target.checked })} />
          Kenarlık
        </label>
      </Section>

      <Section title="Z-Index">
        <Field label="Katman Sırası">
          <input type="number" value={el.zIndex ?? 0}
            onChange={e => set({ zIndex: +e.target.value })} style={inputStyle} />
        </Field>
        <div style={{ fontSize: 10, color: '#aaa' }}>Büyük = üstte. Çakışan elementlerde katman sırası.</div>
      </Section>

      <div style={{ marginTop: 12 }}>
        <button
          onClick={() => dispatch({ type: 'DELETE_ELEMENT', elementId: el.id })}
          style={{
            width: '100%', border: 'none', borderRadius: 6, padding: '7px 0',
            background: '#fee2e2', color: '#dc2626', cursor: 'pointer', fontSize: 12
          }}
        >Element Sil</button>
      </div>
    </>
  )
}

// ── Yardımcılar ───────────────────────────────────────────────────────────────
function Section({ title, children }) {
  return (
    <div style={{ marginBottom: 14 }}>
      <div style={{ fontSize: 10, fontWeight: 700, textTransform: 'uppercase', color: '#888', letterSpacing: 0.5, marginBottom: 6 }}>
        {title}
      </div>
      {children}
    </div>
  )
}

function Field({ label, children }) {
  return (
    <div style={{ marginBottom: 6 }}>
      <div style={{ fontSize: 10, color: '#888', marginBottom: 2 }}>{label}</div>
      {children}
    </div>
  )
}

const inputStyle = {
  width: '100%', border: '1px solid #e0e0e0', borderRadius: 4, padding: '4px 6px',
  fontSize: 12, color: '#333', background: '#fafafa', boxSizing: 'border-box'
}
