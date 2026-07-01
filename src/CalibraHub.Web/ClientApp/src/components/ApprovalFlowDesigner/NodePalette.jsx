/**
 * ApprovalFlowDesigner — sol palet.
 *
 * Her node tipi için draggable bir kart. Kullanıcı kartı canvas'a sürükleyince
 * canvas onDrop'unda yeni node yaratılır. Drag event'inin dataTransfer'ına
 * `application/reactflow` tipiyle node tip ismi yazılır.
 */
import React from 'react'
import { Play, Square, GitBranch, CheckSquare, Bell, GitMerge, Zap, Variable, Clock, Users, Layers, Globe } from 'lucide-react'

var PALETTE = [
  { type: 'start',        label: 'Başla',         icon: Play,        kind: 'start' },
  { type: 'step',         label: 'Adım',          icon: CheckSquare, kind: 'step' },
  { type: 'decision',     label: 'Karar',         icon: GitBranch,   kind: 'decision' },
  { type: 'parallel',     label: 'Paralel',       icon: GitMerge,    kind: 'parallel' },
  { type: 'notification', label: 'Bildirim',      icon: Bell,        kind: 'notification' },
  { type: 'integration',  label: 'Entegrasyon',   icon: Zap,         kind: 'integration' },
  { type: 'setVariable',  label: 'Değişken Ata',  icon: Variable,    kind: 'setvar' },
  { type: 'timer',        label: 'Bekleme',       icon: Clock,       kind: 'timer' },
  { type: 'vote',         label: 'Oylama',        icon: Users,       kind: 'vote' },
  { type: 'subprocess',   label: 'Alt Süreç',     icon: Layers,      kind: 'subprocess' },
  { type: 'webhook',      label: 'Webhook',       icon: Globe,       kind: 'webhook' },
  { type: 'end',          label: 'Bitir',         icon: Square,      kind: 'end' },
]

export default function NodePalette() {
  function onDragStart(ev, nodeType) {
    ev.dataTransfer.setData('application/reactflow', nodeType)
    ev.dataTransfer.effectAllowed = 'move'
  }
  return (
    <div className="afd-palette">
      <div className="afd-palette__title">Düğüm Paleti</div>
      <div className="afd-palette__hint">Sürükle &amp; tuvale bırak</div>
      {PALETTE.map(function (p) {
        var Icon = p.icon
        return (
          <div
            key={p.type}
            className={'afd-palette__item afd-palette__item--' + p.kind}
            draggable
            onDragStart={function (e) { onDragStart(e, p.type) }}
          >
            <Icon size={14} strokeWidth={2.4} />
            <span>{p.label}</span>
          </div>
        )
      })}
    </div>
  )
}
