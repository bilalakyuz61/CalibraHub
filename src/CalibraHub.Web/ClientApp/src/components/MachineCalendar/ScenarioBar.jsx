import { Plus, Pencil, Trash2, Star } from 'lucide-react'

/**
 * ScenarioBar — senaryo sekme şeridi + yönetim aksiyonları (Yeni / Yeniden Adlandır /
 * Varsayılan Yap / Sil). Seçili senaryo indigo dolgu ile vurgulanır, varsayılan senaryo
 * yıldız ikonuyla işaretlenir.
 */
export default function ScenarioBar({
  scenarios, selectedScenarioId, onSelect, onCreate, onRename, onDelete, onSetDefault, busy,
}) {
  var selected = scenarios.find(function (s) { return s.id === selectedScenarioId })

  return (
    <div className="mcal-scenario-bar">
      <div className="mcal-scenario-tabs">
        {scenarios.map(function (s) {
          var active = s.id === selectedScenarioId
          return (
            <button
              key={s.id}
              type="button"
              className={'mcal-scenario-tab' + (active ? ' mcal-scenario-tab--active' : '')}
              title={s.description || s.name}
              onClick={function () { onSelect(s.id) }}
            >
              {s.isDefault && <Star size={11} className="mcal-scenario-tab__star" />}
              {s.name}
            </button>
          )
        })}
        <button type="button" className="mcal-icon-btn" title="Yeni Senaryo" disabled={busy} onClick={onCreate}>
          <Plus size={14} />
        </button>
      </div>

      {selected && (
        <div className="mcal-scenario-actions">
          <button
            type="button"
            className="mcal-link-btn"
            disabled={busy}
            onClick={function () { onRename(selected) }}
          >
            <Pencil size={12} /> Yeniden Adlandır
          </button>
          {!selected.isDefault && (
            <button
              type="button"
              className="mcal-link-btn"
              disabled={busy}
              onClick={function () { onSetDefault(selected) }}
            >
              <Star size={12} /> Varsayılan Yap
            </button>
          )}
          <button
            type="button"
            className="mcal-link-btn mcal-link-btn--danger"
            disabled={busy}
            onClick={function () { onDelete(selected) }}
          >
            <Trash2 size={12} /> Sil
          </button>
        </div>
      )}
    </div>
  )
}
